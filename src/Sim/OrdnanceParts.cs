using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace SpaceFleet.Sim;

public enum OrdnanceKind { Launcher, Missile, PointDefense }
public enum MissileFamily { Light, Counter, Medium, Heavy }

/// <summary>
/// Missile launcher, missile or point-defence part (docs/DESIGN_modular_weapons.md §21). Like main gun parts, values are
/// absolute at the reference hull; a hull's tier and research carry over as per-field ratios against its stock part.
/// A null design means either faction may research it (each faction researches it separately).
/// </summary>
public sealed record OrdnancePart(string Id, string Name, OrdnanceKind Kind, int Cost, int MinTier, ShipBranch[] Branches,
    DesignFamily? Design = null, bool Stock = false,
    WeaponSize Size = WeaponSize.Small, int Cells = 1, float MagazineScale = 1, float ReloadScale = 1,
    MissileFamily? Family = null, float Energy = 0, float PenetrationMm = 0, float ModuleDamage = 0, float HitPoints = 0,
    float AccelG = 0, float SeekerRangeMeters = 0, float BurnScale = 1, float FlightScale = 1,
    WeaponSize? PdClass = null, float RangeMeters = 0, float ShotsPerSecond = 0, float DamagePerHit = 0,
    float? MissileDamagePerHit = null, float MuzzleSpeed = 0, float TrackingScale = 1);

public static class OrdnanceParts
{
    public static IReadOnlyList<OrdnancePart> All { get; } = Load();
    public static OrdnancePart? Find(string id) => All.FirstOrDefault(p => p.Id == id);
    public static OrdnancePart ById(string id) => Find(id) ?? throw new InvalidDataException("알 수 없는 부품입니다: " + id);

    /// <summary>Missile launcher class a hull's magazine accepts: small craft, escort/cruiser, battleship.</summary>
    public static WeaponSize HullClass(ShipDefinition hull) => hull.Kind switch
    { HullKind.Interceptor => WeaponSize.Small, HullKind.Escort => WeaponSize.Medium, _ => WeaponSize.Large };
    /// <summary>Small craft carry light missiles (which also intercept); an escort-class hull with interceptor rounds is a counter hull.</summary>
    public static MissileFamily? Family(ShipDefinition hull) => hull.Missiles is not { } m ? null : hull.Kind switch
    {
        HullKind.Interceptor => MissileFamily.Light,
        HullKind.Escort => m.CanInterceptMissiles ? MissileFamily.Counter : MissileFamily.Medium,
        _ => MissileFamily.Heavy,
    };
    public static OrdnancePart StockLauncher => All.Single(p => p.Stock && p.Kind == OrdnanceKind.Launcher);
    public static OrdnancePart? StockMissile(ShipDefinition hull) => Family(hull) is { } f ? All.Single(p => p.Stock && p.Family == f) : null;
    public static OrdnancePart? StockPointDefense(ShipDefinition hull) => hull.PointDefense is null ? null
        : All.Single(p => p.Stock && p.Kind == OrdnanceKind.PointDefense && p.PdClass == HullClass(hull));

    /// <summary>Why this part cannot go on the hull, or null. Stock parts always fit their own hull.</summary>
    public static string? Socket(ShipDefinition hull, OrdnancePart part)
    {
        if (part.Stock) return part == StockLauncher || part == StockMissile(hull) || part == StockPointDefense(hull) ? null : "다른 계열의 기본 부품입니다.";
        if (part.Design is { } d && d != hull.Design) return "다른 진영의 부품입니다.";
        if (!part.Branches.Contains(hull.Branch)) return "이 함종에서 연구할 수 없는 부품입니다.";
        return part.Kind switch
        {
            OrdnanceKind.Launcher => hull.Missiles is null ? "미사일 발사관이 없는 함선입니다."
                : part.Size > HullClass(hull) ? "함체 미사일 계열보다 큰 발사기입니다." : null,
            OrdnanceKind.Missile => hull.Missiles is null ? "미사일 발사관이 없는 함선입니다."
                : part.Family != Family(hull) ? "이 발사관에 맞지 않는 미사일 계열입니다." : null,
            _ => hull.PointDefense is null ? "근접방어 포대가 없는 함선입니다."
                : part.PdClass != HullClass(hull) ? "이 함체 포대 등급에 맞지 않는 PD입니다." : null,
        };
    }

    public static IEnumerable<OrdnancePart> Candidates(ShipDefinition hull, OrdnanceKind kind)
        => All.Where(p => p.Kind == kind && Socket(hull, p) is null);

    private static float R(float actual, float stock) => stock == 0 ? 1 : actual / stock;

    /// <summary>Applied after main gun parts. Null or stock choices leave the hull (and its content ID) unchanged.</summary>
    internal static ShipDefinition Apply(ShipDefinition hull, string? launcherId, string? missileId, string? pdId)
    {
        OrdnancePart? Pick(string? id, OrdnancePart? stock)
        {
            if (id is null) return null;
            var p = ById(id);
            if (Socket(hull, p) is { } why) throw new InvalidDataException($"{p.Name}: {why}");
            return p == stock ? null : p;
        }
        var launcher = Pick(launcherId, StockLauncher); var missile = Pick(missileId, StockMissile(hull));
        var pd = Pick(pdId, StockPointDefense(hull));
        if (launcherId is not null && ById(launcherId).Kind != OrdnanceKind.Launcher || missileId is not null && ById(missileId).Kind != OrdnanceKind.Missile
            || pdId is not null && ById(pdId).Kind != OrdnanceKind.PointDefense) throw new InvalidDataException("부품 종류가 맞지 않습니다.");
        if (launcher is null && missile is null && pd is null) return hull;
        var json = JsonNode.Parse(hull.ToJson())!.AsObject();
        if (launcher is not null || missile is not null)
        {
            var m = hull.Missiles!; var next = m;
            if (missile is not null)
            {
                var s = StockMissile(hull)!;
                next = next with { Energy = missile.Energy * R(m.Energy, s.Energy), PenetrationMm = missile.PenetrationMm * R(m.PenetrationMm, s.PenetrationMm),
                    ModuleDamage = missile.ModuleDamage * R(m.ModuleDamage, s.ModuleDamage), HitPoints = missile.HitPoints * R(m.HitPoints, s.HitPoints),
                    AccelG = missile.AccelG * R(m.AccelG, s.AccelG), SeekerRangeMeters = missile.SeekerRangeMeters * R(m.SeekerRangeMeters, s.SeekerRangeMeters),
                    BurnSeconds = m.BurnSeconds * missile.BurnScale, MaxFlightSeconds = m.MaxFlightSeconds * missile.FlightScale };
            }
            if (launcher is not null)
                next = next with { Rounds = Math.Max(launcher.Cells, (int)MathF.Round(m.Rounds * launcher.MagazineScale)),
                    ReloadSeconds = m.ReloadSeconds * launcher.ReloadScale, Cells = launcher.Cells };
            json[nameof(ShipDefinition.Missiles)] = JsonNode.Parse(ShipDefinition.SerializeData(next));
        }
        if (pd is not null)
        {
            var d = hull.PointDefense!; var s = StockPointDefense(hull)!;
            float range = pd.RangeMeters * R(d.RangeMeters, s.RangeMeters), damage = pd.DamagePerHit * R(d.DamagePerHit, s.DamagePerHit);
            float missileDamage = (pd.MissileDamagePerHit ?? pd.DamagePerHit) * R(d.EffectiveMissileDamage, s.MissileDamagePerHit ?? s.DamagePerHit);
            var packet = d.EffectiveShipPacket;
            var next = d with { RangeMeters = range, ShotsPerSecond = pd.ShotsPerSecond * R(d.ShotsPerSecond, s.ShotsPerSecond),
                DamagePerHit = damage, MissileDamagePerHit = missileDamage, MuzzleSpeed = pd.MuzzleSpeed * R(d.MuzzleSpeed, s.MuzzleSpeed),
                YawRate = d.YawRate * pd.TrackingScale, ElevationRate = d.ElevationRate * pd.TrackingScale,
                ShipPacket = packet with { Energy = packet.Energy * damage / d.DamagePerHit, ModuleDamage = packet.ModuleDamage * damage / d.DamagePerHit, Range = range } };
            json[nameof(ShipDefinition.PointDefense)] = JsonNode.Parse(ShipDefinition.SerializeData(next));
        }
        return ShipDefinition.Parse(json.ToJsonString(), strict: true);
    }

    private static IReadOnlyList<OrdnancePart> Load()
    {
        using var stream = typeof(OrdnanceParts).Assembly.GetManifestResourceStream("SpaceFleet.data.weapons.ordnance.json")
            ?? throw new InvalidDataException("Missing ordnance part catalog");
        using var reader = new StreamReader(stream);
        var rows = ShipDefinition.ParseData<OrdnancePart[]>(reader.ReadToEnd());
        if (rows.Length == 0 || rows.Any(p => p is null) || rows.Select(p => p.Id).Distinct().Count() != rows.Length
            || rows.Any(p => MainGunParts.Find(p.Id) is not null || ShipComponents.All.Any(c => c.Id == p.Id)))
            throw new InvalidDataException("Invalid ordnance catalog (duplicate IDs across catalogs)");
        static bool Pos(float v) => float.IsFinite(v) && v > 0;
        foreach (var p in rows)
        {
            bool ok = !string.IsNullOrWhiteSpace(p.Name) && p.Id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') && Enum.IsDefined(p.Kind)
                && p.MinTier is >= 1 and <= 7 && p.Cost >= 0 && (p.Cost == 0) == p.Stock && p.Branches is not null
                && (p.Stock || p.Branches.Length > 0) && p.Branches.All(b => Enum.IsDefined(b));
            ok &= p.Kind switch
            {
                OrdnanceKind.Launcher => p.Cells is >= 1 and <= 16 && Pos(p.MagazineScale) && p.MagazineScale <= 1.5f && Pos(p.ReloadScale) && p.ReloadScale <= 3
                    && Enum.IsDefined(p.Size) && p.Family is null && p.PdClass is null,
                OrdnanceKind.Missile => p.Family is { } f && Enum.IsDefined(f) && Pos(p.Energy) && Pos(p.PenetrationMm) && Pos(p.ModuleDamage)
                    && Pos(p.HitPoints) && Pos(p.AccelG) && Pos(p.SeekerRangeMeters) && Pos(p.BurnScale) && Pos(p.FlightScale) && p.PdClass is null,
                _ => p.PdClass is { } c && Enum.IsDefined(c) && Pos(p.RangeMeters) && Pos(p.ShotsPerSecond) && Pos(p.DamagePerHit)
                    && (p.MissileDamagePerHit is null || Pos(p.MissileDamagePerHit.Value)) && Pos(p.MuzzleSpeed) && Pos(p.TrackingScale) && p.Family is null,
            };
            if (!ok) throw new InvalidDataException("Invalid ordnance part: " + p.Id);
        }
        if (rows.Count(p => p.Stock && p.Kind == OrdnanceKind.Launcher) != 1
            || Enum.GetValues<MissileFamily>().Any(f => rows.Count(p => p.Stock && p.Family == f) != 1)
            || new[] { WeaponSize.Small, WeaponSize.Medium, WeaponSize.Large }.Any(c => rows.Count(p => p.Stock && p.PdClass == c) != 1))
            throw new InvalidDataException("Ordnance catalog needs exactly one stock part per launcher, missile family and PD class");
        return Array.AsReadOnly(rows);
    }
}
