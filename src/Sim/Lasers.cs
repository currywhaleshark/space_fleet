using System;
using System.IO;
using Godot;

namespace SpaceFleet.Sim;

/// <summary>Continuous energy/heat rates. Range is a bounded ray horizon, not an effective combat range.</summary>
public sealed record LaserDefinition(float EnergyPerSecond, float ModuleDamagePerSecond, float PenetrationMm,
    float HeatMw, float FalloffMeters, float SensorErrorMeters, float SensorErrorPerKm, float AimErrorScale = 1,
    float RampSeconds = 8, float MaxArmorReduction = .75f, float SpotLengthFraction = .02f,
    float DecaySeconds = 1, float RayRange = 1_000_000)
{
    public const double PulseInterval = 1.0 / 15;
    public const float PulseSeconds = (float)PulseInterval;
    // This adapter supplies common mount geometry and observed aiming fields only.
    // FireRailgun dispatches lasers before projectile creation; no light-speed projectile exists.
    internal RailgunBallistics GeometryAdapter => new(299_792_458, PulseSeconds, EnergyPerSecond * PulseSeconds,
        PenetrationMm, ModuleDamagePerSecond * PulseSeconds, RayRange, SensorErrorMeters, SensorErrorPerKm, 0, 0, AimErrorScale);
    internal void Validate()
    {
        float[] positive = { EnergyPerSecond, ModuleDamagePerSecond, PenetrationMm, HeatMw, FalloffMeters,
            AimErrorScale, RampSeconds, SpotLengthFraction, DecaySeconds, RayRange };
        foreach (float v in positive) if (!float.IsFinite(v) || v <= 0) throw new InvalidDataException("Invalid laser rate/range");
        if (!float.IsFinite(MaxArmorReduction) || MaxArmorReduction < 0 || MaxArmorReduction > .9f
            || !float.IsFinite(SensorErrorMeters) || SensorErrorMeters < 0 || !float.IsFinite(SensorErrorPerKm) || SensorErrorPerKm < 0
            || SpotLengthFraction > .05f || RayRange > 1_000_000 || FalloffMeters > RayRange
            || !float.IsFinite(HeatMw * 2.5f) || !float.IsFinite(ModuleDamagePerSecond * PulseSeconds))
            throw new InvalidDataException("Invalid laser focus/heat budget");
    }
    public float Attenuation(float range) => 1 / (1 + (range / FalloffMeters) * (range / FalloffMeters));
}

/// <summary>One emitter's focus cannot transfer to another mount, ship or hull patch.</summary>
public sealed class LaserState
{
    internal LaserState(LaserDefinition definition) { Definition = definition; Reset(); }
    public LaserDefinition Definition { get; }
    public bool Active { get; internal set; }
    public float FocusFraction => Mathf.Clamp(FocusSeconds / Definition.RampSeconds, 0, 1);
    public float FocusSeconds { get; private set; }
    public float SignatureBoost { get; internal set; }
    public ulong Pulses { get; internal set; }
    public double LastPulseAt { get; internal set; }
    public Vec3d End { get; internal set; }
    public ShipBody? HitShip { get; internal set; }
    public Vector3 HitLocal { get; internal set; }
    public bool ShieldHit { get; internal set; }
    private ShipBody? _focusShip;
    private Vector3 _anchor;
    internal Vector3 RequestedDirection;
    internal double RequestedUntil, PulseCredit;
    internal bool AutoCooling;
    public bool Cooling => AutoCooling;
    internal void Focus(ShipBody ship, Vector3 local, float seconds)
    {
        float radius = Math.Max(.2f, ship.Class.Length * Definition.SpotLengthFraction);
        if (_focusShip != ship || local.DistanceSquaredTo(_anchor) > radius * radius)
        { _focusShip = ship; _anchor = local; FocusSeconds = 0; }
        FocusSeconds = Math.Min(Definition.RampSeconds, FocusSeconds + seconds);
    }
    internal void LoseFocus(float seconds)
    {
        FocusSeconds = Math.Max(0, FocusSeconds - Definition.RampSeconds / Definition.DecaySeconds * seconds);
        if (FocusSeconds == 0) _focusShip = null;
    }
    public void Stop() { RequestedUntil = double.NegativeInfinity; Active = false; PulseCredit = 0; }
    public void Reset()
    {
        Stop(); FocusSeconds = SignatureBoost = 0; Pulses = 0; LastPulseAt = double.NegativeInfinity;
        AutoCooling = false;
        _focusShip = HitShip = null; End = Vec3d.Zero; HitLocal = _anchor = Vector3.Zero; ShieldHit = false;
    }
}

public sealed partial class SimWorld
{
    public static void StopLasers(ShipBody ship)
    { foreach (var gun in ship.Railguns) gun.Laser?.Stop(); }

    private FireAttempt RequestLaser(ShipBody ship, RailgunState gun, Vector3 direction)
    {
        var laser = gun.Laser!;
        if (!direction.IsFinite() || direction.LengthSquared() < 1e-8f)
        { laser.Stop(); return new(false, "조준 방향 없음", Failure: FireFailure.NoDirection); }
        direction = direction.Normalized(); gun.Aim(direction);
        if (!gun.Ready) { laser.Stop(); return new(false, gun.Status, Failure: FireFailure.NotReady); }
        var line = RailLineLocal(ship, gun, ship.Orientation.Inverse() * direction);
        if (line != FireFailure.None)
        { laser.Stop(); return new(false, line == FireFailure.Arc ? "조사각 밖" : "선체 가림", Failure: line); }
        if (!gun.Aligned(direction)) { laser.Stop(); return new(false, "포탑 선회 중", Failure: FireFailure.Traversing); }
        laser.RequestedDirection = direction;
        // Player/AI must renew at 60 Hz. A lost target, stale order or released input cannot latch the beam on.
        laser.RequestedUntil = ship.SimTime + TickDelta * 1.5;
        return new(true, "레이저 조사", Shots: 1);
    }

    private void StepLasers(double dt)
    {
        foreach (var ship in _ships)
        foreach (var gun in ship.Railguns)
        {
            if (gun.Laser is not { } laser) continue;
            float seconds = (float)dt;
            laser.Active = ship.SimTime <= laser.RequestedUntil && gun.Ready && ship.Power.HeatFraction < 1
                && gun.Aligned(laser.RequestedDirection)
                && RailLineLocal(ship, gun, ship.Orientation.Inverse() * laser.RequestedDirection) == FireFailure.None
                && !BarrelBlocked(ship, gun, gun.Yaw, gun.Elevation);
            if (!laser.Active)
            {
                laser.PulseCredit = 0; laser.LoseFocus(seconds);
                laser.SignatureBoost = Mathf.MoveToward(laser.SignatureBoost, 0, seconds);
                continue;
            }
            float output = Math.Min(1.5f, gun.Output * ship.Power.WeaponEffect);
            ship.Power.AddHeat(laser.Definition.HeatMw * (1 + laser.FocusFraction * 1.5f) * output * seconds);
            laser.SignatureBoost = 1 + 2 * laser.FocusFraction;
            laser.PulseCredit += dt;
            if (laser.PulseCredit + 1e-10 < LaserDefinition.PulseInterval) continue;
            laser.PulseCredit -= LaserDefinition.PulseInterval;
            LaserPulse(ship, gun, output);
        }
    }

    private void LaserPulse(ShipBody shooter, RailgunState gun, float output)
    {
        var state = gun.Laser!; var def = state.Definition;
        Vec3d from = gun.MuzzlePosition;
        Vector3 direction = gun.Mount is null ? state.RequestedDirection : gun.Direction;
        Vec3d travel = Vec3d.From(direction) * def.RayRange;
        float nearest = def.RayRange;
        if (Obstacles.Count > 0 && Obstacles.SegmentBlocked(from, from + travel, out var wall))
            nearest = (float)(def.RayRange * wall.Fraction);
        ShipBody? target = null;
        foreach (var ship in _ships)
        {
            if (ship == shooter) continue;
            Vec3d relative = ship.Position - from;
            double along = Math.Clamp(relative.Dot(Vec3d.From(direction)), 0, nearest);
            float radius = DamageRay.DefenseRadius(ship);
            if ((relative - Vec3d.From(direction) * along).LengthSquared() > radius * (double)radius) continue;
            if (DamageRay.FirstDefenseHitAtPose(ship, from, direction, nearest, ship.Position, ship.Orientation, out float distance)
                && distance < nearest) { nearest = distance; target = ship; }
        }
        object? small = null;
        foreach (var missile in _missiles)
            if (missile.Faction != shooter.Faction && missile.Health > 0
                && MovingSphereHit(from - missile.Position, travel, 3, out double t) && t * def.RayRange < nearest)
            { nearest = (float)(t * def.RayRange); small = missile; target = null; }
        foreach (var probe in _probes)
            if (probe.Faction != shooter.Faction && probe.Active
                && MovingSphereHit(from - probe.Position, travel, probe.Definition.Radius, out double t) && t * def.RayRange < nearest)
            { nearest = (float)(t * def.RayRange); small = probe; target = null; }
        ShipBody? droneCarrier = null; int droneIndex = -1;
        foreach (var carrier in _ships)
        {
            if (carrier.Faction == shooter.Faction || carrier.Damage.Destroyed || carrier.Definition.DefenseDrones is not { } drones) continue;
            for (int i=0;i<drones.Count;i++)
                if (carrier.Ordnance.Drones.Alive(i) && MovingSphereHit(from-carrier.Ordnance.Drones.WorldPosition(i),travel,drones.RadiusMeters,out double t)
                    && t*def.RayRange<nearest)
                { nearest=(float)(t*def.RayRange);droneCarrier=carrier;droneIndex=i;small=null;target=null; }
        }

        state.End = from + Vec3d.From(direction) * nearest;
        state.HitShip = target; state.ShieldHit = false;
        state.LastPulseAt = shooter.SimTime; state.Pulses++;
        Log?.Fire(shooter, BattleWeapon.Laser, shooter.SimTime);
        float scale = def.Attenuation(nearest) * output * LaserDefinition.PulseSeconds;
        if (target is null)
        {
            state.LoseFocus(LaserDefinition.PulseSeconds);
            if (small is Missile missile)
            { missile.Health -= def.ModuleDamagePerSecond * scale; if (missile.Health <= 0) DestroyMissile(shooter, missile, shooter.SimTime); }
            else if (small is ReconProbe probe) DamageProbe(shooter, probe, def.ModuleDamagePerSecond * scale, shooter.SimTime);
            else if (droneCarrier is not null) DamageDrone(shooter,droneCarrier,droneIndex,def.ModuleDamagePerSecond*scale,shooter.SimTime);
            return;
        }
        float shieldBefore = target.Damage.Shield;
        state.HitLocal = target.Orientation.Inverse() * (state.End - target.Position).ToVector3();
        if (shieldBefore > 0 || target.Damage.Destroyed) state.LoseFocus(LaserDefinition.PulseSeconds);
        else state.Focus(target, state.HitLocal, LaserDefinition.PulseSeconds);
        var packet = new DamagePacket(def.EnergyPerSecond * scale, def.PenetrationMm,
            def.ModuleDamagePerSecond * scale, def.RayRange);
        uint id = ++_shotSequence;
        var hit = DamageRay.Apply(target, from, direction, packet, shooter.SimTime, id,
            entryArmorScale: shieldBefore > 0 ? 1 : 1 - state.FocusFraction * def.MaxArmorReduction);
        state.End = hit.Point; state.HitLocal = hit.LocalPoint ?? state.HitLocal; state.ShieldHit = !hit.HullHit;
        Log?.Hit(shooter, hit, BattleWeapon.Laser, shooter.SimTime, shieldBefore);
        // Limit secondary bursts/feedback per mount; the continuous surface spot is rendered separately.
        if (state.Pulses % 4 == 0 || hit.CausedDestruction || shieldBefore > 0 && target.Damage.Shield <= 0)
            RecordImpact(id, shooter, hit, shooter.SimTime, direction, packet.Energy, shieldBefore, BattleWeapon.Laser);
        if (target.Faction != shooter.Faction && !target.Damage.Destroyed)
            Sensors.ObserveLaserBearing(target, shooter, -direction, shooter.SimTime);
    }
}
