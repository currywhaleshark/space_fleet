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
    public int BarrelCount => Mount?.Muzzles.Length ?? 1;
    public int SalvoRounds => Math.Min(Rounds, BarrelCount);
    public int LastSalvoRounds { get; private set; }
    public double LastFiredAt { get; private set; } = double.NegativeInfinity;
    public uint ShotCount { get; private set; }
    private Vector3? _aimDirection;
    private double _aimUntil, _aimTime;
    /// <summary>요청 조준 방향의 각속도(월드, rad/s). 움직이는 표적을 따라갈 때 포탑을 한 틱 앞으로 미리 돌린다.</summary>
    private Vector3 _aimRate;
    public Vector3 LocalDirection => Mount?.AimBasis(Yaw, Elevation) * Vector3.Forward ?? Vector3.Forward;
    public Vector3 Direction => _ship.Orientation * LocalDirection;
    // Fire control solves from the battery centre; projectiles leave individual physical muzzles.
    public Vector3 LocalMuzzle => Mount is { } mount
        ? (mount.Muzzle(Yaw, Elevation, 0) + mount.Muzzle(Yaw, Elevation, BarrelCount - 1)) * .5f : Definition.Muzzle;
    public Vec3d BarrelPosition(int barrel) => _ship.Position
        + Vec3d.From(_ship.Orientation * (Mount?.Muzzle(Yaw, Elevation, barrel) ?? Definition.Muzzle));
    public void Aim(Vector3 worldDirection)
    {
        if (!worldDirection.IsFinite() || worldDirection.LengthSquared() < 1e-8f) return;
        Vector3 next = worldDirection.Normalized();
        double now = _ship.SimTime, dt = now - _aimTime;
        if (_aimDirection is Vector3 previous && dt > 1e-6)
        {
            // 같은 표적을 계속 따라가는 중이면 조준 방향이 도는 속도를 잰다. 오래 끊겼거나 크게 튀면 새 표적으로 본다.
            float angle = previous.AngleTo(next);
            Vector3 axis = previous.Cross(next);
            _aimRate = dt > .25 || angle > Mathf.DegToRad(10) || axis.LengthSquared() < 1e-12f
                ? Vector3.Zero : axis.Normalized() * angle / (float)dt;
        }
        else if (_aimDirection is null) _aimRate = Vector3.Zero;
        _aimDirection = next; _aimTime = now; _aimUntil = now + .12;
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
        LastSalvoRounds = 0; LastFiredAt = double.NegativeInfinity; ShotCount = 0; _aimDirection = null; _aimRate = Vector3.Zero;
    }
    internal void Step(double dt)
    {
        float output = Output;
        ReloadRemaining = Mathf.Max(0, ReloadRemaining - (float)dt * (output * _ship.Power.WeaponEffect));
        if (Mount is not { } mount || output <= .01f || _ship.Damage.Destroyed) return;
        // 추적 앞당김: 요청 방향을 조준 각속도만큼 지금 시각까지 돌려서 겨눈다. 다음 틱 정렬 판정 때 포탑이 이미 그 방향에 있다.
        // 그래서 따라갈 수 있는 한계는 포탑 구동 속도(YawRate·ElevationRate)다.
        Vector2 desired = _aimDirection is Vector3 direction && _ship.SimTime <= _aimUntil
            ? mount.Angles(_ship.Orientation.Inverse() * Lead(direction)) : new Vector2(Yaw, Elevation);
        float yaw = Mathf.Clamp(desired.X, -Mathf.DegToRad(mount.YawDegrees), Mathf.DegToRad(mount.YawDegrees));
        float pitch = Mathf.Clamp(desired.Y, Mathf.DegToRad(mount.MinElevation), Mathf.DegToRad(mount.MaxElevation));
        float drive = output * _ship.Power.WeaponEffect;
        // Deliberately do not wrap through the mechanical stop at +/- yaw limit.
        Yaw = Mathf.MoveToward(Yaw, yaw, Mathf.DegToRad(mount.YawRate) * (float)dt * drive);
        Elevation = Mathf.MoveToward(Elevation, pitch, Mathf.DegToRad(mount.ElevationRate) * (float)dt * drive);
    }
    private Vector3 Lead(Vector3 direction)
    {
        float angle = _aimRate.Length() * (float)(_ship.SimTime - _aimTime);
        return angle > 1e-7f ? direction.Rotated(_aimRate.Normalized(), angle) : direction;
    }
    internal void Consume(int rounds)
    {
        Rounds -= rounds;
        ReloadRemaining = Definition.ReloadSeconds;
        _ship.Power.AddHeat(Definition.ShotHeatMj * rounds);
        LastSalvoRounds = rounds;
        LastFiredAt = _ship.SimTime; ShotCount += (uint)rounds;
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
    public Vector3 TargetVelocity { get; init; }
    public Quaternion TargetOrientation { get; init; } = Quaternion.Identity;
}
