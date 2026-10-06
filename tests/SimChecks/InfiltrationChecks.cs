using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Sim;

/// <summary>요격함 침투: 근접방어 사각, 후미 아래 침투 기동, 부위 조준, 방열판.</summary>
static class InfiltrationChecks
{
    private static int _checks;

    public static void Run()
    {
        CheckPointDefenseArcs();
        CheckSubsystemAim();
        CheckRadiators();
        CheckInfiltration();
        Console.WriteLine($"PASS: {_checks} infiltration checks");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    private static ShipDefinition Quiet(string name)
    {
        using var stream = typeof(ShipDefinitions).Assembly.GetManifestResourceStream($"SpaceFleet.data.ships.{name}.json")!;
        using var reader = new StreamReader(stream);
        JsonObject data = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
        foreach (JsonNode? m in data["modules"]!.AsArray()) m!["criticalChance"] = 0;
        return ShipDefinition.Parse(data.ToJsonString());
    }

    private static ShipBody Add(SimWorld world, string name, Faction faction, Vec3d position, string callsign)
    {
        ShipDefinition def = Quiet(name);
        var ship = world.Add(new ShipBody(callsign, def.Flight, faction, def));
        ship.Place(position, Quaternion.Identity);
        ship.Control = ShipControl.Idle;
        return ship;
    }

    private static void CheckPointDefenseArcs()
    {
        int ShotsAt(Vec3d where)
        {
            var world = new SimWorld();
            Add(world, "battleship", Faction.Blue, Vec3d.Zero, "BB");
            ShipBody ic = Add(world, "interceptor", Faction.Red, where, "IC");
            int shots = 0;
            for (int i = 0; i < 60 * 5; i++)
            {
                world.Step();
                shots += world.PointDefenseShots.Count(s => s.Faction == Faction.Blue && Math.Abs(s.Time - world.Time) < SimWorld.TickDelta);
            }
            return shots;
        }
        int above = ShotsAt(new Vec3d(0, 2500, 0));
        int below = ShotsAt(new Vec3d(0, -2500, 200));
        int aftBelow = ShotsAt(new Vec3d(0, -1500, 2500));
        Require(above > 50, $"Dorsal point defense must engage an interceptor above the battleship: {above} shots");
        Require(below == 0 && aftBelow == 0, $"The battleship's belly and lower stern must be blind spots: below {below}, aft-below {aftBelow}");

        var world = new SimWorld();
        Add(world, "battleship", Faction.Blue, Vec3d.Zero, "BB");
        ShipBody exposed = Add(world, "interceptor", Faction.Red, new Vec3d(0, 2500, 0), "IC");
        float shield = exposed.Damage.Shield;
        for (int i = 0; i < 60 * 5; i++) world.Step();
        Require(exposed.Damage.Shield < shield, "Point defense hits must wear down an exposed interceptor's shield");
        Console.WriteLine($"PD arcs (BB vs IC at 2.5 km, 5 s): above {above} shots, below {below}, aft-below {aftBelow}; exposed IC shield {exposed.Damage.Shield:0}/{shield:0}");
    }

    private static void CheckSubsystemAim()
    {
        var world = new SimWorld();
        ShipBody shooter = Add(world, "interceptor", Faction.Blue, new Vec3d(0, -1000, 4000), "S");
        ShipBody target = Add(world, "battleship", Faction.Red, Vec3d.Zero, "T");
        ModuleState engine = Subsystems.Pick(target, AimSubsystem.Engines, shooter.Position)!;
        Require(engine.Definition.Kind == ModuleKind.Thruster && engine.Definition.Center.Y < 0,
            "Engine pick must choose the engine nearest the shooter (lower stern)");
        FiringSolution aimed = FireControl.Solve(shooter, target, 1, sensorError: false, localAim: engine.Definition.Center);
        Vec3d engineWorld = Subsystems.WorldPosition(target, engine.Definition);
        Require(aimed.Valid && (aimed.AimPoint - engineWorld).Length() < 1.0, "Subsystem aim must lead the module position");
        Require(DamageRay.PreviewArmor(target, shooter.Railgun!.MuzzlePosition, aimed.Direction, shooter.Railgun.Definition.PenetrationMm, out float aft),
            $"An interceptor railgun must penetrate the engine block from behind: {aft:0} mm");
        Vector3 front = (target.Position - new Vec3d(0, 0, -4000)).ToVector3().Normalized();
        Require(!DamageRay.PreviewArmor(target, new Vec3d(0, 0, -4000), front, shooter.Railgun.Definition.PenetrationMm, out float bow),
            $"The same gun must not penetrate the bow: {bow:0} mm");
    }

    private static void CheckRadiators()
    {
        var world = new SimWorld();
        ShipBody bb = Add(world, "battleship", Faction.Blue, Vec3d.Zero, "BB");
        float before = bb.Damage.CoolingFraction;
        // 위에서 좌현 방열판을 수직으로 쏜다(실드를 먼저 벗긴다).
        bb.Damage.AbsorbShield(bb.Damage.Shield, 0);
        for (int i = 0; i < 4; i++)
            DamageRay.Apply(bb, new Vec3d(-170, 500, 330), Vector3.Down, new DamagePacket(220, 250, 120, 2000), 1, (uint)(i + 1));
        Require(bb.Damage.Module("radiator-port").Destroyed && bb.Damage.CoolingFraction < before,
            "Shooting a radiator panel must destroy its cooling and cut heat removal");
        Require(bb.Power.CoolingMw < bb.Definition.Power.CoolingMw, "Lost radiators must lower cooling power");
    }

    private static void CheckInfiltration()
    {
        var world = new SimWorld();
        ShipBody bb = Add(world, "battleship", Faction.Blue, Vec3d.Zero, "BB");
        ShipBody ic = Add(world, "interceptor", Faction.Red, new Vec3d(0, 0, -25_000), "IC");
        world.AttachBrain(ic, ShipOrder.AttackOn(bb));
        var seen = new HashSet<string>();
        double firstEngineHit = -1;
        for (int i = 0; i < 60 * 240; i++)
        {
            world.Step();
            seen.Add(world.BrainOf(ic)!.Activity);
            if (firstEngineHit < 0 && bb.Damage.Modules.Any(m => m.Definition.Kind == ModuleKind.Thruster && m.HealthFraction < 1))
                firstEngineHit = world.Time;
        }
        Require(seen.Contains("침투 우회") && seen.Contains("침투 돌진"), $"The interceptor must swing around and run in: {string.Join(",", seen)}");
        Require(firstEngineHit > 0, "An infiltrating interceptor must damage the battleship's engines");
        Require(bb.Damage.PropulsionFraction < 1f, "Engine damage must reduce the battleship's propulsion");
        Console.WriteLine($"Infiltration (IC vs BB with PD, from 25 km ahead): first engine hit {firstEngineHit:0}s, BB propulsion {bb.Damage.PropulsionFraction:P0}, IC {(ic.Damage.Destroyed ? "destroyed" : ic.Damage.Disabled ? "disabled" : $"{ic.Damage.Modules.Count(m => m.Destroyed)} modules lost")}");
    }
}
