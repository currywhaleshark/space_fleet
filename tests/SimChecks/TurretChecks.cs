using Godot;
using System.Text.Json.Nodes;
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
            Require(ship.Railguns.Sum(g => g.Rounds) == ship.Definition.Railgun!.Rounds, "Turret capacities match ship magazine total");
            int perMount = cls.Kind == HullKind.Battleship ? 240 : 320;
            Require(ship.Railguns.All(g => g.Rounds == perMount), "Twin volleys retain the previous turret ammunition endurance");
            var attempt = world.FireRailguns(ship, Vector3.Right);
            Require(!attempt.Fired && attempt.Failure == FireFailure.Traversing, "Cannot snap-fire 90 degrees");
            Aim(world, ship, Vector3.Right, .5);
            Require(ship.Railguns.All(g => !g.Aligned(Vector3.Right)), "Traverse takes physical time");
            Require(ship.Railguns.All(g => Math.Abs(g.Yaw) <= Mathf.DegToRad(g.Mount!.YawRate) * .6f), "Rated slew limit");
            // 90° 선회 시간은 포탑 구동 속도에 달렸다(전함 주포는 느리다).
            double settle = 90 / ship.Railguns.Min(g => Math.Min(g.Mount!.YawRate, g.Mount.ElevationRate)) + 1;
            Aim(world, ship, Vector3.Right, settle);
            Require(ship.Railguns.All(g => g.Aligned(Vector3.Right)), "All dorsal/ventral mounts reach broadside");
            var muzzles = ship.Railguns.SelectMany(g => Enumerable.Range(0, g.BarrelCount)
                .Select(b => (Key: (g.Definition.ModuleId, b), Position: g.BarrelPosition(b)))).ToDictionary(p => p.Key, p => p.Position);
            float heatBefore = ship.Power.HeatMj;
            attempt = world.FireRailguns(ship, Vector3.Right);
            Require(attempt.Fired && attempt.Shots == count * 2, $"{cls.Kind}: broadside salvo: {attempt}");
            Require(world.Projectiles.Count == count * 2, "One round from each of the two physical barrels");
            Require(world.Projectiles.Select(p => p.Id).Distinct().Count() == count * 2, "Every salvo projectile has a unique impact ID");
            Require(Math.Abs(ship.Power.HeatMj - heatBefore - count * 2 * ship.Railgun!.Definition.ShotHeatMj) < .01f, "Heat is charged per round");
            foreach (var shot in world.Projectiles)
            {
                Require((shot.Position - muzzles[(shot.ModuleId, shot.Barrel)]).Length() < .001, "Round leaves that mount's barrel");
                Require(shot.Velocity.Normalized().Dot(Vector3.Right) > .999999f, "Round follows physical bore");
            }
            Require(!world.FireRailguns(ship, Vector3.Right).Fired, "Reload prevents repeated instant salvo");
            Require(ship.Railguns.All(g => g.LastSalvoRounds == 2 && g.ShotCount == 2 && g.Rounds == perMount - 2
                && g.ReloadRemaining == g.Definition.ReloadSeconds), "Both barrels consume ammunition together with one shared reload");
            var damaged = ship.Damage.Modules.Single(m => m.Definition.Id == "gun-1");
            damaged.Health = 0;
            float failedYaw = ship.Railgun!.Yaw;
            Aim(world, ship, Vector3.Left, settle * 2);
            Require(ship.Railgun.Yaw == failedYaw, "Destroyed turret freezes");
            attempt = world.FireRailguns(ship, Vector3.Left);
            Require(attempt.Fired && attempt.Shots == (count - 1) * 2, "Other guns traverse/reload/fire with primary destroyed");
            Require(ship.Railguns.Skip(1).All(g => g.LastSalvoRounds == 2 && g.ShotCount == 4), "Both barrels fire again after reload");
            world.ResetWeapons();
            Require(ship.Railguns.All(g => g.Yaw == 0 && g.Elevation == 0 && g.Rounds == g.Definition.Rounds && g.ShotCount == 0), "Reset every mount");
        }
        {
            var world = new SimWorld(); var bb = Add(world, ShipClass.Battleship);
            Require(SimWorld.RailLineLocal(bb, bb.Railguns[1], Vector3.Forward) == FireFailure.HullBlocked,
                "Rear dorsal gun cannot fire through forward turret/bridge");
            var below = new Vector3(0, -1, -.5f).Normalized();
            Require(SimWorld.RailLineLocal(bb, bb.Railguns[0], below) == FireFailure.Arc, "Dorsal depression stop");
            Aim(world, bb, below, 90 / bb.Railguns[2].Mount!.ElevationRate + 1);
            Require(world.FireRailguns(bb, below).Shots == 2 && bb.Railguns[2].ShotCount == 2, "Only ventral battery fires below, with both barrels");
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
            Require(!dd.Railguns[0].Ready && dd.Railguns[1].Ready && dd.Railguns[1].Rounds == dd.Railguns[1].Definition.Rounds,
                "One turret's reload and ammunition do not block another");
            var enemy = Add(world, ShipClass.Battleship, "ENEMY", Faction.Red);
            enemy.Place(new Vec3d(12000,0,-4000), Quaternion.Identity);
            var friendly = Add(world, ShipClass.Battleship, "SCREEN");
            friendly.Place(new Vec3d(6000,0,-2000), Quaternion.Identity);
            dd.Gunnery = new GunneryOrder { Doctrine = FireDoctrine.Focus, Target = enemy };
            for (int i = 0; i < 900; i++) world.Step();
            Require(dd.Railguns.Sum(g => g.ShotCount) == 2, "Friendly hull blocks every dangerous auto-fire line");
            friendly.Place(new Vec3d(-20000,0,0), Quaternion.Identity);
            for (int i = 0; i < 900; i++) world.Step();
            Require(dd.Railguns.All(g => g.ShotCount > 0), "All unobstructed guns resume when friendly clears");
        }
        {
            var world = new SimWorld(); var ic = Add(world, ShipClass.Interceptor);
            Require(ic.Railguns.Length == 1 && ic.Railgun!.Mount is null, "Interceptor retains fixed forward gun");
            Require(world.FireRailgun(ic, Vector3.Forward).Shots == 1 && ic.Railgun!.Rounds == 199, "Fixed-gun handling remains immediate and single-shot");
        }
        {
            using var stream = typeof(ShipDefinitions).Assembly.GetManifestResourceStream("SpaceFleet.data.ships.escort.json")!;
            using var reader = new StreamReader(stream);
            var json = JsonNode.Parse(reader.ReadToEnd())!;
            json["railgun"]!["rounds"] = 6;
            foreach (var mount in json["railgun"]!["mounts"]!.AsArray()) mount!["rounds"] = 3;
            var def = ShipDefinition.Parse(json.ToJsonString());
            var world = new SimWorld();
            var dd = world.Add(new ShipBody("ODD", def.Flight, Faction.Blue, def));
            var target = Add(world, ShipClass.Battleship, "TARGET", Faction.Red);
            target.Place(new Vec3d(0, 0, -4000), Quaternion.Identity);
            Require(world.FireRailgun(dd, Vector3.Forward).Shots == 2 && dd.Railgun!.Rounds == 1, "Twin volley consumes two of an odd load");
            for (int i = 0; i < 180; i++) world.Step();
            Require(world.Impacts.Count(p => p.Shooter == dd) == 2, "Both physical rounds independently collide and apply damage");
            Require(world.FireRailgun(dd, Vector3.Forward).Shots == 1 && dd.Railgun!.Rounds == 0
                && dd.Railgun.LastSalvoRounds == 1 && dd.Railgun.ShotCount == 3, "Last odd round fires once without negative ammunition or phantom flash");
            Require(!world.FireRailgun(dd, Vector3.Forward).Fired, "Exhausted twin battery cannot fire");
        }
        Console.WriteLine($"PASS: {_checks} turret checks");
    }
}
