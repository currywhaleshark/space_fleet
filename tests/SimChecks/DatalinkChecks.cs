using Godot;
using SpaceFleet.Sim;

static class DatalinkChecks
{
    private static int checks;
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); checks++; }
    private static ShipBody Add(SimWorld world, string name, HullKind kind, Faction side, Vec3d position, DesignFamily design = DesignFamily.Earth)
    {
        var ship = world.Add(new ShipBody(name, ShipDefinitions.For(kind, design), side));
        ship.Place(position, Quaternion.Identity);
        ship.DamageControl.Priority = RepairPriority.Hold;
        return ship;
    }
    private static void Blind(ShipBody ship)
    {
        foreach (var m in ship.Damage.Modules.Where(m=>m.Definition.Kind==ModuleKind.Sensor))
            ship.Damage.Hurt(m, m.Health, ship.SimTime, 0, 1);
    }
    private static void Step(SimWorld world, double seconds)
    { for (int i=0;i<(int)(seconds*60);i++) world.Step(); }
    public static void Run()
    {
        foreach (DesignFamily design in Enum.GetValues<DesignFamily>())
        foreach (HullKind kind in Enum.GetValues<HullKind>())
        {
            var world=new SimWorld();
            var shooter=Add(world,"SHOOTER",kind,Faction.Blue,Vec3d.Zero,design);
            var target=Add(world,"TARGET",HullKind.Battleship,Faction.Red,new(0,0,-10_000));
            Blind(shooter); world.Sensors.Update(world.Ships,0,true);
            Check(!FireControl.Solve(shooter,target,0,track:world.Sensors.Track(Faction.Blue,target)).Valid,
                $"{design}/{kind}: isolated blind ship has no assisted solution");
            var scout=Add(world,"SCOUT",HullKind.Escort,Faction.Blue,new(5000,0,-5000),
                design==DesignFamily.Earth?DesignFamily.Mars:DesignFamily.Earth);
            world.Sensors.Update(world.Ships,0,true);
            var track=world.Sensors.Track(Faction.Blue,target);
            var solution=FireControl.Solve(shooter,target,0,track:track);
            Check(track.Level==TrackLevel.Locked && track.Observation?.Observer==scout && solution.Valid && solution.ViaDatalink,
                $"{design}/{kind}: same team, other design shares its lock and firing quality");
            Check(solution.ErrorMeters>0 && float.IsFinite(solution.ErrorMeters),"Shared lock retains finite uncertainty");
            var damagedTrack=track with {Observation=track.Observation!.Value with {Quality=.5f}};
            Check(FireControl.Solve(shooter,target,0,track:damagedTrack).ErrorMeters>solution.ErrorMeters*1.9f,
                "Observer sensor degradation affects received accuracy");
            var jammed=track with {JamRatio=3};
            Check(Math.Abs(FireControl.Solve(shooter,target,0,track:jammed).ErrorMeters/solution.ErrorMeters-2)<.01,
                "Shared lock still carries ECM error");
            shooter.Damage.Reset(); world.Sensors.Update(world.Ships,0,true);
            Check(Math.Abs(FireControl.Solve(shooter,target,0,track:world.Sensors.Track(Faction.Blue,target)).ErrorMeters-solution.ErrorMeters)<.001,
                "Receiver sensor damage does not double-charge the same shared observation");
            Blind(shooter);
            foreach (var level in new[]{TrackLevel.None,TrackLevel.Contact,TrackLevel.Identified})
                Check(!FireControl.Solve(shooter,target,0,track:track with{Level=level}).Valid,"Below-lock information is insufficient for assisted fire");
            Check(!FireControl.Solve(shooter,target,FireControl.MaxObservationAge+.01,track:track).Valid,
                "A cached observation cannot stay a valid lock indefinitely");
            scout.Damage.Breakup(0,"link lost"); world.Sensors.Update(world.Ships,.25,true);
            Check(!FireControl.Solve(shooter,target,.25,track:world.Sensors.Track(Faction.Blue,target)).Valid,
                "Losing the only observer stops assisted fire at the next network update");
        }
        CheckSnapshot(); CheckLiveGunnery(); CheckWeaponAndPower(); CheckObserverHandover();
        Console.WriteLine($"PASS: {checks} datalink fire-control checks");
    }
    private static void CheckSnapshot()
    {
        var world=new SimWorld(); var shooter=Add(world,"B",HullKind.Battleship,Faction.Blue,Vec3d.Zero);
        var target=Add(world,"R",HullKind.Battleship,Faction.Red,new(0,0,-20000));
        var scout=Add(world,"D",HullKind.Escort,Faction.Blue,new(5000,0,-10000));
        Blind(shooter); target.Velocity=new(100,30,0); world.Sensors.Update(world.Ships,0,true);
        var track=world.Sensors.Track(Faction.Blue,target); var localAim=new Vector3(0,50,-200);
        var expected=FireControl.Solve(shooter,target,.1,false,track,localAim);
        target.Position=new(1e9,1e9,1e9); target.Velocity=new(-500,-100,200);
        target.Orientation=new Quaternion(Vector3.Up,1.2f);
        Check(FireControl.Solve(shooter,target,.1,false,track,localAim)==expected,
            "Linked solution cannot read the target's live position, velocity or orientation between observations");
        Vec3d observedAtFire=track.Observation!.Value.Position+Vec3d.From(track.Observation.Value.Velocity)*.1;
        Vec3d impact=shooter.Railgun!.MuzzlePosition+Vec3d.From(expected.Direction*shooter.Railgun.Definition.MuzzleSpeed)*expected.FlightTime;
        Vec3d predicted=observedAtFire+Vec3d.From(track.Observation.Value.Orientation*localAim)
            +Vec3d.From(track.Observation.Value.Velocity)*expected.FlightTime;
        Check((impact-predicted).Length()<.05,"Snapshot extrapolation includes observation age and projectile flight time");
        world.Sensors.Update(world.Ships,.25,true);
        Check(!FireControl.Solve(shooter,target,.25,track:world.Sensors.Track(Faction.Blue,target)).Valid,
            "Fresh scan drops a teleported/unobserved target instead of following it magically");
    }
    private static void CheckLiveGunnery()
    {
        foreach(bool ai in new[]{false,true})
        {
            var world=new SimWorld(); var shooter=Add(world,"B",HullKind.Battleship,Faction.Blue,Vec3d.Zero);
            var target=Add(world,"R",HullKind.Battleship,Faction.Red,new(0,0,-20000));
            var scout=Add(world,"D",HullKind.Escort,Faction.Blue,new(5000,0,-10000));
            Blind(shooter);
            if(ai) world.AttachBrain(shooter,ShipOrder.HoldAt(shooter.Position,target));
            else shooter.Gunnery=new GunneryOrder {Doctrine=FireDoctrine.Focus,Target=target};
            Step(world,15);
            Check(shooter.Railguns.Sum(g=>g.ShotCount)>0,$"{(ai?"AI":"player gunnery")} fires physical rounds through ally link with its own sensor at zero");
            Check(world.Impacts.Any(i=>i.Hit.Target==target) || target.Damage.Shield<target.Definition.Shield.Capacity,
                "Linked rounds can hit, not just produce a valid mathematical solution");
            Blind(scout); world.Sensors.Update(world.Ships,world.Time,true);
            uint shots=shooter.Railguns.Aggregate(0u,(n,g)=>n+g.ShotCount);
            Step(world,10);
            Check(shooter.Railguns.Aggregate(0u,(n,g)=>n+g.ShotCount)==shots,"Observer sensor loss stops new automatic volleys");
        }
    }
    private static void CheckWeaponAndPower()
    {
        var world=new SimWorld(); var s=Add(world,"B",HullKind.Battleship,Faction.Blue,Vec3d.Zero);
        var t=Add(world,"R",HullKind.Battleship,Faction.Red,new(0,0,-10000));
        Add(world,"D",HullKind.Escort,Faction.Blue,new(5000,0,-5000)); Blind(s);
        world.Sensors.Update(world.Ships,0,true);
        foreach(var m in s.Damage.Modules.Where(m=>m.Definition.Kind==ModuleKind.Gun)) s.Damage.Hurt(m,m.Health,0,0,1);
        Check(!world.TryAutoFire(s,t,null,100,out _),"Link cannot restore destroyed guns");
        foreach(var m in s.Damage.Modules.Where(m=>m.Definition.Kind is ModuleKind.Generator or ModuleKind.Reactor)) s.Damage.Hurt(m,m.Health,0,0,1);
        Check(!FireControl.Solve(s,t,0,track:world.Sensors.Track(Faction.Blue,t)).Valid,"No main generation means no powered fire control even with a shared lock");
    }
    private static void CheckObserverHandover()
    {
        var world=new SimWorld(); var s=Add(world,"B",HullKind.Battleship,Faction.Blue,Vec3d.Zero);
        var t=Add(world,"R",HullKind.Battleship,Faction.Red,new(0,0,-10000)); Blind(s);
        var a=Add(world,"D1",HullKind.Escort,Faction.Blue,new(5000,0,-8000));
        var b=Add(world,"D2",HullKind.Escort,Faction.Blue,new(-5000,0,-5000));
        world.Sensors.Update(world.Ships,0,true);
        Check(world.Sensors.Track(Faction.Blue,t).Observation?.Observer==a,"Best observer owns the shared measurement");
        Blind(a); world.Sensors.Update(world.Ships,.25,true);
        var track=world.Sensors.Track(Faction.Blue,t);
        Check(track.Observation?.Observer==b && FireControl.Solve(s,t,.25,track:track).Valid,"Another observer takes over without reviving the receiver's sensor");
    }
}
