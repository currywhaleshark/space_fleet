using Godot;
using SpaceFleet.Sim;

/// <summary>5단계 센서·전자전 검증.</summary>
static class SensorChecks
{
    private static int _checks;

    public static void Run()
    {
        CheckRanges();
        CheckDatalink();
        CheckEcmAndEccm();
        CheckSignature();
        CheckHysteresis();
        CheckSensorDamage();
        CheckFireControl();
        CheckWorld();
        Console.WriteLine($"PASS: {_checks} sensor/EW checks");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    private static ShipBody Ship(HullKind kind, Faction faction, double zKm, string callsign)
    {
        ShipDefinition definition = ShipDefinitions.For(kind);
        var ship = new ShipBody(callsign, definition.Flight, faction, definition);
        ship.Place(new Vec3d(0, 0, -zKm * 1000.0), Quaternion.Identity);
        ship.Control = new ShipControl { FlightAssist = true };
        return ship;
    }

    private static TrackLevel Level(SensorNet net, IReadOnlyList<ShipBody> ships, Faction observer, ShipBody target)
    {
        net.Update(ships, 0, force: true);
        return net.Track(observer, target).Level;
    }

    /// <summary>단계가 바뀌는 거리(km): √(세기 × 신호 ÷ 기준).</summary>
    private static double Edge(float strength, float signature, float snr) => Math.Sqrt(strength * signature / snr);

    private static void CheckRanges()
    {
        var bb = ShipDefinitions.For(HullKind.Battleship).Sensors;
        double contact = Edge(bb.Strength, bb.Signature, SensorNet.ContactSnr);
        double identify = Edge(bb.Strength, bb.Signature, SensorNet.IdentifySnr);
        double lockKm = Edge(bb.Strength, bb.Signature, SensorNet.LockSnr);
        foreach (var (km, expected) in new[]
                 {
                     (contact * 1.03, TrackLevel.None), (contact * 0.97, TrackLevel.Contact),
                     (identify * 0.97, TrackLevel.Identified), (lockKm * 0.97, TrackLevel.Locked),
                 })
        {
            ShipBody me = Ship(HullKind.Battleship, Faction.Blue, 0, "B"), them = Ship(HullKind.Battleship, Faction.Red, km, "R");
            TrackLevel level = Level(new SensorNet(), new[] { me, them }, Faction.Blue, them);
            Require(level == expected, $"BB vs BB at {km:0} km must be {expected}, got {level}");
        }
        Require(new SensorNet().Track(Faction.Blue, Ship(HullKind.Escort, Faction.Blue, 500, "F")).Level == TrackLevel.Locked,
            "Friendly ships are always fully known");
        Console.WriteLine($"Sensor ranges BB vs BB: contact {contact:0} km, identify {identify:0} km, lock {lockKm:0} km");
    }

    private static void CheckDatalink()
    {
        // 멀리 있는 전함만으로는 접촉뿐이지만, 표적 가까이 있는 아군 호위함이 잠금을 공유한다.
        ShipBody bb = Ship(HullKind.Battleship, Faction.Blue, 0, "B");
        ShipBody dd = Ship(HullKind.Escort, Faction.Blue, 120, "D");
        ShipBody target = Ship(HullKind.Escort, Faction.Red, 150, "R");
        Require(Level(new SensorNet(), new[] { bb, target }, Faction.Blue, target) == TrackLevel.Contact, "BB alone sees a 150 km escort as a contact");
        Require(Level(new SensorNet(), new[] { bb, dd, target }, Faction.Blue, target) == TrackLevel.Locked, "A forward escort must share its lock over the datalink");
    }

    private static void CheckEcmAndEccm()
    {
        TrackLevel At(double km, (int, int, int, int, int)? targetPips, (int, int, int, int, int)? observerPips)
        {
            ShipBody me = Ship(HullKind.Battleship, Faction.Blue, 0, "B"), them = Ship(HullKind.Battleship, Faction.Red, km, "R");
            if (targetPips is var (a, b, c, d, e)) them.Power.SetPips(a, b, c, d, e);
            if (observerPips is var (f, g, h, i, j)) me.Power.SetPips(f, g, h, i, j);
            return Level(new SensorNet(), new[] { me, them }, Faction.Blue, them);
        }
        var ecm = (2, 2, 1, 1, 2);
        var eccm = (2, 2, 0, 4, 0);
        Require(At(90, null, null) == TrackLevel.Locked, "Without ECM a battleship locks at 90 km");
        Require(At(90, ecm, null) == TrackLevel.Identified, "ECM must deny a 90 km lock");
        Require(At(90, ecm, eccm) == TrackLevel.Locked, "Sensor pips (ECCM) must burn through ECM at 90 km");
        Require(At(500, null, null) == TrackLevel.None && At(500, ecm, null) == TrackLevel.Contact,
            "Jamming must give the jammer away as a long-range contact");

        ShipBody me2 = Ship(HullKind.Battleship, Faction.Blue, 0, "B"), them2 = Ship(HullKind.Battleship, Faction.Red, 90, "R");
        them2.Power.SetPips(2, 2, 1, 1, 2);
        var (_, _, jam) = SensorNet.Measure(me2, them2);
        Require(Math.Abs(jam - ShipDefinitions.For(HullKind.Battleship).Sensors.Jammer) < 0.01f, "Two ECM pips against balanced sensors jam at the data value");
    }

    private static void CheckSignature()
    {
        // 신호 크기는 위치와 무관하다. 한 척만 엔진을 태운다.
        ShipBody cold = Ship(HullKind.Escort, Faction.Red, 60, "C"), hot = Ship(HullKind.Escort, Faction.Red, 60, "H");
        hot.Control = new ShipControl { Thrust = new Vector3(0, 0, 1), FlightAssist = false };
        for (int i = 0; i < 30; i++) hot.Step(SimWorld.TickDelta);
        float ratio = SensorNet.Signature(hot) / SensorNet.Signature(cold);
        Require(ratio > 1.4f, $"A burning engine must raise the signature: x{ratio:0.00}");
    }

    private static void CheckHysteresis()
    {
        Require(SensorNet.Classify(7f, 7f, TrackLevel.Locked) == TrackLevel.Locked, "A held lock survives a dip to 7 (above 80% of 8)");
        Require(SensorNet.Classify(7f, 7f, TrackLevel.Identified) == TrackLevel.Identified, "A new lock still needs the full threshold");
        Require(SensorNet.Classify(0.5f, 0.9f, TrackLevel.Contact) == TrackLevel.Contact, "A held contact survives a dip to 90%");
        Require(SensorNet.Classify(0.5f, 0.7f, TrackLevel.Contact) == TrackLevel.None, "A contact drops below 80%");
    }

    private static void CheckSensorDamage()
    {
        ShipBody me = Ship(HullKind.Battleship, Faction.Blue, 0, "B"), them = Ship(HullKind.Battleship, Faction.Red, 100, "R");
        ModuleState sensor = me.Damage.Module("sensor");
        me.Damage.Hurt(sensor, sensor.Health, 0, 0, 1);
        Require(Level(new SensorNet(), new[] { me, them }, Faction.Blue, them) == TrackLevel.None, "A destroyed sensor module must blind the ship");
    }

    private static void CheckFireControl()
    {
        ShipBody me = Ship(HullKind.Battleship, Faction.Blue, 0, "B"), them = Ship(HullKind.Battleship, Faction.Red, 100, "R");
        var identified = new SensorTrack(TrackLevel.Identified, 4, 4, 0, 100_000, 100, them.Position);
        Require(!FireControl.Solve(me, them, 1, true, identified).Valid, "Fire control must refuse an identified-but-unlocked track");
        var clean = new SensorTrack(TrackLevel.Locked, SensorNet.LockSnr, SensorNet.LockSnr, 0, 100_000, 10, them.Position);
        var jammed = clean with { JamRatio = 3f };
        float baseError = FireControl.Solve(me, them, 1, true, clean).ErrorMeters;
        float jamError = FireControl.Solve(me, them, 1, true, jammed).ErrorMeters;
        Require(Math.Abs(jamError / baseError - 2f) < 0.01f, $"ECM (jam 3) must double fire-control error: x{jamError / baseError:0.00}");
        var strong = clean with { TrackSnr = SensorNet.LockSnr * 16 };
        Require(Math.Abs(FireControl.Solve(me, them, 1, true, strong).ErrorMeters / baseError - 0.5f) < 0.01f, "A very strong track halves error at most");
    }

    private static void CheckWorld()
    {
        var world = new SimWorld();
        ShipBody me = world.Add(Ship(HullKind.Battleship, Faction.Blue, 0, "B"));
        ShipBody near = world.Add(Ship(HullKind.Battleship, Faction.Red, 100, "N"));
        ShipBody far = world.Add(Ship(HullKind.Interceptor, Faction.Red, 100, "F"));
        for (int i = 0; i < 20; i++) world.Step();
        Require(world.Sensors.Track(Faction.Blue, near).Level == TrackLevel.Locked, "The world must update tracks as it steps");
        Require(world.Sensors.Track(Faction.Blue, far).Level == TrackLevel.None, "A cold interceptor at 100 km must stay hidden from a battleship");
        Require(world.Sensors.Track(Faction.Red, me).Level == TrackLevel.Locked, "Tracks are kept per faction");
        SensorTrack track = world.Sensors.Track(Faction.Blue, near);
        Require((track.EstimatedPosition - near.Position).Length() <= track.ErrorMeters * 1.75f, "Estimated positions must stay within the stated error");
    }
}
