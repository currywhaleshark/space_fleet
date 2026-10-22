using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SpaceFleet.Sim;

static class TacticalTrials
{
    public static void Run()
    {
        var lines=new List<string>{"ships\tfield\tmode\tseconds\tmodules\tuses\tmean_ms\tp99_ms\tmax_ms\tallocated_bytes_per_tick\tdeterministic\tstate_sha256"};
        foreach(int count in new[]{24,80})
        foreach(var field in Enum.GetValues<Battlefield>())
        foreach(var mode in Enum.GetValues<BattleObjective>())
        {
            var cfg=new BattleConfig{Seed=91,StartDistance=45000,FieldSeed=4,Battlefield=field,Objective=mode,
                Engagement=Engagement.HeadOn,Elevation=Elevation.Level,Match=Lineup(count)};
            var first=Battle(cfg);var replay=Battle(cfg);
            if(first.Hash!=replay.Hash)throw new Exception($"Non-deterministic tactical battle: {count}/{field}/{mode}");
            lines.Add(FormattableString.Invariant($"{count}\t{field}\t{mode}\t60\t{first.Modules}\t{first.Uses}\t{first.Mean:F3}\t{first.P99:F3}\t{first.Max:F3}\t{first.Bytes:F0}\ttrue\t{first.Hash}"));
            Console.WriteLine(lines[^1]);
        }
        string dir="docs/validation/tactical_modules_20261022";Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir,"battle-smoke.tsv"),lines);
        Console.WriteLine("PASS: 12 tactical battle configurations, each replayed at 60 Hz for 60 seconds");
    }
    public static void Performance()
    {
        var lines=new List<string>{"tactical\tships\tfield\tmode\tmeasured_seconds\tmean_ms\tp99_ms\tmax_ms\tallocated_bytes_per_tick"};
        foreach(bool equipped in new[]{false,true,true,false})
        {
            var cfg=new BattleConfig{Seed=91,StartDistance=45000,FieldSeed=4,Battlefield=Battlefield.AsteroidCluster,
                Objective=BattleObjective.Domination,Engagement=Engagement.HeadOn,Elevation=Elevation.Level,Match=Lineup(80,equipped)};
            var result=Battle(cfg,30,5);
            lines.Add(FormattableString.Invariant($"{equipped}\t80\tAsteroidCluster\tDomination\t30\t{result.Mean:F3}\t{result.P99:F3}\t{result.Max:F3}\t{result.Bytes:F0}"));
            Console.WriteLine(lines[^1]);
        }
        File.WriteAllLines("docs/validation/tactical_modules_20261022/tick-comparison.tsv",lines);
    }
    public static void Balance()
    {
        const string path="docs/validation/tactical_modules_20261022/balance.tsv";
        File.WriteAllText(path,"seed\tfit\tships\tseconds\twinner\tside\tremaining_strength\tdestroyed\tdisabled\tmean_survival_s\trail_shots\trail_hits\tmissiles\tmissile_hits\tintercepts\tshield_damage\tmodule_damage\trepairs\tskill_uses\n");
        foreach(int seed in new[]{91,92})foreach(string fit in new[]{"baseline","support","role"})
        {
            var world=new SimWorld();BattleSetup.Spawn(world,new BattleConfig{Seed=seed,StartDistance=45000,
                Objective=BattleObjective.Annihilation,Engagement=Engagement.HeadOn,Elevation=Elevation.Level,Match=Lineup(24,fit!="baseline",fit=="support")});
            Console.WriteLine($"Balance starts: seed={seed} fit={fit}");
            while(world.Rules!.Outcome is null&&world.Time<BattleRules.TimeLimitSeconds+1)world.Step();
            if(world.Rules.Outcome is null)throw new Exception("Battle did not finish");
            foreach(var side in Enum.GetValues<Faction>())
            {
                var ships=world.Ships.Where(s=>s.Faction==side).ToArray();var stats=ships.Select(world.Log!.Ship).ToArray();var attacks=world.Log.Side(side);
                string line=FormattableString.Invariant($"{seed}\t{fit}\t24\t{world.Time:F3}\t{world.Rules.Outcome.Winner?.ToString()??"Draw"}\t{side}\t{world.Rules.Fraction(side):F3}\t{ships.Count(s=>s.Damage.Destroyed)}\t{ships.Count(s=>s.Damage.Disabled&&!s.Damage.Destroyed)}\t{stats.Average(s=>s.DestroyedAt??world.Time):F3}\t{attacks.Rails}\t{attacks.RailHits}\t{attacks.Missiles}\t{attacks.MissileHits}\t{stats.Sum(s=>s.MissilesIntercepted+s.TorpedoesIntercepted)}\t{stats.Sum(s=>s.ShieldDamageInflicted):F1}\t{stats.Sum(s=>s.ModuleDamageInflicted):F1}\t{ships.Sum(s=>s.DamageControl.Completed)}\t{ships.Sum(s=>s.Tactical.ActiveSlots.Sum(t=>t.Uses))}");
                File.AppendAllText(path,line+"\n");Console.WriteLine(line);
            }
        }
    }
    private static MatchLineup Lineup(int count,bool tactical=true,bool support=false)
    {
        var entries=new List<MatchParticipant>();int perTeam=count==24?3:10;
        for(int team=0;team<2;team++)for(int i=0;i<perTeam;i++)
        {
            var kind=count==24?(HullKind)i:i<3?HullKind.Battleship:i<7?HullKind.Escort:HullKind.Interceptor;
            var design=(i+team)%2==0?DesignFamily.Earth:DesignFamily.Mars;
            string hull=ShipDefinitions.For(kind,design).Id;
            var raw=FleetLoadouts.Default(hull);
            ShipFit Fit(ShipFit f)
            {
                if(!tactical)return f;
                if(!support)return TacticalModulesCatalog.EquipAI(f,1);
                var bays=TacticalModulesCatalog.Bays(FleetLoadouts.ResolveFit(f).Definition);
                return f with{Components=(f.Components??new()) with{TacticalModules=new[]{new TacticalMount(bays[0].Id,"repair-stores"),new TacticalMount(bays[1].Id,"repair-controller")}}};
            }
            var fitted=new SquadronLoadout(raw.Members.Select(Fit).ToArray());
            entries.Add(new(i+1,(Faction)team,hull,team==0&&i==0,FleetLoadouts.Resolve(fitted)));
        }
        var result=new MatchLineup(entries);
        if(entries.Sum(p=>p.ShipCount)!=count)throw new Exception("Wrong trial fleet size");
        return result;
    }
    private static (string Hash,int Modules,int Uses,double Mean,double P99,double Max,double Bytes) Battle(BattleConfig config,int seconds=60,int warmup=0)
    {
        var world=new SimWorld();BattleSetup.Spawn(world,config);
        for(int i=0;i<warmup*60;i++)world.Step();
        var times=new double[seconds*60];long allocation=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<times.Length;i++)
        {long start=Stopwatch.GetTimestamp();world.Step();times[i]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;}
        long bytes=GC.GetAllocatedBytesForCurrentThread()-allocation;
        if(world.Ships.Any(s=>!s.Velocity.IsFinite()||!double.IsFinite(s.Position.Length())||!float.IsFinite(s.Power.HeatMj)))throw new Exception("Non-finite battle state");
        var state=new StringBuilder(world.Log!.Summary());
        foreach(var s in world.Ships)
        {
            state.Append(FormattableString.Invariant($"|{s.Callsign}:{s.Position.X:R},{s.Position.Y:R},{s.Position.Z:R},{s.Velocity},{s.Power.HeatMj:R},{s.Ordnance.Missiles},{s.DamageControl.Kits}"));
            foreach(var m in s.Damage.Modules)state.Append(FormattableString.Invariant($";{m.Health:R}"));
            foreach(var slot in s.Tactical.Slots)state.Append(FormattableString.Invariant($";{slot.Definition.Id}:{slot.Uses}:{slot.CooldownUntil:R}:{slot.ActiveUntil:R}"));
        }
        string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state.ToString())));
        Array.Sort(times);
        return(hash,world.Ships.Sum(s=>s.Tactical.Slots.Length),world.Ships.Sum(s=>s.Tactical.ActiveSlots.Sum(t=>t.Uses)),times.Average(),times[(int)(times.Length*.99)],times[^1],bytes/(double)times.Length);
    }
}
