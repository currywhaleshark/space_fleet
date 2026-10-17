using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Game;
using SpaceFleet.Sim;

static class AirDefenseLaserChecks
{
    static int checks;
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
    static ShipDefinition Edit(ShipDefinition d,Action<JsonObject> change)
    {var j=JsonNode.Parse(d.ToJson())!.AsObject();change(j);return ShipDefinition.Parse(j.ToJsonString(),true);}
    static ShipDefinition Definition(string id,bool laser,bool layered=false)
    {
        var d=CruiserTrials.Resolve(id,laser?"{}":"""{"defaultMainGun":null}""").Definition;
        return Edit(d,j=>{j["Decoys"]=null;if(!layered){j["PointDefense"]=null;j["DefenseDrones"]=null;j["Missiles"]=null;}});
    }
    static (SimWorld W,ShipBody Guard,ShipBody Source,ShipBody Anchor) Fixture(bool laser=true,bool layered=false,string id="earth_air_defense_cruiser",bool player=false)
    {
        var w=new SimWorld();var g=w.Add(new ShipBody("GUARD",Definition(id,laser,layered),Faction.Blue));
        var a=w.Add(new ShipBody("ANCHOR",CruiserTrials.PassiveTarget(),Faction.Blue));a.Place(new(0,0,5000),Quaternion.Identity);
        var s=w.Add(new ShipBody("SOURCE",CruiserTrials.Resolve("escort","""{"railgun":null,"pointDefense":null,"defenseDrones":null,"sensors":{"jammer":0}}""").Definition,Faction.Red));
        s.Place(new(0,0,-22000),new Quaternion(Vector3.Up,Mathf.Pi));
        foreach(var ship in w.Ships)ship.Control=new(){FlightAssist=false};
        if(player)g.Gunnery=new();else w.AttachBrain(g,ShipOrder.HoldAt(g.Position));
        w.Log=new BattleLog(w);return(w,g,s,a);
    }
    static Missile Threat(SimWorld w,ShipBody source,ShipBody target,Vec3d from,float speed=2000)
        =>CruiserTrials.AddThreat(w,source,target,from,(target.Position-from).ToVector3().Normalized()*speed);
    public sealed record WaveResult(string Hull,bool Laser,bool Layered,int Seed,int Incoming,int Intercepts,int Hits,int DistinctMountTargets,int Pulses,int Rails,float MaxHeat,float Speed);
    static WaveResult Wave(string id,bool laser,bool layered,int seed,int count=12,float speed=2000)
    {
        var(w,g,s,a)=Fixture(laser,layered,id);var rng=new Random(seed);
        for(int i=0;i<count;i++)
        {
            var p=new Vec3d((rng.NextDouble()-.5)*3200,(i%2==0?1:-1)*(150+rng.NextDouble()*550),-10000-i/4*1000);
            Threat(w,s,a,p,speed);
        }
        w.Sensors.Update(w.Ships,w.Time,true,w.Probes,w.Missiles);
        int distinct=0;float heat=0;
        for(int i=0;i<25*60;i++)
        {
            w.Step();heat=Math.Max(heat,g.Power.HeatFraction);
            distinct=Math.Max(distinct,g.Railguns.Select(x=>x.DefenseTarget).OfType<Missile>().Distinct().Count());
        }
        var log=w.Log!.Ship(g);
        return new(id,laser,layered,seed,count,log.MissilesIntercepted,w.Log.Ship(s).MissileHits,distinct,log.LaserPulses,log.Rails,heat,speed);
    }
    public static void Run()
    {
        foreach(string id in new[]{"earth_air_defense_cruiser","earth_air_defense_cruiser_t4","earth_air_defense_cruiser_t5"})
        {
            var stock=ShipDefinitions.ById(id);var resolved=FleetLoadouts.ResolveFit(FleetLoadouts.Empty(id));
            Check(stock.Battery!.Weapon.Laser is not null&&resolved.Definition.Components is null,"Factory laser requires no component research");
            Check(resolved.Definition.ToJson()==stock.ToJson(),"Factory loadout matches catalog snapshot");
            var p=HangarProfile.Create(id);long funds=p.Research!.Points[DesignFamily.Earth];
            var restored=HangarStorage.Parse(HangarStorage.Serialize(p));
            Check(FleetLoadouts.Resolve(restored.Squadron()).Leader.Battery!.Weapon.Laser is not null&&restored.Research!.Points[DesignFamily.Earth]==funds,"Save/load and launch retain free stock laser");
            var rail=FleetLoadouts.ResolveFit(FleetLoadouts.Empty(id) with{Components=new(MainGun:"main-single")}).Definition;
            Check(rail.Battery!.Weapon.Laser is null&&rail.Railgun!.Rounds>0,"Researched rail replacement still has real ammunition");
            var other=FleetLoadouts.ResolveFit(FleetLoadouts.Empty(id) with{Components=new(Sensor:"sensor-longrange")}).Definition;
            Check(other.Battery!.Weapon.Laser==stock.Battery.Weapon.Laser,"Other component replacement retains stock laser exactly");
        }
        var(f,g,src,anchor)=Fixture(player:true);
        Threat(f,src,anchor,new(500,400,-8000));Threat(f,src,anchor,new(-500,-400,-8000));
        f.Sensors.Update(f.Ships,0,true,f.Probes,f.Missiles);
        g.Gunnery!.Doctrine=FireDoctrine.Hold;CruiserTrials.Step(f,.2);
        Check(g.Railguns.All(x=>x.Laser!.Pulses==0&&x.DefenseTarget is null),"Hold suppresses autonomous defense");
        g.Gunnery.Doctrine=FireDoctrine.Manual;CruiserTrials.Step(f,.2);
        Check(g.Railguns.All(x=>x.Laser!.Pulses==0),"Manual suppresses autonomous defense");
        g.Gunnery.Doctrine=FireDoctrine.Focus;CruiserTrials.Step(f,.2);
        Check(g.Railguns.All(x=>x.Laser!.Pulses==0),"Explicit focus is not replaced by defense");
        g.Gunnery.Doctrine=FireDoctrine.Free;f.Step();
        Check(g.Railguns.Select(x=>x.DefenseTarget).OfType<Missile>().Distinct().Count()==2,"Upper/lower turrets split detected threats");
        var contact=f.Sensors.SmallTrack(g.Faction,g.Railguns.First().DefenseTarget!)!.Value;
        f.SmallTargetAim(g,contact,out var before,out _);var m=(Missile)contact.Target;m.Position+=new Vec3d(1e7,0,0);
        f.SmallTargetAim(g,contact,out var after,out _);Check(before==after,"Small target aim cannot read hidden live position");
        foreach(var x in g.Railguns)x.Reset();
        Check(g.Railguns.All(x=>x.DefenseTarget is null),"Reset clears defense assignments");
        var unseen=Fixture();Threat(unseen.W,unseen.Source,unseen.Anchor,new(1000000,0,-8000));CruiserTrials.Step(unseen.W,1);
        Check(unseen.Guard.Railguns.All(x=>x.DefenseTarget is null&&x.Laser!.Pulses==0),"Undetected distant missile cannot attract fire");
        var cooling=Fixture();Threat(cooling.W,cooling.Source,cooling.Anchor,new(400,300,-9000));
        cooling.Guard.Power.AddHeat(cooling.Guard.Definition.Power.HeatCapacityMj*.9f);
        cooling.W.Sensors.Update(cooling.W.Ships,0,true,cooling.W.Probes,cooling.W.Missiles);cooling.W.Step();
        Check(cooling.Guard.Railguns.All(x=>x.Laser!.Pulses==0&&x.Laser.AutoCooling),"Thermal hold also applies to defensive fire");
        var blocked=Fixture(player:true);
        Threat(blocked.W,blocked.Source,blocked.Anchor,new(0,400,-8000));
        blocked.W.Sensors.Update(blocked.W.Ships,0,true,blocked.W.Probes,blocked.W.Missiles);
        blocked.W.Obstacles.Add(new Obstacle("screen",new(0,200,-4000),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,500)));
        CruiserTrials.Step(blocked.W,.4);
        Check(blocked.Guard.Railguns.All(x=>x.Laser!.Pulses==0),"Known threat behind terrain cannot draw automatic fire");
        var friendly=Fixture();
        var friend=friendly.W.Add(new ShipBody("FRIEND",CruiserTrials.PassiveTarget(),Faction.Blue));friend.Place(new(0,200,-4000),Quaternion.Identity);
        friendly.W.Log=null;
        Threat(friendly.W,friendly.Source,friendly.Anchor,new(0,400,-8000));
        friendly.W.Sensors.Update(friendly.W.Ships,0,true,friendly.W.Probes,friendly.W.Missiles);
        CruiserTrials.Step(friendly.W,.3);
        Check(friendly.Guard.Railguns.All(x=>x.Laser!.Pulses==0),"Allied hull in the firing line holds the battery");
        var crossing=Fixture();
        var fast=Threat(crossing.W,crossing.Source,crossing.Anchor,new(500,50,-200),10000);
        crossing.W.Sensors.Update(crossing.W.Ships,0,true,crossing.W.Probes,crossing.W.Missiles);
        float yaw=crossing.Guard.Railguns[0].Yaw;crossing.W.Step();
        Check(Math.Abs(crossing.Guard.Railguns[0].Yaw-yaw)<=Mathf.DegToRad(crossing.Guard.Railguns[0].Mount!.YawRate)*SimWorld.TickDelta*1.6,"Close crossing cannot bypass the physical traverse ceiling");
        Check(crossing.Guard.Railguns.All(x=>x.Laser!.Pulses==0),"Unaligned close crossing does not receive instant laser damage");
        var lost=Fixture(player:true);Threat(lost.W,lost.Source,lost.Anchor,new(500,400,-8000));
        lost.W.Sensors.Update(lost.W.Ships,0,true,lost.W.Probes,lost.W.Missiles);
        foreach(var ally in lost.W.Ships.Where(x=>x.Faction==Faction.Blue))foreach(var module in ally.Damage.Modules.Where(x=>x.Definition.Kind==ModuleKind.Sensor))module.Health=0;
        CruiserTrials.Step(lost.W,.7);
        Check(lost.Guard.Railguns.All(x=>x.DefenseTarget is null),"Lost small contacts expire and release all mounts");
        var rows=new List<WaveResult>();
        foreach(string id in new[]{"earth_air_defense_cruiser","earth_air_defense_cruiser_t4","earth_air_defense_cruiser_t5"})
        foreach(bool layered in new[]{false,true})foreach(int seed in new[]{1,2,3,4})foreach(bool laser in new[]{false,true})
            rows.Add(Wave(id,laser,layered,seed));
        foreach(bool layered in new[]{false,true})foreach(int seed in new[]{1,2,3,4})foreach(bool laser in new[]{false,true})
            rows.Add(Wave("earth_air_defense_cruiser",laser,layered,seed,48,3500));
        foreach(var group in rows.GroupBy(x=>(x.Hull,x.Layered,x.Laser,x.Incoming,x.Speed)))
            Console.WriteLine($"{group.Key}: intercepted {group.Sum(x=>x.Intercepts)}/{group.Sum(x=>x.Incoming)}, hits {group.Sum(x=>x.Hits)}, distinct {group.Max(x=>x.DistinctMountTargets)}, heat {group.Max(x=>x.MaxHeat):F3}, pulses {group.Sum(x=>x.Pulses)} rails {group.Sum(x=>x.Rails)}");
        string output="docs/validation/aa_lasers_20261017";Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"waves.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        Check(rows.Where(x=>x.Laser&&!x.Layered).Sum(x=>x.Intercepts)>0,"Unassisted factory lasers physically destroy full-health incoming missiles");
        Check(rows.Where(x=>x.Laser&&x.Incoming==48&&!x.Layered).Sum(x=>x.Hits)>0,"Saturation can penetrate finite-traverse two-emitter defense");
        foreach(var group in rows.Where(x=>x.Incoming==12&&!x.Layered).GroupBy(x=>x.Hull))
            Check(group.Where(x=>x.Laser).Sum(x=>x.Intercepts)>group.Where(x=>!x.Laser).Sum(x=>x.Intercepts),"Laser improves matched missile interception at "+group.Key);
        Console.WriteLine($"PASS: {checks} air-defense factory/commands/sensors/thermal/wave checks");
    }
}
