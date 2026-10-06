using System;
using Godot;

namespace SpaceFleet.Sim;

public enum OrderKind
{
    /// <summary>지휘함 주변 대형 자리(FormationOffset)를 지킨다.</summary>
    Escort,
    /// <summary>표적을 함종별 방식으로 공격한다.</summary>
    Attack,
    /// <summary>Point에 멈춰 선다.</summary>
    Hold,
}

public readonly record struct ShipOrder(OrderKind Kind, ShipBody? Target = null, Vec3d Point = default)
{
    public static ShipOrder EscortOf(ShipBody leader) => new(OrderKind.Escort, leader);
    public static ShipOrder AttackOn(ShipBody target) => new(OrderKind.Attack, target);
    public static ShipOrder HoldAt(Vec3d point) => new(OrderKind.Hold, null, point);
}

/// <summary>
/// 함종별 AI 기준값. 레일건은 탄 비행 시간이 RailFlightSeconds 이하일 때만 쏜다(먼 표적은 회피 기동으로 빗나간다).
/// </summary>
public readonly record struct AiProfile(float StandoffMeters, float RailFlightSeconds, float MissileRangeMeters, bool AttackRuns)
{
    public static AiProfile For(HullKind kind) => kind switch
    {
        // 미사일 사거리 > 레일건 유효 거리(전함 12 km/s × 8 s ≈ 96 km): 장거리 미사일전이 포격전보다 먼저 온다.
        HullKind.Battleship => new(60_000f, 8f, 140_000f, false),
        HullKind.Escort => new(30_000f, 5f, 110_000f, false),
        _ => new(6_000f, 2.5f, 15_000f, true),
    };
}

/// <summary>
/// 함선 한 척의 AI. 진영 센서망이 아는 정보만 쓴다(탐지되지 않은 적은 모른다).
/// SimWorld가 매 틱 조종 입력을 쓰고, 0.25초마다 판단(목표·전력 배분)을 갱신한다.
/// </summary>
public sealed class ShipBrain
{
    public ShipBrain(ShipBody ship, ShipOrder order)
    {
        Ship = ship;
        Order = order;
        Profile = AiProfile.For(ship.Class.Kind);
        _side = (Hash(ship.Callsign) & 1) == 0 ? 1f : -1f;
        DefaultPips = ship.Class.Kind == HullKind.Interceptor ? new[] { 2, 2, 2, 2, 0 } : new[] { 2, 2, 1, 1, 2 };
    }

    public ShipBody Ship { get; }
    public AiProfile Profile { get; }
    /// <summary>false면 조종 입력을 쓰지 않는다(연습 표적 등).</summary>
    public bool Enabled { get; set; } = true;
    public ShipOrder Order { get; set; }
    /// <summary>호위 명령의 대형 자리(지휘함 로컬 좌표, m).</summary>
    public Vector3 FormationOffset { get; set; }
    /// <summary>
    /// 기본 전력 배분(추진·실드·무장·센서·ECM). 위협이 없을 때 돌아간다.
    /// 전함·호위함은 ECM 2핍(무장·센서 1핍씩)을 켜고, 요격함은 끈다.
    /// </summary>
    public int[] DefaultPips { get; set; }

    /// <summary>지금 공격 중인 표적(공격 명령이면 Order.Target).</summary>
    public ShipBody? Target => Order.Kind == OrderKind.Attack ? Order.Target : null;
    /// <summary>HUD·검증용: 마지막 판단 요약.</summary>
    public string Activity { get; internal set; } = "";
    /// <summary>방해를 뚫으려고 센서 핍을 올린 상태.</summary>
    public bool Eccm { get; internal set; }

    internal double NextThink;
    internal Vector3 DesiredVelocity;
    internal bool WantBoost;
    internal int SalvoId = -1;
    internal int SalvoLaunched;
    internal double BreakUntil = double.NegativeInfinity;
    internal Vector3 BreakDirection;
    internal readonly float _side;

    internal static uint Hash(string text)
    {
        uint value = 2166136261;
        foreach (char c in text) { value ^= c; value *= 16777619; }
        return value;
    }
}
