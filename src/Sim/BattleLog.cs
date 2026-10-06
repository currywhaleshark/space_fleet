using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SpaceFleet.Sim;

public enum BattlePhase { Approach, Missile, Gunnery, Sniping, Brawl }
public enum BattleWeapon { Railgun, Missile, PointDefense }
public enum BattleEventKind { ModuleDestroyed, Disabled, Destroyed, Collision }
public sealed record BattleEvent(double Time, string Ship, Faction Victim, Faction? Attacker, BattleEventKind Kind, string? Module = null, string? OtherShip = null);
public sealed record BattleInterval(double Start, double Duration, BattlePhase Phase);
public sealed record BattleStrength(double Time, double Blue, double Red);
public sealed class BattleSideLog
{
    public double? Contact, Identified, Locked, MissileLaunch, MissileHit, RailLaunch, RailHit, ModuleDestroyed, Disabled, Destroyed, Close;
    public int Missiles, MissileHits, Rails, RailHits;
    public double?[] FirstTimes => new[] { Contact, Identified, Locked, MissileLaunch, MissileHit, RailLaunch, RailHit, ModuleDestroyed, Disabled, Destroyed, Close };
}
public sealed class BattleShipLog
{
    public int Rails, RailHits, Missiles, MissileHits, ModulesDestroyed;
    public float ShieldDamage, ModuleDamage;
    public int ArmorPenetrations;
}

/// <summary>World-local observer; direct weapon hooks prevent lost/deduplicated transient render events.</summary>
public sealed class BattleLog
{
    public const double IntervalSeconds = 30;
    private sealed class Bucket { public bool Missile, Rail, Brawl; public int Modules, UnshieldedModules; }
    private sealed class State
    {
        public required bool[] Modules; public bool Disabled, Destroyed; public double CollisionTime = -1;
    }
    private readonly SimWorld _world;
    private readonly Dictionary<ShipBody, State> _states = new();
    private readonly Dictionary<int, Bucket> _buckets = new();
    private readonly Dictionary<ShipBody, BattleShipLog> _ships = new();
    private readonly List<BattleEvent> _events = new();
    private readonly List<BattleInterval> _intervals = new();
    private readonly List<BattleStrength> _strength = new();
    private readonly BattleSideLog[] _sides = { new(), new() };
    private readonly Dictionary<(ShipBody,ShipBody),double> _collisionPairs = new();
    private int _nextInterval;
    private double _nextMinute = 60;
    public IReadOnlyList<BattleEvent> Events => _events;
    public IReadOnlyList<BattleInterval> Intervals => _intervals;
    public IReadOnlyList<BattleInterval>? OutcomeIntervals { get; private set; }
    public IReadOnlyList<BattleStrength> Strength => _strength;
    public int FriendlyCollisions { get; private set; }
    public BattleSideLog Side(Faction faction) => _sides[(int)faction];
    public BattleShipLog Ship(ShipBody ship) => _ships[ship];
    public BattleLog(SimWorld world)
    {
        _world = world;
        foreach (ShipBody ship in world.Ships)
        {
            _states[ship] = new State { Modules = ship.Damage.Modules.Select(m => m.Destroyed).ToArray(), Disabled = ship.Damage.Disabled, Destroyed = ship.Damage.Destroyed };
            _ships[ship] = new BattleShipLog();
        }
        RecordStrength(0);
        ObserveSensors(0);
    }
    private Bucket At(double time)
    {
        int index = (int)Math.Floor(time / IntervalSeconds);
        if (!_buckets.TryGetValue(index, out var bucket)) _buckets[index] = bucket = new Bucket();
        return bucket;
    }
    private static void First(ref double? slot, double time) { if (slot is null) slot = time; }
    internal void Fire(ShipBody shooter, BattleWeapon weapon, double time)
    {
        var side = Side(shooter.Faction);
        var stats = Ship(shooter);
        var bucket = At(time);
        if (weapon == BattleWeapon.Missile) { side.Missiles++; stats.Missiles++; First(ref side.MissileLaunch, time); bucket.Missile = true; }
        else if (weapon == BattleWeapon.Railgun)
        {
            side.Rails++; stats.Rails++; First(ref side.RailLaunch, time);
            if (shooter.Class.Kind != HullKind.Interceptor)
            {
                bucket.Rail = true;
                if (_world.Ships.Any(s => s.Faction != shooter.Faction && s.Class.Kind != HullKind.Interceptor && !s.Damage.Destroyed
                    && (s.Position - shooter.Position).LengthSquared() <= 15_000.0 * 15_000)) bucket.Brawl = true;
            }
        }
    }
    internal void Hit(ShipBody shooter, ShotResult hit, BattleWeapon weapon, double time, float shieldBefore)
    {
        if (hit.Target is not { } victim) return;
        var side = Side(shooter.Faction); var stats = Ship(shooter); var received = Ship(victim);
        if (weapon == BattleWeapon.Railgun) { side.RailHits++; stats.RailHits++; First(ref side.RailHit, time); }
        else if (weapon == BattleWeapon.Missile) { side.MissileHits++; stats.MissileHits++; First(ref side.MissileHit, time); }
        if (shooter.Class.Kind == HullKind.Interceptor && victim.Class.Kind != HullKind.Interceptor) At(time).Brawl = true;
        received.ShieldDamage += Math.Max(0, shieldBefore - victim.Damage.Shield);
        received.ModuleDamage += hit.Modules.Sum(m => m.Damage);
        if (!hit.ShieldStopped && !hit.ArmorStopped) received.ArmorPenetrations++;
        ObserveShip(victim, time, shooter);
    }
    private void ObserveShip(ShipBody ship, double time, ShipBody? attacker = null)
    {
        State state = _states[ship]; var side = Side(ship.Faction);
        for (int i = 0; i < state.Modules.Length; i++)
        {
            bool destroyed = ship.Damage.Modules[i].Destroyed;
            if (destroyed && !state.Modules[i])
            {
                _events.Add(new(time, ship.Callsign, ship.Faction, attacker?.Faction, BattleEventKind.ModuleDestroyed, ship.Damage.Modules[i].Definition.Id));
                var bucket = At(time); bucket.Modules++; if (ship.Damage.Shield <= 0.001f) bucket.UnshieldedModules++;
                First(ref side.ModuleDestroyed, time);
                if (attacker is not null) Ship(attacker).ModulesDestroyed++;
            }
            state.Modules[i] = destroyed;
        }
        if (ship.Damage.Disabled && !state.Disabled)
        { _events.Add(new(time, ship.Callsign, ship.Faction, attacker?.Faction, BattleEventKind.Disabled)); First(ref side.Disabled, time); }
        if (ship.Damage.Destroyed && !state.Destroyed)
        { _events.Add(new(time, ship.Callsign, ship.Faction, attacker?.Faction, BattleEventKind.Destroyed)); First(ref side.Destroyed, time); }
        state.Disabled = ship.Damage.Disabled; state.Destroyed = ship.Damage.Destroyed;
    }
    private void ObserveSensors(double time)
    {
        foreach (Faction faction in Enum.GetValues<Faction>())
        {
            var side = Side(faction);
            foreach (ShipBody enemy in _world.Ships.Where(s => s.Faction != faction))
            {
                TrackLevel level = _world.Sensors.Track(faction, enemy).Level;
                if (level >= TrackLevel.Contact) First(ref side.Contact, time);
                if (level >= TrackLevel.Identified) First(ref side.Identified, time);
                if (level >= TrackLevel.Locked) First(ref side.Locked, time);
            }
        }
    }
    internal void Step()
    {
        double time = _world.Time;
        // Sensors are already updated at 4 Hz; inspect them at that cadence.
        if (_world.Tick % 15 == 0) ObserveSensors(time);
        foreach (ShipBody ship in _world.Ships)
        {
            if (ship.LastCollision is { } collision && collision.Time > _states[ship].CollisionTime)
            {
                _states[ship].CollisionTime = collision.Time;
                ShipBody? other = _world.Ships.FirstOrDefault(s => s.Callsign == collision.OtherCallsign);
                if (other is not null && string.CompareOrdinal(ship.Callsign, other.Callsign) < 0)
                {
                    var pair = (ship,other);
                    // Contact lasting multiple substeps is one collision episode, not dozens of new rams.
                    if (!_collisionPairs.TryGetValue(pair,out double last) || collision.Time-last>1)
                    {
                        _events.Add(new(collision.Time, ship.Callsign, ship.Faction, other.Faction, BattleEventKind.Collision, OtherShip:other.Callsign));
                        if (other.Faction == ship.Faction) FriendlyCollisions++;
                    }
                    _collisionPairs[pair] = collision.Time;
                }
                ObserveShip(ship, collision.Time, other);
            }
        }
        if (_world.Tick % 60 == 0)
        {
            foreach (ShipBody ship in _world.Ships) ObserveShip(ship, time);
            foreach (ShipBody a in _world.Ships.Where(s => s.Class.Kind != HullKind.Interceptor && !s.Damage.Destroyed))
            foreach (ShipBody b in _world.Ships.Where(s => s.Faction != a.Faction && s.Class.Kind != HullKind.Interceptor && !s.Damage.Destroyed))
                if ((a.Position - b.Position).LengthSquared() <= 15_000.0 * 15_000) First(ref Side(a.Faction).Close, time);
        }
        while ((_nextInterval + 1) * IntervalSeconds <= time + 1e-6) FinishInterval(IntervalSeconds);
        while (_nextMinute <= time + 1e-6) { RecordStrength(_nextMinute); _nextMinute += 60; }
    }
    private void RecordStrength(double time) => _strength.Add(new(time, BattleRules.Strength(_world.Ships, Faction.Blue), BattleRules.Strength(_world.Ships, Faction.Red)));
    private void FinishInterval(double duration)
    {
        Bucket b = _buckets.GetValueOrDefault(_nextInterval) ?? new Bucket();
        _intervals.Add(new(_nextInterval * IntervalSeconds, duration, Classify(b)));
        _buckets.Remove(_nextInterval++);
    }
    private static BattlePhase Classify(Bucket b) => b.Brawl ? BattlePhase.Brawl : b.Modules >= 2 && b.UnshieldedModules * 2 >= b.Modules ? BattlePhase.Sniping
        : b.Rail ? BattlePhase.Gunnery : b.Missile ? BattlePhase.Missile : BattlePhase.Approach;
    internal void CaptureOutcome(double time)
    {
        if(OutcomeIntervals is not null)return;
        var snapshot=_intervals.ToList();double remaining=time-_nextInterval*IntervalSeconds;
        if(remaining>1e-6)snapshot.Add(new(_nextInterval*IntervalSeconds,remaining,Classify(_buckets.GetValueOrDefault(_nextInterval)??new Bucket())));
        OutcomeIntervals=snapshot.AsReadOnly();
    }
    public void Finish()
    {
        double remainder = _world.Time - _nextInterval * IntervalSeconds;
        if (remainder > 1e-6) FinishInterval(remainder);
    }
    public double PhaseSeconds(BattlePhase phase) => _intervals.Where(i => i.Phase == phase).Sum(i => i.Duration);
    public string Summary()
    {
        string N(double? n) => n?.ToString("0.000", CultureInfo.InvariantCulture) ?? "-";
        string SideSummary(Faction f) { var s = Side(f); return $"{f}:{string.Join('/', s.FirstTimes.Select(N))};M={s.Missiles}/{s.MissileHits};R={s.Rails}/{s.RailHits}"; }
        var outcome = _world.Rules?.Outcome;
        string phases = string.Join(',', Enum.GetValues<BattlePhase>().Select(p => $"{p}:{N(PhaseSeconds(p))}"));
        return $"{SideSummary(Faction.Blue)}|{SideSummary(Faction.Red)}|{phases}|collisions={FriendlyCollisions}|events={_events.Count}|winner={outcome?.Winner?.ToString() ?? "Draw"};time={N(outcome?.Time)}";
    }
}
