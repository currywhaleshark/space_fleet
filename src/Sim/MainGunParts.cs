using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace SpaceFleet.Sim;

/// <summary>
/// Absolute main gun part (docs/DESIGN_modular_weapons.md §4). Values are the part's own at the
/// reference hull; a hull's tier, research and factory differences carry over as per-field
/// ratios against its stock part. Volume replaces mass; RatedMw draws on the weapons channel.
/// </summary>
public sealed record MainGunPart(string Id, string Name, PrimaryWeaponSystem System, WeaponSize Size, int Barrels,
    float Volume, float RatedMw, int Rounds, DesignFamily Design, ShipBranch[] Branches, int MinTier, int Cost,
    RailgunBallistics? Railgun = null, LaserDefinition? Laser = null, HullKind[]? StockKinds = null, float TraverseScale = 1,
    float CaliberMm = 0);

/// <summary>Main gun container upgrades: extra weapon volume, more main gun supply, sockets one size larger.</summary>
public sealed record WeaponBudgetDefinition(float VolumeBonus = 0, float SupplyScale = 1, string[]? EnlargedSockets = null);

public static class MainGunParts
{
    /// <summary>Each tier above a branch's entry tier adds this share of the stock fit's volume (§3).</summary>
    public const float TierVolumeStep = .15f;
    public static IReadOnlyList<MainGunPart> All { get; } = Load();
    public static MainGunPart ById(string id) => All.FirstOrDefault(p => p.Id == id)
        ?? throw new InvalidDataException("알 수 없는 주포 부품입니다: " + id);
    public static MainGunPart? Find(string id) => All.FirstOrDefault(p => p.Id == id);
    public static string UnlockKey(string id) => "part:" + id;
    /// <summary>Stock parts are always fitted; others are researched once per design (§6).</summary>
    public static bool IsStock(MainGunPart part) => part.StockKinds is { Length: > 0 };
    /// <summary>A hull that can research this part: same design, a listed branch and at least the part's tier.</summary>
    public static bool CanResearch(ShipDefinition hull, int tier, MainGunPart part)
        => !IsStock(part) && part.Design == hull.Design && part.Branches.Contains(hull.Branch) && tier >= part.MinTier;

    /// <summary>Volume a socket of this size can hold.</summary>
    public static float SocketVolume(WeaponSize size) => size switch
    { WeaponSize.Small => 2, WeaponSize.Medium => 4, WeaponSize.Large => 8, _ => 12 };

    /// <summary>The part that reproduces this hull's current primary weapon.</summary>
    public static MainGunPart StockFor(ShipDefinition hull)
    {
        var w = hull.Battery?.Weapon ?? throw new InvalidDataException("주포 소켓이 없는 함선입니다.");
        // A fit that switched system (e.g. lasers on a rail battleship) keeps the hull's own stock part as reference.
        return All.SingleOrDefault(p => p.StockKinds?.Contains(hull.Kind) == true && p.Design == hull.Design && p.System == w.System)
            ?? All.FirstOrDefault(p => p.StockKinds?.Contains(hull.Kind) == true && p.Design == hull.Design && p.System == PrimaryWeaponSystem.Railgun)
            ?? throw new InvalidDataException("기본 주포 부품이 없습니다: " + hull.Id);
    }

    /// <summary>Volume the stock fit uses.</summary>
    public static float StockVolume(ShipDefinition hull) => StockFor(hull).Volume * hull.Battery!.Hardpoints.Length;
    /// <summary>Hull total: stock use, plus 15% of it per tier above the branch's entry tier (to 0.5), plus upgrades.</summary>
    public static float VolumeBudget(ShipDefinition hull)
    {
        float stock = StockVolume(hull);
        return stock + MathF.Round(stock * TierVolumeStep * TierSteps(hull) * 2, MidpointRounding.AwayFromZero) / 2 + (hull.WeaponBudget?.VolumeBonus ?? 0);
    }
    /// <summary>Tiers above the first tier of this hull's design and branch in the research tree (0 outside it).</summary>
    public static int TierSteps(ShipDefinition hull)
    {
        // The research catalog validates by resolving hulls; during that, it is not yet available.
        if (ResearchTree.CatalogOrNull is not { } tree || tree.Playable.FirstOrDefault(n => n.HullId == hull.Id) is not { } node) return 0;
        return node.Tier - tree.Playable.Where(n => n.Branch == node.Branch && tree.Design(n) == hull.Design).Min(n => n.Tier);
    }
    /// <summary>
    /// Main gun share of the weapons channel (MW): the stock fit's rated draw, so the stock fit runs at exactly
    /// rated. Lighter parts overcharge, heavier parts reload slower. Reactor upgrades scale it (§7).
    /// </summary>
    public static float Supply(ShipDefinition hull) => StockFor(hull).RatedMw * hull.Battery!.Hardpoints.Length * (hull.WeaponBudget?.SupplyScale ?? 1);
    /// <summary>A socket's size after container upgrades.</summary>
    /// <remarks>A fitted battery (socket parts) already carries the enlarged sizes.</remarks>
    public static WeaponSize SocketSize(ShipDefinition hull, WeaponHardpointDefinition h)
        => hull.Battery?.SocketWeapons is null && hull.WeaponBudget?.EnlargedSockets?.Contains(h.Id) == true
            && h.Size < WeaponSize.ExtraLarge ? h.Size + 1 : h.Size;

    public static bool Fits(ShipDefinition hull, MainGunPart part)
        => hull.Battery is { } b && part.Design == hull.Design && part.Branches.Contains(hull.Branch)
            && b.Hardpoints.Any(h => Socket(h, part, SocketSize(hull, h)) is null);

    /// <summary>Why this per-socket choice cannot be installed, or null. Null entries mean the stock part.</summary>
    public static string? Reject(ShipDefinition hull, IReadOnlyList<string?> choice)
    {
        if (hull.Battery is not { } b) return "주포 소켓이 없는 함선입니다.";
        if (choice.Count != b.Hardpoints.Length) return $"소켓 {b.Hardpoints.Length}개에 부품을 하나씩 골라야 합니다.";
        MainGunPart[] parts;
        try { parts = Parts(hull, choice); } catch (InvalidDataException e) { return e.Message; }
        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            if (p.Design != hull.Design) return $"{p.Name}: 다른 진영의 부품입니다.";
            // The hull's own stock part is always allowed (e.g. a catalog laser on an escort keeps its emitter reference).
            if (p != StockFor(hull) && !p.Branches.Contains(hull.Branch)) return $"{p.Name}: 이 함종에서 연구할 수 없는 부품입니다.";
            if (Socket(b.Hardpoints[i], p, SocketSize(hull, b.Hardpoints[i])) is { } why) return $"{p.Name}: {why}";
        }
        if (parts.Select(p => p.System).Distinct().Count() > 1) return "한 함선에는 한 가지 주포 계통만 달 수 있습니다.";
        return Totals(parts, VolumeBudget(hull), Supply(hull));
    }

    /// <summary>Hull totals: weapon volume, and rated draw at most 1.5× the main gun supply.</summary>
    internal static string? Totals(IReadOnlyList<MainGunPart> parts, float volumeBudget, float supplyMw)
    {
        float volume = parts.Sum(p => p.Volume);
        if (volume > volumeBudget + 1e-4f) return $"무장 용적 초과: {volume:0.#} / {volumeBudget:0.#}";
        float power = parts.Sum(p => p.RatedMw), limit = supplyMw / MainBatteries.MinSupplyRatio;
        if (power > limit + 1e-4f) return $"무장 정격 전력 과다: {power:0.#} MW, 공급 {supplyMw:0.#} MW의 1.5배까지";
        return null;
    }

    internal static string? Socket(WeaponHardpointDefinition h, MainGunPart p, WeaponSize? enlarged = null)
    {
        var size = enlarged ?? h.Size;
        if (p.Size > size) return "소켓보다 큰 부품입니다.";
        if (p.Volume > SocketVolume(size) + 1e-4f) return "소켓 용적을 넘습니다.";
        if (h.Fixed && p.Barrels != 1) return "고정 축선에는 단장 부품만 들어갑니다.";
        return null;
    }

    private static MainGunPart[] Parts(ShipDefinition hull, IReadOnlyList<string?> choice)
    {
        var stock = StockFor(hull);
        return choice.Select(id => id is null ? stock : ById(id)).ToArray();
    }

    /// <summary>Applied after research and catalog systems. Choosing only stock parts keeps the hull (and its content ID) unchanged.</summary>
    internal static ShipDefinition Apply(ShipDefinition hull, IReadOnlyList<string?>? choice)
    {
        float scale = hull.WeaponBudget?.SupplyScale ?? 1;
        if (choice is null)
        {
            // A reactor upgrade overcharges even the stock fit, which then needs explicit socket parts.
            if (scale == 1 || hull.Battery is null) return hull;
            choice = new string?[hull.Battery.Hardpoints.Length];
        }
        if (Reject(hull, choice) is { } why) throw new InvalidDataException(why);
        var b = hull.Battery!; var stock = StockFor(hull); var parts = Parts(hull, choice);
        if (parts.All(p => p == stock) && scale == 1) return hull;
        var a = b.Weapon;
        var weapons = new PrimaryWeaponDefinition[parts.Length];
        var installs = new WeaponInstallationDefinition[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i]; var m = b.Installations[i];
            if (p == stock) { weapons[i] = a with { Id = p.Id, PowerMw = p.RatedMw }; installs[i] = m; continue; }
            weapons[i] = Scaled(p, stock, a);
            int rounds = p.System == PrimaryWeaponSystem.Laser ? 0
                : m.Rounds > 0 ? Math.Max(1, (int)MathF.Round(m.Rounds * (float)p.Rounds / stock.Rounds)) : p.Rounds;
            installs[i] = m with { Muzzles = p.Barrels == m.Muzzles.Length ? m.Muzzles : MainBatteries.Spread(m, p.Barrels), Rounds = rounds };
        }
        // The longest-reaching socket supplies the ship-level view (AI range, HUD); turrets fire their own.
        int primary = Enumerable.Range(0, parts.Length).MaxBy(i => (Reach(weapons[i]), -i));
        var hardpoints = b.Hardpoints.Select(h => h with { Size = SocketSize(hull, h) }).ToArray();
        var battery = new MainBatteryDefinition(hardpoints, weapons[primary], installs) { SocketWeapons = weapons, SupplyMw = Supply(hull) };
        var json = JsonNode.Parse(hull.ToJson())!.AsObject();
        json.Remove(nameof(ShipDefinition.Railgun));
        json[nameof(ShipDefinition.MainBattery)] = JsonNode.Parse(ShipDefinition.SerializeData(battery));
        return ShipDefinition.Parse(json.ToJsonString(), strict: true);
    }

    /// <summary>
    /// Sustained main gun output at the fit's supply, energy per second: Σ shot energy × barrels ÷ reload
    /// (lasers: energy per second). Heat and accuracy are not included.
    /// </summary>
    public static float SustainedEnergy(ShipDefinition def)
    {
        if (def.Battery is not { } b) return 0;
        float ratio = b.SupplyRatio, total = 0;
        for (int i = 0; i < b.Hardpoints.Length; i++)
        {
            var w = b.WeaponAt(i);
            if (w.Laser is { } laser) total += MainBatteries.Supplied(laser, ratio).EnergyPerSecond;
            else if (w.Railgun is { } r) total += r.Energy * MainBatteries.Overcharge(ratio) * w.BarrelCount * MathF.Min(ratio, 1) / r.ReloadSeconds;
        }
        return total;
    }

    /// <summary>Parts a hull could carry at a socket (own design and branch, socket size after upgrades), stock first.</summary>
    public static IEnumerable<MainGunPart> Candidates(ShipDefinition hull, int socket)
    {
        var h = hull.Battery!.Hardpoints[socket]; var stock = StockFor(hull);
        return All.Where(p => p == stock || p.Design == hull.Design && p.Branches.Contains(hull.Branch)
            && Socket(h, p, SocketSize(hull, h)) is null).OrderBy(p => p == stock ? 0 : 1);
    }

    private static float Reach(PrimaryWeaponDefinition w) => w.Railgun?.MaxRange ?? w.Laser!.FalloffMeters;
    private static float R(float actual, float stock) => stock == 0 ? 1 : actual / stock;

    /// <summary>Part value × (hull's actual stock value ÷ stock part value), field by field.</summary>
    private static PrimaryWeaponDefinition Scaled(MainGunPart p, MainGunPart stock, PrimaryWeaponDefinition actual)
    {
        float speed = 1, reload = 1, energy, pen, module, range, sensorM, sensorKm, velocity = 1, heat, aim;
        if (actual.Railgun is { } ar && stock.Railgun is { } sr)
        {
            speed = R(ar.MuzzleSpeed, sr.MuzzleSpeed); reload = R(ar.ReloadSeconds, sr.ReloadSeconds);
            energy = R(ar.Energy, sr.Energy); pen = R(ar.PenetrationMm, sr.PenetrationMm); module = R(ar.ModuleDamage, sr.ModuleDamage);
            range = R(ar.MaxRange, sr.MaxRange); sensorM = R(ar.SensorErrorMeters, sr.SensorErrorMeters);
            sensorKm = R(ar.SensorErrorPerKm, sr.SensorErrorPerKm); velocity = R(ar.VelocityError, sr.VelocityError);
            heat = R(ar.ShotHeatMj, sr.ShotHeatMj); aim = R(ar.AimErrorScale, sr.AimErrorScale);
        }
        else
        {
            var al = actual.Laser!; var sl = stock.Laser!;
            energy = R(al.EnergyPerSecond, sl.EnergyPerSecond); module = R(al.ModuleDamagePerSecond, sl.ModuleDamagePerSecond);
            pen = R(al.PenetrationMm, sl.PenetrationMm); range = R(al.FalloffMeters, sl.FalloffMeters);
            sensorM = R(al.SensorErrorMeters, sl.SensorErrorMeters); sensorKm = R(al.SensorErrorPerKm, sl.SensorErrorPerKm);
            heat = R(al.HeatMw, sl.HeatMw); aim = R(al.AimErrorScale, sl.AimErrorScale);
        }
        var w = new PrimaryWeaponDefinition(p.Id, p.System, p.Size, p.Barrels, null,
            p.TraverseScale * actual.TraverseSpeedScale, 0, p.RatedMw);
        if (p.Railgun is { } r)
            return w with { Railgun = r with { MuzzleSpeed = r.MuzzleSpeed * speed, ReloadSeconds = r.ReloadSeconds * reload,
                Energy = r.Energy * energy, PenetrationMm = r.PenetrationMm * pen, ModuleDamage = r.ModuleDamage * module,
                MaxRange = r.MaxRange * range, SensorErrorMeters = r.SensorErrorMeters * sensorM,
                SensorErrorPerKm = r.SensorErrorPerKm * sensorKm, VelocityError = r.VelocityError * velocity,
                ShotHeatMj = r.ShotHeatMj * heat, AimErrorScale = r.AimErrorScale * aim } };
        var l = p.Laser!;
        return w with { Laser = l with { EnergyPerSecond = l.EnergyPerSecond * energy, ModuleDamagePerSecond = l.ModuleDamagePerSecond * module,
            PenetrationMm = l.PenetrationMm * pen, HeatMw = l.HeatMw * heat, FalloffMeters = MathF.Min(l.FalloffMeters * range, l.RayRange),
            SensorErrorMeters = l.SensorErrorMeters * sensorM, SensorErrorPerKm = l.SensorErrorPerKm * sensorKm, AimErrorScale = l.AimErrorScale * aim } };
    }

    private static IReadOnlyList<MainGunPart> Load()
    {
        using var stream = typeof(MainGunParts).Assembly.GetManifestResourceStream("SpaceFleet.data.weapons.main_guns.json")
            ?? throw new InvalidDataException("Missing main gun part catalog");
        using var reader = new StreamReader(stream);
        var rows = ShipDefinition.ParseData<MainGunPart[]>(reader.ReadToEnd());
        if (rows.Length == 0 || rows.Any(p => p is null) || rows.Select(p => p.Id).Distinct().Count() != rows.Length)
            throw new InvalidDataException("Invalid main gun part catalog");
        foreach (var p in rows)
        {
            bool laser = p.System == PrimaryWeaponSystem.Laser;
            if (string.IsNullOrWhiteSpace(p.Id) || !p.Id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
                || string.IsNullOrWhiteSpace(p.Name) || !Enum.IsDefined(p.System) || !Enum.IsDefined(p.Size) || !Enum.IsDefined(p.Design)
                || p.Barrels is < 1 or > 3 || laser && p.Barrels != 1
                || !float.IsFinite(p.Volume) || p.Volume <= 0 || p.Volume > SocketVolume(p.Size)
                || !float.IsFinite(p.RatedMw) || p.RatedMw <= 0 || p.MinTier is < 1 or > 7 || p.Cost < 0
                || (p.Cost == 0) != (p.StockKinds is { Length: > 0 })
                || p.Branches is not { Length: > 0 } || p.Branches.Any(x => !Enum.IsDefined(x))
                || !float.IsFinite(p.TraverseScale) || p.TraverseScale <= 0 || p.TraverseScale > 1
                || (laser ? p.Laser is null || p.Railgun is not null || p.Rounds != 0 || p.CaliberMm != 0
                    : p.Railgun is null || p.Laser is not null || p.Rounds <= 0 || !float.IsFinite(p.CaliberMm) || p.CaliberMm is < 50 or > 500))
                throw new InvalidDataException("Invalid main gun part: " + p.Id);
            p.Laser?.Validate();
            if (p.Railgun is { } r && !new[] { r.MuzzleSpeed, r.ReloadSeconds, r.Energy, r.PenetrationMm, r.ModuleDamage, r.MaxRange, r.AimErrorScale }
                    .All(v => float.IsFinite(v) && v > 0))
                throw new InvalidDataException("Invalid main gun ballistics: " + p.Id);
        }
        // Exactly one stock part per design, kind and system.
        var stock = rows.Where(p => p.StockKinds is not null).SelectMany(p => p.StockKinds!.Select(k => (p.Design, k, p.System))).ToArray();
        if (stock.Distinct().Count() != stock.Length) throw new InvalidDataException("Duplicate stock main gun part");
        return Array.AsReadOnly(rows);
    }
}
