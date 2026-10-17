using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace SpaceFleet.Sim;

/// <summary>Tree branches without progression state; every node composes a validated hull.</summary>
public sealed record ShipVariantDefinition(string Id, string BaseHullId, int Tier, JsonObject Patch);
public sealed record ShipVariant(ShipVariantDefinition Node, ResolvedLoadout Loadout)
{
    public ShipDefinition Definition => Loadout.Definition;
}

public static class ShipVariants
{
    private static readonly IReadOnlyDictionary<string, ShipVariant> Catalog = Load();
    public static IEnumerable<ShipVariant> All => Catalog.Values;
    public static ShipVariant ById(string id) => Catalog.TryGetValue(id, out var variant)
        ? variant : throw new ArgumentException($"Unknown variant ID: {id}");
    public static ResolvedLoadout Resolve(string id, IReadOnlyList<string>? upgrades=null, IReadOnlyList<EquipmentMount>? equipment=null)
        => Resolve(id, upgrades, equipment, null);
    public static ResolvedLoadout Resolve(string id, IReadOnlyList<string>? upgrades, IReadOnlyList<EquipmentMount>? equipment,
        PrimaryWeaponSelection? mainWeapon)
    {
        var node=ById(id).Node;
        return ShipLoadouts.Resolve(new(node.BaseHullId,node.Patch.ToJsonString(),upgrades,equipment) { MainWeapon = mainWeapon });
    }
    internal static bool TryGet(string id, out ShipVariant variant) => Catalog.TryGetValue(id, out variant!);
    private static IReadOnlyDictionary<string, ShipVariant> Load()
    {
        const string prefix="SpaceFleet.data.variants.";
        var catalog=new Dictionary<string,ShipVariant>(StringComparer.Ordinal);
        var nodes=new Dictionary<string,ShipVariantDefinition>(StringComparer.Ordinal);
        foreach(string name in typeof(ShipVariants).Assembly.GetManifestResourceNames()
            .Where(n=>n.StartsWith(prefix,StringComparison.Ordinal)&&n.EndsWith(".json",StringComparison.Ordinal)).OrderBy(n=>n,StringComparer.Ordinal))
        {
            using var stream=typeof(ShipVariants).Assembly.GetManifestResourceStream(name)!;
            using var reader=new StreamReader(stream);
            var node=ShipDefinition.ParseData<ShipVariantDefinition>(reader.ReadToEnd());
            if(string.IsNullOrWhiteSpace(node.Id)||node.Tier<1||node.Patch is null||name!=prefix+node.Id+".json"
                || ShipDefinitions.All.Any(d=>d.Id==node.Id)) throw new InvalidDataException($"Invalid variant: {name}");
            nodes.Add(node.Id,node);
        }
        // Resolve squad-member references against this catalog while it is being built.
        // This supports high-tier battleships whose default escorts are other variants.
        var visiting=new HashSet<string>(StringComparer.Ordinal);
        ShipDefinition Lookup(string id)
        {
            var original=ShipDefinitions.All.FirstOrDefault(d=>d.Id==id);
            if(original is not null)return original;
            if(catalog.TryGetValue(id,out var done))return done.Definition;
            if(!nodes.TryGetValue(id,out var node))throw new ArgumentException($"Unknown variant member: {id}");
            if(!visiting.Add(id))throw new InvalidDataException($"Cyclic variant squad reference: {id}");
            var resolved=ShipLoadouts.Resolve(new(node.BaseHullId,node.Patch.ToJsonString()),Lookup);
            if(resolved.Definition.Id!=id)throw new InvalidDataException($"Variant identity mismatch: {id}");
            catalog.Add(id,new(node,resolved));visiting.Remove(id);return resolved.Definition;
        }
        foreach(string id in nodes.Keys)Lookup(id);
        return catalog;
    }
}
