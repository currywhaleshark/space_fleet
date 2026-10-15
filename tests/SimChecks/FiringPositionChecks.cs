using Godot;
using SpaceFleet.Sim;

static class FiringPositionChecks
{
    public static void Run()
    {
        int checks=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
        SimWorld Fixture(bool scout,out ShipBody shooter,out ShipBody target,out ShipBrain brain)
        {
            var world=new SimWorld();shooter=world.Add(new ShipBody("SHOOTER",ShipClass.Escort,Faction.Blue));
            target=world.Add(new ShipBody("TARGET",ShipClass.Battleship,Faction.Red));
            shooter.Place(new(0,0,-65000),Quaternion.Identity);target.Place(new(0,0,-85000),Quaternion.Identity);
            target.Control=new(){FlightAssist=false};target.Gunnery=new(){Doctrine=FireDoctrine.Hold};
            // Observe before adding the rock, so the scout-free case becomes memory.
            world.Sensors.Update(world.Ships,0,force:true);
            world.Obstacles.Add(new("GUNSCREEN",new(0,0,-75000),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,1000)));
            if(scout)
            {var eye=world.Add(new ShipBody("SCOUT",ShipClass.Battleship,Faction.Blue));eye.Place(new(9000,0,-82000),Quaternion.Identity);eye.Control=new(){FlightAssist=false};eye.Gunnery=new(){Doctrine=FireDoctrine.Hold};}
            world.PrepareNavigation(new());world.Log=new BattleLog(world);
            brain=world.AttachBrain(shooter,ShipOrder.HoldAt(shooter.Position,target));return world;
        }
        var w=Fixture(true,out var ship,out var enemy,out var brain);Vec3d station=ship.Position;
        double firstFire=-1;int originalRounds=ship.Railguns.Sum(g=>g.Rounds);double maxDistance=0;
        for(int i=0;i<110*60;i++)
        {
            // Station retention requires a live target throughout this navigation test.
            // Lethality and irrecoverable structural wear are covered by damage checks.
            enemy.Damage.Reset();
            w.Step();maxDistance=Math.Max(maxDistance,(ship.Position-station).Length());
            if(ship.Railguns.Sum(g=>g.Rounds)<originalRounds && firstFire<0)firstFire=w.Time;
        }
        Console.WriteLine($"Sidestep first rail fire {firstFire:F2}s, attempts {w.Log!.Environment.SightAttempts}, starts {w.Log.Environment.SightStarts}, success {w.Log.Environment.SightSuccesses}, max station offset {maxDistance:F1}m");
        Check(firstFire>5 && firstFire<70,"Blocked ship moves sideways and resumes real rail fire");
        Check(w.Log.Environment.SightSuccesses>0 && w.Log.Environment.SightReopenSeconds>0,"Actual launch closes a successful sidestep metric");
        Check(maxDistance<5000 && w.Log.Environment.ShipCollisions==0,"Sidestep stays within the hold envelope without hitting the screen");
        Check(w.Log.Environment.BlockedFireSeconds>=5,"Fire waiting time is measured separately from projectile impacts");
        Check(!w.Obstacles.BlocksSegment(ship.Position,w.Sensors.Track(Faction.Blue,enemy).EstimatedPosition),"Successful firing position is retained instead of returning behind the screen");
        var missileWorld=Fixture(true,out var missileShip,out _,out _);
        int missiles=missileShip.Ordnance.Missiles;double missileTime=-1;
        for(int i=0;i<80*60;i++)
        {
            foreach(var gun in missileShip.Railguns)missileShip.Damage.Modules.First(m=>m.Definition.Id==gun.Definition.ModuleId).Health=0;
            missileWorld.Step();
            if(missileShip.Ordnance.Missiles<missiles){missileTime=missileWorld.Time;break;}
        }
        Console.WriteLine($"Missile-only sidestep first launch {missileTime:F2}s");
        Check(missileTime>5 && missileTime<70 && missileWorld.Log!.Environment.SightSuccesses==1,"Missile launch hold independently requests and completes a sidestep");
        Check(missileShip.Railguns.Sum(g=>g.Rounds)==originalRounds,"Missile-only test never uses rail fire to finish the manoeuvre");
        var lost=Fixture(false,out var memoryShip,out var hidden,out var memoryBrain);
        for(int i=0;i<25*60;i++)lost.Step();
        Check(lost.Log!.Environment.SightAttempts==0 && lost.Log.Environment.SightStarts==0,"Memory-only target cannot request a firing sidestep");
        Check(memoryShip.Railguns.Sum(g=>g.Rounds)==originalRounds,"Memory-only target never fires");
        Check(w.SightWithinOrderCheck(brain,station+new Vec3d(4000,0,0)) && !w.SightWithinOrderCheck(brain,station+new Vec3d(6000,0,0)),"Hold constraint filters candidate positions");
        brain.Order=ShipOrder.EscortOf(w.Ships[2],enemy);brain.FormationOffset=new(2000,0,0);
        Check(!w.SightWithinOrderCheck(brain,w.Ships[2].Position+new Vec3d(8000,0,0)),"Escort constraint filters distant sidesteps");
        var capture=new SimWorld();BattleSetup.Spawn(capture,new(){Objective=BattleObjective.Domination});
        var cap=capture.Ships[0];var capBrain=capture.BrainOf(cap)!;cap.Squadron!.ObjectiveOrders=true;var zone=capture.Rules!.CaptureZones[1];capBrain.Order=ShipOrder.HoldAt(zone.Position+new Vec3d(0,0,4000));
        Check(capture.SightWithinOrderCheck(capBrain,zone.Position+new Vec3d(8000,0,0)) && !capture.SightWithinOrderCheck(capBrain,zone.Position+new Vec3d(13000,0,0)),"Capture sidesteps remain inside the capture sphere");
        Console.WriteLine($"PASS: {checks} terrain firing-position checks");
    }
}
