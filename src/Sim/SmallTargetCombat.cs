using System;
using Godot;

namespace SpaceFleet.Sim;

public sealed partial class SimWorld
{
    /// <summary>Player-selected interceptor counter-missile shot, using ordinary ammunition and cadence.</summary>
    public FireAttempt FireMissileIntercept(ShipBody ship, SmallContact contact)
    {
        if (ship.Class.Kind != HullKind.Interceptor || contact.Kind != SmallContactKind.Missile
            || contact.Target is not Missile missile || missile.Faction == ship.Faction
            || !SmallTargetAim(ship, contact, out var direction, out _, playerAssist: true))
            return new(false, "요격 사격통제 없음", Failure: FireFailure.NoDirection);
        var shot = FireRailgun(ship, direction);
        if (shot.Projectile is { } round)
        {
            round.ProximityTarget = missile;
            round.ProximityMeters = ship.Definition.Design == DesignFamily.Mars ? 32 : 40;
        }
        return shot;
    }

    private object? FirstSmallHit(RailProjectile projectile, Vec3d start, Vec3d end, double dt, double time, ref double nearest,
        out bool proximity)
    {
        var shooter = projectile.Shooter;
        proximity = false;
        object? hit = null;
        foreach (var missile in _missiles)
        {
            if (missile.Faction == shooter.Faction || missile.Health <= 0) continue;
            bool fused = projectile.ProximityTarget == missile && projectile.Age >= .04 && projectile.Age <= 3;
            double radius = fused ? projectile.ProximityMeters : 3;
            if (MovingSphereHit(start - missile.Position, end - start - Vec3d.From(missile.Velocity) * dt, radius, out double t) && t < nearest)
            { nearest = t; hit = missile; proximity = fused; }
        }
        foreach (var probe in _probes)
        {
            if (probe.Faction == shooter.Faction || !probe.Active) continue;
            // Probes integrate once at 60 Hz; rail collision runs at 240 Hz. Advance the sampled
            // pose to this substep instead of repeatedly testing against the tick-start position.
            Vec3d position = probe.Position + Vec3d.From(probe.Velocity) * Math.Clamp(time - Time, 0, TickDelta);
            if (MovingSphereHit(start - position, end - start - Vec3d.From(probe.Velocity) * dt,
                probe.Definition.Radius, out double t) && t < nearest) { nearest = t; hit = probe; proximity = false; }
        }
        return hit;
    }

    // Continuous double-precision relative trajectory: a fast missile cannot tunnel through a round.
    private static bool MovingSphereHit(Vec3d r, Vec3d travel, double radius, out double t)
    {
        t = 0;
        double c = r.LengthSquared() - radius * radius;
        if (c <= 0) return true;
        double a = travel.LengthSquared(), b = r.Dot(travel), discriminant = b * b - a * c;
        if (a < 1e-12 || discriminant < 0) return false;
        t = (-b - Math.Sqrt(discriminant)) / a;
        return t >= 0 && t <= 1;
    }

    /// <summary>Shared player/AI lead solution from a fresh sensor snapshot, never the live threat pose.</summary>
    public bool SmallTargetAim(ShipBody ship, SmallContact track, out Vector3 direction, out Vec3d aim, RailgunState? weapon = null, bool playerAssist = false)
    {
        direction = Vector3.Zero; aim = track.Predict(Time);
        var gun = weapon ?? ship.Railgun;
        if (!track.Fresh(Time) || gun is null || ship.Damage.WeaponsFraction <= .01f
            || ship.Power.SensorEffect <= .01f) return false;
        double age = Math.Clamp(Time - track.SeenAt, 0, .5);
        Vector3 acceleration = playerAssist ? track.Acceleration : Vector3.Zero;
        aim += Vec3d.From(acceleration) * (.5 * age * age);
        Vec3d relative = aim - gun.MuzzlePosition;
        double range = relative.Length();
        bool earlyMissile = playerAssist && track.Kind == SmallContactKind.Missile;
        float maxRange = earlyMissile ? (ship.Definition.Design == DesignFamily.Mars ? 12000 : 14000)
            : ship.Definition.Design == DesignFamily.Mars ? 4500 : 6000;
        double maxFlight = earlyMissile ? 2.8 : 1.4;
        if (range > maxRange || range < 1) return false;
        Vector3 velocity = track.Velocity + acceleration * (float)age - ship.Velocity;
        double a = velocity.LengthSquared() - gun.Definition.MuzzleSpeed * gun.Definition.MuzzleSpeed;
        double b = 2 * relative.Dot(Vec3d.From(velocity)), c = relative.LengthSquared();
        double d = b * b - 4 * a * c;
        if (d < 0) return false;
        double t = Math.Abs(a) < 1e-6 ? (b < -1e-6 ? -c / b : -1)
            : (-b - Math.Sqrt(d)) / (2 * a);
        if (t <= 0 || t > maxFlight) return false;
        // Solve the observed accelerating trajectory, never guidance/hidden state. Fixed-point
        // distance/speed iteration oscillates when a closing missile is as fast as our projectile.
        if (playerAssist)
            for (int i = 0; i < 12; i++)
            {
                Vec3d offset = relative + Vec3d.From(velocity) * t + Vec3d.From(acceleration) * (.5 * t * t);
                double distance = offset.Length(), error = distance - gun.Definition.MuzzleSpeed * t;
                if (Math.Abs(error) < .01) break;
                double derivative = offset.Dot(Vec3d.From(velocity + acceleration * (float)t)) / Math.Max(distance, .001)
                    - gun.Definition.MuzzleSpeed;
                if (Math.Abs(derivative) < 1e-6) return false;
                t -= error / derivative;
                if (!double.IsFinite(t) || t <= 0 || t > maxFlight) return false;
            }
        if (!double.IsFinite(t) || t <= 0 || t > maxFlight) return false;
        if (playerAssist && Math.Abs((relative + Vec3d.From(velocity) * t
            + Vec3d.From(acceleration) * (.5 * t * t)).Length() - gun.Definition.MuzzleSpeed * t) > .1) return false;
        aim += Vec3d.From(velocity) * t + Vec3d.From(acceleration) * (.5 * t * t);
        direction = (aim - gun.MuzzlePosition).ToVector3().Normalized();
        return true;
    }
}
