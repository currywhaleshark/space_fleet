using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Godot;
using SpaceFleet.Sim;

// Measurement-only fixture. A lone battleship uses the SAME commander/group rules
// as an escorted battleship; never compare SpawnDuel's different AI to Spawn's AI.
internal static class EscortTrials
{
    internal sealed record Case(int Seed, double Distance, bool EarthBlue, bool Mirror, int EarthEscorts, int MarsEscorts,
        int EarthInterceptors = 0, int MarsInterceptors = 0)
    {
        public string Id => $"s{Seed}-d{Distance:R}-eb{EarthBlue}-m{Mirror}-e{EarthEscorts}-r{MarsEscorts}"
            + (EarthInterceptors == 0 && MarsInterceptors == 0 ? "" : $"-wi{EarthInterceptors}-{MarsInterceptors}");
    }

    internal sealed record GunTuning(float? ReloadSeconds = null, float? AimErrorScale = null);

    private static Dictionary<string, ShipDefinition> Definitions(IReadOnlyDictionary<string, GunTuning> tuning)
    {
        var result = new Dictionary<string, ShipDefinition>();
        foreach (var (id, gun) in tuning)
        {
            if (id is not ("battleship" or "mars_battleship")) throw new ArgumentException("Trial tuning is restricted to battleship guns");
            var node = JsonNode.Parse(ShipDefinitions.ById(id).ToJson())!;
            if (gun.ReloadSeconds is { } reload) node["Railgun"]!["ReloadSeconds"] = reload;
            if (gun.AimErrorScale is { } error) node["Railgun"]!["AimErrorScale"] = error;
            result.Add(id, ShipDefinition.Parse(node.ToJsonString()));
        }
        return result;
    }

    internal static SimWorld Fixture(Case c, IReadOnlyDictionary<string, ShipDefinition>? definitions = null, float wingForwardMeters = 0)
    {
        if (!double.IsFinite(c.Distance) || c.Distance <= 0 || c.EarthEscorts is not (0 or 2) || c.MarsEscorts is not (0 or 2)
            || c.EarthInterceptors is not (0 or 5) || c.MarsInterceptors is not (0 or 5)
            || !float.IsFinite(wingForwardMeters) || wingForwardMeters < 0)
            throw new ArgumentException("Positive separation, zero/two escorts and zero/five interceptors are required");
        var w = new SimWorld();
        var rng = new Random(c.Seed);
        var poses = new (Vec3d Position, Quaternion Rotation)[2];
        // Draw both spatial poses unconditionally: adding escorts or changing teams
        // cannot change the battleship's starting pose or its callsign damage seed.
        for (int side = 0; side < 2; side++)
        {
            double Noise() => rng.NextDouble() * 2 - 1;
            var offset = c.Seed == 0 ? Vec3d.Zero : new Vec3d(Noise(), Noise(), Noise()) * 2000;
            float heading = c.Seed == 0 ? 0 : Mathf.DegToRad((float)Noise() * 5);
            poses[side] = (offset + new Vec3d(0, 0, side == 0 ? 0 : -c.Distance),
                new Quaternion(Vector3.Up, (side == 0 ? 0 : Mathf.Pi) + heading));
        }
        foreach (var faction in new[] { Faction.Blue, Faction.Red })
        {
            bool blue = faction == Faction.Blue, earth = blue == c.EarthBlue;
            var family = earth ? DesignFamily.Earth : DesignFamily.Mars;
            var pose = poses[(blue ? 0 : 1) ^ (c.Mirror ? 1 : 0)];
            var members = new List<ShipBody>();
            ShipBody Add(string call, HullKind kind, Vector3 offset, List<ShipBody> squadron)
            {
                var definition = ShipDefinitions.For(kind, family);
                if (definitions?.TryGetValue(definition.Id, out var replacement) == true) definition = replacement;
                var s = w.Add(new ShipBody(call, definition, faction));
                s.Place(pose.Position + Vec3d.From(pose.Rotation * offset), pose.Rotation);
                s.Control = ShipControl.Idle;
                w.AttachBrain(s, ShipOrder.HoldAt(s.Position));
                squadron.Add(s);
                return s;
            }
            Add(blue ? "BB-01" : "BB-X1", HullKind.Battleship, Vector3.Zero, members);
            int escorts = earth ? c.EarthEscorts : c.MarsEscorts;
            for (int i = 0; i < escorts; i++)
                Add(blue ? $"DD-1{i+1}" : $"DD-X{i+1}", HullKind.Escort,
                    new Vector3(i == 0 ? -1500 : 1500, i == 0 ? 150 : -150, -600), members);
            w.AddSquadron(blue ? "전투단" : "적 전투단", faction, SquadronRole.BattleGroup, members);
            var wing = new List<ShipBody>();
            int interceptors = earth ? c.EarthInterceptors : c.MarsInterceptors;
            for (int i = 0; i < interceptors; i++)
                Add(blue ? $"IC-2{i+1}" : $"IC-X{i+1}", HullKind.Interceptor,
                    new Vector3(420 + 60 * i, 110 - 25 * i, 850 + 50 * i - Math.Min(wingForwardMeters, (float)c.Distance * .2f)), wing);
            if (wing.Count > 0)
                w.AddSquadron(blue ? "요격 편대" : "적 요격 편대", faction, SquadronRole.InterceptorWing, wing);
        }
        w.Doctrine = new FleetDoctrine();
        w.EnableCommander(Faction.Red);
        w.EnableCommander(Faction.Blue);
        w.Rules = new BattleRules(w, BattleObjective.Annihilation);
        w.Sensors.Update(w.Ships, 0, force: true);
        w.Log = new BattleLog(w);
        return w;
    }

    private sealed class Recorder
    {
        public readonly ShipBody Ship;
        private readonly ShipBody[] _enemies;
        private readonly Dictionary<ShipBody, SensorTrack> _tracks = new();
        private Vec3d _position;
        private bool _blind;
        private int _rails;
        public double? FirstFire, FirstFireRange, FirstBlind;
        public int LinkedRounds, BlindRounds, BlindLinkedRounds;
        public double ActiveSeconds, LockedSeconds, LinkedLockSeconds, BlindSeconds, BlindNoLockSeconds;
        public double EscortDistanceSum;
        public int EscortDistanceSamples;

        public Recorder(SimWorld w, ShipBody s) { Ship = s; _enemies = w.Ships.Where(e => e.Faction != s.Faction).ToArray(); }

        // The observation used by StepGunnery belongs to the start of the tick.
        // Snapshot every possible target because StepAI may switch targets this tick.
        public void Before(SimWorld w)
        {
            _position = Ship.Position;
            _blind = Ship.Damage.SensorFraction <= .02f;
            foreach (var enemy in _enemies) _tracks[enemy] = w.Sensors.Track(Ship.Faction, enemy);
            if (!Squadron.Active(Ship)) return;
            ActiveSeconds += SimWorld.TickDelta;
            if (_blind) { FirstBlind ??= w.Time; BlindSeconds += SimWorld.TickDelta; }
            var target = w.BrainOf(Ship)?.Target;
            var track = target is null ? SensorTrack.Unknown : _tracks[target];
            bool locked = track.Level == TrackLevel.Locked && track.Observation is { } obs && w.Time - obs.Time <= .5;
            if (locked)
            {
                LockedSeconds += SimWorld.TickDelta;
                if (track.Observation!.Value.Observer != Ship) LinkedLockSeconds += SimWorld.TickDelta;
            }
            if (_blind && !locked) BlindNoLockSeconds += SimWorld.TickDelta;
            if (Ship.Class.Kind == HullKind.Battleship && w.Tick % 60 == 0)
                foreach (var escort in Ship.Squadron!.Members.Where(s => s.Class.Kind == HullKind.Escort && Squadron.Active(s)))
                { EscortDistanceSum += (escort.Position - Ship.Position).Length(); EscortDistanceSamples++; }
        }

        public void After(SimWorld w)
        {
            int rounds = w.Log!.Ship(Ship).Rails - _rails;
            _rails += rounds;
            if (rounds == 0) return;
            var target = w.BrainOf(Ship)?.Target;
            var track = target is not null && _tracks.TryGetValue(target, out var t) ? t : SensorTrack.Unknown;
            if (FirstFire is null)
            {
                FirstFire = w.Time - SimWorld.TickDelta;
                FirstFireRange = track.Level > TrackLevel.None ? (track.EstimatedPosition - _position).Length() : null;
            }
            bool linked = track.Observation is { } observation && observation.Observer != Ship
                && w.Time - SimWorld.TickDelta - observation.Time <= .5;
            if (linked) LinkedRounds += rounds;
            if (_blind) { BlindRounds += rounds; if (linked) BlindLinkedRounds += rounds; }
        }

        public object Result(SimWorld w)
        {
            var log = w.Log!.Ship(Ship);
            return new { Ship.Callsign, Hull = Ship.Definition.Id, Design = Ship.Definition.Design.ToString(), Kind = Ship.Class.Kind.ToString(),
                Team = Ship.Faction.ToString(), FirstFire, FirstFireRange, FirstBlind,
                LinkedRounds, BlindRounds, BlindLinkedRounds, ActiveSeconds, LockedSeconds, LinkedLockSeconds, BlindSeconds, BlindNoLockSeconds,
                MeanEscortDistance = EscortDistanceSamples > 0 ? (double?)(EscortDistanceSum / EscortDistanceSamples) : null,
                log.Rails, log.RailHits, log.Missiles, log.MissileHits, log.MissilesIntercepted, log.TorpedoesIntercepted, log.DronesDestroyed,
                log.ShieldDamageInflicted, log.ModuleDamageInflicted, log.ShieldDamage, log.ModuleDamage,
                log.DisabledAt, log.DestroyedAt, Ship.Damage.Destroyed, Ship.Damage.Disabled, Ship.Damage.StructureFraction,
                MeanFireRange = log.RailRangeSamples > 0 ? (double?)(log.RailRangeSum / log.RailRangeSamples) : null };
        }
    }

    private static object Run(Case c, double minutes, string? traceDirectory, IReadOnlyDictionary<string, ShipDefinition> definitions)
    {
        var w = Fixture(c, definitions);
        var recorders = w.Ships.Select(s => new Recorder(w, s)).ToArray();
        var telemetry = traceDirectory is null ? null : new BalanceTelemetry(c.Seed, c.Mirror);
        var watch = Stopwatch.StartNew();
        while (w.Time < minutes * 60 && w.Rules!.Outcome is null)
        {
            foreach (var r in recorders) r.Before(w);
            w.Step();
            foreach (var r in recorders) r.After(w);
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
        string winnerDesign = winner is null ? "Draw" : (winner == Faction.Blue) == c.EarthBlue ? "Earth" : "Mars";
        return new { Case = c, Winner = winnerDesign, WinnerTeam = winner?.ToString(),
            Reason = w.Rules.Outcome?.Reason ?? "measurement-time-cap", Time = w.Time, WallSeconds = watch.Elapsed.TotalSeconds,
            Ships = recorders.Select(r => r.Result(w)).ToArray(), w.Log.FriendlyCollisions, w.Log.DroppedEvents, Events = w.Log.Events };
    }

    public static void Save(string output, int[] seeds, double[] distances, int jobs, double minutes, bool telemetry,
        string? tuningPath = null, bool primaryOnly = false)
    {
        if (seeds.Length == 0 || seeds.Distinct().Count() != seeds.Length || distances.Length == 0
            || distances.Distinct().Count() != distances.Length || distances.Any(d => !double.IsFinite(d) || d <= 0)
            || jobs < 1 || !double.IsFinite(minutes) || minutes <= 0)
            throw new ArgumentException("Unique seeds/distances, positive jobs and duration are required");
        var tuning = tuningPath is null ? new Dictionary<string, GunTuning>()
            : JsonSerializer.Deserialize<Dictionary<string, GunTuning>>(File.ReadAllText(tuningPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
                ?? throw new ArgumentException("Empty tuning definition");
        var definitions = Definitions(tuning);
        var supports = primaryOnly ? new[] { (0, 0), (2, 2) } : new[] { (0, 0), (2, 2), (2, 0), (0, 2) };
        var cases = (from seed in seeds from distance in distances from earthBlue in new[] { true, false }
            from mirror in new[] { false, true } from support in supports
            select new Case(seed, distance, earthBlue, mirror, support.Item1, support.Item2)).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string traces = Path.ChangeExtension(output, ".traces");
        if (telemetry) Directory.CreateDirectory(traces);
        var results = new ConcurrentBag<(string Id, object Result)>();
        int done = 0;
        Parallel.ForEach(cases, new ParallelOptions { MaxDegreeOfParallelism = jobs }, c =>
        {
            results.Add((c.Id, Run(c, minutes, telemetry ? traces : null, definitions)));
            int n = Interlocked.Increment(ref done);
            if (n % 16 == 0 || n == cases.Length) Console.WriteLine($"escort-trials {n}/{cases.Length}");
        });
        File.WriteAllText(output, JsonSerializer.Serialize(new { Schema = 2, Minutes = minutes, Seeds = seeds, Distances = distances,
            SupportPairs = supports.Select(s => new[] { s.Item1, s.Item2 }).ToArray(), Tuning = tuning,
            Trials = results.OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => r.Result).ToArray() }, JsonOptions));
        Console.WriteLine($"Saved {cases.Length} paired trials to {output}");
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Check()
    {
        int checks = 0;
        void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); checks++; }
        Require(new Case(1, 20000.1, true, false, 0, 0).Id != new Case(1, 20000.2, true, false, 0, 0).Id,
            "Fractional distances must not overwrite each other's telemetry");
        var original = ShipDefinitions.ById("battleship");
        var adjusted = Definitions(new Dictionary<string, GunTuning> { ["battleship"] = new(3.3f, 1.1f) });
        Require(adjusted["battleship"].Railgun!.ReloadSeconds == 3.3f && adjusted["battleship"].Railgun!.AimErrorScale == 1.1f,
            "Trial override applies gun timing and uncertainty");
        Require(ReferenceEquals(ShipDefinitions.ById("battleship"), original) && !ReferenceEquals(adjusted["battleship"], original),
            "Trial overrides do not mutate the shared hull catalog");
        foreach (bool earthBlue in new[] { false, true })
        foreach (bool mirror in new[] { false, true })
        {
            var c = new Case(3, 70000, earthBlue, mirror, 0, 0);
            var lone = Fixture(c);
            foreach (var support in new[] { (2, 0), (0, 2), (2, 2) })
            {
                var w = Fixture(c with { EarthEscorts = support.Item1, MarsEscorts = support.Item2 });
                Require(w.Ships.Count == 2 + support.Item1 + support.Item2, "Ship count");
                foreach (var bb in lone.Ships)
                {
                    var paired = w.Ships.Single(s => s.Callsign == bb.Callsign);
                    Require(paired.Position == bb.Position && paired.Orientation == bb.Orientation && paired.Definition.Id == bb.Definition.Id,
                        "Support must preserve battleship poses, callsigns and definitions");
                    Require(paired.Squadron!.Role == bb.Squadron!.Role && w.BrainOf(paired)!.Order == lone.BrainOf(bb)!.Order,
                        "Identical commander/group/order setup");
                }
            }
        }
        // Read-only instrumentation must not perturb damage, movement or outcomes.
        foreach (int support in new[] { 0, 2 })
        {
            var c = new Case(2, 20000, true, false, support, support);
            var a = Fixture(c); var b = Fixture(c);
            var recorders = a.Ships.Select(s => new Recorder(a, s)).ToArray();
            var trace = new BalanceTelemetry(c.Seed, c.Mirror);
            for (int tick = 0; tick < 3600; tick++)
            {
                foreach (var r in recorders) r.Before(a);
                a.Step(); b.Step();
                foreach (var r in recorders) r.After(a);
                trace.Capture(a);
            }
            a.Log!.Finish(); b.Log!.Finish();
            Require(a.Log.Summary() == b.Log.Summary(), "Instrumentation changed battle summary");
            for (int i = 0; i < a.Ships.Count; i++)
            {
                var x = a.Ships[i]; var y = b.Ships[i]; var r = recorders[i];
                Require(x.Position == y.Position && x.Orientation == y.Orientation && x.Damage.StructureFraction == y.Damage.StructureFraction,
                    "Instrumentation changed ship state");
                Require(x.Damage.Modules.Select(m => m.Health).SequenceEqual(y.Damage.Modules.Select(m => m.Health)), "Instrumentation changed modules");
                Require(r.BlindLinkedRounds <= r.LinkedRounds && r.LinkedRounds <= a.Log.Ship(x).Rails, "Impossible link counts");
                if (support == 0) Require(r.LinkedRounds == 0, "Solo battleship cannot receive ally observations");
            }
        }
        Console.WriteLine($"PASS: escort trial fixture/instrumentation ({checks} checks)");
    }
}
