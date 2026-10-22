using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

public enum BaySize { Small, Medium, Large, ExtraLarge }
public enum EquipmentCategory { Shield, Sensor, Ecm, Drone }

public sealed record EquipmentBay(string Id, BaySize Size, Vector3 Center, Vector3 HalfSize,
    PowerGrid Grid, EquipmentCategory[] Accepts);
public sealed record EquipmentMount(string BayId, string EquipmentId);
public sealed record EquipmentEffects(float ShieldCapacityFraction = 0, float ShieldDelaySeconds = 0,
    float SensorStrengthFraction = 0, float SignatureFraction = 0, float Jammer = 0, int Drones = 0);

public sealed record EquipmentDefinition(string Id, string Name, EquipmentCategory Category, BaySize Size,
    ModuleKind Kind, float HitPoints, float ResistanceMm, PowerChannel Channel, float PowerMw, float HeatMw,
    float MassKg, EquipmentEffects Effects, float CriticalChance = 0, float CriticalEnergy = 300,
    DesignFamily[]? Designs = null);

/// <summary>Snapshot of the installed device, sufficient to restore a validated definition without catalog drift.</summary>
public sealed record InstalledEquipment(string BayId, string ModuleId, EquipmentDefinition Equipment, int DroneStartIndex = -1);

public static class ShipEquipment
{
    private static readonly IReadOnlyDictionary<string, EquipmentDefinition> Catalog = Load();
    public static IEnumerable<EquipmentDefinition> All => Catalog.Values;
    public static EquipmentDefinition ById(string id) => Catalog.TryGetValue(id, out var equipment)
        ? equipment : throw new ArgumentException($"Unknown equipment ID: {id}", nameof(id));

    private static IReadOnlyDictionary<string, EquipmentDefinition> Load()
    {
        const string prefix = "SpaceFleet.data.parts.";
        var result = new Dictionary<string, EquipmentDefinition>(StringComparer.Ordinal);
        foreach (string name in typeof(ShipEquipment).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal))
        {
            using var stream = typeof(ShipEquipment).Assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var item = ShipDefinition.ParseData<EquipmentDefinition>(reader.ReadToEnd());
            EquipmentRules.ValidateDevice(item);
            if (name != prefix + item.Id + ".json") throw new InvalidDataException($"Equipment ID/file mismatch: {name}");
            result.Add(item.Id, item);
        }
        return result;
    }
}

internal static class EquipmentRules
{
    internal static bool IsEquipment(ModuleKind kind) => kind is ModuleKind.ShieldBooster or ModuleKind.SensorBooster
        or ModuleKind.EcmSuite or ModuleKind.DroneBay;
    private static void Require([DoesNotReturnIf(false)] bool valid, string message)
    { if (!valid) throw new InvalidDataException(message); }
    private static bool Positive(float v) => float.IsFinite(v) && v > 0;
    private static bool Nonnegative(float v) => float.IsFinite(v) && v >= 0;
    private static bool Identifier(string? id) => !string.IsNullOrWhiteSpace(id)
        && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    private static bool Contains(HullSection hull, EquipmentBay bay) =>
        Mathf.Abs(hull.Center.X-bay.Center.X)+bay.HalfSize.X <= hull.HalfSize.X+.001f &&
        Mathf.Abs(hull.Center.Y-bay.Center.Y)+bay.HalfSize.Y <= hull.HalfSize.Y+.001f &&
        Mathf.Abs(hull.Center.Z-bay.Center.Z)+bay.HalfSize.Z <= hull.HalfSize.Z+.001f;
    private static bool Overlaps(Vector3 ac, Vector3 ah, Vector3 bc, Vector3 bh) =>
        Mathf.Abs(ac.X-bc.X) < ah.X+bh.X-.001f && Mathf.Abs(ac.Y-bc.Y) < ah.Y+bh.Y-.001f
        && Mathf.Abs(ac.Z-bc.Z) < ah.Z+bh.Z-.001f;

    internal static void ValidateDevice(EquipmentDefinition d)
    {
        Require(d is not null && Identifier(d.Id) && !string.IsNullOrWhiteSpace(d.Name), "Invalid equipment identity");
        Require(Enum.IsDefined(d!.Category) && Enum.IsDefined(d.Size) && Enum.IsDefined(d.Channel)
            && Positive(d.HitPoints) && Nonnegative(d.ResistanceMm) && Positive(d.PowerMw) && Nonnegative(d.HeatMw)
            && Positive(d.MassKg) && Nonnegative(d.CriticalChance) && d.CriticalChance <= 1 && Nonnegative(d.CriticalEnergy),
            $"{d.Id}: invalid equipment costs/health");
        Require(d.Designs is null || d.Designs.Length > 0 && d.Designs.All(Enum.IsDefined)
            && d.Designs.Distinct().Count() == d.Designs.Length, $"{d.Id}: invalid design restriction");
        var e = d.Effects;
        Require(e is not null && Nonnegative(e.ShieldCapacityFraction) && Nonnegative(e.ShieldDelaySeconds)
            && Nonnegative(e.SensorStrengthFraction) && Nonnegative(e.SignatureFraction) && Nonnegative(e.Jammer)
            && e.Drones >= 0 && e.Drones <= 64, $"{d.Id}: invalid equipment effects");
        bool shield = d.Category == EquipmentCategory.Shield, sensor = d.Category == EquipmentCategory.Sensor,
            ecm = d.Category == EquipmentCategory.Ecm, drone = d.Category == EquipmentCategory.Drone;
        Require((shield && d.Kind == ModuleKind.ShieldBooster && d.Channel == PowerChannel.Shields && e!.ShieldCapacityFraction > 0
                || sensor && d.Kind == ModuleKind.SensorBooster && d.Channel == PowerChannel.Sensors && e!.SensorStrengthFraction > 0
                || ecm && d.Kind == ModuleKind.EcmSuite && d.Channel == PowerChannel.Ecm && e!.Jammer > 0
                || drone && d.Kind == ModuleKind.DroneBay && d.Channel == PowerChannel.Weapons && e!.Drones > 0)
            && (shield || e!.ShieldCapacityFraction == 0 && e.ShieldDelaySeconds == 0)
            && (sensor || e!.SensorStrengthFraction == 0 && e.SignatureFraction == 0)
            && (ecm || e!.Jammer == 0) && (drone || e!.Drones == 0)
            && (drone || d.CriticalChance == 0), $"{d.Id}: equipment category/effect mismatch");
    }

    internal static void Validate(ShipDefinition ship)
    {
        Require(ship.Bays is not null && ship.InstalledEquipment is not null, $"{ship.Id}: null bays/equipment");
        Require(ship.Bays!.Length <= 16 && ship.Bays.All(b => b is not null && Identifier(b.Id))
            && ship.Bays.Select(b => b.Id).Distinct().Count() == ship.Bays.Length, $"{ship.Id}: invalid/duplicate bays");
        foreach (var bay in ship.Bays)
        {
            Require(Enum.IsDefined(bay.Size) && Enum.IsDefined(bay.Grid) && bay.Center.IsFinite() && bay.HalfSize.IsFinite()
                && bay.HalfSize.X > 0 && bay.HalfSize.Y > 0 && bay.HalfSize.Z > 0
                && bay.Accepts is { Length: > 0 } && bay.Accepts.All(Enum.IsDefined)
                && bay.Accepts.Distinct().Count() == bay.Accepts.Length && ship.HullSections.Any(h => Contains(h, bay)),
                $"{ship.Id}: invalid/outside bay {bay.Id}");
            Require(!ship.Modules.Any(m => !IsEquipment(m.Kind) && Overlaps(bay.Center,bay.HalfSize,m.Center,m.HalfSize)),
                $"{ship.Id}: bay {bay.Id} overlaps a fixed module");
        }
        for (int i = 0; i < ship.Bays.Length; i++)
        for (int j = i+1; j < ship.Bays.Length; j++)
            Require(!Overlaps(ship.Bays[i].Center,ship.Bays[i].HalfSize,ship.Bays[j].Center,ship.Bays[j].HalfSize),
                $"{ship.Id}: overlapping bays");
        var installed = ship.InstalledEquipment!;
        Require(ship.Modules.Any(m => !IsEquipment(m.Kind)), "Equipment cannot replace all core modules");
        Require(installed.All(i => i is not null) && installed.Select(i => i.BayId).Distinct().Count() == installed.Length
            && installed.Select(i => i.ModuleId).Distinct().Count() == installed.Length, $"{ship.Id}: duplicate installation");
        double power = 0, heat = 0;
        int droneCount = ship.DefenseDrones?.Count ?? 0;
        int baseDrones = droneCount - installed.Sum(i => i.Equipment?.Effects?.Drones ?? 0);
        Require(baseDrones >= 0 && (installed.All(i => i.Equipment?.Effects?.Drones == 0) || baseDrones > 0), "Drone expansion requires existing defensive drones");
        int nextDrone = baseDrones;
        foreach (var item in installed)
        {
            ValidateDevice(item.Equipment);
            var d = item.Equipment;
            var bay = ship.Bays.SingleOrDefault(b => b.Id == item.BayId);
            var module = ship.Modules.SingleOrDefault(m => m.Id == item.ModuleId);
            Require(bay is not null && module is not null && item.ModuleId == "fit-" + item.BayId
                && d.Size <= bay.Size && bay.Accepts.Contains(d.Category)
                && (d.Designs is null || d.Designs.Contains(ship.Design)), $"{ship.Id}: incompatible installation {item.BayId}");
            Require(module!.Kind == d.Kind && module.Center == bay!.Center && module.HalfSize == bay.HalfSize
                && module.Grid == bay.Grid && module.HitPoints == d.HitPoints && module.ResistanceMm == d.ResistanceMm
                && module.CriticalChance == d.CriticalChance && module.CriticalEnergy == d.CriticalEnergy
                && module.Capacity == 1 && module.Feeds is null && module.VisualEngineIndex == -1,
                $"{ship.Id}: equipment damage geometry/stats mismatch");
            Require(item.DroneStartIndex == (d.Effects.Drones > 0 ? nextDrone : -1), "Invalid drone ownership interval");
            nextDrone += d.Effects.Drones;
            power += d.PowerMw; heat += d.HeatMw;
        }
        Require(ship.Modules.Count(m => IsEquipment(m.Kind)) == installed.Length, "Equipment damage module without installation");
        Require(ship.Power.Engines + ship.Power.Shields + ship.Power.Weapons + ship.Power.Sensors + power <= ship.Power.OutputMw,
            $"{ship.Id}: equipment exceeds rated power budget");
        Require(heat <= ship.Power.CoolingMw, $"{ship.Id}: equipment exceeds heat budget");
        double shield = 1, sensor = 1, signature = 1, jam = ship.Sensors.Jammer, delay = ship.Shield.RechargeDelay;
        foreach (var item in installed)
        {
            var e = item.Equipment.Effects;
            shield += e.ShieldCapacityFraction; sensor += e.SensorStrengthFraction;
            signature += e.SignatureFraction; jam += e.Jammer; delay += e.ShieldDelaySeconds;
        }
        Require(shield * ship.Shield.Capacity <= float.MaxValue && sensor * ship.Sensors.Strength <= float.MaxValue
            && signature * ship.Sensors.Signature <= float.MaxValue && jam <= float.MaxValue && delay <= float.MaxValue,
            "Equipment effect overflow");
    }
}
