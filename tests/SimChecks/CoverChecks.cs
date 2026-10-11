using Godot;
using SpaceFleet.Sim;

static class CoverChecks
{
    public static void Run()
    {
        int checks=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
        var obstacles=new ObstacleSet();
        obstacles.Add(new("SCREEN",Vec3d.Zero,Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,3500)));
        var exposure=new TerrainExposure(obstacles,new[]{new Vec3d(0,0,-16000)});
        Check(exposure.At(new(0,0,9000))==0 && exposure.At(new(12000,0,0))==1,"Exposure uses actual occlusion");
        var nav=new TerrainNavigation(obstacles,Vec3d.Zero,new(22000,12000,22000));
        Vec3d start=new(-14000,0,0),goal=new(14000,0,0);
        Check(nav.FindPath(start,goal,100,out var ordinary),"Ordinary route exists");
        Check(nav.FindPath(start,goal,100,out var covered,exposure:exposure),"Weighted route exists");
        double Cost(TerrainPath path){Vec3d last=start;double sum=0;foreach(var p in path.Points){sum+=exposure.Cost(last,p);last=p;}return sum;}
        Console.WriteLine($"Cover cost {Cost(ordinary):F1} -> {Cost(covered):F1}; path {ordinary.Length:F1} -> {covered.Length:F1}");
        Check(Cost(covered)<Cost(ordinary),"Smoothing preserves the lower exposure route");
        Check(nav.FindPath(start,goal,100,out var repeated,exposure:exposure) && repeated.Points.SequenceEqual(covered.Points),"Weighted ties are deterministic");
        Vec3d last=start;foreach(var point in covered.Points){Check(!obstacles.BlocksSegment(last,point,100),"Covered segment remains physically clear");last=point;}
        int searches=nav.Searches;
        Check(nav.FindPath(new(0,10000,0),new(10000,10000,0),100,out _,exposure:exposure) && nav.Searches==searches,"Clear straight goals retain the zero A-star fast path");
        var w=new SimWorld();var me=w.Add(new ShipBody("ME",ShipClass.Interceptor,Faction.Blue));
        var enemy=w.Add(new ShipBody("ENEMY",ShipClass.Battleship,Faction.Red));enemy.Place(new(0,0,-15000),Quaternion.Identity);
        var unseen=TerrainExposure.Observe(w,Faction.Blue);Check(unseen.Observers==0,"Unobserved enemies have no exposure cost");
        w.Sensors.Update(w.Ships,0,force:true);Check(TerrainExposure.Observe(w,Faction.Blue).Observers==1,"Live contact contributes");
        w.Obstacles.Add(new("WALL",new(0,0,-7000),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,2000)));
        w.Sensors.Update(w.Ships,.25,force:true);var memory=w.Sensors.Memory(Faction.Blue,enemy,.25);Check(memory is not null,"Fixture loses the observed contact to terrain");
        var remembered=TerrainExposure.Observe(w,Faction.Blue);double before=remembered.At(new(9000,0,-10000));
        enemy.Place(new(1e10,0,0),Quaternion.Identity);enemy.Damage.Breakup(1,"hidden");
        Check(TerrainExposure.Observe(w,Faction.Blue).At(new(9000,0,-10000))==before,"Hidden movement and damage never update exposure");
        var wing=new Squadron("WING",Faction.Blue,SquadronRole.InterceptorWing,new[]{me});
        foreach(var mission in Enum.GetValues<InterceptorMission>())
        {wing.Mission=mission;Check(SimWorld.CoverMission(me)==(mission is InterceptorMission.Recon or InterceptorMission.Assault),"Cover cost mission scope");}
        var escort=new ShipBody("DD",ShipClass.Escort,Faction.Blue);new Squadron("DD",Faction.Blue,SquadronRole.InterceptorWing,new[]{escort});
        Check(!SimWorld.CoverMission(escort),"Capital ships never use exposure cost");
        Console.WriteLine($"PASS: {checks} observed terrain cover checks");
    }
}
