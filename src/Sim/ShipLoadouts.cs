using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SpaceFleet.Sim;

/// <summary>Ordered definition overrides followed by physical bay installations.</summary>
public sealed record ShipLoadout(string BaseHullId, string? TreePatch = null, IReadOnlyList<string>? UpgradePatches = null,
    IReadOnlyList<EquipmentMount>? Equipment = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PrimaryWeaponSelection? MainWeapon { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShipComponentSelection? Components { get; init; }
    /// <summary>Absolute main gun part per socket (null entry = stock part); applied after catalog systems.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string?>? MainGuns { get; init; }
}

public sealed record ResolvedLoadout(string LoadoutId, string BaseHullId, string ContentHash,
    string DefinitionJson, ShipDefinition Definition);

/// <summary>
/// Pure composition: base -> tree -> explicit primary -> upgrades -> catalog systems -> main gun parts -> bay equipment -> validation.
/// Objects merge recursively; arrays replace as a whole; null removes an optional field.
/// No arithmetic/string expression evaluation and no mutation of the base catalog.
/// </summary>
public static class ShipLoadouts
{
    public static ResolvedLoadout Resolve(ShipLoadout loadout)
        => Resolve(loadout,ShipDefinitions.ById);
    internal static ResolvedLoadout Resolve(ShipLoadout loadout,Func<string,ShipDefinition> referenceLookup)
    {
        ArgumentNullException.ThrowIfNull(loadout);
        var source = ReadObject(ShipDefinitions.SourceJson(loadout.BaseHullId));
        if (loadout.TreePatch is not null) Merge(source, ReadObject(loadout.TreePatch));
        if (loadout.MainWeapon is { } choice)
            source = ReadObject(MainBatteries.Select(ShipDefinition.Parse(source.ToJsonString(), strict: true), choice).ToJson());
        foreach (string patch in loadout.UpgradePatches ?? Array.Empty<string>())
        {
            var upgrade = ReadObject(patch);
            if (source.Any(p => p.Key.Equals(nameof(ShipDefinition.MainBattery), StringComparison.OrdinalIgnoreCase) && p.Value is JsonObject))
                AdaptWeaponUpgrade(upgrade);
            Merge(source, upgrade);
        }
        var definition = ShipDefinition.Parse(source.ToJsonString(), strict: true);
        if (loadout.MainWeapon is not null && loadout.Components?.MainGun is not null)
            throw new InvalidDataException("Choose a catalog primary or an explicit primary, not both");
        var mainGuns = loadout.MainGuns ?? loadout.Components?.MainGuns;
        if (mainGuns is not null && (loadout.MainWeapon is not null || loadout.Components?.MainGun is not null))
            throw new InvalidDataException("Choose main gun parts or a primary replacement, not both");
        if (loadout.Components?.PowerParts is { Length: > 0 } && (loadout.MainWeapon is not null || loadout.Components.MainGun is not null))
            throw new InvalidDataException("Power parts need socket main guns, not a primary replacement");
        definition = ShipComponents.Apply(definition, loadout.Components, useFactoryPrimary:loadout.MainWeapon is null);
        definition = MainGunParts.Apply(definition, mainGuns, loadout.Components?.PowerParts);
        if (loadout.Components is { } parts && (parts.Launcher ?? parts.Missile ?? parts.PointDefensePart) is not null)
        {
            if (parts.PointDefensePart is not null && parts.Secondary is not null)
                throw new InvalidDataException("Choose a point-defence part or a catalog secondary, not both");
            definition = OrdnanceParts.Apply(definition, parts.Launcher, parts.Missile, parts.PointDefensePart);
        }
        if (definition.InstalledEquipment.Length > 0)
            throw new InvalidDataException("Install equipment through bay selections, not definition patches");
        var mounts=(loadout.Equipment??Array.Empty<EquipmentMount>()).Concat(loadout.Components?.AuxiliaryParts??Array.Empty<EquipmentMount>()).ToArray();
        if (mounts.Length>0) definition = Install(definition, mounts);
        definition=TacticalModulesCatalog.Install(definition,loadout.Components?.TacticalModules);
        ShipRoleValidation.ValidateReferences(definition, referenceLookup);
        // Hash the validated, materialized values, including defaults. Property order and
        // whitespace in patches do not affect identity; array order deliberately does.
        string json = Canonical(ReadObject(definition.ToJson()));
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        return new($"{loadout.BaseHullId}@{hash}", loadout.BaseHullId, hash, json, definition);
    }

    // Existing research patches address railgun statistics. When the hull uses
    // the separated schema, apply those same absolute values to its weapon only.
    // Geometry/ammo edits must use the explicit socket/installation schema.
    private static void AdaptWeaponUpgrade(JsonObject patch)
    {
        string? key = patch.Select(p => p.Key).FirstOrDefault(k => k.Equals("railgun", StringComparison.OrdinalIgnoreCase));
        if (key is null) return;
        if (patch.Any(p => p.Key.Equals("mainBattery", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Upgrade cannot author both railgun and mainBattery");
        var value = patch[key]; patch.Remove(key);
        if (value is null) { patch[nameof(ShipDefinition.MainBattery)] = null; return; }
        if (value is not JsonObject stats) throw new InvalidDataException("Railgun upgrade must be an object");
        string[] allowed = { "MuzzleSpeed", "ReloadSeconds", "Energy", "PenetrationMm", "ModuleDamage", "MaxRange",
            "SensorErrorMeters", "SensorErrorPerKm", "VelocityError", "ShotHeatMj", "AimErrorScale" };
        if (stats.Any(p => !allowed.Contains(p.Key, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("Separated weapon upgrades may change ballistics only; use mainBattery for geometry");
        patch[nameof(ShipDefinition.MainBattery)] = new JsonObject {
            [nameof(MainBatteryDefinition.Weapon)] = new JsonObject { [nameof(PrimaryWeaponDefinition.Railgun)] = stats } };
    }

    private static ShipDefinition Install(ShipDefinition hull, IReadOnlyList<EquipmentMount> mounts)
    {
        if (mounts.Any(m => m is null) || mounts.Select(m => m.BayId).Distinct().Count() != mounts.Count)
            throw new InvalidDataException("Null/duplicate equipment bay selection");
        var modules = hull.Modules.ToList();
        var installed = new List<InstalledEquipment>();
        float mass = hull.Flight.MassKg;
        int drones = hull.DefenseDrones?.Count ?? 0;
        // Mount selection order is immaterial. Stable ownership intervals survive serialization.
        foreach (var mount in mounts.OrderBy(m => m.BayId, StringComparer.Ordinal))
        {
            var bay = hull.Bays.SingleOrDefault(b => b.Id == mount.BayId)
                ?? throw new InvalidDataException($"Unknown bay: {mount.BayId}");
            var device = ShipEquipment.ById(mount.EquipmentId);
            if (device.Size > bay.Size || !bay.Accepts.Contains(device.Category)
                || device.Designs is not null && !device.Designs.Contains(hull.Design))
                throw new InvalidDataException($"Incompatible equipment: {device.Id} / {bay.Id}");
            if (device.Effects.Drones > 0 && hull.DefenseDrones is null)
                throw new InvalidDataException("Drone expansion requires an existing drone system");
            string id = "fit-" + bay.Id;
            modules.Add(new(id, device.Name, device.Kind, bay.Center, bay.HalfSize, device.HitPoints,
                device.ResistanceMm, bay.Grid, CriticalChance: device.CriticalChance, CriticalEnergy: device.CriticalEnergy));
            installed.Add(new(bay.Id, id, device, device.Effects.Drones > 0 ? drones : -1));
            drones += device.Effects.Drones;
            mass += device.MassKg;
        }
        float ratio = hull.Flight.MassKg / mass;
        var flight = hull.Flight with { MassKg = mass, ForwardAccel = hull.Flight.ForwardAccel * ratio,
            StrafeAccel = hull.Flight.StrafeAccel * ratio, BrakeAccel = hull.Flight.BrakeAccel * ratio };
        var result = ReadObject(hull.ToJson());
        result[nameof(ShipDefinition.Flight)] = JsonNode.Parse(ShipDefinition.SerializeData(flight));
        result[nameof(ShipDefinition.Modules)] = JsonNode.Parse(ShipDefinition.SerializeData(modules));
        result[nameof(ShipDefinition.InstalledEquipment)] = JsonNode.Parse(ShipDefinition.SerializeData(installed));
        if (hull.DefenseDrones is { } defense)
            result[nameof(ShipDefinition.DefenseDrones)] = JsonNode.Parse(ShipDefinition.SerializeData(defense with { Count = drones }));
        return ShipDefinition.Parse(result.ToJsonString(), strict: true);
    }

    private static JsonObject ReadObject(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        CheckNames(document.RootElement);
        return JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("Loadout patch must be a JSON object");
    }

    private static void CheckNames(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException($"Duplicate loadout field: {property.Name}");
                CheckNames(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) CheckNames(item);
    }

    private static void Merge(JsonObject target, JsonObject patch)
    {
        foreach (var (key, value) in patch)
        {
            string name = target.Select(p => p.Key).FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? key;
            if (value is null)
            {
                // Preserve unknown null fields for strict schema validation (do not hide typos).
                if (target.ContainsKey(name)) target.Remove(name);
                else target[name] = null;
            }
            else if (value is JsonObject child)
            {
                if (target[name] is not JsonObject nested) target[name] = nested = new JsonObject();
                Merge(nested, child);
            }
            else target[name] = value.DeepClone();
        }
    }

    private static string Canonical(JsonNode source)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(source, writer);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(JsonNode? value, Utf8JsonWriter writer)
    {
        if (value is JsonObject obj)
        {
            writer.WriteStartObject();
            foreach (var (key, item) in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
            { writer.WritePropertyName(key); Write(item, writer); }
            writer.WriteEndObject();
        }
        else if (value is JsonArray array)
        {
            writer.WriteStartArray();
            foreach (var item in array) Write(item, writer);
            writer.WriteEndArray();
        }
        else if (value is null) writer.WriteNullValue();
        else value.WriteTo(writer);
    }
}
