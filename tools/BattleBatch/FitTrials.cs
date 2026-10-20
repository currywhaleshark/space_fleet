using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using SpaceFleet.Sim;

/// <summary>
/// Modular weapons phase 4: mixed 10:10 tiered matches where AI squads field random main gun mixes and the opposing
/// squads mirror their fitted scores. Records team score gaps against outcomes and each ship's damage by its fit.
/// </summary>
static class FitTrials
{
    sealed record ShipRow(int Seed, string Leader, string Team, string Callsign, string Hull, string Research, string Fit, double Inflicted, bool Destroyed, int Shots, double AmmoLeft);
    sealed record MatchRow(int Seed, string Leader, double Blue, double Red, double Gap, string Winner, double Time, int Ships, double BlueDamage, double RedDamage, int BlueParts);

    public static void Save(string output, int[] seeds, string[] leaders, int jobs, double seconds)
    {
        var ships = new ConcurrentBag<ShipRow>(); var matches = new ConcurrentBag<MatchRow>();
        var owned = FleetLoadouts.Hulls.Select(h => h.Id).ToHashSet();
        var none = owned.ToDictionary(id => id, id => (IReadOnlySet<string>)new HashSet<string>());
        var tasks = leaders.SelectMany(l => seeds.Select(s => (Leader: l, Seed: s))).ToArray();
        Parallel.ForEach(tasks, new ParallelOptions { MaxDegreeOfParallelism = jobs }, task =>
        {
            var match = Matchmaking.CreateTiered(task.Seed, FleetLoadouts.Default(task.Leader), owned, none);
            var world = new SimWorld();
            var config = new BattleConfig { Seed = task.Seed, Match = match };
            BattleSetup.Spawn(world, config);
            while (world.Time < seconds && world.Rules!.Outcome is null) world.Step();
            world.Log!.Finish();
            double Team(Faction f) => match.Participants.Where(p => p.Team == f).Sum(p => p.MatchPoints);
            double blue = Team(Faction.Blue), red = Team(Faction.Red);
            var outcome = world.Rules!.Outcome;
            // Without a decision inside the window, the side with more surviving strength leads.
            string winner = outcome?.Winner?.ToString() ?? Leading(world);
            double Dealt(Faction f) => world.Ships.Where(b => b.Faction == f).Sum(b => world.Log.Ship(b).ShieldDamageInflicted + world.Log.Ship(b).ModuleDamageInflicted);
            // Ships carrying a non-stock main gun part, or (forced ordnance trials) every AI ship that can carry the forced part.
            var forced = FitScores.ForcedPart is { } fid ? OrdnanceParts.Find(fid) : null;
            int blueParts = world.Ships.Count(b => b.Faction == Faction.Blue && (forced is not null
                ? !b.Callsign.StartsWith("B01") && OrdnanceParts.Socket(b.Definition, forced) is null
                : b.Definition.Battery?.SocketWeapons is { } sw && sw.Any(p => p.Id != MainGunParts.StockFor(b.Definition).Id)));
            matches.Add(new(task.Seed, task.Leader, blue, red, (blue - red) / Math.Max(blue, red), winner, world.Time, world.Ships.Count, Dealt(Faction.Blue), Dealt(Faction.Red), blueParts));
            foreach (var body in world.Ships)
            {
                var log = world.Log.Ship(body); var d = body.Definition; var b = d.Battery;
                // Non-stock sockets only: a reactor upgrade alone converts the battery but keeps stock parts.
                string fit = "stock";
                if (b?.SocketWeapons is { } parts)
                {
                    var stock = MainGunParts.StockFor(d).Id;
                    if (parts.Any(p => p.Id != stock)) fit = string.Join("+", parts.Select(p => p.Id == stock ? "-" : p.Id));
                }
                // Research state, so part fits are compared with stock ships researched the same way.
                string research = $"{d.StructureHitPoints:0}/{d.WeaponBudget?.VolumeBonus ?? 0:0}/{d.WeaponBudget?.SupplyScale ?? 1:0.00}/{d.Flight.ForwardAccel:0.###}/{d.DamageControl.RepairSeconds:0.##}";
                ships.Add(new(task.Seed, task.Leader, body.Faction.ToString(), body.Callsign, d.Id, research, fit,
                    log.ShieldDamageInflicted + log.ModuleDamageInflicted, body.Damage.Destroyed, log.Rails,
                    body.Railguns.Where(g => !g.IsLaser).Sum(g => g.Definition.Rounds) is int full && full > 0 ? body.Railguns.Where(g => !g.IsLaser).Sum(g => g.Rounds) / (double)full : 1));
            }
            Console.WriteLine($"fit-trial {task.Leader} seed {task.Seed}: blue {blue:0.00} red {red:0.00} → {winner} at {world.Time:0}s");
        });
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var m = matches.OrderBy(r => r.Leader).ThenBy(r => r.Seed).ToArray();
        var s = ships.OrderBy(r => r.Leader).ThenBy(r => r.Seed).ThenBy(r => r.Team).ThenBy(r => r.Hull).ToArray();
        File.WriteAllLines(output, new[] { "seed,leader,blue,red,gap,winner,time,ships,blue_damage,red_damage,blue_parts" }.Concat(m.Select(r =>
            string.Join(",", r.Seed, r.Leader, F(r.Blue), F(r.Red), F(r.Gap), r.Winner, F(r.Time), r.Ships, F(r.BlueDamage), F(r.RedDamage), r.BlueParts))));
        File.WriteAllLines(Path.ChangeExtension(output, ".ships.csv"), new[] { "seed,leader,team,callsign,hull,research,fit,inflicted,destroyed,shots,ammo_left" }.Concat(s.Select(r =>
            string.Join(",", r.Seed, r.Leader, r.Team, r.Callsign, r.Hull, r.Research, r.Fit, F(r.Inflicted), r.Destroyed, r.Shots, F(r.AmmoLeft)))));
        File.WriteAllText(Path.ChangeExtension(output, ".summary.json"), JsonSerializer.Serialize(Summary(m, s), new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(File.ReadAllText(Path.ChangeExtension(output, ".summary.json")));
    }

    static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    static string Leading(SimWorld world)
    {
        double Alive(Faction f) => world.Ships.Where(b => b.Faction == f && !b.Damage.Destroyed).Sum(b => b.Definition.BattleValue.Weight);
        double blue = Alive(Faction.Blue), red = Alive(Faction.Red);
        return blue > red ? "Blue*" : red > blue ? "Red*" : "Draw";
    }

    static object Summary(MatchRow[] matches, ShipRow[] ships)
    {
        // Did the higher fitted score win more than chance? Gap bins split by |gap|.
        var decided = matches.Where(r => r.Winner is not "Draw").ToArray();
        bool HigherWon(MatchRow r) => (r.Gap >= 0) == r.Winner.StartsWith("Blue");
        var bins = new[] { (0.0, .005), (.005, .01), (.01, .03) }.Select(b =>
        {
            var rows = decided.Where(r => Math.Abs(r.Gap) >= b.Item1 && Math.Abs(r.Gap) < b.Item2).ToArray();
            return new { gap = $"{b.Item1:P1}–{b.Item2:P1}", matches = rows.Length, higherScoreWins = rows.Count(HigherWon) };
        }).ToArray();
        // Each non-stock part: mean damage per ship carrying it ÷ the stock mean of the same hull and research state.
        string Key(ShipRow r) => r.Hull + "|" + r.Research;
        var stockMean = ships.Where(r => r.Fit == "stock").GroupBy(Key).Where(g => g.Count() >= 3).ToDictionary(g => g.Key, g => g.Average(r => r.Inflicted));
        var parts = ships.Where(r => r.Fit != "stock" && stockMean.TryGetValue(Key(r), out var mean) && mean > 0)
            .SelectMany(r => r.Fit.Split('+').Where(p => p != "-").Distinct().Select(p => (Part: p, Ratio: r.Inflicted / stockMean[Key(r)])))
            .GroupBy(x => x.Part).Select(g => new { part = g.Key, ships = g.Count(), relativeDamage = Math.Round(g.Average(x => x.Ratio), 3) })
            .OrderByDescending(x => x.relativeDamage).ToArray();
        var blueWins = decided.Count(r => r.Winner.StartsWith("Blue"));
        return new
        {
            matches = matches.Length, decidedOrLeading = decided.Length, blueWins, redWins = decided.Length - blueWins,
            meanAbsGap = Math.Round(matches.Average(r => Math.Abs(r.Gap)), 4), maxAbsGap = Math.Round(matches.Max(r => Math.Abs(r.Gap)), 4),
            higherScoreWins = decided.Count(HigherWon), gapBins = bins,
            shipsOnParts = ships.Count(r => r.Fit != "stock"), ships = ships.Length, parts
        };
    }
}
