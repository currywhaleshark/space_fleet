using System;
using Godot;

namespace SpaceFleet.Sim;

public sealed record RailgunDefinition(string ModuleId, Vector3 Muzzle, float MuzzleSpeed, float ReloadSeconds,
    float Energy, float PenetrationMm, float ModuleDamage, float MaxRange, int Rounds, float TraverseDegrees,
    float SensorErrorMeters = 1f, float SensorErrorPerKm = 0.15f, float VelocityError = 0.3f, float ShotHeatMj = 0f)
{
    public DamagePacket Packet => new(Energy, PenetrationMm, ModuleDamage, MaxRange);
}

/// <summary>
/// 주포 상태. 재장전 속도 = 주포 모듈 상태(체력·전력망·탄약고) × 무장 채널 배율(핍·전압 강하·과열).
/// 발사할 때마다 ShotHeatMj만큼 열이 오르고, 과열 중에는 쏠 수 없다.
/// </summary>
public sealed class RailgunState
{
    private readonly ShipBody _ship;
    public RailgunState(ShipBody ship) { _ship = ship; Reset(); }
    public RailgunDefinition Definition => _ship.Definition.Railgun!;
    public int Rounds { get; private set; }
    public float ReloadRemaining { get; private set; }
    public float Output => _ship.Damage.WeaponFraction(Definition.ModuleId);
    private float ReloadRate => Output * _ship.Power.WeaponEffect;
    public Vec3d MuzzlePosition => _ship.Position + Vec3d.From(_ship.Orientation * Definition.Muzzle);
    public string Status => _ship.Damage.Destroyed ? "격침" : Output <= 0.01f ? "주포/전력/탄약고 손상"
        : Rounds <= 0 ? "탄약 소진" : _ship.Power.Overheated ? "과열 · 발사 불가"
        : ReloadRemaining > 0 ? ReloadRate <= 0.01f ? "재장전 대기 · 무장 전력 없음"
            : $"재장전 {ReloadRemaining / ReloadRate:0.0}s" : "발사 준비";
    public bool Ready => !_ship.Damage.Destroyed && Output > 0.01f && Rounds > 0 && ReloadRemaining <= 0 && !_ship.Power.Overheated;
    public void Reset() { Rounds = Definition.Rounds; ReloadRemaining = 0; }
    internal void Step(double dt) => ReloadRemaining = Mathf.Max(0, ReloadRemaining - (float)dt * ReloadRate);
    internal void Consume()
    {
        Rounds--;
        ReloadRemaining = Definition.ReloadSeconds;
        _ship.Power.AddHeat(Definition.ShotHeatMj);
    }
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

/// <summary>발사 실패 사유 코드. 문구(Reason)는 알림용이고 판단은 이 값으로 한다.</summary>
public enum FireFailure { None, NotInWorld, NoGun, NoDirection, NotReady, Arc, HullBlocked }

public readonly record struct FireAttempt(bool Fired, string Reason, RailProjectile? Projectile = null, FireFailure Failure = FireFailure.None);
public sealed record ProjectileImpact(uint Id, ShipBody Shooter, ShotResult Hit, double Time);
