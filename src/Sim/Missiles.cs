using System;
using Godot;

namespace SpaceFleet.Sim;

/// <summary>
/// 함선 미사일 데이터. 가속은 G, 거리 m, 시간 s.
/// 비례항법이 요구한 가속을 기수 선회·추력 편향 한계 안에서 실행하며, 연료가 떨어지면 관성으로 난다.
/// </summary>
public sealed record MissileDefinition(int Rounds, float ReloadSeconds, Vector3 LaunchPoint, float EjectSpeed,
    float AccelG, float BurnSeconds, float MaxFlightSeconds, float SeekerRangeMeters, float SeekerFovDegrees,
    float SeekerStrength, float FuzeMeters, float Energy, float PenetrationMm, float ModuleDamage, float HitPoints,
    float LaunchHeatMj)
{
    public DamagePacket Packet => new(Energy, PenetrationMm, ModuleDamage, Math.Max(FuzeMeters * 20f, 2000f));
    public float Accel => AccelG * ShipBody.StandardGravity;
    /// <summary>Body attitude slew, degrees/s. Flight-path curvature is also limited by available lateral thrust.</summary>
    public float TurnRateDegrees { get; init; } = 90f;
    /// <summary>Maximum commanded thrust angle from momentum; at most 90 degrees prevents commanded retro-thrust.</summary>
    public float MaxSteeringAngleDegrees { get; init; } = 90f;
    /// <summary>Engine nozzle deflection from the actual body attitude.</summary>
    public float ThrustGimbalDegrees { get; init; } = 10f;
    public bool CanInterceptMissiles { get; init; }
    public float InterceptRangeMeters { get; init; } = 12000;
    public float InterceptFuzeMeters { get; init; } = 24;
    public float InterceptDamage { get; init; } = 12;
    public float BodyScale { get; init; } = 1;
    public float SensorSignature { get; init; } = .65f;
    public float BurningSignature { get; init; } = 2f;
    public float LaunchSignatureFraction { get; init; }
    public float LaunchSignatureSeconds { get; init; }
    /// <summary>Launch cells that can fire at once (§21); null = the single stock tube.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? Cells { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public int CellCount => Cells ?? 1;
}

/// <summary>
/// 근접방어 포대. Mounts는 함선 로컬 위치이며 포대마다 독립적으로 사격한다.
/// Normals(로컬, 포대가 바라보는 방향)가 있으면 그 방향에서 ArcDegrees 안만 쏠 수 있다(선체가 만드는 사각).
/// YawDegrees는 포대 담당 방위 중심에서 좌우 한계, 고각은 갑판 기준, 구동 속도는 초당 도 단위다.
/// MuzzleSpeed(m/s)는 통계형 선행조준에 사용한다. TrackingErrorMeters는 기동 예측 오차의 명중률 보정 척도이며 실제 피탄 반경이 아니다.
/// </summary>
public sealed record PointDefenseDefinition(Vector3[] Mounts, float RangeMeters, float ShotsPerSecond, float HitChance, float DamagePerHit,
    Vector3[]? Normals = null, float ArcDegrees = 100f, float YawDegrees = 110f,
    float MinElevation = -5f, float MaxElevation = 85f, float YawRate = 60f,
    float ElevationRate = 45f, float ToleranceDegrees = 1.5f,
    float MuzzleSpeed = 6000f, float TrackingErrorMeters = 6f)
{
    // Legacy anti-ship rounds remain unchanged unless a replacement explicitly defines them.
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DamagePacket? ShipPacket { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public DamagePacket EffectiveShipPacket => ShipPacket ?? new DamagePacket(20f,25f,10f,5000f);
    // Missile bodies (including assault torpedoes) may use a different round
    // effect from drones/probes. Null retains the legacy damage and stock JSON.
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public float? MissileDamagePerHit { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public float EffectiveMissileDamage => MissileDamagePerHit ?? DamagePerHit;
}

/// <summary>디코이. 한 번에 PerLaunch개를 사출하며, 신호는 함선 기본 신호 × SignatureFactor에서 수명 동안 0으로 줄어든다.</summary>
public sealed record DecoyDefinition(int Count, int PerLaunch, float CooldownSeconds, float SignatureFactor, float LifetimeSeconds, float EjectSpeed);

/// <summary>함선의 미사일·디코이 잔량과 재장전. 재장전은 무장 채널 배율을 따른다.</summary>
public sealed class OrdnanceState
{
    private readonly ShipBody _ship;

    public OrdnanceState(ShipBody ship)
    {
        _ship = ship;
        Interceptor = new InterceptorEquipment(ship);
        Antimatter = new AntimatterState(ship);
        Drones = new DefenseDroneState(ship);
        PointDefense = new PointDefenseMountState[ship.Definition.PointDefense?.Mounts.Length ?? 0];
        for (int i = 0; i < PointDefense.Length; i++) PointDefense[i] = new PointDefenseMountState(ship.Definition.PointDefense!, i, ShipBrain.Hash(ship.Callsign));
        Reset();
    }

    public int Missiles { get; private set; }
    public InterceptorEquipment Interceptor { get; }
    public AntimatterState Antimatter { get; }
    public DefenseDroneState Drones { get; }
    public PointDefenseMountState[] PointDefense { get; }
    public float MissileReload { get; private set; }
    /// <summary>Cells holding a missile ready to fire. A single tube holds one; empty cells refill one per reload.</summary>
    public int LoadedCells { get; private set; }
    private int CellLimit => Math.Min(MissileDefinition?.CellCount ?? 1, Missiles);
    /// <summary>Lateral offset of the next firing cell (zero for a single tube) so a salvo leaves side by side.</summary>
    public Vector3 CellOffset => MissileDefinition is { CellCount: > 1 } d
        ? Vector3.Right * 2.5f * ((d.CellCount - LoadedCells) % d.CellCount - (d.CellCount - 1) * .5f) : Vector3.Zero;
    public double LastMissileLaunch { get; private set; } = double.NegativeInfinity;
    public int Decoys { get; private set; }
    public float DecoyCooldown { get; private set; }

    public MissileDefinition? MissileDefinition => _ship.Definition.Missiles;
    public DecoyDefinition? DecoyDefinition => _ship.Definition.Decoys;

    public string MissileStatus => MissileDefinition is null ? "발사관 없음" : _ship.Damage.Destroyed ? "격침"
        : _ship.Damage.MagazineFraction <= 0.01f ? "탄약고 손상" : Missiles <= 0 ? "미사일 소진"
        : _ship.Power.Overheated ? "과열 · 발사 불가" : LoadedCells <= 0 ? $"장전 {MissileReload:0.0}s"
        : MissileDefinition.CellCount > 1 ? $"발사 준비 · {LoadedCells}/{MissileDefinition.CellCount}셀" : "발사 준비";

    public bool MissileReady => MissileDefinition is not null && !_ship.Damage.Destroyed && _ship.Damage.MagazineFraction > 0.01f
        && Missiles > 0 && LoadedCells > 0 && !_ship.Power.Overheated;

    public bool DecoyReady => DecoyDefinition is not null && !_ship.Damage.Destroyed && Decoys > 0 && DecoyCooldown <= 0;

    public void Reset()
    {
        Missiles = MissileDefinition?.Rounds ?? 0;
        Decoys = DecoyDefinition?.Count ?? 0;
        MissileReload = 0;
        LoadedCells = CellLimit;
        LastMissileLaunch = double.NegativeInfinity;
        DecoyCooldown = 0;
        // Resetting the practice guns/repair tools cannot restock a spent or jettisoned AM payload.
        Antimatter.Cancel();
        Drones.Reset();
        foreach (var mount in PointDefense) mount.Reset();
    }

    internal void Step(double dt)
    {
        // Empty cells refill one at a time; a single tube behaves exactly as the old reload timer.
        if (LoadedCells < CellLimit)
        {
            MissileReload = Mathf.Max(0f, MissileReload - (float)dt * _ship.Power.WeaponEffect);
            if (MissileReload <= 0) { LoadedCells++; if (LoadedCells < CellLimit) MissileReload = MissileDefinition!.ReloadSeconds; }
        }
        else MissileReload = Mathf.Max(0f, MissileReload - (float)dt * _ship.Power.WeaponEffect);
        DecoyCooldown = Mathf.Max(0f, DecoyCooldown - (float)dt);
        Antimatter.Step(dt);
        Drones.Step(dt);
    }

    internal void ConsumeMissile()
    {
        Missiles--;
        LastMissileLaunch = _ship.SimTime;
        LoadedCells = Math.Max(0, LoadedCells - 1);
        if (MissileReload <= 0 || MissileDefinition!.CellCount == 1) MissileReload = MissileDefinition!.ReloadSeconds;
        LoadedCells = Math.Min(LoadedCells, CellLimit);
        _ship.Power.AddHeat(MissileDefinition.LaunchHeatMj);
    }

    internal int ConsumeDecoys()
    {
        int n = Math.Min(Decoys, DecoyDefinition!.PerLaunch);
        Decoys -= n;
        DecoyCooldown = DecoyDefinition.CooldownSeconds;
        return n;
    }
}

public sealed class Missile
{
    public ShotCredit Credit { get; init; }
    internal ShotCredit SupportCredit;
    internal double CreditFlightSeconds,CreditLinkSeconds;
    public required uint Id { get; init; }
    public required ShipBody Shooter { get; init; }
    public ShipBody? Target { get; init; }
    public Missile? InterceptTarget { get; init; }
    internal bool InterceptClosing, InterceptPassed;
    internal Vector3 LastAimVelocity;
    public required MissileDefinition Definition { get; init; }
    public BattleWeapon Weapon { get; init; } = BattleWeapon.Missile;
    public AntimatterDefinition? Assault { get; init; }
    public Vector3? LocalAim { get; init; }
    public Vector3 LaunchDirection { get; init; }
    /// <summary>Actual body/motor attitude, independent of momentum. AM uses its fixed launch direction.</summary>
    public Vector3 NoseDirection { get; internal set; }
    public Vector3 PreviousNoseDirection { get; internal set; }
    public double TravelMeters { get; internal set; }
    public double LaunchRange { get; init; }
    public double SeekerSeconds { get; internal set; }
    public float ClosestTargetHull { get; internal set; } = float.PositiveInfinity;
    public Faction Faction => Shooter.Faction;
    public Vec3d Position { get; internal set; }
    public Vec3d PrevPosition { get; internal set; }
    public Vector3 Velocity { get; internal set; }
    public double Age { get; internal set; }
    public float Health { get; internal set; }
    /// <summary>탐색기가 지금 쫓는 대상(함선 또는 디코이). null이면 센서망 중간 유도 중.</summary>
    public object? SeekerTarget { get; internal set; }
    /// <summary>마지막으로 향한 목표점. 센서망이 표적을 놓치면 여기로 계속 난다.</summary>
    public Vec3d AimPoint { get; internal set; }
    public bool Burning => Age < Definition.BurnSeconds && !InterceptPassed;
    public bool SeekerLocked => SeekerTarget is not null;
    public bool OnDecoy => SeekerTarget is Decoy;
    internal double NextSeekerCheck;
}

public sealed class Decoy
{
    public required uint Id { get; init; }
    public required ShipBody Owner { get; init; }
    public required DecoyDefinition Definition { get; init; }
    public Faction Faction => Owner.Faction;
    public Vec3d Position { get; internal set; }
    public Vec3d PrevPosition { get; internal set; }
    public Vector3 Velocity { get; internal set; }
    public double Age { get; internal set; }
    /// <summary>현재 신호: 함선 기본 신호 × 배율에서 수명 동안 선형으로 줄어든다.</summary>
    public float Signature => Owner.Definition.Sensors.Signature * Definition.SignatureFactor
        * Mathf.Max(0f, 1f - (float)(Age / Definition.LifetimeSeconds));
    public bool Expired => Age >= Definition.LifetimeSeconds;
}

public enum OrdnanceEventKind
{
    Detonation,
    Intercepted,
    Expired,
    ContainmentFailure,
    Jettisoned,
    DroneDestroyed,
    ProbeDestroyed,
}

public readonly record struct OrdnanceEvent(OrdnanceEventKind Kind, Vec3d Position, double Time, Faction Faction,
    BattleWeapon Weapon = BattleWeapon.Missile, Vector3 Direction = default);

/// <summary>근접방어 사격 한 발(연출용). Hit이면 미사일 체력을 깎았다.</summary>
public readonly record struct PointDefenseShot(Vec3d From, Vec3d To, double Time, bool Hit, Faction Faction);
