using Godot;
using SpaceFleet.Sim;
using System.Text.Json.Nodes;

static class InterceptorRoleChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); checks++; }
    private static ShipBody Add(SimWorld world, string id, Faction faction, Vec3d position, string call, float? assaultReach = null)
    {
        var def = ShipDefinitions.ById(id);
        if (assaultReach is { } reach)
        {
            var node = JsonNode.Parse(def.ToJson())!;
            node["Ai"]!["AssaultReachMeters"] = reach;
            def = ShipDefinition.Parse(node.ToJsonString());
        }
        var ship = world.Add(new ShipBody(call, def.Flight, faction, def));
        ship.Place(position, Quaternion.Identity); ship.Control = ShipControl.Idle;
        return ship;
    }
    private static void Step(SimWorld world, double seconds)
    { for (int i = 0; i < Math.Ceiling(seconds * 60); i++) world.Step(); }
    public static void Trace()
    {
        var world = new SimWorld(); BattleSetup.Spawn(world, new BattleConfig { Seed=1 });
        for (int i=0;i<120*60;i++)
        {
            world.Step();
            if (i%600!=0) continue;
            foreach (var ship in world.Ships.Where(s=>s.Callsign=="IC-21"))
            {
                var brain=world.BrainOf(ship)!;
                Console.WriteLine($"t={world.Time:F1} mission={ship.Squadron!.Mission} action={ship.Ordnance.Interceptor.FirstAction} range={(brain.Order.Point-ship.Position).Length():F0} speed={ship.Velocity.Length():F0} desired={brain.DesiredVelocity.Length():F0} stationSpeed={brain.StationVelocity.Length():F0} ships={ship.Position} point={brain.Order.Point} anchor={ship.Squadron.ProtectedShip?.Callsign}");
            }
        }
    }
    public static void Run()
    {
        Flight(); MapPlacement(); Observation(); SmallCombat(); MovingProbeCollision(); Missions(); MovingStation();
        Console.WriteLine($"PASS: {checks} interceptor role/probe/outer defense checks");
    }

    private static void MapPlacement()
    {
        foreach (var id in new[] { "interceptor", "mars_interceptor" })
        foreach (double offset in new[] { 0d, 1e12 })
        {
            var world = new SimWorld();
            var ship = Add(world, id, Faction.Blue, new(offset, offset, offset), "MAP-SCOUT");
            Check(!world.LaunchProbeAt(ship, ship.Position).Fired && ship.Ordnance.Interceptor.Probes == 2,
                "Degenerate map placement cannot consume a probe");
            Vec3d requested = ship.Position + new Vec3d(3000, 2000, -6000);
            Check(world.LaunchProbeAt(ship, requested).Fired, "Map deployment launches");
            var probe = world.Probes.Single();
            Check((probe.Destination - requested).Length() < .01, "Near map destination is exact in double world coordinates");
            Step(world, 25);
            Check((probe.Position - requested).Length() < 100 && probe.OnStation, "Probe reaches the previewed station");
            var range = SensorNet.NominalContactRange(probe.Definition.SensorStrength);
            Check(Math.Abs(probe.Definition.SensorStrength / Math.Pow(range / 1000, 2) - SensorNet.ContactSnr) < .001,
                "Displayed contact radius matches sensor threshold for unit signature");
            double front = SensorNet.NominalContactRange(SensorNet.ProbeDirectionalStrength(probe.Definition, 1));
            double rear = SensorNet.NominalContactRange(SensorNet.ProbeDirectionalStrength(probe.Definition, -1));
            Check(rear < front && rear > front * .8, "Probe coverage retains its directional response");
            Check(world.LaunchProbeAt(ship, ship.Position + new Vec3d(200000, 0, 0)).Fired,
                "Out-of-range map request deploys at maximum reach");
            var distant = world.Probes.Last();
            Check(Math.Abs((distant.Destination - distant.Position).Length() - distant.Definition.DeploymentMeters) < .01,
                "Clamped preview and real travel range agree");
        }
    }

    private static void Flight()
    {
        foreach (var id in new[] { "interceptor", "mars_interceptor" })
        foreach (double offset in new[] { 0d, 1e12 })
        {
            var world = new SimWorld();
            var ship = Add(world, id, Faction.Blue, new(offset, offset, offset), "SCOUT");
            Check(ship.Ordnance.Interceptor.Probes == 2, "Both designs carry two probes");
            Check(!world.LaunchProbe(ship, Vector3.Zero).Fired, "Invalid direction consumes nothing");
            Check(world.LaunchProbe(ship, Vector3.Forward).Fired, "Probe launch succeeds");
            var probe = world.Probes.Single();
            Check(!world.LaunchProbe(ship, Vector3.Forward).Fired, "Cooldown prevents double launch");
            Check(ship.Ordnance.Missiles == 4 && ship.Ordnance.Antimatter.Rounds == 3, "All stores independent");
            Step(world, 25);
            double distance = (probe.Position - ship.Position).Length();
            Check(distance > 20_000 && distance < 50_000, $"Useful deployment by 25 s, still under way to the far station: {id} {distance:F0}");
            // A 70-75 km throw reaches station in about 50 s, leaving most of its 120 s battery on station.
            Step(world, 35);
            Check(probe.OnStation && Math.Abs((probe.Position - ship.Position).Length() - ship.Definition.ReconProbes!.DeploymentMeters - ship.Hull.BoundingRadius - 4) < 200,
                $"Probe brakes into its far observation area: {id}");
            Check(world.LaunchProbe(ship, Vector3.Right).Fired, "Second probe uses independent direction");
            world.ResetWeapons();
            Check(ship.Ordnance.Interceptor.Probes == 0 && !world.LaunchProbe(ship, Vector3.Forward).Fired, "Combat reset does not refill probes");
            Step(world, 121);
            Check(world.Probes.Count == 0 && world.ProbeFlights.Count == 2, "Lifetime removes lightweight bodies and records outcomes");
        }
    }

    private static void Observation()
    {
        var world = new SimWorld();
        var scout = Add(world, "interceptor", Faction.Blue, Vec3d.Zero, "SCOUT");
        var enemy = Add(world, "interceptor", Faction.Red, new(0, 0, -36_000), "HIDDEN");
        foreach (var module in scout.Damage.Modules.Where(m => m.Definition.Kind == ModuleKind.Sensor))
            scout.Damage.Hurt(module, module.Health, 0, 0, 1);
        scout.DamageControl.Priority = RepairPriority.Hold;
        // Station 30 km ahead, 6 km short of the target (the full throw now reaches 70 km).
        world.LaunchProbeAt(scout, new Vec3d(0, 0, -30_000));
        Step(world, .5);
        Check(world.Sensors.Track(Faction.Blue, enemy).Level == TrackLevel.None, "Probe does not remotely reveal its destination on launch");
        Step(world, 30);
        var track = world.Sensors.Track(Faction.Blue, enemy);
        Check(track.Level == TrackLevel.Identified && track.Observation is null, "Actual forward probe location identifies but cannot lock");
        Check(scout.Ordnance.Interceptor.NewContacts > 0 && scout.Ordnance.Interceptor.NewIdentifications > 0, "Probe intelligence contribution measured");
        Check(!FireControl.Solve(scout, enemy, world.Time, track: track).Valid, "Probe-only identification cannot produce precise rail lock");
        var probe = world.Probes.Single();
        world.LaunchProbe(scout, Vector3.Right);
        var second = world.Probes.Last(); second.Position = enemy.Position + new Vec3d(6000, 0, 0); second.Destination = second.Position; second.Velocity = Vector3.Zero;
        world.Sensors.Update(world.Ships, world.Time, true, world.Probes, world.Missiles);
        Check(world.Sensors.Track(Faction.Blue, enemy).ErrorMeters < track.ErrorMeters, "Cross-angle observations reduce position error");
        Check(world.Sensors.Track(Faction.Blue, enemy).Level == TrackLevel.Identified, "Multiple probes still cannot stack into a lock");
        foreach (var p in world.Probes) p.Health = 0;
        Step(world, .5);
        Check(world.Sensors.Track(Faction.Blue, enemy).Level == TrackLevel.None, "Probe loss removes live observation; UI retains its existing contact memory");
        Check(world.Probes.Count == 0, "Destroyed probes removed");
    }

    private static void SmallCombat()
    {
        foreach (double offset in new[] { 0d, 1e12 })
        {
            var world = new SimWorld();
            var ship = Add(world, "interceptor", Faction.Blue, new(offset, 0, 0), "DEFENDER");
            var enemy = Add(world, "interceptor", Faction.Red, new(offset, 0, -8000), "LAUNCHER");
            world.Sensors.Update(world.Ships, 0, true);
            Check(world.LaunchMissile(enemy, ship).Fired, "Threat fixture launches real missile");
            var missile = world.Missiles.Single();
            missile.Position = ship.Railgun!.MuzzlePosition + new Vec3d(0, 0, -1500);
            missile.Velocity = new(0, 0, 1000); missile.NoseDirection = Vector3.Back;
            world.Sensors.Update(world.Ships, 0, true, world.Probes, world.Missiles);
            var seen = world.Sensors.SmallTrack(Faction.Blue, missile)!.Value;
            Check(world.SmallTargetAim(ship, seen, out var direction, out var aim), "Detected missile has a finite lead solution");
            var before = missile.Position;
            missile.Position += new Vec3d(100_000, 0, 0);
            Check(world.SmallTargetAim(ship, seen, out var unchanged, out var unchangedAim) && direction == unchanged && aim == unchangedAim,
                "Lead solution never reads a hidden updated enemy pose");
            missile.Position = before;
            Check(world.FireRailgun(ship, direction).Fired, "Forward gun launches physical round toward missile");
            Step(world, .4);
            Check(!world.Missiles.Contains(missile) && ship.Ordnance.Interceptor.MissileKills == 1, "Physical rail round destroys missile and credits defender");
            Check(ship.Ordnance.Missiles == 4 && ship.Ordnance.Antimatter.Rounds == 3, "Defense does not spend normal/AM missiles");
            Check(!world.SmallTargetAim(ship, seen with { SeenAt = -1 }, out _, out _), "Stale missile data cannot authorize firing");

            var anchor = Add(world, "battleship", Faction.Blue, ship.Position + new Vec3d(0, 0, 7500), "ANCHOR");
            var incoming = new SmallContact(missile, SmallContactKind.Missile, TrackLevel.Locked,
                ship.Position + new Vec3d(0, 0, -9000), new(0, 0, 1500), world.Time);
            Check(world.TryDefenseIntercept(ship, anchor, incoming, out var intercept) && (intercept - anchor.Position).Length() < 22_000,
                "Feasible incoming route supplies a bounded lead crossing");
            Check(!world.TryDefenseIntercept(ship, anchor, incoming with { Position = ship.Position + new Vec3d(0, 0, 1000) }, out _),
                "Missile already past defender is never tail-chased");
            Check(!world.TryDefenseIntercept(ship, anchor, incoming with { Velocity = Vector3.Forward * 1500 }, out _), "Outbound missiles are ignored");
            Check(!world.TryDefenseIntercept(ship, anchor, incoming with { Position = ship.Position + new Vec3d(50_000, 0, -9000) }, out _), "Impossible crossing rejected");

            Check(world.LaunchProbe(enemy, Vector3.Forward).Fired, "Enemy deploys destructible probe");
            var probe = world.Probes.Single(); probe.Position = ship.Position + new Vec3d(0, -.6, -1800);
            probe.Destination = probe.Position; probe.Velocity = Vector3.Zero;
            ship.Railgun.Reset();
            Check(world.FireRailgun(ship, Vector3.Forward).Fired, "Forward gun shoots probe"); Step(world, .4);
            Check(!world.Probes.Contains(probe) && ship.Ordnance.Interceptor.ProbeKills == 1, "Rail collision destroys fragile probe");
        }
        var aiWorld = new SimWorld();
        var guard = Add(aiWorld, "interceptor", Faction.Blue, new(0,0,-7500), "GUARD");
        var capital = Add(aiWorld, "battleship", Faction.Blue, Vec3d.Zero, "CAPITAL");
        var attacker = Add(aiWorld, "interceptor", Faction.Red, new(0,0,-30_000), "ATTACKER");
        aiWorld.AttachBrain(guard, ShipOrder.HoldAt(guard.Position));
        var defenseWing = aiWorld.AddSquadron("DEFENSE", Faction.Blue, SquadronRole.InterceptorWing, new[] { guard });
        defenseWing.PlayerLed = true;
        aiWorld.Sensors.Update(aiWorld.Ships, 0, true);
        Check(aiWorld.LaunchMissile(attacker, capital).Fired, "Defense AI fixture launches incoming missile");
        var incomingMissile = aiWorld.Missiles.Single();
        incomingMissile.Position = new(0,0,-12_000); incomingMissile.Velocity = new(0,0,1500); incomingMissile.NoseDirection=Vector3.Back;
        aiWorld.Sensors.Update(aiWorld.Ships, 0, true, aiWorld.Probes, aiWorld.Missiles);
        aiWorld.OrderWing(defenseWing, InterceptorMission.AirDefense, capital);
        Step(aiWorld, 5);
        Check(guard.Ordnance.Interceptor.MissileKills > 0, "Outer defense AI actually aligns its forward gun and intercepts before capital PD");
        Step(aiWorld, 1);
        Check(aiWorld.BrainOf(guard)!.SmallTarget is null, "Defender releases a destroyed threat and returns to its station");
        var pdWorld = new SimWorld();
        var defender = Add(pdWorld, "battleship", Faction.Blue, Vec3d.Zero, "PD");
        var owner = Add(pdWorld, "interceptor", Faction.Red, new(0, 0, -100_000), "OWNER");
        pdWorld.LaunchProbe(owner, Vector3.Back);
        var pdProbe = pdWorld.Probes.Single(); pdProbe.Position = new(0, 900, -350); pdProbe.Destination = pdProbe.Position; pdProbe.Velocity = Vector3.Zero;
        Step(pdWorld, 30);
        Check(!pdWorld.Probes.Contains(pdProbe) && defender.Ordnance.Interceptor.ProbeKills == 1, "Existing mechanically limited PD detects and destroys probes");
    }

    private static void MovingProbeCollision()
    {
        foreach (double offset in new[] { 0d, 1e12 })
        {
            var world = new SimWorld();
            var ship = Add(world, "interceptor", Faction.Blue, new(offset, 0, 0), "GUN");
            var owner = Add(world, "interceptor", Faction.Red, new(offset, 0, -100_000), "SCOUT");
            Check(world.LaunchProbe(owner, Vector3.Right).Fired, "Moving probe fixture launches");
            var probe = world.Probes.Single();
            const double crossingTime = .014; // Fourth 240 Hz substep of the first tick.
            probe.Velocity = Vector3.Right * 1500;
            probe.Position = ship.Railgun!.MuzzlePosition
                + new Vec3d(-1500 * crossingTime, 0, -ship.Railgun.Definition.MuzzleSpeed * crossingTime);
            probe.Destination = probe.Position + new Vec3d(30_000, 0, 0);
            Check(world.FireRailgun(ship, Vector3.Forward).Fired, "Round launched across moving probe path");
            world.Step();
            Check(!world.Probes.Contains(probe) && ship.Ordnance.Interceptor.ProbeKills == 1,
                "Fast crossing probe is struck at the actual substep pose, including large coordinates");
        }
    }

    private static void Missions()
    {
        foreach (var id in new[] { "interceptor", "mars_interceptor" })
        {
            var world = new SimWorld { Doctrine = new FleetDoctrine() };
            var anchor = Add(world, "battleship", Faction.Blue, Vec3d.Zero, "ANCHOR");
            world.AddSquadron("BG", Faction.Blue, SquadronRole.BattleGroup, new[] { anchor });
            // Legacy planning range isolates the no-timer/recon regression. The longer
            // current range and an actually identified blocking screen are covered by EngagementTimingChecks.
            var ships = Enumerable.Range(0, 5).Select(i => Add(world, id, Faction.Blue, new(i * 600, 0, -5000), $"W{i}", assaultReach: 0)).ToArray();
            foreach (var s in ships) world.AttachBrain(s, ShipOrder.HoldAt(s.Position));
            var wing = world.AddSquadron("W", Faction.Blue, SquadronRole.InterceptorWing, ships);
            var enemy = Add(world, "battleship", Faction.Red, new(0, 0, -140_000), "CAPITAL");
            var escort = Add(world, "escort", Faction.Red, new(1500, -8000, -124_000), "SCREEN");
            Add(world, "escort", Faction.Red, new(-1500, -8000, -124_000), "SCREEN2");
            world.EnableCommander(Faction.Blue);
            Step(world, 180);
            Check(ships.Sum(s => s.Ordnance.Interceptor.Launched) > 0, "Wing has useful early recon action");
            Check(ships.All(s => s.Ordnance.Interceptor.FirstAssault is null), "No timed 120 s rush into intact distant screen");
            Check(ships.All(s => (s.Position - anchor.Position).Length() < 22_000), "Waiting interceptors stay within friendly defense area");
            Check(wing.MissionChanges < 20, "Automatic mission hysteresis avoids frequent switching");
            world.OrderWing(wing, InterceptorMission.Return); Step(world, 15);
            Check(wing.Mission == InterceptorMission.Return && wing.OrderedMission == InterceptorMission.Return, "Player return order persists over auto recommendation");
            world.OrderWing(wing, InterceptorMission.Assault, enemy); Step(world, 1);
            Check(wing.Mission == InterceptorMission.Assault && ships.All(s => world.BrainOf(s)!.Target == enemy), "Explicit risky assault uses existing attack-run brains");
            Check(ships.Any(s => s.Ordnance.Interceptor.FirstAssault is not null), "First assault recorded");
            world.OrderWing(wing, InterceptorMission.AirDefense, anchor); Step(world, 1);
            Check(wing.ProtectedShip == anchor && ships.All(s => world.BrainOf(s)!.Target is null), "Air defense disengages capital assault");
            var pilot = ships[0]; world.DetachBrain(pilot);
            var control = pilot.Control;
            world.OrderWing(wing, InterceptorMission.Recon); Step(world, 1);
            Check(pilot.Control.Equals(control), "Wing commands never replace player 6DOF inputs");
            world.OrderWing(wing, null); Step(world, 1);
            Check(wing.OrderedMission is null, "Player can return wing to autonomous missions");
        }
    }
    private static void MovingStation()
    {
        foreach (var id in new[] { "interceptor", "mars_interceptor" })
        {
            var world = new SimWorld();
            var anchor = Add(world, "battleship", Faction.Blue, Vec3d.Zero, "MOVING");
            anchor.Velocity = Vector3.Forward * 120; anchor.Control = new ShipControl { FlightAssist=false };
            var ship = Add(world, id, Faction.Blue, new(0,0,-3000), "GUARD");
            var brain = world.AttachBrain(ship, ShipOrder.HoldAt(ship.Position));
            var wing = world.AddSquadron("W", Faction.Blue, SquadronRole.InterceptorWing, new[] { ship });
            wing.PlayerLed = true; world.OrderWing(wing, InterceptorMission.AirDefense, anchor);
            Step(world, 90);
            Check((brain.Order.Point - ship.Position).Length() < 1000, "Defense station follows the moving capital without permanent lag");
            Check(ship.Ordnance.Interceptor.FirstAction < 90, "Establishing the forward defense station is recorded before firing is necessary");
        }
    }

}
