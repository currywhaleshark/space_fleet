using Godot;
using SpaceFleet.Sim;

static class BattleChecks
{
    private static int _checks;
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); _checks++; }
    private static void Step(SimWorld world, double seconds) { for (int i = 0; i < seconds * 60; i++) world.Step(); }
    public static void Run()
    {
        CheckSetup(); CheckRules(); CheckPhases(); CheckDeterminism(); CheckFullBattle();
        Console.WriteLine($"PASS: {_checks} battle/setup/log/rules checks");
    }
    private static SimWorld Battle(int seed = 0, bool mirror = false)
    { var world = new SimWorld(); BattleSetup.Spawn(world, new BattleConfig { Seed = seed, Mirror = mirror }); return world; }
    private static void CheckSetup()
    {
        var a = Battle(); var b = Battle(1); var c = Battle(1); var flipped = Battle(1, true);
        Require(a.Ships.Count == 24 && a.Squadrons.Count == 6, "Canonical battle roster");
        Require(a.Ships.Any(s => s.Callsign == "DD-31") && a.Ships.Any(s => s.Callsign == "IC-21"), "Game callsigns must be canonical");
        Require(a.Ships[0].Position != b.Ships[0].Position, "Nonzero seed must jitter placement");
        Require(b.Ships.Zip(c.Ships).All(p => p.First.Position == p.Second.Position && p.First.Orientation == p.Second.Orientation), "Seed must repeat exact poses");
        Require(b.Ships.Zip(flipped.Ships).All(p => p.First.Faction == p.Second.Faction && p.First.Callsign == p.Second.Callsign), "Mirror must preserve faction/callsign identity");
        Require(flipped.Ships[0].Position == b.Ships[12].Position && flipped.Ships[0].Orientation == b.Ships[12].Orientation, "Mirror must swap spatial poses");
        Require((b.Ships[1].Position - b.Ships[0].Position) == (a.Ships[1].Position - a.Ships[0].Position), "Squad jitter must preserve internal formation");
    }
    private static void Kill(ShipBody ship) => ship.Damage.Breakup(0, "RULE TEST");
    private static void CheckRules()
    {
        var world = Battle();
        Require(world.Rules!.Initial(Faction.Blue) == 20.5 && world.Rules.Initial(Faction.Red) == 20.5, "Starting strength must equal 20.5");
        Require(world.Rules.Evaluate(0) is null, "Healthy battle must not be over");
        Kill(world.Ships[0]);
        Require(world.Rules.Evaluate(1) is null, "Flagship alone is insufficient for defeat");
        foreach (var s in world.Ships.Where(s => s.Faction == Faction.Blue && s.Class.Kind == HullKind.Escort).Take(3)) Kill(s);
        Require(world.Rules.Evaluate(2)?.Winner == Faction.Red, "Flagship plus half strength must lose");
        Require(world.Rules.Evaluate(1500)?.Time == 2, "Outcome must stay latched");
        world = Battle();
        foreach (Faction f in Enum.GetValues<Faction>())
        {
            foreach (var s in world.Ships.Where(s => s.Faction == f && s.Class.Kind == HullKind.Battleship)) Kill(s);
            foreach (var s in world.Ships.Where(s => s.Faction == f && s.Class.Kind == HullKind.Escort).Take(3)) Kill(s);
        }
        Require(world.Rules!.Evaluate(5) is { Winner: null, Reason: "동시 전력 상실" }, "Same-tick defeat must draw");
        world = Battle();
        Require(world.Rules!.Evaluate(1499) is null && world.Rules.Evaluate(1500) is { Winner: null }, "Time limit/equal-strength draw");
        world = Battle(); Kill(world.Ships.First(s => s.Faction == Faction.Red && s.Class.Kind == HullKind.Escort));
        Require(world.Rules!.Evaluate(1500) is { Winner: null }, "Difference within 10 percentage points must draw");
        world = Battle();
        foreach (var s in world.Ships.Where(s => s.Faction == Faction.Red && s.Class.Kind == HullKind.Escort).Take(2)) Kill(s);
        Require(world.Rules!.Evaluate(1500)?.Winner == Faction.Blue, "Time limit must reward higher remaining fraction");
        world = new SimWorld();
        foreach (Faction f in Enum.GetValues<Faction>()) for (int i = 0; i < 4; i++) world.Add(new ShipBody($"{f}{i}", ShipClass.Escort, f));
        world.Rules = new BattleRules(world);
        foreach (var s in world.Ships.Where(s => s.Faction == Faction.Blue).Take(3)) Kill(s);
        Require(world.Rules.Evaluate(0)?.Winner == Faction.Red, "Exactly 25% must lose without flagship rule");
    }
    private static void CheckPhases()
    {
        foreach (var (range, weapon, expected) in new[] { (20_000, BattleWeapon.Missile, BattlePhase.Missile),
                     (50_000, BattleWeapon.Railgun, BattlePhase.Gunnery), (10_000, BattleWeapon.Railgun, BattlePhase.Brawl) })
        {
            var world = new SimWorld();
            var a = world.Add(new ShipBody("A", ShipClass.Escort, Faction.Blue));
            var b = world.Add(new ShipBody("B", ShipClass.Escort, Faction.Red));
            a.Place(Vec3d.Zero, Quaternion.Identity); b.Place(new Vec3d(0, 0, -range), Quaternion.Identity);
            world.Sensors.Update(world.Ships, 0, force: true); world.Log = new BattleLog(world);
            bool fired = weapon == BattleWeapon.Missile ? world.LaunchMissile(a, b).Fired : world.FireRailgun(a, a.Forward).Fired;
            Require(fired, "Phase fixture weapon must fire");
            Step(world, 30);
            Require(world.Log.Intervals[0].Phase == expected, $"Phase classification {expected}");
            Require(weapon == BattleWeapon.Missile ? world.Log.Side(Faction.Blue).Missiles == 1 : world.Log.Side(Faction.Blue).Rails == 1, "Direct weapon log must count once");
        }
        var scene = Battle();
        foreach (var brain in scene.Brains.Values) brain.Enabled = false;
        scene.Ships[0].Damage.AbsorbShield(10_000, 0);
        foreach (var module in scene.Ships[0].Damage.Modules.Take(2)) scene.Ships[0].Damage.Hurt(module, module.Health, 1, 0, 1);
        Step(scene, 30);
        Require(scene.Log!.Intervals[0].Phase == BattlePhase.Sniping, "Two unshielded module kills must classify as sniping");
        Require(scene.Log.Events.Count(e => e.Kind == BattleEventKind.ModuleDestroyed) >= 2, "Module events must be recorded");
    }
    private static void CheckDeterminism()
    {
        string Run() { var world = Battle(1); Step(world, 300); world.Log!.Finish(); return world.Log.Summary(); }
        Require(Run() == Run(), "Same seed must repeat BattleLog summary byte for byte");
    }
    private static void CheckFullBattle()
    {
        var world = Battle(); var activities = new HashSet<string>();
        Step(world, 0);
        while (world.Time < 1500 && world.Rules!.Outcome is null)
        {
            world.Step();
            if (world.Tick % 120 == 0) foreach (var squad in world.Squadrons) activities.Add($"{squad.Role}:{squad.Activity}");
        }
        world.Log!.Finish();
        Require(world.Log.Side(Faction.Blue).RailHit is not null || world.Log.Side(Faction.Red).RailHit is not null, "Full battle must engage");
        foreach (string expected in new[] { "EscortSquadron:측면 기동", "EscortSquadron:측면 공격", "InterceptorWing:요격", "BattleGroup:포격" })
            Require(activities.Contains(expected), $"Existing squad behaviour must appear: {expected}");
        Require(world.Rules!.Outcome is not null, "Battle must have an outcome by time limit");
        Console.WriteLine($"Canonical battle: {world.Log.Summary()}");
        Console.WriteLine($"  destroyed/disabled {world.Ships.Count(s => s.Damage.Destroyed || s.Damage.Disabled)}/24");
    }
}
