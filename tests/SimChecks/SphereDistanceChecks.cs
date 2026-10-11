using SpaceFleet.Sim;

static class SphereDistanceChecks
{
    public static void Run()
    {
        var random=new Random(9701);int checks=0;
        Vec3d Point(double scale)=>new((random.NextDouble()-.5)*scale,(random.NextDouble()-.5)*scale,(random.NextDouble()-.5)*scale);
        for(int i=0;i<10000;i++)
        {
            Vec3d point=Point(i%3==0?1e12:5000),half=new(450,100,600);
            double expected=OriginalObstacleMath.SegmentBoxDistance(point,point,half,out var a,out var b);
            double actual=ObstacleMath.SegmentBoxDistance(point,point,half,out var x,out var y);
            if(actual!=expected || x!=a || y!=b)throw new Exception("Sphere/box exact result changed");checks++;
            Vec3d start=Point(6000),end=Point(6000),center=Point(6000);
            bool old=OriginalObstacleMath.SegmentCapsule(start,end,center,center,1300,out double t1);
            bool current=ObstacleMath.SegmentCapsule(start,end,center,center,1300,out double t2);
            if(old!=current || old && t1!=t2)throw new Exception("Sphere sweep exact first hit changed");checks++;
        }
        Console.WriteLine($"PASS: {checks} sphere fast-path reference checks");
    }
}
