using System.Collections.Concurrent;
using System.Globalization;
using SpaceFleet.Sim;

// Traces every probe launched in real tiered matches: when, how far from the enemy, and what it found.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
var hulls = args.Length > 0 ? args[0].Split(',') : new[] { "interceptor", "mars_interceptor", "earth_recon_t3", "mars_assault_t3", "battleship", "mars_battleship" };
int[] seeds = args.Length > 1 ? args[1].Split(',').Select(int.Parse).ToArray() : new[] { 1, 2 };
double seconds = args.Length > 2 ? double.Parse(args[2]) : 200;
var course = args.Length > 3 ? Enum.Parse<Engagement>(args[3]) : Engagement.Auto;
var height = args.Length > 4 ? Enum.Parse<Elevation>(args[4]) : Elevation.Auto;
var field = args.Length > 5 ? Enum.Parse<Battlefield>(args[5]) : Battlefield.DeepSpace;
var rows = new ConcurrentBag<string>();
var tasks = hulls.SelectMany(h => seeds.Select(s => (Hull: h, Seed: s))).ToArray();
Parallel.ForEach(tasks, new ParallelOptions { MaxDegreeOfParallelism = 6 }, task =>
{
    var match = Matchmaking.CreateTiered(task.Seed, FleetLoadouts.Default(task.Hull), FleetLoadouts.Hulls.Select(h => h.Id).ToHashSet(),
        new Dictionary<string, IReadOnlySet<string>>());
    var w = new SimWorld(); BattleSetup.Spawn(w, new BattleConfig { Match = match, Seed = task.Seed, Engagement = course, Elevation = height, Battlefield = field, FieldSeed = task.Seed });
    var seen = new Dictionary<uint, (ReconProbe Probe, double NearestCap, double NearestAny, int KnownCaps, int KnownAny, string Mission)>();
    var arrival = new Dictionary<uint, double>();
    // Contact discovery times per side, to compare probes against the fleet's own sensors.
    var firstContact = new Dictionary<(Faction, string), double>();
    var firstIdent = new Dictionary<(Faction, string), double>();
    int tick = 0;
    while (w.Time < seconds && w.Rules!.Outcome is null)
    {
        w.Step(); tick++;
        foreach (var p in w.Probes)
        {
            if (!seen.ContainsKey(p.Id))
            {
                var enemies = w.Ships.Where(s => s.Faction != p.Faction && !s.Damage.Destroyed).ToArray();
                double cap = enemies.Where(s => s.Class.Kind != HullKind.Interceptor).Select(s => (s.Position - p.Owner.Position).Length()).DefaultIfEmpty(double.NaN).Min();
                double any = enemies.Select(s => (s.Position - p.Owner.Position).Length()).DefaultIfEmpty(double.NaN).Min();
                int knownCaps = enemies.Count(s => s.Class.Kind != HullKind.Interceptor && w.Sensors.Track(p.Faction, s).Level >= TrackLevel.Identified);
                int knownAny = enemies.Count(s => w.Sensors.Track(p.Faction, s).Level >= TrackLevel.Contact);
                seen[p.Id] = (p, cap, any, knownCaps, knownAny, p.Owner.Squadron?.Activity ?? "");
            }
            if (!arrival.ContainsKey(p.Id) && p.OnStation)
                arrival[p.Id] = w.Ships.Where(s => s.Faction != p.Faction && !s.Damage.Destroyed && s.Class.Kind != HullKind.Interceptor)
                    .Select(s => (s.Position - p.Position).Length()).DefaultIfEmpty(double.NaN).Min();
        }
        if (tick % 60 == 0)
            foreach (var s in w.Ships)
                foreach (Faction side in new[] { Faction.Blue, Faction.Red })
                    if (s.Faction != side && !firstContact.ContainsKey((side, s.Callsign)) && w.Sensors.Track(side, s).Level >= TrackLevel.Contact)
                        firstContact[(side, s.Callsign)] = w.Time;
            foreach (var s in w.Ships)
                foreach (Faction side in new[] { Faction.Blue, Faction.Red })
                    if (s.Faction != side && !firstIdent.ContainsKey((side, s.Callsign)) && w.Sensors.Track(side, s).Level >= TrackLevel.Identified)
                        firstIdent[(side, s.Callsign)] = w.Time;
    }
    var flights = w.ProbeFlights.ToDictionary(f => f.Id);
    foreach (var (id, info) in seen)
    {
        var p = info.Probe; int contacts = flights.TryGetValue(id, out var f) ? f.Contacts : p.Contacts.Count;
        int idents = flights.TryGetValue(id, out f) ? f.Identifications : p.Identifications.Count;
        rows.Add(string.Join(',', task.Hull, task.Seed, p.Owner.Callsign, p.Owner.Definition.Id, p.Faction, p.LaunchedAt.ToString("0.0"),
            (info.NearestCap / 1000).ToString("0.0"), (info.NearestAny / 1000).ToString("0.0"), info.KnownCaps, info.KnownAny,
            '"' + info.Mission + '"', ((p.Destination - p.Owner.Position).Length() / 1000).ToString("0.0"),
            arrival.TryGetValue(id, out var a) ? (a / 1000).ToString("0.0") : "", contacts, idents));
    }
    var enemiesAll = w.Ships.GroupBy(s => s.Class.Kind);
    foreach (var g in w.Ships.GroupBy(s => (s.Faction, s.Class.Kind)))
    {
        var other = g.Key.Faction == Faction.Blue ? Faction.Red : Faction.Blue;
        var times = g.Select(s => firstContact.TryGetValue((other, s.Callsign), out var t) ? t : double.NaN).ToArray();
        var itimes = g.Select(s => firstIdent.TryGetValue((other, s.Callsign), out var t) ? t : double.NaN).ToArray();
        rows.Add($"#ident,{task.Hull},{task.Seed},{g.Key.Faction},{g.Key.Kind},{g.Count()},{itimes.Count(double.IsFinite)},{(itimes.Where(double.IsFinite).DefaultIfEmpty(double.NaN).Min()):0.0},{(itimes.Where(double.IsFinite).DefaultIfEmpty(double.NaN).Average()):0.0}");
        rows.Add($"#contact,{task.Hull},{task.Seed},{g.Key.Faction},{g.Key.Kind},{g.Count()},{times.Count(double.IsFinite)},{(times.Where(double.IsFinite).DefaultIfEmpty(double.NaN).Min()):0.0},{(times.Where(double.IsFinite).DefaultIfEmpty(double.NaN).Average()):0.0}");
    }
    Console.Error.WriteLine($"done {task.Hull} {task.Seed} t={w.Time:0} probes={seen.Count}");
});
Console.WriteLine("hull,seed,owner,ownerHull,side,launchT,nearestCapKm,nearestAnyKm,knownCaps,knownContacts,activity,throwKm,arrivalCapKm,probeContacts,probeIdents");
foreach (var r in rows.Where(r => !r.StartsWith("#")).OrderBy(r => r)) Console.WriteLine(r);
Console.WriteLine("#contact,hull,seed,faction,kind,ships,contacted,firstT,avgT");
foreach (var r in rows.Where(r => r.StartsWith("#")).OrderBy(r => r)) Console.WriteLine(r);
