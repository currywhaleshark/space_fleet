using Godot;
using SpaceFleet.Sim;

static class PlayerInterceptChecks
{
    private static int checks;
    private static void Check(bool ok, string message)
    { if (!ok) throw new InvalidOperationException(message); checks++; }
    private static ShipBody Add(SimWorld world, string id, Faction side, Vec3d at, string name)
    {
        var def = ShipDefinitions.ById(id);
        var ship = world.Add(new ShipBody(name, def.Flight, side, def));
        ship.Place(at, Quaternion.Identity); return ship;
    }
    private static void Step(SimWorld world, double seconds)
    { for (int i = 0; i < Math.Ceiling(seconds * 60); i++) world.Step(); }
    public static void Run()
    {
        foreach (string id in new[] { "interceptor", "mars_interceptor" })
        foreach (double offset in new[] { 0d, 1e12 })
        {
            var world = new SimWorld();
            var ship = Add(world, id, Faction.Blue, new(offset, offset, offset), "PLAYER");
            var enemy = Add(world, "interceptor", Faction.Red, ship.Position + new Vec3d(0, 0, -8000), "ENEMY");
            world.Sensors.Update(world.Ships, 0, true);
            Check(world.LaunchMissile(enemy, ship).Fired, "Fuze fixture launches");
            var missile = world.Missiles.Single();
            var muzzle = ship.Railgun!.MuzzlePosition;
            var observed = new SmallContact(missile, SmallContactKind.Missile, TrackLevel.Locked,
                muzzle + new Vec3d(100, 0, -10000), Vector3.Back * ship.Railgun.Definition.MuzzleSpeed, world.Time);
            Check(world.SmallTargetAim(ship, observed, out var direction, out var aim, playerAssist: true)
                && direction.IsFinite() && (aim - muzzle).Length() < 10000, "Equal-speed closing trajectory has a finite intercept");
            Check(world.SmallTargetAim(ship, observed, out _, out _), "AI and player use the equipped weapon range");
            Check(world.SmallTargetAim(ship, observed with { Acceleration = new(70, 0, 300) }, out var accelDirection, out _, playerAssist: true)
                && !direction.IsEqualApprox(accelDirection), "Observed acceleration adjusts high-speed lead without iteration oscillation");
            Check(!world.SmallTargetAim(ship, observed with { Position = muzzle + new Vec3d(0, 0, -1500000) }, out _, out _, playerAssist: true),
                "Player assist still has a bounded engagement range");
            Check(!world.FireMissileIntercept(ship, observed with { SeenAt = -1 }).Fired, "Stale track cannot program a fuze or spend ammunition");
        }
        foreach (string id in new[] { "interceptor", "mars_interceptor" })
        foreach (double offset in new[] { 0d, 1e12 })
        foreach (var scenario in new[] { "near", "far", "manual", "armour", "early", "expired" })
        {
            var world = new SimWorld();
            var ship = Add(world, id, Faction.Blue, new(offset, offset, offset), "PLAYER");
            var enemy = Add(world, "interceptor", Faction.Red, ship.Position + new Vec3d(0, 0, -8000), "ENEMY");
            world.Sensors.Update(world.Ships, 0, true); world.LaunchMissile(enemy, ship);
            var missile = world.Missiles.Single();
            enemy.Ordnance.Reset(); world.LaunchMissile(enemy, ship);
            var adjacent = world.Missiles.Last();
            Vec3d muzzle = ship.Railgun!.MuzzlePosition;
            double z = scenario == "early" ? -50 : -2000;
            missile.Position = muzzle + new Vec3d(scenario == "far" ? 50 : 20, 0, z);
            adjacent.Position = muzzle + new Vec3d(-20, 0, z);
            foreach (var m in world.Missiles) { m.Age = m.Definition.BurnSeconds + 1; m.Velocity = Vector3.Zero; }
            if (scenario == "armour") missile.Health = 12;
            float health = missile.Health;
            var observed = new SmallContact(missile, SmallContactKind.Missile, TrackLevel.Locked,
                muzzle + new Vec3d(0, 0, z), Vector3.Zero, world.Time);
            int ammo = ship.Railgun.Rounds;
            var shot = scenario == "manual" ? world.FireRailgun(ship, Vector3.Forward) : world.FireMissileIntercept(ship, observed);
            Check(shot.Fired && ship.Railgun.Rounds == ammo - 1, "One actual round and its normal reload are consumed");
            if (scenario == "expired") missile.Position = muzzle + new Vec3d(20, 0, -26000);
            Step(world, scenario == "expired" ? 4.6 : .6);
            bool hits = scenario is "near" or "armour";
            Check(Math.Abs(missile.Health - (hits ? Math.Max(0, health - 6) : health)) < .01, $"{id}/{offset}/{scenario}: bounded six-point fragment damage");
            Check(adjacent.Health == adjacent.Definition.HitPoints, "Adjacent unselected missile has no enlarged collision volume or splash damage");
            Check(ship.Ordnance.Interceptor.MissileKills == (scenario == "near" ? 1 : 0), "Credit only occurs on an actual destruction");
            Check(ship.Ordnance.Missiles == 4 && ship.Ordnance.Antimatter.Rounds == 3, "Other weapon stores are independent");
            Check(ship.Railgun.ReloadRemaining >= 0, "Normal weapon reload remains valid");
        }
        Console.WriteLine($"PASS: {checks} player missile lead/proximity checks");
    }
}
