using System;
using Godot;

namespace SpaceFleet.Sim;

public sealed record RailgunDefinition(string ModuleId, Vector3 Muzzle, float MuzzleSpeed, float ReloadSeconds,
    float Energy, float PenetrationMm, float ModuleDamage, float MaxRange, int Rounds, float TraverseDegrees,
    float SensorErrorMeters = 1f, float SensorErrorPerKm = 0.15f, float VelocityError = 0.3f)
{
    public DamagePacket Packet => new(Energy, PenetrationMm, ModuleDamage, MaxRange);
}

/// <summary>주포 상태. 재장전은 해당 주포·전력·냉각 상태에 따라 느려진다.</summary>
public sealed class RailgunState
{
    private readonly ShipBody _ship;
    public RailgunState(ShipBody ship) { _ship = ship; Reset(); }
    public RailgunDefinition Definition => _ship.Definition.Railgun!;
    public int Rounds { get; private set; }
    public float ReloadRemaining { get; private set; }
    public float Output => _ship.Damage.WeaponFraction(Definition.ModuleId);
    public Vec3d MuzzlePosition => _ship.Position + Vec3d.From(_ship.Orientation * Definition.Muzzle);
    public string Status => _ship.Damage.Destroyed ? "격침" : Output <= 0.01f ? "주포/전력/탄약고 손상"
        : Rounds <= 0 ? "탄약 소진" : ReloadRemaining > 0
            ? _ship.Damage.CoolingFraction <= 0.01f ? "재장전 대기 · 냉각 손상"
                : $"재장전 {ReloadRemaining / (Output * _ship.Damage.CoolingFraction):0.0}s" : "발사 준비";
    public bool Ready => !_ship.Damage.Destroyed && Output > 0.01f && Rounds > 0 && ReloadRemaining <= 0;
    public void Reset() { Rounds = Definition.Rounds; ReloadRemaining = 0; }
    internal void Step(double dt) => ReloadRemaining = Mathf.Max(0, ReloadRemaining - (float)dt * Output * _ship.Damage.CoolingFraction);
    internal void Consume() { Rounds--; ReloadRemaining = Definition.ReloadSeconds; }
}

public sealed class RailProjectile
{
    public required uint Id { get; init; }
    public required ShipBody Shooter { get; init; }
    public required Vector3 Velocity { get; init; }
    public required DamagePacket Packet { get; init; }
    public required double Lifetime { get; init; }
    public required Vec3d Position { get; set; }
    public Vec3d PrevPosition { get; internal set; }
    public double Age { get; internal set; }
}

public readonly record struct FireAttempt(bool Fired, string Reason, RailProjectile? Projectile = null);
public sealed record ProjectileImpact(uint Id, ShipBody Shooter, ShotResult Hit, double Time);
