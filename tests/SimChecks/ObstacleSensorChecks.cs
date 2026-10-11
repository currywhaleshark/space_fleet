using Godot;
using SpaceFleet.Sim;

static class ObstacleSensorChecks
{
    public static void Run()
    {
        int checks=0;
        void Check(bool ok,string why) { if(!ok) throw new Exception(why); checks++; }
        foreach(double offset in new[]{0.0,1e12})
        {
            var w=new SimWorld(); Vec3d origin=new(offset,offset,-offset);
            var a=w.Add(new ShipBody("OBSERVER",ShipClass.Battleship,Faction.Blue));
            var b=w.Add(new ShipBody("TARGET",ShipClass.Battleship,Faction.Red));
            a.Place(origin,Quaternion.Identity); b.Place(origin+new Vec3d(0,0,-10000),Quaternion.Identity);
            b.Velocity=new Vector3(120,0,0);
            void Observe(double t)=>w.Sensors.Update(w.Ships,t,force:true,probes:w.Probes,missiles:w.Missiles);
            Observe(0); var seen=w.Sensors.Track(Faction.Blue,b);
            Check(seen.Level==TrackLevel.Locked,"Fixture is genuinely locked before obstruction");
            w.Obstacles.Add(new("WALL",origin+new Vec3d(0,0,-5000),Quaternion.Identity,
                ObstaclePart.Capsule(new(-50000,0,0),new(50000,0,0),1000)));
            Observe(.25); var memory=w.Sensors.Memory(Faction.Blue,b,.25)!;
            Check(w.Sensors.Track(Faction.Blue,b).Level==TrackLevel.None && memory is not null,"Occlusion removes live lock, creates separate memory");
            Check((memory!.Predict(10)-seen.EstimatedPosition-new Vec3d(1200,0,0)).Length()<.001,"Memory extrapolates last observed velocity in double coordinates");
            Check(memory.ErrorMeters(20)>memory.ErrorMeters(10) && !memory.Fresh(60),"Uncertainty grows and expires at sixty seconds");
            b.Place(origin+new Vec3d(3000,0,-11000),Quaternion.Identity); b.Velocity=new(-900,700,300);
            Observe(1);
            Check(w.Sensors.Memory(Faction.Blue,b,1)==memory,"Hidden motion never rewrites the frozen observation");
            var brain=w.AttachBrain(a,ShipOrder.AttackOn(b));
            w.Sensors.Update(w.Ships,0,force:true); // world time is still zero
            int ammo=a.Railguns.Sum(g=>g.Rounds),missiles=a.Ordnance.Missiles;
            for(int i=0;i<30;i++) w.Step();
            Check(brain.Target==b && brain.Activity=="추정 항적 추적" && brain.DesiredVelocity.Length()>0,"AI retains and navigates toward memory");
            Check(a.Railguns.Sum(g=>g.Rounds)==ammo && a.Ordnance.Missiles==missiles,"Memory is never a firing track");
            w.Obstacles.Clear(); Observe(2);
            Check(w.Sensors.Memory(Faction.Blue,b,2) is null && w.Sensors.Track(Faction.Blue,b).Level>=TrackLevel.Contact,"Reacquisition clears memory even after obstacle removal");
            Check(w.Sensors.OcclusionStats(Faction.Blue) is {LockLosses:1,Reacquisitions:1,LockLostSeconds:>0},"Occlusion and recovery metrics are recorded");
            b.Place(origin+new Vec3d(0,0,-1e8),Quaternion.Identity); Observe(3);
            Check(w.Sensors.Track(Faction.Blue,b).Level==TrackLevel.None && w.Sensors.Memory(Faction.Blue,b,3) is null,"Range-only loss never creates memory");
        }
        var net=new SimWorld();
        var watcher=net.Add(new ShipBody("WATCH",ShipClass.Battleship,Faction.Blue));
        var enemy=net.Add(new ShipBody("ENEMY",ShipClass.Battleship,Faction.Red)); enemy.Place(new(0,0,-10000),Quaternion.Identity);
        var wall=new Obstacle("WALL",new(0,0,-5000),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,1000)); net.Obstacles.Add(wall);
        Check(SensorNet.Measure(watcher,enemy,obstacles:net.Obstacles)==(0f,0f,0f),"Direct measurement respects obstacle");
        var scout=net.Add(new ShipBody("SCOUT",ShipClass.Interceptor,Faction.Blue)); scout.Place(new(5000,0,-8000),Quaternion.Identity);
        net.Sensors.Update(net.Ships,0,force:true);
        Check(net.Sensors.Track(Faction.Blue,enemy).Level>=TrackLevel.Contact,"Alternate observer shares observation across blocked friendly datalink");
        scout.Place(new(0,0,1e7),Quaternion.Identity);
        var probe=new ReconProbe { Id=1, Owner=scout, Definition=new(), Position=new(2000,0,-9000), Health=4, Direction=Vector3.Forward };
        net.Sensors.Update(net.Ships,1,force:true,probes:new[]{probe});
        Check(net.Sensors.Track(Faction.Blue,enemy).Level is >=TrackLevel.Contact and <TrackLevel.Locked,"Visible probe observes but cannot create high-quality lock alone");
        probe.Position=Vec3d.Zero; net.Sensors.Update(net.Ships,2,force:true,probes:new[]{probe});
        Check(net.Sensors.Track(Faction.Blue,enemy).Level==TrackLevel.None,"Occluded probe contributes zero signal");
        net.Sensors.Update(net.Ships,62,force:true);
        Check(net.Sensors.Memory(Faction.Blue,enemy,62) is null,"Expired memories are removed by sensor update");
        var small=new ReconProbe { Id=2,Owner=enemy,Definition=new(),Position=new(0,0,-2000),Health=4 };
        net.Sensors.Update(net.Ships,63,force:true,probes:new[]{small});
        Check(net.Sensors.SmallTrack(Faction.Blue,small)!.Value.Fresh(63),"Small contact is observed in front of wall");
        small.Position=new(0,0,-6000); net.Sensors.Update(net.Ships,63.25,force:true,probes:new[]{small});
        Check(net.Sensors.SmallTrack(Faction.Blue,small) is {Level:TrackLevel.None,Position.Z:-2000},"Occlusion invalidates small contact immediately, preserving last position");
        var jamWorld=new SimWorld();
        var receiver=jamWorld.Add(new ShipBody("RX",ShipClass.Battleship,Faction.Blue));
        var signal=jamWorld.Add(new ShipBody("SIGNAL",ShipClass.Battleship,Faction.Red)); signal.Place(new(0,0,-10000),Quaternion.Identity);
        var jammer=jamWorld.Add(new ShipBody("ECM",ShipDefinitions.For(HullKind.Escort,DesignFamily.Mars),Faction.Red));
        jammer.Place(new(4000,0,-10000),Quaternion.Identity); jammer.Power.SetPips(0,0,0,4,4);
        jamWorld.Sensors.Update(jamWorld.Ships,0,force:true); float jam=jamWorld.Sensors.Track(Faction.Blue,signal).JamRatio;
        Check(jam>0,"Fixture has an active supporting jammer");
        jamWorld.Obstacles.Add(new("SCREEN",new(2000,0,-5000),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,500)));
        jamWorld.Sensors.Update(jamWorld.Ships,.25,force:true);
        Check(jamWorld.Sensors.Track(Faction.Blue,signal).JamRatio<jam,"Only the blocked jammer loses influence at this observer");
        Vector3 HiddenMotion(double distance,bool destroyed)
        {
            var w=new SimWorld();
            var pilot=w.Add(new ShipBody("P",ShipClass.Interceptor,Faction.Blue));
            var t=w.Add(new ShipBody("T",ShipClass.Battleship,Faction.Red)); t.Place(new(0,0,-10000),Quaternion.Identity);
            w.Sensors.Update(w.Ships,0,force:true);
            w.Obstacles.Add(new("SCREEN",new(0,0,-600),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,200)));
            w.Sensors.Update(w.Ships,.25,force:true);
            t.Place(new(0,0,-distance),Quaternion.Identity); if(destroyed)t.Damage.Breakup(0,"unobserved");
            var brain=w.AttachBrain(pilot,ShipOrder.AttackOn(t));
            for(int i=0;i<2;i++) w.Step();
            return brain.DesiredVelocity;
        }
        Check(HiddenMotion(1500,true)==HiddenMotion(11000,false),"Hidden position and destruction cannot influence AI navigation or avoidance");
        Console.WriteLine($"PASS: {checks} obstacle sensor/memory checks");
    }
}
