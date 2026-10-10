using Godot;
using SpaceFleet.Sim;

static class BattleChecks
{
    private static int _checks;
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); _checks++; }
    private static void Step(SimWorld world, double seconds) { for (int i = 0; i < seconds * 60; i++) world.Step(); }
    public static void Run()
    {
        CheckSetup(); CheckRules(); CheckPhases(); CheckSalvo(); CheckPostures(); CheckReplacement(); CheckLogBounds(); CheckDeterminism(); CheckFullBattle(); CheckBrawlStatistics();
        Console.WriteLine($"PASS: {_checks} battle/setup/log/rules checks");
    }
    private static SimWorld Battle(int seed = 0, bool mirror = false)
    { var world = new SimWorld(); BattleSetup.Spawn(world, new BattleConfig { Seed = seed, Mirror = mirror }); return world; }
    private static void CheckSetup()
    {
        var a = Battle(); var b = Battle(1); var c = Battle(1); var flipped = Battle(1, true);
        Require(a.Ships.Count == 24 && a.Squadrons.Count == 6, "Canonical battle roster");
        Require(a.Ships.Any(s => s.Callsign == "DD-31") && a.Ships.Any(s => s.Callsign == "IC-21"), "Game callsigns must be canonical");
        Require(a.Ships[0].Position != b.Ships[0].Position, "Nonzero seed must jitter placement");
        Require(b.Ships.Zip(c.Ships).All(p => p.First.Position == p.Second.Position && p.First.Orientation == p.Second.Orientation), "Seed must repeat exact poses");
        Require(b.Ships.Zip(flipped.Ships).All(p => p.First.Faction == p.Second.Faction && p.First.Callsign == p.Second.Callsign), "Mirror must preserve faction/callsign identity");
        Require(flipped.Ships[0].Position == b.Ships[12].Position && flipped.Ships[0].Orientation == b.Ships[12].Orientation, "Mirror must swap spatial poses");
        Require((b.Ships[1].Position - b.Ships[0].Position) == (a.Ships[1].Position - a.Ships[0].Position), "Squad jitter must preserve internal formation");
    }
    private static void Kill(ShipBody ship) => ship.Damage.Breakup(0, "RULE TEST");
    private static void CheckRules()
    {
        var world = Battle();
        Require(world.Rules!.Initial(Faction.Blue) == 20.5 && world.Rules.Initial(Faction.Red) == 20.5, "Starting strength must equal 20.5");
        Require(world.Rules.Evaluate(0) is null, "Healthy battle must not be over");
        Kill(world.Ships[0]);
        Require(world.Rules.Evaluate(1) is null, "Flagship alone is insufficient for defeat");
        foreach (var s in world.Ships.Where(s => s.Faction == Faction.Blue && s.Class.Kind == HullKind.Escort).Take(3)) Kill(s);
        Require(world.Rules.Evaluate(2)?.Winner == Faction.Red, "Flagship plus half strength must lose");
        Require(world.Rules.Evaluate(1500)?.Time == 2, "Outcome must stay latched");
        world = Battle();
        foreach (Faction f in Enum.GetValues<Faction>())
        {
            foreach (var s in world.Ships.Where(s => s.Faction == f && s.Class.Kind == HullKind.Battleship)) Kill(s);
            foreach (var s in world.Ships.Where(s => s.Faction == f && s.Class.Kind == HullKind.Escort).Take(3)) Kill(s);
        }
        Require(world.Rules!.Evaluate(5) is { Winner: null, Reason: "동시 전력 상실" }, "Same-tick defeat must draw");
        world = Battle();
        Require(world.Rules!.Evaluate(1499) is null && world.Rules.Evaluate(1500) is { Winner: null }, "Time limit/equal-strength draw");
        world = Battle(); Kill(world.Ships.First(s => s.Faction == Faction.Red && s.Class.Kind == HullKind.Escort));
        Require(world.Rules!.Evaluate(1500) is { Winner: null }, "Difference within 10 percentage points must draw");
        world = Battle();
        foreach (var s in world.Ships.Where(s => s.Faction == Faction.Red && s.Class.Kind == HullKind.Escort).Take(2)) Kill(s);
        Require(world.Rules!.Evaluate(1500)?.Winner == Faction.Blue, "Time limit must reward higher remaining fraction");
        world = new SimWorld();
        foreach (Faction f in Enum.GetValues<Faction>()) for (int i = 0; i < 4; i++) world.Add(new ShipBody($"{f}{i}", ShipClass.Escort, f));
        world.Rules = new BattleRules(world);
        foreach (var s in world.Ships.Where(s => s.Faction == Faction.Blue).Take(3)) Kill(s);
        Require(world.Rules.Evaluate(0)?.Winner == Faction.Red, "Exactly 25% must lose without flagship rule");
    }
    private static void CheckPhases()
    {
        foreach (var (range, weapon, expected) in new[] { (20_000, BattleWeapon.Missile, BattlePhase.Missile),
                     (50_000, BattleWeapon.Railgun, BattlePhase.Gunnery), (10_000, BattleWeapon.Railgun, BattlePhase.Brawl) })
        {
            var world = new SimWorld();
            var a = world.Add(new ShipBody("A", ShipClass.Escort, Faction.Blue));
            var b = world.Add(new ShipBody("B", ShipClass.Escort, Faction.Red));
            a.Place(Vec3d.Zero, Quaternion.Identity); b.Place(new Vec3d(0, 0, -range), Quaternion.Identity);
            world.Sensors.Update(world.Ships, 0, force: true); world.Log = new BattleLog(world);
            bool fired = weapon == BattleWeapon.Missile ? world.LaunchMissile(a, b).Fired : world.FireRailgun(a, a.Forward).Fired;
            Require(fired, "Phase fixture weapon must fire");
            Step(world, 30);
            Require(world.Log.Intervals[0].Phase == expected, $"Phase classification {expected}");
            Require(weapon == BattleWeapon.Missile ? world.Log.Side(Faction.Blue).Missiles == 1 : world.Log.Side(Faction.Blue).Rails == 2,
                "Direct weapon log counts every physical round once, including both twin barrels");
        }
        var scene = Battle();
        foreach (var brain in scene.Brains.Values) brain.Enabled = false;
        Require(scene.FireRailgun(scene.Ships[0], scene.Ships[0].Forward).Shots == 2,
            "Mixed-phase fixture includes a real twin rail volley");
        scene.Ships[0].Damage.AbsorbShield(10_000, 0);
        foreach (var module in scene.Ships[0].Damage.Modules.Take(2)) scene.Ships[0].Damage.Hurt(module, module.Health, 1, 0, 1);
        Step(scene, 30);
        Require(scene.Log!.Intervals[0].Phase == BattlePhase.Sniping, "Unshielded module kills take phase priority over simultaneous rail fire");
        Require(scene.Log.Events.Count(e => e.Kind == BattleEventKind.ModuleDestroyed) >= 2, "Module events must be recorded");
    }
    private static void CheckDeterminism()
    {
        string Run() { var world = Battle(1); Step(world, 300); world.Log!.Finish(); return world.Log.Summary(); }
        Require(Run() == Run(), "Same seed must repeat BattleLog summary byte for byte");
    }
    private static void CheckSalvo()
    {
        var world = new SimWorld();
        var a = world.Add(new ShipBody("LEFT", ShipClass.Escort, Faction.Blue));
        var b = world.Add(new ShipBody("RIGHT", ShipClass.Escort, Faction.Blue));
        var c = world.Add(new ShipBody("C", ShipClass.Battleship, Faction.Red));
        var d = world.Add(new ShipBody("D", ShipClass.Battleship, Faction.Red));
        a.Place(Vec3d.Zero, Quaternion.Identity); b.Place(new Vec3d(3000,0,0), Quaternion.Identity);
        c.Place(new Vec3d(0,0,-80_000), Quaternion.Identity); d.Place(new Vec3d(3000,0,-80_000), Quaternion.Identity);
        world.AttachBrain(a, ShipOrder.HoldAt(a.Position, c)); world.AttachBrain(b, ShipOrder.HoldAt(b.Position, d));
        world.Sensors.Update(world.Ships, 0, force:true); world.Log = new BattleLog(world);
        Step(world, 3);
        Require(world.Log.Ship(a).Missiles > 0 && world.Log.Ship(b).Missiles > 0, "Separate-target salvos must not wait for one another");
        Require(world.BrainOf(a)!._side == new ShipBrain(c, ShipOrder.HoldAt(c.Position), 0)._side, "Tactics must depend on fleet slot, not callsign/faction");
    }
    /// <summary>
    /// 난전 발생은 여러 판 통계로 검사한다. 시드 1~6 정방향 6판 중 절반 이상에서 난전 국면이 나와야 한다
    /// (20판 기준 17판에서 발생). 포격만으로 끝나는 판은 허용하되, 근접전이 사라지는 회귀를 잡는다.
    /// </summary>
    private static void CheckBrawlStatistics()
    {
        int[] seeds = { 1, 2, 3, 4, 5, 6 };
        var brawl = new double[seeds.Length];
        var outcome = new double[seeds.Length];
        Parallel.For(0, seeds.Length, i =>
        {
            var world = Battle(seeds[i]);
            while (world.Time < 1500 && world.Rules!.Outcome is null) world.Step();
            world.Log!.Finish();
            brawl[i] = world.Log.PhaseSeconds(BattlePhase.Brawl);
            outcome[i] = world.Time;
        });
        int withBrawl = brawl.Count(b => b > 0);
        Console.WriteLine($"Brawl statistics (seeds 1-6): {withBrawl}/{seeds.Length} battles, brawl s = "
            + string.Join(" ", brawl.Select(b => b.ToString("0"))) + ", outcome s = " + string.Join(" ", outcome.Select(o => o.ToString("0"))));
        Require(withBrawl * 2 >= seeds.Length, $"Brawl must appear in at least half of the sampled battles: {withBrawl}/{seeds.Length}");
    }

    private static void CheckFullBattle()
    {
        var world = Battle(); var activities = new HashSet<string>();
        Step(world, 0);
        while (world.Time < 1500 && world.Rules!.Outcome is null)
        {
            world.Step();
            if (world.Tick % 120 == 0) foreach (var squad in world.Squadrons) activities.Add($"{squad.Role}:{squad.Activity}");
        }
        world.Log!.Finish();
        // 실패해도 어느 국면이 빠졌는지 보이도록 요약을 먼저 찍는다.
        Console.WriteLine($"Canonical battle: {world.Log.Summary()}");
        Require(world.Log.Side(Faction.Blue).RailHit is not null || world.Log.Side(Faction.Red).RailHit is not null, "Full battle must engage");
        // Twin volleys can turn every gunfire interval into the higher-priority subsystem-damage phase.
        Require(world.Log.PhaseSeconds(BattlePhase.Gunnery) + world.Log.PhaseSeconds(BattlePhase.Sniping) > 0,
            "Full battle must record gunfire, including intervals classified as subsystem strikes");
        // 난전은 한 판에서 반드시 나오는 국면이 아니다(포격으로 먼저 판정 날 수 있다). 여러 판 통계로 본다(CheckBrawlStatistics).
        foreach (BattlePhase expected in new[] { BattlePhase.Missile, BattlePhase.Sniping })
            Require(world.Log.PhaseSeconds(expected) > 0, $"Full battle phase must appear: {expected}");
        Require(world.Rules!.Outcome is not null, "Battle must have an outcome by time limit");
        Console.WriteLine($"  destroyed/disabled {world.Ships.Count(s => s.Damage.Destroyed || s.Damage.Disabled)}/24");
        var outcome=world.Rules.Outcome;double duration=world.Log.Intervals.Sum(i=>i.Duration);
        long outcomeTick=world.Tick;
        int intervals=world.Log.Intervals.Count, strength=world.Log.Strength.Count;
        Step(world,300);
        // Tick/60 and outcome.Time+300 can differ by one double ULP at non-integer outcomes.
        Require(world.Rules.Outcome==outcome&&world.Tick==outcomeTick+(long)(300*SimWorld.TickRate),"Battle continues for five minutes with latched outcome");
        Require(world.Log.Intervals.Count==intervals&&world.Log.Intervals.Sum(i=>i.Duration)==duration&&world.Log.Strength.Count==strength,"Result history stays bounded and frozen");
        Require(world.Log.Events.Count<=BattleLog.EventCapacity,"Battle event cap after five minutes");
        Require(world.Impacts.Count<=64&&world.Impacts.All(e=>world.Time-e.Time<=3.001),"Impact list prunes old render events");
        Require(world.OrdnanceEvents.All(e=>world.Time-e.Time<=3.001),"Ordnance list prunes old render events");
    }
    private static void CheckPostures()
    {
        var doctrine = new FleetDoctrine { GunlineMinimumSeconds=180 }; var state = new FleetState();
        var facts = new FleetFacts(100_000, 1, 20.5, 20.5, 1);
        state.Update(29, doctrine, facts); Require(state.Posture == FleetPosture.Approach, "Minimum 30s hold");
        state.Update(30, doctrine, facts); Require(state.Posture == FleetPosture.Missile, "Identified main in band starts missile phase");
        state.Update(59, doctrine, facts with { MissileStock = 0 }); Require(state.Posture == FleetPosture.Missile, "Hold before ammo transition");
        state.Update(60, doctrine, facts with { MissileStock = 0.35f }); Require(state.Posture == FleetPosture.Gunline, "Ammo threshold starts gunline");
        state.Update(90, doctrine, facts with { KnownFlagshipPropulsion = 0.5f }); Require(state.Posture == FleetPosture.Gunline, "Gunline minimum hold before close");
        state.Update(240, doctrine, facts with { KnownFlagshipPropulsion = 0.5f }); Require(state.Posture == FleetPosture.Close, "Known flagship half propulsion starts close");
        state.Update(270, doctrine, facts); Require(state.Posture == FleetPosture.Close, "No regression from close");
        state.Update(300, doctrine, facts with { OwnStrength = 8.2 }); Require(state.Posture == FleetPosture.Withdraw, "0.4 strength ratio withdraws");
        state.Update(330, doctrine, facts); Require(state.Posture == FleetPosture.Withdraw, "Withdrawal must remain stable");
        var timeout = new FleetState(); timeout.Update(30, doctrine, facts);
        timeout.Update(270, doctrine, facts); Require(timeout.Posture == FleetPosture.Gunline, "Missile timeout");
        timeout.Update(630, doctrine, facts); Require(timeout.Posture == FleetPosture.Close, "Gunline timeout");
        var unseen = new FleetState(); unseen.Update(30, doctrine, facts with { IdentifiedMainRange = double.PositiveInfinity, KnownEnemyStrength = 0 });
        Require(unseen.Posture == FleetPosture.Approach, "No omniscient transition before identification");
        var world = Battle(); var squad = world.Squadrons[0]; squad.PlayerLed = true;
        var leader = squad.Leader!; var order = world.BrainOf(leader)!.Order;
        Step(world, 60); Require(world.BrainOf(leader)!.Order == order, "Commander must not force player-led squad posture orders");
    }
    private static void CheckReplacement()
    {
        var world=Battle();
        ShipBody ic=BattleRules.Replacement(world.Ships,Faction.Blue,HullKind.Interceptor)!;
        Require(ic.Class.Kind==HullKind.Interceptor&&ic.Faction==Faction.Blue,"Prefer surviving same class/faction");
        foreach(var ship in world.Ships.Where(s=>s.Faction==Faction.Blue&&s.Class.Kind==HullKind.Interceptor))Kill(ship);
        Require(BattleRules.Replacement(world.Ships,Faction.Blue,HullKind.Interceptor)?.Class.Kind==HullKind.Battleship,"Fallback to largest living class");
        foreach(var ship in world.Ships.Where(s=>s.Faction==Faction.Blue))Kill(ship);
        Require(BattleRules.Replacement(world.Ships,Faction.Blue,HullKind.Interceptor) is null,"No survivor means spectate");
        world=Battle();Step(world,5);world.Log!.CaptureOutcome(world.Time);
        Require(world.Log.OutcomeIntervals!.Sum(i=>i.Duration)==5,"Outcome snapshot includes partial final interval");
        Step(world,30);Require(world.Log.OutcomeIntervals!.Sum(i=>i.Duration)==5,"Post-outcome simulation must not change result timeline");
    }
    private static void CheckLogBounds()
    {
        var world=new SimWorld();var ship=world.Add(new ShipBody("LOG",ShipClass.Escort,Faction.Blue));world.Log=new BattleLog(world);
        Step(world,5);world.Log.Finish();world.Log.Finish();
        Require(world.Log.Intervals.Count==1&&world.Log.Intervals[0].Duration==5,"Finish must be idempotent at a partial interval");
        Step(world,26);world.Log.Finish();
        Require(world.Log.Intervals.Count==2&&world.Log.Intervals.Sum(i=>i.Duration)==31,"Continuing after Finish replaces the partial interval");
        world.Log.CaptureOutcome(world.Time);
        Require(world.Log.OutcomeIntervals!.Sum(i=>i.Duration)==31,"Outcome after Finish must not duplicate the partial interval");
        for(int cycle=0;cycle<BattleLog.EventCapacity&&world.Log.DroppedEvents==0;cycle++)
        {
            ship.Damage.Reset();Step(world,1);
            foreach(var module in ship.Damage.Modules)ship.Damage.Hurt(module,module.Health,world.Time,0,(uint)cycle);
            Step(world,1);
        }
        Require(world.Log.Events.Count==BattleLog.EventCapacity&&world.Log.DroppedEvents>0,"Repeated repairs and destruction respect the event cap");
        Require(world.Log.Events[0].Time<world.Log.Events[^1].Time,"Capped result events preserve the original battle sequence");
    }
}
