using System;
using System.Collections.Generic;
using Godot;

namespace SpaceFleet.Sim;

public sealed record BattleConfig
{
    public int Seed { get; init; }
    public double StartDistance { get; init; } = 150_000;
    public Vector3 RedOffset { get; init; } = Vector3.Zero;
    public bool Mirror { get; init; }
    public double PositionJitter { get; init; } = 2_000;
    public float HeadingJitterDegrees { get; init; } = 5;
}

public sealed record BattleRoster(IReadOnlyList<ShipBody> Ships, ShipBody BlueFlagship, ShipBody RedFlagship);

/// <summary>Canonical game/test fleet placement. Jitter belongs to a spatial squad slot, so mirrors swap the same poses.</summary>
public static class BattleSetup
{
    public static BattleRoster Spawn(SimWorld world, BattleConfig config)
    {
        if (world.Ships.Count != 0) throw new ArgumentException("Battle setup requires an empty world");
        if (config.StartDistance <= 0 || !double.IsFinite(config.StartDistance) || !config.RedOffset.IsFinite()
            || config.PositionJitter < 0 || !double.IsFinite(config.PositionJitter) || !float.IsFinite(config.HeadingJitterDegrees))
            throw new ArgumentException("Invalid battle config");
        var random = new Random(config.Seed);
        var offsets = new Vec3d[2, 3];
        var headings = new float[2, 3];
        for (int side = 0; side < 2; side++)
        for (int squad = 0; squad < 3; squad++)
        {
            double Noise() => random.NextDouble() * 2 - 1;
            if (config.Seed == 0) continue;
            offsets[side, squad] = new Vec3d(Noise(), Noise(), Noise()) * config.PositionJitter;
            headings[side, squad] = Mathf.DegToRad((float)Noise() * config.HeadingJitterDegrees);
        }
        var ships = new List<ShipBody>();
        var flagships = new ShipBody[2];
        foreach (Faction faction in new[] { Faction.Blue, Faction.Red })
        {
            bool blue = faction == Faction.Blue;
            int side = (blue ? 0 : 1) ^ (config.Mirror ? 1 : 0);
            double dir = side == 0 ? 1 : -1;
            Vec3d origin = side == 0 ? Vec3d.Zero : Vec3d.From(config.RedOffset) + new Vec3d(0, 0, -config.StartDistance);
            ShipBody Make(string call, ShipClass cls, int squad, double x, double y, double z)
            {
                var ship = world.Add(new ShipBody(call, cls, faction));
                ship.Place(origin + offsets[side, squad] + new Vec3d(x * dir, y, z * dir),
                    new Quaternion(Vector3.Up, (side == 0 ? 0 : Mathf.Pi) + headings[side, squad]));
                ship.Control = ShipControl.Idle;
                world.AttachBrain(ship, ShipOrder.HoldAt(ship.Position));
                ships.Add(ship);
                return ship;
            }
            var bg = new[]
            {
                Make(blue ? "BB-01" : "BB-X1", ShipClass.Battleship, 0, 0, 0, 0),
                Make(blue ? "DD-11" : "DD-X1", ShipClass.Escort, 0, -1500, 150, -600),
                Make(blue ? "DD-12" : "DD-X2", ShipClass.Escort, 0, 1500, -150, -600),
            };
            var es = new ShipBody[4];
            for (int i = 1; i <= es.Length; i++)
                es[i - 1] = Make(blue ? $"DD-3{i}" : $"DD-X{i + 2}", ShipClass.Escort, 1,
                    -9000 + (i % 2 == 0 ? 1 : -1) * 1200 * ((i + 1) / 2), 0, -2000 + 900 * ((i + 1) / 2));
            var ic = new ShipBody[5];
            for (int i = 0; i < ic.Length; i++)
                ic[i] = Make(blue ? $"IC-2{i + 1}" : $"IC-X{i + 1}", ShipClass.Interceptor, 2, 420 + 60 * i, 110 - 25 * i, 850 + 50 * i);
            world.AddSquadron(blue ? "전투단" : "적 전투단", faction, SquadronRole.BattleGroup, bg);
            world.AddSquadron(blue ? "호위 전대" : "적 호위 전대", faction, SquadronRole.EscortSquadron, es);
            world.AddSquadron(blue ? "요격 편대" : "적 요격 편대", faction, SquadronRole.InterceptorWing, ic);
            flagships[(int)faction] = bg[0];
        }
        // Preserve the game's commander registration order in this measurement stage.
        world.Doctrine = new FleetDoctrine();
        world.EnableCommander(Faction.Red);
        world.EnableCommander(Faction.Blue);
        world.Sensors.Update(world.Ships, world.Time, force: true);
        world.Rules = new BattleRules(world);
        world.Log = new BattleLog(world);
        return new BattleRoster(ships.AsReadOnly(), flagships[0], flagships[1]);
    }
}
