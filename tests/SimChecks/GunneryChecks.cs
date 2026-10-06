using Godot;
using SpaceFleet.Sim;

static class GunneryChecks
{
    private static int _checks;
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); _checks++; }
    private static ShipBody Add(SimWorld world, string id, ShipClass cls, Faction faction, Vec3d at)
    {
        var ship = world.Add(new ShipBody(id, cls, faction));
        ship.Place(at, Quaternion.Identity);
        ship.Control = new ShipControl { FlightAssist = false };
        return ship;
    }
    private static void Step(SimWorld world, double seconds)
    { for (int i = 0; i < seconds * SimWorld.TickRate; i++) world.Step(); }
    public static void Run()
    {
        foreach (FireDoctrine doctrine in new[] { FireDoctrine.Focus, FireDoctrine.Hold, FireDoctrine.Manual })
        {
            var world = new SimWorld();
            var dd = Add(world, "DD", ShipClass.Escort, Faction.Blue, Vec3d.Zero);
            var enemy = Add(world, "ENEMY", ShipClass.Escort, Faction.Red, new Vec3d(0, 0, -20_000));
            dd.Gunnery = new GunneryOrder { Doctrine = doctrine, Target = enemy };
            int rounds = dd.Railgun!.Rounds;
            Step(world, 20);
            Require(doctrine == FireDoctrine.Focus ? dd.Railgun.Rounds < rounds : dd.Railgun.Rounds == rounds, $"{doctrine} firing rule");
            if (doctrine == FireDoctrine.Focus) Require(enemy.Damage.Shield < enemy.Definition.Shield.Capacity, "Focus must hit enemy");
        }
        {
            var world = new SimWorld();
            var dd = Add(world, "DD", ShipClass.Escort, Faction.Blue, Vec3d.Zero);
            var ic = Add(world, "IC", ShipClass.Interceptor, Faction.Red, new Vec3d(0, 0, -15_000));
            Add(world, "BB", ShipClass.Battleship, Faction.Red, new Vec3d(0, 0, -60_000));
            dd.Gunnery = new GunneryOrder();
            Step(world, 2);
            Require(dd.Gunnery.Engaged == ic, "Free must prefer nearby interceptor");
        }
        {
            var world = new SimWorld();
            var dd = Add(world, "DD", ShipClass.Escort, Faction.Blue, Vec3d.Zero);
            var enemy = Add(world, "ENEMY", ShipClass.Escort, Faction.Red, new Vec3d(0, 0, -10_000));
            dd.Gunnery = new GunneryOrder { Doctrine = FireDoctrine.Disable, Target = enemy, AimPart = AimSubsystem.Engines };
            Step(world, 1);
            var module = dd.Gunnery.EngagedModule;
            Require(module is not null && module.Definition.Kind == ModuleKind.Thruster, "Disable must aim at functional module");
            Step(world, 90);
            Require(module!.HealthFraction < 1, "Disable must damage selected module");
        }
        {
            var world = new SimWorld();
            var bb = Add(world, "BB", ShipClass.Battleship, Faction.Blue, Vec3d.Zero);
            var enemy = Add(world, "BELOW", ShipClass.Escort, Faction.Red, new Vec3d(0, -8000, 0));
            bb.Gunnery = new GunneryOrder { Doctrine = FireDoctrine.Focus, Target = enemy };
            int rounds = bb.Railgun!.Rounds;
            Step(world, 3);
            Require(bb.Railgun.Rounds == rounds, "Blocked battleship must not fire");
            Require(bb.Gunnery.Status == GunneryStatus.HullBlocked, $"Below target status: {bb.Gunnery.Status}");
            bb.Control = new ShipControl { Roll = 1 };
            for (int i = 0; bb.Up.Y > -0.99f && i < 1800; i++) world.Step();
            bb.Control = ShipControl.Idle;
            Step(world, 30);
            Require(bb.Railgun.Rounds < rounds, "Battleship must fire after rolling clear");
        }
        Console.WriteLine($"PASS: {_checks} gunnery checks");
    }
}
