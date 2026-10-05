using System;
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
        World.ResetWeapons();
        InspectTarget = target;
        Camera.ResetAim(player.Orientation);
        _liveShots = 0;
        LastFireMessage = $"이동 표적 연습 · {speed:0} m/s";
        LastFireTime = World.Time;
    }

    private void StepCombat()
    {
        if (Controlled?.Body is not ShipBody player) return;
        bool automated = _shot?.BallisticsTarget is not null;
        if (automated && _practiceTarget is not null)
        {
            Vector3 bearing = (_practiceTarget.Position - player.Position).ToVector3().Normalized();
            Camera.ResetAim(Basis.LookingAt(bearing, player.Up).GetRotationQuaternion());
        }
        FiringSolution = FireAssist && InspectTarget is not null ? FireControl.Solve(player, InspectTarget.Body, World.Time) : null;
        CorrectingAim = FiringSolution is { Valid: true } solution && InspectTarget is not null
            && Camera.AimForward.AngleTo((InspectTarget.Body.Position - player.Position).ToVector3()) < Mathf.DegToRad(8);
        bool firing = Input.MouseMode == Input.MouseModeEnum.Captured && Input.IsActionPressed(InputSetup.Fire);
        if (automated) firing = World.Tick >= 30 && _liveShots < _shot!.Pulses && player.Railgun?.Ready == true;
        if (!firing) return;
        // 센서가 없어도 수동 사격은 가능하다. 선택 표적이 조준 범위 안에 있을 때만 선행 보정한다.
        Vector3 direction = CorrectingAim ? FiringSolution!.Direction : ManualDirection(player);
        FireAttempt attempt = World.FireRailgun(player, direction);
        LastFireMessage = attempt.Reason;
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
