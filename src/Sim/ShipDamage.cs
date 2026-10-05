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
    public float PowerFraction => (_portPower + _starboardPower) * 0.5f;
    public float PropulsionFraction { get; private set; }
    public float ManeuverFraction { get; private set; }
    public float WeaponsFraction { get; private set; }
    public float SensorFraction { get; private set; }
    public float CoolingFraction { get; private set; }
    public bool Destroyed => _catastrophic || _allDestroyed;
    /// <summary>실드가 마지막으로 에너지를 흡수한 시뮬레이션 시각. HUD 강조용.</summary>
    public double LastShieldHitTime { get; private set; } = double.NegativeInfinity;
    public ModuleState Module(string id) => _byId[id];
    public float WeaponFraction(string id) => Destroyed ? 0 : _byId[id].HealthFraction
        * GridPower(_byId[id].Definition.Grid) * Average(ModuleKind.Magazine, powered: false);

    public float GridPower(PowerGrid grid) => grid switch
    {
        PowerGrid.Port => _portPower,
        PowerGrid.Starboard => _starboardPower,
        _ => PowerFraction,
    };
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

    public void Step(double dt)
    {
        double previous = _sinceHit;
        _sinceHit += dt;
        double rechargeTime = Math.Max(0, _sinceHit - _definition.Shield.RechargeDelay)
            - Math.Max(0, previous - _definition.Shield.RechargeDelay);
        if (!Destroyed && rechargeTime > 0)
            Shield = Mathf.Min(ShieldCapacity, Shield + (float)rechargeTime * _definition.Shield.RechargePerSecond * PowerFraction * CoolingFraction);
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
        else Report(time, $"{module.Definition.Name} 손상 · {module.HealthFraction * 100:0}%");
        Recompute();
        return damage;
    }

    internal void ApplyCollision(Vector3 localPoint, float deltaSpeed, double time)
    {
        if (deltaSpeed <= 20f || Destroyed) return;
        _sinceHit = 0;
        float damage = (deltaSpeed - 20f) * (deltaSpeed - 20f) * 0.006f;
        ModuleState[] nearest = Modules.Where(m => !m.Destroyed)
            .OrderBy(m => m.Definition.Center.DistanceSquaredTo(localPoint)).Take(3).ToArray();
        foreach (ModuleState module in nearest)
            Hurt(module, damage / Math.Max(1, nearest.Length), time, damage, (uint)(time * 60));
    }

    internal void Report(double time, string message)
    {
        _reports.Add(new DamageReport(time, message));
        if (_reports.Count > 6) _reports.RemoveAt(0);
    }

    private void Recompute()
    {
        _allDestroyed = Modules.All(m => m.Destroyed);
        _portPower = PowerFor(PowerGrid.Port);
        _starboardPower = PowerFor(PowerGrid.Starboard);
        PropulsionFraction = Average(ModuleKind.Thruster, powered: true);
        ManeuverFraction = Average(ModuleKind.ManeuverThruster, powered: true);
        WeaponsFraction = Average(ModuleKind.Gun, powered: true) * Average(ModuleKind.Magazine, powered: false);
        SensorFraction = Average(ModuleKind.Sensor, powered: true);
        CoolingFraction = Average(ModuleKind.Cooling, powered: true);
        ShieldCapacity = _definition.Shield.Capacity * Average(ModuleKind.ShieldEmitter, powered: true);
        Shield = Mathf.Min(Shield, ShieldCapacity);
    }

    private float PowerFor(PowerGrid grid)
    {
        float maximum = 0, current = 0;
        bool hasBus = false, busAlive = false;
        foreach (ModuleState m in Modules)
        {
            if (m.Definition.Kind == ModuleKind.PowerBus && (m.Definition.Grid == grid || m.Definition.Grid == PowerGrid.Shared))
            { hasBus = true; busAlive |= !m.Destroyed; }
            if (m.Definition.Kind is not (ModuleKind.Reactor or ModuleKind.Generator)) continue;
            if (m.Definition.Feeds is not null && !m.Definition.Feeds.Contains(grid) && !m.Definition.Feeds.Contains(PowerGrid.Shared)) continue;
            maximum += m.Definition.Capacity;
            current += m.Definition.Capacity * m.HealthFraction;
        }
        if (Destroyed || (hasBus && !busAlive)) return 0;
        return maximum > 0 ? current / maximum : 1f;
    }

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
