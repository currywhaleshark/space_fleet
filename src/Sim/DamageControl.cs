using System;

namespace SpaceFleet.Sim;

public enum RepairPriority { Automatic, Power, Propulsion, Weapons, Sensors, Defense, Cooling, Hold }

/// <summary>One emergency crew, finite replacement/bypass kits, and a partial function ceiling.</summary>
public sealed record DamageControlDefinition(int Kits = 0, float RepairSeconds = 18,
    float RestoreFraction = .35f, float QuietSeconds = 5);

public sealed class DamageControlState
{
    public const double RecoveryGraceSeconds = 60;
    private readonly ShipBody _ship;
    private readonly DamageControlDefinition _definition;
    private readonly double[] _work;
    private uint _resetVersion;
    private double _nextSelection, _disabledSince = double.NaN;
    private int _active = -1;
    private RepairPriority _priority;

    public DamageControlState(ShipBody ship)
    {
        _ship = ship;
        _definition = ship.Definition.DamageControl;
        _work = new double[ship.Damage.Modules.Count];
        ship.Damage.ModuleDamaged += OnHit;
        Reset();
    }

    public int Kits { get; private set; }
    public int Capacity => _definition.Kits;
    public int Completed { get; private set; }
    public ModuleState? ActiveModule => _active < 0 ? null : _ship.Damage.Modules[_active];
    public float RestoreFraction => _definition.RestoreFraction;
    public RepairPriority Priority
    {
        get => _priority;
        set { if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            _priority = value; _active = -1; _nextSelection = 0; }
    }
    public double QuietRemaining => ActiveModule is { } m
        ? Math.Max(0, m.LastHitTime + _definition.QuietSeconds - _ship.SimTime) : 0;
    public float Progress => _active < 0 ? 0 : (float)Math.Clamp(_work[_active] / Duration(ActiveModule!), 0, 1);
    public double WorkRemaining => _active < 0 ? 0 : Math.Max(0, Duration(ActiveModule!) - _work[_active]);
    // Auxiliary batteries/tools allow a powerless but intact ship to attempt recovery.
    // The bounded grace prevents contested or impossible repairs from stalling a verdict forever.
    public bool RecoveringDisable => _ship.Damage.Disabled && !_ship.Damage.Destroyed
        && Priority != RepairPriority.Hold && Kits > 0 && HasMobilityRepair()
        && (double.IsNaN(_disabledSince) || _ship.SimTime - _disabledSince < RecoveryGraceSeconds);

    private void Reset()
    {
        _resetVersion = _ship.Damage.ResetVersion;
        Kits = Capacity; Completed = 0; _priority = RepairPriority.Automatic;
        _active = -1; _nextSelection = 0; _disabledSince = double.NaN;
        Array.Clear(_work);
    }

    internal void Step(double dt)
    {
        if (_resetVersion != _ship.Damage.ResetVersion) Reset();
        if (_ship.Damage.Destroyed) { _active = -1; return; }
        if (_ship.Damage.Disabled) { if (double.IsNaN(_disabledSince)) _disabledSince = _ship.SimTime - dt; }
        else _disabledSince = double.NaN;
        if (Kits <= 0 || Priority == RepairPriority.Hold) { _active = -1; return; }
        // Selection only four times a second; module damage and user commands wake it immediately.
        if (_ship.SimTime >= _nextSelection)
        {
            _nextSelection = _ship.SimTime + .25;
            _active = Select();
        }
        if (ActiveModule is not { } module || !Eligible(module)) return;
        // Integrate only the part of this step after the local cease-fire interval.
        double start = Math.Max(_ship.SimTime - dt, module.LastHitTime + _definition.QuietSeconds);
        _work[_active] += Math.Max(0, _ship.SimTime - start);
        if (_work[_active] + 1e-8 < Duration(module)) return;
        if (_ship.Damage.EmergencyRestore(module, RestoreFraction, _ship.SimTime))
        { Kits--; Completed++; }
        _work[_active] = 0; _active = -1; _nextSelection = 0;
    }

    private double Duration(ModuleState module) => _definition.RepairSeconds * (module.Destroyed ? 1.5 : 1);
    private bool Eligible(ModuleState m) => m.Definition.Kind != ModuleKind.AntimatterContainment
        && m.HealthFraction < RestoreFraction - .0001f;
    private bool HasMobilityRepair()
    {
        foreach (var m in _ship.Damage.Modules)
            if (Eligible(m) && m.Definition.Kind is ModuleKind.PowerBus or ModuleKind.Generator or ModuleKind.Reactor
                or ModuleKind.Thruster or ModuleKind.ManeuverThruster) return true;
        return false;
    }
    private int Select()
    {
        int best = -1, score = int.MinValue;
        for (int i = 0; i < _ship.Damage.Modules.Count; i++)
        {
            var m = _ship.Damage.Modules[i];
            if (!Eligible(m)) continue;
            var kind = m.Definition.Kind;
            bool power = kind is ModuleKind.PowerBus or ModuleKind.Generator or ModuleKind.Reactor;
            // Restore supply before consumers; a destroyed generator requires a restart before its bus.
            int rank = power && _ship.Damage.GenerationFraction <= .001f ? 1000 : 0;
            if (Matches(kind, Priority)) rank += 400;
            rank += kind switch
            {
                ModuleKind.Reactor or ModuleKind.Generator => 100,
                ModuleKind.PowerBus => 95,
                ModuleKind.Sensor => 90,
                ModuleKind.ManeuverThruster => 80,
                ModuleKind.Thruster => 75,
                ModuleKind.Magazine => 65,
                ModuleKind.Gun => 60,
                ModuleKind.Cooling => 50,
                _ => 40,
            };
            // A consumer cannot work until its grid is back; don't waste its kit first.
            if (!power && _ship.Damage.GridPower(m.Definition.Grid) == 0) rank -= 600;
            // Finish the current job on equal priority instead of hopping between damaged parts.
            if (i == _active) rank += 1;
            if (rank > score) { score = rank; best = i; }
        }
        return best;
    }
    private void OnHit(ModuleState module, double time)
    {
        for (int i = 0; i < _ship.Damage.Modules.Count; i++)
            if (ReferenceEquals(module, _ship.Damage.Modules[i])) { _work[i] = 0; break; }
        _nextSelection = 0;
    }
    private static bool Matches(ModuleKind kind, RepairPriority priority) => priority switch
    {
        RepairPriority.Power => kind is ModuleKind.PowerBus or ModuleKind.Generator or ModuleKind.Reactor,
        RepairPriority.Propulsion => kind is ModuleKind.Thruster or ModuleKind.ManeuverThruster,
        RepairPriority.Weapons => kind is ModuleKind.Gun or ModuleKind.Magazine,
        RepairPriority.Sensors => kind is ModuleKind.Sensor or ModuleKind.SensorBooster or ModuleKind.EcmSuite,
        RepairPriority.Defense => kind is ModuleKind.ShieldEmitter or ModuleKind.ShieldBooster or ModuleKind.DroneBay,
        RepairPriority.Cooling => kind == ModuleKind.Cooling,
        _ => false,
    };
    public static string Label(RepairPriority priority) => priority switch
    {
        RepairPriority.Power => "전력", RepairPriority.Propulsion => "추진", RepairPriority.Weapons => "무장",
        RepairPriority.Sensors => "센서", RepairPriority.Defense => "실드", RepairPriority.Cooling => "냉각",
        RepairPriority.Hold => "중지", _ => "자동",
    };
}
