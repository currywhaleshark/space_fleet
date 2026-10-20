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
        return Math.Pow(R(Guns(fitted), Guns(stock)), Firepower)
            * Math.Pow(R(Shields(fitted), Shields(stock)), Defense)
            * Math.Pow(R(Defence(fitted), Defence(stock)), PointDefense)
            * Math.Pow(R(Ordnance(fitted, stock), Ordnance(stock, stock)), Missiles)
            * Math.Pow(R(Agility(fitted), Agility(stock)), Mobility)
            * Math.Pow(R(Sight(fitted), Sight(stock)), Sensing);
    }

    internal static double Guns(ShipDefinition def) => MainGunParts.SustainedFirepower(def);
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
        if (ForcedPart is HeavyMix or HeavyMixFed)
            return HeavyParts(fit, ForcedPart == HeavyMixFed);
        if (ForcedPart is { } forcedPower && PowerParts.Find(forcedPower) is { } forcedKind)
        {
            // Stock guns plus as many of the forced generator/cooler as the weapon volume holds.
            var h = FleetLoadouts.ResolveFit(fit with { Components = null }).Definition;
            if (h.Battery is null || PowerParts.Socket(h, forcedKind) is not null) return null;
            var stockGuns = new string?[h.Battery.Hardpoints.Length];
            var ids = new List<string>();
            while (ids.Count < PowerParts.MaxPerShip && MainGunParts.Reject(h, stockGuns, ids.Append(forcedPower).ToArray()) is null) ids.Add(forcedPower);
            return ids.Count == 0 ? null : new ShipComponentSelection { PowerParts = ids.ToArray() };
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
        var guns = combos.Length == 0 ? new string?[hull.Battery.Hardpoints.Length] : combos[random.Next(combos.Length)];
        // Half the part mixes also fill spare weapon volume with one random generator or cooler (§24).
        var power = random.NextDouble() < .5 ? RandomPower(hull, guns, random) : null;
        if (combos.Length == 0 && power is null) return null;
        return new ShipComponentSelection { MainGuns = guns.Any(g => g is not null) ? guns : null, PowerParts = power };
    }

    /// <summary>
    /// Test hook values for generator trials (§24): every AI member that can takes the strongest under-supplied gun mix
    /// that leaves room for a generator; "+gen" also fills that room with generators up to the mix's rated draw.
    /// </summary>
    internal const string HeavyMix = "heavy-mix", HeavyMixFed = "heavy-mix+gen";
    private static readonly ConcurrentDictionary<string, (string?[] Guns, string[] Power)?> HeavyCache = new();
    internal static ShipComponentSelection? HeavyParts(ShipFit fit, bool fed)
    {
        string key = fit.HullId + "|" + string.Join(",", fit.Upgrades ?? Array.Empty<string>());
        var choice = HeavyCache.GetOrAdd(key, _ =>
        {
            var hull = FleetLoadouts.ResolveFit(fit with { Components = null }).Definition;
            if (hull.Battery is null) return null;
            var generators = PowerParts.Candidates(hull).Where(p => p.Kind == PowerPartKind.Generator).OrderByDescending(p => p.SupplyMw).ToArray();
            (string?[] Guns, string[] Power)? best = null; double bestFire = 0;
            foreach (var combo in MainGunParts.Combos(hull).Where(c => MainGunParts.UnderSupplied(hull, c)))
            {
                var power = new List<string>();
                foreach (var g in generators)
                    while (power.Count < PowerParts.MaxPerShip && MainGunParts.Reject(hull, combo, power.Append(g.Id).ToArray()) is null
                        && FleetLoadouts.ResolveFit(fit with { Components = new ShipComponentSelection { MainGuns = combo, PowerParts = power.Count == 0 ? null : power.ToArray() } })
                            .Definition.Battery!.SupplyRatio < 1 - 1e-4f)
                        power.Add(g.Id);
                if (power.Count == 0) continue;
                double fire = MainGunParts.EffectiveFirepower(FleetLoadouts.ResolveFit(fit with { Components = new ShipComponentSelection { MainGuns = combo, PowerParts = power.ToArray() } }).Definition);
                if (fire > bestFire) { bestFire = fire; best = (combo, power.ToArray()); }
            }
            return best;
        });
        if (choice is not { } c) return null;
        return new ShipComponentSelection { MainGuns = c.Guns.Any(g => g is not null) ? c.Guns : null, PowerParts = fed ? c.Power : null };
    }

    private static string[]? RandomPower(ShipDefinition hull, string?[] guns, Random random)
    {
        var legal = PowerParts.Candidates(hull).Where(p => MainGunParts.Reject(hull, guns, new[] { p.Id }) is null).ToArray();
        return legal.Length == 0 ? null : new[] { legal[random.Next(legal.Length)].Id };
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
            .Select(c => new ShipComponentSelection { MainGuns = c })
            // Generators matter only where they lift an under-supplied mix toward rated (§24).
            .Concat(all.Where(c => MainGunParts.UnderSupplied(hull, c)).Take(samples)
                .SelectMany(c => PowerParts.Candidates(hull).Where(p => p.Kind == PowerPartKind.Generator && MainGunParts.Reject(hull, c, new[] { p.Id }) is null)
                    .Select(p => new ShipComponentSelection { MainGuns = c, PowerParts = new[] { p.Id } })))
            .Prepend(null).ToArray();
        return options.MinBy(o => Math.Abs(tree * Ratio(fit with { Components = o }) - target));
    }
}
