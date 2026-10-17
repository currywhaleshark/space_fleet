using Godot;
using System.Text.Json.Nodes;
using SpaceFleet.Sim;

static class EngagementTimingChecks
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        (SimWorld World, Squadron Wing, ShipBody Target) Fixture(DesignFamily design, float reach, double offset = 0)
        {
            var world = new SimWorld();
            var hull = ShipDefinitions.For(HullKind.Interceptor, design);
            var node = JsonNode.Parse(hull.ToJson())!;
            node["Ai"]!["AssaultReachMeters"] = reach;
            hull = ShipDefinition.Parse(node.ToJsonString());
            var anchorNode = JsonNode.Parse(ShipDefinitions.For(HullKind.Battleship, design).ToJson())!;
            anchorNode["Sensors"]!["Strength"] = 20000;
            var anchor = world.Add(new ShipBody("ANCHOR", ShipDefinition.Parse(anchorNode.ToJsonString()), Faction.Blue));
            anchor.Place(new(offset, offset, offset), Quaternion.Identity);
            anchor.Power.SetPips(2, 1, 1, 4, 0);
            var ship = world.Add(new ShipBody("WING", hull, Faction.Blue));
            ship.Place(anchor.Position + new Vec3d(3000, 0, -7000), Quaternion.Identity);
            world.AttachBrain(ship, ShipOrder.HoldAt(ship.Position));
            var target = world.Add(new ShipBody("TARGET", ShipDefinitions.ById("battleship"), Faction.Red));
            target.Place(anchor.Position + new Vec3d(0, 0, -130000), new Quaternion(Vector3.Up, Mathf.Pi));
            target.Power.SetPips(2, 2, 2, 2, 0);
            var wing = world.AddSquadron("WING", Faction.Blue, SquadronRole.InterceptorWing, new[] { ship });
            world.Sensors.Update(world.Ships, 0, force: true);
            return (world, wing, target);
        }

        foreach (var design in Enum.GetValues<DesignFamily>())
        foreach (double offset in new[] { 0d, 1e12 })
        {
            var (world, wing, target) = Fixture(design, 0, offset);
            Check(world.Sensors.Track(Faction.Blue, target).Level >= TrackLevel.Identified, "Identified planning fixture");
            world.OrderWing(wing, null);
            Check(wing.Mission != InterceptorMission.Assault && !wing.AssaultAvailable, "Zero reach preserves legacy planning distance");

            (world, wing, target) = Fixture(design, 170000, offset);
            world.OrderWing(wing, null);
            Check(wing.Mission == InterceptorMission.Assault && wing.AssaultCommittedTarget == target,
                "An observed screen opening may launch a distant approach");
            Check(wing.Leader!.Ordnance.Antimatter.Launches == 0, "Planning reach does not extend torpedo firing range");
            target.Place(target.Position + new Vec3d(5e6, 0, 0), Quaternion.Identity);
            world.OrderWing(wing, null);
            Check(wing.AssaultCommittedTarget == target, "Unobserved movement cannot change the decision");
            world.OrderWing(wing, InterceptorMission.Return);
            Check(wing.Mission == InterceptorMission.Return && wing.AssaultCommittedTarget is null,
                "Explicit recall overrides distant commitment");

            (world, wing, target) = Fixture(design, 170000, offset);
            target.Place(target.Position + new Vec3d(5e6, 0, 0), Quaternion.Identity);
            world.Sensors.Update(world.Ships, 2, force: true);
            world.OrderWing(wing, null);
            Check(!wing.AssaultAvailable && wing.AssaultCommittedTarget is null, "An unknown distant target cannot start an assault");

            (world, wing, target) = Fixture(design, 170000, offset);
            var sector = new Vector3(0, -.5f, 1).Normalized();
            var entry = target.Position + Vec3d.From(target.Orientation * sector) * 17000;
            for (int i = 0; i < 2; i++)
            {
                var guard = world.Add(new ShipBody("GUARD" + i, ShipDefinitions.ById("escort"), Faction.Red));
                guard.Place(entry + new Vec3d(i * 1000, 0, 0), Quaternion.Identity);
                guard.Power.SetPips(2, 2, 2, 2, 0);
            }
            // Explicit target selection uses the same observed capital while automatic safety gates still apply.
            wing.MissionTarget = target;
            world.Sensors.Update(world.Ships, 0, force: true);
            Check(world.Ships.Where(s => s.Callsign.StartsWith("GUARD")).All(s =>
                world.Sensors.Track(Faction.Blue, s).Level >= TrackLevel.Identified), "Escort screen is actually observed");
            world.OrderWing(wing, null, target);
            Check(!wing.AssaultAvailable && wing.Mission != InterceptorMission.Assault, "Observed escorts still close the approach");
            world.EnableCommander(Faction.Blue);
            for (int tick = 0; tick < 180 * 60; tick++) world.Step();
            Check(wing.Members.All(s => s.Ordnance.Interceptor.FirstAssault is null)
                && world.Ships.Where(s => s.Callsign.StartsWith("GUARD")).All(s =>
                    world.Sensors.Track(Faction.Blue, s).Level >= TrackLevel.Identified && !s.Damage.Destroyed),
                "The extended planning range cannot turn into a timed rush through an observed intact screen");
            world.OrderWing(wing, InterceptorMission.Assault, target);
            Check(wing.Mission == InterceptorMission.Assault, "Player can explicitly order a risky approach");
        }
        var definition = ShipDefinitions.ById("interceptor");
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity })
        {
            bool rejected = false;
            try
            {
                var node = JsonNode.Parse(definition.ToJson())!;
                node["Ai"]!["AssaultReachMeters"] = invalid;
                ShipDefinition.Parse(node.ToJsonString());
            }
            catch (System.IO.InvalidDataException) { rejected = true; }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "Invalid planning reach must be rejected");
        }
        Console.WriteLine($"PASS: {checks} engagement timing/planning checks");
    }
}
