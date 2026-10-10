using System;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class ScaleTest
{
    public bool TrackingSmallAim { get; private set; }
    private double _smallTrackingOverrideUntil;
    public double? SmallApproachSeconds { get; private set; }

    private void StepSmallTracking()
    {
        TrackingSmallAim = false;
        if (Controlled?.Body is not { } ship || Scheme != ControlScheme.Pilot || !FireAssist
            || SelectedWeapon != PlayerWeapon.MainGun || SelectedSmallContact is not { Kind: SmallContactKind.Missile } selected
            || Paused || Spectating || MapControlsBlocked || MenuOpen || RadarPointerCaptured || _fireReleaseGuard
            || Input.MouseMode != Input.MouseModeEnum.Captured || !Input.IsActionPressed(InputSetup.Fire)
            || World.Time < _smallTrackingOverrideUntil) return;
        var track = World.Sensors.SmallTrack(ship.Faction, selected.Target);
        if (track is not { } contact || !contact.Fresh(World.Time)) return;
        Vec3d relative = contact.Predict(World.Time) - ship.Position;
        Vector3 velocity = contact.Velocity - ship.Velocity;
        // Follow the incoming leg only: never drag the pilot into a U-turn behind a spent threat.
        if (relative.Dot(Vec3d.From(velocity)) >= 0 || ship.Forward.AngleTo(relative.ToVector3()) > Mathf.DegToRad(80)
            || Camera.AimForward.AngleTo(relative.ToVector3()) > Mathf.DegToRad(70)) return;
        if (!World.SmallTargetAim(ship, contact, out var direction, out _, playerAssist: true)) return;
        TrackingSmallAim = true;
        // Only changes the pilot's requested heading. ShipBody still enforces attitude acceleration,
        // turn rate and thrust limits; the weapon still waits for its physical traverse cone.
        Camera.GuideAim(direction, Mathf.DegToRad(120) * (float)SimWorld.TickDelta);
    }

    private void UpdateSmallApproach(ShipBody ship, SmallContact contact)
    {
        SmallApproachSeconds = null;
        if (!contact.Fresh(World.Time) || contact.Kind != SmallContactKind.Missile) return;
        Vec3d relative = contact.Predict(World.Time) - ship.Position;
        Vec3d velocity = Vec3d.From(contact.Velocity - ship.Velocity);
        if (velocity.LengthSquared() < 1) return;
        double approach = -relative.Dot(velocity) / velocity.LengthSquared();
        if (approach > 0 && approach < 60) SmallApproachSeconds = approach;
    }
}
