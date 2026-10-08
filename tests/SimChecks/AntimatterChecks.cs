using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Sim;

static class AntimatterChecks
{
    private static int _checks;
    private static void Check(bool ok, string why) { if (!ok) throw new Exception(why); _checks++; }
    internal static ShipDefinition Variant(string name, bool pd = false, bool critical = false, bool drones = false)
    {
        using var stream = typeof(ShipDefinitions).Assembly.GetManifestResourceStream($"SpaceFleet.data.ships.{name}.json")!;
        using var reader = new StreamReader(stream);
        var json = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
        if (!pd) json.Remove("pointDefense");
        if (!pd && !drones) json.Remove("defenseDrones");
        if (!critical) foreach (var m in json["modules"]!.AsArray()) m!["criticalChance"] = 0;
        return ShipDefinition.Parse(json.ToJsonString());
    }
    internal static (SimWorld World, ShipBody IC, ShipBody BB) Fixture(float range = 3000, bool pd = false, bool drones = false)
    {
        var world = new SimWorld();
        var icDef = Variant("interceptor"); var bbDef = Variant("battleship", pd, drones:drones);
        var ic = world.Add(new ShipBody("IC-AM", icDef.Flight, Faction.Blue, icDef));
        var bb = world.Add(new ShipBody("BB-AM", bbDef.Flight, Faction.Red, bbDef));
        ic.Place(Vec3d.Zero, Quaternion.Identity); bb.Place(new Vec3d(0,0,-range), Quaternion.Identity);
        ic.Control = bb.Control = new ShipControl { FlightAssist = false };
        world.Sensors.Update(world.Ships, 0, force: true); world.Log = new BattleLog(world);
        return (world, ic, bb);
    }
    internal static void Step(SimWorld world, double seconds)
    { for (int i = 0; i < Math.Round(seconds * 60); i++) world.Step(); }
    private static void Arm(SimWorld world, ShipBody ic)
    {
        Check(ic.Ordnance.Antimatter.BeginArming(), "Can prepare AM on intact interceptor");
        Step(world, 2.5);
        Check(ic.Ordnance.Antimatter.Ready, "AM reaches ARMED after 2.5 seconds");
    }
    internal static (bool Hit, bool Intercepted, bool Expired, double Time) Fly(SimWorld world, ShipBody bb)
    {
        double start = world.Time; bool intercepted = false, expired = false, hit = false;
        while (world.Missiles.Any(m => m.Weapon == BattleWeapon.Antimatter) && world.Time-start < 9)
        {
            world.Step();
            hit |= world.Impacts.Any(i => i.Weapon == BattleWeapon.Antimatter && i.Hit.Target == bb);
            intercepted |= world.OrdnanceEvents.Any(e => e.Weapon == BattleWeapon.Antimatter && e.Kind == OrdnanceEventKind.Intercepted);
            expired |= world.OrdnanceEvents.Any(e => e.Weapon == BattleWeapon.Antimatter && e.Kind == OrdnanceEventKind.Expired);
        }
        return (hit, intercepted, expired, world.Time-start);
    }
    public static void Run()
    {
        CheckState(); CheckFlight(); CheckMovingLaunch(); CheckDamage(); CheckContainment(); CheckCountermeasures(); CheckAI();
        Console.WriteLine($"PASS: {_checks} antimatter checks");
    }
    private static void CheckState()
    {
        var (w, ic, bb) = Fixture(); var am = ic.Ordnance.Antimatter;
        Check(am.Rounds == 2 && ic.Ordnance.Missiles == 4, "Independent 2 AM + 4 normal missiles");
        Check(!w.LaunchAntimatter(ic, bb).Fired, "SAFE cannot fire");
        Check(am.BeginArming(), "Start arming"); Step(w, 2);
        Check(am.Mode == AntimatterMode.Arming && !w.LaunchAntimatter(ic,bb).Fired, "No early launch");
        Check(am.Cancel() && am.Rounds == 2 && am.Mode == AntimatterMode.Safe, "Cancel preserves ammo");
        Arm(w, ic);
        Check(w.LaunchMissile(ic,bb).Fired && am.Ready && am.Rounds == 2, "Normal missile independent of ARMED AM");
        Check(w.LaunchAntimatter(ic,bb).Fired && am.Rounds == 1 && ic.Ordnance.Missiles == 3, "Launch consumes only AM");
        Check(am.Mode == AntimatterMode.Safe && !w.LaunchAntimatter(ic,bb).Fired, "Every AM shot requires re-arming");
        Arm(w,ic); Check(w.LaunchAntimatter(ic,bb).Fired && am.Rounds == 0, "Second shot exhausts AM");
        Check(!am.BeginArming(), "Empty cannot arm");
        w.ResetWeapons(); ic.Damage.Reset();
        Check(am.Rounds == 0, "Repair/practice reset cannot restock spent AM");
        Check(!w.LaunchAntimatter(bb,ic).Fired, "Capital ships cannot launch AM");
        var fresh = Fixture(); Arm(fresh.World,fresh.IC);
        foreach (var generator in fresh.IC.Damage.Modules.Where(m=>m.Definition.Kind is ModuleKind.Reactor or ModuleKind.Generator))
            fresh.IC.Damage.Hurt(generator,10000,fresh.World.Time,0,1);
        Step(fresh.World,.1);
        Check(fresh.IC.Ordnance.Antimatter.Mode == AntimatterMode.Safe, "Loss of weapon power aborts preparation safely");
    }
    private static void CheckFlight()
    {
        foreach (float range in new[] { 2000, 5000, 7800, 10000, 15000 })
        {
            var (w,ic,bb) = Fixture(range); Arm(w,ic);
            Check(w.LaunchAntimatter(ic,bb).Fired, "Long launch allowed but must waste payload");
            var flight = w.Missiles.Single(); var result = Fly(w,bb);
            Check(range <= 7800 ? result.Hit : !result.Hit && result.Expired, $"AM range {range}: {result}");
            Check(result.Time <= 7.02 && flight.TravelMeters <= 8000.01, "Hard life/distance limits");
            Console.WriteLine($"AM range {range/1000:0.0} km: hit={result.Hit}, expired={result.Expired}, flight={result.Time:0.00}s, path={flight.TravelMeters:0}m");
        }
        {
            var (w,ic,bb) = Fixture(12000); Arm(w,ic); ic.Velocity = Vector3.Forward * 760;
            Check(w.LaunchAntimatter(ic,bb).Fired, "Boosted launcher fixture");
            var result = Fly(w,bb); Check(!result.Hit && result.Expired, "Carrier boost cannot turn AM into long-range missile");
        }
        {
            var (w,ic,bb) = Fixture(); Arm(w,ic);
            ic.Orientation = new Quaternion(Vector3.Up, Mathf.Pi);
            Check(!w.LaunchAntimatter(ic,bb).Fired && ic.Ordnance.Antimatter.Rounds == 2, "Off-axis launch cannot snap around");
        }
    }
    private static void CheckMovingLaunch()
    {
        // Real flight against narrow moving hulls, not just the interception formula.
        foreach(string kind in new[]{"battleship","escort"})
        foreach(float lateral in new[]{0,100,200,300})
        {
            var w=new SimWorld(); var icDef=Variant("interceptor"); var targetDef=Variant(kind);
            var ic=w.Add(new ShipBody("IC-MOTION",icDef.Flight,Faction.Blue,icDef));
            var target=w.Add(new ShipBody("TARGET-MOTION",targetDef.Flight,Faction.Red,targetDef));
            target.Place(new Vec3d(0,0,-4500),Quaternion.Identity);
            ic.Control=target.Control=new ShipControl { FlightAssist=false };
            w.Log=new BattleLog(w); Arm(w,ic);
            ic.Velocity=Vector3.Right*lateral+Vector3.Forward*200;
            target.Velocity=Vector3.Right*100;
            w.Sensors.Update(w.Ships,w.Time,force:true);
            Vector3 aim=SimWorld.AntimatterLaunchDirection(ic,target,w.Sensors.Track(ic.Faction,target),null,out _);
            ic.Orientation=Basis.LookingAt(aim,Vector3.Up).GetRotationQuaternion();
            Check(w.LaunchAntimatter(ic,target).Fired,"Moving launch accepts a correctly aimed carrier");
            var missile=w.Missiles.Single(); var result=Fly(w,target);
            Check(result.Hit,$"Inherited lateral velocity must not amplify into a miss: {kind}, lateral={lateral}");
            Check(w.Log.AntimatterFlights is { Count:1 } && w.Log.AntimatterFlights[0].Outcome==AntimatterOutcome.Hit,
                "Flight outcome recorded exactly once, separately from launch");
            Check(missile.TravelMeters<=8000.01 && missile.Age<=7.02,"Moving hit respects unchanged range/lifetime");
        }
    }
    private static void CheckDamage()
    {
        var (w,ic,bb) = Fixture(); Arm(w,ic); float shield = bb.Damage.Shield;
        w.LaunchAntimatter(ic,bb); Fly(w,bb);
        Check(!bb.Damage.Destroyed && bb.Damage.Modules.All(m=>m.HealthFraction==1), "Full BB shield prevents arbitrary one-shot kills");
        Check(shield-bb.Damage.Shield > 900, "AM puts heavy load on shield");
        (w,ic,bb) = Fixture();
        var engine = bb.Damage.Modules.First(m=>m.Definition.Kind==ModuleKind.Thruster);
        Vector3 point = engine.Definition.Center;
        ic.Place(bb.Position + Vec3d.From(point) + new Vec3d(0,0,3000), Quaternion.Identity);
        Arm(w,ic); bb.Damage.AbsorbShield(bb.Damage.Shield,w.Time);
        w.Sensors.Update(w.Ships,w.Time,force:true);
        Check(w.LaunchAntimatter(ic,bb,point).Fired, "Weak-part launch");
        var weak = Fly(w,bb);
        Check(weak.Hit && engine.Destroyed, $"Unshielded engine must be destroyed by local AM strike: hp={engine.Health}");
        Check(bb.Damage.Modules.Count(m=>m.Destroyed) < bb.Damage.Modules.Count/2, "Localized strike preserves distant compartments");
        Check(w.Log!.Side(Faction.Blue).Torpedoes==1 && w.Log.Side(Faction.Blue).TorpedoHits==1
            && w.Log.Side(Faction.Blue).Missiles==0, "AM has independent combat statistics");
    }
    private static void CheckContainment()
    {
        foreach (bool armed in new[] { false,true })
        {
            var (w,ic,_) = Fixture(); var am = ic.Ordnance.Antimatter;
            if (armed) am.BeginArming();
            var module = ic.Damage.Module("am-containment");
            ic.Damage.Hurt(module, 16, w.Time, 20, 1);
            Check(armed ? ic.Damage.Destroyed && am.Mode==AntimatterMode.Failed : !ic.Damage.Destroyed && am.Warning,
                "Same direct damage is survivable SAFE but catastrophic during ARMING");
            if (armed) Check(!am.Jettison() && !am.Cancel() && !am.BeginArming(), "Failure cannot be reversed by jettison/cancel/arm");
            else
            {
                Check(!am.BeginArming(), "Damaged containment cannot be armed unsafely");
                Check(am.Jettison() && am.Rounds==0, "Emergency jettison while damaged SAFE");
                ic.Damage.Hurt(module,100,w.Time,100,2);
                Check(!ic.Damage.Destroyed, "Empty destroyed containment cannot cook off");
                ic.Damage.Reset(); w.ResetWeapons(); Check(am.Rounds==0 && am.Jettisoned, "Repair cannot undo jettison");
            }
        }
        {
            var (w,ic,_) = Fixture(); Arm(w,ic); var am=ic.Ordnance.Antimatter;
            ic.Damage.Hurt(ic.Damage.Modules.First(m=>m.Definition.Kind==ModuleKind.Sensor), 100, w.Time,100,1);
            Check(!ic.Damage.Destroyed && am.Failures==0, "Unrelated module hit never randomly detonates AM");
            ic.Damage.AbsorbShield(ic.Damage.Shield,w.Time);
            var module=ic.Damage.Module("am-containment");
            Vector3 point=module.Definition.Center;
            var hit=DamageRay.Apply(ic,Vec3d.From(point+Vector3.Right*30),Vector3.Left,new DamagePacket(100,100,40),w.Time,2);
            Check(hit.Modules.Any(m=>m.Id==module.Definition.Id) && am.Mode==AntimatterMode.Failed, "Actual penetrating ray triggers dedicated containment failure");
        }
        {
            var (w,ic,_) = Fixture(); ic.Damage.Breakup(w.Time,"test");
            Check(!ic.Ordnance.Antimatter.BeginArming() && !ic.Ordnance.Antimatter.Jettison(), "Destroyed ship cannot operate payload");
            Step(w,.1); Check(ic.Ordnance.Antimatter.Rounds==0, "Destruction retires stored rounds");
        }
    }
    private static void CheckCountermeasures()
    {
        var (w,ic,bb) = Fixture(4000,true); Arm(w,ic);
        ic.Place(bb.Position + new Vec3d(0,4000,0), Basis.LookingAt(Vector3.Down,Vector3.Forward).GetRotationQuaternion());
        w.Sensors.Update(w.Ships,w.Time,force:true);
        w.LaunchAntimatter(ic,bb); var missile=w.Missiles.Single();
        missile.Health=.01f;
        var result=Fly(w,bb);
        Check(result.Intercepted && !result.Hit && bb.Damage.Modules.All(m=>m.HealthFraction==1), "PD interception is harmless to target, not AM detonation");
        (w,ic,bb)=Fixture(); Arm(w,ic); w.LaunchAntimatter(ic,bb); missile=w.Missiles.Single();
        w.LaunchDecoys(bb);
        Step(w,.6); Check(missile.OnDecoy, "AM seeker can actually acquire a deployed decoy");
        Check(missile.Assault!.TerminalAccelG < missile.Definition.AccelG, "Lateral terminal authority is limited");
        int LockedTicks(bool jam)
        {
            var (world,shooter,victim)=Fixture(5000); Arm(world,shooter);
            if(jam) victim.Power.SetPips(1,1,1,1,4);
            world.LaunchAntimatter(shooter,victim); var round=world.Missiles.Single(); int locked=0;
            for(int i=0;i<200;i++) { world.Step(); if(round.SeekerLocked) locked++; }
            return locked;
        }
        int unjammed=LockedTicks(false),jammed=LockedTicks(true);
        Check(unjammed>jammed, $"ECM shortens terminal tracking window: {unjammed} vs {jammed}");
        {
            (w,ic,bb)=Fixture(4000,drones:true); Arm(w,ic); w.LaunchAntimatter(ic,bb); missile=w.Missiles.Single();
            var drones=bb.Ordnance.Drones;
            // Place a fragile test round inside one live drone's firing envelope, behind the BB's deck PD.
            missile.Position=bb.Position+Vec3d.From(drones.LocalPosition(0,w.Time)+Vector3.Up*100);
            missile.PrevPosition=missile.Position; missile.Velocity=Vector3.Up*10; missile.Health=.01f;
            var droneResult=Fly(w,bb);
            Check(drones.Rounds.Sum()<bb.Definition.DefenseDrones!.Count*bb.Definition.DefenseDrones.RoundsPerDrone,
                "Defense drones consume their own ammunition");
            Check(droneResult.Intercepted && !droneResult.Hit, "Defense drones participate in physical interception");
        }
    }
    private static void CheckAI()
    {
        var (w,ic,bb)=Fixture(5000);
        var brain=w.AttachBrain(ic,ShipOrder.AttackOn(bb));
        for(int i=0;i<1200 && ic.Ordnance.Antimatter.Launches==0;i++) w.Step();
        Check(ic.Ordnance.Antimatter.Launches>0 && (ic.Position-bb.Position).Length()<=5000,
            "AI infiltrates and launches AM only at close range");
        Check(brain.BreakUntil>w.Time,"AI immediately begins escape after AM launch");
        Step(w,1);
        Check(brain.Activity.Contains("이탈"),"AI executes escape after launching");
        (w,ic,bb)=Fixture(5000);
        w.AttachBrain(ic,ShipOrder.AttackOn(bb));
        ic.Damage.Hurt(ic.Damage.Module("am-containment"),8,w.Time,10,1);
        Step(w,.5); Check(ic.Ordnance.Antimatter.Jettisoned && !ic.Damage.Destroyed,"Damaged AI discards dangerous payload");
        (w,ic,bb)=Fixture(5000);
        ic.Place(bb.Position+new Vec3d(3000,-4000,0),Quaternion.Identity);
        w.Sensors.Update(w.Ships,w.Time,force:true);
        var defender=w.AttachBrain(bb,ShipOrder.HoldAt(bb.Position));
        float initial=bb.Up.Dot((ic.Position-bb.Position).ToVector3().Normalized());
        Step(w,12);
        Check(bb.Up.Dot((ic.Position-bb.Position).ToVector3().Normalized())>initial+.3f,"BB rolls dorsal defenses toward identified AM raider");
        (w,ic,bb)=Fixture(15000);
        var dd=w.Add(new ShipBody("ESCORT",ShipClass.Escort,Faction.Red));
        dd.Place(bb.Position,Quaternion.Identity);
        var other=w.Add(new ShipBody("OTHER",ShipClass.Escort,Faction.Blue)); other.Place(bb.Position+new Vec3d(3000,0,0),Quaternion.Identity);
        w.Sensors.Update(w.Ships,w.Time,force:true);
        Check(w.PickGunneryTarget(dd)==ic,"Escort prioritizes identified AM raider over closer escort");
    }
}
