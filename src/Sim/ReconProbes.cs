using System;
using System.Collections.Generic;
using Godot;

namespace SpaceFleet.Sim;

public enum ProbeKind { Recon }
public sealed record ReconProbeDefinition
{
    public ProbeKind Kind { get; init; } = ProbeKind.Recon;
    public int Count { get; init; } = 2;
    public float LifetimeSeconds { get; init; } = 120;
    public float DeploymentMeters { get; init; } = 30_000;
    public float Acceleration { get; init; } = 220;
    public float BurnSeconds { get; init; } = 8;
    public float SensorStrength { get; init; } = 85;
    public float Signature { get; init; } = .6f;
    public float HitPoints { get; init; } = 4;
    public float Radius { get; init; } = 2;
    public float CooldownSeconds { get; init; } = 12;
    /// <summary>Optional authored rack exits, in ship-local meters. Legacy hulls retain clearance launch.</summary>
    public Vector3[]? LaunchPoints { get; init; }
}

/// <summary>Finite stores and lifetime counters; weapon/development reset cannot replenish probes.</summary>
public sealed class InterceptorEquipment(ShipBody ship)
{
    public int Probes { get; internal set; } = ship.Definition.ReconProbes?.Count ?? 0;
    public int Launched { get; internal set; }
    public int NewContacts { get; internal set; }
    public int NewIdentifications { get; internal set; }
    public int MissileKills { get; internal set; }
    public int ProbeKills { get; internal set; }
    public double? FirstAction { get; internal set; }
    public double? FirstAssault { get; internal set; }
    internal double NextLaunch;
}

/// <summary>A sensor, battery and flight state, deliberately not a ShipBody.</summary>
public sealed class ReconProbe
{
    public required uint Id { get; init; }
    public required ShipBody Owner { get; init; }
    public required ReconProbeDefinition Definition { get; init; }
    public Faction Faction => Owner.Faction;
    public Vec3d Position, PreviousPosition, Destination;
    public Vector3 Velocity, Direction;
    public double Age;
    public double LaunchedAt { get; init; }
    public float Health;
    public int Observing { get; internal set; }
    public double Remaining => Math.Max(0, Definition.LifetimeSeconds - Age);
    public bool Active => Health > 0 && Remaining > 0;
    public bool OnStation => (Destination - Position).Length() < 100 && Velocity.Length() < 20;
    internal readonly HashSet<ShipBody> Contacts = new(), Identifications = new();
}

public readonly record struct ProbeFlight(uint Id, string Owner, Faction Faction, double LaunchedAt,
    double Lifetime, bool Destroyed, int Contacts, int Identifications);

public sealed partial class SimWorld
{
    private readonly List<ReconProbe> _probes = new();
    private readonly List<ProbeFlight> _probeFlights = new();
    public IReadOnlyList<ReconProbe> Probes => _probes;
    public IReadOnlyList<ProbeFlight> ProbeFlights => _probeFlights;

    /// <summary>Map plane destination, clipped to the same deployment distance used by actual flight.</summary>
    public static bool TryProbeDestination(ShipBody ship, Vec3d requested, out Vec3d destination,ObstacleSet? obstacles=null)
    {
        destination = ship.Position;
        if (ship.Definition.ReconProbes is not { } def) return false;
        bool authored = def.LaunchPoints is { Length: > 0 };
        Vec3d start = authored ? ProbeStart(ship, def, Vector3.Zero) : ship.Position;
        Vec3d delta = requested - start;
        double distance = delta.Length(), clearance = authored ? 0 : ship.Hull.BoundingRadius + 4;
        if (!double.IsFinite(distance) || distance <= clearance + 1) return false;
        destination = start + delta * (Math.Min(distance, clearance + def.DeploymentMeters) / distance);
        if(obstacles is {Count:>0} && obstacles.SegmentBlocked(destination,destination,out _,def.Radius)
            && obstacles.SegmentBlocked(start,destination,out var hit,def.Radius))
        {
            destination=hit.Position-delta*(2/distance);
            if((destination-start).Length()<=clearance+1) return false;
        }
        return true;
    }

    public FireAttempt LaunchProbeAt(ShipBody ship, Vec3d requested)
    {
        if (!TryProbeDestination(ship, requested, out var destination,Obstacles)) return new(false, "프로브 투사 위치 없음");
        return LaunchProbe(ship, (destination - ship.Position).ToVector3(), destination);
    }

    public FireAttempt LaunchProbe(ShipBody ship, Vector3 direction) => LaunchProbe(ship, direction, null);

    private static Vec3d ProbeStart(ShipBody ship, ReconProbeDefinition definition, Vector3 direction)
        => ship.Position + Vec3d.From(definition.LaunchPoints is { Length: > 0 } ports
            ? ship.Orientation * ports[ship.Ordnance.Interceptor.Launched % ports.Length]
            : direction * (ship.Hull.BoundingRadius + 4));

    private FireAttempt LaunchProbe(ShipBody ship, Vector3 direction, Vec3d? destination)
    {
        if (!_ships.Contains(ship) || ship.Definition.ReconProbes is not { } def)
            return new(false, "정찰 프로브 미탑재");
        var equipment = ship.Ordnance.Interceptor;
        if (!Squadron.Active(ship) || ship.Power.Supply <= .01f) return new(false, "사출 계통 정지");
        if (equipment.Probes <= 0) return new(false, "프로브 소진");
        if (Time < equipment.NextLaunch) return new(false, "프로브 사출 준비 중");
        if (!direction.IsFinite() || direction.LengthSquared() < .01f) return new(false, "투사 방향 없음");
        direction = direction.Normalized();
        Vec3d start = ProbeStart(ship, def, direction);
        Vec3d goal=destination??start+Vec3d.From(direction)*def.DeploymentMeters;
        if(Obstacles.Count>0 && !TryProbeDestination(ship,goal,out goal,Obstacles)) return new(false,"프로브 투사 위치 없음");
        _probes.Add(new ReconProbe { Id = ++_shotSequence, Owner = ship, Definition = def,
            LaunchedAt = Time, Position = start, PreviousPosition = start, Destination = goal,
            Direction = direction, Velocity = ship.Velocity + direction * 50, Health = def.HitPoints });
        equipment.Probes--; equipment.Launched++; equipment.NextLaunch = Time + def.CooldownSeconds;
        equipment.FirstAction ??= Time;
        return new(true, "정찰 프로브 사출");
    }

    private void StepProbes(double dt)
    {
        foreach (var probe in _probes)
        {
            probe.PreviousPosition = probe.Position;
            var r = (probe.Destination - probe.Position).ToVector3();
            float distance = r.Length(), accel = probe.Definition.Acceleration;
            float speed = Mathf.Min(accel * probe.Definition.BurnSeconds, Mathf.Sqrt(2 * accel * distance) * .85f);
            Vector3 desired = distance > 1 ? r / distance * speed : Vector3.Zero;
            probe.Velocity += (desired - probe.Velocity).LimitLength(accel * (float)dt);
            probe.Position += Vec3d.From(probe.Velocity) * dt;
            if(Obstacles.Count>0 && Obstacles.SegmentBlocked(probe.PreviousPosition,probe.Position,out var hit,probe.Definition.Radius))
            {
                probe.Position=hit.Position; probe.Health=0;
                Log?.Environment.Weapon(Time+dt*hit.Fraction,probe.Owner.Callsign,hit,null);
                _ordnanceEvents.Add(new(OrdnanceEventKind.ProbeDestroyed,hit.Position,Time+dt*hit.Fraction,probe.Faction));
            }
            probe.Age += dt;
        }
        for (int i = _probes.Count - 1; i >= 0; i--)
            if (!_probes[i].Active) EndProbe(_probes[i]);
    }

    private void EndProbe(ReconProbe probe)
    {
        if (!_probes.Remove(probe)) return;
        _probeFlights.Add(new(probe.Id, probe.Owner.Callsign, probe.Faction, probe.LaunchedAt,
            probe.Age, probe.Health <= 0, probe.Contacts.Count, probe.Identifications.Count));
    }

    private void DamageProbe(ShipBody shooter, ReconProbe probe, float damage, double time)
    {
        if (!probe.Active || shooter.Faction == probe.Faction) return;
        probe.Health -= damage;
        if (probe.Health > 0) return;
        shooter.Ordnance.Interceptor.ProbeKills++;
        shooter.Ordnance.Interceptor.FirstAction ??= time;
        _ordnanceEvents.Add(new(OrdnanceEventKind.ProbeDestroyed, probe.Position, time, probe.Faction));
        EndProbe(probe);
    }

    private void DestroyMissile(ShipBody shooter, Missile missile, double time)
    {
        if (!_missiles.Remove(missile)) return;
        missile.Health = 0;
        Log?.Intercept(shooter, missile);
        Log?.EndAntimatter(missile, AntimatterOutcome.PointDefense, time);
        shooter.Ordnance.Interceptor.MissileKills++;
        shooter.Ordnance.Interceptor.FirstAction ??= time;
        _ordnanceEvents.Add(new(OrdnanceEventKind.Intercepted, missile.Position, time, missile.Faction, missile.Weapon));
    }
}
