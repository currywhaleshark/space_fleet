using SpaceFleet.Game;
using SpaceFleet.Sim;

/// <summary>Modular weapons phase 4: fitted strength score, score-mirrored AI fits, AI use of mixed batteries.</summary>
static class FitScoreChecks
{
    /// <summary>Team fitted-score gap allowed when AI picks parts to mirror the other side (relative to the larger team).</summary>
    public const double TeamTolerance = .03;
    private static int checks;
    private static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
    private static bool Near(double a,double b,double tol=1e-6)=>Math.Abs(a-b)<=tol*Math.Max(1,Math.Abs(b));

    public static void Run(){Scores();Matches();Ai();Console.WriteLine($"PASS: {checks} fit score/matching/AI fit checks");}

    private static void Scores()
    {
        var tree=ResearchTree.Catalog;
        foreach(var hull in FleetLoadouts.Hulls)
        {
            var fit=FleetLoadouts.Empty(hull.Id);
            Check(FitScores.Ratio(fit)==1&&FitScores.Score(fit)==tree.Score(fit),$"{hull.Id}: stock fit scores the tree score exactly");
            var researched=fit with{Upgrades=tree.ByHull(hull.Id).Upgrades.Select(u=>u.Id).ToArray()};
            Check(FitScores.Score(researched)==tree.Score(researched),$"{hull.Id}: research keeps its match bonus, not a second fit bonus");
        }
        // Parts move the score with firepower: an overcharged light mix scores below stock, a heavier legal mix differently.
        var escort=FleetLoadouts.Empty("escort");
        double Ratio(params string?[] guns)=>FitScores.Ratio(escort with{Components=new ShipComponentSelection{MainGuns=guns}});
        Check(Ratio("earth-rail-m2","earth-rail-m2")==1||Near(Ratio("earth-rail-m2","earth-rail-m2"),1),"Explicit stock parts score as stock");
        Check(Ratio("earth-rail-s2","earth-rail-s2")<.95,"Two 100 mm twins score clearly below stock");
        Check(Near(Ratio("earth-rail-m3","earth-rail-m1-hp"),Ratio("earth-rail-m3","earth-rail-m1-hp")),"Score is deterministic");
        var catalog=escort with{Components=new ShipComponentSelection(MainGun:"main-triple")};
        Check(FitScores.Ratio(catalog)!=1,"Catalog systems also move the fitted score");
        // A laser part's rate counts with the laser equivalence and reach.
        var aa=FleetLoadouts.ResolveFit(FleetLoadouts.Empty("earth_air_defense_cruiser")).Definition;
        Check(FitScores.Guns(aa)>0,"Laser batteries have a firepower term");
        // Rails and lasers share one unit (laser balance 2026-10-20): a laser hull fitted with rails, and a rail hull
        // fitted with lasers, land where the mirror duels put them instead of near zero.
        var aaT5=FleetLoadouts.Empty("earth_air_defense_cruiser_t5") with{Upgrades=new[]{"fire-control"}};
        double Fitted(ShipFit fit,params string?[] guns)=>MainGunParts.EffectiveFirepower(FleetLoadouts.ResolveFit(fit with{Components=new ShipComponentSelection{MainGuns=guns}}).Definition)
            /MainGunParts.EffectiveFirepower(FleetLoadouts.ResolveFit(fit).Definition);
        double rails=Fitted(aaT5,"earth-rail-s2","earth-rail-s2"),large=Fitted(aaT5,"earth-laser-l",null);
        Check(rails>1.2&&rails<2.5,$"AA cruiser twin rails score as a real anti-ship upgrade ({rails:0.00})");
        Check(large>1.5&&large<2.5,$"AA cruiser large emitter ({large:0.00})");
        var line=FleetLoadouts.Empty("battleship") with{Upgrades=new[]{"fire-control"}};
        double lasers=Fitted(line,"earth-laser-l","earth-laser-l","earth-laser-l");
        Check(lasers>.2&&lasers<.5,$"Battleship emitters score well below its rails ({lasers:0.00})");
        Console.WriteLine($"  firepower vs stock: AA T5 twin rails {rails:0.000}, AA T5 large emitter {large:0.000}, battleship 3 emitters {lasers:0.000}");
    }

    private static void Matches()
    {
        var gaps=new List<double>();int partsFielded=0,members=0;
        var squads=new[]{"escort","battleship","earth_gun_cruiser","mars_battleship_t5","interceptor_t3"};
        foreach(string leader in squads)
        for(int seed=1;seed<=12;seed++)
        {
            var squad=FleetLoadouts.Default(leader);
            var owned=FleetLoadouts.Hulls.Select(h=>h.Id).ToHashSet();
            var upgrades=owned.ToDictionary(id=>id,id=>(IReadOnlySet<string>)new HashSet<string>());
            MatchLineup match;
            try{match=Matchmaking.CreateTiered(seed,squad,owned,upgrades);}
            catch(InvalidDataException){continue;}
            double blue=match.Participants.Where(p=>p.Team==Faction.Blue).Sum(p=>p.MatchPoints),red=match.Participants.Where(p=>p.Team==Faction.Red).Sum(p=>p.MatchPoints);
            gaps.Add(Math.Abs(blue-red)/Math.Max(blue,red));
            foreach(var p in match.Participants.Where(p=>!p.IsPlayer))
                foreach(var m in p.Loadout!.Members){members++;if(m.Definition.Battery?.SocketWeapons is not null)partsFielded++;}
            var again=Matchmaking.CreateTiered(seed,squad,owned,upgrades);
            Check(again.Participants.Select(p=>p.Loadout!.LoadoutId).SequenceEqual(match.Participants.Select(p=>p.Loadout!.LoadoutId)),"Same seed builds the same fitted lineup");
        }
        Console.WriteLine($"  team fitted-score gap: matches {gaps.Count}, mean {gaps.Average():P2}, max {gaps.Max():P2}; AI ships on part mixes {partsFielded}/{members}");
        Check(gaps.Count>=40&&gaps.Max()<=TeamTolerance,$"Mirrored teams stay within {TeamTolerance:P0} fitted score (max {gaps.Max():P2})");
        Check(partsFielded>members/5&&partsFielded<members*4/5,"AI squads field a mix of stock and part fits");
    }

    private static void Ai()
    {
        // Mixed battery: the longest-reaching gun drives ship-level range; a stock battery keeps its first mount.
        var mixed=ShipLoadouts.Resolve(new ShipLoadout("battleship"){MainGuns=new[]{"earth-rail-s2",null,null}}).Definition;
        var body=new ShipBody("MIX",mixed,Faction.Blue);
        Check(body.Railgun!.Definition.MaxRange==body.Railguns.Max(g=>g.Definition.MaxRange)&&body.Railgun!=body.Railguns[0],"Ship-level gun is the longest-reaching mount");
        var stock=new ShipBody("STOCK",ShipDefinitions.ById("battleship"),Faction.Blue);
        Check(stock.Railgun==stock.Railguns[0],"Stock battery keeps its first mount as the ship-level gun");
        // Under-supplied fits lean a pip into weapons; stock and rated fits keep their authored pips.
        var heavy=ShipLoadouts.Resolve(new ShipLoadout("escort"){MainGuns=new[]{"earth-rail-m3","earth-rail-m1-hp"}}).Definition;
        var brain=new ShipBrain(new ShipBody("H",heavy,Faction.Blue),new ShipOrder(OrderKind.Hold));
        var stockBrain=new ShipBrain(new ShipBody("S",ShipDefinitions.ById("escort"),Faction.Blue),new ShipOrder(OrderKind.Hold));
        Check(brain.DefaultPips[(int)PowerChannel.Weapons]==stockBrain.DefaultPips[(int)PowerChannel.Weapons]+1&&brain.DefaultPips.Sum()==ShipPower.TotalPips,
            "Under-supplied fit leans one pip into weapons");
        // Overcharged rails hold a salvo that would overheat the ship; stock guns never hold for this.
        var light=ShipLoadouts.Resolve(new ShipLoadout("escort"){MainGuns=new[]{"earth-rail-s2","earth-rail-s2"}}).Definition;
        var world=new SimWorld();var shooter=world.Add(new ShipBody("HOT",light,Faction.Blue));var target=world.Add(new ShipBody("T",ShipDefinitions.ById("escort"),Faction.Red));
        target.Place(new Vec3d(0,0,-20000),Godot.Quaternion.Identity);
        shooter.Control=target.Control=new ShipControl{FlightAssist=false};
        world.Sensors.Update(world.Ships,world.Time,force:true);
        var gun=shooter.Railguns[0];
        Check(gun.Overcharge==MainBatteries.MaxOvercharge,"Light mix runs at the overcharge cap");
        shooter.Power.AddHeat(light.Power.HeatCapacityMj*.95f-gun.Definition.ShotHeatMj*gun.SalvoRounds*.5f);
        world.TryAutoFire(shooter,target,null,1000,out var status);
        Check(status==GunneryStatus.Reload&&gun.ShotCount==0,$"Overcharged salvo waits near overheat ({status})");
    }
}
