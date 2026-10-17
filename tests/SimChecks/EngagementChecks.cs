using System.Reflection;
using Godot;
using SpaceFleet.Sim;

static class EngagementChecks
{
    private static int checks;
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); checks++; }
    private static object? Call(SimWorld world, string method, params object[] args) =>
        typeof(SimWorld).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(world, args);
    public static void Run()
    {
        Approach(); Eccm(); Commitment(); Arming();
        Console.WriteLine($"PASS: {checks} engagement approach/commitment/arming checks");
    }
    private static void Approach()
    {
        foreach (double offset in new[] { 0d, 1e12 })
        {
            var w = new SimWorld();
            var ship = w.Add(new ShipBody("BB", ShipDefinitions.ById("battleship"), Faction.Blue));
            var target = w.Add(new ShipBody("TARGET", ShipDefinitions.ById("mars_battleship"), Faction.Red));
            ship.Place(new(offset, offset, offset), Quaternion.Identity);
            target.Place(ship.Position + new Vec3d(80_000, 0, 0), Quaternion.Identity);
            var brain = w.AttachBrain(ship, ShipOrder.AttackOn(target));
            brain.CommandStandoff = 120_000;
            var track = new SensorTrack(TrackLevel.Identified, 4, 4, 0, 80_000, 100, target.Position);
            Vector3 Approach(SensorTrack t) => (Vector3)Call(w, "Standoff", brain, target, t)!;
            Check(Approach(track).X > 0, "Unresolved lock closes inside commander missile band");
            Check(Approach(track with { EstimatedPosition = ship.Position + new Vec3d(150_000, 0, 0) }).X > ship.Class.MaxSpeed,
                "Distant approach actually requests boost speed, not only a boost flag");
            Check(Approach(track with { Level = TrackLevel.Locked }).X < 0, "Valid lock preserves commander standoff");
            Vector3 observed = Approach(track);
            target.Place(ship.Position + new Vec3d(-500_000, 0, 0), Quaternion.Identity);
            Check(Approach(track) == observed, "Approach never reads an unobserved enemy position");
            brain.CommandStandoff = 0;
            Check(Approach(track).X > 0 && brain.WantBoost, "Explicit approach posture still closes with boost");
        }
    }
    private static void Commitment()
    {
        var (w, wing, anchor, target) = BranchTrials.Fixture("mars_assault_t3");
        w.OrderWing(wing, null);
        var points = wing.Members.Select(s => w.BrainOf(s)!.Order.Point).ToArray();
        Missile Incoming() => new Missile { Id = 99, Shooter = target, Target = anchor,
            Definition = ShipDefinitions.ById("battleship").Missiles!,
            Position = anchor.Position + new Vec3d(0, 0, -10_000), Velocity = new(0, 0, 1000), Health = 10 };
        var missile = Incoming();
        void Observe(bool incoming) => w.Sensors.Update(w.Ships, w.Time, true, w.Probes,
            incoming ? new[] { missile } : Array.Empty<Missile>());
        Observe(true); w.OrderWing(wing, null);
        Check(wing.Mission == InterceptorMission.AirDefense, "Uncommitted wing answers an incoming missile");
        (w, wing, anchor, target) = BranchTrials.Fixture("mars_assault_t3");
        missile = Incoming(); missile.Position = anchor.Position + new Vec3d(0, 0, 1000); missile.Velocity = new(0, 0, -1000);
        Observe(true); w.OrderWing(wing, null);
        Check(wing.Rallying && wing.Mission == InterceptorMission.Assault,
            "Already passed missiles cannot recall an uncommitted wing that cannot intercept them");
        // A fresh fixture keeps the threat-observation lifetime independent of this test's clock.
        (w, wing, anchor, target) = BranchTrials.Fixture("mars_assault_t3");
        w.OrderWing(wing, null);
        for (int i = 0; i < 3; i++) wing.Members[i].Place(points[i], Quaternion.Identity);
        w.OrderWing(wing, null);
        Check(wing.AssaultCommittedTarget == target && !wing.Rallying, "Ready wing commits on departure");
        foreach (var member in wing.Members) member.Place(member.Position + new Vec3d(0, 0, -15_000), Quaternion.Identity);
        missile = Incoming();
        Observe(true); w.OrderWing(wing, null);
        Check(wing.Mission == InterceptorMission.Assault && !wing.Rallying
            && wing.Members.All(s => w.BrainOf(s)!.Target == target), "Home-fleet missile alert does not recall a departed assault");
        target.Place(new(500_000, 0, 500_000), Quaternion.Identity);
        w.OrderWing(wing, null);
        Check(wing.AssaultCommittedTarget == target, "Unobserved target motion cannot cancel commitment");
        w.OrderWing(wing, InterceptorMission.Return);
        Check(wing.AssaultCommittedTarget is null && wing.Members.All(s => w.BrainOf(s)!.Order.Kind == OrderKind.Escort),
            "Player recall immediately overrides commitment");
        w.OrderWing(wing, InterceptorMission.Assault, target); w.OrderWing(wing, null);
        foreach (var member in wing.Members.Take(3)) member.Damage.Catastrophe(w.Time, "test losses");
        w.OrderWing(wing, null);
        Check(wing.Mission == InterceptorMission.Return && wing.AssaultCommittedTarget is null,
            "Heavy wing losses abort an automatic commitment");
        (w, wing, anchor, target) = BranchTrials.Fixture("mars_assault_t3");
        w.OrderWing(wing, InterceptorMission.Assault, target);
        foreach (var member in wing.Members) member.Ordnance.Antimatter.Jettison();
        w.OrderWing(wing, null);
        Check(wing.AssaultCommittedTarget is null && wing.Mission != InterceptorMission.Assault,
            "No remaining payload ends the automatic assault");
        (w, wing, anchor, target) = BranchTrials.Fixture("mars_assault_t3");
        w.OrderWing(wing, InterceptorMission.Assault, target); w.OrderWing(wing, null);
        target.Damage.Catastrophe(w.Time, "test target loss");
        w.Sensors.Update(w.Ships, w.Time, true); w.OrderWing(wing, null);
        Check(wing.AssaultCommittedTarget is null, "Observed target loss releases commitment");
    }
    private static void Eccm()
    {
        var w = new SimWorld();
        var ship = w.Add(new ShipBody("BB", ShipDefinitions.ById("battleship"), Faction.Blue));
        var enemy = w.Add(new ShipBody("JAMMER", ShipDefinitions.ById("mars_battleship"), Faction.Red));
        var brain = w.AttachBrain(ship, ShipOrder.AttackOn(enemy));
        ship.Power.SetPips(2, 2, 1, 1, 2); enemy.Power.SetPips(2, 1, 1, 2, 2);
        enemy.Place(new(0, 0, -110_000), Quaternion.Identity); w.Sensors.Update(w.Ships, w.Time, true);
        Check(w.Sensors.Track(Faction.Blue, enemy).Level < TrackLevel.Locked, "ECCM fixture starts without a lock");
        Call(w, "AllocatePower", brain);
        Check(brain.Eccm && ship.Power.Pips(PowerChannel.Sensors) == 4, "Jammed approach allocates sensor power");
        enemy.Place(new(0, 0, -40_000), Quaternion.Identity); w.Sensors.Update(w.Ships, w.Time, true);
        Check(w.Sensors.Track(Faction.Blue, enemy).Level == TrackLevel.Locked, "ECCM fixture acquires a firing lock");
        Call(w, "AllocatePower", brain);
        Check(brain.Eccm && ship.Power.Pips(PowerChannel.Sensors) == 4, "Acquiring the lock does not switch off its ECCM support");
        enemy.Power.SetPips(2, 2, 2, 2, 0); w.Sensors.Update(w.Ships, w.Time, true);
        Call(w, "AllocatePower", brain);
        Check(!brain.Eccm && ship.Power.Pips(PowerChannel.Sensors) == brain.DefaultPips[3], "Unjammed engagement returns to normal power");
    }
    private static void Arming()
    {
        var (w, ship, target) = AntimatterChecks.Fixture(7500);
        target.Place(target.Position, new Quaternion(Vector3.Up, Mathf.Pi));
        var brain = w.AttachBrain(ship, ShipOrder.AttackOn(target));
        var state = ship.Ordnance.Antimatter;
        var track = new SensorTrack(TrackLevel.Identified, 4, 4, 0, 7500, 100, target.Position)
            { ObservedOrientation = target.Orientation };
        void Engage(SensorTrack t) => Call(w, "EngageAntimatter", brain, target, t);
        Engage(track);
        Check(state.Mode == AntimatterMode.Arming, "Identification prepares AM before the firing window even outside the stern cone");
        state.Step(1); Engage(track with { Level = TrackLevel.Locked });
        Engage(track); Check(state.ArmingElapsed == 1, "Identified/locked transitions do not restart preparation");
        state.Step(1.5); Engage(track);
        Check(state.Ready && state.Launches == 0, "Armed payload cannot fire without a lock");
        Engage(track with { EstimatedPosition = new(0, 0, -9000) });
        Check(state.Ready, "Distance hysteresis retains readiness after briefly leaving the preparation band");
        Engage(track with { EstimatedPosition = new(0, 0, -11000) });
        Check(state.Mode == AntimatterMode.Safe, "Leaving the approach cancels unsafe preparation");
        Engage(track); state.Step(2.5); Engage(track with { Level = TrackLevel.Contact });
        Check(state.Mode == AntimatterMode.Safe, "Losing identification safely cancels preparation");
        Engage(track); state.Step(2.5); brain.BreakUntil = w.Time + 6; Engage(track);
        Check(state.Mode == AntimatterMode.Safe, "Escape phase never arms another payload");
        brain.BreakUntil = -1;
        target.Place(new(0, 0, -4500), target.Orientation); w.Sensors.Update(w.Ships, w.Time, true);
        var close = track with { EstimatedPosition = target.Position, Level = TrackLevel.Locked };
        Engage(close); state.Step(2.5); Engage(close);
        Check(state.Launches == 1 && brain.BreakUntil > w.Time, "Locked close aligned shot fires opportunistically then escapes");
        Check(w.Missiles.Single().LaunchRange <= state.Definition!.RecommendedMaxMeters, "AI retains the short effective firing distance");
    }
}
