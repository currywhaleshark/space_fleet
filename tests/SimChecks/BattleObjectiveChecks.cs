using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

static class BattleObjectiveChecks
{
    private static int _checks;
    private static void Require(bool ok, string name)
    {
        if (!ok) throw new Exception("Battle objective check: " + name);
        _checks++;
    }
    private static SimWorld Setup(BattleObjective objective)
    {
        var world = new SimWorld();
        BattleSetup.Spawn(world, new BattleConfig { Objective = objective });
        foreach (ShipBrain brain in world.Brains.Values) brain.Enabled = false;
        return world;
    }
    private static void Kill(ShipBody s) => s.Damage.Breakup(0, "OBJECTIVE TEST");

    public static void Run()
    {
        var old = Setup(BattleObjective.Annihilation);
        Require(old.Rules!.Objective == BattleObjective.Annihilation, "legacy annihilation default");
        Kill(old.Rules.Flagship(Faction.Blue)!);
        Require(old.Rules.Evaluate(1) is null, "annihilation preserves combined flagship/strength condition");

        var command = Setup(BattleObjective.Flagship);
        Require(command.Rules!.Flagship(Faction.Blue) is { Callsign: "BB-01" }, "flagship identity");
        Kill(command.Rules.Flagship(Faction.Blue)!);
        Require(command.Rules.Evaluate(1) is { Winner: Faction.Red, Reason: "기함 상실" },
            "flagship loss resolves immediately");
        Require(command.Rules.Evaluate(500)!.Time == 1, "flagship result latched");

        var simultaneous = Setup(BattleObjective.Flagship);
        Kill(simultaneous.Rules!.Flagship(Faction.Blue)!);
        Kill(simultaneous.Rules.Flagship(Faction.Red)!);
        Require(simultaneous.Rules.Evaluate(1) is { Winner: null }, "double flagship loss is a draw");

        var zones = BattleRules.DefaultZones(150_000, Vec3d.Zero);
        Require(zones.Length == 3 && zones.All(z => z.RadiusMeters > 0), "three spherical zones");
        Require(zones.Select(z => z.Name).SequenceEqual(new[] { "A", "B", "C" }), "stable labels");
        Vec3d blueStart = Vec3d.Zero, redStart = new(0, 0, -150_000);
        Require(zones.All(z => Math.Abs((z.Position - blueStart).Length() - (z.Position - redStart).Length()) < 1),
            "zones are equidistant from both fleets");
        Require(zones.Max(z => z.Position.X) - zones.Min(z => z.Position.X) >= 100_000
            && zones.Select(z => Math.Round(z.Position.Y)).Distinct().Count() == 3, "zones sit side by side at distinct heights");

        // 응급복구로 무력화를 풀고 있는 기함은 아직 패배가 아니다. 복구 수단이 없으면 즉시 패배.
        var repair = Setup(BattleObjective.Flagship);
        ShipBody repairing = repair.Rules!.Flagship(Faction.Blue)!;
        foreach (var m in repairing.Damage.Modules.Where(m => m.Definition.Kind is ModuleKind.Reactor or ModuleKind.Generator))
            repairing.Damage.Hurt(m, m.Definition.HitPoints, repairing.SimTime, 0, 1);
        repairing.Step(1d / 60);
        Require(repairing.Damage.Disabled && repairing.DamageControl.RecoveringDisable && repair.Rules.Evaluate(1) is null,
            "flagship under emergency repair is not yet lost");
        repairing.DamageControl.Priority = RepairPriority.Hold;
        Require(repair.Rules.Evaluate(2) is { Winner: Faction.Red, Reason: "기함 상실" }, "flagship without recovery is lost");

        var blue = new ShipBody("BLUE", ShipClass.Interceptor, Faction.Blue);
        var red = new ShipBody("RED", ShipClass.Escort, Faction.Red);
        blue.Place(Vec3d.Zero, Quaternion.Identity);
        red.Place(new Vec3d(40_000, 0, 0), Quaternion.Identity);
        var zone = new CaptureZone("TEST", Vec3d.Zero, 10_000);
        zone.Update(new[] { blue, red }, 40);
        Require(zone.Owner == Faction.Blue && zone.Progress == 100, "blue captures zone");
        red.Place(Vec3d.Zero, Quaternion.Identity);
        zone.Update(new[] { blue, red }, 60);
        Require(zone.Contested && zone.ScoringOwner is null, "contested area pauses capture and scoring");
        blue.Place(new Vec3d(40_000, 0, 0), Quaternion.Identity);
        zone.Update(new[] { blue, red }, 60);
        Require(zone.Owner == Faction.Red && zone.Progress == -100, "red can recapture fully");
        red.Place(new Vec3d(40_000, 0, 0), Quaternion.Identity);
        zone.Update(new[] { blue, red }, 10);
        Require(zone.Owner == Faction.Red && zone.ScoringOwner == Faction.Red, "ownership persists on departure");

        var match = new SimWorld();
        var own = match.Add(new ShipBody("B", ShipClass.Interceptor, Faction.Blue));
        var foe = match.Add(new ShipBody("R", ShipClass.Interceptor, Faction.Red));
        own.Place(Vec3d.Zero, Quaternion.Identity);
        foe.Place(new Vec3d(50_000, 0, 0), Quaternion.Identity);
        match.Rules = new BattleRules(match, BattleObjective.Domination,
            new[] { new CaptureZone("C", Vec3d.Zero, 10_000) }, scoreLimit: 2);
        for (int i = 1; i <= 3000 && match.Rules.Outcome is null; i++)
            match.Rules.Evaluate(i * SimWorld.TickDelta);
        Require(match.Rules.Outcome is { Winner: Faction.Blue, Reason: "거점 점수 달성" },
            "score victory after sustained control");
        double score = match.Rules.Score(Faction.Blue);
        match.Rules.Evaluate(1_500);
        Require(match.Rules.Score(Faction.Blue) == score, "score and result freeze after conclusion");

        // 점령 AI는 거점 중심 구조물과 겹치지 않게 자기 쪽으로 물러난 지점에 머문다. 요격 편대 임무가 이 명령을 덮지 않는다.
        var hold = new SimWorld();
        BattleSetup.Spawn(hold, new BattleConfig { Objective = BattleObjective.Domination });
        for (int i = 0; i < 180; i++) hold.Step();
        var holds = hold.Brains.Values.Where(b => b.Enabled && b.Order.Kind == OrderKind.Hold).ToArray();
        Require(holds.Length > 0 && holds.All(b => hold.Rules!.CaptureZones
                .Min(z => (b.Order.Point - z.Position).Length()) >= SimWorld.HoldClearance - 1),
            "capture AI holds clear of the zone structure");
        Require(hold.Squadrons.Where(q => q.Role == SquadronRole.InterceptorWing && !q.PlayerLed).All(q => q.ObjectiveOrders),
            "objective orders flag interceptor wings");

        var mirrored = Setup(BattleObjective.Domination);
        Require(mirrored.Rules!.CaptureZones.Count == 3, "domination setup activates objective zones");
        Console.WriteLine($"PASS: {_checks} battle objective checks");
    }
}
