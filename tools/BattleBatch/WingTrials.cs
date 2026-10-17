using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpaceFleet.Sim;

// Controlled addition to EscortTrials: identical BB + two escorts on each side.
// Only the optional, separately commanded five-ship interceptor wing changes.
internal static class WingTrials
{
    internal sealed record TimingTuning(float CoreHealthScale = 1, float ShieldScale = 1, float WingForwardMeters = 0,
        float StructureScale = 1, float CriticalEnergyScale = 1, float AssaultReachScale = 1, float ExternalHealthScale = 1);
    private static Dictionary<string, ShipDefinition> Definitions(TimingTuning tuning, string? baselinePath = null)
    {
        if (!float.IsFinite(tuning.CoreHealthScale) || tuning.CoreHealthScale <= 0
            || !float.IsFinite(tuning.ShieldScale) || tuning.ShieldScale <= 0
            || !float.IsFinite(tuning.WingForwardMeters) || tuning.WingForwardMeters < 0
            || !float.IsFinite(tuning.StructureScale) || tuning.StructureScale <= 0
            || !float.IsFinite(tuning.CriticalEnergyScale) || tuning.CriticalEnergyScale <= 0
            || !float.IsFinite(tuning.AssaultReachScale) || tuning.AssaultReachScale <= 0
            || !float.IsFinite(tuning.ExternalHealthScale) || tuning.ExternalHealthScale <= 0)
            throw new ArgumentException("Positive finite timing tuning required");
        var result = new Dictionary<string, ShipDefinition>();
        if (baselinePath is not null)
            foreach (var pair in JsonNode.Parse(File.ReadAllText(baselinePath))!.AsObject())
            {
                var definition = ShipDefinition.Parse(pair.Value!.ToJsonString());
                if (definition.Id != pair.Key || ShipDefinitions.ById(pair.Key).Kind != definition.Kind)
                    throw new ArgumentException("Baseline hull ID/kind mismatch");
                result.Add(pair.Key, definition);
            }
        ShipDefinition Source(string id) => result.GetValueOrDefault(id) ?? ShipDefinitions.ById(id);
        if (tuning == new TimingTuning()) return result;
        foreach (string id in new[] { "battleship", "mars_battleship" })
        {
            var node = JsonNode.Parse(Source(id).ToJson())!;
            node["Shield"]!["Capacity"] = node["Shield"]!["Capacity"]!.GetValue<float>() * tuning.ShieldScale;
            node["StructureHitPoints"] = node["StructureHitPoints"]!.GetValue<float>() * tuning.StructureScale;
            foreach (var module in node["Modules"]!.AsArray())
                if (module!["Kind"]!.GetValue<string>() is "Magazine" or "Reactor" or "Generator" or "PowerBus")
                {
                    module["HitPoints"] = module["HitPoints"]!.GetValue<float>() * tuning.CoreHealthScale;
                    module["CriticalEnergy"] = module["CriticalEnergy"]!.GetValue<float>() * tuning.CriticalEnergyScale;
                }
                else module!["HitPoints"] = module["HitPoints"]!.GetValue<float>() * tuning.ExternalHealthScale;
            result[id] = ShipDefinition.Parse(node.ToJsonString());
        }
        if (tuning.AssaultReachScale != 1)
            foreach (string id in new[] { "interceptor", "mars_interceptor" })
            {
                var definition = Source(id);
                float reach = definition.Ai.AssaultReachMeters > 0 ? definition.Ai.AssaultReachMeters
                    : definition.Design == DesignFamily.Mars ? 100_000 : 85_000;
                var node = JsonNode.Parse(definition.ToJson())!;
                node["Ai"]!["AssaultReachMeters"] = reach * tuning.AssaultReachScale;
                result[id] = ShipDefinition.Parse(node.ToJsonString());
            }
        return result;
    }

    private sealed class Recorder(SimWorld world)
    {
        private readonly ShipBody[] _ships = world.Ships.ToArray();
        private readonly double[] _activeSeconds = new double[world.Ships.Count];
        private readonly double[] _fightingSeconds = new double[world.Ships.Count];
        private readonly double?[] _firstHit = new double?[world.Ships.Count];
        private readonly double?[] _firstRailFire = new double?[world.Ships.Count];
        private readonly int[] _torpedoes = new int[world.Ships.Count];
        public readonly List<object> LaunchWindows = new();
        private readonly double?[] _closestCapital = new double?[world.Ships.Count];
        private readonly Dictionary<Squadron, double[]> _missions = world.Squadrons
            .Where(q => q.Role == SquadronRole.InterceptorWing).ToDictionary(q => q, _ => new double[5]);

        public void Capture(SimWorld w)
        {
            for (int i = 0; i < _ships.Length; i++)
            {
                var ship = _ships[i];
                if (!Squadron.Active(ship)) continue;
                _activeSeconds[i] += SimWorld.TickDelta;
                if (ship.Damage.WeaponsFraction > .1f) _fightingSeconds[i] += SimWorld.TickDelta;
                if (ship.Class.Kind != HullKind.Interceptor || w.Tick % 60 != 0) continue;
                // Post-hoc observer only; no values are fed back to the sensor net or AI.
                foreach (var enemy in _ships)
                    if (enemy.Faction != ship.Faction && enemy.Class.Kind != HullKind.Interceptor && Squadron.Active(enemy))
                    {
                        double distance = (ship.Position - enemy.Position).Length();
                        _closestCapital[i] = Math.Min(_closestCapital[i] ?? double.PositiveInfinity, distance);
                    }
            }
            foreach (var (wing, seconds) in _missions)
                if (wing.Members.Any(Squadron.Active)) seconds[(int)wing.Mission] += SimWorld.TickDelta;
        }

        public void After(SimWorld w)
        {
            for (int i = 0; i < _ships.Length; i++)
            {
                var ship = _ships[i]; var log = w.Log!.Ship(ship);
                if (log.ShieldDamage > 0 || log.ModuleDamage > 0) _firstHit[i] ??= w.Time - SimWorld.TickDelta;
                if (log.Rails > 0) _firstRailFire[i] ??= w.Time - SimWorld.TickDelta;
                int count = log.Torpedoes - _torpedoes[i];
                _torpedoes[i] = log.Torpedoes;
                if (count <= 0) continue;
                bool Fighting(ShipBody s) => s.Class.Kind == HullKind.Battleship && Squadron.Active(s) && s.Damage.WeaponsFraction > .1f;
                LaunchWindows.Add(new { Time = w.Time - SimWorld.TickDelta, ship.Callsign,
                    Design = ship.Definition.Design.ToString(), Count = count,
                    OwnBattleshipFighting = _ships.Any(s => s.Faction == ship.Faction && Fighting(s)),
                    EnemyBattleshipFighting = _ships.Any(s => s.Faction != ship.Faction && Fighting(s)) });
            }
        }

        public object[] Ships(SimWorld w) => _ships.Select((ship, i) =>
        {
            var log = w.Log!.Ship(ship);
            var equipment = ship.Ordnance.Interceptor;
            return (object)new { ship.Callsign, Hull = ship.Definition.Id, Design = ship.Definition.Design.ToString(),
                Team = ship.Faction.ToString(), Kind = ship.Class.Kind.ToString(), Weight = BattleRules.Weight(ship.Definition),
                ActiveSeconds = _activeSeconds[i], ClosestCapitalMeters = _closestCapital[i],
                FightingSeconds = _fightingSeconds[i], FirstHit = _firstHit[i], FirstRailFire = _firstRailFire[i],
                log.Rails, log.RailHits, log.Missiles, log.MissileHits, log.Torpedoes, log.TorpedoHits,
                log.MissilesIntercepted, log.TorpedoesIntercepted, log.CounterMissileKills, log.DronesDestroyed,
                log.ShieldDamageInflicted, log.ModuleDamageInflicted, log.ShieldDamage, log.ModuleDamage,
                log.DisabledAt, log.DestroyedAt, ship.Damage.Destroyed, ship.Damage.Disabled, ship.Damage.StructureFraction,
                ship.Damage.Destruction, Repairs = ship.DamageControl.Completed,
                ship.Damage.WeaponsFraction, ship.Damage.SensorFraction,
                MeanFireRange = log.RailRangeSamples > 0 ? (double?)(log.RailRangeSum / log.RailRangeSamples) : null,
                equipment.FirstAction, equipment.FirstAssault, ProbesLaunched = equipment.Launched,
                ProbeContacts = equipment.NewContacts, ProbeIdentifications = equipment.NewIdentifications, equipment.ProbeKills,
                AmRoundsLeft = ship.Ordnance.Antimatter.Rounds, AmFailures = ship.Ordnance.Antimatter.Failures,
                AmJettisons = ship.Ordnance.Antimatter.Jettisons };
        }).ToArray();

        public object[] Wings() => _missions.Select(pair => (object)new {
            Design = pair.Key.Members[0].Definition.Design.ToString(), Team = pair.Key.Faction.ToString(),
            pair.Key.MissionChanges, FinalMission = pair.Key.Mission.ToString(),
            MissionSeconds = Enum.GetValues<InterceptorMission>().ToDictionary(m => m.ToString(), m => pair.Value[(int)m])
        }).ToArray();
    }

    private static object Run(EscortTrials.Case c, double minutes, string? traceDirectory,
        TimingTuning tuning, IReadOnlyDictionary<string, ShipDefinition> definitions)
    {
        var w = EscortTrials.Fixture(c, definitions, tuning.WingForwardMeters);
        var recorder = new Recorder(w);
        var telemetry = traceDirectory is null ? null : new BalanceTelemetry(c.Seed, c.Mirror);
        var watch = Stopwatch.StartNew();
        while (w.Time < minutes * 60 && w.Rules!.Outcome is null)
        {
            recorder.Capture(w);
            w.Step();
            recorder.After(w);
            telemetry?.Capture(w);
        }
        w.Log!.Finish();
        if (telemetry is not null)
        {
            using var file = File.Create(Path.Combine(traceDirectory!, c.Id + ".csv.gz"));
            using var gzip = new GZipStream(file, CompressionLevel.Optimal);
            using var writer = new StreamWriter(gzip, new System.Text.UTF8Encoding(false));
            writer.WriteLine(BalanceTelemetry.Header);
            foreach (string row in telemetry.Rows) writer.WriteLine(row);
        }
        Faction? winner = w.Rules!.Outcome?.Winner;
        string design = winner is null ? "Draw" : (winner == Faction.Blue) == c.EarthBlue ? "Earth" : "Mars";
        return new { Case = c, Winner = design, WinnerTeam = winner?.ToString(),
            Reason = w.Rules.Outcome?.Reason ?? "measurement-time-cap", Time = w.Time, WallSeconds = watch.Elapsed.TotalSeconds,
            Ships = recorder.Ships(w), Wings = recorder.Wings(), recorder.LaunchWindows, AntimatterFlights = w.Log.AntimatterFlights,
            ProbeFlights = w.ProbeFlights, ActiveProbes = w.Probes.Select(p => new {
                p.Id, Owner = p.Owner.Callsign, p.Age, p.Remaining, p.Observing }).ToArray(),
            w.Log.FriendlyCollisions, w.Log.DroppedEvents, Events = w.Log.Events };
    }

    public static void Save(string output, int[] seeds, double[] distances, int jobs, double minutes, bool telemetry, bool primaryOnly,
        string? tuningPath = null, string? baselinePath = null)
    {
        if (seeds.Length == 0 || seeds.Distinct().Count() != seeds.Length || distances.Length == 0
            || distances.Distinct().Count() != distances.Length || distances.Any(d => !double.IsFinite(d) || d <= 0)
            || jobs < 1 || !double.IsFinite(minutes) || minutes <= 0)
            throw new ArgumentException("Unique seeds/distances, positive jobs and duration are required");
        var wings = primaryOnly ? new[] { (0, 0), (5, 5) } : new[] { (0, 0), (5, 5), (5, 0), (0, 5) };
        var tuning = tuningPath is null ? new TimingTuning() : JsonSerializer.Deserialize<TimingTuning>(File.ReadAllText(tuningPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
            ?? throw new ArgumentException("Empty timing tuning");
        var definitions = Definitions(tuning, baselinePath);
        var cases = (from seed in seeds from distance in distances from earthBlue in new[] { true, false }
            from mirror in new[] { false, true } from wing in wings
            select new EscortTrials.Case(seed, distance, earthBlue, mirror, 2, 2, wing.Item1, wing.Item2)).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string traces = Path.ChangeExtension(output, ".traces");
        if (telemetry) Directory.CreateDirectory(traces);
        var results = new ConcurrentBag<(string Id, object Result)>();
        int done = 0;
        Parallel.ForEach(cases, new ParallelOptions { MaxDegreeOfParallelism = jobs }, c =>
        {
            results.Add((c.Id, Run(c, minutes, telemetry ? traces : null, tuning, definitions)));
            int n = Interlocked.Increment(ref done);
            if (n % 8 == 0 || n == cases.Length) Console.WriteLine($"wing-trials {n}/{cases.Length}");
        });
        var document = new {
            Schema = 1, Minutes = minutes, Seeds = seeds, Distances = distances, Tuning = tuning,
            DefinitionBaseline = baselinePath is null ? null : Path.GetFileName(baselinePath),
            BaseComposition = "Each side: 1 battleship + 2 escorts; automatic commander and wing missions; DeepSpace Annihilation",
            WingHulls = new[] { "interceptor", "mars_interceptor" },
            WingPairs = wings.Select(p => new[] { p.Item1, p.Item2 }).ToArray(),
            Trials = results.OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => r.Result).ToArray()
        };
        var options = new JsonSerializerOptions { WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        using (var file = File.Create(output))
        {
            if (output.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                using var gzip = new GZipStream(file, CompressionLevel.Optimal);
                JsonSerializer.Serialize(gzip, document, options);
            }
            else JsonSerializer.Serialize(file, document, options);
        }
        Console.WriteLine($"Saved {cases.Length} paired wing trials to {output}");
    }

    public static void Check()
    {
        int checks = 0;
        void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); checks++; }
        var originals = new[] { "battleship", "mars_battleship", "interceptor", "mars_interceptor" }
            .ToDictionary(id => id, id => ShipDefinitions.ById(id).ToJson());
        Require(Definitions(new()).Count == 0, "Default tuning must use the catalog unchanged");
        var tuned = Definitions(new(CoreHealthScale: 3, ShieldScale: 4, StructureScale: 3,
            CriticalEnergyScale: 2, AssaultReachScale: 1.6f, ExternalHealthScale: 3));
        foreach (var (id, definition) in tuned)
        {
            var source = ShipDefinitions.ById(id);
            Require(source.ToJson() == originals[id], "Tuning mutated the catalog");
            Require(JsonNode.DeepEquals(JsonNode.Parse(definition.ToJson())!["HullSections"],
                    JsonNode.Parse(source.ToJson())!["HullSections"])
                && definition.Modules.Select(m => (m.Id, m.Center, m.HalfSize, m.CriticalChance))
                    .SequenceEqual(source.Modules.Select(m => (m.Id, m.Center, m.HalfSize, m.CriticalChance))),
                "Tuning changed geometry or critical probability");
            if (source.Kind != HullKind.Battleship) continue;
            Require(definition.Modules.Zip(source.Modules).All(p => p.First.HitPoints == p.Second.HitPoints * 3)
                && definition.Shield.Capacity == source.Shield.Capacity * 4
                && definition.StructureHitPoints == source.StructureHitPoints * 3, "Reserve scaling");
            Require(definition.Shield.RechargePerSecond == source.Shield.RechargePerSecond, "No recharge buff");
        }
        foreach (var invalid in new[] { new TimingTuning(CoreHealthScale: 0), new(ShieldScale: float.NaN),
            new(WingForwardMeters: -1), new(StructureScale: float.PositiveInfinity), new(CriticalEnergyScale: -1),
            new(AssaultReachScale: 0), new(ExternalHealthScale: -1) })
        {
            bool rejected = false;
            try { Definitions(invalid); } catch (ArgumentException) { rejected = true; }
            Require(rejected, "Invalid timing tuning accepted");
        }
        foreach (bool earthBlue in new[] { false, true })
        foreach (bool mirror in new[] { false, true })
        foreach (var wings in new[] { (5, 0), (0, 5), (5, 5) })
        {
            var c = new EscortTrials.Case(3, 70000, earthBlue, mirror, 2, 2);
            var baseline = EscortTrials.Fixture(c);
            var w = EscortTrials.Fixture(c with { EarthInterceptors = wings.Item1, MarsInterceptors = wings.Item2 });
            Require(w.Ships.Count == 6 + wings.Item1 + wings.Item2, "Wing counts");
            foreach (var ship in baseline.Ships)
            {
                var paired = w.Ships.Single(s => s.Callsign == ship.Callsign);
                Require(paired.Position == ship.Position && paired.Orientation == ship.Orientation
                    && paired.Definition.Id == ship.Definition.Id && paired.Faction == ship.Faction,
                    "Adding a wing must preserve capital poses, callsigns, teams and definitions");
                Require(paired.Squadron!.Role == SquadronRole.BattleGroup && paired.Squadron.Members.Count == 3
                    && w.BrainOf(paired)!.Order == baseline.BrainOf(ship)!.Order, "Capital group/order changed");
            }
            Require(w.Ships.Where(s => s.Class.Kind == HullKind.Interceptor).All(s =>
                s.Squadron!.Role == SquadronRole.InterceptorWing && s.Squadron.Members.Count == 5), "Separate wing required");
        }
        var sample = new EscortTrials.Case(2, 20000, true, false, 2, 2, 5, 5);
        var a = EscortTrials.Fixture(sample); var b = EscortTrials.Fixture(sample);
        var recorder = new Recorder(a); var trace = new BalanceTelemetry(sample.Seed, sample.Mirror);
        for (int i = 0; i < 3600; i++)
        { recorder.Capture(a); a.Step(); b.Step(); recorder.After(a); trace.Capture(a); }
        a.Log!.Finish(); b.Log!.Finish();
        Require(a.Log.Summary() == b.Log.Summary(), "Instrumentation changed summary");
        Require(a.Log.AntimatterFlights.SequenceEqual(b.Log.AntimatterFlights), "Instrumentation changed torpedoes");
        Require(a.ProbeFlights.SequenceEqual(b.ProbeFlights), "Instrumentation changed probes");
        for (int i = 0; i < a.Ships.Count; i++)
        {
            var x = a.Ships[i]; var y = b.Ships[i];
            Require(x.Position == y.Position && x.Orientation == y.Orientation && x.Damage.StructureFraction == y.Damage.StructureFraction,
                "Instrumentation changed ship state");
            Require(x.Damage.Modules.Select(m => m.Health).SequenceEqual(y.Damage.Modules.Select(m => m.Health)), "Instrumentation changed modules");
            Require(x.Ordnance.Interceptor.FirstAction == y.Ordnance.Interceptor.FirstAction
                && x.Squadron!.Mission == y.Squadron!.Mission, "Instrumentation changed missions");
        }
        Console.WriteLine($"PASS: wing trial fixture/instrumentation ({checks} checks)");
    }
}
