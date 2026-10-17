using Godot;
using SpaceFleet.Sim;
using System.Reflection;

static class ObstacleWeaponChecks
{
    public static void Run()
    {
        int n=0;
        void Check(bool ok,string why) { if(!ok)throw new Exception(why); n++; }
        foreach(double offset in new[]{0.0,1e12})
        foreach(int layout in new[]{0,1,2}) // obstacle first, ship first, unobstructed side
        {
            var (w,s,t)=Fixture(offset);
            var origin=s.Position;
            w.Obstacles.Add(new("BLOCK",origin+new Vec3d(layout==2?1800:0,0,layout==1?-4500:-1500),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,200)));
            var fire=w.FireRailgun(s,Vector3.Forward);
            Check(fire.Fired,"Manual rail fire remains possible into terrain");
            var original=fire.Projectile!;
            ((List<RailProjectile>)w.Projectiles)[0]=new RailProjectile{ Id=original.Id,Shooter=s,Position=original.Position,
                PrevPosition=original.Position,Packet=original.Packet,Lifetime=original.Lifetime,Velocity=Vector3.Forward*2_000_000 };
            w.Step();
            Check(w.Impacts.Any(i=>i.Hit.Target==t)==(layout!=0),"Rail chooses earliest ship/obstacle, including high coordinates");
            Check(w.Log!.Environment.BlockedRails==(layout==0?1:0),"Only actual obstacle rail impacts count");
            if(layout==0)Check(w.OrdnanceEvents.Any(e=>e.Kind==OrdnanceEventKind.Detonation),"Terrain rail impact produces existing flash");
            foreach(int kind in new[]{0,1,2})
            {
                var (mw,ms,mt)=Fixture(offset);
                mw.Obstacles.Add(w.Obstacles[0]);
                var am=ms.Definition.Antimatter!;
                var def=kind==2?am.Flight:kind==1?ms.Definition.Missiles!:ShipDefinitions.For(HullKind.Escort).Missiles!;
                var m=new Missile { Id=1,Shooter=ms,Target=mt,Definition=def with { AccelG=0 },Assault=kind==2?am:null,
                    Weapon=kind==2?BattleWeapon.Antimatter:BattleWeapon.Missile,Position=ms.Position+new Vec3d(0,0,-100),
                    PrevPosition=ms.Position+new Vec3d(0,0,-100),Velocity=Vector3.Forward*2_000_000,LaunchDirection=Vector3.Forward,
                    Health=def.HitPoints,AimPoint=mt.Position,NextSeekerCheck=100 };
                ((List<Missile>)mw.Missiles).Add(m); mw.Step();
                Check(mw.Impacts.Any(i=>i.Hit.Target==mt)==(layout!=0),$"Weapon {kind}: obstacle/ship order and open flank");
                Check(mw.Missiles.Count==0,"Projectile terminates at first physical impact");
                if(kind==2)Check(mw.Log!.AntimatterFlights.Single().Outcome==(layout==0?AntimatterOutcome.Obstacle:AntimatterOutcome.Hit),"AM terrain outcome is distinct from hit/PD/expiry");
                if(layout==0)Check((m.Position-(ms.Position+new Vec3d(0,0,-1300))).Length()<.01,"Blocked missile stops at actual double-precision surface");
            }
        }
        var (auto,a,b)=Fixture(0);
        var rock=new Obstacle("ROCK",new(0,0,-1500),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,200)); auto.Obstacles.Add(rock);
        var observer=auto.Add(new ShipBody("LINK",ShipClass.Battleship,Faction.Blue)); observer.Place(new(5000,0,-3000),Quaternion.Identity);
        auto.Log=new BattleLog(auto); auto.Sensors.Update(auto.Ships,0,force:true);
        Check(auto.Sensors.Track(Faction.Blue,b).Level==TrackLevel.Locked,"Other observer provides lock behind terrain");
        Check(!auto.TryAutoFire(a,b,null,30,out var state)&&state==GunneryStatus.Obstructed,"Autogun reports terrain, despite valid shared lock");
        auto.AttachBrain(a,ShipOrder.AttackOn(b));
        Check(!auto.LaunchMissile(a,b).Fired,"AI missile launch holds behind terrain");
        a.Ordnance.Antimatter.BeginArming(); a.Ordnance.Antimatter.Step(3);
        Check(!auto.LaunchAntimatter(a,b).Fired,"AI AM launch holds behind terrain");
        var defense=typeof(SimWorld).GetMethod("ClearDefenseLine",BindingFlags.NonPublic|BindingFlags.Instance)!;
        Check(!(bool)defense.Invoke(auto,new object?[]{new Vec3d(0,500,-1000),new Vec3d(0,-500,-2000),null})!,"Shared PD/drone line blocks terrain");
        Check((bool)defense.Invoke(auto,new object?[]{new Vec3d(1500,0,-1000),new Vec3d(1500,0,-2000),null})!,"Shared PD/drone line permits a clear flank");
        var (pw,ps,_)=Fixture(1e12); pw.Obstacles.Add(new("PROBE WALL",ps.Position+new Vec3d(0,0,-5000),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,500)));
        Check(SimWorld.TryProbeDestination(ps,ps.Position+new Vec3d(0,0,-5000),out var goal,pw.Obstacles)
            && (goal-ps.Position).Z > -4500,"Probe goal inside obstacle clamps before front surface");
        Check(pw.LaunchProbeAt(ps,ps.Position+new Vec3d(0,0,-5000)).Fired,"Clipped probe destination can launch");
        for(int i=0;i<2400;i++)pw.Step();
        Check(pw.Probes.Count==1 && pw.Probes[0].Active && pw.Probes[0].OnStation,"Probe reaches clipped station without hitting obstacle");
        pw.Probes[0].Destination=ps.Position+new Vec3d(0,0,-10000); pw.Probes[0].Velocity=Vector3.Forward*1e6f;
        pw.Step();
        Check(pw.Probes.Count==0 && pw.Log!.Environment.BlockedProbes==1,"Swept probe collision cannot tunnel through terrain");
        foreach(bool blocked in new[]{false,true})
        {
            var dw=new SimWorld(); var guard=dw.Add(new ShipBody("GUARD",AntimatterChecks.Variant("battleship",pd:true,drones:true),Faction.Blue));
            var attacker=dw.Add(new ShipBody("ATTACKER",AntimatterChecks.Variant("interceptor"),Faction.Red));
            attacker.Place(new(0,0,-50000),Quaternion.Identity);
            dw.Obstacles.Add(new("BARRIER",new(blocked?0:15000,0,-1550),Quaternion.Identity,
                ObstaclePart.Capsule(new(-5000,0,0),new(5000,0,0),500)));
            var def=attacker.Definition.Missiles!;
            // Above the bow: the test isolates terrain, not the battleship's own deck occlusion.
            var threat=new Missile{Id=1,Shooter=attacker,Definition=def with{BurnSeconds=0},Position=new(0,300,-2100),
                PrevPosition=new(0,300,-2100),AimPoint=new(0,300,-2100),Velocity=Vector3.Zero,Health=1e6f,NextSeekerCheck=100};
            ((List<Missile>)dw.Missiles).Add(threat);
            int rounds=guard.Ordnance.Drones.RemainingRounds;
            for(int i=0;i<600;i++)dw.Step();
            Check((guard.Ordnance.PointDefense.Sum(g=>g.ShotCount)>0)==!blocked,"Actual PD chooses only terrain-clear missile targets");
            Check((guard.Ordnance.Drones.RemainingRounds<rounds)==!blocked,"Actual drone batteries respect terrain and fire on clear targets");
        }
        Console.WriteLine($"PASS: {n} obstacle weapon/probe checks");
    }
    private static (SimWorld,ShipBody,ShipBody) Fixture(double offset)
    {
        var w=new SimWorld(); var s=w.Add(new ShipBody("SHOOTER",AntimatterChecks.Variant("interceptor"),Faction.Blue));
        var t=w.Add(new ShipBody("VICTIM",AntimatterChecks.Variant("battleship"),Faction.Red));
        s.Place(new(offset,offset,offset),Quaternion.Identity); t.Place(s.Position+new Vec3d(0,0,-3000),Quaternion.Identity);
        s.Control=t.Control=new ShipControl{FlightAssist=false}; w.Log=new BattleLog(w);
        w.Sensors.Update(w.Ships,0,force:true); return(w,s,t);
    }
}
