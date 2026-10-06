using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Sim;

/// <summary>6단계 함선 AI·함대 지휘 검증.</summary>
static class AIChecks
{
    private static int _checks;

    public static void Run()
    {
        CheckEscortFormation();
        CheckAvoidance();
        CheckFleetAttack();
        CheckAttackRun();
        CheckSensorFairness();
        CheckDeterminism();
        CheckDisabled();
        CheckFullBattle();
        Console.WriteLine($"PASS: {_checks} AI/fleet checks");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    private static ShipDefinition Variant(string name, bool pointDefense = true)
    {
        using var stream = typeof(ShipDefinitions).Assembly.GetManifestResourceStream($"SpaceFleet.data.ships.{name}.json")!;
        using var reader = new StreamReader(stream);
        JsonObject data = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
        if (!pointDefense) data.Remove("pointDefense");
        foreach (JsonNode? m in data["modules"]!.AsArray()) m!["criticalChance"] = 0;
        return ShipDefinition.Parse(data.ToJsonString());
    }

    private static ShipBody Add(SimWorld world, string name, Faction faction, Vec3d position, string callsign, bool pointDefense = true)
    {
        ShipDefinition def = Variant(name, pointDefense);
        var ship = world.Add(new ShipBody(callsign, def.Flight, faction, def));
        ship.Place(position, faction == Faction.Red ? new Quaternion(Vector3.Up, Mathf.Pi) : Quaternion.Identity);
        ship.Control = ShipControl.Idle;
        return ship;
    }

    private static bool Damaged(ShipBody ship) =>
        ship.Damage.Shield < ship.Damage.ShieldCapacity - 0.5f || ship.Damage.Modules.Any(m => m.HealthFraction < 1f);

    private static void CheckEscortFormation()
    {
        var world = new SimWorld();
        ShipBody leader = Add(world, "battleship", Faction.Blue, Vec3d.Zero, "L");
        ShipBody escort = Add(world, "escort", Faction.Blue, new Vec3d(-5000, 0, 3000), "E");
        world.AttachBrain(escort, ShipOrder.EscortOf(leader)).FormationOffset = new Vector3(1500, 0, 0);
        leader.Control = new ShipControl { FlightAssist = false };
        for (int i = 0; i < 60 * 120; i++)
        {
            leader.Velocity = Vector3.Forward * 60f;
            world.Step();
        }
        Vec3d slot = leader.Position + Vec3d.From(leader.Orientation * new Vector3(1500, 0, 0));
        double error = (slot - escort.Position).Length();
        Require(error < 600, $"Escort must hold its slot on a moving leader: {error:0} m off");
        Require(escort.LastCollision is null && leader.LastCollision is null, "Formation flying must not collide");
    }

    private static void CheckAvoidance()
    {
        var world = new SimWorld();
        ShipBody a = Add(world, "escort", Faction.Blue, new Vec3d(-6000, 0, 0), "A");
        ShipBody b = Add(world, "escort", Faction.Blue, new Vec3d(6000, 0, 0), "B");
        world.AttachBrain(a, ShipOrder.HoldAt(Vec3d.Zero));
        world.AttachBrain(b, ShipOrder.HoldAt(Vec3d.Zero));
        double closest = double.PositiveInfinity;
        for (int i = 0; i < 60 * 180; i++)
        {
            world.Step();
            closest = Math.Min(closest, (a.Position - b.Position).Length());
        }
        Require(a.LastCollision is null && b.LastCollision is null, "Two ships told to hold the same point must not collide");
        Require(closest > a.Hull.BoundingRadius * 2, $"Avoidance must keep separation: closest {closest:0} m");
    }

    /// <summary>적 전함+호위함 2(지휘관) 대 정지한 아군 전함(AI 없음, ECM 없음).</summary>
    private static (double FirstMissile, double FirstRail, double FirstDamage, float Shield, int Damaged, int SalvoSpread) FleetAttack()
    {
        var world = new SimWorld();
        ShipBody blue = Add(world, "battleship", Faction.Blue, Vec3d.Zero, "BLUE");
        foreach (var (name, offset, call) in new[] { ("battleship", new Vec3d(0, 0, -110_000), "RBB"),
                     ("escort", new Vec3d(-2000, 0, -108_500), "RD1"), ("escort", new Vec3d(2000, 0, -108_500), "RD2") })
        {
            ShipBody red = Add(world, name, Faction.Red, offset, call);
            world.AttachBrain(red, ShipOrder.HoldAt(red.Position)).DefaultPips = new[] { 2, 2, 1, 1, 2 };
        }
        world.EnableCommander(Faction.Red);
        double firstMissile = -1, firstRail = -1, firstDamage = -1;
        var launches = new List<double>();
        var seen = new HashSet<uint>();
        for (int i = 0; i < 60 * 600 && (firstDamage < 0 || firstRail < 0 || launches.Count < 4); i++)
        {
            world.Step();
            foreach (Missile m in world.Missiles)
                if (seen.Add(m.Id)) launches.Add(world.Time);
            if (firstMissile < 0 && launches.Count > 0) firstMissile = world.Time;
            if (firstRail < 0 && world.Projectiles.Count > 0) firstRail = world.Time;
            if (firstDamage < 0 && Damaged(blue)) firstDamage = world.Time;
        }
        // 첫 일제 사격에서 두 척 이상이 6초 창 안에 쐈는가.
        int spread = launches.Count(t => t - launches.FirstOrDefault() <= 6.0);
        return (firstMissile, firstRail, firstDamage, blue.Damage.Shield, blue.Damage.Modules.Count(m => m.HealthFraction < 1f), spread);
    }

    private static void CheckFleetAttack()
    {
        var r = FleetAttack();
        Require(r.FirstMissile > 0, "The red fleet must launch missiles at an identified battleship");
        Require(r.SalvoSpread >= 3, $"The first salvo must come from several ships within the window: {r.SalvoSpread} launches");
        Require(r.FirstRail > 0, "The red fleet must open railgun fire once in range");
        Require(r.FirstMissile < r.FirstRail, "Long-range missiles must come before the railgun duel");
        Require(r.FirstDamage > 0, "The red fleet must damage the blue battleship");
        Console.WriteLine($"Fleet attack (RBB+2DD vs BB, 110 km): first missile {r.FirstMissile:0}s, first railgun {r.FirstRail:0}s, first damage {r.FirstDamage:0}s");
    }

    private static void CheckAttackRun()
    {
        var world = new SimWorld();
        ShipBody target = Add(world, "escort", Faction.Blue, Vec3d.Zero, "T", pointDefense: false);
        ShipBody ic = Add(world, "interceptor", Faction.Red, new Vec3d(0, 0, -25_000), "IC");
        world.AttachBrain(ic, ShipOrder.AttackOn(target));
        int passes = 0;
        bool breaking = false;
        for (int i = 0; i < 60 * 120; i++)
        {
            world.Step();
            bool now = world.BrainOf(ic)!.Activity == "이탈";
            if (now && !breaking) passes++;
            breaking = now;
        }
        Require(Damaged(target), "An interceptor attack run must damage a stationary escort");
        Require(ic.LastCollision is null, "Attack runs must break off before ramming");
        Require(passes >= 2, $"The interceptor must make repeated passes: {passes}");
        Console.WriteLine($"Attack run (IC vs DD, 25 km, 120 s): {passes} passes, DD shield {target.Damage.Shield:0}/{target.Damage.ShieldCapacity:0}");
    }

    private static void CheckSensorFairness()
    {
        var world = new SimWorld();
        Add(world, "interceptor", Faction.Blue, new Vec3d(0, 0, -100_000), "COLD");
        ShipBody red = Add(world, "battleship", Faction.Red, Vec3d.Zero, "R");
        world.AttachBrain(red, ShipOrder.HoldAt(Vec3d.Zero));
        world.EnableCommander(Faction.Red);
        for (int i = 0; i < 60 * 30; i++) world.Step();
        Require(world.Missiles.Count == 0 && world.Projectiles.Count == 0 && world.BrainOf(red)!.Order.Kind == OrderKind.Hold,
            "AI must not engage what its sensors cannot see");
    }

    private static void CheckDisabled()
    {
        var world = new SimWorld();
        ShipBody ship = Add(world, "escort", Faction.Blue, Vec3d.Zero, "D");
        world.AttachBrain(ship, ShipOrder.HoldAt(new Vec3d(0, 0, -50_000)));
        for (int i = 0; i < 120; i++) world.Step();
        foreach (string id in new[] { "bus-port", "bus-starboard" })
        {
            ModuleState m = ship.Damage.Module(id);
            ship.Damage.Hurt(m, m.Health, world.Time, 0, 1);
        }
        Vector3 drift = ship.Velocity;
        for (int i = 0; i < 120; i++) world.Step();
        Require(ship.Damage.Disabled && !ship.Damage.Destroyed, "Losing both power buses must disable, not destroy");
        Require(ship.Damage.Reports.Any(r => r.Message.Contains("무력화")), "Becoming disabled must be reported");
        Require(ship.Velocity.DistanceTo(drift) < 0.01f && world.BrainOf(ship)!.Activity == "무력화", "A disabled ship must drift and its AI must stand down");
    }

    private static void CheckDeterminism()
    {
        var a = FleetAttack();
        var b = FleetAttack();
        Require(a == b, "The same scenario must play out identically");
    }

    /// <summary>게임 시작 배치와 같은 두 함대(양쪽 지휘관)를 20분 동안 싸우게 한다.</summary>
    private static void CheckFullBattle()
    {
        var world = new SimWorld();
        var anchor = new Vec3d(3000, 22000, -150000);
        var ships = new List<ShipBody>
        {
            Add(world, "battleship", Faction.Blue, new Vec3d(0, 0, 0), "BB-01"),
            Add(world, "escort", Faction.Blue, new Vec3d(-900, 160, -500), "DD-11"),
            Add(world, "escort", Faction.Blue, new Vec3d(950, -140, -300), "DD-12"),
            Add(world, "interceptor", Faction.Blue, new Vec3d(420, 110, 850), "IC-21"),
            Add(world, "interceptor", Faction.Blue, new Vec3d(470, 70, 905), "IC-22"),
            Add(world, "battleship", Faction.Red, anchor, "BB-X1"),
            Add(world, "escort", Faction.Red, anchor + new Vec3d(-2200, 400, 1500), "DD-X1"),
            Add(world, "escort", Faction.Red, anchor + new Vec3d(2400, -600, 1800), "DD-X2"),
            Add(world, "interceptor", Faction.Red, anchor + new Vec3d(-600, 300, 9000), "IC-X1"),
            Add(world, "interceptor", Faction.Red, anchor + new Vec3d(500, 200, 9200), "IC-X2"),
        };
        foreach (ShipBody s in ships)
        {
            ShipBrain brain = world.AttachBrain(s, ShipOrder.HoldAt(s.Position));
            if (s.Faction == Faction.Red && s.Class.Kind != HullKind.Interceptor) brain.DefaultPips = new[] { 2, 2, 1, 1, 2 };
        }
        world.EnableCommander(Faction.Blue);
        world.EnableCommander(Faction.Red);

        double firstContact = -1;
        int collisions = 0;
        var missiles = new Dictionary<Faction, int> { [Faction.Blue] = 0, [Faction.Red] = 0 };
        var rails = new Dictionary<Faction, int> { [Faction.Blue] = 0, [Faction.Red] = 0 };
        var hits = new Dictionary<(Faction, bool), int>();
        var intercepted = new Dictionary<Faction, int> { [Faction.Blue] = 0, [Faction.Red] = 0 };
        var seenMissiles = new HashSet<uint>();
        var seenRails = new HashSet<uint>();
        var seenImpacts = new HashSet<uint>();
        var seenEvents = new HashSet<(double, Faction, OrdnanceEventKind)>();
        for (int i = 0; i < 60 * 60 * 20; i++)
        {
            world.Step();
            foreach (Missile m in world.Missiles) if (seenMissiles.Add(m.Id)) missiles[m.Faction]++;
            foreach (RailProjectile p in world.Projectiles) if (seenRails.Add(p.Id)) rails[p.Shooter.Faction]++;
            foreach (ProjectileImpact imp in world.Impacts)
                if (seenImpacts.Add(imp.Id))
                {
                    var key = (imp.Shooter.Faction, seenMissiles.Contains(imp.Id));
                    hits[key] = hits.GetValueOrDefault(key) + 1;
                }
            foreach (OrdnanceEvent e in world.OrdnanceEvents)
                if (e.Kind == OrdnanceEventKind.Intercepted && seenEvents.Add((e.Time, e.Faction, e.Kind))) intercepted[e.Faction]++;
            if (firstContact < 0 && ships.Any(Damaged)) firstContact = world.Time;
            foreach (ShipBody s in ships)
                if (s.LastCollision is CollisionImpact c && Math.Abs(c.Time - world.Time) < SimWorld.TickDelta) collisions++;
        }
        string Side(Faction f) => string.Join(" ", ships.Where(s => s.Faction == f)
            .Select(s => $"{s.Callsign}:{(s.Damage.Destroyed ? "X" : $"{s.Damage.Modules.Count(m => m.Destroyed)}")}"));
        Require(firstContact > 0, "The two fleets must engage within 20 minutes");
        Console.WriteLine($"Full battle 20 min: first damage {firstContact / 60:0.0} min, collisions {collisions}");
        Console.WriteLine($"  blue {Side(Faction.Blue)} | red {Side(Faction.Red)}  (X = destroyed, n = modules lost)");
        foreach (Faction f in new[] { Faction.Blue, Faction.Red })
            Console.WriteLine($"  {f}: missiles {missiles[f]} (hit {hits.GetValueOrDefault((f, true))}, shot down {intercepted[f]}), railgun {rails[f]} (hit {hits.GetValueOrDefault((f, false))})");
    }
}
