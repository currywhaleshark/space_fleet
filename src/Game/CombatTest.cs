using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

public partial class ScaleTest
{
    private BallisticsView _ballistics = null!;
    private ShipBody? _practiceTarget;
    private int _liveShots;
    public bool FireAssist { get; private set; } = true;
    public FiringSolution? FiringSolution { get; private set; }
    public bool CorrectingAim { get; private set; }
    public string LastFireMessage { get; private set; } = "";
    public bool LastFireFailed { get; private set; }
    /// <summary>사격보조가 노리는 부위(Y로 바꾼다).</summary>
    public AimSubsystem AimPart { get; private set; } = AimSubsystem.Center;
    /// <summary>지금 고른 부위의 모듈(표적·내 위치 기준). 중심이거나 남은 모듈이 없으면 null.</summary>
    public ModuleState? AimModule => FireTarget is null ? null : Scheme == ControlScheme.Helm ? Gunnery?.EngagedModule : FireTarget is ShipView t && Controlled?.Body is ShipBody me
        ? Subsystems.Pick(t.Body, AimPart, me.Position) : null;

    private void CycleAimPart()
    {
        int i = Array.IndexOf(Subsystems.Cycle, AimPart);
        AimPart = Subsystems.Cycle[(i + 1) % Subsystems.Cycle.Length];
        Notify($"조준 부위: {Subsystems.Label(AimPart)}", failed: false);
    }

    /// <summary>사격통제 표적. 검사 표적이 살아 있는 적일 때만 잡는다(아군에게 선행 보정을 계산하지 않는다).</summary>
    public ShipView? FireTarget
    {
        get
        {
            ShipView? view = !Camera.TelescopeHeld && Scheme == ControlScheme.Helm && Gunnery?.Engaged is { } engaged
                ? Views.Find(v => v.Body == engaged) : InspectTarget;
            return view is not null && Controlled is { } me && view.Body.Faction != me.Body.Faction
                && TrackOf(view).Level > TrackLevel.None && !view.Body.Damage.Destroyed ? view : null;
        }
    }
    public double LastFireTime { get; private set; } = -100;

    private void SetupPractice(string callsign = "DD-X1", float distance = 6000, float speed = 180)
    {
        if (Controlled is null) return;
        var target = Views.Find(v => v.Body.Callsign == callsign && v != Controlled)
            ?? throw new ArgumentException($"Unknown practice target: {callsign}");
        ShipBody player = Controlled.Body;
        _practiceTarget = target.Body;
        SuspendBrain(target.Body);
        // 기본은 표적이 나를 마주 본다. --stern이면 꼬리를 보인다(후미 침투 검증).
        Quaternion facing = _shot?.Stern == true ? player.Orientation : player.Orientation * new Quaternion(Vector3.Up, Mathf.Pi);
        target.Body.Place(player.Position + Vec3d.From(player.Forward) * distance, facing.Normalized());
        var previewArgs = BattleArgs.Parse(OS.GetCmdlineUserArgs());
        if (previewArgs.ContainsKey("test-bearing") || previewArgs.ContainsKey("test-elevation"))
        {
            float Angle(string key) => previewArgs.TryGetValue(key,out string? value)
                ? Mathf.DegToRad(float.Parse(value,System.Globalization.CultureInfo.InvariantCulture)) : 0;
            float angle=Angle("test-bearing"), elevation=Angle("test-elevation");
            var direction = player.Orientation * new Vector3(Mathf.Sin(angle)*Mathf.Cos(elevation), Mathf.Sin(elevation), -Mathf.Cos(angle)*Mathf.Cos(elevation));
            target.Body.Place(player.Position + Vec3d.From(direction) * distance, facing.Normalized());
        }
        if (_shot is { Below: > 0 })
            target.Body.Place(player.Position - Vec3d.From(player.Up) * (_shot.Below * 1000), facing.Normalized());
        target.Body.Velocity = player.Orientation * Vector3.Right * speed;
        target.Body.Damage.Reset();
        // 연습 표적은 ECM을 끄고 바로 잠글 수 있게 한다(--keep-ecm이면 기본 배분 유지).
        target.Body.Power.Reset();
        if (_shot?.KeepEcm == true)
            ApplyDefaultPips(target.Body);
        World.ResetWeapons();
        World.Sensors.Update(World.Ships, World.Time, force: true);
        InspectTarget = target;
        Camera.ResetAim(player.Orientation);
        _liveShots = 0;
        LastFireMessage = $"이동 표적 연습 · {speed:0} m/s";
        LastFireTime = World.Time;
    }

    private ShipBody? _drillShooter;
    private int _drillLaunched;
    private int _autoMissiles;
    public const int DrillSalvo = 6;

    /// <summary>미사일 선택 후 좌클릭: 검사 표적(탐지된 적)에 한 발. 접촉 이상이면 쏠 수 있다.</summary>
    private void LaunchMissileAtTarget()
    {
        if (Controlled?.Body is not ShipBody player) return;
        if (InspectTarget is not ShipView target || target.Body.Faction == player.Faction)
        {
            Notify("미사일 표적 없음 (R로 적 선택)", failed: true);
            return;
        }
        FireAttempt attempt = World.LaunchMissile(player, target.Body);
        Notify(attempt.Reason, !attempt.Fired);
    }

    private void LaunchDecoys()
    {
        if (Controlled?.Body is not ShipBody player) return;
        int n = World.LaunchDecoys(player);
        Notify(n > 0 ? $"디코이 {n}" : player.Ordnance.DecoyDefinition is null ? "디코이 없음" : player.Ordnance.Decoys <= 0 ? "디코이 소진" : "디코이 재장전 중", n == 0);
    }

    /// <summary>
    /// F8 미사일 훈련: 적 호위함 하나를 40 km 앞에 세우고 6발을 내게 일제 사격시킨다(장전 간격마다 한 발).
    /// 근접방어·디코이·ECM을 시험하는 용도다. 적 AI는 6단계에서 만든다.
    /// </summary>
    private void SetupMissileDrill(float distance = 40_000f)
    {
        if (Controlled?.Body is not ShipBody player) return;
        ShipView? shooter = Views.Find(v => v.Body.Faction != player.Faction && v.Body.Class.Kind == HullKind.Escort
            && v.Body != _practiceTarget && !v.Body.Damage.Destroyed);
        if (shooter is null) return;
        Vector3 offset = (player.Orientation * new Quaternion(Vector3.Up, Mathf.DegToRad(20f))) * Vector3.Forward * distance;
        shooter.Body.Place(player.Position + Vec3d.From(offset), Basis.LookingAt(-offset.Normalized(), Vector3.Up).GetRotationQuaternion());
        shooter.Body.Ordnance.Reset();
        _drillShooter = shooter.Body;
        SuspendBrain(shooter.Body);
        _drillLaunched = 0;
        World.Sensors.Update(World.Ships, World.Time, force: true);
        Notify($"미사일 훈련 · {shooter.Body.Callsign} {distance / 1000:0} km", failed: false);
    }

    private void StepDrill()
    {
        if (_drillShooter is not ShipBody shooter || Controlled?.Body is not ShipBody player) return;
        if (_drillLaunched >= DrillSalvo || shooter.Damage.Destroyed) { _drillShooter = null; return; }
        if (shooter.Ordnance.MissileReady && World.LaunchMissile(shooter, player).Fired)
            _drillLaunched++;
    }

    private void Notify(string message, bool failed)
    {
        LastFireMessage = message;
        LastFireFailed = failed;
        LastFireTime = World.Time;
    }

    private void StepCombat()
    {
        StepDrill();
        StepJettison();
        if (MapControlsBlocked) return;
        // 검증용 자동 디코이: 나를 노리는 미사일이 8 km 안이면 쿨다운마다 사출.
        if (_shot is { AutoDecoys: true } && Controlled?.Body is ShipBody me && me.Ordnance.DecoyReady
            && World.Missiles.Any(m => m.Target == me && (m.Position - me.Position).Length() < 8000))
            World.LaunchDecoys(me);
        // 검증용 자동 발사: --launch=N은 검사 표적에 미사일을 N발까지 쏜다.
        if (_shot is { Launch: > 0 } && _autoMissiles < _shot.Launch && World.Tick >= 30 && Controlled?.Body.Ordnance.MissileReady == true
            && InspectTarget is ShipView mt && World.LaunchMissile(Controlled.Body, mt.Body).Fired)
            _autoMissiles++;
        if (Controlled?.Body is not ShipBody player) return;
        if (Scheme == ControlScheme.Helm)
        {
            FiringSolution = Gunnery?.Solution;
            CorrectingAim = false;
            if (!MenuOpen && !RadarPointerCaptured && SelectedWeapon == PlayerWeapon.MainGun && Gunnery?.Doctrine == FireDoctrine.Manual)
            {
                Vector2 cursor = GetViewport().GetMousePosition();
                Vec3d point = Camera.TelescopeHeld || _shot?.ManualFire == true
                    ? ManualAimPoint(player, RenderOrigin + Vec3d.From(Camera.Position), Camera.AimForward)
                    : ManualAimPoint(player, RenderOrigin + Vec3d.From(Camera.ProjectRayOrigin(cursor)), Camera.ProjectRayNormal(cursor));
                foreach (RailgunState gun in player.Railguns) gun.Aim((point - gun.MuzzlePosition).ToVector3());
                if ((!_fireReleaseGuard && Input.IsActionPressed(InputSetup.Fire)) || (_shot?.ManualFire == true && World.Tick >= 30 && _liveShots < _shot.Pulses))
                {
                    if (Camera.TelescopeHeld && ScopeHullBlocked) { Notify("선체에 시야 가림",true); return; }
                    FireAttempt manual = World.FireRailguns(player, Vector3.Forward, point);
                    Notify(manual.Reason, !manual.Fired);
                    if (manual.Fired) _liveShots++;
                }
            }
            else if (!MenuOpen && !RadarPointerCaptured && SelectedWeapon == PlayerWeapon.MainGun
                && !_fireReleaseGuard && Input.IsActionPressed(InputSetup.Fire)) FireSelectedMainBattery();
            return;
        }
        bool automated = _shot?.BallisticsTarget is not null;
        if (automated && _practiceTarget is not null)
        {
            Vector3 bearing = (_practiceTarget.Position - player.Position).ToVector3().Normalized();
            Camera.ResetAim(Basis.LookingAt(bearing, player.Up).GetRotationQuaternion());
        }
        ShipView? fireTarget = FireTarget;
        // 사격통제는 센서망 잠금이 있어야 해를 낸다. ECM·신호 세기가 오차에 반영된다.
        SensorTrack? track = fireTarget is null ? null : TrackOf(fireTarget);
        FiringSolution = FireAssist && fireTarget is not null ? FireControl.Solve(player, fireTarget.Body, World.Time, track: track, localAim: AimModule?.Definition.Center) : null;
        CorrectingAim = FiringSolution is { Valid: true } && track is SensorTrack known
            && Camera.AimForward.AngleTo((known.EstimatedPosition - player.Position).ToVector3()) < Mathf.DegToRad(8);
        bool firing = SelectedWeapon == PlayerWeapon.MainGun && !_fireReleaseGuard
            && Input.MouseMode == Input.MouseModeEnum.Captured && Input.IsActionPressed(InputSetup.Fire);
        if (automated) firing = World.Tick >= 30 && _liveShots < _shot!.Pulses && player.Railgun?.Ready == true;
        if (MenuOpen || RadarPointerCaptured) firing = false;
        if (!firing) return;
        if (Camera.TelescopeHeld && ScopeHullBlocked) { Notify("선체에 시야 가림",true); return; }
        // 센서가 없어도 수동 사격은 가능하다. 선택 표적이 조준 범위 안에 있을 때만 선행 보정한다.
        Vector3 direction = CorrectingAim ? FiringSolution!.Direction : ManualDirection(player);
        FireAttempt attempt = World.FireRailgun(player, direction);
        LastFireMessage = attempt.Reason;
        LastFireFailed = !attempt.Fired;
        LastFireTime = World.Time;
        if (attempt.Fired && automated)
        {
            _liveShots++;
            GD.Print($"railgun fired: {player.Callsign}, round {_liveShots}, {(CorrectingAim ? "assisted" : "manual")}");
        }
    }

    private Vector3 ManualDirection(ShipBody shooter)
        => ManualDirection(shooter, RenderOrigin + Vec3d.From(Camera.Position), Camera.AimForward);

    private Vector3 ManualDirection(ShipBody shooter, Vec3d origin, Vector3 ray)
    {
        if (shooter.Railgun is not RailgunState gun) return ray;
        return (ManualAimPoint(shooter, origin, ray) - gun.MuzzlePosition).ToVector3().Normalized();
    }

    private Vec3d ManualAimPoint(ShipBody shooter, Vec3d origin, Vector3 ray)
    {
        RailgunState gun = shooter.Railgun!;
        float distance = gun.Definition.MaxRange;
        foreach (ShipBody ship in World.Ships)
            if (ship != shooter && DamageRay.FirstHit(ship, origin, ray, distance, out float hit)) distance = hit;
        Vec3d point = origin + Vec3d.From(ray) * distance;
        return point;
    }
}
