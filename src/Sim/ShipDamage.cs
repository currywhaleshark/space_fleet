using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

public sealed class ModuleState
{
    public ModuleState(ModuleDefinition definition) { Definition = definition; Health = definition.HitPoints; }
    public ModuleDefinition Definition { get; }
    public float Health { get; internal set; }
    public float HealthFraction => Health / Definition.HitPoints;
    public bool Destroyed => Health <= 0f;
    /// <summary>마지막으로 피해를 받은 시뮬레이션 시각. HUD 강조용.</summary>
    public double LastHitTime { get; internal set; } = double.NegativeInfinity;
}
public readonly record struct DamageReport(double Time, string Message);

/// <summary>실드·모듈과 전력 계통의 상태. 수치는 프로토타입용 게임 단위이며 노드에 의존하지 않는다.</summary>
public sealed class ShipDamage
{
    /// <summary>발전 모듈은 이 체력 비율 미만에서 출력이 절반으로 떨어진다(그 위로는 정격 출력).</summary>
    public const float GeneratorDegradeThreshold = 0.5f;

    private readonly ShipDefinition _definition;
    private readonly uint _seed;
    private readonly Dictionary<string, ModuleState> _byId;
    private readonly Dictionary<int, ModuleState> _engines;
    private readonly List<DamageReport> _reports = new();
    private double _sinceHit;
    private float _portPower = 1f, _starboardPower = 1f;
    private bool _catastrophic;
    private bool _allDestroyed;

    public ShipDamage(ShipDefinition definition, string callsign)
    {
        _definition = definition;
        _seed = Hash(callsign);
        Modules = Array.AsReadOnly(definition.Modules.Select(m => new ModuleState(m)).ToArray());
        _byId = Modules.ToDictionary(m => m.Definition.Id);
        _engines = Modules.Where(m => m.Definition.Kind == ModuleKind.Thruster && m.Definition.VisualEngineIndex >= 0)
            .ToDictionary(m => m.Definition.VisualEngineIndex);
        Reset();
    }

    public IReadOnlyList<ModuleState> Modules { get; }
    public IReadOnlyList<DamageReport> Reports => _reports;
    public float Shield { get; private set; }
    public float ShieldCapacity { get; private set; }
    /// <summary>살아 있는 전력망 비율(좌·우현 각 0 또는 1의 평균). 발전량은 GenerationFraction.</summary>
    public float PowerFraction => (_portPower + _starboardPower) * 0.5f;
    /// <summary>
    /// 정격 대비 현재 발전량. 발전 모듈 출력 단계(정격·50%·0)와, 연결된 전력망이 하나라도 있는지로 정한다.
    /// 실제 배분과 전압 강하는 ShipPower가 계산한다.
    /// </summary>
    public float GenerationFraction { get; private set; }
    /// <summary>실드가 피격 지연을 지나 재충전 중인가. 전력 수요 계산용.</summary>
    public bool ShieldRecharging => !Destroyed && Shield < ShieldCapacity && _sinceHit >= _definition.Shield.RechargeDelay;
    public float PropulsionFraction { get; private set; }
    public float ManeuverFraction { get; private set; }
    public float WeaponsFraction { get; private set; }
    public float SensorFraction { get; private set; }
    public float CoolingFraction { get; private set; }
    /// <summary>탄약고 평균 상태(전력 무관). 미사일 발사 가능 여부에 쓴다.</summary>
    public float MagazineFraction { get; private set; }
    public bool Destroyed => _catastrophic || _allDestroyed;
    /// <summary>
    /// 무력화: 격침은 아니지만 발전이 없거나 추진·자세 제어를 모두 잃어 스스로 움직일 수 없다. 관성으로 떠다닌다.
    /// </summary>
    public bool Disabled => !Destroyed && (GenerationFraction <= 0.001f || (PropulsionFraction <= 0.001f && ManeuverFraction <= 0.001f));
    /// <summary>실드가 마지막으로 에너지를 흡수한 시뮬레이션 시각. HUD 강조용.</summary>
    public double LastShieldHitTime { get; private set; } = double.NegativeInfinity;
    public ModuleState Module(string id) => _byId[id];
    public float WeaponFraction(string id) => Destroyed ? 0 : _byId[id].HealthFraction
        * GridPower(_byId[id].Definition.Grid) * Average(ModuleKind.Magazine, powered: false);

    /// <summary>
    /// 해당 전력망의 연결 상태(1 = 버스가 살아 있고 급전하는 발전 모듈이 있다, 0 = 정전).
    /// 양현 공용 장비(Shared)는 살아 있는 쪽 전력망에 붙는다. 전력량 자체는 ShipPower의 채널 배율이 정한다.
    /// </summary>
    public float GridPower(PowerGrid grid) => grid switch
    {
        PowerGrid.Port => _portPower,
        PowerGrid.Starboard => _starboardPower,
        _ => Mathf.Max(_portPower, _starboardPower),
    };

    /// <summary>발전 모듈의 현재 출력 비율: 정격 1, 출력 저하 0.5, 파괴 0.</summary>
    public static float GeneratorOutput(ModuleState module) =>
        module.Destroyed ? 0f : module.HealthFraction < GeneratorDegradeThreshold ? 0.5f : 1f;
    public float EngineFraction(int visualIndex)
    {
        return !_engines.TryGetValue(visualIndex, out ModuleState? engine) ? PropulsionFraction
            : engine.HealthFraction * GridPower(engine.Definition.Grid);
    }

    public void Reset()
    {
        foreach (ModuleState module in Modules)
        {
            module.Health = module.Definition.HitPoints;
            module.LastHitTime = double.NegativeInfinity;
        }
        LastShieldHitTime = double.NegativeInfinity;
        _catastrophic = false;
        _sinceHit = 0;
        _reports.Clear();
        Recompute();
        Shield = ShieldCapacity;
    }

    /// <param name="rechargeScale">실드 채널 성능 배율(전력 배분·전압 강하·과열 반영).</param>
    public void Step(double dt, float rechargeScale = 1f)
    {
        double previous = _sinceHit;
        _sinceHit += dt;
        double rechargeTime = Math.Max(0, _sinceHit - _definition.Shield.RechargeDelay)
            - Math.Max(0, previous - _definition.Shield.RechargeDelay);
        if (!Destroyed && rechargeTime > 0)
            Shield = Mathf.Min(ShieldCapacity, Shield + (float)rechargeTime * _definition.Shield.RechargePerSecond * rechargeScale);
    }

    internal float AbsorbShield(float energy, double time)
    {
        _sinceHit = 0;
        float absorbed = Mathf.Min(Shield, energy);
        Shield -= absorbed;
        if (absorbed > 0)
        {
            LastShieldHitTime = time;
            Report(time, Shield <= 0f ? "실드 소진" : $"실드 흡수 {absorbed:0} · 잔량 {Shield:0}");
        }
        return energy - absorbed;
    }

    internal float Hurt(ModuleState module, float amount, double time, float energy, uint shotSequence)
    {
        if (Destroyed || module.Destroyed || amount <= 0f) return 0f;
        _sinceHit = 0;
        bool generator = module.Definition.Kind is ModuleKind.Reactor or ModuleKind.Generator;
        bool wasRated = module.HealthFraction >= GeneratorDegradeThreshold;
        float damage = Mathf.Min(amount, module.Health);
        module.Health -= damage;
        module.LastHitTime = time;
        if (module.Destroyed)
        {
            Report(time, module.Definition.Kind == ModuleKind.PowerBus
                ? module.Definition.Name + " 단절" : module.Definition.Name + " 파괴");
            if (module.Definition.CriticalChance > 0 && energy >= module.Definition.CriticalEnergy
                && Roll(_seed ^ Hash(module.Definition.Id) ^ shotSequence) < module.Definition.CriticalChance)
            {
                _catastrophic = true;
                foreach (ModuleState state in Modules) state.Health = 0;
                Shield = 0;
                Report(time, module.Definition.Kind == ModuleKind.Magazine ? "탄약고 유폭 · 함선 격침" : "반응로 폭주 · 함선 격침");
            }
        }
        else if (generator && wasRated && module.HealthFraction < GeneratorDegradeThreshold)
            Report(time, $"{module.Definition.Name} 출력 저하 · 50%");
        else Report(time, $"{module.Definition.Name} 손상 · {module.HealthFraction * 100:0}%");
        Recompute();
        return damage;
    }

    /// <summary>충돌 에너지가 선체 구조 한계를 넘었을 때. 모든 모듈을 잃고 잔해로 남는다.</summary>
    internal void Breakup(double time, string other)
    {
        if (Destroyed) return;
        _sinceHit = 0;
        _catastrophic = true;
        foreach (ModuleState state in Modules)
        {
            state.Health = 0;
            state.LastHitTime = time;
        }
        Shield = 0;
        Report(time, $"충돌 · 선체 붕괴 ({other})");
        Recompute();
    }

    internal void Report(double time, string message)
    {
        _reports.Add(new DamageReport(time, message));
        if (_reports.Count > 6) _reports.RemoveAt(0);
    }

    private void Recompute()
    {
        bool wasDisabled = Disabled;
        RecomputeSystems();
        if (!wasDisabled && Disabled && _reports.Count > 0)
            Report(_reports[^1].Time, "전원·추진 상실 · 무력화");
    }

    private void RecomputeSystems()
    {
        _allDestroyed = Modules.All(m => m.Destroyed);
        _portPower = PowerFor(PowerGrid.Port);
        _starboardPower = PowerFor(PowerGrid.Starboard);
        GenerationFraction = Generation();
        PropulsionFraction = Average(ModuleKind.Thruster, powered: true);
        ManeuverFraction = Average(ModuleKind.ManeuverThruster, powered: true);
        WeaponsFraction = Average(ModuleKind.Gun, powered: true) * Average(ModuleKind.Magazine, powered: false);
        SensorFraction = Average(ModuleKind.Sensor, powered: true);
        CoolingFraction = Average(ModuleKind.Cooling, powered: true);
        MagazineFraction = Average(ModuleKind.Magazine, powered: false);
        ShieldCapacity = _definition.Shield.Capacity * Average(ModuleKind.ShieldEmitter, powered: true);
        Shield = Mathf.Min(Shield, ShieldCapacity);
    }

    /// <summary>전력망 연결: 버스가 살아 있고, 그 망에 급전하는 발전 모듈이 하나라도 살아 있으면 1.</summary>
    private float PowerFor(PowerGrid grid)
    {
        if (Destroyed || !BusAlive(grid)) return 0;
        bool hasGenerator = false, generatorAlive = false;
        foreach (ModuleState m in Modules)
        {
            if (!IsGenerator(m) || !Feeds(m, grid)) continue;
            hasGenerator = true;
            generatorAlive |= !m.Destroyed;
        }
        return !hasGenerator || generatorAlive ? 1f : 0f;
    }

    /// <summary>정격 대비 발전량. 살아 있는 전력망에 급전할 수 있는 발전 모듈의 출력 단계만 센다.</summary>
    private float Generation()
    {
        if (Destroyed) return 0;
        float maximum = 0, current = 0;
        foreach (ModuleState m in Modules)
        {
            if (!IsGenerator(m)) continue;
            maximum += m.Definition.Capacity;
            bool connected = (Feeds(m, PowerGrid.Port) && BusAlive(PowerGrid.Port))
                || (Feeds(m, PowerGrid.Starboard) && BusAlive(PowerGrid.Starboard));
            if (connected)
                current += m.Definition.Capacity * GeneratorOutput(m);
        }
        return maximum > 0 ? current / maximum : PowerFraction;
    }

    private bool BusAlive(PowerGrid grid)
    {
        bool hasBus = false, alive = false;
        foreach (ModuleState m in Modules)
        {
            if (m.Definition.Kind != ModuleKind.PowerBus || (m.Definition.Grid != grid && m.Definition.Grid != PowerGrid.Shared)) continue;
            hasBus = true;
            alive |= !m.Destroyed;
        }
        return !hasBus || alive;
    }

    private static bool IsGenerator(ModuleState m) => m.Definition.Kind is ModuleKind.Reactor or ModuleKind.Generator;

    private static bool Feeds(ModuleState m, PowerGrid grid) =>
        m.Definition.Feeds is null || m.Definition.Feeds.Contains(grid) || m.Definition.Feeds.Contains(PowerGrid.Shared);

    private float Average(ModuleKind kind, bool powered)
    {
        float sum = 0; int count = 0;
        foreach (ModuleState m in Modules)
        {
            if (m.Definition.Kind != kind) continue;
            sum += m.HealthFraction * (powered ? GridPower(m.Definition.Grid) : 1f);
            count++;
        }
        return Destroyed ? 0f : count > 0 ? sum / count : 1f;
    }

    private static uint Hash(string value)
    {
        uint result = 2166136261;
        foreach (char c in value) { result ^= c; result *= 16777619; }
        return result;
    }
    private static float Roll(uint value)
    {
        value ^= value >> 16; value *= 0x7feb352d; value ^= value >> 15;
        value *= 0x846ca68b; value ^= value >> 16;
        return (value & 0xffffff) / 16777216f;
    }
}
