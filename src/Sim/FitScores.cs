using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace SpaceFleet.Sim;

/// <summary>
/// Fitted strength score (docs/DESIGN_modular_weapons.md §8). The research tree score stays the base: hull weight ×
/// tier multiplier × research bonus. Weapon and system choices then scale it by how this fit compares with the same
/// hull, research and bay equipment on stock weapons and systems. A stock fit scores exactly the tree score.
/// </summary>
public static class FitScores
{
    // Firepower dominates; protection, point defence, mobility and sensing make up the rest.
    private const double Firepower = .45, Defense = .15, PointDefense = .1, Missiles = .1, Mobility = .1, Sensing = .1;
    private static readonly ConcurrentDictionary<string, double> Cache = new();

    public static double Score(ShipFit fit) => ResearchTree.Catalog.Score(fit) * Ratio(fit);

    /// <summary>1 for a fit without catalog systems or main gun parts; otherwise fitted ÷ stock-weapons strength.</summary>
    public static double Ratio(ShipFit fit)
    {
        if (fit.Components is null || fit.Components.IsEmpty) return 1;
        string key = $"{fit.HullId}|{string.Join(",", fit.Upgrades ?? Array.Empty<string>())}|{string.Join(",", fit.Equipment.Select(m => m.BayId + "=" + m.EquipmentId))}"
            + $"|{ShipDefinition.SerializeData(fit.Components)}";
        return Cache.GetOrAdd(key, _ =>
        {
            var fitted = FleetLoadouts.ResolveFit(fit).Definition;
            var stock = FleetLoadouts.ResolveFit(fit with { Components = null }).Definition;
            return Ratio(fitted, stock);
        });
    }

    /// <summary>Weighted geometric mean of fitted ÷ stock for each strength term.</summary>
    public static double Ratio(ShipDefinition fitted, ShipDefinition stock)
    {
        static double R(double a, double b) => b <= 0 ? 1 : Math.Max(a, 1e-6) / b;
        return Math.Pow(R(Guns(fitted, stock), Guns(stock, stock)), Firepower)
            * Math.Pow(R(Shields(fitted), Shields(stock)), Defense)
            * Math.Pow(R(Defence(fitted), Defence(stock)), PointDefense)
            * Math.Pow(R(Ordnance(fitted, stock), Ordnance(stock, stock)), Missiles)
            * Math.Pow(R(Agility(fitted), Agility(stock)), Mobility)
            * Math.Pow(R(Sight(fitted), Sight(stock)), Sensing);
    }

    internal static double Guns(ShipDefinition def, ShipDefinition reference) => MainGunParts.EffectiveFirepower(def, reference);
    private static double Shields(ShipDefinition d) => d.Shield.Capacity + d.Shield.RechargePerSecond * 30 + d.StructureHitPoints;
    private static double Defence(ShipDefinition d) => d.PointDefense is not { } pd ? 1
        : pd.ShotsPerSecond * pd.EffectiveMissileDamage * Math.Sqrt(pd.RangeMeters) * pd.Mounts.Length;
    /// <summary>Missile strength: energy weighted by penetration, survivability (HP) and speed, salvo size and magazine.</summary>
    private static double Ordnance(ShipDefinition d, ShipDefinition reference)
    {
        if (d.Missiles is not { } m || reference.Missiles is not { } r) return 1;
        return m.Energy * Math.Sqrt(m.PenetrationMm / r.PenetrationMm) * Math.Pow(m.HitPoints / r.HitPoints, .3) * Math.Pow(m.AccelG / r.AccelG, .3)
            * Math.Pow(m.CellCount, .3) * Math.Pow(m.Rounds / (double)r.Rounds, .25) / Math.Sqrt(m.ReloadSeconds / r.ReloadSeconds);
    }
    private static double Agility(ShipDefinition d) => d.Flight.ForwardAccel * .5 + d.Flight.StrafeAccel * .3 + d.Flight.BrakeAccel * .2;
    private static double Sight(ShipDefinition d) => d.Sensors.Strength / Math.Sqrt(Math.Max(d.Sensors.Signature, 1e-6));

    /// <summary>A random fit of main gun parts an AI member may field (stock half the time), within the hull's legal mixes.</summary>
    /// <summary>Test hook: overrides the AI's chance of keeping stock weapons.</summary>
    internal static double? StockChanceOverride;
    /// <summary>Test hook: every AI squad member that can carry this part carries as many as legal; mirrors stay stock.</summary>
    internal static string? ForcedPart;
    public static ShipComponentSelection? RandomParts(ShipFit fit, Random random, double stockChance = .5)
    {
        if (ForcedPart is { } forcedId && OrdnanceParts.Find(forcedId) is { } ordnance)
        {
            var h = FleetLoadouts.ResolveFit(fit with { Components = null }).Definition;
            if (OrdnanceParts.Socket(h, ordnance) is not null || ordnance.Stock) return null;
            return ordnance.Kind switch
            {
                OrdnanceKind.Launcher => new ShipComponentSelection { Launcher = forcedId },
                OrdnanceKind.Missile => new ShipComponentSelection { Missile = forcedId },
                _ => new ShipComponentSelection { PointDefensePart = forcedId },
            };
        }
        if (ForcedPart is { } forced)
        {
            var h = FleetLoadouts.ResolveFit(fit with { Components = null }).Definition;
            if (h.Battery is null) return null;
            var best = MainGunParts.Combos(h, sameSystem: false).Select(c => (Combo: c, Count: c.Count(p => p == forced)))
                .Where(x => x.Count > 0).OrderByDescending(x => x.Count).ThenBy(x => x.Combo.Count(p => p is not null)).FirstOrDefault();
            return best.Combo is null ? null : new ShipComponentSelection { MainGuns = best.Combo };
        }
        if (random.NextDouble() < (StockChanceOverride ?? stockChance)) return null;
        var hull = FleetLoadouts.ResolveFit(fit with { Components = null }).Definition;
        if (hull.Battery is null) return null;
        var combos = MainGunParts.Combos(hull).Where(c => c.Any(p => p is not null)).ToArray();
        return combos.Length == 0 ? null : new ShipComponentSelection { MainGuns = combos[random.Next(combos.Length)] };
    }

    /// <summary>The legal main gun mix (stock included) whose score is closest to a target, among a random sample.</summary>
    public static ShipComponentSelection? ClosestParts(ShipFit fit, double target, Random random, int samples = 64)
    {
        if (ForcedPart is not null) return null;
        var hull = FleetLoadouts.ResolveFit(fit with { Components = null }).Definition;
        double tree = ResearchTree.Catalog.Score(fit);
        if (hull.Battery is null || Math.Abs(tree - target) <= target * 1e-9) return null;
        // Every legal mix when there are few (the usual case), otherwise a random sample.
        var all = MainGunParts.Combos(hull).Where(c => c.Any(p => p is not null)).ToArray();
        var options = (all.Length <= samples ? all : all.OrderBy(_ => random.Next()).Take(samples).ToArray())
            .Select(c => new ShipComponentSelection { MainGuns = c }).Prepend(null).ToArray();
        return options.MinBy(o => Math.Abs(tree * Ratio(fit with { Components = o }) - target));
    }
}
