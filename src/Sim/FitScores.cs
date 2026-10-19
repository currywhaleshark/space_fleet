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
    /// <summary>Lasers spend energy differently (focus, armour reduction); this converts their rate to rail-equivalent.</summary>
    public const float LaserEquivalence = 2f;
    // Firepower dominates; protection, point defence, mobility and sensing make up the rest.
    private const double Firepower = .5, Defense = .2, PointDefense = .1, Mobility = .1, Sensing = .1;
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
            * Math.Pow(R(Agility(fitted), Agility(stock)), Mobility)
            * Math.Pow(R(Sight(fitted), Sight(stock)), Sensing);
    }

    /// <summary>Sustained main gun output with a mild reach bonus against the stock fit's longest gun.</summary>
    internal static double Guns(ShipDefinition def, ShipDefinition reference)
    {
        if (def.Battery is not { } b) return 0;
        float ratio = b.SupplyRatio, reach = Reach(reference);
        double total = 0;
        for (int i = 0; i < b.Hardpoints.Length; i++)
        {
            var w = b.WeaponAt(i);
            if (w.Laser is { } laser)
                total += MainBatteries.Supplied(laser, ratio).EnergyPerSecond * LaserEquivalence * Math.Pow(LaserReach(laser) / reach, .25);
            else if (w.Railgun is { } r)
                total += r.Energy * MainBatteries.Overcharge(ratio) * w.BarrelCount * Math.Min(ratio, 1) / r.ReloadSeconds * Math.Pow(r.MaxRange / reach, .25);
        }
        return total;
    }
    private static float LaserReach(LaserDefinition laser) => laser.FalloffMeters * 5;
    private static float Reach(ShipDefinition def) => def.Battery is not { } b ? 1
        : Enumerable.Range(0, b.Hardpoints.Length).Select(b.WeaponAt).Max(w => w.Railgun?.MaxRange ?? LaserReach(w.Laser!));
    private static double Shields(ShipDefinition d) => d.Shield.Capacity + d.Shield.RechargePerSecond * 30 + d.StructureHitPoints;
    private static double Defence(ShipDefinition d) => d.PointDefense is not { } pd ? 1
        : pd.ShotsPerSecond * pd.EffectiveMissileDamage * Math.Sqrt(pd.RangeMeters) * pd.Mounts.Length;
    private static double Agility(ShipDefinition d) => d.Flight.ForwardAccel * .5 + d.Flight.StrafeAccel * .3 + d.Flight.BrakeAccel * .2;
    private static double Sight(ShipDefinition d) => d.Sensors.Strength / Math.Sqrt(Math.Max(d.Sensors.Signature, 1e-6));

    /// <summary>A random fit of main gun parts an AI member may field (stock half the time), within the hull's legal mixes.</summary>
    public static ShipComponentSelection? RandomParts(ShipFit fit, Random random, double stockChance = .5)
    {
        if (random.NextDouble() < stockChance) return null;
        var hull = FleetLoadouts.ResolveFit(fit with { Components = null }).Definition;
        if (hull.Battery is null) return null;
        var combos = MainGunParts.Combos(hull).Where(c => c.Any(p => p is not null)).ToArray();
        return combos.Length == 0 ? null : new ShipComponentSelection { MainGuns = combos[random.Next(combos.Length)] };
    }

    /// <summary>The legal main gun mix (stock included) whose score is closest to a target, among a random sample.</summary>
    public static ShipComponentSelection? ClosestParts(ShipFit fit, double target, Random random, int samples = 32)
    {
        var hull = FleetLoadouts.ResolveFit(fit with { Components = null }).Definition;
        double tree = ResearchTree.Catalog.Score(fit);
        if (hull.Battery is null || Math.Abs(tree - target) <= target * 1e-9) return null;
        var options = MainGunParts.Combos(hull).Where(c => c.Any(p => p is not null)).OrderBy(_ => random.Next()).Take(samples)
            .Select(c => new ShipComponentSelection { MainGuns = c }).Prepend(null).ToArray();
        return options.MinBy(o => Math.Abs(tree * Ratio(fit with { Components = o }) - target));
    }
}
