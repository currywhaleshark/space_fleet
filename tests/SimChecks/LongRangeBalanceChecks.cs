using System.Reflection;
using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Sim;

static class LongRangeBalanceChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
    private static object? Call(SimWorld w, string method, params object[] args) =>
        typeof(SimWorld).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, args);

    public static void Run()
    {
        Jamming(); Prediction(); AccuracyScaling(); Precision(); DefensePower(); WeakLockPower(); ReturnFire();
        Console.WriteLine($"PASS: {checks} long-range accuracy/EW/defense-power checks");
    }

    private static void Jamming()
    {
        float previous = 0;
        foreach (float raw in new[] { 0f, .1f, .5f, 1, 2, 4, 10, 100 })
        {
            float effective = SensorNet.EffectiveJamming(raw);
            Check(effective >= previous && effective <= raw && effective < 2,
                "ECM remains monotonic but cannot erase sensor strength through unlimited jamming");
            previous = effective;
        }
        var observer = new ShipBody("OBSERVER", ShipDefinitions.ById("battleship"), Faction.Blue);
        var target = new ShipBody("TARGET", ShipDefinitions.ById("mars_battleship"), Faction.Red);
        target.Place(new(0, 0, -100_000), Quaternion.Identity);
        target.Power.SetPips(2, 1, 1, 2, 2);
        var own = SensorNet.Measure(observer, target);
        var weakScreen = SensorNet.Measure(observer, target, 1);
        var strongScreen = SensorNet.Measure(observer, target, 4);
        Check(own == weakScreen, "Weaker escort ECM does not stack with self ECM");
        Check(strongScreen.Track < own.Track && strongScreen.Jam > own.Jam, "Stronger escort ECM still protects its ally");
        observer.Power.SetPips(2, 1, 1, 4, 0);
        var eccm = SensorNet.Measure(observer, target, 4);
        Check(eccm.Track > strongScreen.Track && eccm.Jam < strongScreen.Jam, "ECCM improves signal and reduces effective interference");
        var sensor = observer.Damage.Module("sensor");
        observer.Damage.Hurt(sensor, sensor.Health, 0, 0, 1);
        Check(SensorNet.Measure(observer, target).Track == 0, "Saturation cannot resurrect a destroyed local sensor");
    }

    private static void Prediction()
    {
        foreach (string hull in new[] { "battleship", "mars_battleship" })
        foreach (double offset in new[] { 0d, 1e12 })
        {
            var ship = new ShipBody("SHOOTER", ShipDefinitions.ById(hull), Faction.Blue);
            var target = new ShipBody("TARGET", ShipDefinitions.ById("battleship"), Faction.Red);
            ship.Place(new(offset, offset, offset), Quaternion.Identity);
            FiringSolution Solve(double seconds, bool noise = true)
            {
                target.Place(ship.Railgun!.MuzzlePosition + new Vec3d(0, 0, -ship.Railgun.Definition.MuzzleSpeed * seconds), Quaternion.Identity);
                var track = new SensorTrack(TrackLevel.Locked, 8, 8, 0, 0, 1, target.Position)
                { Observation = new(ship, 0, target.Position, Vector3.Zero, Quaternion.Identity, 1) };
                var result = FireControl.Solve(ship, target, 0, noise, track);
                Check(result.Valid && float.IsFinite(result.ErrorMeters), "Long-range solution is finite at a large world offset");
                var expected = result;
                target.Position += new Vec3d(1e7, 1e7, 1e7); target.Velocity = Vector3.One * 1000;
                Check(FireControl.Solve(ship, target, 0, noise, track) == expected,
                    "Prediction uses observations, not unobserved target motion");
                target.Velocity = Vector3.Zero;
                return result;
            }
            var close = Solve(1); var medium = Solve(4); var far = Solve(8); var exact = Solve(8, false);
            var gun = ship.Railgun!.Definition;
            float original = (gun.SensorErrorMeters + gun.SensorErrorPerKm * (float)(close.Range / 1000) + gun.VelocityError * (float)close.FlightTime) * gun.AimErrorScale;
            Check(Math.Abs(close.ErrorMeters - original) < .001, "Sub-two-second flight retains existing interception accuracy");
            Check(far.ErrorMeters > medium.ErrorMeters * 3 && medium.ErrorMeters > close.ErrorMeters,
                "Long flight accumulates uncertainty faster than a linear distance term");
            Check(exact.ErrorMeters == 0, "Exact ballistic solver remains free of sensor/prediction noise");
        }
    }

    private static void AccuracyScaling()
    {
        ShipDefinition Definition(float scale)
        {
            var node = JsonNode.Parse(ShipDefinitions.ById("battleship").ToJson())!;
            node["Railgun"]!["AimErrorScale"] = scale;
            // Isolate position/prediction error from the intercept time changing
            // when velocity measurement noise is also scaled.
            node["Railgun"]!["VelocityError"] = 0;
            return ShipDefinition.Parse(node.ToJsonString());
        }
        var a = new ShipBody("CALIBRATION", Definition(1), Faction.Blue);
        var b = new ShipBody("CALIBRATION", Definition(1.1f), Faction.Blue);
        var ally = new ShipBody("OBSERVER", ShipDefinitions.ById("escort"), Faction.Blue);
        var target = new ShipBody("TARGET", ShipDefinitions.ById("mars_battleship"), Faction.Red);
        foreach (double distance in new[] { 20000d, 70000, 130000 })
        {
            target.Place(new(0, 0, -distance), Quaternion.Identity);
            var track = new SensorTrack(TrackLevel.Locked, 12, 12, .4f, distance, 1, target.Position)
            { Observation = new(ally, 0, target.Position, Vector3.Zero, Quaternion.Identity, .8f) };
            var baseline = FireControl.Solve(a, target, 0, track: track);
            var spread = FireControl.Solve(b, target, 0, track: track);
            Check(baseline.Valid && spread.Valid && spread.ViaDatalink && Math.Abs(spread.ErrorMeters / baseline.ErrorMeters - 1.1) < .0001,
                "Receiver gun scales ally-supplied measurement and prediction uncertainty");
            Check(baseline.Direction.DistanceSquaredTo(spread.Direction) > 1e-12f, "Accuracy scale changes actual aim, not only the HUD error value");
            Check(FireControl.Solve(a, target, 0, false, track) == FireControl.Solve(b, target, 0, false, track),
                "Exact solver ignores all weapon accuracy scaling");
            var sensor = b.Damage.Module("sensor");
            if (!sensor.Destroyed) b.Damage.Hurt(sensor, sensor.Health, 0, 0, 1);
            Check(FireControl.Solve(b, target, 0, track: track) == spread, "Local sensor loss retains calibrated ally fire control");
        }
        var roundtrip = ShipDefinition.Parse(b.Definition.ToJson());
        Check(roundtrip.Railgun!.AimErrorScale == 1.1f && roundtrip.Railgun.ReloadSeconds == b.Definition.Railgun!.ReloadSeconds,
            "Gun accuracy and reload survive data serialization");
        foreach (float invalid in new[] { 0f, -1f })
        {
            bool rejected = false;
            try { Definition(invalid); } catch (Exception e) when (e is ArgumentException or InvalidDataException) { rejected = true; }
            Check(rejected, "Nonpositive gun accuracy scales are rejected");
        }
    }

    private static void Precision()
    {
        foreach (bool mars in new[] { false, true })
        {
            var w = new SimWorld { Doctrine = new FleetDoctrine() };
            var ship = w.Add(new ShipBody("SHOOTER", ShipDefinitions.ById(mars ? "mars_battleship" : "battleship"), Faction.Blue));
            var target = w.Add(new ShipBody("EARTH", ShipDefinitions.ById("battleship"), Faction.Red));
            target.Damage.AbsorbShield(target.Damage.Shield, 0);
            var brain = w.AttachBrain(ship, ShipOrder.AttackOn(target));
            // A nearby allied observer supplies a valid long-range lock, not an invented track.
            var scout = w.Add(new ShipBody("SCOUT", ShipDefinitions.ById("mars_escort"), Faction.Blue));
            void Observe(double distance)
            {
                target.Place(new(0, 0, -distance), Quaternion.Identity);
                scout.Place(target.Position + new Vec3d(5000, 0, 0), Quaternion.Identity);
                w.Sensors.Update(w.Ships, 0, true); Call(w, "Think", brain);
            }
            Observe(130_000);
            Check(w.Sensors.Track(Faction.Blue, target).Level == TrackLevel.Locked && brain.AimModule is null,
                "Long-range hull lock alone does not resolve a tiny sensor module");
            Observe(20_000);
            Check(brain.AimModule is not null && (!mars || brain.AimModule.Definition.Kind == ModuleKind.Sensor),
                "Closing the distance retains each design's precision doctrine");
            if (!mars) continue;
            var selected = brain.AimModule;
            target.Damage.Hurt(selected!, selected!.Health, 0, 0, 1);
            target.Position += new Vec3d(1e6, 0, 0);
            Call(w, "Think", brain);
            Check(brain.AimModule == selected, "Mars precision selection does not read unseen module health or movement");
        }
    }

    private static void DefensePower()
    {
        var w = new SimWorld();
        var ship = w.Add(new ShipBody("EARTH", ShipDefinitions.ById("battleship"), Faction.Blue));
        var target = w.Add(new ShipBody("JAMMER", ShipDefinitions.ById("mars_battleship"), Faction.Red));
        target.Place(new(0, 0, -100_000), Quaternion.Identity);
        target.Power.SetPips(2, 1, 1, 2, 2);
        var brain = w.AttachBrain(ship, ShipOrder.AttackOn(target));
        ship.Power.SetPips(2, 2, 1, 1, 2);
        var missile = new Missile { Id = 1, Shooter = target, Target = ship, Definition = target.Definition.Missiles!,
            Position = new(0, 0, -3000), Velocity = new(0, 0, 1000), Health = 10 };
        w.Sensors.Update(w.Ships, 0, true, missiles: new[] { missile }); Call(w, "PrepareAIThreats");
        Check((bool)Call(w, "IncomingMissileThreat", ship, 6000f)!, "Defense fixture observes an actual approaching missile");
        Call(w, "AllocatePower", brain);
        Check(brain.Eccm && ship.Power.Pips(PowerChannel.Sensors) == 4 && ship.Power.Pips(PowerChannel.Weapons) == 2,
            "Missile defense retains necessary ECCM and powers PD instead of repeatedly dropping the gun lock");
        Check(Enumerable.Range(0, 5).Sum(i => ship.Power.Pips((PowerChannel)i)) == ShipPower.TotalPips,
            "Combined defense and ECCM still spend only eight power pips");
    }

    private static void WeakLockPower()
    {
        var w = new SimWorld();
        var ship = w.Add(new ShipBody("EARTH", ShipDefinitions.ById("battleship"), Faction.Blue));
        var target = w.Add(new ShipBody("MARS", ShipDefinitions.ById("mars_battleship"), Faction.Red));
        var brain = w.AttachBrain(ship, ShipOrder.AttackOn(target));
        target.Place(new(0, 0, -145_000), Quaternion.Identity);
        w.Sensors.Update(w.Ships, 0, true);
        Check(w.Sensors.Track(Faction.Blue, target) is { Level: TrackLevel.Identified, Jammed: false },
            "Weak unjammed track fixture starts below firing lock");
        Call(w, "AllocatePower", brain);
        for (int i = 0; i < 8; i++)
        {
            w.Sensors.Update(w.Ships, 0, true); Call(w, "AllocatePower", brain);
            Check(w.Sensors.Track(Faction.Blue, target).Level == TrackLevel.Locked && ship.Power.Pips(PowerChannel.Sensors) == 4,
                "Weak unjammed lock keeps tracking power across repeated decisions");
        }
        target.Place(new(0, 0, -40_000), Quaternion.Identity); w.Sensors.Update(w.Ships, 0, true);
        Call(w, "AllocatePower", brain);
        Check(!brain.Eccm && ship.Power.Pips(PowerChannel.Sensors) == brain.DefaultPips[3],
            "Strong unjammed signal releases tracking power with hysteresis");
    }

    private static void ReturnFire()
    {
        foreach (bool swap in new[] { false, true })
        foreach (bool mirror in new[] { false, true })
        {
            var world = new SimWorld();
            BattleSetup.SpawnDuel(world, new BattleConfig { Seed = 1, Mirror = mirror, StartDistance = 130_000,
                BlueDesign = swap ? DesignFamily.Mars : DesignFamily.Earth,
                RedDesign = swap ? DesignFamily.Earth : DesignFamily.Mars }, HullKind.Battleship);
            var earth = world.Ships.Single(s => s.Definition.Design == DesignFamily.Earth);
            for (int i = 0; i < 180 * 60 && world.Rules!.Outcome is null && earth.Railguns.All(g => g.ShotCount == 0); i++)
                world.Step();
            Check(earth.Railguns.Any(g => g.ShotCount > 0),
                $"Earth battleship can return fire before defeat at 130 km (team swap={swap}, mirror={mirror})");
        }
    }
}
