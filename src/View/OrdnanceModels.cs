using System.Collections.Generic;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.View;

/// <summary>Missile cell hatches (modular weapons §10, §22): one Blender kit hatch per launcher cell, where each cell fires.</summary>
public static class OrdnanceModels
{
    public const float HatchMeters = 2.2f;

    public static void Apply(Node3D hull, ShipDefinition def, Palette palette)
    {
        if (def.Missiles is not { CellCount: > 1 } missiles || !WeaponKit.Available) return;
        var armour = new SurfaceTool(); armour.Begin(Mesh.PrimitiveType.Triangles);
        var dark = new SurfaceTool(); dark.Begin(Mesh.PrimitiveType.Triangles);
        var tools = new Dictionary<WeaponKit.Slot, SurfaceTool> { [WeaponKit.Slot.Armour] = armour, [WeaponKit.Slot.Dark] = dark, [WeaponKit.Slot.Glow] = dark };
        var cells = new Node3D { Name = "MissileCells" };
        int n = missiles.CellCount;
        for (int i = 0; i < n; i++)
        {
            // Same spacing as OrdnanceState.CellOffset, so each hatch sits where its missile leaves.
            var at = missiles.LaunchPoint + Vector3.Right * 2.5f * (i - (n - 1) * .5f);
            WeaponKit.Append(tools, "kit_cell_hatch", new Transform3D(Basis.FromScale(Vector3.One * HatchMeters), at - Vector3.Up * HatchMeters * .05f));
            cells.AddChild(new Node3D { Name = $"cell_{i}", Position = at });
        }
        // Housing under the row, sunk into the hull so the cells read as one module rather than floating lids.
        float width = n * 2.5f + .6f;
        var housing = new SurfaceTool(); housing.Begin(Mesh.PrimitiveType.Triangles);
        MeshKit.AddTaper(housing, missiles.LaunchPoint + new Vector3(0, -HatchMeters * .05f - 4f, 1.5f), new Vector2(width, 8f),
            missiles.LaunchPoint + new Vector3(0, -HatchMeters * .05f - 4f, -1.5f), new Vector2(width, 8f));
        housing.GenerateNormals();
        var mesh = armour.Commit(); dark.Commit(mesh); housing.Commit(mesh);
        mesh.SurfaceSetMaterial(0, palette.Hull); mesh.SurfaceSetMaterial(1, palette.Dark); mesh.SurfaceSetMaterial(2, palette.Dark);
        cells.AddChild(new MeshInstance3D { Name = "MissileCellHatches", Mesh = mesh });
        hull.AddChild(cells);
    }
}
