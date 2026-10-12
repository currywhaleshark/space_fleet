using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using SpaceFleet.Sim;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var options = new Dictionary<string, string>();
for (int i = 0; i < args.Length; i += 2) options.Add(args[i].TrimStart('-'), args[i + 1]);
int[] groups = options.GetValueOrDefault("groups", "1,2,4,10").Split(',').Select(int.Parse).ToArray();
int warmup = int.Parse(options.GetValueOrDefault("warmup", "15")) * 60;
int ticks = int.Parse(options.GetValueOrDefault("seconds", "60")) * 60;
double distance = double.Parse(options.GetValueOrDefault("distance", "40000"));
int seed = int.Parse(options.GetValueOrDefault("seed", "1"));
int repeats = int.Parse(options.GetValueOrDefault("repeats", "3"));
if (groups.Any(n => n < 1 || n > 100) || warmup < 0 || ticks < 1 || repeats < 1)
    throw new ArgumentException("Positive fleet groups, measurement duration and repetitions required.");
string output = Path.GetFullPath(options.GetValueOrDefault("output", "shots/scale/scale.json"));
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var results = new List<object>();
foreach (int count in groups)
for (int repeat = 0; repeat < repeats; repeat++)
{
    var world = Create(count);
    SimProfiler.Enabled = false;
    for (int tick = 0; tick < warmup; tick++) world.Step();
    var samples = new double[ticks];
    int rails = 0, missiles = 0, probes = 0;
    long allocation = 0;
    SimProfiler.Enabled = true; SimProfiler.Reset();
    for (int tick = 0; tick < ticks; tick++)
    {
        long memory = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
        world.Step();
        samples[tick] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        allocation += GC.GetAllocatedBytesForCurrentThread() - memory;
        rails = Math.Max(rails, world.Projectiles.Count); missiles = Math.Max(missiles, world.Missiles.Count);
        probes = Math.Max(probes, world.Probes.Count);
    }
    var sections = SimProfiler.Snapshot().ToDictionary(x => x.Key.ToString(), x => x.Value.TotalMs * 1000 / ticks);
    SimProfiler.Enabled = false;
    Array.Sort(samples);
    double Percentile(double p) => samples[Math.Clamp((int)Math.Ceiling(samples.Length * p) - 1, 0, samples.Length - 1)];
    world.Log!.Finish();
    string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(world.Log.Summary())));
    double meanMs = samples.Average(), p99Ms = Percentile(.99);
    results.Add(new { groups = count, ships = world.Ships.Count, repeat, seed, warmupTicks = warmup, measuredTicks = ticks,
        distanceMeters = distance, meanMs, p95Ms = Percentile(.95), p99Ms, maxMs = samples[^1],
        allocatedBytesPerTick = allocation / (double)ticks, peakRails = rails, peakMissiles = missiles, peakProbes = probes,
        activeAtEnd = world.Ships.Count(s => !s.Damage.Destroyed && !s.Damage.Disabled), sectionsUs = sections, summarySha256 = digest });
    File.WriteAllText(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"ships={world.Ships.Count} repeat={repeat} mean={meanMs:F3} p99={p99Ms:F3}ms rails={rails} missiles={missiles} hash={digest[..12]}");
}

SimWorld Create(int count)
{
    // Synthetic shared battlefield: duplicate the canonical 12-per-side rosters,
    // each with all three squadrons. No production roster, weapon or AI edits.
    var world = new SimWorld();
    for (int group = 0; group < count; group++)
    {
        var template = new SimWorld();
        BattleSetup.Spawn(template, new BattleConfig { Seed = seed + group, StartDistance = distance });
        var map = new Dictionary<ShipBody, ShipBody>();
        var offset = new Vec3d((group % 5) * 14000, (group / 5) * 10000, 0);
        foreach (var old in template.Ships)
        {
            var ship = world.Add(new ShipBody($"G{group}-{old.Callsign}", old.Definition, old.Faction));
            ship.Place(old.Position + offset, old.Orientation);
            world.AttachBrain(ship, ShipOrder.HoldAt(ship.Position)); map.Add(old, ship);
        }
        foreach (var squad in template.Squadrons)
            world.AddSquadron($"G{group}-{squad.Name}", squad.Faction, squad.Role, squad.Members.Select(s => map[s]));
    }
    world.Doctrine = new FleetDoctrine();
    world.EnableCommander(Faction.Red); world.EnableCommander(Faction.Blue);
    world.Rules = new BattleRules(world, BattleObjective.Annihilation);
    world.Sensors.Update(world.Ships, 0, force: true);
    world.Log = new BattleLog(world);
    return world;
}
