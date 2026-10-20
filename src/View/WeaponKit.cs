using System.Collections.Generic;
using Godot;

namespace SpaceFleet.View;

/// <summary>
/// Blender weapon kit (assets/weapons/weapon_kit.glb, built by tools/build-weapon-kit.py). Each piece is one unit wide,
/// its back at the origin and running one unit forward (-Z). Surfaces are sorted by kit material into armour, dark and
/// glow so a part can be merged into the usual palette-coloured barrel mesh.
/// </summary>
public static class WeaponKit
{
    public const string Path = "res://assets/weapons/weapon_kit.glb";
    public enum Slot { Armour, Dark, Glow }
    private static Dictionary<string, Mesh>? _pieces;

    public static bool Available => Pieces.Count > 0;
    private static Dictionary<string, Mesh> Pieces => _pieces ??= Load();

    private static Dictionary<string, Mesh> Load()
    {
        var pieces = new Dictionary<string, Mesh>();
        if (!ResourceLoader.Exists(Path)) return pieces;
        var root = GD.Load<PackedScene>(Path).Instantiate();
        void Walk(Node n)
        {
            if (n is MeshInstance3D m && m.Mesh is { } mesh && m.Name.ToString().StartsWith("kit_")) pieces[m.Name] = mesh;
            foreach (Node c in n.GetChildren()) Walk(c);
        }
        Walk(root);
        root.Free();
        return pieces;
    }

    private static Slot SlotOf(Material? m) => m?.ResourceName switch
    {
        "kit_dark" => Slot.Dark,
        "kit_glow" or "kit_lens" => Slot.Glow,
        _ => Slot.Armour,
    };

    /// <summary>Appends a kit piece, transformed, to the surface tool of each of its materials.</summary>
    public static void Append(IReadOnlyDictionary<Slot, SurfaceTool> tools, string piece, Transform3D transform)
    {
        var mesh = Pieces[piece];
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            tools[SlotOf(mesh.SurfaceGetMaterial(s))].AppendFrom(mesh, s, transform);
    }

    /// <summary>A piece stretched from z = back to z = front (front more negative), with the given width and height.</summary>
    public static Transform3D Span(float x, float y, float back, float front, float width, float height)
        => new(Basis.FromScale(new Vector3(width, height, back - front)), new Vector3(x, y, back));
}
