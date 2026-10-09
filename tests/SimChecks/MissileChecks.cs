using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Sim;

/// <summary>5b단계 미사일·디코이·근접방어 검증.</summary>
static class MissileChecks
{
    private static int _checks;

    public static void Run()
    {
        CheckStationaryHit();
        CheckCrossingHit();
        CheckPointDefense();
        CheckDecoys();
        CheckGroupDefense();
        CheckLaunchRules();
        CheckLargeCoordinates();
        CheckLaunchAttitude();
        CheckSteeringLimits();
        CheckMissedPass();
        CheckWideReattack();
        CheckSteeringValidation();
        Console.WriteLine($"PASS: {_checks} missile/PD/decoy checks");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    /// <summary>데이터 변형: 근접방어·디코이를 빼거나 치명 판정을 끈 함선(결과를 운에 덜 흔들리게).</summary>
    private static ShipDefinition Variant(string name, bool pointDefense = true, bool decoys = true)
    {
        using var stream = typeof(ShipDefinitions).Assembly.GetManifestResourceStream($"SpaceFleet.data.ships.{name}.json")!;
        using var reader = new StreamReader(stream);
        JsonObject data = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
        if (!pointDefense) { data.Remove("pointDefense"); data.Remove("defenseDrones"); }
        if (!decoys) data.Remove("decoys");
        foreach (JsonNode? m in data["modules"]!.AsArray()) m!["criticalChance"] = 0;
        return ShipDefinition.Parse(data.ToJsonString());
    }

    private static ShipBody Add(SimWorld world, ShipDefinition def, Faction faction, Vec3d position, string callsign)
    {
        var ship = world.Add(new ShipBody(callsign, def.Flight, faction, def));
        ship.Place(position, Quaternion.Identity);
        ship.Control = new ShipControl { FlightAssist = false };
        return ship;
    }

    /// <summary>미사일이 모두 사라질 때까지(또는 limit초) 진행하며 결과를 센다.</summary>
    private static (int Hits, int Intercepted, int Wasted, double Seconds) Fly(SimWorld world, ShipBody target, double limit,
        Action<int>? everyTick = null)
    {
        int intercepted = 0, wasted = 0;
        var seen = new HashSet<OrdnanceEvent>();
        // 명중은 위치가 아니라 실제로 표적에 피해 계산이 들어간 폭발(Impacts)로 센다.
        // 디코이에 끌려간 미사일은 함선 수십 m 옆에서 터져도 피해가 없다.
        var hitIds = new HashSet<uint>();
        int tick = 0;
        while (tick < limit * 60 && (world.Missiles.Count > 0 || tick == 0))
        {
            everyTick?.Invoke(tick);
            world.Step();
            tick++;
            foreach (ProjectileImpact impact in world.Impacts)
                if (impact.Hit.Target == target)
                    hitIds.Add(impact.Id);
            foreach (OrdnanceEvent e in world.OrdnanceEvents)
                if (seen.Add(e))
                {
                    if (e.Kind == OrdnanceEventKind.Intercepted) intercepted++;
                    else wasted++;
                }
        }
        return (hitIds.Count, intercepted, wasted - hitIds.Count, tick / 60.0);
    }

    private static void CheckStationaryHit()
    {
        var world = new SimWorld();
        ShipBody dd = Add(world, Variant("escort"), Faction.Blue, Vec3d.Zero, "DD");
        ShipBody bb = Add(world, Variant("battleship", pointDefense: false), Faction.Red, new Vec3d(0, 0, -60_000), "BB");
        world.Step();
        float shield = bb.Damage.Shield;
        Require(world.LaunchMissile(dd, bb).Fired, "A locked escort must launch at a 60 km battleship");
        var (hits, _, _, seconds) = Fly(world, bb, 120);
        Require(hits == 1 && bb.Damage.Shield < shield, "A missile must reach and damage a stationary battleship");
        Console.WriteLine($"Missile DD->BB 60 km stationary: impact after {seconds:0.0}s");
    }

    private static void CheckCrossingHit()
    {
        var world = new SimWorld();
        ShipBody dd = Add(world, Variant("escort"), Faction.Blue, Vec3d.Zero, "DD");
        ShipBody target = Add(world, Variant("escort", pointDefense: false, decoys: false), Faction.Red, new Vec3d(0, 0, -30_000), "T");
        target.Velocity = Vector3.Right * 200f;
        world.Step();
        Require(world.LaunchMissile(dd, target).Fired, "Launch at a crossing escort");
        var (hits, _, _, seconds) = Fly(world, target, 120, _ => target.Velocity = Vector3.Right * 200f);
        Require(hits == 1, "Proportional navigation must hit a 200 m/s crossing escort at 30 km");
        Console.WriteLine($"Missile DD->DD 30 km crossing 200 m/s: impact after {seconds:0.0}s");
    }

    private static (int Hits, int Intercepted) Salvo(bool pointDefense, bool useDecoys, int count = 6, float azimuth = 0)
    {
        var world = new SimWorld();
        ShipBody shooter = Add(world, Variant("battleship"), Faction.Blue, Vec3d.Zero, "S");
        ShipBody target = Add(world, Variant("escort", pointDefense, decoys: useDecoys), Faction.Red,
            Vec3d.From(new Vector3(0, 0, -40_000).Rotated(Vector3.Up, Mathf.DegToRad(azimuth))), "T");
        world.Step();
        int launched = 0;
        var (hits, intercepted, _, _) = Fly(world, target, 240, tick =>
        {
            if (launched < count && shooter.Ordnance.MissileReady && world.LaunchMissile(shooter, target).Fired)
                launched++;
            // 디코이: 미사일이 8 km 안에 들어오면 쿨다운마다 사출.
            if (useDecoys && target.Ordnance.DecoyReady
                && world.Missiles.Any(m => (m.Position - target.Position).Length() < 8000))
                world.LaunchDecoys(target);
        });
        Require(launched == count, "Salvo must launch every missile");
        return (hits, intercepted);
    }

    private static void CheckPointDefense()
    {
        int bareHits = 0, defendedHits = 0, intercepted = 0;
        // Finite PD arcs and deterministic hit rolls make one frontal volley a poor
        // coverage test. Exercise all four sides without changing the actual PD data.
        foreach (float azimuth in new[] { 0f, 90f, 180f, 270f })
        {
            var bare = Salvo(false, false, azimuth: azimuth);
            var defended = Salvo(true, false, azimuth: azimuth);
            Require(bare.Hits == 6 && bare.Intercepted == 0, $"Undefended salvo at {azimuth} degrees: {bare.Hits}/6 hits");
            bareHits += bare.Hits; defendedHits += defended.Hits; intercepted += defended.Intercepted;
        }
        Require(intercepted >= 1 && defendedHits > 0 && defendedHits < bareHits,
            $"PD must intercept missiles across four approach directions: {intercepted} intercepted, {defendedHits} hit");
        Console.WriteLine($"Salvo BB->DD, four directions at 40 km: no PD {bareHits}/24 hits / PD {intercepted} intercepted, {defendedHits} hits");
    }

    private static void CheckDecoys()
    {
        var bare = Salvo(pointDefense: false, useDecoys: false);
        var decoyed = Salvo(pointDefense: false, useDecoys: true);
        Require(decoyed.Hits < bare.Hits, $"Decoys must pull some missiles away: {decoyed.Hits} vs {bare.Hits} hits");
        Console.WriteLine($"Salvo BB->DD x6 at 40 km: decoys {decoyed.Hits} hits (no decoys {bare.Hits})");
    }

    /// <summary>호위함 4척이 2발씩(8발) 동시에 전투단(전함+호위함 2)을 노린다. 막을 수는 있지만 다 막지는 못해야 한다.</summary>
    private static void CheckGroupDefense()
    {
        (int hits, int intercepted) Trial(bool defense, float azimuth, int range)
        {
            var world = new SimWorld();
            ShipBody bb = Add(world, Variant("battleship", defense), Faction.Red, Vec3d.Zero, "BB");
            Add(world, Variant("escort", defense), Faction.Red, new Vec3d(-1600, 0, -600), "D1");
            Add(world, Variant("escort", defense), Faction.Red, new Vec3d(1600, 0, -600), "D2");
            var shooters = Enumerable.Range(0, 4)
                .Select(i => Add(world, Variant("escort"), Faction.Blue,
                    Vec3d.From(new Vector3(-6000 + i * 4000, 0, -range).Rotated(Vector3.Up, Mathf.DegToRad(azimuth))), $"S{i}")).ToList();
            world.Step();
            var launched = new Dictionary<ShipBody, int>();
            var (hits, intercepted, _, _) = Fly(world, bb, 240, tick =>
            {
                foreach (ShipBody s in shooters)
                    if (launched.GetValueOrDefault(s) < 2 && s.Ordnance.MissileReady && world.LaunchMissile(s, bb).Fired)
                        launched[s] = launched.GetValueOrDefault(s) + 1;
            });
            Require(launched.Values.Sum() == 8, "All eight missiles must launch");
            return (hits, intercepted);
        }
        int totalHits = 0, totalIntercepted = 0;
        foreach (float azimuth in new[] { 0f, 90f, 180f, 270f })
        foreach (int range in new[] { 30_000, 60_000 })
        {
            var bare = Trial(false, azimuth, range);
            var defended = Trial(true, azimuth, range);
            Require(bare.hits == 8 && bare.intercepted == 0, $"Undefended group at {range} m/{azimuth} degrees takes all eight missiles");
            Require(defended.hits >= 2 && defended.hits <= bare.hits, "A saturated group cannot stop every missile");
            totalHits += defended.hits; totalIntercepted += defended.intercepted;
        }
        Require(totalIntercepted >= 1 && totalHits < 64,
            $"A group must intercept missiles across multiple ranges and approach directions: {totalIntercepted} intercepted, {totalHits} hit");
        Console.WriteLine($"Group defense, four directions at 30/60 km: {totalIntercepted}/64 intercepted, {totalHits} hit");
    }

    private static void CheckLaunchRules()
    {
        var world = new SimWorld();
        ShipBody bb = Add(world, Variant("battleship"), Faction.Blue, Vec3d.Zero, "B");
        ShipBody ic = Add(world, Variant("interceptor"), Faction.Red, new Vec3d(0, 0, -100_000), "I");
        ShipBody dd = Add(world, Variant("escort"), Faction.Red, new Vec3d(0, 0, -40_000), "D");
        world.Step();
        Require(!world.LaunchMissile(bb, ic).Fired, "An undetected target cannot be engaged");
        Require(world.LaunchMissile(bb, dd).Fired && !world.LaunchMissile(bb, dd).Fired, "Reload must block an immediate second launch");
        Require(!world.LaunchMissile(bb, bb).Fired, "A ship cannot target itself or friends");
        ModuleState mag = bb.Damage.Module("magazine-port");
        bb.Damage.Hurt(mag, mag.Health, 0, 0, 1);
        ModuleState mag2 = bb.Damage.Module("magazine-starboard");
        bb.Damage.Hurt(mag2, mag2.Health, 0, 0, 1);
        for (int i = 0; i < 600; i++) world.Step();
        Require(!world.LaunchMissile(bb, dd).Fired && bb.Ordnance.MissileStatus.Contains("탄약고"), "Destroyed magazines must stop launches");
        world.ResetWeapons();
        Require(world.Missiles.Count == 0 && bb.Ordnance.Missiles == bb.Definition.Missiles!.Rounds, "Weapon reset must clear missiles and restore rounds");
    }

    private static void CheckLargeCoordinates()
    {
        var world = new SimWorld();
        var far = new Vec3d(1e9, -2e9, 3e9);
        ShipBody dd = Add(world, Variant("escort"), Faction.Blue, far, "DD");
        ShipBody bb = Add(world, Variant("battleship", pointDefense: false), Faction.Red, far + new Vec3d(0, 0, -30_000), "BB");
        world.Step();
        Require(world.LaunchMissile(dd, bb).Fired, "Launch at large coordinates");
        Require(Fly(world, bb, 120).Hits == 1, "Missiles must hit at 1e9 m world coordinates");
    }

    private static (SimWorld World, Missile Missile) NearMiss(string kind, float speed, bool behind = false)
    {
        var world = new SimWorld();
        var shooter = Add(world, Variant(kind, false, false), Faction.Blue, new Vec3d(0, 0, 2000), "SHOOTER");
        var target = Add(world, Variant("interceptor", false, false), Faction.Red, Vec3d.Zero, "TARGET");
        world.Step();
        Require(world.LaunchMissile(shooter, target).Fired, "Near-pass fixture must launch a real missile");
        var missile = world.Missiles.Single();
        missile.Position = missile.PrevPosition = new Vec3d(300, 0, behind ? -200 : 1000);
        missile.Velocity = Vector3.Forward * speed;
        missile.NoseDirection = missile.PreviousNoseDirection = Vector3.Forward;
        missile.Age = 2;
        missile.SeekerTarget = target;
        missile.NextSeekerCheck = world.Time;
        return (world, missile);
    }

    private static void CheckLaunchAttitude()
    {
        var world = new SimWorld();
        var shooter = Add(world, Variant("escort", false), Faction.Blue, Vec3d.Zero, "DD");
        var target = Add(world, Variant("battleship", false), Faction.Red, new Vec3d(0, 0, -30000), "BB");
        shooter.Place(Vec3d.Zero, new Quaternion(Vector3.Forward, .8f));
        shooter.Velocity = Vector3.Forward * 1500;
        world.Step();
        Require(world.LaunchMissile(shooter, target).Fired, "Moving, rolled carrier can launch");
        var missile = world.Missiles.Single();
        Require(missile.NoseDirection.DistanceTo(shooter.Up) < .0001f
            && missile.PreviousNoseDirection == missile.NoseDirection, "Launch attitude follows the actual vertical tube");
        Require(missile.Velocity.DistanceTo(shooter.Velocity + shooter.Up * missile.Definition.EjectSpeed) < .001f
            && missile.NoseDirection.AngleTo(missile.Velocity) > 1, "Ejection preserves carrier momentum without rotating the missile to match it");
    }

    private static void CheckSteeringLimits()
    {
        Vector3 reversed = SimWorld.TurnMissileNose(Vector3.Forward, Vector3.Back, .1f);
        Require(reversed.IsFinite() && Math.Abs(reversed.Length() - 1) < .0001f
            && Math.Abs(reversed.AngleTo(Vector3.Forward) - .1f) < .0001f, "Exact 180-degree demand has a finite, bounded turn axis");
        float slowTurn = 0;
        foreach (float speed in new[] { 500f, 2500f })
        {
            var (world, missile) = NearMiss("escort", speed, behind: true);
            bool boundedNose = true, boundedThrust = true, finite = true;
            for (int tick = 0; tick < 60; tick++)
            {
                Vector3 oldNose = missile.NoseDirection, oldVelocity = missile.Velocity;
                world.Step();
                float turnLimit = Mathf.DegToRad(missile.Definition.TurnRateDegrees) * (float)SimWorld.TickDelta;
                Vector3 acceleration = (missile.Velocity - oldVelocity) / (float)SimWorld.TickDelta;
                boundedNose &= oldNose.AngleTo(missile.NoseDirection) <= turnLimit + .0002f;
                boundedThrust &= acceleration.Length() <= missile.Definition.Accel + .1f
                    && acceleration.AngleTo(missile.NoseDirection) <= Mathf.DegToRad(missile.Definition.ThrustGimbalDegrees) + turnLimit + .002f;
                finite &= missile.NoseDirection.IsFinite() && missile.Velocity.IsFinite();
            }
            Require(boundedNose && boundedThrust && finite, "Live guidance obeys body slew, engine gimbal, and acceleration limits");
            float pathTurn = Vector3.Forward.AngleTo(missile.Velocity);
            if (speed == 500) slowTurn = pathTurn;
            else Require(pathTurn < slowTurn * .6f, "Higher momentum must turn the flight path substantially more slowly");
        }
    }

    private static void CheckMissedPass()
    {
        foreach (string kind in new[] { "battleship", "escort", "interceptor" })
        {
            var (world, missile) = NearMiss(kind, 2500);
            double start = world.Time, pass = -1, reapproach = -1, widest = 0;
            bool hit = false, coastStable = true, coastObserved = false, seekerLost = false;
            while (world.Missiles.Contains(missile) && world.Time - start < 181)
            {
                bool coasting = !missile.Burning;
                Vector3 velocity = missile.Velocity, nose = missile.NoseDirection;
                world.Step();
                double elapsed = world.Time - start;
                Vector3 to = (missile.Target.Position - missile.Position).ToVector3();
                float closing = to.Normalized().Dot(missile.Velocity);
                if (closing < 0 && pass < 0) pass = elapsed;
                if (pass >= 0 && reapproach < 0)
                {
                    widest = Math.Max(widest, to.Length());
                    if (closing > 0) reapproach = elapsed;
                }
                if (pass >= 0 && !missile.SeekerLocked) seekerLost = true;
                if (coasting) { coastObserved = true; coastStable &= missile.Velocity == velocity && missile.NoseDirection == nose; }
                hit |= world.Impacts.Any(i => i.Id == missile.Id && i.Hit.Target == missile.Target);
            }
            Require(pass >= 0 && pass < 1 && seekerLost, "Fixture passes close enough to lose the forward search window");
            Require(!hit && (reapproach < 0 || reapproach > 25) && widest > 10000,
                $"{kind} must not brake/reverse into a quick second hit: approach={reapproach:F1}s, widest={widest:F0}m");
            Require(coastObserved && coastStable, "Burnout ends both powered steering and acceleration");
            Require(!world.Missiles.Contains(missile)
                && world.OrdnanceEvents.Any(e => e.Kind == OrdnanceEventKind.Expired), "Missed missile expires on its existing lifetime, not instantly on passing");
            Console.WriteLine($"Near miss {kind} at 2500 m/s: reapproach={(reapproach < 0 ? "none" : $"{reapproach:F1}s")}, no hit, coast/expiry OK");
        }
    }

    private static void CheckWideReattack()
    {
        var (world, missile) = NearMiss("escort", 200, behind: true);
        double start = world.Time, widest = 0;
        bool hit = false;
        while (world.Missiles.Contains(missile) && world.Time - start < 60)
        {
            world.Step();
            widest = Math.Max(widest, (missile.Position - missile.Target.Position).Length());
            hit |= world.Impacts.Any(i => i.Id == missile.Id && i.Hit.Target == missile.Target);
        }
        Require(hit && world.Time - start > 20 && widest > 5000 && missile.Age < missile.Definition.BurnSeconds,
            "A lower-speed missile may reattack, but only after a large, fuel-consuming turn");
        Console.WriteLine($"Low-speed rear-target reattack: hit after {world.Time - start:F1}s, turn reaches {widest / 1000:F1} km");
    }

    private static void CheckSteeringValidation()
    {
        using var stream = typeof(ShipDefinitions).Assembly.GetManifestResourceStream("SpaceFleet.data.ships.escort.json")!;
        using var reader = new StreamReader(stream);
        string json = reader.ReadToEnd();
        foreach (var (key, value) in new[] { ("turnRateDegrees", 0f), ("turnRateDegrees", 181f),
            ("maxSteeringAngleDegrees", 0f), ("maxSteeringAngleDegrees", 91f), ("thrustGimbalDegrees", -1f), ("thrustGimbalDegrees", 46f) })
        {
            var data = JsonNode.Parse(json)!;
            data["missiles"]![key] = value;
            bool rejected = false;
            try { ShipDefinition.Parse(data.ToJsonString()); }
            catch (InvalidDataException) { rejected = true; }
            Require(rejected, $"Invalid missile control value must be rejected: {key}={value}");
        }
    }
}
