using Godot;
using SpaceFleet.Sim;

static class CollisionChecks
{
    private static int _checks;

    public static int Run()
    {
        CheckHeadOnAndTunneling();
        CheckMassDifference();
        CheckGlancingContact();
        CheckOverlapAndRestingContact();
        CheckRotatedHull();
        CheckFlyby();
        CheckMultipleContacts();
        CheckLargeCoordinates();
        CheckWorldInterpolationAndFlight();
        Console.WriteLine($"PASS: {_checks} collision/world checks");
        return _checks;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
        _checks++;
    }

    private static ShipBody Add(SimWorld world, string callsign, Vec3d position, Vector3 velocity,
        ShipClass? shipClass = null, Quaternion? orientation = null)
    {
        var body = world.Add(new ShipBody(callsign, shipClass ?? ShipClass.Interceptor, Faction.Blue));
        body.Place(position, orientation ?? Quaternion.Identity);
        body.Velocity = velocity;
        body.Control = new ShipControl { FlightAssist = false };
        return body;
    }

    private static void Step(SimWorld world, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
            world.Step();
        foreach (ShipBody ship in world.Ships)
            Require(double.IsFinite(ship.Position.X) && double.IsFinite(ship.Position.Y) && double.IsFinite(ship.Position.Z)
                && ship.Velocity.IsFinite(), "Collision response must remain finite");
    }

    private static void CheckHeadOnAndTunneling()
    {
        foreach (float speed in new[] { 100f, 760f, 10000f })
        {
            var world = new SimWorld();
            ShipBody a = Add(world, "A", new(0, 0, -100), Vector3.Back * speed);
            ShipBody b = Add(world, "B", new(0, 0, 100), Vector3.Forward * speed);
            Step(world, speed < 1000 ? 180 : 1);
            Require(a.Position.Z < b.Position.Z, "Head-on ships must not pass through each other");
            Require(!ShipCollision.Overlaps(a, b), "Head-on ships must separate");
            Require(a.LastCollision is not null && b.LastCollision is not null, "Impact must be reported for both ships");
            Require((a.Velocity + b.Velocity).Length() < 0.01f, "Equal-mass impact must conserve momentum");
            Require(a.Velocity.Length() <= speed * 0.06f && b.Velocity.Length() <= speed * 0.06f,
                "Inelastic collision must not gain kinetic energy");
        }
    }

    private static void CheckMassDifference()
    {
        var world = new SimWorld();
        ShipBody battleship = Add(world, "BB", Vec3d.Zero, Vector3.Zero, ShipClass.Battleship);
        ShipBody interceptor = Add(world, "IC", new(0, 0, 800), Vector3.Forward * 760f);
        Step(world, 60);
        Require(interceptor.LastCollision?.OtherCallsign == "BB", "Small ship must hit the battleship aft hull");
        Require(battleship.Velocity.Length() < 0.02f, "Interceptor must not shove a battleship at its own speed");
        Require(interceptor.Velocity.Z >= 0f && !ShipCollision.Overlaps(interceptor, battleship), "Interceptor must stop or rebound outside hull");
        double originalMomentum = interceptor.Class.MassKg * -760.0;
        double momentum = interceptor.Class.MassKg * interceptor.Velocity.Z + battleship.Class.MassKg * battleship.Velocity.Z;
        Require(Math.Abs(momentum - originalMomentum) / Math.Abs(originalMomentum) < 0.0001,
            "Different-mass collision must conserve momentum");
    }

    private static void CheckGlancingContact()
    {
        var world = new SimWorld();
        ShipBody battleship = Add(world, "BB", Vec3d.Zero, Vector3.Zero, ShipClass.Battleship);
        ShipBody interceptor = Add(world, "IC", new(160, 20, 0), new(-200, 0, -300));
        Step(world, 45);
        Require(interceptor.LastCollision is not null, "Glancing approach must make contact");
        Require(!ShipCollision.Overlaps(interceptor, battleship), "Glancing ship must stay outside the hull");
        Require(interceptor.Velocity.Z < -250f, "Glancing collision must preserve most tangential motion");
        Require(interceptor.Velocity.Length() < 361f, "Friction must not add energy");
    }

    private static void CheckOverlapAndRestingContact()
    {
        var world = new SimWorld();
        ShipBody a = Add(world, "A", Vec3d.Zero, Vector3.Zero);
        ShipBody b = Add(world, "B", Vec3d.Zero, Vector3.Zero);
        Step(world, 1);
        Require(!ShipCollision.Overlaps(a, b), "Initially overlapping ships must separate");
        Require(a.Velocity == Vector3.Zero && b.Velocity == Vector3.Zero, "Overlap correction must not invent velocity");

        world = new SimWorld();
        ShipBody embeddedHull = Add(world, "BB", Vec3d.Zero, Vector3.Zero, ShipClass.Battleship);
        ShipBody embeddedShip = Add(world, "IC", Vec3d.Zero, Vector3.Zero);
        Step(world, 1);
        Require(!ShipCollision.Overlaps(embeddedHull, embeddedShip), "A ship spawned fully inside a compound hull must escape its parts");

        world = new SimWorld();
        ShipBody battleship = Add(world, "BB", Vec3d.Zero, Vector3.Zero, ShipClass.Battleship);
        ShipBody interceptor = Add(world, "IC", new(0, 0, 598.02), Vector3.Forward);
        interceptor.Control = new ShipControl { Thrust = new Vector3(0, 0, 1), FlightAssist = true, HelmForward = Vector3.Forward };
        float peak = 0;
        for (int tick = 0; tick < 1200; tick++)
        {
            world.Step();
            peak = Math.Max(peak, interceptor.Velocity.Length());
            if (ShipCollision.Overlaps(interceptor, battleship))
                throw new InvalidOperationException("Continuously thrusting at a surface must not penetrate");
        }
        Require(peak < 3f, "Resting contact must not build up bounce velocity");
    }

    private static void CheckRotatedHull()
    {
        var world = new SimWorld();
        Quaternion orientation = new Quaternion(Vector3.Up, 0.65f) * new Quaternion(Vector3.Forward, 0.9f);
        ShipBody battleship = Add(world, "BB", Vec3d.Zero, Vector3.Zero, ShipClass.Battleship, orientation);
        Vector3 localStart = new(180, 20, 330);
        ShipBody interceptor = Add(world, "IC", Vec3d.From(orientation * localStart), orientation * Vector3.Down * 150f,
            orientation: orientation);
        Step(world, 20);
        Require(interceptor.LastCollision is not null, "Rotated radiator must participate in collision");
        Require(!ShipCollision.Overlaps(interceptor, battleship), "Rotated parts must separate along their own axes");

        world = new SimWorld();
        battleship = Add(world, "BB", Vec3d.Zero, Vector3.Zero, ShipClass.Battleship);
        battleship.Control = new ShipControl { Roll = 1, FlightAssist = false };
        interceptor = Add(world, "IC", new(-170, 18, 330), Vector3.Zero);
        Step(world, 180);
        Require(interceptor.LastCollision is not null && interceptor.Velocity.Length() > 1f,
            "A rotating hull must transfer surface motion to nearby ships");
        Require(!ShipCollision.Overlaps(interceptor, battleship), "Rotating contact must remain separated");
    }

    private static void CheckFlyby()
    {
        var world = new SimWorld();
        ShipBody battleship = Add(world, "BB", Vec3d.Zero, Vector3.Zero, ShipClass.Battleship);
        ShipBody interceptor = Add(world, "IC", new(110, 20, 800), Vector3.Forward * 760);
        Step(world, 120);
        Require(interceptor.LastCollision is null, "A close flyby outside the actual parts must not hit a giant bounding sphere");
        Require(interceptor.Velocity.DistanceTo(Vector3.Forward * 760) < 0.01f, "Collision-free flyby must keep its momentum");
    }

    private static void CheckLargeCoordinates()
    {
        (Vec3d, Vector3, Vector3) RunAt(double offset)
        {
            var world = new SimWorld();
            ShipBody a = Add(world, "A", new(offset, offset, offset - 100), Vector3.Back * 760f);
            ShipBody b = Add(world, "B", new(offset, offset, offset + 100), Vector3.Forward * 760f);
            Step(world, 30);
            Require(!ShipCollision.Overlaps(a, b), "Far-away collision must separate");
            return (b.Position - a.Position, a.Velocity, b.Velocity);
        }
        var near = RunAt(0);
        foreach (double offset in new[] { 1e6, 1e9 })
        {
            var far = RunAt(offset);
            Require((near.Item1 - far.Item1).Length() < 0.01 && near.Item2.DistanceTo(far.Item2) < 0.01f
                && near.Item3.DistanceTo(far.Item3) < 0.01f, "Large coordinates must produce the same relative collision result");
        }
    }

    private static void CheckMultipleContacts()
    {
        var world = new SimWorld();
        ShipBody a = Add(world, "A", new(0, 0, -100), Vector3.Back * 2000);
        ShipBody b = Add(world, "B", Vec3d.Zero, Vector3.Zero);
        ShipBody c = Add(world, "C", new(0, 0, 100), Vector3.Forward * 2000);
        Step(world, 30);
        Require(a.Position.Z < b.Position.Z && b.Position.Z < c.Position.Z, "Three-way collision must not swap ship order");
        Require(!ShipCollision.Overlaps(a, b) && !ShipCollision.Overlaps(b, c) && !ShipCollision.Overlaps(a, c),
            "Simultaneous contacts must all separate");
        Require((a.Velocity + b.Velocity + c.Velocity).Length() < 0.01f, "Multiple contacts must conserve total momentum");
    }

    private static void CheckWorldInterpolationAndFlight()
    {
        var world = new SimWorld();
        ShipBody ship = Add(world, "IC", new(420, 110, 850), Vector3.Forward * 380);
        Vec3d initial = ship.Position;
        ship.Control = new ShipControl { Thrust = new Vector3(0, 0, 1), FlightAssist = true, HelmForward = Vector3.Back };
        world.Step();
        Require(ship.PrevPosition == initial, "Render interpolation must retain the entire tick's starting position");
        Require(world.Tick == 1 && Math.Abs(world.Time - SimWorld.TickDelta) < 1e-12, "Collision substeps must not change the external 60Hz clock");
        double alignedAt = -1;
        for (int tick = 1; tick < 1800; tick++)
        {
            Vector3 before = ship.Velocity;
            world.Step();
            float measuredG = (ship.Velocity - before).Length() / (float)SimWorld.TickDelta / ShipBody.StandardGravity;
            if (measuredG > 5.002f || ship.Velocity.Dot(ship.Forward) < 0f)
                throw new InvalidOperationException("World integration must preserve the assisted G limit and avoid backward drift");
            if (alignedAt < 0 && ship.Forward.Dot(Vector3.Back) > 0.999f && ship.Velocity.Normalized().Dot(Vector3.Back) > 0.99f)
                alignedAt = world.Time;
        }
        Require(alignedAt > 0 && alignedAt < 12, "New collision stepping must preserve the tuned half turn");
        Console.WriteLine($"World 380m/s half turn: {alignedAt:0.00}s");
    }
}
