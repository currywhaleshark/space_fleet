using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Sim;

static class DroneCombatChecks
{
    private static int _checks;
    private static void Check(bool ok,string why) { if(!ok) throw new Exception(why); _checks++; }
    private static void Step(SimWorld world,double seconds) => AntimatterChecks.Step(world,seconds);
    private static ShipDefinition Definition(string name,bool pd=false,float droneHealth=8)
    {
        using var stream=typeof(ShipDefinitions).Assembly.GetManifestResourceStream($"SpaceFleet.data.ships.{name}.json")!;
        using var reader=new StreamReader(stream);
        var json=JsonNode.Parse(reader.ReadToEnd())!.AsObject();
        if(!pd) json.Remove("pointDefense");
        if(json["defenseDrones"] is { } d) d["hitPoints"]=droneHealth;
        return ShipDefinition.Parse(json.ToJsonString());
    }
    private static (SimWorld World,ShipBody IC,ShipBody BB) Fixture(bool auxiliary=false,float health=8)
    {
        var w=new SimWorld(); var icDef=Definition("interceptor",auxiliary); var bbDef=Definition("battleship",droneHealth:health);
        var ic=w.Add(new ShipBody("IC",icDef.Flight,Faction.Blue,icDef));
        var bb=w.Add(new ShipBody("BB",bbDef.Flight,Faction.Red,bbDef));
        ic.Place(new Vec3d(0,0,5000),Quaternion.Identity); bb.Place(Vec3d.Zero,Quaternion.Identity);
        ic.Control=bb.Control=new ShipControl { FlightAssist=false }; ic.Ordnance.Antimatter.Jettison();
        w.Log=new BattleLog(w); return(w,ic,bb);
    }
    private static void FreezeDrones(ShipBody carrier,SimWorld world)
    {
        foreach(var module in carrier.Damage.Modules.Where(m=>m.Definition.Kind==ModuleKind.Sensor))
            carrier.Damage.Hurt(module,module.Health,world.Time,0,1);
    }
    private static void OnlyOne(SimWorld world,ShipBody ic,ShipBody bb)
    {
        FreezeDrones(bb,world);
        for(int i=1;i<bb.Ordnance.Drones.Rounds.Length;i++) world.DamageDrone(ic,bb,i,float.MaxValue,world.Time);
        world.Log=new BattleLog(world);
    }
    private static void PositionDrone(ShipBody bb,Vec3d position) => bb.Place(position-Vec3d.From(bb.Ordnance.Drones.LocalPosition(0,0)),Quaternion.Identity);
    public static void Run()
    {
        CheckOffense(); CheckShieldTracers(); CheckDamage(); CheckPriority(); CheckRailgun();
        Check(ShipDefinitions.For(HullKind.Interceptor).PointDefense!.Mounts.Length==2,"Interceptor carries separate ventral and dorsal auxiliaries");
        for(int index=0;index<2;index++)
        {
            CheckAuxiliary(index);
            uint slow=Flyby(80,index),fast=Flyby(1200,index);
            Check(slow>10 && fast<slow*.65f,$"Close fast drones evade auxiliary {index} tracking: {slow} vs {fast}");
            Console.WriteLine($"IC auxiliary {index} vs drone / 5 s: slow {slow}, fast {fast} shots");
        }
        Console.WriteLine($"PASS: {_checks} drone combat checks");
    }
    private static void CheckOffense()
    {
        var (w,ic,bb)=Fixture(); var d=bb.Ordnance.Drones;
        d.Assign(DroneSector.Fore); Step(w,20);
        ic.Place(new Vec3d(0,300,-2000),Quaternion.Identity);
        float shield=ic.Damage.Shield;
        Step(w,8);
        Check(d.ShipHits>0 && ic.Damage.Shield<shield && d.RemainingRounds<640,"Drones attack hostile interceptors and consume their own finite rounds");
        Check(w.Log!.Ship(ic).ShieldDamage>0,"Drone hits enter the normal ship damage log");
        for(int i=0;i<180;i++) w.Step();
        Check(w.Impacts.Any(p=>p.Weapon==BattleWeapon.DefenseDrone && p.Shooter==bb && p.Hit.Target==ic),"Drone impacts drive ordinary shield/armor/hull feedback");
        // Thin armor and internal modules use the same DamageRay path after shield depletion.
        for(int i=0;i<1200 && w.Log.Ship(ic).ModuleDamage==0;i++) w.Step();
        Check(w.Log.Ship(ic).ModuleDamage>0,"Drone ship rounds can penetrate thin interceptor armor and damage modules");
        var outside=Fixture(); outside.BB.Ordnance.Drones.Assign(DroneSector.Fore); Step(outside.World,20);
        outside.IC.Place(new Vec3d(1800,300,0),Quaternion.Identity); Step(outside.World,3);
        Check(outside.BB.Ordnance.Drones.RemainingRounds==640,"Sector-limited drone guns do not attack ships outside assigned azimuth");
        var priority=Fixture(); var drones=priority.BB.Ordnance.Drones;
        drones.Assign(DroneSector.Fore); Step(priority.World,20);
        priority.IC.Place(new Vec3d(0,300,-1700),Quaternion.Identity);
        priority.World.Sensors.Update(priority.World.Ships,priority.World.Time,force:true);
        Check(priority.World.LaunchMissile(priority.IC,priority.BB).Fired,"Priority test missile launches");
        var missile=priority.World.Missiles.Single();
        missile.Position=missile.PrevPosition=priority.BB.Position+new Vec3d(0,0,-1800);
        missile.Age=missile.Definition.BurnSeconds+1; missile.Velocity=Vector3.Zero; missile.Health=100000;
        Step(priority.World,2);
        Check(drones.RemainingRounds<640 && drones.ShipHits==0,"Incoming missiles keep priority over an interceptor in the same defense sector");
    }
    private static void CheckShieldTracers()
    {
        var (w,ic,bb)=Fixture(); bb.Ordnance.Drones.Assign(DroneSector.Fore); Step(w,20);
        ic.Place(new Vec3d(0,300,-2000),Quaternion.Identity);
        for(int i=0;i<1200 && !w.PointDefenseShots.Any(s=>s.Hit);i++) { ic.Damage.Reset(); w.Step(); }
        var hits=w.PointDefenseShots.Where(s=>s.Hit).ToArray(); var shell=ic.Definition.ShieldEnvelope;
        Check(hits.Length>0 && hits.All(s=>Mathf.Abs((((s.To-ic.Position).ToVector3()-shell.Center)/shell.Radii).Length()-1)<.0001f),
            "Drone tracers stop on the active shield skin rather than piercing through to the hull center");
    }
    private static void CheckDamage()
    {
        var (w,ic,bb)=Fixture(); var d=bb.Ordnance.Drones;
        Check(!w.DamageDrone(ic,bb,0,4,w.Time) && d.Alive(0) && d.HealthFraction(0)==.5f,"First auxiliary hit damages an individual drone");
        Check(w.DamageDrone(ic,bb,0,4,w.Time) && !d.Alive(0) && d.SurvivingCount==7 && d.ArmedCount==7 && d.RemainingRounds==560,"Second hit removes the drone and its available ammunition");
        Check(!w.DamageDrone(ic,bb,0,4,w.Time) && w.OrdnanceEvents.Count==1 && w.Log!.Ship(ic).DronesDestroyed==1,"Destruction event and kill credit are emitted exactly once");
        Vector3 point=d.LocalPosition(0,0); d.Assign(DroneSector.Fore); Step(w,20); bb.Damage.Reset();
        Check(!d.Alive(0) && d.LocalPosition(0,0)==point && d.Rounds[0]==80,"Dead drones do not move, fire or revive on a carrier repair/command");
        for(int i=1;i<8;i++) w.DamageDrone(ic,bb,i,100,w.Time);
        Check(d.SurvivingCount==0 && !d.Active && !d.Assign(DroneSector.Aft),"Destroyed swarm has no command or firing capability");
        w.ResetWeapons();
        Check(d.SurvivingCount==8 && d.RemainingRounds==640 && d.HealthFraction(0)==1 && d.Sector==DroneSector.AllAround,"Explicit practice reset recreates a healthy swarm");
        var normal=Fixture(); var reduced=Fixture();
        normal.BB.Ordnance.Drones.Assign(DroneSector.Fore); reduced.BB.Ordnance.Drones.Assign(DroneSector.Fore);
        for(int i=4;i<8;i++) reduced.World.DamageDrone(reduced.IC,reduced.BB,i,100,0);
        Step(normal.World,20); Step(reduced.World,20);
        normal.IC.Place(new Vec3d(0,300,-2000),Quaternion.Identity); reduced.IC.Place(new Vec3d(0,300,-2000),Quaternion.Identity);
        Step(normal.World,3); Step(reduced.World,3);
        int full=640-normal.BB.Ordnance.Drones.RemainingRounds,half=320-reduced.BB.Ordnance.Drones.RemainingRounds;
        Check(full>0 && half==full/2,$"Loss of four drones halves actual sustained shots: {full}/{half}");
    }
    private static void CheckAuxiliary(int index)
    {
        var (w,ic,bb)=Fixture(true,100000); OnlyOne(w,ic,bb); ic.Place(Vec3d.Zero,Quaternion.Identity);
        var mount=ic.Ordnance.PointDefense[index];
        int side=index==0 ? -1 : 1;
        Check(mount.Definition.YawRate==90 && mount.Definition.ElevationRate==60 && mount.Definition.YawDegrees==100,"Interceptor auxiliary has explicit finite rates and traverse stops");
        PositionDrone(bb,new Vec3d(600,side*300,600)); Step(w,.1);
        Check(mount.TargetKind==PointDefenseTarget.Drone && !mount.Aligned && mount.ShotCount==0,"Auxiliary acquires a drone but cannot snap-fire before slewing");
        Check(Math.Abs(mount.Traverse)<=Mathf.DegToRad(9.01f) && mount.Elevation<=Mathf.DegToRad(6.01f),"World tracking respects yaw/pitch degrees per second");
        Step(w,5);
        Check(mount.Aligned && mount.ShotCount>0 && bb.Ordnance.Drones.HealthFraction(0)<1,"Aligned auxiliary actually damages the independently positioned drone");
        Check(ic.Ordnance.PointDefense[1-index].ShotCount==0,"Opposite deck cannot shoot through the hull into the other hemisphere");
        uint before=mount.ShotCount; PositionDrone(bb,new Vec3d(0,0,-600)); Step(w,2);
        Check(mount.ShotCount==before && mount.TargetKind==PointDefenseTarget.None,"Forward blind spot cannot engage drones");
        PositionDrone(bb,new Vec3d(0,-side*400,600)); Step(w,2);
        Check(mount.ShotCount==before,"Turret cannot fire beyond its deck elevation/depression limits");
        Check(ic.Ordnance.PointDefense[1-index].ShotCount>0,"Opposite deck auxiliary takes over when the drone crosses hemispheres");
        PositionDrone(bb,new Vec3d(600,side*300,600)); Step(w,3);
        w.DamageDrone(ic,bb,0,float.MaxValue,w.Time); before=mount.ShotCount; Step(w,1);
        Check(mount.ShotCount==before && mount.TargetKind==PointDefenseTarget.None,"Destroyed drone is immediately dropped from fire control");
        var actual=Fixture(true); OnlyOne(actual.World,actual.IC,actual.BB); actual.IC.Place(Vec3d.Zero,Quaternion.Identity);
        PositionDrone(actual.BB,new Vec3d(0,side*250,400));
        for(int i=0;i<3600 && actual.BB.Ordnance.Drones.Alive(0);i++) actual.World.Step();
        Check(!actual.BB.Ordnance.Drones.Alive(0) && actual.World.Log!.Ship(actual.IC).DronesDestroyed==1,"Stock interceptor auxiliary destroys a stock 8-HP drone without external damage injection");
        Console.WriteLine($"IC auxiliary {index} stock drone kill: {actual.World.Time:0.00}s, {actual.IC.Ordnance.PointDefense[index].ShotCount} shots");
        // Hemisphere follows the ship, including a 180-degree roll, rather than world up/down.
        ic.Ordnance.Reset();
        ic.Place(Vec3d.Zero,new Quaternion(Vector3.Forward,Mathf.Pi)); bb.Ordnance.Drones.Reset(); OnlyOne(w,ic,bb);
        PositionDrone(bb,Vec3d.From(ic.Orientation*new Vector3(0,side*300,600))); Step(w,3);
        Check(mount.ShotCount>0 && ic.Ordnance.PointDefense[1-index].ShotCount==0,"Rolled ship preserves deck-relative auxiliary coverage");
    }
    private static void CheckPriority()
    {
        var (w,ic,bb)=Fixture(true,100000); OnlyOne(w,ic,bb); ic.Place(Vec3d.Zero,Quaternion.Identity);
        PositionDrone(bb,new Vec3d(0,-300,600));
        var def=Definition("escort"); var launcher=w.Add(new ShipBody("LAUNCH",def.Flight,Faction.Red,def)); launcher.Place(new Vec3d(0,0,7000),Quaternion.Identity);
        w.Log=new BattleLog(w); w.Sensors.Update(w.Ships,w.Time,force:true);
        Check(w.LaunchMissile(launcher,ic).Fired,"Auxiliary priority missile launches");
        var m=w.Missiles.Single(); m.Age=m.Definition.BurnSeconds+1; m.Health=100000; m.Velocity=Vector3.Zero;
        Vector3 direction=new Vector3(0,-1,1).Normalized();
        m.Position=m.PrevPosition=Vec3d.From(direction*1700); Step(w,.1);
        Check(ic.Ordnance.PointDefense[0].TargetKind==PointDefenseTarget.Drone,"Out-of-range missile pretrack does not suppress an in-range drone");
        m.Position=m.PrevPosition=Vec3d.From(direction*800); Step(w,.1);
        Check(ic.Ordnance.PointDefense[0].TargetKind==PointDefenseTarget.Missile,"In-range hostile missile takes priority over the drone");
    }
    private static void CheckRailgun()
    {
        Check(SimWorld.DroneIntersection(new Vec3d(-100,0,0),new Vec3d(200,0,0),4,out double t) && Math.Abs(t-.48)<1e-12,"Fast segment finds first sphere entry instead of tunneling through the drone");
        Check(!SimWorld.DroneIntersection(new Vec3d(-100,5,0),new Vec3d(200,0,0),4,out _),"Near miss does not hit a drone sphere");
        foreach(bool blocked in new[]{false,true})
        {
            var (w,ic,bb)=Fixture(); OnlyOne(w,ic,bb); Vec3d offset=new(1e9,-2e9,3e9);
            ic.Place(offset,Quaternion.Identity); PositionDrone(bb,offset+new Vec3d(0,0,-900));
            if(blocked) { var block=w.Add(new ShipBody("BLOCK",ShipClass.Escort,Faction.Red,Definition("escort"))); block.Place(offset+new Vec3d(0,0,-500),Quaternion.Identity); }
            w.Log=new BattleLog(w);
            var fired=w.FireRailgun(ic,Vector3.Forward); Check(fired.Fired,"Direct fire test launches a real main-gun projectile");
            Step(w,.2);
            Check(bb.Ordnance.Drones.Alive(0)==blocked,blocked ? "Nearer hull occludes a drone behind it" : "A real rail round destroys a small drone at billion-meter world coordinates");
        }
    }
    private static uint Flyby(float speed,int index)
    {
        var (w,ic,bb)=Fixture(true,100000); OnlyOne(w,ic,bb); ic.Place(Vec3d.Zero,Quaternion.Identity);
        for(int i=0;i<300;i++)
        {
            PositionDrone(bb,new Vec3d((i/60d-2.5)*speed,index==0 ? -200 : 200,350)); bb.Velocity=Vector3.Right*speed;
            w.Step();
        }
        return ic.Ordnance.PointDefense[index].ShotCount;
    }
}
