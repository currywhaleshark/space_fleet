using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

/// <summary>전장 절대 좌표의 자유 지도. 함선 조작·렌더 원점·자함 위치에 종속되지 않는다.</summary>
public sealed class WorldMapState
{
    public Vec3d Center { get; private set; }
    public double HalfSpan { get; private set; } = 100_000;
    public float Yaw { get; private set; } = -.35f;
    public float Pitch { get; private set; } = .78f;
    public bool Initialized { get; private set; }
    private Basis View => new Basis(Vector3.Right, Pitch) * new Basis(Vector3.Up, Yaw);

    public void Fit(IEnumerable<Vec3d> positions)
    {
        Vec3d[] points = positions.ToArray();
        if (points.Length == 0) return;
        var min = new Vec3d(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z));
        var max = new Vec3d(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z));
        Center = min + (max - min) * .5;
        HalfSpan = Math.Clamp(points.Max(p => (p - Center).Length()) * 1.2, 5000, 5e8);
        Initialized = true;
    }

    public Vector3 Project(Vec3d position)
        => ProjectUnit(((position - Center) * (1 / HalfSpan)).ToVector3());

    public Vector3 ProjectUnit(Vector3 normalized)
    {
        Vector3 p = View * normalized;
        return new Vector3(p.X, -p.Y, p.Z);
    }

    public bool Contains(Vec3d position) => (position - Center).LengthSquared() <= HalfSpan * HalfSpan;

    public Vec3d ScreenPlane(Vector2 normalized) => Center
        + Vec3d.From(View.Inverse() * new Vector3(normalized.X, -normalized.Y, 0)) * HalfSpan;

    /// <summary>Where the cursor ray meets the horizontal plane at world height <paramref name="y"/>; null when the view is edge-on.</summary>
    public Vec3d? ScreenOnLevel(Vector2 normalized, double y)
    {
        Basis inverse = View.Inverse();
        Vector3 along = inverse * new Vector3(normalized.X, -normalized.Y, 0), depth = inverse * Vector3.Back;
        if (Mathf.Abs(depth.Y) < .05f) return null;
        double t = ((y - Center.Y) / HalfSpan - along.Y) / depth.Y;
        return Center + Vec3d.From(along + depth * (float)t) * HalfSpan;
    }
    /// <summary>World metres per screen pixel of vertical cursor travel, for raising or lowering a point.
    /// A near top-down view is clamped so a small drag cannot jump tens of kilometres.</summary>
    public double HeightPerPixel(float radiusPixels) => HalfSpan / (radiusPixels * Math.Max(.25, Math.Cos(Pitch)));

    public void Pan(Vector2 normalizedDelta) => Center -= ScreenPlane(normalizedDelta) - Center;
    public void Zoom(float factor, Vector2 anchor)
    {
        if (!float.IsFinite(factor) || factor <= 0) return;
        Vec3d before = ScreenPlane(anchor);
        HalfSpan = Math.Clamp(HalfSpan * factor, 250, 5e8);
        Center += before - ScreenPlane(anchor);
    }
    public void Orbit(Vector2 pixels)
    {
        Yaw = Mathf.Wrap(Yaw - pixels.X * .006f, -Mathf.Pi, Mathf.Pi);
        Pitch = Mathf.Clamp(Pitch + pixels.Y * .006f, .12f, 1.48f);
    }

    public static Vec3d? KnownPosition(Faction observer, ShipBody target, SensorTrack track)
        => target.Faction == observer ? target.Position : track.Level == TrackLevel.None ? null
            : target.Position + Vec3d.From(track.Offset);

}
