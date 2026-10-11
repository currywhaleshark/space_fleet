using Godot;
using SpaceFleet.Sim;

static class ObstacleCollisionChecks
{
    public static void Run()
    {
        int checks=0;
        void Check(bool yes,string why) { if(!yes) throw new Exception(why); checks++; }
        foreach(double offset in new[]{0.0,1e12})
        foreach(var pose in new[]{Quaternion.Identity,new Quaternion(Vector3.Up,.73f)})
        foreach(bool capsule in new[]{false,true})
        {
            var obstacle=new Obstacle("sweep",new(offset,offset,offset),pose,capsule
                ?ObstaclePart.Capsule(new(0,-20,0),new(0,20,0),10):ObstaclePart.Sphere(Vec3d.Zero,10));
            Vec3d W(double x,double y,double z)=>obstacle.ToWorld(new(x,y,z));
            var box=new HullBox(Vector3.Zero,Vector3.One);
            Check(ShipCollision.SweepObstacleBox(obstacle,0,W(-100,0,0),W(100,0,0),pose,box,out double t,out var contact)
                && Math.Abs(t-.445)<1e-5,"Continuous sphere/capsule hit: thin obstacle between both endpoints");
            Check(contact.Normal.Dot(obstacle.Rotation.Apply(new(-1,0,0)))>.999,"Continuous contact normal faces incoming box");
            Check(ShipCollision.SweepObstacleBox(obstacle,0,W(-100,0,10.99),W(100,0,10.99),pose,box,out _,out _),"Near tangent swept box hits");
            Check(!ShipCollision.SweepObstacleBox(obstacle,0,W(-100,0,11.01),W(100,0,11.01),pose,box,out _,out _),"Near tangent swept box outside misses");
            Check(!ShipCollision.SweepObstacleBox(obstacle,0,W(-100,0,0),W(-200,0,0),pose,box,out _,out _),"Separating sweep misses");
        }
        foreach(float speed in new[]{50f,120f,760f,60000f})
        {
            var world=Fixture(new(0,0,250),Vector3.Forward*speed,100);
            for(int i=0;i<400 && world.Ships[0].LastCollision is null;i++) world.Step();
            var ship=world.Ships[0];
            Check(ship.LastCollision?.OtherCallsign=="ROCK","Physical obstacle contact is reported");
            Check(ship.Position.Z>0,"240 Hz obstacle contact prevents high-speed tunneling");
            Check(ship.Velocity.IsFinite() && ship.Velocity.Length()<=speed+1,"Infinite-mass response adds no linear energy");
            Check(world.Obstacles[0].Position==Vec3d.Zero,"Fixed obstacle cannot be pushed");
            Check(world.Log!.Environment.ShipCollisions>0 && world.Log.Environment.CollisionEnergy>0,"Collision telemetry records impulse energy");
            if(speed>=760) Check(ship.Damage.Destroyed,"Fast impact reaches existing structural breakup threshold");
        }
        var inside=Fixture(Vec3d.Zero,Vector3.Zero,500); inside.Step();
        Check(inside.Ships[0].Hull.Boxes.All(box=>!ObstacleSet.BoxContact(inside.Obstacles[0],0,inside.Ships[0].Position,
            inside.Ships[0].Orientation,box,out _)),"Initially embedded compound hull is pushed outside");
        Check(inside.Ships[0].Velocity==Vector3.Zero,"Static overlap correction creates no velocity");
        var glance=Fixture(new(95,0,250),Vector3.Forward*60,100);
        for(int i=0;i<360;i++) glance.Step();
        Check(glance.Ships[0].LastCollision is not null,"Glancing path contacts obstacle");
        Check(glance.Ships[0].Velocity.Z < -20,"Glancing response retains tangential travel");
        double[] energy=new double[2],damage=new double[2];
        for(int n=0;n<2;n++)
        {
            var world=Fixture(new(0,0,250),Vector3.Forward*(n==0?50:120),100);
            while(world.Ships[0].LastCollision is null && world.Time<8) world.Step();
            energy[n]=world.Log!.Environment.CollisionEnergy; damage[n]=world.Log.Environment.CollisionDamage;
        }
        Check(energy[1]>energy[0]*4,"Higher closing speed increases dissipated energy quadratically");
        Check(damage[1]>=damage[0],"Greater impact energy does not reduce module damage");

        var navigation=Fixture(new(0,0,4000),Vector3.Zero,500);
        var pilot=navigation.Ships[0]; Vec3d goal=new(0,0,-4000);
        navigation.AttachBrain(pilot,ShipOrder.HoldAt(goal));
        double closest=double.PositiveInfinity;
        for(int i=0;i<120*60;i++) { navigation.Step(); closest=Math.Min(closest,(pilot.Position-goal).Length()); }
        Check(closest<500 && !pilot.Damage.Destroyed,$"AI reaches beyond obstacle: closest {closest:F1}, position {pilot.Position}");
        Check(navigation.Log!.Environment.ShipCollisions==0,"AI avoidance clears obstacle without collision");
        var stuck=Fixture(new(0,0,1100),Vector3.Zero,500); var frozen=stuck.Ships[0];
        stuck.AttachBrain(frozen,ShipOrder.HoldAt(goal));
        for(int i=0;i<65*60;i++) { frozen.Place(new(0,0,1100),Quaternion.Identity); frozen.Velocity=Vector3.Zero; stuck.Step(); }
        Check(stuck.Log!.Environment.StuckDetections==1,"Thirty seconds without goal progress logs one stuck episode");
        Console.WriteLine($"PASS: {checks} obstacle collision/avoidance checks");
    }
    private static SimWorld Fixture(Vec3d position,Vector3 velocity,double radius)
    {
        var world=new SimWorld(); var ship=world.Add(new ShipBody("PILOT",ShipClass.Interceptor,Faction.Blue));
        ship.Place(position,Quaternion.Identity); ship.Velocity=velocity; ship.Control=new ShipControl { FlightAssist=false };
        world.Obstacles.Add(new("ROCK",Vec3d.Zero,Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,radius)));
        world.Log=new BattleLog(world); return world;
    }
}
