using Godot;
using SpaceFleet.Sim;

static class NavigationVisibilityChecks
{
    public static void Run()
    {
        int checks=0;
        void Check(bool value,string why){if(!value)throw new Exception(why);checks++;}
        foreach(double shift in new[]{0.0,1e9,1e12,1e15})
        {
            var random=new Random(8104);var center=new Vec3d(shift,-shift,shift);
            Vec3d Offset(double scale)=>new((random.NextDouble()-.5)*scale,(random.NextDouble()-.5)*scale,(random.NextDouble()-.5)*scale);
            var obstacles=new ObstacleSet();
            for(int i=0;i<32;i++)
                obstacles.Add(new($"V{i}",center+Offset(30000),new Quaternion(Vector3.Up,(float)(random.NextDouble()*3)),
                    ObstaclePart.Sphere(Offset(500),200+random.NextDouble()*1000),
                    ObstaclePart.Capsule(new(-800,0,0),new(800,0,0),350)));
            var index=obstacles.VisibilityIndex()!;
            for(int i=0;i<16;i++)
            {
                Vec3d origin=i<4?obstacles[i].Position:center+Offset(50000);
                var query=new ObstacleBvh.VisibilityQuery(index,origin);
                for(int n=0;n<1500;n++)
                {
                    var direction=n<600?new Vec3d((n%17-8)*.125,(n/17%17-8)*.125,n%2==0?1:-1):Offset(1);
                    if(n%3==1)direction=new(direction.Z,direction.X,direction.Y);
                    if(n%3==2)direction=new(direction.Y,direction.Z,direction.X);
                    Vec3d end=n==0?origin:origin+direction*(n%7==0?1e-8:60000);
                    Check(query.Blocks(end)==obstacles.BlocksSegment(origin,end),"Directional candidate cache changed exact visibility");
                }
            }
            var positions=Enumerable.Range(0,48).Select(_=>center+Offset(40000)).ToArray();
            var current=new TerrainExposure(obstacles,positions);var reference=new ReferenceExposure(obstacles,positions);
            for(int n=0;n<1200;n++)
            {
                if(n==600)obstacles.Add(new("Geometry added",center,Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,9000)));
                if(n==900){obstacles.Clear();for(int i=0;i<6;i++)obstacles.Add(new("Replacement",center+Offset(12000),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,1500)));}
                var point=center+Offset(50000);
                Check(current.At(point)==reference.At(point),"Exposure fraction/cache invalidation changed");
                Check(current.At(point)==reference.At(point),"Repeated exposure sample changed");
            }
        }
        // Exact A* path, sum order, expansion count, budget failure and smoothing.
        foreach(double shift in new[]{0.0,1e12})
        {
            var center=new Vec3d(shift,-shift,shift);var obstacles=new ObstacleSet();
            for(int i=-2;i<=2;i++)obstacles.Add(new("Wall",center+new Vec3d(0,i*1800,0),Quaternion.Identity,ObstaclePart.Capsule(new(-6000,0,0),new(6000,0,0),1300)));
            var current=new TerrainNavigation(obstacles,center,new(14000,14000,14000));
            var reference=new ReferenceNavigation(obstacles,center,new(14000,14000,14000));
            var observations=Enumerable.Range(0,24).Select(i=>center+new Vec3d((i%6-3)*1500,(i/6-2)*1800,12000)).ToArray();
            foreach(int budget in new[]{1,20,200,1024,30000})
            foreach(double radius in new[]{50.0,350,850})
            {
                var a=new TerrainExposure(obstacles,observations);var b=new ReferenceExposure(obstacles,observations);
                Vec3d start=center+new Vec3d(0,0,7000),goal=center+new Vec3d(0,0,-7000);
                bool found=current.FindPath(start,goal,radius,out var path,budget,a);
                bool oldFound=reference.FindPath(start,goal,radius,out var oldPath,budget,b);
                Check(found==oldFound && path.Expanded==oldPath.Expanded && path.Length==oldPath.Length && path.Points.SequenceEqual(oldPath.Points),"Navigation path, budget or smoothing changed");
            }
        }
        Console.WriteLine($"PASS: {checks} navigation visibility/reference checks");
    }
}
