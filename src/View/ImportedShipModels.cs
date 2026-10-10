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

    public static ShipModel? TryBuild(ShipDefinition definition, Faction faction, int seed)
    {
        ShipClass cls = definition.Flight;
        string id = definition.Id;
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
        var b = new HullBuilder(Palette.For(faction, cls.Length / 10, definition.Design), seed);
        var visual = scene.Instantiate<Node3D>();
        visual.Name = "BlenderHull";
        b.Root.AddChild(visual);
        var accent = new StandardMaterial3D { AlbedoColor = faction == Faction.Blue ? new(.16f,.34f,.46f) : new(.48f,.12f,.07f), Metallic = .55f, Roughness = .4f };
        var shield = new StandardMaterial3D { AlbedoColor = new(.02f,.1f,.12f), EmissionEnabled = true,
            Emission = faction == Faction.Blue ? new(.08f,.5f,.8f) : new(.8f,.22f,.08f), EmissionEnergyMultiplier = .7f };
        foreach (MeshInstance3D mesh in Meshes(visual))
        for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
        {
            Material? source = mesh.Mesh.SurfaceGetMaterial(i);
            string name = source?.ResourceName ?? "";
            Material? replacement = name.StartsWith("EngineGlow") ? b.Palette.Glow
                : name.StartsWith("IFF") ? accent : name.StartsWith("ShieldGlow") ? shield
                : HullSurface(id, name, source as BaseMaterial3D, cls.Length, definition.Design);
            if (replacement is not null) mesh.SetSurfaceOverrideMaterial(i, replacement);
        }
        foreach (var engine in def.GetProperty("engines").EnumerateArray().OrderBy(e => e.GetProperty("index").GetInt32()))
            b.Engine(V(engine.GetProperty("position")), engine.GetProperty("radius").GetSingle(), engine.GetProperty("plume").GetSingle(), buildNozzle: false);
        foreach (var jet in def.GetProperty("rcs").EnumerateArray())
            b.RcsNozzle(V(jet.GetProperty("position")), V(jet.GetProperty("exhaust")), jet.GetProperty("size").GetSingle(), jet.GetProperty("plume").GetSingle(), buildNozzle: false);
        var turrets = new List<TurretRig>();
        Node3D Required(string name) => visual.FindChild(name, recursive: true, owned: false) as Node3D
            ?? throw new InvalidOperationException($"{id}: missing Blender turret node {name}. Run tools/build.ps1 to refresh model imports.");
        foreach (TurretDefinition mount in definition.Railgun?.Mounts ?? Array.Empty<TurretDefinition>())
        {
            string key = mount.ModuleId.Replace('-', '_');
            Node3D yaw = Required("turret_" + key);
            turrets.Add(new(mount.ModuleId, yaw, yaw.Basis, Required("elevation_" + key), Required("recoil_" + key),
                Enumerable.Range(0, mount.Muzzles.Length).Select(i => Required($"muzzle_{key}_{i}")).ToArray()));
        }
        var defense = new List<PointDefenseRig>();
        for(int i=0;i<(definition.PointDefense?.Mounts.Length ?? 0);i++)
        {
            var yaw=Required($"pd_yaw_{i}");
            defense.Add(new(i,yaw,yaw.Basis,Required($"pd_pitch_{i}"),
                Enumerable.Range(0,2).Select(j=>Required($"pd_muzzle_{i}_{j}")).ToArray()));
        }
        return b.Finish() with { Turrets = turrets, PointDefense = defense };
    }

    private static readonly Dictionary<(string Ship, string Material), ShaderMaterial> HullSurfaces = new();
    private static Shader? _hullShader;

    /// <summary>
    /// 장갑·모서리·홈 재질을 선체 패널 셰이더로 바꾼다(Blender 기본색 유지). 판 크기는 함선 길이에 비례한다.
    /// 작업등(주황 점)은 장갑판에만, 화성 설계는 더 많고 붉게.
    /// </summary>
    private static ShaderMaterial? HullSurface(string ship, string name, BaseMaterial3D? source, float length, DesignFamily design)
    {
        if (source is null) return null;
        (float Variation, float Hatch, float Lights, float Seam)? style =
            name.StartsWith("Armor - graphite") ? (.13f, .35f, 1f, .022f)
            : name.StartsWith("Armor - raised") ? (.1f, .25f, .6f, .02f)
            : name.StartsWith("Edge") ? (.07f, 0f, 0f, .03f)
            : name.StartsWith("Recess") ? (.06f, .5f, 0f, .03f)
            : name.StartsWith("Markings") ? (.05f, 0f, 0f, .03f)
            : null;
        if (style is not { } s) return null;
        if (HullSurfaces.TryGetValue((ship, name), out var cached)) return cached;
        bool mars = design == DesignFamily.Mars;
        var m = new ShaderMaterial { Shader = _hullShader ??= GD.Load<Shader>("res://shaders/hull_panels.gdshader") };
        Color c = source.AlbedoColor;
        // 시안처럼 어두운 흑연색을 유지한다(거칠기를 올린 만큼 확산광이 늘어 밝아지는 것을 되돌린다).
        // 흰 표식은 시안처럼 바랜 상아색으로(큰 흰 면이 해를 받아 번쩍이지 않게).
        float shade = name.StartsWith("Markings") ? .45f : .72f;
        m.SetShaderParameter("base_color", new Vector3(c.R, c.G, c.B) * shade);
        m.SetShaderParameter("metallic_value", source.Metallic);
        // 평평한 큰 면이 해를 거울처럼 반사해 하얗게 타는 것을 막는다.
        m.SetShaderParameter("roughness_value", Mathf.Max(source.Roughness, .56f));
        m.SetShaderParameter("panel", Mathf.Clamp(length * .011f, .6f, 16f));
        m.SetShaderParameter("seam", s.Seam);
        m.SetShaderParameter("variation", s.Variation);
        m.SetShaderParameter("hatch", s.Hatch);
        m.SetShaderParameter("light_density", s.Lights * (mars ? .07f : .045f));
        m.SetShaderParameter("light_color", mars ? new Vector3(1f, .3f, .05f) : new Vector3(1f, .55f, .16f));
        m.SetShaderParameter("light_energy", mars ? 5f : 3.5f);
        m.SetShaderParameter("seed", (ship.GetHashCode() & 255) * .37f);
        HullSurfaces[(ship, name)] = m;
        return m;
    }

    internal static IEnumerable<MeshInstance3D> Meshes(Node root)
    {
        if (root is MeshInstance3D mesh) yield return mesh;
        foreach (Node child in root.GetChildren())
            foreach (MeshInstance3D nested in Meshes(child)) yield return nested;
    }
}
