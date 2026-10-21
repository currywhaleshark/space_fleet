using System.Text.Json;
using Godot;
using SpaceFleet.Sim;

// Paired range experiment. Full-health coasting salvos, fixed geometry/seeds and a passive protected hull.
// Crossing the 5 km inner screen is distinct from hitting the protected hull or merely expiring.
static class InterceptionTrials
{
    public sealed record Row(string Hull,bool Layered,int RangeKm,int Count,int Seed,int Intercepted,int InnerScreen,
        int Hits,int ExpiredOrMissed,int Remaining,double? FirstFireKm,int[] KillsByRange,float PeakHeat,int Pulses);
    public static Row Wave(string hull,bool layered,int rangeKm,int count,int seed)
    {
        var w=new SimWorld();
        var def=CruiserTrials.Resolve(hull,"""{"decoys":null}""",layered?"{}":"""{"pointDefense":null,"defenseDrones":null,"missiles":null}""").Definition;
        var g=w.Add(new ShipBody("GUARD",def,Faction.Blue));
        var a=w.Add(new ShipBody("ANCHOR",CruiserTrials.PassiveTarget(),Faction.Blue));a.Place(new(0,0,5000),Quaternion.Identity);
        var s=w.Add(new ShipBody("SOURCE",CruiserTrials.Resolve("escort","""{"railgun":null,"pointDefense":null,"defenseDrones":null,"decoys":null,"sensors":{"jammer":0}}""").Definition,Faction.Red));
        s.Place(new(0,0,-22000),new Quaternion(Vector3.Up,Mathf.Pi));
        foreach(var ship in w.Ships)ship.Control=new(){FlightAssist=false};
        var rng=new Random(seed);var incoming=new List<Missile>();
        for(int i=0;i<count;i++)
        {
            var p=new Vec3d((rng.NextDouble()-.5)*3000,(i%2==0?1:-1)*(200+rng.NextDouble()*500),-rangeKm*1000-i/4*300);
            incoming.Add(CruiserTrials.AddThreat(w,s,a,p,(a.Position-p).ToVector3().Normalized()*2000));
        }
        s.Place(new(0,0,-1000000),Quaternion.Identity);
        w.AttachBrain(g,ShipOrder.HoldAt(g.Position));w.Sensors.Update(w.Ships,0,true,w.Probes,w.Missiles);w.Log=new(w);
        var crossed=new HashSet<uint>();var killed=new HashSet<uint>();int[] bins=new int[5];float heat=0;double? first=null;
        for(int tick=0;tick<40*60;tick++)
        {
            w.Step();heat=Math.Max(heat,g.Power.HeatFraction);
            foreach(var m in incoming)
            {
                double km=(m.Position-g.Position).Length()/1000;
                if(m.Health>0&&w.Missiles.Contains(m)&&(m.Position-a.Position).Length()<=5000)crossed.Add(m.Id);
                if(m.Health<=0&&killed.Add(m.Id))bins[km<5?0:km<12?1:km<24?2:km<36?3:4]++;
            }
            if(first is null&&g.Railguns.Any(x=>x.Laser?.Active==true&&x.DefenseTarget is Missile))
                first=g.Railguns.Where(x=>x.Laser?.Active==true&&x.DefenseTarget is Missile).Max(x=>(((Missile)x.DefenseTarget!).Position-g.Position).Length()/1000);
        }
        int left=incoming.Count(w.Missiles.Contains),hits=w.Log.Ship(s).MissileHits,intercepts=killed.Count;
        return new(hull,layered,rangeKm,count,seed,intercepts,crossed.Count,hits,count-intercepts-hits-left,left,first,bins,heat,w.Log.Ship(g).LaserPulses);
    }
    public static void Run(string output)
    {
        var rows=new List<Row>();
        foreach(string hull in new[]{"earth_air_defense_cruiser","earth_air_defense_cruiser_t5"})
        foreach(bool layered in new[]{false,true})foreach(int range in new[]{8,16,24,32,48})
        foreach(int count in new[]{12,48})foreach(int seed in new[]{1,2,3,4})
            rows.Add(Wave(hull,layered,range,count,seed));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        foreach(var group in rows.GroupBy(x=>(x.Hull,x.Layered,x.RangeKm,x.Count)))
            Console.WriteLine($"{group.Key}: intercepted {group.Sum(x=>x.Intercepted)}/{group.Sum(x=>x.Count)}, inner {group.Sum(x=>x.InnerScreen)}, hit {group.Sum(x=>x.Hits)}, first {group.Max(x=>x.FirstFireKm):F1}km");
    }

    // Constant-distance optical tracking trial: an indestructible, stationary missile body.
    // Separates per-pulse accuracy from the extra engagement time in an approaching wave.
    public static void Accuracy(string output)
    {
        var rows=new List<object>();
        foreach(bool linked in new[]{false,true})foreach(int km in new[]{8,16,24,32,40,48})foreach(int seed in new[]{1,2,3,4})
        {
            var w=new SimWorld();
            var def=CruiserTrials.Resolve("earth_air_defense_cruiser_t5","""{"pointDefense":null,"defenseDrones":null,"missiles":null,"decoys":null}""").Definition;
            var guard=w.Add(new ShipBody("GUARD"+seed,def,Faction.Blue));
            var source=w.Add(new ShipBody("SOURCE",CruiserTrials.Resolve("escort","""{"pointDefense":null,"defenseDrones":null,"decoys":null,"railgun":null,"sensors":{"jammer":0}}""").Definition,Faction.Red));
            source.Place(new(0,0,-8000),Quaternion.Identity);
            var missile=CruiserTrials.AddThreat(w,source,guard,new(0,0,-km*1000),Vector3.Zero);missile.Health=100000;
            source.Place(new(1e6,0,0),Quaternion.Identity);
            if(linked)
            {var scout=w.Add(new ShipBody("SCOUT",CruiserTrials.PassiveTarget(),Faction.Blue));scout.Place(missile.Position+new Vec3d(1000,0,0),Quaternion.Identity);}
            foreach(var ship in w.Ships)ship.Control=new(){FlightAssist=false};
            int pulses=0,hits=0,locked=0;float damage=0;var counts=new ulong[guard.Railguns.Length];
            for(int tick=0;tick<360;tick++)
            {
                w.Sensors.Update(w.Ships,w.Time,probes:w.Probes,missiles:w.Missiles);
                var track=w.Sensors.SmallTrack(guard.Faction,missile);
                if(track is { } observed&&observed.Locked(w.Time))locked++;
                foreach(var gun in guard.Railguns)
                    if(track is { } seen&&w.SmallTargetAim(guard,seen,out var direction,out _,gun))w.FireRailgun(guard,gun,direction);
                float before=missile.Health;w.Step();damage+=before-missile.Health;
                for(int i=0;i<counts.Length;i++)
                {
                    var beam=guard.Railguns[i].Laser!;
                    if(beam.Pulses>counts[i])
                    {pulses++;if((beam.End-missile.Position).Length()<3.1)hits++;counts[i]=beam.Pulses;}
                }
            }
            rows.Add(new{Linked=linked,RangeKm=km,Seed=seed,LockedSeconds=locked/60d,Pulses=pulses,HitPulses=hits,Damage=damage,LimitKm=SimWorld.SmallTargetRange(guard.Railgun!)/1000});
        }
        File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS: {rows.Count} constant-distance physical laser accuracy trials -> {output}");
    }
}
