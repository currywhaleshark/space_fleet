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
        CheckBattleGroupFormation();
        CheckWingStrike();
        CheckEscortScreen();

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
        var group = new List<ShipBody>();
        foreach (var (name, offset, call) in new[] { ("battleship", new Vec3d(0, 0, -110_000), "RBB"),
                     ("escort", new Vec3d(-2000, 0, -108_500), "RD1"), ("escort", new Vec3d(2000, 0, -108_500), "RD2") })
        {
            ShipBody red = Add(world, name, Faction.Red, offset, call);
            world.AttachBrain(red, ShipOrder.HoldAt(red.Position));
            group.Add(red);
        }
        world.AddSquadron("RED BG", Faction.Red, SquadronRole.BattleGroup, group);
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
        // 주력함 표적은 후미 아래로 우회 침투하므로 첫 돌진까지 시간이 걸린다.
        for (int i = 0; i < 60 * 180; i++)
        {
            world.Step();
            bool now = world.BrainOf(ic)!.Activity == "이탈";
            if (now && !breaking) passes++;
            breaking = now;
        }
        Require(Damaged(target), "An interceptor attack run must damage a stationary escort");
        Require(ic.LastCollision is null, "Attack runs must break off before ramming");
        Require(passes >= 2, $"The interceptor must make repeated passes: {passes}");
        Console.WriteLine($"Attack run (IC vs DD, 25 km, 180 s): {passes} passes, DD shield {target.Damage.Shield:0}/{target.Damage.ShieldCapacity:0}");
    }

    private static void CheckSensorFairness()
    {
        var world = new SimWorld();
        Add(world, "interceptor", Faction.Blue, new Vec3d(0, 0, -100_000), "COLD");
        ShipBody red = Add(world, "battleship", Faction.Red, Vec3d.Zero, "R");
        world.AttachBrain(red, ShipOrder.HoldAt(Vec3d.Zero));
        world.AddSquadron("R", Faction.Red, SquadronRole.BattleGroup, new[] { red });
        world.EnableCommander(Faction.Red);
        for (int i = 0; i < 60 * 30; i++) world.Step();
        Require(world.Missiles.Count == 0 && world.Projectiles.Count == 0 && world.BrainOf(red)!.Order.Kind == OrderKind.Hold,
            "AI must not engage what its sensors cannot see");
    }

    private static void CheckBattleGroupFormation()
    {
        var world = new SimWorld();
        ShipBody bb = Add(world, "battleship", Faction.Blue, Vec3d.Zero, "BB");
        ShipBody d1 = Add(world, "escort", Faction.Blue, new Vec3d(-3000, 0, 2000), "D1");
        ShipBody d2 = Add(world, "escort", Faction.Blue, new Vec3d(3000, 0, 2000), "D2");
        ShipBody enemy = Add(world, "battleship", Faction.Red, new Vec3d(0, 0, -100_000), "E");
        foreach (ShipBody s in new[] { bb, d1, d2 }) world.AttachBrain(s, ShipOrder.HoldAt(s.Position));
        world.AddSquadron("BG", Faction.Blue, SquadronRole.BattleGroup, new[] { bb, d1, d2 });
        world.EnableCommander(Faction.Blue);
        for (int i = 0; i < 60 * 120; i++) world.Step();
        Require(world.BrainOf(bb)!.Order is { Kind: OrderKind.Attack } o && o.Target == enemy, "The battle group flagship must attack the enemy battleship");
        foreach (ShipBody d in new[] { d1, d2 })
        {
            ShipBrain brain = world.BrainOf(d)!;
            Require(brain.Order.Kind == OrderKind.Escort && brain.Order.Target == bb && brain.Target == enemy,
                "Battle group escorts must hold formation on the flagship and fire at its target");
            Require((d.Position - bb.Position).Length() < 4000, $"Battle group escorts must stay close: {(d.Position - bb.Position).Length():0} m");
        }
    }

    private static void CheckWingStrike()
    {
        var world = new SimWorld();
        ShipBody flagship = Add(world, "battleship", Faction.Blue, Vec3d.Zero, "BB");
        world.AttachBrain(flagship, ShipOrder.HoldAt(Vec3d.Zero));
        world.AddSquadron("BG", Faction.Blue, SquadronRole.BattleGroup, new[] { flagship });
        var wing = Enumerable.Range(0, 5).Select(i => Add(world, "interceptor", Faction.Blue, new Vec3d(-2000 + i * 1000, 500, -3000), $"I{i}")).ToList();
        foreach (ShipBody s in wing) world.AttachBrain(s, ShipOrder.HoldAt(s.Position));
        Squadron squadron = world.AddSquadron("WING", Faction.Blue, SquadronRole.InterceptorWing, wing);
        ShipBody target = Add(world, "escort", Faction.Red, new Vec3d(0, 0, -30_000), "T", pointDefense: false);
        world.EnableCommander(Faction.Blue);
        bool gathered = false, struck = false;
        for (int i = 0; i < 60 * 90 && !struck; i++)
        {
            world.Step();
            gathered |= squadron.Activity == "집결";
            struck = squadron.Activity == "돌격";
        }
        Require(gathered && struck, "The wing must gather before striking");
        Require(wing.All(s => world.BrainOf(s)!.Order is { Kind: OrderKind.Attack } o && o.Target == target),
            "All five interceptors must strike the same target together");
        for (int i = 0; i < 60 * 60; i++) world.Step();
        Require(Damaged(target), "The wing strike must damage its target");

        // 적 요격함이 나타나면 요격으로 전환해 나눠 맡는다.
        var raiders = Enumerable.Range(0, 2).Select(i => Add(world, "interceptor", Faction.Red, new Vec3d(-20_000 + i * 2000, 0, -10_000), $"R{i}")).ToList();
        for (int i = 0; i < 60 * 4; i++) world.Step();
        Require(squadron.Activity == "요격", "The wing must switch to intercepting enemy interceptors");
        Require(raiders.All(r => wing.Any(s => world.BrainOf(s)!.Target == r)), "Interceptions must be spread over the raiders");
    }

    private static void CheckEscortScreen()
    {
        var world = new SimWorld();
        ShipBody flagship = Add(world, "battleship", Faction.Blue, Vec3d.Zero, "BB");
        world.AttachBrain(flagship, ShipOrder.HoldAt(Vec3d.Zero));
        world.AddSquadron("BG", Faction.Blue, SquadronRole.BattleGroup, new[] { flagship });
        var escorts = Enumerable.Range(0, 4).Select(i => Add(world, "escort", Faction.Blue, new Vec3d(-8000 - i * 1200, 0, -2000), $"D{i}")).ToList();
        foreach (ShipBody s in escorts) world.AttachBrain(s, ShipOrder.HoldAt(s.Position));
        Squadron squadron = world.AddSquadron("ES", Faction.Blue, SquadronRole.EscortSquadron, escorts);
        ShipBody enemy = Add(world, "escort", Faction.Red, new Vec3d(0, 0, -120_000), "E");
        world.EnableCommander(Faction.Blue);
        double widest = 0;
        for (int i = 0; i < 60 * 240; i++)
        {
            world.Step();
            if (squadron.Activity == "측면 기동")
                widest = Math.Max(widest, Math.Abs(escorts[0].Position.X));
        }
        Require(widest > 15_000, $"The escort squadron must swing out to the flank: {widest / 1000:0} km");
        Require(world.BrainOf(escorts[0])!.Target == enemy, "Flanking escorts must keep their target");

        // 적 요격함이 기함에 붙으면 방공으로 돌아온다.
        Add(world, "interceptor", Faction.Red, new Vec3d(0, 0, -12_000), "RAID");
        for (int i = 0; i < 60 * 4; i++) world.Step();
        Require(squadron.Activity == "방공" && escorts.All(s => world.BrainOf(s)!.Order.Target == flagship),
            "Escorts must fall back to screen the flagship against raiders");
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

}
