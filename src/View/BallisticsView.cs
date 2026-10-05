using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.View;

/// <summary>시뮬레이션 위치를 그리는 예광과 명중 섬광. 판정에는 관여하지 않는다.</summary>
public partial class BallisticsView : Node3D
{
    private readonly Dictionary<uint, MeshInstance3D> _trails = new();
    private readonly Dictionary<uint, MeshInstance3D> _hits = new();
    private readonly CylinderMesh _trailMesh = new() { Height = 1, TopRadius = 1, BottomRadius = 1, RadialSegments = 6 };
    private readonly SphereMesh _hitMesh = new() { Radius = 1, Height = 2, RadialSegments = 12, Rings = 6 };
    private readonly StandardMaterial3D _blue = Glow(new Color(0.25f, 0.8f, 1f));
    private readonly StandardMaterial3D _red = Glow(new Color(1f, 0.35f, 0.15f));

    public void Sync(SimWorld world, Vec3d origin, double alpha)
    {
        var active = new HashSet<uint>();
        foreach (RailProjectile p in world.Projectiles)
        {
            active.Add(p.Id);
            if (!_trails.TryGetValue(p.Id, out MeshInstance3D? trail))
            {
                trail = Make(_trailMesh, p.Shooter.Faction == Faction.Blue ? _blue : _red);
                _trails.Add(p.Id, trail);
            }
            Vec3d position = Vec3d.Lerp(p.PrevPosition, p.Position, alpha);
            Vector3 direction = p.Velocity.Normalized();
            if (direction.LengthSquared() < 0.1f) { trail.Visible = false; continue; }
            float length = (float)Math.Min(p.Velocity.Length() * 0.035, p.Velocity.Length() * p.Age);
            float radius = Mathf.Clamp((float)(position - origin).Length() * 0.00003f, 0.12f, 3f);
            Vector3 axis = Mathf.Abs(direction.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up;
            Vector3 x = axis.Cross(direction).Normalized(), z = x.Cross(direction).Normalized();
            trail.Transform = new Transform3D(new Basis(x * radius, direction * Mathf.Max(0.01f, length), z * radius),
                (position - origin).ToVector3() - direction * length * 0.5f);
        }
        Prune(_trails, active);
        active.Clear();
        foreach (ProjectileImpact impact in world.Impacts)
        {
            double age = world.Time - impact.Time;
            if (age > 0.3) continue;
            active.Add(impact.Id);
            if (!_hits.TryGetValue(impact.Id, out MeshInstance3D? flash))
            {
                flash = Make(_hitMesh, impact.Hit.ShieldStopped ? _blue : _red);
                _hits.Add(impact.Id, flash);
            }
            float radius = Mathf.Max(2, (impact.Hit.Target?.Class.Length ?? 30) * 0.025f) * (float)(1 - age / 0.3);
            flash.Position = (impact.Hit.Point - origin).ToVector3();
            flash.Scale = Vector3.One * Mathf.Max(0.01f, radius);
        }
        Prune(_hits, active);
    }

    private MeshInstance3D Make(Mesh mesh, Material material)
    {
        var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(node);
        return node;
    }
    private static void Prune(Dictionary<uint, MeshInstance3D> nodes, HashSet<uint> active)
    {
        foreach (uint id in nodes.Keys.Where(id => !active.Contains(id)).ToArray())
        { nodes[id].QueueFree(); nodes.Remove(id); }
    }
    private static StandardMaterial3D Glow(Color color) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = color,
        EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 4,
    };
}
