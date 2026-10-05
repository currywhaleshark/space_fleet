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

    /// <summary>사격통제 표적. 검사 표적이 살아 있는 적일 때만 잡는다(아군에게 선행 보정을 계산하지 않는다).</summary>
    public ShipView? FireTarget => InspectTarget is ShipView view && Controlled is ShipView me
        && view.Body.Faction != me.Body.Faction && !view.Body.Damage.Destroyed ? view : null;
    public double LastFireTime { get; private set; } = -100;

    private void SetupPractice(string callsign = "DD-X1", float distance = 6000, float speed = 180)
    {
        if (Controlled is null) return;
        var target = Views.Find(v => v.Body.Callsign == callsign && v != Controlled)
            ?? throw new ArgumentException($"Unknown practice target: {callsign}");
        ShipBody player = Controlled.Body;
        _practiceTarget = target.Body;
        target.Body.Place(player.Position + Vec3d.From(player.Forward) * distance,
            (player.Orientation * new Quaternion(Vector3.Up, Mathf.Pi)).Normalized());
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

    /// <summary>우클릭: 검사 표적(탐지된 적)에 미사일 한 발. 접촉 이상이면 쏠 수 있다.</summary>
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
        // 검증용 자동 디코이: 나를 노리는 미사일이 8 km 안이면 쿨다운마다 사출.
        if (_shot is { AutoDecoys: true } && Controlled?.Body is ShipBody me && me.Ordnance.DecoyReady
            && World.Missiles.Any(m => m.Target == me && (m.Position - me.Position).Length() < 8000))
            World.LaunchDecoys(me);
        // 검증용 자동 발사: --launch=N은 검사 표적에 미사일을 N발까지 쏜다.
        if (_shot is { Launch: > 0 } && _autoMissiles < _shot.Launch && World.Tick >= 30 && Controlled?.Body.Ordnance.MissileReady == true
            && InspectTarget is ShipView mt && World.LaunchMissile(Controlled.Body, mt.Body).Fired)
            _autoMissiles++;
        if (Controlled?.Body is not ShipBody player) return;
        bool automated = _shot?.BallisticsTarget is not null;
        if (automated && _practiceTarget is not null)
        {
            Vector3 bearing = (_practiceTarget.Position - player.Position).ToVector3().Normalized();
            Camera.ResetAim(Basis.LookingAt(bearing, player.Up).GetRotationQuaternion());
        }
        ShipView? fireTarget = FireTarget;
        // 사격통제는 센서망 잠금이 있어야 해를 낸다. ECM·신호 세기가 오차에 반영된다.
        SensorTrack? track = fireTarget is null ? null : TrackOf(fireTarget);
        FiringSolution = FireAssist && fireTarget is not null ? FireControl.Solve(player, fireTarget.Body, World.Time, track: track) : null;
        CorrectingAim = FiringSolution is { Valid: true } && track is SensorTrack known
            && Camera.AimForward.AngleTo((known.EstimatedPosition - player.Position).ToVector3()) < Mathf.DegToRad(8);
        bool firing = Input.MouseMode == Input.MouseModeEnum.Captured && Input.IsActionPressed(InputSetup.Fire);
        if (automated) firing = World.Tick >= 30 && _liveShots < _shot!.Pulses && player.Railgun?.Ready == true;
        if (!firing) return;
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
    {
        if (shooter.Railgun is not RailgunState gun) return Camera.AimForward;
        Vec3d origin = RenderOrigin + Vec3d.From(Camera.Position);
        float distance = gun.Definition.MaxRange;
        foreach (ShipBody ship in World.Ships)
            if (ship != shooter && DamageRay.FirstHit(ship, origin, Camera.AimForward, distance, out float hit)) distance = hit;
        Vec3d point = origin + Vec3d.From(Camera.AimForward) * distance;
        return (point - gun.MuzzlePosition).ToVector3().Normalized();
    }
}
