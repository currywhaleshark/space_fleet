using System;
using Godot;

namespace SpaceFleet.Sim;

public enum PowerChannel
{
    Engines,
    Shields,
    Weapons,
    Sensors,
    /// <summary>전자전 방해. 기본은 꺼짐(핍 0)이며, 다른 채널에서 핍을 가져와 켠다.</summary>
    Ecm,
}

/// <summary>
/// 함선 전력·열 데이터(MW, MJ). 채널 값은 핍 2개(기준)일 때 최대 활동의 소비 전력이다.
/// 기준 배분의 합은 정격 발전량보다 작게 잡아(약 80%) 극단 배분에도 건강한 함선은 전압 강하가 드물게 한다.
/// </summary>
public sealed record PowerDefinition(float OutputMw, float Engines, float Shields, float Weapons, float Sensors, float Ecm,
    float HeatCapacityMj, float CoolingMw)
{
    public float Nominal(PowerChannel channel) => channel switch
    {
        PowerChannel.Engines => Engines,
        PowerChannel.Shields => Shields,
        PowerChannel.Weapons => Weapons,
        PowerChannel.Sensors => Sensors,
        _ => Ecm,
    };
}

/// <summary>
/// 전력 배분과 폐열. 플레이어는 핍 8개를 5채널에 0~4개씩 나눈다(기본 2/2/2/2, ECM 0).
/// - 핍 수가 채널 성능 배율과 소비 전력을 정한다(2핍 = 기준 1.0).
/// - 채널은 실제로 일할 때만 전력을 끈다(추진 = 가속 중, 실드 = 재충전 중, 무장 = 재장전 중, 센서 = 항상, ECM = 켜져 있는 동안).
/// - 수요가 가용 발전량을 넘으면 모든 채널이 같은 비율로 깎인다(전압 강하).
/// - 소비한 전력은 모두 열이 되고 레일건 발사는 열을 순간적으로 더한다. 냉각 모듈이 열을 빼낸다.
/// - 열 용량을 넘으면 과열: 모든 채널 절반, 레일건 발사 불가. 70% 아래로 식어야 회복한다.
/// </summary>
public sealed class ShipPower
{
    public const int ChannelCount = 5;
    public const int MaxPips = 4;
    public const int TotalPips = 8;
    public const int BalancedPips = 2;
    /// <summary>쉬고 있는 채널이 끄는 대기 전력 비율.</summary>
    public const float IdleActivity = 0.1f;
    public const float OverheatThrottle = 0.5f;
    /// <summary>과열 후 이 비율 아래로 식어야 출력 제한이 풀린다.</summary>
    public const float RecoverHeatFraction = 0.7f;
    /// <summary>열은 용량의 이 배수까지만 쌓인다(순간 발열이 무한히 쌓이지 않게).</summary>
    public const float HeatCeiling = 1.25f;
    /// <summary>
    /// 보조 추진기(횡·상하·제동·후진)가 같은 가속을 낼 때의 전력 단가, 메인 추진 대비.
    /// 비행보조의 미끄럼 보정은 작은 기수 조작에도 횡추력을 순간 최대로 쓰므로, 메인과 같은 단가면 전력이 튄다.
    /// </summary>
    public const float RcsPowerFactor = 0.3f;

    private static readonly float[] EffectByPips = { 0.3f, 0.65f, 1f, 1.25f, 1.5f };
    private static readonly float[] DrawByPips = { 0.15f, 0.5f, 1f, 1.5f, 2f };
    // ECM은 0핍이면 완전히 꺼진다(대기 전력도 없다).
    private static readonly float[] EcmEffectByPips = { 0f, 0.65f, 1f, 1.25f, 1.5f };
    private static readonly float[] EcmDrawByPips = { 0f, 0.5f, 1f, 1.5f, 2f };
    private static readonly int[] DefaultPips = { BalancedPips, BalancedPips, BalancedPips, BalancedPips, 0 };

    private readonly ShipBody _ship;
    private readonly int[] _pips = new int[ChannelCount];
    private readonly float[] _draw = new float[ChannelCount];

    public ShipPower(ShipBody ship)
    {
        _ship = ship;
        Reset();
    }

    private PowerDefinition Definition => _ship.Definition.Power;

    /// <summary>지금 낼 수 있는 발전량(MW). 발전 모듈 손상과 전력망 연결에 따라 줄어든다.</summary>
    public float AvailableMw { get; private set; }
    /// <summary>채널들이 요청한 전력 합(MW).</summary>
    public float DemandMw { get; private set; }
    /// <summary>요청 대비 실제 공급 비율. 1이면 전압 강하 없음.</summary>
    public float Supply { get; private set; } = 1f;
    public float HeatMj { get; private set; }
    public float HeatFraction => HeatMj / Definition.HeatCapacityMj;
    public bool Overheated { get; private set; }
    /// <summary>지금 빼낼 수 있는 열(MW). 냉각 모듈 손상에 따라 줄어든다.</summary>
    public float CoolingMw => Definition.CoolingMw * _ship.Damage.CoolingFraction;
    /// <summary>이번 틱에 실제로 소비한 전력 합 = 발열(MW, 레일건 순간 발열 제외).</summary>
    public float HeatInMw { get; private set; }

    public int Pips(PowerChannel channel) => _pips[(int)channel];
    /// <summary>채널의 실제 소비 전력(MW, 전압 강하 반영).</summary>
    public float DrawMw(PowerChannel channel) => _draw[(int)channel];
    /// <summary>채널을 4핍으로 최대 활동시킬 때의 소비 전력(MW). HUD 눈금용(부스트 중 추진은 넘을 수 있다).</summary>
    public float MaxDrawMw(PowerChannel channel) => Definition.Nominal(channel) * DrawByPips[MaxPips];

    /// <summary>채널 성능 배율. 핍 배율 × 공급 비율 × 과열 제한.</summary>
    public float Effect(PowerChannel channel) => _ship.Damage.Destroyed ? 0f
        : (channel == PowerChannel.Ecm ? EcmEffectByPips : EffectByPips)[_pips[(int)channel]] * Supply * (Overheated ? OverheatThrottle : 1f);

    public float EngineEffect => Effect(PowerChannel.Engines);
    public float ShieldEffect => Effect(PowerChannel.Shields);
    public float WeaponEffect => Effect(PowerChannel.Weapons);
    public float SensorEffect => Effect(PowerChannel.Sensors);
    public float EcmEffect => Effect(PowerChannel.Ecm);
    public bool EcmActive => _pips[(int)PowerChannel.Ecm] > 0 && EcmEffect > 0.01f;

    public void Reset()
    {
        ResetPips();
        HeatMj = 0f;
        Overheated = false;
        Supply = 1f;
        Array.Clear(_draw);
    }

    /// <summary>기본 배분: 추진·실드·무장·센서 2개씩, ECM 꺼짐.</summary>
    public void ResetPips() => Array.Copy(DefaultPips, _pips, ChannelCount);

    /// <summary>채널에 핍 하나를 더한다. 다른 채널 중 핍이 가장 많은 곳에서 하나 가져온다.</summary>
    public bool AddPip(PowerChannel channel)
    {
        int target = (int)channel;
        if (_pips[target] >= MaxPips)
            return false;
        int donor = -1;
        for (int i = 0; i < ChannelCount; i++)
            if (i != target && _pips[i] > 0 && (donor < 0 || _pips[i] > _pips[donor]))
                donor = i;
        if (donor < 0)
            return false;
        _pips[donor]--;
        _pips[target]++;
        return true;
    }

    /// <summary>배분을 직접 지정한다(AI·검증용). 합 8, 채널당 0~4.</summary>
    public void SetPips(int engines, int shields, int weapons, int sensors, int ecm = 0)
    {
        int[] next = { engines, shields, weapons, sensors, ecm };
        int sum = 0;
        foreach (int p in next)
        {
            if (p < 0 || p > MaxPips)
                throw new ArgumentOutOfRangeException(nameof(engines), "Pips must be 0..4");
            sum += p;
        }
        if (sum != TotalPips)
            throw new ArgumentException("Pips must sum to 8");
        Array.Copy(next, _pips, ChannelCount);
    }

    internal void AddHeat(float mj) => HeatMj = Mathf.Min(HeatMj + mj, Definition.HeatCapacityMj * HeatCeiling);

    internal void Step(double dt)
    {
        PowerDefinition def = Definition;
        ShipDamage damage = _ship.Damage;
        AvailableMw = damage.Destroyed ? 0f : def.OutputMw * damage.GenerationFraction;

        // 채널 활동도: 실제로 일하는 정도. 추진은 직전 틱의 가속으로 정한다(부스트면 1을 넘는다).
        // 전방 가속은 메인 추진, 나머지(횡·상하·제동·후진)는 보조 추진기 단가(RcsPowerFactor)로 센다.
        Vector3 a = _ship.LocalAcceleration;
        float main = Mathf.Max(0f, -a.Z);
        float rcs = new Vector3(a.X, a.Y, Mathf.Max(0f, a.Z)).Length();
        float engines = Mathf.Clamp((main + RcsPowerFactor * rcs) / Mathf.Max(_ship.Class.ForwardAccel, 1e-3f), 0f, 2f);
        float shields = damage.ShieldRecharging ? 1f : IdleActivity;
        float weapons = System.Array.Exists(_ship.Railguns, gun => gun.ReloadRemaining > 0f) ? 1f : IdleActivity;
        if (_ship.Ordnance.Antimatter.Mode is AntimatterMode.Arming or AntimatterMode.Armed) weapons = 1f;
        Span<float> activity = stackalloc float[] { Mathf.Max(engines, IdleActivity), shields, weapons, 1f, 1f };

        float demand = 0f;
        for (int i = 0; i < ChannelCount; i++)
        {
            float[] draws = i == (int)PowerChannel.Ecm ? EcmDrawByPips : DrawByPips;
            _draw[i] = def.Nominal((PowerChannel)i) * draws[_pips[i]] * activity[i];
            demand += _draw[i];
        }
        DemandMw = demand;
        Supply = demand > 1e-6f ? Mathf.Min(1f, AvailableMw / demand) : AvailableMw > 0f ? 1f : 0f;

        float heatIn = 0f;
        for (int i = 0; i < ChannelCount; i++)
        {
            _draw[i] *= Supply;
            heatIn += _draw[i];
        }
        HeatInMw = heatIn;
        HeatMj = Mathf.Clamp(HeatMj + (heatIn - CoolingMw) * (float)dt, 0f, def.HeatCapacityMj * HeatCeiling);

        double time = _ship.SimTime;
        if (!Overheated && HeatMj >= def.HeatCapacityMj)
        {
            Overheated = true;
            damage.Report(time, "과열 · 출력 제한");
        }
        else if (Overheated && HeatMj <= def.HeatCapacityMj * RecoverHeatFraction)
        {
            Overheated = false;
            damage.Report(time, "냉각 회복");
        }
    }
}
