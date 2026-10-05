using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Sim;

static class BallisticsChecks
{
    private static int _checks;
    public static void Run()
    {
        CheckIntercept();
        CheckFireControl();
        CheckFlightAndInheritance();
        CheckMovingHits();
        CheckNearestAndExpiry();
        CheckWeaponState();
        CheckData();
        Console.WriteLine($"PASS: {_checks} ballistics/fire-control checks");
    }
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        _checks++;
    }
    private static bool Near(double a, double b, double epsilon = 0.001) => Math.Abs(a - b) < epsilon;
    private static JsonObject Data()
    {
        using var stream = typeof(ShipDefinitions).Assembly.GetManifestResourceStream("SpaceFleet.data.ships.interceptor.json")!;
        using var reader = new StreamReader(stream);
        var data = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
        data["shield"] = JsonNode.Parse("""{"capacity":0,"rechargePerSecond":0,"rechargeDelay":1}""");
        data["hullSections"] = JsonNode.Parse("""
            [{"id":"hull","name":"시험 선체","center":[0,0,0],"halfSize":[10,10,10],"armor":{"thicknessMm":0}}]
            """);
        data["modules"] = JsonNode.Parse("""
            [{"id":"sensor","name":"센서","kind":"Sensor","center":[0,0,0],"halfSize":[1,1,1],"hitPoints":1000,"resistanceMm":0},
             {"id":"gun","name":"주포","kind":"Gun","center":[5,0,0],"halfSize":[1,1,1],"hitPoints":1000,"resistanceMm":0},
             {"id":"magazine","name":"탄약고","kind":"Magazine","center":[-5,0,0],"halfSize":[1,1,1],"hitPoints":1000,"resistanceMm":0}]
            """);
        data["railgun"] = JsonNode.Parse("""
            {"moduleId":"gun","muzzle":[0,0,-11],"muzzleSpeed":1000,"reloadSeconds":0.5,
             "energy":500,"penetrationMm":500,"moduleDamage":100,"maxRange":5000,"rounds":3,"traverseDegrees":180,
             "sensorErrorMeters":1,"sensorErrorPerKm":0.2,"velocityError":0.3}
            """);
        return data;
    }
    private static ShipDefinition Definition(Action<JsonObject>? change = null)
    {
        JsonObject data = Data(); change?.Invoke(data); return ShipDefinition.Parse(data.ToJsonString());
    }
    private static ShipBody Add(SimWorld world, string id, Vec3d position, Vector3? velocity = null,
        ShipDefinition? definition = null, Quaternion? rotation = null)
    {
        definition ??= Definition();
        var ship = world.Add(new ShipBody(id, definition.Flight, Faction.Blue, definition));
        ship.Place(position, rotation ?? Quaternion.Identity);
        ship.Velocity = velocity ?? Vector3.Zero;
        ship.Control = new ShipControl { FlightAssist = false };
        return ship;
    }
    private static void Step(SimWorld world, int count)
    {
        for (int i = 0; i < count; i++) world.Step();
    }
    private static void Destroy(ShipBody ship, string id)
    {
        ModuleState module = ship.Damage.Module(id);
        ship.Damage.Hurt(module, module.Health, 1, 0, 1);
    }

    private static void CheckIntercept()
    {
        foreach (var (relative, velocity) in new[]
        {
            (new Vec3d(0,0,-1000), Vec3d.Zero),
            (new Vec3d(0,0,-1000), new Vec3d(200,0,0)),
            (new Vec3d(0,0,-1000), new Vec3d(0,0,500)),
            (new Vec3d(0,0,-1000), new Vec3d(0,0,1000)),
            (new Vec3d(0,0,-1000), new Vec3d(0,0,2000)),
            (new Vec3d(300,-500,-900), new Vec3d(-200,100,500)),
        })
        {
            bool valid = FireControl.Intercept(relative, velocity, 1000, 10, out double t);
            Require(valid && t > 0, "A reachable interception must have a positive solution");
            Require(Near((relative + velocity * t).Length(), 1000 * t, 1e-6), "Intercept time must satisfy equal projectile and target travel distance");
        }
        Require(!FireControl.Intercept(new(0,0,-1000), new(0,0,-1000), 1000, 10, out _), "Equal-speed receding target must be unreachable");
        Require(!FireControl.Intercept(new(0,0,-1000), new(0,0,-2000), 1000, 10, out _), "Faster receding target must be unreachable");
        Require(!FireControl.Intercept(new(0,0,-1000), new(1200,0,0), 1000, 10, out _), "Excessive transverse target speed must be unreachable");
        Require(!FireControl.Intercept(new(0,0,-1000), Vec3d.Zero, 1000, 0.5, out _), "A solution beyond projectile lifetime must be rejected");
        Require(!FireControl.Intercept(Vec3d.Zero, Vec3d.Zero, 1000, 10, out _), "Coincident muzzle and target must not create an undefined direction");
        Require(!FireControl.Intercept(new(1,0,0), Vec3d.Zero, double.NaN, 10, out _), "Nonfinite input must be rejected");
    }

    private static void CheckFireControl()
    {
        var world = new SimWorld();
        ShipBody shooter = Add(world, "S", Vec3d.Zero), target = Add(world, "T", new(0,0,-3000), new(150,0,0));
        FiringSolution solution = FireControl.Solve(shooter, target, 10, sensorError: false);
        Require(solution.Valid && solution.Direction.X > 0.1f && solution.FlightTime > 3, "Lead must point ahead of a transverse target");
        Vec3d bullet = shooter.Railgun!.MuzzlePosition + Vec3d.From(shooter.Velocity + solution.Direction * 1000) * solution.FlightTime;
        Require((bullet - (target.Position + Vec3d.From(target.Velocity) * solution.FlightTime)).Length() < 0.001, "An exact solution must meet the target in world space");
        Vector3 boost = new(600,100,-300);
        shooter.Velocity += boost; target.Velocity += boost;
        FiringSolution boosted = FireControl.Solve(shooter, target, 10, false);
        Require(boosted.Direction.DistanceTo(solution.Direction) < 1e-6 && Near(boosted.FlightTime, solution.FlightTime), "A shared velocity boost must preserve the firing solution");
        FiringSolution observed = FireControl.Solve(shooter, target, 10);
        FiringSolution repeated = FireControl.Solve(shooter, target, 10);
        Require(observed == repeated && observed.ErrorMeters > 0, "Sensor error must be nonzero and repeatable");
        FiringSolution next = FireControl.Solve(shooter, target, 10.001);
        Require(next.Direction.DistanceTo(observed.Direction) < 0.0001, "Sensor observation noise must change smoothly");
        ModuleState sensor = shooter.Damage.Module("sensor");
        shooter.Damage.Hurt(sensor, sensor.Health * 0.5f, 1, 0, 1);
        FiringSolution damaged = FireControl.Solve(shooter, target, 10);
        Require(damaged.ErrorMeters > observed.ErrorMeters * 1.9f, "Sensor damage must degrade the solution uncertainty");
        Destroy(shooter, "sensor");
        Require(!FireControl.Solve(shooter, target, 10).Valid, "A destroyed sensor must disable fire-control solutions");
        shooter.Damage.Reset(); target.Place(new(0,0,-6000), Quaternion.Identity);
        Require(!FireControl.Solve(shooter, target, 10).Valid, "A target beyond gun range must be rejected");
    }

    private static void CheckFlightAndInheritance()
    {
        var world = new SimWorld();
        ShipBody shooter = Add(world, "S", Vec3d.Zero, new(200,10,-50));
        ShipBody target = Add(world, "T", new(0,0,-1000), shooter.Velocity);
        FireAttempt shot = world.FireRailgun(shooter, Vector3.Forward);
        Require(shot.Fired && world.Impacts.Count == 0, "Firing must spawn a projectile without instant damage");
        Require(shot.Projectile!.Velocity == shooter.Velocity + Vector3.Forward * 1000, "Projectile must inherit the launch ship velocity");
        Require(shot.Projectile.Position == shooter.Railgun!.MuzzlePosition, "Projectile must begin at the authored muzzle");
        Step(world, 30);
        Require(world.Projectiles.Count == 1 && world.Impacts.Count == 0, "A distant target must wait for projectile travel time");
        Require(Near(world.Projectiles[0].Position.X, 100) && Near(world.Projectiles[0].Age, 0.5), "Projectile must travel ballistically in world space");
        Step(world, 40);
        Require(world.Impacts.Count == 1 && world.Impacts[0].Hit.Target == target, "An equal-velocity target must be hit after the delay");
        Require(Near(world.Impacts[0].Time, 0.979, 0.002), "Impact time must account for muzzle position and target surface");
        Require(target.Damage.Module("sensor").Health < 1000 && world.Projectiles.Count == 0, "Impact must deliver existing module damage and consume the projectile");
    }

    private static void CheckMovingHits()
    {
        foreach (Vec3d origin in new[] { Vec3d.Zero, new Vec3d(1e6,-2e6,3e6), new Vec3d(1e12,-2e12,3e12) })
        {
            var world = new SimWorld();
            ShipBody shooter = Add(world, "S", origin), target = Add(world, "T", origin + new Vec3d(0,0,-3000), new(180,0,0));
            FiringSolution solution = FireControl.Solve(shooter, target, 0, false);
            Require(world.FireRailgun(shooter, solution.Direction).Fired, "Assisted aim must permit a shot");
            Step(world, 210);
            Require(world.Impacts.Any(i => i.Hit.Target == target), "Lead must hit a moving target at all tested coordinate scales");
            Require(target.Damage.Module("sensor").Health < 1000, "Moving-target hit must follow the internal module path");
            var manualWorld = new SimWorld();
            ShipBody manual = Add(manualWorld, "S", origin);
            Add(manualWorld, "T", origin + new Vec3d(0,0,-3000), new(180,0,0));
            manualWorld.FireRailgun(manual, Vector3.Forward);
            Step(manualWorld, 210);
            Require(manualWorld.Impacts.Count == 0, "A manual shot at the current position must miss a transverse target");
        }
        var fast = new SimWorld();
        ShipBody gun = Add(fast, "S", Vec3d.Zero, definition: Definition(d => d["railgun"]!["muzzleSpeed"] = 100000));
        ShipBody thin = Add(fast, "T", new(0,0,-170));
        fast.FireRailgun(gun, Vector3.Forward); fast.Step();
        Require(fast.Impacts.Any(i => i.Hit.Target == thin), "A projectile crossing an entire hull within one substep must not tunnel");
        var crossing = new SimWorld();
        ShipBody crossGun = Add(crossing, "S", Vec3d.Zero, definition: Definition(d => d["railgun"]!["muzzleSpeed"] = 100000));
        ShipBody crossTarget = Add(crossing, "T", new(-30,0,-211), new(15000,0,0));
        crossing.FireRailgun(crossGun, Vector3.Forward); crossing.Step();
        Require(crossing.Impacts.Any(i => i.Hit.Target == crossTarget), "A hull crossing the projectile segment must be tested in relative motion");
        var rotated = new SimWorld();
        Quaternion rotation = Quaternion.FromEuler(new(0.1f,0.4f,0.3f));
        ShipBody rotatedGun = Add(rotated, "S", Vec3d.Zero, rotation: rotation);
        ShipBody rotatedTarget = Add(rotated, "T", Vec3d.From(rotation * Vector3.Forward * 1000), rotation: rotation);
        rotated.FireRailgun(rotatedGun, rotatedGun.Forward); Step(rotated, 90);
        Require(rotated.Impacts.Any(i => i.Hit.Target == rotatedTarget), "Projectile intersection must respect hull orientation");
    }

    private static void CheckNearestAndExpiry()
    {
        var world = new SimWorld();
        ShipBody shooter = Add(world, "S", Vec3d.Zero);
        ShipBody far = Add(world, "FAR", new(0,0,-500)), near = Add(world, "NEAR", new(0,0,-150));
        world.FireRailgun(shooter, Vector3.Forward); Step(world, 60);
        Require(world.Impacts.Count == 1 && world.Impacts[0].Hit.Target == near, "Nearest physical hull must block a projectile regardless of insertion order");
        Require(Near(far.Damage.Module("sensor").Health, 1000), "The projectile must not damage a hull behind the first one");
        var empty = new SimWorld();
        ShipBody gun = Add(empty, "S", Vec3d.Zero);
        empty.FireRailgun(gun, Vector3.Forward); Step(empty, 301);
        Require(empty.Projectiles.Count == 0 && empty.Impacts.Count == 0, "Missed rounds must expire at maximum flight time");
        Step(world, 240);
        Require(world.Impacts.Count == 0, "Old impact effects must be pruned");
        var interpolation = new SimWorld();
        ShipBody interpGun = Add(interpolation, "S", Vec3d.Zero);
        RailProjectile p = interpolation.FireRailgun(interpGun, Vector3.Forward).Projectile!;
        Vec3d initial = p.Position; interpolation.Step();
        Require(p.PrevPosition == initial && Near((p.Position - initial).Length(), 1000 / 60.0, 0.01), "Projectile interpolation must span a full 60Hz tick");
    }

    private static void CheckWeaponState()
    {
        var world = new SimWorld();
        ShipBody shooter = Add(world, "S", Vec3d.Zero);
        int initialRounds = shooter.Railgun!.Rounds;
        Require(world.FireRailgun(shooter, Vector3.Forward).Fired && shooter.Railgun.Rounds == initialRounds - 1, "Successful fire must consume one round");
        Require(!world.FireRailgun(shooter, Vector3.Forward).Fired && shooter.Railgun.Rounds == initialRounds - 1, "Reloading must reject fire without consuming ammunition");
        Step(world, 31);
        Require(world.FireRailgun(shooter, Vector3.Forward).Fired, "Gun must fire again after its reload interval");
        Step(world, 31); world.FireRailgun(shooter, Vector3.Forward); Step(world, 31);
        Require(!world.FireRailgun(shooter, Vector3.Forward).Fired && shooter.Railgun.Rounds == 0, "Empty ammunition must prevent fire");
        world.ResetWeapons();
        Require(shooter.Railgun.Rounds == initialRounds && shooter.Railgun.Ready && world.Projectiles.Count == 0, "Weapon reset must clear projectiles and restore rounds/readiness");
        Require(!world.FireRailgun(shooter, Vector3.Back).Fired && shooter.Railgun.Rounds == initialRounds, "A muzzle pointed through its own hull must reject fire");
        Require(!world.FireRailgun(shooter, new(float.NaN,0,0)).Fired, "Invalid firing direction must be rejected");
        Destroy(shooter, "gun");
        Require(!world.FireRailgun(shooter, Vector3.Forward).Fired, "Destroyed mounted gun must prevent fire");
        shooter.Damage.Reset(); Destroy(shooter, "magazine");
        Require(!world.FireRailgun(shooter, Vector3.Forward).Fired, "Destroyed magazine must prevent fire");
        var damagedWorld = new SimWorld();
        ShipBody damaged = Add(damagedWorld, "S", Vec3d.Zero);
        damaged.Damage.Hurt(damaged.Damage.Module("gun"), 500, 1, 0, 1);
        damagedWorld.FireRailgun(damaged, Vector3.Forward); Step(damagedWorld, 31);
        Require(!damaged.Railgun!.Ready, "Gun damage must slow reloading");
        Step(damagedWorld, 30);
        Require(damaged.Railgun.Ready, "A partially damaged gun must eventually reload");
        var arcWorld = new SimWorld();
        ShipBody limited = Add(arcWorld, "S", Vec3d.Zero, definition: Definition(d => d["railgun"]!["traverseDegrees"] = 12));
        Require(!arcWorld.FireRailgun(limited, Vector3.Right).Fired && limited.Railgun!.Rounds == 3, "Gun traverse limit must prevent sideways firing");
        var powerWorld = new SimWorld();
        ShipBody powered = Add(powerWorld, "PROD", Vec3d.Zero, definition: ShipDefinitions.For(HullKind.Interceptor));
        Destroy(powered, "bus-port"); Destroy(powered, "bus-starboard");
        Require(!powerWorld.FireRailgun(powered, powered.Forward).Fired, "Power failure must prevent fire");
        var manualWorld = new SimWorld();
        ShipBody blind = Add(manualWorld, "BLIND", Vec3d.Zero);
        Destroy(blind, "sensor");
        Require(manualWorld.FireRailgun(blind, Vector3.Forward).Fired, "A sensor failure must still permit manual firing");
        var selectedWorld = new SimWorld();
        ShipBody bb = Add(selectedWorld, "BB", Vec3d.Zero, definition: ShipDefinitions.For(HullKind.Battleship));
        Destroy(bb, "gun-1");
        Require(bb.Damage.WeaponsFraction > 0 && !selectedWorld.FireRailgun(bb, bb.Forward).Fired, "Other surviving guns must not mask loss of the mounted gun");
    }

    private static void CheckData()
    {
        foreach (Action<JsonObject> change in new Action<JsonObject>[]
        {
            d => d["railgun"]!["moduleId"] = "sensor",
            d => d["railgun"]!["muzzleSpeed"] = 0,
            d => d["railgun"]!["rounds"] = -1,
            d => d["railgun"]!["traverseDegrees"] = 181,
            d => d["railgun"]!["reloadSeconds"] = 0,
            d => d["railgun"]!["sensorErrorPerKm"] = -1,
        })
        {
            bool rejected = false;
            try { Definition(change); } catch (Exception e) when (e is InvalidDataException or JsonException or ArgumentException) { rejected = true; }
            Require(rejected, "Invalid railgun data must be rejected");
        }
        foreach (HullKind kind in Enum.GetValues<HullKind>())
            Require(ShipDefinitions.For(kind).Railgun is not null, "All playable classes must define a railgun");
    }
}
