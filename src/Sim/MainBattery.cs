using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;

namespace SpaceFleet.Sim;

public enum WeaponSize { Small, Medium, Large, ExtraLarge }
public enum PrimaryWeaponSystem { Railgun, Laser }

/// <summary>Hull-owned socket and mechanical limits. Coordinates use the existing deck frame.</summary>
public sealed record WeaponHardpointDefinition(string Id, string ModuleId, WeaponSize Size,
    Vector3 Pivot, Vector3 Trunnion, bool Ventral,
    float YawDegrees, float MinElevation, float MaxElevation, float YawRate, float ElevationRate,
    float ToleranceDegrees = .03f, Vector3 HousingCenter = default, Vector3 HousingHalfSize = default,
    bool Fixed = false, float AimConeDegrees = 0);

/// <summary>Ballistics shared by every installation of the selected primary weapon.</summary>
public sealed record RailgunBallistics(float MuzzleSpeed, float ReloadSeconds, float Energy,
    float PenetrationMm, float ModuleDamage, float MaxRange, float SensorErrorMeters = 1,
    float SensorErrorPerKm = .15f, float VelocityError = .3f, float ShotHeatMj = 0, float AimErrorScale = 1);

/// <summary>
/// One weapon family/format per ship. Mass and rated power are per installation,
/// already included in the authored hull totals; replacement applies only their difference.
/// Legacy stock weapons have zero separately-accounted cost (their cost remains in the hull).
/// </summary>
public sealed record PrimaryWeaponDefinition(string Id, PrimaryWeaponSystem System, WeaponSize Size,
    int BarrelCount, RailgunBallistics? Railgun, float TraverseSpeedScale = 1, float MassKg = 0, float PowerMw = 0)
{
    [global::System.Text.Json.Serialization.JsonIgnore(Condition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public LaserDefinition? Laser { get; init; }
}

/// <summary>Weapon-owned barrel geometry and magazine at a hull socket; offsets are from its trunnion.</summary>
public sealed record WeaponInstallationDefinition(string HardpointId, Vector3[] Muzzles, int Rounds);

public sealed record MainBatteryDefinition(WeaponHardpointDefinition[] Hardpoints,
    PrimaryWeaponDefinition Weapon, WeaponInstallationDefinition[] Installations)
{
    /// <summary>
    /// Modular parts, one per socket (calibers may differ; one system per ship). Null keeps the
    /// single shared Weapon. When present, Weapon is the primary socket's part and supplies the
    /// ship-level compatibility view; each turret fires with its own part.
    /// </summary>
    [global::System.Text.Json.Serialization.JsonIgnore(Condition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PrimaryWeaponDefinition[]? SocketWeapons { get; init; }
    public PrimaryWeaponDefinition WeaponAt(int socket) => SocketWeapons?[socket] ?? Weapon;
    /// <summary>
    /// Weapons-channel power the hull supplies to its main guns (MW). Socket parts' PowerMw are their
    /// rated draw; supply ÷ rated sets reload (under) or overcharge (over). Null = stock, ratio 1.
    /// </summary>
    [global::System.Text.Json.Serialization.JsonIgnore(Condition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public float? SupplyMw { get; init; }
    /// <summary>Supply ÷ total rated power of the installed parts (1 for stock batteries).</summary>
    public float SupplyRatio => SupplyMw is float supply && SocketWeapons is { } parts ? supply / parts.Sum(p => p.PowerMw) : 1;
}

/// <summary>Optional loadout choice. Null installations retain the authored barrel layout and magazine.</summary>
public sealed record PrimaryWeaponSelection(PrimaryWeaponDefinition Weapon,
    WeaponInstallationDefinition[]? Installations = null);

/// <summary>Compiled once per hull; the flat turret is an adapter for existing firing and model rigs.</summary>
public sealed record ResolvedWeaponMount(WeaponHardpointDefinition Hardpoint, PrimaryWeaponDefinition Weapon,
    WeaponInstallationDefinition Installation, TurretDefinition? Turret);

public static class MainBatteries
{
    /// <summary>Rated draw may exceed supply by half at most: reload then takes 1.5× as long.</summary>
    public const float MinSupplyRatio = 1 / 1.5f;
    /// <summary>Surplus supply raises per-shot energy and muzzle speed by up to 30%.</summary>
    public const float MaxOvercharge = 1.3f;
    public static float Overcharge(float supplyRatio) => Mathf.Clamp(supplyRatio, 1, MaxOvercharge);
    /// <summary>Share of the overcharge that reaches muzzle speed (accuracy); energy takes all of it. +30% → +7.5% speed.</summary>
    public const float OverchargeSpeedShare = .25f;
    public static float OverchargeSpeed(float supplyRatio) => 1 + (Overcharge(supplyRatio) - 1) * OverchargeSpeedShare;

    /// <summary>
    /// Runtime values at a supply ratio. Under rated: reload stretches by rated ÷ supply, the shot is unchanged.
    /// Over rated: energy × overcharge (≤1.3), muzzle speed × a quarter of it, shot heat × overcharge².
    /// </summary>
    public static RailgunDefinition Supplied(RailgunDefinition gun, float supplyRatio)
    {
        if (supplyRatio == 1) return gun;
        float over = Overcharge(supplyRatio), under = Mathf.Min(supplyRatio, 1);
        return gun with { MuzzleSpeed = gun.MuzzleSpeed * OverchargeSpeed(supplyRatio), Energy = gun.Energy * over,
            ShotHeatMj = gun.ShotHeatMj * over * over, ReloadSeconds = gun.ReloadSeconds / under };
    }

    /// <summary>A laser's damage rate follows supply directly up to 130%; heat follows the square above rated.</summary>
    public static LaserDefinition Supplied(LaserDefinition laser, float supplyRatio)
    {
        if (supplyRatio == 1) return laser;
        float rate = Mathf.Min(supplyRatio, MaxOvercharge);
        return laser with { EnergyPerSecond = laser.EnergyPerSecond * rate, ModuleDamagePerSecond = laser.ModuleDamagePerSecond * rate,
            HeatMw = laser.HeatMw * (rate > 1 ? rate * rate : rate) };
    }

    private static void Require([DoesNotReturnIf(false)] bool valid, string message)
    { if (!valid) throw new InvalidDataException("Main battery: " + message); }
    private static bool Positive(float v) => float.IsFinite(v) && v > 0;
    private static bool Nonnegative(float v) => float.IsFinite(v) && v >= 0;
    private static bool Identifier(string? id) => !string.IsNullOrWhiteSpace(id)
        && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>Lossless layout adapter, including the legacy interceptor's fixed gun/aim-assist cone.</summary>
    internal static MainBatteryDefinition? FromLegacy(ShipDefinition ship)
    {
        if (ship.Railgun is not { } gun) return null;
        WeaponSize size = ship.Kind switch
        { HullKind.Battleship => WeaponSize.Large, HullKind.Escort => WeaponSize.Medium, _ => WeaponSize.Small };
        var stats = new RailgunBallistics(gun.MuzzleSpeed, gun.ReloadSeconds, gun.Energy, gun.PenetrationMm,
            gun.ModuleDamage, gun.MaxRange, gun.SensorErrorMeters, gun.SensorErrorPerKm, gun.VelocityError,
            gun.ShotHeatMj, gun.AimErrorScale);
        var weapon = new PrimaryWeaponDefinition("stock-" + ship.Id, PrimaryWeaponSystem.Railgun, size,
            gun.Mounts?[0].Muzzles.Length ?? 1, stats);
        if (gun.Mounts is not { } mounts)
            return new(new[] { new WeaponHardpointDefinition(gun.ModuleId, gun.ModuleId, size,
                Vector3.Zero, Vector3.Zero, false, 0, 0, 0, 0, 0, Fixed: true, AimConeDegrees: gun.TraverseDegrees) },
                weapon, new[] { new WeaponInstallationDefinition(gun.ModuleId, new[] { gun.Muzzle }, gun.Rounds) });
        return new(mounts.Select(m => new WeaponHardpointDefinition(m.ModuleId, m.ModuleId, size,
            m.Pivot, m.Trunnion, m.Ventral, m.YawDegrees, m.MinElevation, m.MaxElevation, m.YawRate, m.ElevationRate,
            m.ToleranceDegrees, m.HousingCenter, m.HousingHalfSize, AimConeDegrees: gun.TraverseDegrees)).ToArray(),
            weapon, mounts.Select(m => new WeaponInstallationDefinition(m.ModuleId, (Vector3[])m.Muzzles.Clone(), m.Rounds)).ToArray());
    }

    internal static void Validate(MainBatteryDefinition b, ShipDefinition ship)
    {
        Require(b.Hardpoints is { Length: > 0 and <= 32 } && b.Hardpoints.All(h => h is not null), "empty/null hardpoints");
        Require(b.Installations is not null && b.Installations.Length == b.Hardpoints.Length
            && b.Installations.All(m => m is not null), "every socket requires one installation");
        Require(b.Hardpoints.Select(h => h.Id).Distinct().Count() == b.Hardpoints.Length
            && b.Hardpoints.Select(h => h.ModuleId).Distinct().Count() == b.Hardpoints.Length, "duplicate socket/module");
        Require(b.SocketWeapons is null || b.SocketWeapons.Length == b.Hardpoints.Length
            && b.SocketWeapons.All(s => s is not null), "every socket requires one part");
        if (b.SocketWeapons is { } parts)
            Require(parts.Any(s => ShipDefinition.SerializeData(s) == ShipDefinition.SerializeData(b.Weapon)),
                "primary weapon must be one of the socket parts");
        Require(b.SupplyMw is null || b.SocketWeapons is not null && b.SocketWeapons.All(p => Positive(p.PowerMw))
            && Positive(b.SupplyMw.Value) && b.SupplyRatio >= MinSupplyRatio - 1e-4f, "invalid main gun supply");
        var weapons = Enumerable.Range(0, b.Hardpoints.Length).Select(b.WeaponAt).Prepend(b.Weapon).ToArray();
        foreach (var w in weapons.Distinct())
        {
            Require(w is not null && Identifier(w.Id) && Enum.IsDefined(w.System)
                && Enum.IsDefined(w.Size), "invalid weapon identity");
            Require(w.System == b.Weapon.System, "one weapon system per ship");
            Require(w.BarrelCount is >= 1 and <= 3
                && Positive(w.TraverseSpeedScale) && w.TraverseSpeedScale <= 1
                && Nonnegative(w.MassKg) && Nonnegative(w.PowerMw), "invalid weapon format/cost");
            if (w.System == PrimaryWeaponSystem.Laser)
            {
                Require(w.Railgun is null && w.Laser is not null && w.BarrelCount == 1, "laser requires one emitter and laser parameters only");
                w.Laser.Validate();
            }
            else Require(w.Railgun is not null && w.Laser is null, "railgun requires ballistic parameters only");
            var g = w.Railgun ?? w.Laser!.GeometryAdapter;
            Require(Positive(g.MuzzleSpeed) && Positive(g.ReloadSeconds) && Positive(g.Energy)
                && Positive(g.PenetrationMm) && Positive(g.ModuleDamage) && Positive(g.MaxRange)
                && Nonnegative(g.SensorErrorMeters) && Nonnegative(g.SensorErrorPerKm)
                && Nonnegative(g.VelocityError) && Nonnegative(g.ShotHeatMj) && Positive(g.AimErrorScale), "invalid ballistics");
        }
        Require(ship.Modules is not null && ship.Flight is not null && ship.Power is not null, "missing hull data");
        Require(weapons.Skip(1).Sum(w => (double)w.MassKg) < ship.Flight.MassKg
            // Socket parts draw against SupplyMw (checked above); replacement costs come out of the authored channel.
            && (b.SupplyMw is not null || weapons.Skip(1).Sum(w => (double)w.PowerMw) <= ship.Power.Weapons), "weapon costs exceed authored hull totals");
        long rounds = 0;
        for (int i = 0; i < b.Hardpoints.Length; i++)
        {
            var h = b.Hardpoints[i]; var m = b.Installations[i]; var w = b.WeaponAt(i);
            Require(Identifier(h.Id) && Identifier(h.ModuleId) && Enum.IsDefined(h.Size) && w.Size <= h.Size
                && m.HardpointId == h.Id, "incompatible size or installation order/identity");
            Require(ship.Modules.Any(d => d.Id == h.ModuleId && d.Kind == ModuleKind.Gun), "socket requires gun damage module");
            Require(h.Pivot.IsFinite() && h.Trunnion.IsFinite() && h.HousingCenter.IsFinite()
                && h.HousingHalfSize.IsFinite() && (h.HousingHalfSize == Vector3.Zero
                    || h.HousingHalfSize.X > 0 && h.HousingHalfSize.Y > 0 && h.HousingHalfSize.Z > 0), "invalid socket geometry");
            Require(m.Muzzles is not null && m.Muzzles.Length == w.BarrelCount
                && m.Muzzles.All(p => p.IsFinite() && p.Z < 0)
                && (w.System == PrimaryWeaponSystem.Laser ? m.Rounds == 0 : m.Rounds > 0), "invalid barrels or ammunition");
            Require(Nonnegative(h.AimConeDegrees) && h.AimConeDegrees <= 180
                && Positive(h.ToleranceDegrees) && h.ToleranceDegrees <= 1, "invalid aiming limits");
            if (h.Fixed)
                Require(b.Hardpoints.Length == 1 && w.BarrelCount == 1 && !h.Ventral
                    && h.YawDegrees == 0 && h.MinElevation == 0 && h.MaxElevation == 0
                    && h.YawRate == 0 && h.ElevationRate == 0, "fixed axis requires one barrel and no turret drive");
            else
                Require(Positive(h.YawDegrees) && h.YawDegrees <= 180 && float.IsFinite(h.MinElevation)
                    && h.MinElevation >= -90 && h.MinElevation <= 0 && Positive(h.MaxElevation) && h.MaxElevation <= 90
                    && Positive(h.YawRate) && Positive(h.ElevationRate)
                    && Positive(h.YawRate * w.TraverseSpeedScale) && Positive(h.ElevationRate * w.TraverseSpeedScale), "invalid turret drive");
            rounds += m.Rounds;
        }
        Require(rounds <= int.MaxValue, "magazine overflow");
    }

    internal static RailgunDefinition Compile(MainBatteryDefinition b)
    {
        var w = b.Weapon; var g = w.Railgun ?? w.Laser!.GeometryAdapter;
        var mounts = b.Hardpoints[0].Fixed ? null : b.Hardpoints.Select((h, i) =>
            new TurretDefinition(h.ModuleId, h.Pivot, h.Trunnion, (Vector3[])b.Installations[i].Muzzles.Clone(),
                h.Ventral, h.YawDegrees, h.MinElevation, h.MaxElevation,
                h.YawRate * b.WeaponAt(i).TraverseSpeedScale, h.ElevationRate * b.WeaponAt(i).TraverseSpeedScale, b.Installations[i].Rounds,
                h.ToleranceDegrees, h.HousingCenter, h.HousingHalfSize)).ToArray();
        var first = b.Hardpoints[0]; var barrels = b.Installations[0].Muzzles;
        Vector3 muzzle = mounts is null ? first.Pivot + first.Trunnion + barrels[0]
            : mounts[0].Breech(0) + mounts[0].MountBasis * (barrels[0] + barrels[^1]) * .5f;
        return new(first.ModuleId, muzzle, g.MuzzleSpeed, g.ReloadSeconds, g.Energy, g.PenetrationMm,
            g.ModuleDamage, g.MaxRange, b.Installations.Sum(m => m.Rounds), first.AimConeDegrees,
            g.SensorErrorMeters, g.SensorErrorPerKm, g.VelocityError, g.ShotHeatMj, mounts, g.AimErrorScale);
    }

    /// <summary>Spreads barrels evenly about the installation's center line.</summary>
    internal static Vector3[] Spread(WeaponInstallationDefinition m, int count)
    {
        var center=(m.Muzzles[0]+m.Muzzles[^1])*.5f;
        float span=Mathf.Max(Mathf.Abs(m.Muzzles[^1].X-m.Muzzles[0].X)*.5f,Mathf.Abs(center.Z)*.06f);
        return Enumerable.Range(0,count).Select(i=>center+Vector3.Right*(count==1?0:(2f*i/(count-1)-1)*span)).ToArray();
    }

    internal static ResolvedWeaponMount[] Bind(MainBatteryDefinition? b, RailgunDefinition? gun) => b is null
        ? Array.Empty<ResolvedWeaponMount>() : b.Hardpoints.Select((h, i) =>
            new ResolvedWeaponMount(h, b.WeaponAt(i), b.Installations[i], gun!.Mounts?[i])).ToArray();

    internal static ShipDefinition Select(ShipDefinition hull, PrimaryWeaponSelection choice)
    {
        var original = hull.Battery;
        Require(original is not null, "cannot add primary weapons to a hull without sockets");
        Require(choice.Weapon is not null, "missing selected weapon");
        var selected = choice.Installations ?? original.Installations;
        Require(selected.All(m => m is not null) && selected.Length == original.Hardpoints.Length
            && selected.Select(m => m.HardpointId).Distinct().Count() == selected.Length, "invalid selected installations");
        var ordered = original.Hardpoints.Select(h => selected.SingleOrDefault(m => m.HardpointId == h.Id)
            ?? throw new InvalidDataException("Unknown/missing selected socket: " + h.Id)).ToArray();
        if (JsonNode.DeepEquals(JsonNode.Parse(ShipDefinition.SerializeData(choice.Weapon)), JsonNode.Parse(ShipDefinition.SerializeData(original.Weapon)))
            && JsonNode.DeepEquals(JsonNode.Parse(ShipDefinition.SerializeData(ordered)), JsonNode.Parse(ShipDefinition.SerializeData(original.Installations))))
            return hull; // Explicit stock selection preserves old snapshots and content IDs too.
        var battery = new MainBatteryDefinition(original.Hardpoints, choice.Weapon, ordered);
        float mass = hull.Flight.MassKg + (choice.Weapon.MassKg - original.Weapon.MassKg) * original.Hardpoints.Length;
        float power = hull.Power.Weapons + (choice.Weapon.PowerMw - original.Weapon.PowerMw) * original.Hardpoints.Length;
        Require(Positive(mass) && Positive(power), "invalid replacement mass/power");
        float ratio = hull.Flight.MassKg / mass;
        var flight = hull.Flight with { MassKg = mass, ForwardAccel = hull.Flight.ForwardAccel * ratio,
            StrafeAccel = hull.Flight.StrafeAccel * ratio, BrakeAccel = hull.Flight.BrakeAccel * ratio };
        var json = JsonNode.Parse(hull.ToJson())!.AsObject();
        json.Remove(nameof(ShipDefinition.Railgun));
        json[nameof(ShipDefinition.MainBattery)] = JsonNode.Parse(ShipDefinition.SerializeData(battery));
        json[nameof(ShipDefinition.Flight)] = JsonNode.Parse(ShipDefinition.SerializeData(flight));
        json[nameof(ShipDefinition.Power)] = JsonNode.Parse(ShipDefinition.SerializeData(hull.Power with { Weapons = power }));
        return ShipDefinition.Parse(json.ToJsonString(), strict: true);
    }
}
