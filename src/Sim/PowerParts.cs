using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SpaceFleet.Sim;

public enum PowerPartKind { Generator, Cooler }

/// <summary>
/// Internal power part (docs/DESIGN_modular_weapons.md §24): takes main gun weapon volume, no socket or hardpoint.
/// A generator adds main gun supply, but only up to the fitted guns' rated draw: it lets heavy guns fire at their rated
/// rate, while overcharge still comes from the hull's own supply. A cooler adds cooling and heat capacity.
/// Earth coolers and Mars generators are the more efficient pair (§9).
/// </summary>
public sealed record PowerPart(string Id, string Name, PowerPartKind Kind, WeaponSize Size, float Volume, DesignFamily Design,
    ShipBranch[] Branches, int MinTier, int Cost, float SupplyMw = 0, float CoolingMw = 0, float HeatCapacityMj = 0);

public static class PowerParts
{
    /// <summary>Power parts a single fit may carry; weapon volume is the real limit.</summary>
    public const int MaxPerShip = 4;
    public static IReadOnlyList<PowerPart> All { get; } = Load();
    public static PowerPart? Find(string id) => All.FirstOrDefault(p => p.Id == id);
    public static PowerPart ById(string id) => Find(id) ?? throw new InvalidDataException("알 수 없는 전력 부품입니다: " + id);

    /// <summary>Why this part cannot go on the hull, or null.</summary>
    public static string? Socket(ShipDefinition hull, PowerPart part)
    {
        if (hull.Battery is null) return "주포가 없는 함선입니다.";
        if (part.Design != hull.Design) return "다른 진영의 부품입니다.";
        if (!part.Branches.Contains(hull.Branch)) return "이 함종에서 연구할 수 없는 부품입니다.";
        return null;
    }

    public static IEnumerable<PowerPart> Candidates(ShipDefinition hull) => All.Where(p => Socket(hull, p) is null);

    internal static PowerPart[] Parts(ShipDefinition hull, IReadOnlyList<string>? ids)
    {
        if (ids is null || ids.Count == 0) return Array.Empty<PowerPart>();
        if (ids.Count > MaxPerShip) throw new InvalidDataException($"전력 부품은 {MaxPerShip}개까지 달 수 있습니다.");
        var parts = ids.Select(ById).ToArray();
        foreach (var p in parts) if (Socket(hull, p) is { } why) throw new InvalidDataException($"{p.Name}: {why}");
        return parts;
    }

    /// <summary>
    /// Main gun supply with generators: the hull's own supply, raised by the generators toward the rated draw but never
    /// past it (generators remove under-supply; they do not overcharge).
    /// </summary>
    public static float Supply(float hullSupplyMw, float ratedMw, IEnumerable<PowerPart> parts)
    {
        float generators = parts.Where(p => p.Kind == PowerPartKind.Generator).Sum(p => p.SupplyMw);
        return ratedMw > hullSupplyMw ? MathF.Min(hullSupplyMw + generators, ratedMw) : hullSupplyMw;
    }

    private static IReadOnlyList<PowerPart> Load()
    {
        using var stream = typeof(PowerParts).Assembly.GetManifestResourceStream("SpaceFleet.data.weapons.power.json")
            ?? throw new InvalidDataException("Missing power part catalog");
        using var reader = new StreamReader(stream);
        var rows = ShipDefinition.ParseData<PowerPart[]>(reader.ReadToEnd());
        if (rows.Length == 0 || rows.Any(p => p is null) || rows.Select(p => p.Id).Distinct().Count() != rows.Length
            || rows.Any(p => MainGunParts.Find(p.Id) is not null || OrdnanceParts.Find(p.Id) is not null || ShipComponents.All.Any(c => c.Id == p.Id)))
            throw new InvalidDataException("Invalid power part catalog (duplicate IDs across catalogs)");
        static bool Pos(float v) => float.IsFinite(v) && v > 0;
        foreach (var p in rows)
        {
            bool ok = !string.IsNullOrWhiteSpace(p.Name) && p.Id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
                && Enum.IsDefined(p.Kind) && Enum.IsDefined(p.Size) && Enum.IsDefined(p.Design) && Pos(p.Volume) && p.Volume <= 12
                && p.MinTier is >= 1 and <= 7 && p.Cost > 0 && p.Branches is { Length: > 0 } && p.Branches.All(b => Enum.IsDefined(b))
                && (p.Kind == PowerPartKind.Generator
                    ? Pos(p.SupplyMw) && p.CoolingMw == 0 && p.HeatCapacityMj == 0
                    : p.SupplyMw == 0 && Pos(p.CoolingMw) && float.IsFinite(p.HeatCapacityMj) && p.HeatCapacityMj >= 0);
            if (!ok) throw new InvalidDataException("Invalid power part: " + p.Id);
        }
        return Array.AsReadOnly(rows);
    }
}
