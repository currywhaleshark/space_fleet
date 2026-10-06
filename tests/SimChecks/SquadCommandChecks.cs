using Godot;
using SpaceFleet.Game;
using SpaceFleet.Sim;

static class SquadCommandChecks
{
    public static void Run()
    {
        int checks = 0;
        void Require(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        var world = new SimWorld();
        ShipBody Add(string id, ShipClass cls, Faction f, Vec3d at)
        {
            var ship = world.Add(new ShipBody(id, cls, f)); ship.Place(at, Quaternion.Identity);
            ship.Control = ShipControl.Idle; return ship;
        }
        var leader = Add("L", ShipClass.Battleship, Faction.Blue, Vec3d.Zero);
        var a = Add("A", ShipClass.Escort, Faction.Blue, new Vec3d(5000, 0, 0));
        var b = Add("B", ShipClass.Escort, Faction.Blue, new Vec3d(-5000, 0, 0));
        var other = Add("OTHER", ShipClass.Escort, Faction.Blue, new Vec3d(0, 5000, 0));
        world.AddSquadron("PLAYER", Faction.Blue, SquadronRole.BattleGroup, new[] { leader, a, b });
        world.AddSquadron("OTHER", Faction.Blue, SquadronRole.EscortSquadron, new[] { other });
        var brainA = world.AttachBrain(a, ShipOrder.HoldAt(a.Position));
        var brainB = world.AttachBrain(b, ShipOrder.HoldAt(b.Position));
        var otherBrain = world.AttachBrain(other, ShipOrder.HoldAt(other.Position));
        var first = Add("IC1", ShipClass.Interceptor, Faction.Red, new Vec3d(0, 0, -10_000));
        var second = Add("IC2", ShipClass.Interceptor, Faction.Red, new Vec3d(1000, 0, -12_000));
        for (int i = 0; i < 60; i++) world.Step();
        SquadCommands.Apply(world, leader, SquadCommand.Intercept);
        Require(brainA.Order.Kind == OrderKind.Attack && brainB.Order.Kind == OrderKind.Attack, "Intercept must attack nearby identified ICs");
        Require(brainA.Target != brainB.Target && new[] { brainA.Target, brainB.Target }.Contains(first)
            && new[] { brainA.Target, brainB.Target }.Contains(second), "Intercept must distribute two threats");
        Require(otherBrain.Order.Kind == OrderKind.Hold, "Commands must affect only player's squad");
        SquadCommands.Apply(world, leader, SquadCommand.Return, first, first);
        Require(brainA.Order.Kind == OrderKind.Escort && brainB.Order.Kind == OrderKind.Escort, "Return must restore escort formation");
        Require(brainA.Target is null && brainB.Target is null && brainA.Order.FireAt is null, "Return must clear attack/fire target");
        double before = (a.Position - (leader.Position + Vec3d.From(brainA.FormationOffset))).Length();
        for (int i = 0; i < 1200; i++) world.Step();
        Require((a.Position - (leader.Position + Vec3d.From(brainA.FormationOffset))).Length() < before, "Return must approach formation slot");
        SquadCommands.Apply(world, leader, SquadCommand.Escort, fireAt: first);
        Require(brainA.Order.FireAt == first, "Escort must share leader's fire target");
        SquadCommands.Apply(world, leader, SquadCommand.Hold);
        Require(brainA.Order.Point == a.Position && brainB.Order.Point == b.Position, "Hold must keep individual positions");
        first.Teleport(new Vec3d(0, 0, -100_000)); second.Teleport(new Vec3d(0, 0, -100_000));
        world.Sensors.Update(world.Ships, world.Time, force: true);
        SquadCommands.Apply(world, leader, SquadCommand.Intercept);
        Require(brainA.Order.Kind == OrderKind.Escort && brainB.Order.Kind == OrderKind.Escort, "Intercept without nearby threats must escort");
        Console.WriteLine($"PASS: {checks} player squad command checks");
    }
}
