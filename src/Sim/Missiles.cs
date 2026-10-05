using System;
using Godot;

namespace SpaceFleet.Sim;

/// <summary>
/// 함선 미사일 데이터. 가속은 G, 거리 m, 시간 s.
/// 연소 중에는 늘 최대 가속으로 비례항법 + 남는 추력으로 가속하고, 연료가 떨어지면 관성으로 난다.
/// </summary>
public sealed record MissileDefinition(int Rounds, float ReloadSeconds, Vector3 LaunchPoint, float EjectSpeed,
    float AccelG, float BurnSeconds, float MaxFlightSeconds, float SeekerRangeMeters, float SeekerFovDegrees,
    float SeekerStrength, float FuzeMeters, float Energy, float PenetrationMm, float ModuleDamage, float HitPoints,
    float LaunchHeatMj)
{
    public DamagePacket Packet => new(Energy, PenetrationMm, ModuleDamage, Math.Max(FuzeMeters * 20f, 2000f));
    public float Accel => AccelG * ShipBody.StandardGravity;
}

/// <summary>근접방어 포대. Mounts는 함선 로컬 위치이며 포대마다 독립적으로 사격한다.</summary>
public sealed record PointDefenseDefinition(Vector3[] Mounts, float RangeMeters, float ShotsPerSecond, float HitChance, float DamagePerHit);

/// <summary>디코이. 한 번에 PerLaunch개를 사출하며, 신호는 함선 기본 신호 × SignatureFactor에서 수명 동안 0으로 줄어든다.</summary>
public sealed record DecoyDefinition(int Count, int PerLaunch, float CooldownSeconds, float SignatureFactor, float LifetimeSeconds, float EjectSpeed);

/// <summary>함선의 미사일·디코이 잔량과 재장전. 재장전은 무장 채널 배율을 따른다.</summary>
public sealed class OrdnanceState
{
    private readonly ShipBody _ship;

    public OrdnanceState(ShipBody ship)
    {
        _ship = ship;
        Reset();
    }

    public int Missiles { get; private set; }
    public float MissileReload { get; private set; }
    public int Decoys { get; private set; }
    public float DecoyCooldown { get; private set; }

    public MissileDefinition? MissileDefinition => _ship.Definition.Missiles;
    public DecoyDefinition? DecoyDefinition => _ship.Definition.Decoys;

    public string MissileStatus => MissileDefinition is null ? "발사관 없음" : _ship.Damage.Destroyed ? "격침"
        : _ship.Damage.MagazineFraction <= 0.01f ? "탄약고 손상" : Missiles <= 0 ? "미사일 소진"
        : _ship.Power.Overheated ? "과열 · 발사 불가" : MissileReload > 0 ? $"장전 {MissileReload:0.0}s" : "발사 준비";

    public bool MissileReady => MissileDefinition is not null && !_ship.Damage.Destroyed && _ship.Damage.MagazineFraction > 0.01f
        && Missiles > 0 && MissileReload <= 0 && !_ship.Power.Overheated;

    public bool DecoyReady => DecoyDefinition is not null && !_ship.Damage.Destroyed && Decoys > 0 && DecoyCooldown <= 0;

    public void Reset()
    {
        Missiles = MissileDefinition?.Rounds ?? 0;
        Decoys = DecoyDefinition?.Count ?? 0;
        MissileReload = 0;
        DecoyCooldown = 0;
    }

    internal void Step(double dt)
    {
        MissileReload = Mathf.Max(0f, MissileReload - (float)dt * _ship.Power.WeaponEffect);
        DecoyCooldown = Mathf.Max(0f, DecoyCooldown - (float)dt);
    }

    internal void ConsumeMissile()
    {
        Missiles--;
        MissileReload = MissileDefinition!.ReloadSeconds;
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
    public required uint Id { get; init; }
    public required ShipBody Shooter { get; init; }
    public required ShipBody Target { get; init; }
    public required MissileDefinition Definition { get; init; }
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
    public bool Burning => Age < Definition.BurnSeconds;
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
}

public readonly record struct OrdnanceEvent(OrdnanceEventKind Kind, Vec3d Position, double Time, Faction Faction);

/// <summary>근접방어 사격 한 발(연출용). Hit이면 미사일 체력을 깎았다.</summary>
public readonly record struct PointDefenseShot(Vec3d From, Vec3d To, double Time, bool Hit, Faction Faction);
