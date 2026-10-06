using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using SpaceFleet.Sim;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var argsMap = new Dictionary<string, string>();
for (int i = 0; i < args.Length; i++) argsMap[args[i].TrimStart('-')] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "true";
int[] seeds = argsMap.GetValueOrDefault("seeds", "1").Split(',').Select(int.Parse).ToArray();
double minutes = double.Parse(argsMap.GetValueOrDefault("minutes", "25"));
int jobs = int.Parse(argsMap.GetValueOrDefault("jobs", "1"));
bool mirror = argsMap.ContainsKey("mirror"), profile = argsMap.ContainsKey("profile");
string output = Path.GetFullPath(argsMap.GetValueOrDefault("output", "shots/batch/battle.csv"));
var tasks = seeds.SelectMany(seed => mirror ? new[] { (Seed: seed, Mirror: false), (Seed: seed, Mirror: true) } : new[] { (Seed: seed, Mirror: false) }).ToArray();
var rows = new ConcurrentBag<Row>();
var watch = Stopwatch.StartNew();
Parallel.ForEach(tasks, new ParallelOptions { MaxDegreeOfParallelism = jobs }, task =>
{
    var world = new SimWorld();
    BattleSetup.Spawn(world, new BattleConfig { Seed = task.Seed, Mirror = task.Mirror });
    var run = Stopwatch.StartNew();
    var ticks = profile ? new List<double>() : null;
    while (world.Time < minutes * 60 && world.Rules!.Outcome is null)
    {
        long start = profile ? Stopwatch.GetTimestamp() : 0;
        world.Step();
        if (profile) ticks!.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }
    world.Log!.Finish();
    var row = new Row(task.Seed, task.Mirror, world, run.Elapsed.TotalSeconds, ticks);
    rows.Add(row);
    Console.WriteLine($"done {task.Seed}/{(task.Mirror ? "mirror" : "normal")}: {world.Log.Summary()} wall={row.Wall:F1}s");
});
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var ordered = rows.OrderBy(r => r.Seed).ThenBy(r => r.Mirror).ToArray();
File.WriteAllLines(output, new[] { Row.Header }.Concat(ordered.Select(r => r.Csv())), new System.Text.UTF8Encoding(false));
foreach (string metric in Row.Metrics.Concat(new[] { "Outcome", "Wall" }))
{
    PrintMetric(metric, ordered.Select(r => r.Value(metric)));
    if (Row.Metrics.Contains(metric)) foreach (Faction f in Enum.GetValues<Faction>())
        PrintMetric($"{f} {metric}", ordered.Select(r => r.World.Log!.Side(f).FirstTimes[Array.IndexOf(Row.Metrics, metric)]));
}
void PrintMetric(string name, IEnumerable<double?> samples)
{
    double[] values = samples.Where(v => v.HasValue).Select(v => v!.Value).OrderBy(v => v).ToArray();
    if (values.Length == 0) { Console.WriteLine($"{name}: no events"); return; }
    double median = (values[(values.Length - 1) / 2] + values[values.Length / 2]) * 0.5;
    Console.WriteLine($"{name}: median={median:F2} min={values[0]:F2} max={values[^1]:F2} occurred={values.Length}/{ordered.Length}");
}
foreach (Faction faction in Enum.GetValues<Faction>())
    Console.WriteLine($"wins {faction}: {ordered.Count(r => r.World.Rules!.Outcome?.Winner == faction)}/{ordered.Length}");
Console.WriteLine($"draws: {ordered.Count(r => r.World.Rules!.Outcome is { Winner: null })}/{ordered.Length}; unresolved: {ordered.Count(r => r.World.Rules!.Outcome is null)}");
foreach (bool flipped in new[] { false, true })
{
    var subset = ordered.Where(r => r.Mirror == flipped).ToArray();
    if (subset.Length > 0) Console.WriteLine($"position {(flipped ? "mirror" : "normal")}: Blue {subset.Count(r => r.World.Rules!.Outcome?.Winner == Faction.Blue)}/{subset.Length}, Red {subset.Count(r => r.World.Rules!.Outcome?.Winner == Faction.Red)}/{subset.Length}");
}
foreach (BattlePhase phase in Enum.GetValues<BattlePhase>()) Console.WriteLine($"phase {phase} >=60s: {ordered.Count(r => r.World.Log!.PhaseSeconds(phase) >= 60)}/{ordered.Length}");
Console.WriteLine($"friendly collisions: mean={ordered.Average(r => r.World.Log!.FriendlyCollisions):F2}");
Console.WriteLine($"batch wall={watch.Elapsed.TotalSeconds:F1}s jobs={jobs} csv={output}");

sealed record Row(int Seed, bool Mirror, SimWorld World, double Wall, List<double>? TickMs)
{
    public static readonly string[] Metrics = { "Contact", "Identified", "Locked", "MissileLaunch", "MissileHit", "RailLaunch", "RailHit", "ModuleDestroyed", "Disabled", "Destroyed", "Close" };
    private static readonly string[] Names = { "contact", "identified", "locked", "missileLaunch", "missileHit", "railLaunch", "railHit", "moduleDestroyedVictim", "disabledVictim", "destroyedVictim", "close" };
    public static string Header => "seed,mirror,winner,reason,outcomeTime,wallSeconds,friendlyCollisions," + string.Join(',',
        Enum.GetValues<Faction>().SelectMany(f => Names.Select(n => $"{f}_{n}").Concat(new[] { $"{f}_missiles", $"{f}_missileHits", $"{f}_rails", $"{f}_railHits" })))
        + "," + string.Join(',', Enum.GetValues<BattlePhase>().Select(p => $"{p}_seconds")) + ",stepMeanMs,stepP99Ms,stepMaxMs";
    private static string N(double? value) => value?.ToString("0.000", CultureInfo.InvariantCulture) ?? "";
    public double? Value(string name)
    {
        var blue = World.Log!.Side(Faction.Blue); var red = World.Log.Side(Faction.Red);
        double? Earliest(double? a, double? b) => a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);
        int index = Array.IndexOf(Metrics, name);
        return index >= 0 ? Earliest(blue.FirstTimes[index], red.FirstTimes[index]) : name == "Outcome" ? World.Rules!.Outcome?.Time : Wall;
    }
    public string Csv()
    {
        var outcome = World.Rules!.Outcome;
        var values = new List<string> { Seed.ToString(), Mirror ? "1" : "0", outcome is null ? "Unresolved" : outcome.Winner?.ToString() ?? "Draw", outcome?.Reason ?? "", N(outcome?.Time), N(Wall), World.Log!.FriendlyCollisions.ToString() };
        foreach (Faction f in Enum.GetValues<Faction>())
        { var s = World.Log.Side(f); values.AddRange(s.FirstTimes.Select(N)); values.AddRange(new[] { s.Missiles, s.MissileHits, s.Rails, s.RailHits }.Select(n => n.ToString())); }
        values.AddRange(Enum.GetValues<BattlePhase>().Select(p => N(World.Log.PhaseSeconds(p))));
        double[] sorted = TickMs?.OrderBy(n => n).ToArray() ?? Array.Empty<double>();
        values.AddRange(sorted.Length == 0 ? new[] { "", "", "" } : new[] { N(sorted.Average()), N(sorted[(int)Math.Ceiling(sorted.Length * 0.99) - 1]), N(sorted[^1]) });
        return string.Join(',', values);
    }
}
