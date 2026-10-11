using Godot;
using SpaceFleet.Sim;

static class ObstacleBvhChecks
{
    public static void Run()
    {
        SphereDistanceChecks.Run();
        int checks=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
        var random=new Random(9413);
        Vec3d V(double size)=>new((random.NextDouble()*2-1)*size,(random.NextDouble()*2-1)*size,(random.NextDouble()*2-1)*size);
        foreach(double offset in new[]{0.0,1e12,-1e12})
        {
            Vec3d origin=new(offset,-offset,offset);var set=new ObstacleSet();
            for(int i=0;i<280;i++)
            {
                double r=150+random.NextDouble()*4850;var parts=new List<ObstaclePart>();
                for(int j=0;j<1+i%4;j++)parts.Add(j%2==0?ObstaclePart.Sphere(V(300),r):ObstaclePart.Capsule(V(800),V(800),r));
                set.Add(new($"Test{i}",origin+V(70000),new Quaternion(Vector3.Up,(float)random.NextDouble()),parts.ToArray()));
            }
            // Identical shapes exercise both object and primitive tie ordering.
            set.Add(new("Tie-first",origin,Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,50),ObstaclePart.Sphere(Vec3d.Zero,50)));
            set.Add(new("Tie-last",origin,Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,50)));
            set.BuildIndex();
            for(int i=0;i<10000;i++)
            {
                Vec3d a=origin+V(85000),b=i%17==0?a:origin+V(85000);double padding=i%7==0?2000:0;
                if(i%4==0){a=set[i%set.Count].Position;b=a+V(15000);}
                bool expected=ObstacleLinearReference.SegmentBlocked(set,a,b,out var old,padding);
                bool actual=set.SegmentBlocked(a,b,out var hit,padding);
                Check(expected==actual && (!actual || old==hit),"BVH first segment hit equals full scan bit for bit");
                Check(set.BlocksSegment(a,b,padding)==expected,"Unordered visibility query equals ordered reference");
                double radius=random.NextDouble()*15000;
                expected=ObstacleLinearReference.ClosestApproach(set,a,radius,out var oldNear);
                actual=set.ClosestApproach(a,radius,out var near);
                Check(expected==actual && (!actual || oldNear==near),"BVH proximity equals full scan bit for bit");
            }
            Check(set.SegmentBlocked(origin,origin,out var tie) && tie.Obstacle.Name=="Tie-first" && tie.Part==0,"Insertion order breaks equal hit ties");
            set.Add(new("After-build",origin+new Vec3d(200000,0,0),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,100)));
            Check(set.ClosestApproach(origin+new Vec3d(200000,0,0),0,out _),"Add invalidates cached tree");
            set.Clear();Check(!set.SegmentBlocked(origin,origin,out _) && !set.ClosestApproach(origin,1e9,out _),"Clear invalidates tree");
            for(int trial=0;trial<90;trial++)
            {
                var shapes=new ObstacleSet();
                for(int j=0;j<25;j++)shapes.Add(new($"Collision{j}",origin+V(1400),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,80+random.NextDouble()*350)));
                var a=new ShipBody("COMPARE",ShipClass.Escort,Faction.Blue);var b=new ShipBody("COMPARE",ShipClass.Escort,Faction.Blue);
                Vec3d start=origin+V(1500);var pose=Quaternion.FromEuler(V(2).ToVector3());var velocity=V(2000).ToVector3();
                a.Place(start,pose);b.Place(start,pose);a.Velocity=b.Velocity=velocity;
                a.PrevPosition=b.PrevPosition=start;a.Position=b.Position=start+Vec3d.From(velocity)*.8;
                ShipCollision.ResolveObstacles(new[]{a},shapes,1);
                ShipCollision.ResolveObstaclesLinear(new[]{b},shapes,1);
                Check(a.Position==b.Position && a.Velocity==b.Velocity && a.LastCollision==b.LastCollision,"Sweeps and successive overlap corrections preserve order");
                Check(a.Damage.Modules.Select(x=>x.Health).SequenceEqual(b.Damage.Modules.Select(x=>x.Health)),"Collision damage remains identical");
            }
        }
        var world=new SimWorld();BattleSetup.Spawn(world,new(){Battlefield=Battlefield.AsteroidCluster});
        foreach(bool doctrine in new[]{true,false})
        {
            world.Doctrine=doctrine?new():null;
            for(int i=0;i<1000;i++)
            {
                var ship=world.Ships[i%world.Ships.Count];ship.Place(world.Asteroids!.Center+V(40000),Quaternion.Identity);
                Vector3 desired=V(1500).ToVector3();
                Check(world.ObstacleAvoidanceBaseline(ship,desired)==world.ObstacleAvoidanceCurrent(ship,desired),"BVH preserves summed avoidance vectors exactly");
            }
        }
        Console.WriteLine($"PASS: {checks} obstacle BVH/reference checks");
    }
    public static void Profile()
    {
        foreach(var field in Enum.GetValues<Battlefield>())
        {
            var world=new SimWorld();BattleSetup.Spawn(world,new(){Battlefield=field,Objective=BattleObjective.Domination});
            Vec3d center=new(0,0,-75000);var random=new Random(5481);
            foreach(var ship in world.Ships)ship.Place(center+new Vec3d(random.Next(-20000,20000),random.Next(-5000,5000),random.Next(-20000,20000)),Quaternion.Identity);
            var samples=new List<object>();
            for(int repeat=0;repeat<6;repeat++)
            {
                double Run(bool old)
                {
                    long start=System.Diagnostics.Stopwatch.GetTimestamp();
                    for(int i=0;i<100000;i++)
                    {var ship=world.Ships[i%24];if(old)world.ObstacleAvoidanceBaseline(ship,new(250,50,-400));else world.ObstacleAvoidanceCurrent(ship,new(250,50,-400));}
                    return System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMicroseconds/100000;
                }
                double before,after;if(repeat%2==0){before=Run(true);after=Run(false);}else{after=Run(false);before=Run(true);}
                if(repeat>0)samples.Add(new{repeat,beforeUs=before,afterUs=after});
            }
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{field=field.ToString(),samples}));
        }
    }
}
