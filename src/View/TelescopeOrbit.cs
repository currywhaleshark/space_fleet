using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.View;

/// <summary>A constant-height exterior optic, fitted once to the authored superstructure.</summary>
public readonly record struct TelescopeOrbit(Vector3 Center, float Radius)
{
    public Vector3 Offset(Vector3 aim)
    {
        Vector3 outward = new Vector3(aim.X, 0, aim.Z).Normalized();
        if (outward.LengthSquared() < .01f) outward = Vector3.Forward;
        return Center + outward * Radius;
    }

    public static TelescopeOrbit Fit(ShipDefinition definition, IEnumerable<Aabb>? visualBounds = null)
    {
        float clearance = Mathf.Max(1, definition.Flight.Length * .01f);
        var bridge = definition.HullSections.FirstOrDefault(s => s.Id == "bridge");
        Vector3 center = bridge is not null ? bridge.Center + Vector3.Up * bridge.HalfSize.Y * .6f
            : Vector3.Up * definition.Flight.CameraHeight * .25f;
        float radius = 0;
        void Include(Aabb box)
        {
            // Include everything above the optic, including tall forward command blocks.
            // Leave the lower deck in view when looking down, as in the normal bridge optic.
            if (bridge is not null && box.End.Y < center.Y - clearance) return;
            float x = Mathf.Max(Mathf.Abs(box.Position.X-center.X), Mathf.Abs(box.End.X-center.X));
            float z = Mathf.Max(Mathf.Abs(box.Position.Z-center.Z), Mathf.Abs(box.End.Z-center.Z));
            radius = Mathf.Max(radius, new Vector2(x,z).Length());
        }
        foreach (var section in definition.HullSections)
            Include(new Aabb(section.Center-section.HalfSize, section.HalfSize*2));
        if (visualBounds is not null) foreach (var box in visualBounds) Include(box);
        return new(center, radius + clearance);
    }

    // Local transforms work before the model enters the scene tree. Never include exhaust or flashes.
    internal static IEnumerable<Aabb> ModelBounds(Node3D model)
    {
        return Walk(model, Transform3D.Identity);
        static IEnumerable<Aabb> Walk(Node3D node, Transform3D parent)
        {
            if (!node.Visible || node.Name == "Plume" || node.Name == "Rcs") yield break;
            var pose = parent * node.Transform;
            if (node is MeshInstance3D { Mesh: not null } mesh)
            {
                // FleetHull is one merged mesh: its whole AABB would push the camera past
                // the bow. Individual triangles keep the optic fitted to the upper structure.
                foreach (var box in TriangleBounds(mesh.Mesh,pose)) yield return box;
            }
            foreach (Node child in node.GetChildren())
                if (child is Node3D spatial)
                    foreach (var box in Walk(spatial,pose)) yield return box;
        }
    }

    private static IEnumerable<Aabb> TriangleBounds(Mesh mesh, Transform3D pose)
    {
        for (int surface=0;surface<mesh.GetSurfaceCount();surface++)
        {
            var arrays=mesh.SurfaceGetArrays(surface);
            var vertices=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var indices=arrays[(int)Mesh.ArrayType.Index].VariantType==Variant.Type.Nil
                ? System.Array.Empty<int>() : arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            int count=indices.Length>0?indices.Length:vertices.Length;
            for(int i=0;i+2<count;i+=3)
            {
                Vector3 a=pose*vertices[indices.Length>0?indices[i]:i];
                Vector3 b=pose*vertices[indices.Length>0?indices[i+1]:i+1];
                Vector3 c=pose*vertices[indices.Length>0?indices[i+2]:i+2];
                yield return new Aabb(a,Vector3.Zero).Expand(b).Expand(c);
            }
        }
    }
}
