using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using SpaceFleet.Sim;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var argsMap = new Dictionary<string, string>();
for (int i = 0; i < args.Length; i++) argsMap[args[i].TrimStart('-')] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "true";
int[] seeds = argsMap.GetValueOrDefault("seeds", "1").Split(',').Select(int.Parse).ToArray();
double minutes = double.Parse(argsMap.GetValueOrDefault("minutes", "25"));
int jobs = int.Parse(argsMap.GetValueOrDefault("jobs", "1"));
var blueDesign = Enum.Parse<DesignFamily>(argsMap.GetValueOrDefault("blue-design", "Earth"), true);
var redDesign = Enum.Parse<DesignFamily>(argsMap.GetValueOrDefault("red-design", "Earth"), true);
HullKind? duel = argsMap.TryGetValue("duel", out string? duelKind) ? Enum.Parse<HullKind>(duelKind, true) : null;
double startDistance = double.Parse(argsMap.GetValueOrDefault("distance", "150000"));
bool mirror = argsMap.ContainsKey("mirror"), profile = argsMap.ContainsKey("profile") || argsMap.ContainsKey("slow-ticks");
bool tickProfile = profile || argsMap.ContainsKey("tick-profile");
bool telemetry = argsMap.ContainsKey("telemetry");
string output = Path.GetFullPath(argsMap.GetValueOrDefault("output", "shots/batch/battle.csv"));
if(argsMap.ContainsKey("component-trials"))
{ ComponentTrials.Save(output,argsMap.GetValueOrDefault("phase","all"),seeds,jobs,minutes*60,argsMap.ContainsKey("legacy-pd"));return; }
if(argsMap.ContainsKey("cruiser-trials")){CruiserTrials.Save(output);return;}
if(argsMap.ContainsKey("branch-trials")){BranchTrials.Save(output);return;}
if(argsMap.ContainsKey("long-range-trials")){LongRangeTrials.Save(output);return;}
if(argsMap.ContainsKey("escort-trials-check")){EscortTrials.Check();return;}
if(argsMap.ContainsKey("wing-trials-check")){WingTrials.Check();return;}
if(argsMap.ContainsKey("wing-trials"))
{
    WingTrials.Save(output, seeds, argsMap.GetValueOrDefault("distances", "20000,70000,130000").Split(',').Select(double.Parse).ToArray(),
        jobs, minutes, telemetry, argsMap.ContainsKey("primary-only"), argsMap.GetValueOrDefault("timing-tuning"),
        argsMap.GetValueOrDefault("definition-baseline"));
    return;
}
if(argsMap.ContainsKey("escort-trials"))
{
    EscortTrials.Save(output, seeds, argsMap.GetValueOrDefault("distances", "20000,70000,130000").Split(',').Select(double.Parse).ToArray(),
        jobs, minutes, telemetry, argsMap.GetValueOrDefault("gunnery-tuning"), argsMap.ContainsKey("primary-only"));
    return;
}
if(argsMap.ContainsKey("small-hull-trials")){SmallHullTrials.Save(output,argsMap.ContainsKey("geometry-only"));return;}
var battlefield=Enum.Parse<Battlefield>(argsMap.GetValueOrDefault("battlefield","DeepSpace"),true);
int[] fieldSeeds=argsMap.GetValueOrDefault("field-seeds",argsMap.GetValueOrDefault("field-seed","1")).Split(',').Select(int.Parse).Distinct().ToArray();
if(fieldSeeds.Length>1)
{
    // Keep legacy CSV/summary schemas byte-compatible. Each terrain seed gets a
    // separate file set, whose sidecar identifies its independent terrain seed.
    foreach(int fieldSeed in fieldSeeds)
    {
        var child=new ProcessStartInfo(Environment.ProcessPath!) {UseShellExecute=false};
        if(string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath),"dotnet",StringComparison.OrdinalIgnoreCase))child.ArgumentList.Add(typeof(Row).Assembly.Location);
        foreach(var arg in argsMap.Where(x=>x.Key is not ("field-seeds" or "field-seed" or "output")))
        {child.ArgumentList.Add("--"+arg.Key);if(arg.Value!="true")child.ArgumentList.Add(arg.Value);}
        child.ArgumentList.Add("--field-seed");child.ArgumentList.Add(fieldSeed.ToString());
        child.ArgumentList.Add("--output");child.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(output)!,Path.GetFileNameWithoutExtension(output)+$"-field{fieldSeed}"+Path.GetExtension(output)));
        using var process=Process.Start(child)!;process.WaitForExit();if(process.ExitCode!=0)throw new InvalidOperationException("Terrain batch failed: "+fieldSeed);
    }
    return;
}
var tasks = seeds.SelectMany(seed => mirror ? new[] { (Seed: seed, Mirror: false), (Seed: seed, Mirror: true) } : new[] { (Seed: seed, Mirror: false) }).ToArray();
var rows = new ConcurrentBag<Row>();
var watch = Stopwatch.StartNew();
Parallel.ForEach(tasks, new ParallelOptions { MaxDegreeOfParallelism = jobs }, task =>
{
    // A worker may be reused for another battle. Never share/reset another worker's timings.
    SimProfiler.Enabled = profile; SimProfiler.Reset();
    try
    {
        var world = new SimWorld {CoverCostEnabled=argsMap.GetValueOrDefault("cover-cost","true")!="false"};
        var config = new BattleConfig { Seed = task.Seed, Mirror = task.Mirror, BlueDesign=blueDesign, RedDesign=redDesign, StartDistance=startDistance, Objective = Enum.Parse<BattleObjective>(argsMap.GetValueOrDefault("objective", "Annihilation"), true),Battlefield=battlefield,FieldSeed=fieldSeeds[0],
            Engagement=Enum.Parse<Engagement>(argsMap.GetValueOrDefault("engagement","Auto"),true) };
        if (argsMap.TryGetValue("match-hull", out var matchHull))
            config = config with { Match = Matchmaking.Create(task.Seed, matchHull) };
        if(argsMap.TryGetValue("tier-hull",out var tierHull))
            config=config with{Match=Matchmaking.CreateTiered(task.Seed,FleetLoadouts.Default(tierHull),
                FleetLoadouts.Hulls.Select(h=>h.Id).ToHashSet(),new Dictionary<string,IReadOnlySet<string>>())};
        var overrides=new Dictionary<string,string>();
        foreach(var (key,side) in new[]{("blue-escort",true),("red-escort",false)})
            if(argsMap.TryGetValue(key,out var hull))
            {
                if(ShipDefinitions.ById(hull).Kind!=HullKind.Escort)throw new ArgumentException(key+" requires an escort/cruiser hull");
                if(config.Match is not null)throw new ArgumentException("Escort overrides use the canonical fleet, not matchmaking");
                if(duel is not null)overrides[side?"DUEL-B":"DUEL-R"]=hull;
                else foreach(var call in side?new[]{"DD-11","DD-12","DD-31","DD-32","DD-33","DD-34"}:new[]{"DD-X1","DD-X2","DD-X3","DD-X4","DD-X5","DD-X6"})overrides[call]=hull;
            }
        if(overrides.Count>0)config=config with{HullOverrides=overrides};
        foreach(var (key,side) in new[]{("blue-wing",true),("red-wing",false)})
            if(argsMap.TryGetValue(key,out var hull))
            {
                if(ShipDefinitions.ById(hull).Kind!=HullKind.Interceptor||config.Match is not null||duel is not null)
                    throw new ArgumentException(key+" requires a small hull in the canonical fleet");
                for(int i=1;i<=5;i++)overrides[side?$"IC-2{i}":$"IC-X{i}"]=hull;
            }
        if(overrides.Count>0)config=config with{HullOverrides=overrides};
        if(argsMap.TryGetValue("specialist-reach-scale",out var reachScale))
        {
            if(duel is not null || argsMap.ContainsKey("small-geometry"))
                throw new ArgumentException("Specialist timing requires the canonical fleet and current geometry");
            world=SmallHullTrials.Fleet(config,false,float.Parse(reachScale));
            world.CoverCostEnabled=argsMap.GetValueOrDefault("cover-cost","true")!="false";
        }
        else if(argsMap.TryGetValue("small-geometry",out var geometry))
        {
            if(duel is not null || geometry is not ("legacy" or "current"))throw new ArgumentException("--small-geometry requires legacy/current and the canonical fleet");
            world=SmallHullTrials.Fleet(config,geometry=="legacy");
            world.CoverCostEnabled=argsMap.GetValueOrDefault("cover-cost","true")!="false";
        }
        else if (duel is { } kind) BattleSetup.SpawnDuel(world, config, kind); else BattleSetup.Spawn(world, config);
        var run = Stopwatch.StartNew();
        var ticks = tickProfile ? new List<double>() : null;
        var sections = profile ? new SectionProfile() : null;
        var slowTicks = argsMap.ContainsKey("slow-ticks") ? new SlowTickProfile() : null;
        var snapshots = telemetry ? new BalanceTelemetry(task.Seed, task.Mirror) : null;
        while (world.Time < minutes * 60 && world.Rules!.Outcome is null)
        {
            long start = tickProfile ? Stopwatch.GetTimestamp() : 0;
            world.Step();
            slowTicks?.Capture(world.Tick);
            if (tickProfile) ticks!.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            sections?.Capture(world.Tick);
            snapshots?.Capture(world);
        }
        sections?.Capture(world.Tick, final: true);
        world.Log!.Finish();
        slowTicks?.Save(Path.ChangeExtension(output,$".{task.Seed}-{task.Mirror}.slow-ticks.csv"),world);
        var row = new Row(task.Seed, task.Mirror, world, run.Elapsed.TotalSeconds, ticks, sections, snapshots);
        rows.Add(row);
        Console.WriteLine($"done {task.Seed}/{(task.Mirror ? "mirror" : "normal")} {Deployment.Resolve(config)}: {world.Log.Summary()} wall={row.Wall:F1}s");
        if (argsMap.ContainsKey("trace")) foreach(var e in world.Log.Events) Console.WriteLine(e);
    }
    finally { SimProfiler.Enabled = false; SimProfiler.Reset(); }
});
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(Path.ChangeExtension(output,".field.json"),JsonSerializer.Serialize(new {battlefield=battlefield.ToString(),fieldSeed=fieldSeeds[0],
    obstacleCount=rows.First().World.Obstacles.Count,rocks=rows.First().World.Asteroids?.Rocks.Count??0,regenerations=rows.First().World.Asteroids?.Regenerations??0},new JsonSerializerOptions{WriteIndented=true}));
var ordered = rows.OrderBy(r => r.Seed).ThenBy(r => r.Mirror).ToArray();
if (argsMap.ContainsKey("match-hull")||argsMap.ContainsKey("tier-hull"))
    File.WriteAllText(Path.ChangeExtension(output,".losses.json"),JsonSerializer.Serialize(ordered.Select(r => new {
        r.Seed, r.Mirror, Ships=r.World.Ships.Count, Outcome=r.World.Rules!.Outcome,
        Destroyed=r.World.Ships.Count(s=>s.Damage.Destroyed), Disabled=r.World.Ships.Count(s=>s.Damage.Disabled),
        Repairs=r.World.Ships.Sum(s=>s.DamageControl.Completed),
        Causes=r.World.Ships.Where(s=>s.Damage.Destroyed).GroupBy(s=>s.Damage.Destruction).ToDictionary(g=>g.Key.ToString(),g=>g.Count()),
        Hulls=r.World.Ships.GroupBy(s=>s.Definition.Id).Select(g=>new{Hull=g.Key,Ships=g.Count(),Destroyed=g.Count(s=>s.Damage.Destroyed),Disabled=g.Count(s=>s.Damage.Disabled),Repairs=g.Sum(s=>s.DamageControl.Completed)}).ToArray()
    }),new JsonSerializerOptions{WriteIndented=true}));
if (telemetry)
    File.WriteAllLines(Path.ChangeExtension(output, ".telemetry.csv"), new[] { BalanceTelemetry.Header }
        .Concat(ordered.SelectMany(r => r.Telemetry!.Rows)), new System.Text.UTF8Encoding(false));
File.WriteAllLines(output, new[] { Row.Header }.Concat(ordered.Select(r => r.Csv())), new System.Text.UTF8Encoding(false));
var summaries = ordered.Select(r => new SummaryRow(r.Seed, r.Mirror, r.World.Log!.Summary())).ToArray();
File.WriteAllText(Path.ChangeExtension(output, ".summaries.json"), JsonSerializer.Serialize(summaries,
    new JsonSerializerOptions { WriteIndented = true }), new System.Text.UTF8Encoding(false));
if (argsMap.TryGetValue("compare-summaries", out string? reference))
{
    var expected = JsonSerializer.Deserialize<SummaryRow[]>(File.ReadAllText(reference))
        ?? throw new InvalidDataException("Empty summary reference");
    var byBattle = expected.ToDictionary(s => (s.Seed, s.Mirror));
    var actualKeys = summaries.Select(s => (s.Seed, s.Mirror)).ToHashSet();
    var differences = summaries.Where(s => !byBattle.TryGetValue((s.Seed, s.Mirror), out var old) || old.Summary != s.Summary).ToArray();
    if (expected.Length != summaries.Length || actualKeys.Count != summaries.Length ||
        !actualKeys.SetEquals(byBattle.Keys) || differences.Length > 0)
        throw new InvalidOperationException($"Battle summary mismatch: expected {expected.Length}, actual {summaries.Length}; " +
            string.Join(", ", differences.Select(s => $"{s.Seed}/{s.Mirror}")));
    Console.WriteLine($"PASS: {summaries.Length}/{summaries.Length} battle summaries match {reference} byte for byte");
}
if (profile)
{
    var sections = ordered.SelectMany(r => r.Sections!.Rows(r.Seed, r.Mirror, r.World)).ToArray();
    string sectionOutput = Path.ChangeExtension(output, ".sections.csv");
    File.WriteAllLines(sectionOutput, new[] { SectionRow.Header }.Concat(sections.Select(s => s.Csv())), new System.Text.UTF8Encoding(false));
    foreach (var scope in sections.GroupBy(s => s.Scope))
        Console.WriteLine($"sections {scope.Key} weighted us/tick: " + string.Join(" ", scope.GroupBy(s => s.Section)
            .Select(g => $"{g.Key}={g.Sum(s => s.TotalMs) * 1000 / g.Sum(s => s.Ticks):F2}")));
    Console.WriteLine($"section csv={sectionOutput}; Tick includes all sections; AI includes its four child sections.");
    if (jobs > 1) Console.WriteLine("Profile warning: parallel battles contend for CPU; use --jobs 1 for latency baselines.");
}
// Capture at the verdict, before optional post-outcome stability simulation changes surviving ships.
string shipOutput = Path.ChangeExtension(output, ".ships.csv");
File.WriteAllLines(shipOutput, new[] { "seed,mirror,faction,callsign,kind,destroyed,disabled,operational,shieldFraction,moduleHealthFraction,propulsionFraction,weaponsFraction,rails,railHits,railRoundsRemaining,missiles,missileHits,missilesRemaining,torpedoes,torpedoHits,torpedoesRemaining,amFailures,amJettisons,shieldDamageReceived,moduleDamageReceived,modulesDestroyedInflicted,heatFraction,design,hullId,shieldDamageInflicted,moduleDamageInflicted,firstDisabled,firstDestroyed,survivalSeconds,railRangeSum,railRangeSamples,missilesIntercepted,torpedoesIntercepted,probesLaunched,probeContacts,probeIdentifications,probeKills,firstAction,firstAssault,wingMission,wingChanges" }
    .Concat(ordered.SelectMany(row => row.World.Ships.Select(ship =>
    {
        var stats = row.World.Log!.Ship(ship);
        string F(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);
        return string.Join(',', new[] { row.Seed.ToString(), row.Mirror ? "1" : "0", ship.Faction.ToString(), ship.Callsign, ship.Class.Kind.ToString(),
            ship.Damage.Destroyed ? "1" : "0", ship.Damage.Disabled ? "1" : "0", !ship.Damage.Destroyed && !ship.Damage.Disabled ? "1" : "0",
            F(ship.Damage.Shield / ship.Definition.Shield.Capacity), F(ship.Damage.Modules.Sum(m => m.Health) / ship.Damage.Modules.Sum(m => m.Definition.HitPoints)),
            F(ship.Damage.PropulsionFraction), F(ship.Damage.WeaponsFraction), stats.Rails.ToString(), stats.RailHits.ToString(), ship.Railguns.Sum(g => g.Rounds).ToString(),
            stats.Missiles.ToString(), stats.MissileHits.ToString(), ship.Ordnance.Missiles.ToString(), stats.Torpedoes.ToString(), stats.TorpedoHits.ToString(),
            ship.Ordnance.Antimatter.Rounds.ToString(), ship.Ordnance.Antimatter.Failures.ToString(), ship.Ordnance.Antimatter.Jettisons.ToString(),
            F(stats.ShieldDamage), F(stats.ModuleDamage), stats.ModulesDestroyed.ToString(), F(ship.Power.HeatFraction),
            ship.Definition.Design.ToString(),ship.Definition.Id,F(stats.ShieldDamageInflicted),F(stats.ModuleDamageInflicted),
            stats.DisabledAt?.ToString("0.000") ?? "",stats.DestroyedAt?.ToString("0.000") ?? "",
            F(Math.Min(stats.DisabledAt ?? row.World.Time,stats.DestroyedAt ?? row.World.Time)),
            F(stats.RailRangeSum),stats.RailRangeSamples.ToString(),stats.MissilesIntercepted.ToString(),stats.TorpedoesIntercepted.ToString(),
            ship.Ordnance.Interceptor.Launched.ToString(), ship.Ordnance.Interceptor.NewContacts.ToString(),
            ship.Ordnance.Interceptor.NewIdentifications.ToString(), ship.Ordnance.Interceptor.ProbeKills.ToString(),
            ship.Ordnance.Interceptor.FirstAction?.ToString("0.000", CultureInfo.InvariantCulture) ?? "",
            ship.Ordnance.Interceptor.FirstAssault?.ToString("0.000", CultureInfo.InvariantCulture) ?? "",
            ship.Squadron?.Mission.ToString() ?? "", ship.Squadron?.MissionChanges.ToString() ?? "" });
    }))), new System.Text.UTF8Encoding(false));
File.WriteAllLines(Path.ChangeExtension(output, ".probes.csv"), new[] { "seed,mirror,id,owner,faction,launchedAt,lifetime,destroyed,contacts,identifications" }
    .Concat(ordered.SelectMany(r => r.World.ProbeFlights.Select(p => FormattableString.Invariant(
        $"{r.Seed},{(r.Mirror ? 1 : 0)},{p.Id},{p.Owner},{p.Faction},{p.LaunchedAt:F3},{p.Lifetime:F3},{(p.Destroyed ? 1 : 0)},{p.Contacts},{p.Identifications}")))));
string flightOutput = Path.ChangeExtension(output, ".am.csv");
File.WriteAllLines(Path.ChangeExtension(output,".cover.csv"),new[]{"seed,mirror,enabled,pathMeters,pathExposureMeters,exposureSum,samples,entrants,arrivals,lostBeforeArrival,budgetFallbacks"}
    .Concat(ordered.Select(r=>{var e=r.World.Log!.Environment;return $"{r.Seed},{(r.Mirror?1:0)},{r.World.CoverCostEnabled},{e.CoverPathLength:F3},{e.CoverPathExposure:F3},{e.ExposureSum:F3},{e.ExposureSamples},{e.AssaultEntrants},{e.AssaultArrivals},{e.AssaultLostBeforeArrival},{e.CoverBudgetFallbacks}";})));
File.WriteAllLines(Path.ChangeExtension(output,".sight.csv"),new[]{"seed,mirror,blockedShipSeconds,attempts,starts,successes,failures,reopenSeconds"}
    .Concat(ordered.Select(row=>{var e=row.World.Log!.Environment;return $"{row.Seed},{(row.Mirror?1:0)},{e.BlockedFireSeconds:F3},{e.SightAttempts},{e.SightStarts},{e.SightSuccesses},{e.SightFailures},{e.SightReopenSeconds:F3}";})));
File.WriteAllLines(Path.ChangeExtension(output,".navigation.csv"),new[]{"seed,mirror,fieldSeed,regenerations,buildMs,plans,failures,expanded,planMs,stuckReplans,formationCompressions"}
    .Concat(ordered.Select(row=>{var n=row.World.Log!.Environment;return $"{row.Seed},{(row.Mirror?1:0)},{row.World.FieldSeed},{row.World.Asteroids?.Regenerations??0},{n.NavigationBuildMs:F3},{n.PathPlans},{n.PathFailures},{n.PathExpanded},{n.PathMilliseconds:F3},{n.StuckReplans},{n.FormationCompressions}";})));
File.WriteAllLines(Path.ChangeExtension(output,".navigation-jobs.csv"),new[]{"seed,mirror,completed,pending,queuePeak,cancelled,workPeak,sliceMaxMs,meanLatencySeconds,maxLatencySeconds"}
    .Concat(ordered.Select(row=>{var n=row.World.Log!.Environment;return $"{row.Seed},{(row.Mirror?1:0)},{n.PathPlans},{row.World.PendingNavigation},{n.PathQueuePeak},{n.PathCancelled},{n.PathWorkPeak},{n.PathSliceMaxMs:F3},{n.PathLatencySeconds/Math.Max(1,n.PathPlans):F6},{n.PathLatencyMaxSeconds:F6}";})));
File.WriteAllLines(Path.ChangeExtension(output,".environment.csv"),new[]{"seed,mirror,shipCollisions,collisionEnergyJ,collisionModuleDamage,stuckDetections,blueLockLosses,blueLostSeconds,blueReacquisitions,redLockLosses,redLostSeconds,redReacquisitions,blockedRails,blockedMissiles,blockedTorpedoes,blockedProbes"}
    .Concat(ordered.Select(row=> { var e=row.World.Log!.Environment; var b=row.World.Log.SensorOcclusion(Faction.Blue); var r=row.World.Log.SensorOcclusion(Faction.Red);
        return $"{row.Seed},{(row.Mirror?1:0)},{e.ShipCollisions},{e.CollisionEnergy:F3},{e.CollisionDamage:F3},{e.StuckDetections},{b.LockLosses},{b.LockLostSeconds:F3},{b.Reacquisitions},{r.LockLosses},{r.LockLostSeconds:F3},{r.Reacquisitions},{e.BlockedRails},{e.BlockedMissiles},{e.BlockedTorpedoes},{e.BlockedProbes}"; })));
File.WriteAllLines(Path.ChangeExtension(output,".environment-events.csv"),new[]{"seed,mirror,time,kind,source,obstacle,x,y,z"}
    .Concat(ordered.SelectMany(row=>row.World.Log!.Environment.Events.Select(e=>
        $"{row.Seed},{(row.Mirror?1:0)},{e.Time:F3},{e.Kind},{e.Source},{e.Obstacle},{e.Position.X:F3},{e.Position.Y:F3},{e.Position.Z:F3}"))));
File.WriteAllLines(flightOutput, new[] { "seed,mirror,faction,shooter,target,targetKind,launchTime,range,flightSeconds,path,seekerSeconds,closestHull,outcome,hitShip" }
    .Concat(ordered.SelectMany(row => row.World.Log!.AntimatterFlights.Select(f =>
        $"{row.Seed},{(row.Mirror ? 1 : 0)},{f.Faction},{f.Shooter},{f.Target},{f.TargetKind},{f.LaunchTime:F3},{f.Range:F1},{f.FlightSeconds:F3},{f.TravelMeters:F1},{f.SeekerSeconds:F3},{f.ClosestHull:F1},{f.Outcome},{f.HitShip}"))));
foreach(var group in ordered.SelectMany(r=>r.World.Log!.AntimatterFlights).GroupBy(f=>f.Outcome))
    Console.WriteLine($"AM {group.Key}: {group.Count()} (mean launch {group.Average(f=>f.Range):F0}m, closest hull {group.Average(f=>f.ClosestHull):F1}m)");
Console.WriteLine($"AM InFlight at outcome: {ordered.Sum(r=>r.World.Missiles.Count(m=>m.Assault is not null))}");
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
if (tickProfile)
{
    // Exact pooled percentiles, not an average of each battle's percentile.
    var pooled = new List<string> { "scope,ticks,meanMs,p50Ms,p95Ms,p99Ms,maxMs" };
    string Pooled(string scope, IEnumerable<double> samples)
    {
        double[] sorted = samples.OrderBy(x => x).ToArray();
        if (sorted.Length == 0) return $"{scope},0,,,,,";
        double P(double percentile) => sorted[(int)Math.Ceiling(sorted.Length*percentile)-1];
        return $"{scope},{sorted.Length},{sorted.Average():F6},{P(.5):F6},{P(.95):F6},{P(.99):F6},{sorted[^1]:F6}";
    }
    pooled.Add(Pooled("All", ordered.SelectMany(r => r.Samples())));
    foreach (BattlePhase phase in Enum.GetValues<BattlePhase>())
        pooled.Add(Pooled(phase.ToString(), ordered.SelectMany(r => r.Samples(phase))));
    File.WriteAllLines(Path.ChangeExtension(output, ".ticks.csv"), pooled, new System.Text.UTF8Encoding(false));
}
if (tickProfile) foreach (var row in ordered)
{
    Console.WriteLine($"step {row.Seed}/{row.Mirror} All: {Row.Describe(row.Samples())}");
    foreach (BattlePhase phase in Enum.GetValues<BattlePhase>())
        Console.WriteLine($"step {row.Seed}/{row.Mirror} {phase}: {Row.Describe(row.Samples(phase))}");
}
double postSeconds = double.Parse(argsMap.GetValueOrDefault("post-seconds", "0"));
if (postSeconds > 0) foreach (var row in ordered)
{
    var world = row.World;
    var outcome = world.Rules!.Outcome;
    int intervals = world.Log!.Intervals.Count, strength = world.Log.Strength.Count;
    int[] Sizes() => new[] {world.Projectiles.Count,world.Missiles.Count,world.Impacts.Count,world.OrdnanceEvents.Count,world.Log.Events.Count};
    int[] before=Sizes(), peak=(int[])before.Clone();
    double end=world.Time+postSeconds;
    while(world.Time<end)
    {
        world.Step();
        int[] sizes=Sizes();for(int i=0;i<peak.Length;i++)peak[i]=Math.Max(peak[i],sizes[i]);
    }
    if(world.Rules.Outcome!=outcome || world.Log.Intervals.Count!=intervals || world.Log.Strength.Count!=strength
        || peak[2]>64 || peak[4]>BattleLog.EventCapacity || world.Impacts.Any(e=>world.Time-e.Time>3.001)
        || world.OrdnanceEvents.Any(e=>world.Time-e.Time>3.001)) throw new InvalidOperationException("Post-outcome lists or frozen records exceeded bounds");
    Console.WriteLine($"post {row.Seed}/{row.Mirror} +{postSeconds:F0}s: projectile/missile/impact/ordnance/battleEvents before={string.Join('/',before)} peak={string.Join('/',peak)} after={string.Join('/',Sizes())}; intervals={intervals}; dropped={world.Log.DroppedEvents}; PASS");
}

sealed record Row(int Seed, bool Mirror, SimWorld World, double Wall, List<double>? TickMs, SectionProfile? Sections, BalanceTelemetry? Telemetry)
{
    public static readonly string[] Metrics = { "Contact", "Identified", "Locked", "MissileLaunch", "MissileHit", "RailLaunch", "RailHit", "ModuleDestroyed", "Disabled", "Destroyed", "Close" };
    private static readonly string[] Names = { "contact", "identified", "locked", "missileLaunch", "missileHit", "railLaunch", "railHit", "moduleDestroyedVictim", "disabledVictim", "destroyedVictim", "close" };
    public static string Header => "seed,mirror,winner,reason,outcomeTime,wallSeconds,friendlyCollisions," + string.Join(',',
        Enum.GetValues<Faction>().SelectMany(f => Names.Select(n => $"{f}_{n}").Concat(new[] { $"{f}_missiles", $"{f}_missileHits", $"{f}_rails", $"{f}_railHits",
            $"{f}_torpedoes", $"{f}_torpedoHits", $"{f}_amFailures", $"{f}_amJettisons" })))
        + "," + string.Join(',', Enum.GetValues<BattlePhase>().Select(p => $"{p}_seconds")) + ",stepMeanMs,stepP99Ms,stepMaxMs"
        + "," + string.Join(',', Enum.GetValues<BattlePhase>().SelectMany(p=>new[]{ $"{p}_stepMeanMs",$"{p}_stepP99Ms",$"{p}_stepMaxMs" }));
    public IEnumerable<double> Samples(BattlePhase? phase = null)
    {
        if(TickMs is null)yield break;
        if(phase is null){foreach(double sample in TickMs)yield return sample;yield break;}
        foreach(var interval in World.Log!.Intervals.Where(i=>i.Phase==phase))
        {
            int start=Math.Clamp((int)Math.Round(interval.Start*SimWorld.TickRate),0,TickMs.Count);
            int end=Math.Clamp((int)Math.Round((interval.Start+interval.Duration)*SimWorld.TickRate),0,TickMs.Count);
            for(int i=start;i<end;i++)yield return TickMs[i];
        }
    }
    private static string[] Statistics(IEnumerable<double> samples)
    {
        double[] sorted=samples.OrderBy(n=>n).ToArray();
        return sorted.Length==0 ? new[]{"","",""} : new[]{N(sorted.Average()),N(sorted[(int)Math.Ceiling(sorted.Length*.99)-1]),N(sorted[^1])};
    }
    public static string Describe(IEnumerable<double> samples)
    {var s=Statistics(samples);return s[0]==""?"no ticks":$"mean={s[0]} p99={s[1]} max={s[2]} ms";}
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
        {
            var s = World.Log.Side(f); values.AddRange(s.FirstTimes.Select(N));
            values.AddRange(new[] { s.Missiles, s.MissileHits, s.Rails, s.RailHits, s.Torpedoes, s.TorpedoHits,
                (int)World.Ships.Where(ship=>ship.Faction==f).Sum(ship=>ship.Ordnance.Antimatter.Failures),
                (int)World.Ships.Where(ship=>ship.Faction==f).Sum(ship=>ship.Ordnance.Antimatter.Jettisons) }.Select(n => n.ToString()));
        }
        values.AddRange(Enum.GetValues<BattlePhase>().Select(p => N(World.Log.PhaseSeconds(p))));
        values.AddRange(Statistics(Samples()));
        foreach(BattlePhase phase in Enum.GetValues<BattlePhase>())values.AddRange(Statistics(Samples(phase)));
        return string.Join(',', values);
    }
}
