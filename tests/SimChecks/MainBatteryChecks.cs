using System.Text.Json.Nodes;
using System.Diagnostics.CodeAnalysis;
using Godot;
using SpaceFleet.Sim;

static class MainBatteryChecks
{
    private static int checks;
    private static void Check([DoesNotReturnIf(false)] bool ok, string why) { if (!ok) throw new Exception(why); checks++; }
    private static void Reject(Action action, string why)
    {
        try { action(); }
        catch (System.IO.InvalidDataException) { checks++; return; }
        throw new Exception("Expected rejection: " + why);
    }
    public static void Run()
    {
        LegacyAndNative();
        SelectionAndBudgets();
        FixedAxis();
        InvalidDefinitions();
        Console.WriteLine($"PASS: {checks} primary hardpoint/weapon/legacy compatibility checks");
    }

    private static ShipDefinition Native(ShipDefinition source, MainBatteryDefinition? battery = null)
    {
        var json = JsonNode.Parse(source.ToJson())!.AsObject();
        json.Remove("Railgun");
        json["MainBattery"] = JsonNode.Parse(ShipDefinition.SerializeData(battery ?? source.Battery));
        return ShipDefinition.Parse(json.ToJsonString(), strict: true);
    }

    private static void LegacyAndNative()
    {
        foreach (var original in ShipDefinitions.All.Concat(ShipVariants.All.Select(v => v.Definition)))
        {
            string before = original.ToJson();
            var battery = original.Battery!;
            Check(original.MainBattery is null && battery is not null, original.Id + ": stock schema retained");
            Check(!before.Contains("MainBattery") && !before.Contains("PrimaryMounts"), "Derived data does not change legacy JSON");
            Check(battery.Hardpoints.Length == (original.Railgun!.Mounts?.Length ?? 1), "All stock sockets are represented");
            Check(battery.Installations.Sum(m => m.Rounds) == original.Railgun.Rounds, "Legacy ammunition preserved");
            var selected = ShipDefinitions.All.Any(d => d.Id == original.Id)
                ? ShipLoadouts.Resolve(new(original.Id) { MainWeapon = new(battery.Weapon) })
                : ShipVariants.Resolve(original.Id, null, null, new(battery.Weapon));
            Check(selected.Definition.ToJson() == before, "Explicit stock selection also preserves the old schema");
            var native = Native(original);
            string saved = native.ToJson();
            var parsed = JsonNode.Parse(saved)!.AsObject();
            Check(parsed.ContainsKey("MainBattery") && !parsed.ContainsKey("Railgun"), "Only one authored weapon source is serialized");
            Check(ShipDefinition.Parse(saved, strict: true).ToJson() == saved, "Native schema round-trips without normalization drift");
            Check(native.Hull.Boxes.SequenceEqual(original.Hull.Boxes)
                && ShipDefinition.SerializeData(native.Modules) == ShipDefinition.SerializeData(original.Modules), "Conversion preserves damage and collision geometry");
            var a = new ShipBody("SAME", original, Faction.Blue);
            var b = new ShipBody("SAME", native, Faction.Blue);
            Quaternion pose = new Quaternion(new Vector3(1, 2, 3).Normalized(), .63f);
            a.Place(new(1e12, -1e12, 1e12), pose); b.Place(a.Position, pose);
            a.Control = b.Control = new ShipControl { FlightAssist = false };
            for (int tick = 0; tick < 120; tick++)
            {
                Vector3 aim = pose * new Vector3(.25f, .15f, -1).Normalized();
                foreach (var g in a.Railguns) g.Aim(aim);
                foreach (var g in b.Railguns) g.Aim(aim);
                a.Step(SimWorld.TickDelta); b.Step(SimWorld.TickDelta);
                for (int i = 0; i < a.Railguns.Length; i++)
                {
                    var ga = a.Railguns[i]; var gb = b.Railguns[i];
                    Check(ga.Yaw == gb.Yaw && ga.Elevation == gb.Elevation && ga.Direction == gb.Direction,
                        "Legacy/native slew and elevation are bit-identical");
                    for (int barrel = 0; barrel < ga.BarrelCount; barrel++)
                        Check(ga.BarrelPosition(barrel) == gb.BarrelPosition(barrel), "Legacy/native muzzle positions are bit-identical at large coordinates");
                }
            }
            CompareFiring(original, native);
            Check(original.ToJson() == before, "Conversion and simulation do not mutate catalog data");
        }
        var empty = ShipLoadouts.Resolve(new("interceptor", """{"railgun":null}""")).Definition;
        Check(empty.Battery is null && new ShipBody("NONE", empty, Faction.Blue).Railguns.Length == 0, "Unarmed hull stays unarmed");
    }

    private static void CompareFiring(ShipDefinition original, ShipDefinition native)
    {
        var wa = new SimWorld(); var wb = new SimWorld();
        var a = wa.Add(new ShipBody("FIRE", original, Faction.Blue));
        var b = wb.Add(new ShipBody("FIRE", native, Faction.Blue));
        a.Control = b.Control = new ShipControl { FlightAssist = false };
        a.Velocity = b.Velocity = new(13, 7, -28);
        for (int tick = 0; tick < 480; tick++)
        {
            var fa = wa.FireRailguns(a, Vector3.Forward); var fb = wb.FireRailguns(b, Vector3.Forward);
            Check(fa.Fired == fb.Fired && fa.Failure == fb.Failure && fa.Shots == fb.Shots, "Fire gates and salvos unchanged");
            Check(a.Power.HeatMj == b.Power.HeatMj && a.Railguns.Select(g => (g.Rounds, g.ReloadRemaining, g.ShotCount))
                .SequenceEqual(b.Railguns.Select(g => (g.Rounds, g.ReloadRemaining, g.ShotCount))), "Ammo, reload and shot heat unchanged");
            Check(wa.Projectiles.Select(p => (p.ModuleId, p.Barrel, p.Position, p.Velocity, p.Packet, p.Age))
                .SequenceEqual(wb.Projectiles.Select(p => (p.ModuleId, p.Barrel, p.Position, p.Velocity, p.Packet, p.Age))), "Actual projectile states unchanged");
            wa.Step(); wb.Step();
        }
        Check(a.Railguns.Sum(g => g.ShotCount) > 0, "Compatibility fixture actually fires");
    }

    private static PrimaryWeaponSelection Triple(ShipDefinition hull)
    {
        var source = hull.Battery!;
        return new(source.Weapon with { Id = "fixture-triple", BarrelCount = 3, TraverseSpeedScale = .5f,
            MassKg = 1000000, PowerMw = 10, Railgun = source.Weapon.Railgun! with { ShotHeatMj = 2 } },
            source.Installations.Select(m => m with { Muzzles = new[] { m.Muzzles[0], (m.Muzzles[0] + m.Muzzles[^1]) * .5f, m.Muzzles[^1] }, Rounds = 321 }).ToArray());
    }

    private static void SelectionAndBudgets()
    {
        var source = ShipDefinitions.ById("escort"); string before = source.ToJson();
        var choice = Triple(source);
        var resolved = ShipLoadouts.Resolve(new("escort") { MainWeapon = choice });
        var d = resolved.Definition;
        Check(d.Battery!.Hardpoints.SequenceEqual(source.Battery!.Hardpoints), "Weapon choice cannot move hull sockets");
        Check(d.Railgun!.Mounts!.All(m => m.Muzzles.Length == 3 && m.Rounds == 321), "All turrets share the selected format");
        Check(d.Flight.MassKg == source.Flight.MassKg + 2000000 && d.Power.Weapons == source.Power.Weapons + 20,
            "Replacement mass/power charged per installation");
        Check(d.Flight.ForwardAccel == source.Flight.ForwardAccel * (source.Flight.MassKg / d.Flight.MassKg), "Added mass reduces acceleration");
        Check(d.Railgun.Mounts![0].YawRate == source.Railgun!.Mounts![0].YawRate * .5f, "Weapon inertia respects hull drive ceiling");
        for (int i = 0; i < 3; i++) d = ShipDefinition.Parse(d.ToJson(), strict: true);
        Check(d.Flight.MassKg == resolved.Definition.Flight.MassKg && d.Power.Weapons == resolved.Definition.Power.Weapons,
            "Snapshot reload never charges weapon costs twice");
        var copy = ShipDefinition.ParseData<PrimaryWeaponSelection>(ShipDefinition.SerializeData(choice));
        Check(ShipLoadouts.Resolve(new("escort") { MainWeapon = copy }).ContentHash == resolved.ContentHash,
            "Weapon selection is serializable and content addressed");
        var reversed = choice with { Installations = choice.Installations!.Reverse().ToArray() };
        Check(ShipLoadouts.Resolve(new("escort") { MainWeapon = reversed }).ContentHash == resolved.ContentHash, "Selection order does not move sockets or change identity");
        var research = ShipLoadouts.Resolve(new("escort", UpgradePatches: new[] { """{"railgun":{"reloadSeconds":2.75}}""" }) { MainWeapon = choice });
        Check(research.Definition.Railgun!.ReloadSeconds == 2.75f && research.Definition.MainBattery!.Weapon.Railgun!.ReloadSeconds == 2.75f,
            "Existing absolute research patches affect the selected weapon");
        var tree = new JsonObject { ["railgun"] = null,
            ["mAiNbAtTeRy"] = JsonNode.Parse(ShipDefinition.SerializeData(source.Battery)) };
        var mixedCase = ShipLoadouts.Resolve(new("escort", tree.ToJsonString(), new[] { """{"RAILGUN":{"reloadSeconds":2.75}}""" }));
        Check(mixedCase.Definition.MainBattery!.Weapon.Railgun!.ReloadSeconds == 2.75f, "Upgrade mapping respects case-insensitive hull fields");
        var disarmed = ShipLoadouts.Resolve(new("escort", UpgradePatches: new[] { """{"railgun":null}""" }) { MainWeapon = choice });
        Check(disarmed.Definition.Battery is null && new ShipBody("DISARMED", disarmed.Definition, Faction.Blue).Railguns.Length == 0,
            "Legacy removal patch also removes a selected primary battery");
        Check(!ShipDefinition.SerializeData(new ShipLoadout("escort")).Contains("MainWeapon"), "Absent selection preserves loadout serialization");
        var single = new PrimaryWeaponSelection(source.Battery.Weapon with { Id = "fixture-single", BarrelCount = 1 },
            source.Battery.Installations.Select(m => m with { Muzzles = new[] { (m.Muzzles[0] + m.Muzzles[^1]) * .5f } }).ToArray());
        var singleWorld = new SimWorld();
        var singleShip = singleWorld.Add(new ShipBody("SINGLE", ShipLoadouts.Resolve(new("escort") { MainWeapon = single }).Definition, Faction.Blue));
        Check(singleWorld.FireRailguns(singleShip, Vector3.Forward).Shots == 2 && singleWorld.Projectiles.Count == 2,
            "Single-barrel format fires one projectile at each socket");
        var bay = source.Bays.First(b => b.Accepts.Contains(EquipmentCategory.Ecm));
        var equipped = ShipLoadouts.Resolve(new("escort", Equipment: new[] { new EquipmentMount(bay.Id, "ecm-amplifier") }) { MainWeapon = choice });
        Check(equipped.Definition.MainBattery is not null && equipped.Definition.InstalledEquipment.Length == 1
            && equipped.Definition.Flight.MassKg > resolved.Definition.Flight.MassKg, "Physical equipment composes after the selected weapon");
        var world = new SimWorld(); var ship = world.Add(new ShipBody("TRIPLE", d, Faction.Blue));
        ship.Control = new ShipControl { FlightAssist = false };
        var shot = world.FireRailguns(ship, Vector3.Forward);
        Check(shot.Fired && shot.Shots == 6 && world.Projectiles.Count == 6, "Test format fires three actual barrels per turret");
        Check(ship.Railguns.All(g => g.Rounds == 318) && ship.Power.HeatMj == 12, "Test format consumes ammo and heat per barrel");
        var first = ship.Railgun!; var module = ship.Damage.Modules.Single(m => m.Definition.Id == first.Hardpoint.ModuleId);
        module.Health = 0;
        Check(first.Output == 0 && !first.Ready, "Selected weapon remains linked to the original physical gun module");
        Check(source.ToJson() == before, "Selections never rewrite stock data");
    }

    private static void FixedAxis()
    {
        var source = ShipDefinitions.ById("interceptor"); var b = source.Battery!;
        var axis = b with { Hardpoints = new[] { b.Hardpoints[0] with { Size = WeaponSize.ExtraLarge, AimConeDegrees = 0 } },
            Weapon = b.Weapon with { Id = "fixture-axis", Size = WeaponSize.ExtraLarge } };
        var definition = Native(source, axis);
        var w = new SimWorld(); var s = w.Add(new ShipBody("AXIS", definition, Faction.Blue));
        s.Control = new ShipControl { FlightAssist = false };
        Check(s.Railgun!.Hardpoint.Fixed && s.Railgun.Mount is null, "Fixed axis has no fabricated turret drive");
        Check(!w.FireRailgun(s, new Vector3(.01f, 0, -1)).Fired, "Zero-cone axis cannot correct fire away from the hull nose");
        Check(w.FireRailgun(s, Vector3.Forward).Fired, "Zero-cone axis fires along the hull nose");
        s.Railgun.Reset(); s.Place(new(1e12, 1e12, -1e12), new Quaternion(Vector3.Up, Mathf.Pi / 2));
        Check(w.FireRailgun(s, s.Forward).Fired && w.Projectiles.Last().Velocity.Normalized().Dot(s.Forward) > .999999f,
            "Turning the hull turns the axis at large coordinates");
        for (int i = 0; i < 24; i++)
        {
            s.Railgun.Reset();
            s.Place(s.Position, new Quaternion(new Vector3(.3f, 1, .7f).Normalized(), i * .17f));
            Check(w.FireRailgun(s, s.Forward).Fired, "Zero-cone axis tolerates coordinate-transform roundoff at every hull attitude");
        }
    }

    private static void InvalidDefinitions()
    {
        var source = ShipDefinitions.ById("escort"); var b = source.Battery!;
        void Bad(MainBatteryDefinition bad, string why) => Reject(() => Native(source, bad), why);
        Bad(b with { Hardpoints = Array.Empty<WeaponHardpointDefinition>() }, "no sockets");
        Bad(b with { Installations = b.Installations.Take(1).ToArray() }, "missing installation");
        Bad(b with { Hardpoints = new[] { b.Hardpoints[0], b.Hardpoints[0] } }, "duplicate socket");
        Bad(b with { Hardpoints = new[] { b.Hardpoints[0] with { ModuleId = "missing" }, b.Hardpoints[1] } }, "missing damage module");
        Bad(b with { Weapon = b.Weapon with { Size = WeaponSize.ExtraLarge } }, "oversized weapon");
        Bad(b with { Weapon = b.Weapon with { BarrelCount = 3 } }, "mixed barrel count");
        Bad(b with { Weapon = b.Weapon with { System = PrimaryWeaponSystem.Laser } }, "unimplemented system cannot masquerade as a railgun");
        Bad(b with { Weapon = b.Weapon with { TraverseSpeedScale = 1.1f } }, "cannot exceed hull drive rating");
        Bad(b with { Weapon = b.Weapon with { MassKg = -1 } }, "negative mass");
        Bad(b with { Weapon = b.Weapon with { PowerMw = 1000 } }, "authored power inconsistent");
        Bad(b with { Weapon = b.Weapon with { Railgun = b.Weapon.Railgun! with { MuzzleSpeed = 0 } } }, "zero projectile speed");
        Bad(b with { Installations = new[] { b.Installations[0] with { Rounds = 0 }, b.Installations[1] } }, "empty magazine");
        Bad(b with { Installations = b.Installations.Select(m => m with { Rounds = int.MaxValue }).ToArray() }, "magazine sum overflow");
        Bad(b with { Installations = b.Installations.Select(m => m with { Muzzles = new[] { Vector3.Back, Vector3.Back } }).ToArray() }, "backward barrel offsets");
        Bad(b with { Hardpoints = new[] { b.Hardpoints[0] with { Fixed = true }, b.Hardpoints[1] } }, "fixed/rotating mixed battery");
        var dual = JsonNode.Parse(source.ToJson())!.AsObject(); dual["MainBattery"] = JsonNode.Parse(ShipDefinition.SerializeData(b));
        Reject(() => ShipDefinition.Parse(dual.ToJsonString(), strict: true), "two authored primary weapon sources");
        Reject(() => ShipLoadouts.Resolve(new("escort") { MainWeapon = new(b.Weapon with { PowerMw = 1000 }) }), "replacement exceeds generation");
        Reject(() => ShipLoadouts.Resolve(new("interceptor", """{"railgun":null}""") { MainWeapon = new(b.Weapon) }), "unarmed hull has no sockets");
        Reject(() => ShipLoadouts.Resolve(new("escort", UpgradePatches: new[] { """{"railgun":{"rounds":999}}""" }) { MainWeapon = Triple(source) }),
            "ambiguous legacy geometry/ammo upgrade on separated schema");
        Reject(() => ShipLoadouts.Resolve(new("escort") { MainWeapon = Triple(source) with { Installations = new[] { b.Installations[0], b.Installations[0] } } }),
            "duplicate selected socket");
    }
}
