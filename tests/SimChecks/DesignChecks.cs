using Godot;
using SpaceFleet.Sim;

static class DesignChecks
{
    public static void Run()
    {
        int count=0;
        void Check(bool ok,string message) { if(!ok)throw new Exception(message); count++; }
        Check(ShipDefinitions.All.Count==6,"Six independently identified hulls");
        foreach(var kind in Enum.GetValues<HullKind>())
        {
            var earth=ShipDefinitions.For(kind); var mars=ShipDefinitions.For(kind,DesignFamily.Mars);
            Check(earth.Design==DesignFamily.Earth && earth.Id==kind.ToString().ToLowerInvariant(),"Legacy JSON retains Earth identity");
            Check(ReferenceEquals(ShipDefinitions.ById(mars.Id),mars),"Stable ID catalog lookup");
            foreach(var team in Enum.GetValues<Faction>())
            {
                var ship=new ShipBody("DESIGN",mars,team);
                Check(ship.Faction==team && ship.Definition.Design==DesignFamily.Mars,"Team and design independent");
                Check(ReferenceEquals(new ShipBody("CLASS",mars.Flight,team).Definition,mars),"Flight compatibility resolves the correct design");
                Check(ship.Class.ForwardAccel>earth.Flight.ForwardAccel && ship.Class.PitchYawRateDeg>earth.Flight.PitchYawRateDeg,"Mars maneuver advantage");
                Check(ship.Damage.ShieldCapacity<earth.Shield.Capacity,"Mars shield tradeoff");
                Check(ship.Definition.PointDefense!.Mounts.Length<earth.PointDefense!.Mounts.Length,"Mars fewer defensive mounts");
                Check(ship.Definition.HullSections.First(s=>s.Id=="hull").HalfSize.X<earth.HullSections.First(s=>s.Id=="hull").HalfSize.X,"Slender collision hull");
                foreach(var m in ship.Definition.Modules)
                    Check(ship.Definition.HullSections.Any(s=>
                        Math.Abs(m.Center.X-s.Center.X)+m.HalfSize.X<=s.HalfSize.X+.001 &&
                        Math.Abs(m.Center.Y-s.Center.Y)+m.HalfSize.Y<=s.HalfSize.Y+.001 &&
                        Math.Abs(m.Center.Z-s.Center.Z)+m.HalfSize.Z<=s.HalfSize.Z+.001),"Every module contained in collision geometry");
            }
        }
        var eb=ShipDefinitions.For(HullKind.Battleship); var mb=ShipDefinitions.For(HullKind.Battleship,DesignFamily.Mars);
        var eg=eb.Railgun!; var mg=mb.Railgun!;
        Check(mg.Mounts!.Length<eg.Mounts!.Length && mg.Energy>eg.Energy && mg.PenetrationMm>eg.PenetrationMm && mg.MuzzleSpeed>eg.MuzzleSpeed,"Mars precision battery tradeoffs");
        Check(mg.Energy*mg.Mounts.Length/mg.ReloadSeconds<eg.Energy*eg.Mounts.Length/eg.ReloadSeconds,"Earth higher sustained battery energy");
        Check(mb.Modules.Count(m=>m.Kind==ModuleKind.Reactor)<eb.Modules.Count(m=>m.Kind==ModuleKind.Reactor),"Mars reactor redundancy lower");
        foreach(var family in Enum.GetValues<DesignFamily>())
        {
            var ic=new ShipBody("AM",ShipDefinitions.For(HullKind.Interceptor,family),Faction.Blue);
            Check(ic.Ordnance.Missiles==4 && ic.Ordnance.Antimatter.Rounds==2,"Both interceptors keep independent missile/AM stores");
            ic.Ordnance.Antimatter.BeginArming(); ic.Step(2.6);
            Check(ic.Ordnance.Antimatter.Mode==AntimatterMode.Armed,"Existing arming works on both hulls");
            ic.Ordnance.Antimatter.Cancel();
            Check(ic.Ordnance.Antimatter.Mode==AntimatterMode.Safe,"Arming cancellation remains available");
            ic.Ordnance.Antimatter.Jettison();
            Check(ic.Ordnance.Antimatter.Rounds==0 && ic.Ordnance.Missiles==4,"Jettison does not discard ordinary missiles");
        }
        var world=new SimWorld();
        var overrides=new Dictionary<string,string> { ["DD-11"]="mars_escort",["IC-21"]="mars_interceptor",["DD-X1"]="escort",["IC-X1"]="interceptor" };
        BattleSetup.Spawn(world,new BattleConfig { BlueDesign=DesignFamily.Earth,RedDesign=DesignFamily.Mars,HullOverrides=overrides });
        Check(world.Ships.Count==24 && world.Ships.Select(s=>s.Definition.Id).Distinct().Count()==6,"All six hulls coexist in a 24-ship battle");
        Check(world.Ships.Single(s=>s.Callsign=="DD-11").Faction==Faction.Blue,"Mars is not synonymous with Red");
        for(int i=0;i<120;i++)world.Step();
        Check(world.Ships.All(s=>double.IsFinite(s.Position.LengthSquared()) && s.Orientation.IsFinite()),"Mixed fleet physics remains finite");
        // ECM lives in the existing team sensor net: allied observer sees its contact, opposing team does not inherit it.
        var netWorld=new SimWorld();
        var observer=netWorld.Add(new ShipBody("OBSERVER",ShipClass.Battleship,Faction.Blue));
        var victim=netWorld.Add(new ShipBody("VICTIM",ShipClass.Battleship,Faction.Red));
        var screen=netWorld.Add(new ShipBody("SCREEN",ShipDefinitions.For(HullKind.Escort,DesignFamily.Mars),Faction.Red));
        victim.Place(new Vec3d(0,0,-50000),Quaternion.Identity);screen.Place(victim.Position+new Vec3d(4000,0,0),Quaternion.Identity);
        netWorld.Sensors.Update(netWorld.Ships,0,true); var unscreened=netWorld.Sensors.Track(Faction.Blue,victim);
        screen.Power.SetPips(2,1,1,2,2); screen.Step(.1);
        netWorld.Sensors.Update(netWorld.Ships,.1,true); var screened=netWorld.Sensors.Track(Faction.Blue,victim);
        Check(screened.JamRatio>unscreened.JamRatio && screened.TrackSnr<unscreened.TrackSnr,"Mars escort ECM screens a nearby Earth teammate");
        Check(netWorld.Sensors.Track(Faction.Red,victim).Level==TrackLevel.Locked,"Own team retains own contact data");
        screen.Place(new Vec3d(100000,0,0),Quaternion.Identity);netWorld.Sensors.Update(netWorld.Ships,.2,true);
        Check(netWorld.Sensors.Track(Faction.Blue,victim).JamRatio==unscreened.JamRatio,"ECM support falls off outside its radius");
        // No lock, no precision module. With a lock, blueprint choice must not depend on hidden module health.
        var aiWorld=new SimWorld();var shooter=aiWorld.Add(new ShipBody("MARS",mb,Faction.Blue));
        var hidden=aiWorld.Add(new ShipBody("HIDDEN",eb,Faction.Red));hidden.Place(new Vec3d(0,0,-1e9),Quaternion.Identity);
        var brain=aiWorld.AttachBrain(shooter,ShipOrder.AttackOn(hidden));
        for(int i=0;i<30;i++)aiWorld.Step();
        Check(brain.AimModule is null && shooter.Railguns.All(g=>g.ShotCount==0),"Unknown contact never yields a precision target or a shot");
        hidden.Place(new Vec3d(0,0,-20000),Quaternion.Identity);aiWorld.Sensors.Update(aiWorld.Ships,aiWorld.Time,true);
        for(int i=0;i<30;i++)aiWorld.Step();
        string? selected=brain.AimModule?.Definition.Id;
        Check(selected is not null,"Locked Mars battery selects a blueprint module");
        var selectedModule=hidden.Damage.Modules.First(m=>m.Definition.Id==selected);
        hidden.Damage.Hurt(selectedModule,selectedModule.Health,aiWorld.Time,0,1);
        for(int i=0;i<30;i++)aiWorld.Step();
        Check(brain.AimModule?.Definition.Id==selected,"Precision target does not read hidden enemy module destruction");
        var assaultWorld=new SimWorld();
        var raider=assaultWorld.Add(new ShipBody("PHOBOS",ShipDefinitions.For(HullKind.Interceptor,DesignFamily.Mars),Faction.Blue));
        var capital=assaultWorld.Add(new ShipBody("ASSAULT-TARGET",eb,Faction.Red));
        var sector=new Vector3(0,-.5f,1).Normalized();
        raider.Place(Vec3d.From(sector*5000),Basis.LookingAt(-sector).GetRotationQuaternion());
        assaultWorld.AttachBrain(raider,ShipOrder.AttackOn(capital));
        for(int i=0;i<1200 && raider.Ordnance.Antimatter.Launches==0;i++)assaultWorld.Step();
        Check(raider.Ordnance.Antimatter.Launches>0,"Mars AI can complete rear-sector arming and launch against a capital");
        Check(assaultWorld.BrainOf(raider)!.BreakUntil>assaultWorld.Time,"Mars assault uses the shared launch-and-escape behavior");
        Console.WriteLine($"PASS: {count} design/catalog/Mars/ECM checks");
    }
}
