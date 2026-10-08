using System;
using Godot;

namespace SpaceFleet.Sim;

public sealed record RailgunDefinition(string ModuleId, Vector3 Muzzle, float MuzzleSpeed, float ReloadSeconds,
    float Energy, float PenetrationMm, float ModuleDamage, float MaxRange, int Rounds, float TraverseDegrees,
    float SensorErrorMeters = 1f, float SensorErrorPerKm = 0.15f, float VelocityError = 0.3f, float ShotHeatMj = 0f,
    TurretDefinition[]? Mounts = null)
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
    public RailgunState(ShipBody ship, TurretDefinition? mount = null)
    {
        _ship = ship; Mount = mount;
        var common = ship.Definition.Railgun!;
        Definition = mount is null ? common : common with { ModuleId = mount.ModuleId, Rounds = mount.Rounds,
            Muzzle = mount.Breech(0) + mount.MountBasis * (mount.Muzzles[0] + mount.Muzzles[^1]) * .5f };
        Reset();
    }
    public RailgunDefinition Definition { get; }
    public TurretDefinition? Mount { get; }
    public float Yaw { get; private set; }
    public float Elevation { get; private set; }
    public float PreviousYaw { get; internal set; }
    public float PreviousElevation { get; internal set; }
    public int Barrel { get; private set; }
    public int LastBarrel { get; private set; }
    public double LastFiredAt { get; private set; } = double.NegativeInfinity;
    public uint ShotCount { get; private set; }
    private Vector3? _aimDirection;
    private double _aimUntil;
    public Vector3 LocalDirection => Mount?.AimBasis(Yaw, Elevation) * Vector3.Forward ?? Vector3.Forward;
    public Vector3 Direction => _ship.Orientation * LocalDirection;
    public Vector3 LocalMuzzle => Mount?.Muzzle(Yaw, Elevation, Barrel) ?? Definition.Muzzle;
    public void Aim(Vector3 worldDirection)
    {
        if (!worldDirection.IsFinite() || worldDirection.LengthSquared() < 1e-8f) return;
        _aimDirection = worldDirection.Normalized(); _aimUntil = _ship.SimTime + .12;
    }
    public bool Aligned(Vector3 worldDirection) => Mount is null
        || Direction.AngleTo(worldDirection) <= Mathf.DegToRad(Mount.ToleranceDegrees);
    public int Rounds { get; private set; }
    public float ReloadRemaining { get; private set; }
    public float Output => _ship.Damage.WeaponFraction(Definition.ModuleId);
    private float ReloadRate => Output * _ship.Power.WeaponEffect;
    public Vec3d MuzzlePosition => _ship.Position + Vec3d.From(_ship.Orientation * LocalMuzzle);
    public string Status => _ship.Damage.Destroyed ? "격침" : Output <= 0.01f ? "주포/전력/탄약고 손상"
        : Rounds <= 0 ? "탄약 소진" : _ship.Power.Overheated ? "과열 · 발사 불가"
        : ReloadRemaining > 0 ? ReloadRate <= 0.01f ? "재장전 대기 · 무장 전력 없음"
            : $"재장전 {ReloadRemaining / ReloadRate:0.0}s" : "발사 준비";
    public bool Ready => !_ship.Damage.Destroyed && Output > 0.01f && Rounds > 0 && ReloadRemaining <= 0 && !_ship.Power.Overheated;
    public void Reset()
    {
        Rounds = Definition.Rounds; ReloadRemaining = 0; Yaw = Elevation = PreviousYaw = PreviousElevation = 0;
        Barrel = LastBarrel = 0; LastFiredAt = double.NegativeInfinity; ShotCount = 0; _aimDirection = null;
    }
    internal void Step(double dt)
    {
        ReloadRemaining = Mathf.Max(0, ReloadRemaining - (float)dt * ReloadRate);
        if (Mount is not { } mount || Output <= .01f || _ship.Damage.Destroyed) return;
        Vector2 desired = _aimDirection is Vector3 direction && _ship.SimTime <= _aimUntil
            ? mount.Angles(_ship.Orientation.Inverse() * direction) : new Vector2(Yaw, Elevation);
        float yaw = Mathf.Clamp(desired.X, -Mathf.DegToRad(mount.YawDegrees), Mathf.DegToRad(mount.YawDegrees));
        float pitch = Mathf.Clamp(desired.Y, Mathf.DegToRad(mount.MinElevation), Mathf.DegToRad(mount.MaxElevation));
        float drive = Output * _ship.Power.WeaponEffect;
        // Deliberately do not wrap through the mechanical stop at +/- yaw limit.
        Yaw = Mathf.MoveToward(Yaw, yaw, Mathf.DegToRad(mount.YawRate) * (float)dt * drive);
        Elevation = Mathf.MoveToward(Elevation, pitch, Mathf.DegToRad(mount.ElevationRate) * (float)dt * drive);
    }
    internal void Consume()
    {
        Rounds--;
        ReloadRemaining = Definition.ReloadSeconds;
        _ship.Power.AddHeat(Definition.ShotHeatMj);
        LastBarrel = Barrel; Barrel = (Barrel + 1) % (Mount?.Muzzles.Length ?? 1);
        LastFiredAt = _ship.SimTime; ShotCount++;
    }
}

public sealed class RailProjectile
{
    public required uint Id { get; init; }
    public required ShipBody Shooter { get; init; }
    public string ModuleId { get; init; } = "";
    public int Barrel { get; init; }
    public required Vector3 Velocity { get; init; }
    public required DamagePacket Packet { get; init; }
    public required double Lifetime { get; init; }
    public required Vec3d Position { get; set; }
    public Vec3d PrevPosition { get; internal set; }
    public double Age { get; internal set; }
}

/// <summary>발사 실패 사유 코드. 문구(Reason)는 알림용이고 판단은 이 값으로 한다.</summary>
public enum FireFailure { None, NotInWorld, NoGun, NoDirection, NotReady, Arc, HullBlocked, Traversing }

public readonly record struct FireAttempt(bool Fired, string Reason, RailProjectile? Projectile = null, FireFailure Failure = FireFailure.None, int Shots = 0);
public sealed record ProjectileImpact(uint Id, ShipBody Shooter, ShotResult Hit, double Time)
{
    // 명중 순간의 연출 자료. 이후 표적 상태가 바뀌어도 이 탄의 결과는 바뀌지 않는다.
    public Vector3 IncomingDirection { get; init; }
    public float Energy { get; init; }
    public bool ShieldBroken { get; init; }
    public bool TargetDestroyed { get; init; }
    public BattleWeapon Weapon { get; init; }
}
