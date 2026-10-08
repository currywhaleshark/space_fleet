using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.View;

/// <summary>Blender GLB의 정적 외형에 기존 추진·자세 제어 이펙트를 연결한다.</summary>
public static class ImportedShipModels
{
    private static readonly Dictionary<string, PackedScene> Scenes = new();
    private static JsonDocument? _manifest;
    private static Vector3 V(JsonElement a) => new(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle());

    public static ShipModel? TryBuild(ShipClass cls, Faction faction, int seed)
    {
        string id = cls.Kind.ToString().ToLowerInvariant();
        string path = $"res://assets/ships/{id}.glb";
        if (!ResourceLoader.Exists(path)) return null;
        if (!Scenes.TryGetValue(id, out PackedScene? scene)) Scenes[id] = scene = GD.Load<PackedScene>(path);
        if (scene is null) return null;
        if (_manifest is null)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SpaceFleet.assets.ships.manifest.json")
                ?? throw new InvalidOperationException("Missing ship model FX manifest");
            _manifest = JsonDocument.Parse(stream);
        }
        var def = _manifest.RootElement.GetProperty("ships").EnumerateArray().First(s => s.GetProperty("id").GetString() == id);
        var b = new HullBuilder(Palette.For(faction, cls.Length / 10), seed);
        var visual = scene.Instantiate<Node3D>();
        visual.Name = "BlenderHull";
        b.Root.AddChild(visual);
        var accent = new StandardMaterial3D { AlbedoColor = faction == Faction.Blue ? new(.16f,.34f,.46f) : new(.48f,.12f,.07f), Metallic = .55f, Roughness = .4f };
        var shield = new StandardMaterial3D { AlbedoColor = new(.02f,.1f,.12f), EmissionEnabled = true,
            Emission = faction == Faction.Blue ? new(.08f,.5f,.8f) : new(.8f,.22f,.08f), EmissionEnergyMultiplier = .7f };
        foreach (MeshInstance3D mesh in Meshes(visual))
        for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
        {
            string name = mesh.Mesh.SurfaceGetMaterial(i)?.ResourceName ?? "";
            Material? replacement = name.StartsWith("EngineGlow") ? b.Palette.Glow
                : name.StartsWith("IFF") ? accent : name.StartsWith("ShieldGlow") ? shield : null;
            if (replacement is not null) mesh.SetSurfaceOverrideMaterial(i, replacement);
        }
        foreach (var engine in def.GetProperty("engines").EnumerateArray().OrderBy(e => e.GetProperty("index").GetInt32()))
            b.Engine(V(engine.GetProperty("position")), engine.GetProperty("radius").GetSingle(), engine.GetProperty("plume").GetSingle(), buildNozzle: false);
        foreach (var jet in def.GetProperty("rcs").EnumerateArray())
            b.RcsNozzle(V(jet.GetProperty("position")), V(jet.GetProperty("exhaust")), jet.GetProperty("size").GetSingle(), jet.GetProperty("plume").GetSingle(), buildNozzle: false);
        var turrets = new List<TurretRig>();
        foreach (TurretDefinition mount in ShipDefinitions.For(cls.Kind).Railgun?.Mounts ?? Array.Empty<TurretDefinition>())
        {
            string key = mount.ModuleId.Replace('-', '_');
            Node3D Required(string name) => visual.FindChild(name, recursive: true, owned: false) as Node3D
                ?? throw new InvalidOperationException($"{id}: missing Blender turret node {name}");
            Node3D yaw = Required("turret_" + key);
            turrets.Add(new(mount.ModuleId, yaw, yaw.Basis, Required("elevation_" + key), Required("recoil_" + key),
                Enumerable.Range(0, mount.Muzzles.Length).Select(i => Required($"muzzle_{key}_{i}")).ToArray()));
        }
        return b.Finish() with { Turrets = turrets };
    }

    internal static IEnumerable<MeshInstance3D> Meshes(Node root)
    {
        if (root is MeshInstance3D mesh) yield return mesh;
        foreach (Node child in root.GetChildren())
            foreach (MeshInstance3D nested in Meshes(child)) yield return nested;
    }
}
