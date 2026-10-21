using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Sim;

static class InterceptionChecks
{
    static int checks;
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
    static ShipDefinition Edit(ShipDefinition d,Action<JsonObject> edit)
    {var j=JsonNode.Parse(d.ToJson())!.AsObject();edit(j);return ShipDefinition.Parse(j.ToJsonString(),true);}
    static (SimWorld W,ShipBody G,ShipBody S,Missile M) Fixture(double range=30000)
    {
        var w=new SimWorld();var g=w.Add(new ShipBody("GUARD",ShipDefinitions.ById("earth_air_defense_cruiser_t4"),Faction.Blue));
        var s=w.Add(new ShipBody("SOURCE",CruiserTrials.Resolve("escort","""{"sensors":{"jammer":0},"pointDefense":null,"defenseDrones":null}""").Definition,Faction.Red));
        s.Place(new(0,0,-8000),Quaternion.Identity);
        var m=CruiserTrials.AddThreat(w,s,g,new(200,100,-range),Vector3.Back*2000);m.Age=0;
        s.Place(new(1000000,0,0),Quaternion.Identity);w.Sensors.Update(w.Ships,0,true,w.Probes,w.Missiles);
        return(w,g,s,m);
    }
    public static void Run()
    {
        foreach(double range in new[]{8000d,16000,30000,60000,120000})
        {
            var(w,g,_,m)=Fixture(range);var sample=w.Sensors.SmallTrack(g.Faction,m);
            if(range<=30000)
            {
                Check(sample is { } locked&&locked.Locked(0),"AA locks a burning missile through 30 km");
                var c=sample!.Value;
                Check(c.ErrorMeters>0&&c.Position!=m.Position,"Observed missile position has deterministic measurement uncertainty");
                Check(w.SmallTargetAim(g,c,out var before,out _,out var reason),"Useful range solution: "+reason);
                m.Position+=new Vec3d(1e6,0,0);
                Check(w.SmallTargetAim(g,c,out var after,out _)&&before==after,"Aiming ignores hidden live position");
                Check(!w.SmallTargetAim(g,c with{Level=TrackLevel.Identified},out _,out _,out reason)&&reason.Contains("잠금"),"Identified alone cannot shoot");
                Check(!w.SmallTargetAim(g,c with{SeenAt=-1},out _,out _,out reason)&&reason=="신호 소실","Stale lock cannot shoot");
            }
            else Check(sample is null||!sample.Value.Locked(0),"Distant missile cannot supply precision lock");
        }
        var f=Fixture(30000);var old=f.W.Sensors.SmallTrack(f.G.Faction,f.M)!.Value;
        f.M.Age=f.M.Definition.BurnSeconds+1;f.W.Sensors.Update(f.W.Ships,.25,true,f.W.Probes,f.W.Missiles);
        Check(f.W.Sensors.SmallTrack(f.G.Faction,f.M)!.Value.Level<TrackLevel.Locked,"Coasting reduces signature and releases distant lock");
        var f2=Fixture(30000);Check(old==f2.W.Sensors.SmallTrack(f2.G.Faction,f2.M)!.Value with{Target=old.Target,Observer=old.Observer},"Identical fixtures reproduce observation values");
        Check(!f.W.SmallTargetAim(f.G,old with{Position=f.G.Position+new Vec3d(0,0,-200000)},out _,out _,out var far)&&far.Contains("사거리 밖")&&far.Contains("km"),"Range failure includes actual/allowed distance");
        var shortGun=new ShipBody("SHORT",Edit(f.G.Definition,j=>j["MainBattery"]!["Weapon"]!["Laser"]!["FalloffMeters"]=5000),Faction.Blue);
        Check(SimWorld.SmallTargetRange(shortGun.Railgun!)<SimWorld.SmallTargetRange(f.G.Railgun!),"Individual laser hardware sets its interception range");
        var normal=Fixture(3000);normal.G.Gunnery=new(){Doctrine=FireDoctrine.Hold};normal.W.Sensors.Update(normal.W.Ships,0,true,normal.W.Probes,normal.W.Missiles);
        normal.G.DamageControl.Priority=RepairPriority.Hold;
        foreach(var mod in normal.G.Damage.Modules.Where(m=>m.Definition.Kind==ModuleKind.Sensor))normal.G.Damage.Hurt(mod,mod.Health,0,0,1);
        normal.W.Sensors.Update(normal.W.Ships,.6,true,normal.W.Probes,normal.W.Missiles);
        normal.W.ThreatTimeForCheck(60); // The last valid observation has actually expired in simulation time.
        for(int i=0;i<60;i++)normal.W.Step();
        Check(normal.G.Ordnance.PointDefense.All(p=>p.ShotCount==0)&&normal.G.Ordnance.Drones.Interceptions==0,"Blind PD/drone cannot use live missile position");
        var scout=normal.W.Add(new ShipBody("LINK",ShipDefinitions.ById("battleship"),Faction.Blue));
        scout.Place(normal.M.Position+new Vec3d(1000,0,0),Quaternion.Identity);
        normal.W.Sensors.Update(normal.W.Ships,normal.W.Time,true,normal.W.Probes,normal.W.Missiles);
        Check(normal.W.Sensors.SmallTrack(Faction.Blue,normal.M) is { } linked&&linked.Locked(normal.W.Time)
            && normal.W.SmallTargetAim(normal.G,linked,out _,out _),"Main gun can use allied missile lock after its own sensor is destroyed");
        var jam=Fixture(30000);float unjammed=jam.W.Sensors.SmallTrack(Faction.Blue,jam.M)!.Value.Signal;
        var screen=jam.W.Add(new ShipBody("ECM",ShipDefinitions.ById("mars_escort"),Faction.Red));
        screen.Place(jam.M.Position+new Vec3d(1000,0,0),Quaternion.Identity);
        screen.Power.SetPips(1,1,2,2,2);
        jam.W.Sensors.Update(jam.W.Ships,.25,true,jam.W.Probes,jam.W.Missiles);
        Check(jam.W.Sensors.SmallTrack(Faction.Blue,jam.M)!.Value.Signal<unjammed,"Escort ECM degrades missile lock quality");
        var probeOnly=Fixture(30000);
        var probe=new ReconProbe{Id=1,Owner=probeOnly.G,Definition=new(),Health=1,Position=probeOnly.M.Position+new Vec3d(1000,0,0)};
        probeOnly.G.Place(new(1e6,0,0),Quaternion.Identity);
        probeOnly.W.Sensors.Update(probeOnly.W.Ships,1,true,new[]{probe},probeOnly.W.Missiles);
        Check(probeOnly.W.Sensors.SmallTrack(Faction.Blue,probeOnly.M) is {Level:TrackLevel.Identified},"A probe alone identifies but cannot retain a former precision lock");
        AutoLaser();
        Console.WriteLine($"PASS: {checks} missile observation/range/laser discipline checks");
    }
    static void AutoLaser()
    {
        var f=Fixture();var w=f.W;var g=f.G;var target=f.S;
        // Isolate the long-range firing rule from sensor quality and physical barrel slew.
        var scout=w.Add(new ShipBody("SCOUT",ShipDefinitions.ById("battleship"),Faction.Blue));
        g.Gunnery=new(){Doctrine=FireDoctrine.Focus,Target=target};
        void At(float range){target.Place(new(0,0,-range),Quaternion.Identity);scout.Place(new(4000,0,-range),Quaternion.Identity);w.Sensors.Update(w.Ships,w.Time,true,w.Probes,w.Missiles);}
        At(150000);w.TryAutoFire(g,target,null,100,out var state);
        Check(state==GunneryStatus.Attenuated&&g.Railguns.All(x=>x.Laser!.RequestedUntil<0),"Locked distant hull does not waste laser power");
        float scale=g.Railgun!.Laser!.Definition.FalloffMeters;
        At(scale*1.7f);w.TryAutoFire(g,target,null,100,out state);Check(state!=GunneryStatus.Attenuated,"Auto starts above 25 percent transmission");
        At(scale*1.9f);w.TryAutoFire(g,target,null,100,out state);Check(state!=GunneryStatus.Attenuated,"Auto maintains the 20-25 percent band");
        At(scale*2.1f);w.TryAutoFire(g,target,null,100,out state);Check(state==GunneryStatus.Attenuated,"Auto stops below 20 percent");
        At(scale*1.9f);w.TryAutoFire(g,target,null,100,out state);Check(state==GunneryStatus.Attenuated,"Auto does not chatter back on in the hysteresis band");
        At(3000);g.Power.AddHeat(g.Definition.Power.HeatCapacityMj*.6f);w.TryAutoFire(g,target,null,100,out state);
        Check(state==GunneryStatus.DefenseReserve,"AA reserves thermal capacity for incoming weapons");
        g.Power.Reset();At(150000);
        var gun=g.Railguns[0];Vector3 dir=gun.Direction;
        Check(w.FireRailgun(g,gun,dir).Fired,"Manual laser request remains available outside automatic effective range");
    }
}
