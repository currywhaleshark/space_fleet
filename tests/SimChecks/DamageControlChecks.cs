using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Sim;

static class DamageControlChecks
{
    private static int checks;
    private static void Check(bool ok, string why) { if (!ok) throw new Exception(why); checks++; }
    private static ShipBody Ship(HullKind kind = HullKind.Battleship, DesignFamily design = DesignFamily.Earth)
    {
        var ship = new ShipBody("DC", ShipDefinitions.For(kind, design), Faction.Blue);
        ship.Place(Vec3d.Zero, Quaternion.Identity);
        return ship;
    }
    private static ModuleState Part(ShipBody ship, ModuleKind kind) => ship.Damage.Modules.First(m => m.Definition.Kind == kind);
    private static void Hit(ShipBody s, ModuleState m, float fraction = 1) => s.Damage.Hurt(m,
        m.Definition.HitPoints * fraction, s.SimTime, 0, 1);
    private static void Step(ShipBody s, double seconds, double dt = 1d / 60)
    { for (int i = 0; i < (int)Math.Round(seconds / dt); i++) s.Step(dt); }
    public static void Run()
    {
        foreach (DesignFamily design in Enum.GetValues<DesignFamily>())
        foreach (HullKind kind in Enum.GetValues<HullKind>())
        {
            var s = Ship(kind, design); var dc = s.DamageControl; var config = s.Definition.DamageControl;
            Step(s, 60);
            Check(dc.Kits == config.Kits && dc.Completed == 0, $"{design}/{kind} healthy ship consumes nothing");
            var sensor = Part(s, ModuleKind.Sensor);
            Hit(s, sensor); Step(s, config.QuietSeconds + config.RepairSeconds * 1.5 - .1);
            Check(sensor.Destroyed && dc.Progress > .9, "Destroyed component stays offline until work completes");
            Step(s, .5);
            Check(Math.Abs(sensor.HealthFraction - .35) < 1e-5 && s.Damage.SensorFraction > .02, "Sensor revives at partial capability");
            Check(dc.Kits == config.Kits - 1 && dc.Completed == 1, "Exactly one kit per completed job");
            var target = Ship(); target.Place(new Vec3d(0,0,-2000), Quaternion.Identity);
            Check(FireControl.Solve(s, target, s.SimTime, sensorError:false).Valid, "Recovered sensor permits an actual firing solution");
            Step(s, 80);
            Check(Math.Abs(sensor.HealthFraction - .35) < 1e-5 && dc.Completed == 1, "Emergency cap cannot regenerate to full health");
            s.Damage.Reset(); s.Step(1d/60);
            Check(dc.Kits == config.Kits && dc.Completed == 0 && dc.Priority == RepairPriority.Automatic, "Developer damage reset clears jobs/resources");
        }
        CheckInterruption(); CheckPriorities(); CheckResources(); CheckPower(); CheckCatastrophes(); CheckRules(); CheckTimeSteps(); CheckData();
        Console.WriteLine($"PASS: {checks} damage-control checks");
    }
    private static void CheckInterruption()
    {
        var s = Ship(); var sensor = Part(s, ModuleKind.Sensor); Hit(s, sensor);
        Step(s, 20); Check(s.DamageControl.Progress > .4, "Repair accumulates real work");
        Hit(s, sensor); s.Step(1d/60);
        Check(s.DamageControl.Progress == 0 && s.DamageControl.QuietRemaining > 4.9, "Direct hit on zero-health module resets work and quiet delay");
        for (int i = 0; i < 12; i++) { Step(s, 4); Hit(s, sensor); }
        Check(sensor.Destroyed && s.DamageControl.Kits == 8, "Sustained local fire suppresses repair without draining kits");
        Step(s, 15); double progress = s.DamageControl.Progress;
        s.Damage.AbsorbShield(1, s.SimTime);
        Hit(s, Part(s, ModuleKind.Cooling), .1f); Step(s, 1);
        Check(s.DamageControl.Progress > progress, "Shield and unrelated compartment hits do not interrupt the local crew");
        Step(s, 25); Check(!sensor.Destroyed, "Disengaging allows completed recovery");
    }
    private static void CheckPriorities()
    {
        var s = Ship(); var sensor = Part(s, ModuleKind.Sensor); var gun = Part(s, ModuleKind.Gun);
        Hit(s, sensor); Hit(s, gun); s.DamageControl.Priority = RepairPriority.Weapons; Step(s, 10);
        Check(s.DamageControl.ActiveModule == gun, "Manual weapons priority overrides automatic sensor choice");
        s.DamageControl.Priority = RepairPriority.Hold; Step(s, 60);
        Check(gun.Destroyed && sensor.Destroyed && s.DamageControl.Kits == 8, "Hold preserves kits and unfinished jobs");
        s.DamageControl.Priority = RepairPriority.Weapons; Step(s, 23);
        Check(!gun.Destroyed && sensor.Destroyed, "Returning to priority resumes prior work");
        Step(s, 30); Check(!sensor.Destroyed && s.DamageControl.Completed == 2, "Priority falls back to other damaged systems");
    }
    private static void CheckResources()
    {
        var s = Ship(HullKind.Interceptor); var sensor = Part(s, ModuleKind.Sensor);
        s.Ordnance.ConsumeMissile(); s.Ordnance.Antimatter.Jettison();
        var world = new SimWorld(); world.Add(s); world.FireRailgun(s, s.Forward);
        int missiles = s.Ordnance.Missiles, rounds = s.Railguns.Sum(g=>g.Rounds);
        for (int i=0;i<3;i++) { Hit(s,sensor); Step(s,25); }
        Check(sensor.Destroyed && s.DamageControl.Kits == 0 && s.DamageControl.Completed == 2, "Two kits cannot fund a third recovery");
        Check(s.Ordnance.Missiles == missiles && s.Railguns.Sum(g=>g.Rounds) == rounds && s.Ordnance.Antimatter.Rounds == 0
            && s.Ordnance.Antimatter.Jettisoned, "Repairs never replenish ammo or jettisoned AM");
        s = Ship(HullKind.Interceptor); s.Ordnance.Antimatter.Jettison();
        var containment = Part(s,ModuleKind.AntimatterContainment); Hit(s,containment); Step(s,90);
        Check(containment.Destroyed && s.DamageControl.Kits == 2, "Containment cannot be rebuilt in combat even after jettison");
        s = Ship(); sensor = Part(s,ModuleKind.Sensor); Hit(s,sensor,.8f); Step(s,23.5);
        Check(Math.Abs(sensor.HealthFraction - .35) < 1e-5, "Damaged but intact parts use the shorter repair time");
        s = Ship(HullKind.Interceptor); var shield = Part(s,ModuleKind.ShieldEmitter); Hit(s,shield); Step(s,20.1);
        Check(!shield.Destroyed && s.Damage.Shield < s.Damage.ShieldCapacity * .1f, "Emitter restoration does not refill the shield instantly");
    }
    private static void CheckPower()
    {
        var s = Ship();
        foreach (var m in s.Damage.Modules.Where(m=>m.Definition.Kind is ModuleKind.Reactor or ModuleKind.Generator)) Hit(s,m);
        var sensor = Part(s,ModuleKind.Sensor); Hit(s,sensor); s.DamageControl.Priority = RepairPriority.Sensors;
        s.Step(1d/60);
        Check(s.Damage.Disabled && s.DamageControl.RecoveringDisable, "Intact powerless ship gets a bounded repair window");
        Check(s.DamageControl.ActiveModule!.Definition.Kind is ModuleKind.Reactor or ModuleKind.Generator, "Emergency supply takes precedence over requested sensor work");
        Step(s,33); Check(!s.Damage.Disabled && s.Damage.GenerationFraction > 0, "Auxiliary tools restart power without main generation");
        s = Ship(); var bus = Part(s,ModuleKind.PowerBus); Hit(s,bus);
        var gun = s.Damage.Modules.First(m=>m.Definition.Kind==ModuleKind.Gun && m.Definition.Grid==bus.Definition.Grid);
        Hit(s,gun); s.DamageControl.Priority = RepairPriority.Weapons; s.Step(1d/60);
        Check(s.DamageControl.ActiveModule == bus, "An unpowered gun waits for its supply bus");
    }
    private static void CheckCatastrophes()
    {
        var s = Ship(); Hit(s, Part(s,ModuleKind.Sensor)); Step(s,15); s.Damage.Breakup(s.SimTime,"DC test"); Step(s,90);
        Check(s.Damage.Destroyed && s.DamageControl.Completed == 0 && s.Damage.Modules.All(m=>m.Destroyed), "Hull breakup is irreversible");
        s = Ship(HullKind.Interceptor); s.Ordnance.Antimatter.BeginArming(); Hit(s,Part(s,ModuleKind.AntimatterContainment),.4f); Step(s,90);
        Check(s.Damage.Destroyed && s.Ordnance.Antimatter.Mode == AntimatterMode.Failed && s.DamageControl.Completed == 0,
            "Arming containment hit still detonates and cannot be repaired");
    }
    private static void CheckRules()
    {
        var world = new SimWorld(); var s = Ship(); world.Add(s);
        var enemy = new ShipBody("RED", ShipDefinitions.For(HullKind.Battleship),Faction.Red);
        enemy.Place(new Vec3d(0,0,-100000), Quaternion.Identity); world.Add(enemy); world.Rules = new BattleRules(world);
        foreach(var m in s.Damage.Modules.Where(m=>m.Definition.Kind is ModuleKind.Reactor or ModuleKind.Generator)) Hit(s,m);
        Check(world.Rules.Evaluate(0) is null, "Recoverable disable does not immediately end a duel");
        var generator = s.Damage.Modules.First(m=>m.Definition.Kind is ModuleKind.Reactor or ModuleKind.Generator);
        for(int i=0;i<61;i++) { Hit(s,generator); for(int j=0;j<60;j++) world.Step(); }
        Check(world.Rules.Outcome is { Winner: Faction.Red } && world.Rules.Outcome.Time >= 60,
            "Continually interrupted recovery cannot delay defeat indefinitely");
    }
    private static void CheckTimeSteps()
    {
        string Run(double dt)
        {
            var s=Ship(); Hit(s,Part(s,ModuleKind.Sensor)); Step(s,40,dt);
            return $"{s.DamageControl.Kits}/{s.DamageControl.Completed}/{s.Damage.SensorFraction}";
        }
        Check(Run(1d/60)==Run(1d/240), "60/240 Hz restore the same health and expend the same resources");
    }
    private static void CheckData()
    {
        using var stream=typeof(ShipDefinitions).Assembly.GetManifestResourceStream("SpaceFleet.data.ships.interceptor.json")!;
        using var reader=new StreamReader(stream); string json=reader.ReadToEnd();
        foreach(var field in new[]{"kits","repairSeconds","restoreFraction","quietSeconds"})
        {
            var data=JsonNode.Parse(json)!; data["damageControl"]![field]=-1;
            bool rejected=false; try {ShipDefinition.Parse(data.ToJsonString());} catch(InvalidDataException) {rejected=true;}
            Check(rejected,"Reject invalid DC " + field);
        }
    }
}
