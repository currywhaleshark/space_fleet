using Godot;
using SpaceFleet.Sim;

static class TurretChecks
{
    private static int _checks;
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); _checks++; }
    private static ShipBody Add(SimWorld world, ShipClass cls, string id = "TEST", Faction faction = Faction.Blue)
    {
        var ship = world.Add(new ShipBody(id, cls, faction));
        ship.Place(Vec3d.Zero, Quaternion.Identity);
        ship.Control = new ShipControl { FlightAssist = false };
        return ship;
    }
    private static void Aim(SimWorld world, ShipBody ship, Vector3 direction, double seconds)
    {
        for (int tick = 0; tick < seconds * SimWorld.TickRate; tick++)
        { foreach (var gun in ship.Railguns) gun.Aim(direction); world.Step(); }
    }
    public static void Run()
    {
        foreach (var cls in new[] { ShipClass.Battleship, ShipClass.Escort })
        {
            var world = new SimWorld(); var ship = Add(world, cls);
            int count = cls.Kind == HullKind.Battleship ? 3 : 2;
            Require(ship.Railguns.Length == count, $"{cls.Kind}: mount count");
            Require(ship.Railguns.Sum(g => g.Rounds) == ship.Definition.Railgun!.Rounds, "Preserve ship magazine total");
            var attempt = world.FireRailguns(ship, Vector3.Right);
            Require(!attempt.Fired && attempt.Failure == FireFailure.Traversing, "Cannot snap-fire 90 degrees");
            Aim(world, ship, Vector3.Right, .5);
            Require(ship.Railguns.All(g => !g.Aligned(Vector3.Right)), "Traverse takes physical time");
            Require(ship.Railguns.All(g => Math.Abs(g.Yaw) <= Mathf.DegToRad(g.Mount!.YawRate) * .6f), "Rated slew limit");
            Aim(world, ship, Vector3.Right, 5);
            Require(ship.Railguns.All(g => g.Aligned(Vector3.Right)), "All dorsal/ventral mounts reach broadside");
            var muzzles = ship.Railguns.ToDictionary(g => g.Definition.ModuleId, g => g.MuzzlePosition);
            attempt = world.FireRailguns(ship, Vector3.Right);
            Require(attempt.Fired && attempt.Shots == count, $"{cls.Kind}: broadside salvo: {attempt}");
            Require(world.Projectiles.Count == count, "One round from each ready mount");
            foreach (var shot in world.Projectiles)
            {
                Require((shot.Position - muzzles[shot.ModuleId]).Length() < .001, "Round leaves that mount's barrel");
                Require(shot.Velocity.Normalized().Dot(Vector3.Right) > .999999f, "Round follows physical bore");
            }
            Require(!world.FireRailguns(ship, Vector3.Right).Fired, "Reload prevents repeated instant salvo");
            Require(ship.Railguns.All(g => g.Barrel == 1 && g.ShotCount == 1), "Alternate paired barrels after firing");
            var damaged = ship.Damage.Modules.Single(m => m.Definition.Id == "gun-1");
            damaged.Health = 0;
            float failedYaw = ship.Railgun!.Yaw;
            Aim(world, ship, Vector3.Left, 16);
            Require(ship.Railgun.Yaw == failedYaw, "Destroyed turret freezes");
            attempt = world.FireRailguns(ship, Vector3.Left);
            Require(attempt.Fired && attempt.Shots == count - 1, "Other guns traverse/reload/fire with primary destroyed");
            Require(ship.Railguns.Skip(1).All(g => g.LastBarrel == 1), "Second shot uses other barrel");
            world.ResetWeapons();
            Require(ship.Railguns.All(g => g.Yaw == 0 && g.Elevation == 0 && g.Rounds == g.Definition.Rounds && g.ShotCount == 0), "Reset every mount");
        }
        {
            var world = new SimWorld(); var bb = Add(world, ShipClass.Battleship);
            Require(SimWorld.RailLineLocal(bb, bb.Railguns[1], Vector3.Forward) == FireFailure.HullBlocked,
                "Rear dorsal gun cannot fire through forward turret/bridge");
            var below = new Vector3(0, -1, -.5f).Normalized();
            Require(SimWorld.RailLineLocal(bb, bb.Railguns[0], below) == FireFailure.Arc, "Dorsal depression stop");
            Aim(world, bb, below, 6);
            Require(world.FireRailguns(bb, below).Shots == 1 && bb.Railguns[2].ShotCount == 1, "Only ventral battery fires below");
            var oldYaw = bb.Railguns[2].Yaw;
            for (int i = 0; i < 120; i++) world.Step();
            Require(bb.Railguns[2].Yaw == oldYaw, "Expired aim holds last bearing");
        }
        {
            var world = new SimWorld(); var dd = Add(world, ShipClass.Escort);
            var enemy = Add(world, ShipClass.Escort, "ENEMY", Faction.Red);
            enemy.Place(new Vec3d(12000,0,-4000), Quaternion.Identity);
            enemy.Velocity = new Vector3(0,0,15);
            dd.Gunnery = new GunneryOrder { Doctrine = FireDoctrine.Focus, Target = enemy };
            for (int i = 0; i < 1800; i++) world.Step();
            Require(dd.Railguns.All(g => g.ShotCount > 0), "Auto gunner solves lead independently for all guns");
            Require(enemy.Damage.Shield < enemy.Definition.Shield.Capacity, "Moving broadside target actually hit");
            uint shots = (uint)dd.Railguns.Sum(g => g.ShotCount);
            enemy.Place(new Vec3d(0,0,-2_000_000), Quaternion.Identity);
            for (int i = 0; i < 1800; i++) world.Step();
            Require(dd.Railguns.Sum(g => g.ShotCount) == shots, "Lost sensor lock prevents all auto firing");
        }
        {
            var world = new SimWorld(); var dd = Add(world, ShipClass.Escort);
            Require(world.FireRailgun(dd, dd.Railguns[0], Vector3.Forward).Fired, "Single mount fires independently");
            Require(!dd.Railguns[0].Ready && dd.Railguns[1].Ready && dd.Railguns[1].Rounds == 80,
                "One turret's reload and ammunition do not block another");
            var enemy = Add(world, ShipClass.Battleship, "ENEMY", Faction.Red);
            enemy.Place(new Vec3d(12000,0,-4000), Quaternion.Identity);
            var friendly = Add(world, ShipClass.Battleship, "SCREEN");
            friendly.Place(new Vec3d(6000,0,-2000), Quaternion.Identity);
            dd.Gunnery = new GunneryOrder { Doctrine = FireDoctrine.Focus, Target = enemy };
            for (int i = 0; i < 900; i++) world.Step();
            Require(dd.Railguns.Sum(g => g.ShotCount) == 1, "Friendly hull blocks every dangerous auto-fire line");
            friendly.Place(new Vec3d(-20000,0,0), Quaternion.Identity);
            for (int i = 0; i < 900; i++) world.Step();
            Require(dd.Railguns.All(g => g.ShotCount > 0), "All unobstructed guns resume when friendly clears");
        }
        {
            var world = new SimWorld(); var ic = Add(world, ShipClass.Interceptor);
            Require(ic.Railguns.Length == 1 && ic.Railgun!.Mount is null, "Interceptor retains fixed forward gun");
            Require(world.FireRailgun(ic, Vector3.Forward).Fired, "Fixed-gun handling remains immediate");
        }
        Console.WriteLine($"PASS: {_checks} turret checks");
    }
}
