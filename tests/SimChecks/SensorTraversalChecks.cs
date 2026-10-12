using Godot;
using SpaceFleet.Sim;

static class SensorTraversalChecks
{
    public static void Run()
    {
        int checks=0;
        void Check(bool value,string why) { if(!value)throw new Exception(why);checks++; }
        void SameTrack(SensorTrack a,SensorTrack b)
        {
            if(a.Observation is {} x && b.Observation is {} y)
            { Check(x.Observer.Callsign==y.Observer.Callsign,"Sensor winner/tie changed");a=a with { Observation=x with { Observer=y.Observer } }; }
            Check(a==b,"Sensor signal/position/quality/observation changed");
        }
        foreach(int count in new[]{12,48})
        foreach(double offset in new[]{0.0,1e12})
        foreach(double distance in new[]{2000.0,20000.0,80000.0,200000.0})
        {
            var current=Fixture(count,offset,distance);var reference=Fixture(count,offset,distance);
            for(int step=0;step<48;step++)
            {
                double time=step<40?step*.25:80+(step-40)*.25;
                foreach(var world in new[]{current,reference})
                {
                    if(step%12==0)
                    {
                        world.Obstacles.Clear();
                        if(step%24==0)
                            world.Obstacles.Add(new("wall",new Vec3d(offset,-offset,offset-distance*.5),Quaternion.Identity,
                                ObstaclePart.Capsule(new(-1e6,0,0),new(1e6,0,0),step==0?1000:10000)));
                        else
                            world.Obstacles.Add(new("partial",new Vec3d(offset+4000,-offset,offset-distance*.5),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,1800)));
                    }
                    for(int i=0;i<count;i++)
                    {
                        var ship=world.Ships[i];
                        ship.Position+=new Vec3d(i%2==0?73:-73, i%3==0?41:-17,0);
                        if(step==20 && i%7==0)
                            foreach(var module in ship.Damage.Modules.Where(m=>m.Definition.Kind==ModuleKind.Sensor))
                                ship.Damage.Hurt(module,module.Health,time,0,1);
                        if(step==31 && i%11==0)ship.Damage.Catastrophe(time,"sensor traversal test");
                    }
                }
                current.Sensors.Update(current.Ships,time,force:true,probes:current.Probes,missiles:current.Missiles);
                reference.Sensors.ReferenceUpdate(reference.Ships,time,force:true,probes:reference.Probes,missiles:reference.Missiles);
                foreach(Faction side in Enum.GetValues<Faction>())
                {
                    Check(current.Sensors.OcclusionStats(side)==reference.Sensors.OcclusionStats(side),"Occlusion loss/recovery/time counters changed");
                    for(int i=0;i<count;i++)
                    {
                        var a=current.Ships[i];var b=reference.Ships[i];
                        SameTrack(current.Sensors.Track(side,a),reference.Sensors.Track(side,b));
                        var x=current.Sensors.Memory(side,a,time);var y=reference.Sensors.Memory(side,b,time);
                        Check((x is null)==(y is null),"Occlusion memory creation/expiry changed");
                        if(x is not null && y is not null)
                        {
                            SameTrack(x.LastTrack,y.LastTrack);
                            Check((x with { LastTrack=y.LastTrack })==y,"Frozen memory changed");
                        }
                    }
                }
                for(int i=0;i<current.Probes.Count;i++)
                    Check(current.Probes[i].Observing==reference.Probes[i].Observing,"Probe observation contribution changed");
                for(int i=0;i<count;i++)
                {
                    var a=current.Ships[i].Ordnance.Interceptor;var b=reference.Ships[i].Ordnance.Interceptor;
                    Check((a.NewContacts,a.NewIdentifications,a.FirstAction)==(b.NewContacts,b.NewIdentifications,b.FirstAction),"Probe discovery credit changed");
                }
            }
        }
        var planning=Fixture(12,0,20000);
        planning.Sensors.Update(planning.Ships,0,force:true);
        var first=planning.PlanningForCheck(Faction.Blue);
        Check(ReferenceEquals(first,planning.PlanningForCheck(Faction.Blue)),"Same-tick planners must reuse the same observation snapshot");
        Check(!ReferenceEquals(first,planning.PlanningForCheck(Faction.Red)),"Opposing planners must not share observations");
        planning.Step();
        var second=planning.PlanningForCheck(Faction.Blue);
        Check(ReferenceEquals(first,second),"Unchanged sensor observations can share exact samples across ticks");
        planning.Sensors.Update(planning.Ships,planning.Time,force:true);
        planning.Step();
        var changed=planning.PlanningForCheck(Faction.Blue);
        Check(!ReferenceEquals(second,changed),"Changed sensor observations invalidate planning samples");
        planning.Obstacles.Add(new("new geometry",Vec3d.Zero,Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,100)));
        planning.Step();
        var geometry=planning.PlanningForCheck(Faction.Blue);
        Check(!ReferenceEquals(changed,geometry),"Changed static geometry invalidates planning samples");
        planning.ResetWeapons();
        Check(!ReferenceEquals(geometry,planning.PlanningForCheck(Faction.Blue)),"Planning cache reset");
        Console.WriteLine($"PASS: {checks} sensor traversal/reference checks");
    }

    private static SimWorld Fixture(int count,double offset,double distance)
    {
        var world=new SimWorld();
        for(int i=0;i<count;i++)
        {
            var def=ShipDefinitions.For((HullKind)(i%3),i%6<3?DesignFamily.Earth:DesignFamily.Mars);
            var ship=world.Add(new ShipBody($"S{i}",def,(Faction)(i%2)));
            ship.Place(new Vec3d(offset+(i/2%6)*3000,-offset+i/12*4000,offset-(i%2)*distance),Quaternion.Identity);
            ship.Velocity=new Vector3(i%2==0?120:-120,0,0);
            if(i%4==0)ship.Power.SetPips(0,0,0,4,4);
            if(i%7==0)
                ((List<ReconProbe>)world.Probes).Add(new ReconProbe { Id=(uint)i,Owner=ship,Definition=new(),Health=4,
                    Position=ship.Position+new Vec3d(6000,1200,-distance*.35),Direction=Vector3.Forward });
        }
        return world;
    }
}
