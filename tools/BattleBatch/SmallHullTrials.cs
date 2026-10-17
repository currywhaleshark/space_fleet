using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Sim;

// Geometry comparisons hold performance values constant. Measurements are independent
// of the render mesh: damage still uses the game's authored compound box approximation.
internal static class SmallHullTrials
{
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    public static readonly string[] Hulls={"earth_recon_t3","mars_assault_t3"};
    // Test-only clone of the canonical starting fleet. No production definition registry mutation.
    public static SimWorld Fleet(BattleConfig config,bool legacy,float specialistReachScale=1)
    {
        if(config.Match is not null || config.Battlefield!=Battlefield.DeepSpace || config.Objective!=BattleObjective.Annihilation)
            throw new ArgumentException("Small-hull geometry comparison requires the canonical deep-space annihilation fleet");
        if(!float.IsFinite(specialistReachScale)||specialistReachScale<=0)
            throw new ArgumentException("Positive finite specialist reach scale required");
        var initial=new SimWorld();BattleSetup.Spawn(initial,config);
        var world=new SimWorld();var ships=new Dictionary<ShipBody,ShipBody>();
        foreach(var original in initial.Ships)
        {
            var definition=Hulls.Contains(original.Definition.Id)?Geometry(original.Definition.Id,legacy):original.Definition;
            if(specialistReachScale!=1 && definition.Branch is ShipBranch.Recon or ShipBranch.Assault)
            {
                var node=JsonNode.Parse(definition.ToJson())!;
                float reach=definition.Ai.AssaultReachMeters>0?definition.Ai.AssaultReachMeters:
                    definition.Design==DesignFamily.Mars?100000:85000;
                node["Ai"]!["AssaultReachMeters"]=reach*specialistReachScale;
                definition=ShipDefinition.Parse(node.ToJsonString());
            }
            var ship=world.Add(new ShipBody(original.Callsign,definition,original.Faction));
            ship.Place(original.Position,original.Orientation);ship.Control=ShipControl.Idle;
            world.AttachBrain(ship,ShipOrder.HoldAt(ship.Position));ships.Add(original,ship);
        }
        foreach(var squad in initial.Squadrons)
            world.AddSquadron(squad.Name,squad.Faction,squad.Role,squad.Members.Select(s=>ships[s]));
        world.Doctrine=new FleetDoctrine();world.EnableCommander(Faction.Red);world.EnableCommander(Faction.Blue);
        world.Rules=new BattleRules(world,config.Objective);world.Sensors.Update(world.Ships,0,true);world.Log=new BattleLog(world);
        return world;
    }
    public static ShipDefinition Geometry(string id,bool legacy)
    {
        var d=ShipDefinitions.ById(id);if(!legacy)return d;
        var source=ShipDefinitions.ById(id.StartsWith("earth")?"interceptor":"mars_interceptor");
        var node=JsonNode.Parse(d.ToJson())!;var old=JsonNode.Parse(source.ToJson())!;
        foreach(string key in new[]{"ModelId","HullSections","Bays"})node[key]=old[key]!.DeepClone();
        foreach(var module in node["Modules"]!.AsArray())
        {
            var match=old["Modules"]!.AsArray().Single(m=>m!["Id"]!.GetValue<string>()==module!["Id"]!.GetValue<string>())!;
            foreach(string key in new[]{"Center","HalfSize"})module![key]=match[key]!.DeepClone();
        }
        node["Railgun"]!["Muzzle"]=old["Railgun"]!["Muzzle"]!.DeepClone();
        node["Missiles"]!["LaunchPoint"]=old["Missiles"]!["LaunchPoint"]!.DeepClone();
        node["Antimatter"]!["Flight"]!["LaunchPoint"]=old["Antimatter"]!["Flight"]!["LaunchPoint"]!.DeepClone();
        node["PointDefense"]!["Mounts"]=old["PointDefense"]!["Mounts"]!.DeepClone();
        if(node["ReconProbes"] is {} probes)probes["LaunchPoints"]=null;
        return ShipDefinition.Parse(node.ToJsonString());
    }

    public sealed record Projection(string Hull,bool Legacy,string View,double Area,double PdDamagedArea,
        double ArmedPdCatastropheArea,double RailAmArea,double RailReactorArea,double RailBothArea);
    public static Projection Project(string id,bool legacy,string name,Vector3 from,float step=.5f)
    {
        var def=Geometry(id,legacy);var ship=new ShipBody("RASTER",def,Faction.Blue);
        Vector3 axis=from.Normalized();Vector3 u=axis.Cross(Math.Abs(axis.Y)>.9?Vector3.Right:Vector3.Up).Normalized();var v=axis.Cross(u);
        int hits=0,pd=0,fatal=0,am=0,reactor=0,both=0;uint sequence=0;
        for(float x=-22+step/2;x<22;x+=step)for(float y=-22+step/2;y<22;y+=step)
        {
            var origin=Vec3d.From(axis*80+u*x+v*y);
            if(!DamageRay.FirstHit(ship,origin,-axis,160,out _))continue;
            hits++;
            // Every ray starts with a new full store. Combat reset intentionally does NOT refill AM.
            var victim=new ShipBody("PD-RASTER",def,Faction.Blue);victim.Ordnance.Antimatter.BeginArming();
            var hit=DamageRay.ApplyAtPose(victim,origin,-axis,new(20,25,10,160),0,++sequence,Vec3d.Zero,Quaternion.Identity,false);
            if(hit.Modules.Count>0)pd++;if(victim.Damage.Destroyed)fatal++;
            victim=new ShipBody("RAIL-RASTER",def,Faction.Blue);
            hit=DamageRay.ApplyAtPose(victim,origin,-axis,new(220,250,120,160),0,++sequence,Vec3d.Zero,Quaternion.Identity,false);
            bool a=hit.Modules.Any(m=>m.Id=="am-containment"),r=hit.Modules.Any(m=>m.Id=="reactor");
            if(a)am++;if(r)reactor++;if(a&&r)both++;
        }
        double cell=step*step;return new(id,legacy,name,hits*cell,pd*cell,fatal*cell,am*cell,reactor*cell,both*cell);
    }

    public sealed record Coverage(string Hull,bool Legacy,int Directions,int Mechanical,int Clear,int Blocked,int BuriedMounts,
        Dictionary<string,int[]> Cardinal);
    public static Coverage Cover(string id,bool legacy,int samples=2048)
    {
        var def=Geometry(id,legacy);var ship=new ShipBody("COVER",def,Faction.Blue);var pd=def.PointDefense!;
        int mechanical=0,clear=0,blocked=0,buried=0;
        int[] At(Vector3 point)
        {
            int can=0,free=0;
            for(int i=0;i<pd.Mounts.Length;i++)
            {
                var state=ship.Ordnance.PointDefense[i];Vector3 delta=point-pd.Mounts[i];if(!state.Contains(delta))continue;
                can++;Vector3 start=pd.Mounts[i]+state.MountBasis*Vector3.Up*.05f;
                if(!DamageRay.FirstHit(ship,Vec3d.From(start),(point-start).Normalized(),(point-start).Length(),out _))free++;
            }
            return new[]{can,free};
        }
        for(int i=0;i<pd.Mounts.Length;i++)
        {
            var p=pd.Mounts[i]+ship.Ordnance.PointDefense[i].MountBasis*Vector3.Up*.05f;
            if(def.HullSections.Any(s=>Math.Abs(p.X-s.Center.X)<s.HalfSize.X&&Math.Abs(p.Y-s.Center.Y)<s.HalfSize.Y&&Math.Abs(p.Z-s.Center.Z)<s.HalfSize.Z))buried++;
        }
        for(int i=0;i<samples;i++)
        {
            double y=1-2*(i+.5)/samples,r=Math.Sqrt(1-y*y),a=i*Math.PI*(3-Math.Sqrt(5));
            var n=At(new Vector3((float)(r*Math.Cos(a)),(float)y,(float)(r*Math.Sin(a)))*800);
            if(n[0]>0)mechanical++;if(n[1]>0)clear++;if(n[0]>0&&n[1]==0)blocked++;
        }
        var cardinal=new Dictionary<string,int[]>();
        foreach(var (name,dir) in new[]{("fore",Vector3.Forward),("aft",Vector3.Back),("port",Vector3.Left),("starboard",Vector3.Right),
            ("dorsal",Vector3.Up),("ventral",Vector3.Down),("rear_upper",new Vector3(0,.5f,1).Normalized()),("rear_lower",new Vector3(0,-.5f,1).Normalized())})cardinal[name]=At(dir*800);
        return new(id,legacy,samples,mechanical,clear,blocked,buried,cardinal);
    }

    public sealed record Assault(string Hull,bool Legacy,string Defense,string Approach,int Seed,double Seconds,
        int Launched,int Hits,int Intercepted,bool Destroyed,bool Disabled,double? FirstLaunch,double MinimumRange,
        bool Escaped,double? EscapeSeconds,double ContainmentHealth,int Repairs,int PdShots,int DroneInterceptions,
        string Destruction,uint AmFailures,int AmRemaining,Dictionary<string,int> FlightOutcomes);
    public static Assault RunAssault(string id,bool legacy,string defense,string approach,int seed,double seconds=180)
    {
        var w=new SimWorld();var def=Geometry(id,legacy);
        var ship=w.Add(new ShipBody("RUN-"+seed,def,Faction.Blue));
        string patch=defense=="none"?"""{"railgun":null,"missiles":null,"pointDefense":null,"defenseDrones":null,"decoys":null}"""
            :defense=="pd"?"""{"railgun":null,"missiles":null,"defenseDrones":null,"decoys":null}"""
            :"""{"railgun":null,"missiles":null,"decoys":null}""";
        var target=w.Add(new ShipBody("FORT",CruiserTrials.Resolve("battleship",patch).Definition,Faction.Red));
        target.Control=new ShipControl {FlightAssist=false};
        // No omniscient control inputs: normal friendly sensors, a passive link anchor and sampled enemy tracks.
        var relay=w.Add(new ShipBody("LINK",CruiserTrials.Resolve("battleship",
            """{"railgun":null,"missiles":null,"pointDefense":null,"defenseDrones":null,"sensors":{"strength":10000}}""").Definition,Faction.Blue));
        relay.Place(new(40000,0,40000),Quaternion.Identity);relay.Control=new ShipControl {FlightAssist=false};
        Vector3 direction=approach switch {"fore"=>Vector3.Forward,"dorsal"=>Vector3.Up,_=>new Vector3(0,-.5f,1).Normalized()};
        var random=new Random(seed);direction=(direction+new Vector3((float)(random.NextDouble()-.5)*.15f,0,0)).Normalized();
        ship.Place(Vec3d.From(direction*16000),Basis.LookingAt(-direction,Math.Abs(direction.Y)>.9?Vector3.Forward:Vector3.Up).GetRotationQuaternion());
        w.AttachBrain(ship,ShipOrder.AttackOn(target));w.Sensors.Update(w.Ships,0,true);w.Log=new BattleLog(w);
        double minimum=double.MaxValue;double? first=null,escape=null;bool returning=false;
        while(w.Time<seconds && !ship.Damage.Destroyed)
        {
            w.Step();double range=(ship.Position-target.Position).Length();minimum=Math.Min(minimum,range);
            if(w.Log.Ship(ship).Torpedoes>0 && first is null)first=w.Time;
            // Measure a deliberate return after one strike: shared AI navigates a real Hold/Return leg.
            if(first is {} fired && !returning && w.Time-fired>=6)
            {w.BrainOf(ship)!.Order=ShipOrder.HoldAt(Vec3d.From(direction*14000));returning=true;}
            if(returning&&range>9000&&escape is null)escape=w.Time-first;
            if(escape is not null && w.Time-first>50)break;
        }
        var log=w.Log.Ship(ship);
        return new(id,legacy,defense,approach,seed,w.Time,log.Torpedoes,log.TorpedoHits,w.Log.AntimatterFlights.Count(a=>a.Outcome==AntimatterOutcome.PointDefense),
            ship.Damage.Destroyed,ship.Damage.Disabled,first,minimum,escape is not null,escape,ship.Ordnance.Antimatter.Containment,
            ship.DamageControl.Completed,(int)target.Ordnance.PointDefense.Sum(p=>p.ShotCount),(int)target.Ordnance.Drones.Interceptions,
            ship.Damage.Destruction.ToString(),ship.Ordnance.Antimatter.Failures,ship.Ordnance.Antimatter.Rounds,
            w.Log.AntimatterFlights.GroupBy(f=>f.Outcome.ToString()).ToDictionary(g=>g.Key,g=>g.Count()));
    }

    public static void Save(string output,bool quick=false)
    {
        Directory.CreateDirectory(output);
        var projections=new List<Projection>();var coverage=new List<Coverage>();var assault=new List<Assault>();
        foreach(string id in Hulls)foreach(bool legacy in new[]{true,false})
        {
            foreach(var (name,dir) in new[]{("fore",Vector3.Forward),("aft",Vector3.Back),("side",Vector3.Right),("dorsal",Vector3.Up),
                ("ventral",Vector3.Down),("rear_lower",new Vector3(0,-.5f,1))})projections.Add(Project(id,legacy,name,dir));
            coverage.Add(Cover(id,legacy));Console.WriteLine($"geometry {id}/{legacy}: {JsonSerializer.Serialize(coverage[^1])}");
        }
        File.WriteAllText(Path.Combine(output,"projection.json"),JsonSerializer.Serialize(projections,Json));
        File.WriteAllText(Path.Combine(output,"coverage.json"),JsonSerializer.Serialize(coverage,Json));
        if(quick)return;
        foreach(string id in Hulls)foreach(bool legacy in new[]{true,false})
        foreach(string defense in new[]{"none","pd","pd_drones"})foreach(string approach in new[]{"fore","dorsal","rear_lower"})
        foreach(int seed in new[]{1,2,3})
        {
            var row=RunAssault(id,legacy,defense,approach,seed);assault.Add(row);
            Console.WriteLine($"assault {id}/{legacy}/{defense}/{approach}/{seed}: AM {row.Hits}/{row.Launched}, destroyed={row.Destroyed}, escape={row.Escaped}, nearest={row.MinimumRange:F0}");
            File.WriteAllText(Path.Combine(output,"assault.json"),JsonSerializer.Serialize(assault,Json));
        }
    }
}
