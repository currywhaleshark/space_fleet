using System;
using System.Collections;
using System.Collections.Generic;
using Godot;

namespace SpaceFleet.Sim;

public enum ObstacleShape { Sphere, Capsule }
public readonly record struct ObstaclePart(ObstacleShape Shape, Vec3d A, Vec3d B, double Radius)
{
    public static ObstaclePart Sphere(Vec3d center, double radius) => new(ObstacleShape.Sphere, center, center, radius);
    public static ObstaclePart Capsule(Vec3d a, Vec3d b, double radius) => new(ObstacleShape.Capsule, a, b, radius);
}

/// <summary>Fixed, infinite-mass simulation geometry. All endpoints are obstacle-local metres.</summary>
public sealed class Obstacle
{
    public string Name { get; }
    public Vec3d Position { get; }
    public Quaternion Orientation { get; }
    public IReadOnlyList<ObstaclePart> Parts { get; }
    public double BoundingRadius { get; }
    internal DoubleRotation Rotation { get; }

    public Obstacle(string name, Vec3d position, Quaternion orientation, params ObstaclePart[] parts)
    {
        if (!ObstacleMath.Finite(position) || parts.Length == 0) throw new ArgumentException("Invalid obstacle");
        Name = name; Position = position; Orientation = orientation; Rotation = new(orientation);
        var copy = (ObstaclePart[])parts.Clone();
        foreach (var part in copy)
        {
            if (!ObstacleMath.Finite(part.A) || !ObstacleMath.Finite(part.B) || !double.IsFinite(part.Radius) || part.Radius <= 0
                || (part.Shape == ObstacleShape.Sphere && part.A != part.B)) throw new ArgumentException("Invalid obstacle primitive");
            BoundingRadius = Math.Max(BoundingRadius, Math.Max(part.A.Length(), part.B.Length()) + part.Radius);
        }
        Parts = Array.AsReadOnly(copy);
    }
    public Vec3d ToWorld(Vec3d local) => Position + Rotation.Apply(local);
    public Vec3d ToLocal(Vec3d world) => Rotation.Inverse(world - Position);
}

public readonly record struct ObstacleHit(Obstacle Obstacle, int Part, Vec3d Position, Vec3d Normal, double Fraction);
public readonly record struct ObstacleProximity(Obstacle Obstacle, int Part, Vec3d Position, Vec3d Normal, double Clearance);
public readonly record struct ObstacleBoxContact(Vec3d Position, Vec3d Normal, double Penetration);

/// <summary>Single query boundary; the broad phase can later be replaced without changing callers or hit ordering.</summary>
public sealed class ObstacleSet : IReadOnlyList<Obstacle>
{
    private readonly List<Obstacle> _items = new();
    public int Count => _items.Count;
    public Obstacle this[int index] => _items[index];
    public void Add(Obstacle obstacle) => _items.Add(obstacle);
    public void Clear() => _items.Clear();
    public IEnumerator<Obstacle> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool SegmentBlocked(Vec3d a, Vec3d b, out ObstacleHit hit, double padding = 0)
    {
        hit = default;
        if (Count == 0) return false;
        double nearest = double.PositiveInfinity;
        foreach (var obstacle in _items)
        {
            if (!ObstacleMath.SegmentSphere(a - obstacle.Position, b - obstacle.Position, Vec3d.Zero,
                obstacle.BoundingRadius + padding, out _)) continue;
            Vec3d start = obstacle.ToLocal(a), end = obstacle.ToLocal(b);
            for (int i = 0; i < obstacle.Parts.Count; i++)
            {
                var part = obstacle.Parts[i];
                if (!ObstacleMath.SegmentCapsule(start, end, part.A, part.B, part.Radius + padding, out double t) || t >= nearest) continue;
                nearest = t;
                Vec3d point = Vec3d.Lerp(start, end, t), axis = ObstacleMath.OnSegment(point, part.A, part.B);
                hit = new(obstacle, i, Vec3d.Lerp(a, b, t), obstacle.Rotation.Apply(ObstacleMath.Unit(point - axis, start - end)), t);
            }
        }
        return double.IsFinite(nearest);
    }

    /// <summary>Nearest surface within radius; negative clearance means the query sphere penetrates it.</summary>
    public bool ClosestApproach(Vec3d point, double radius, out ObstacleProximity hit)
    {
        hit = default;
        if (Count == 0) return false;
        double nearest = double.PositiveInfinity;
        foreach (var obstacle in _items)
        {
            if ((point - obstacle.Position).Length() > obstacle.BoundingRadius + radius) continue;
            Vec3d local = obstacle.ToLocal(point);
            for (int i = 0; i < obstacle.Parts.Count; i++)
            {
                var part = obstacle.Parts[i];
                Vec3d axis = ObstacleMath.OnSegment(local, part.A, part.B);
                double clearance = (local - axis).Length() - part.Radius - radius;
                if (clearance > 0 || clearance >= nearest) continue;
                nearest = clearance;
                Vec3d normal = ObstacleMath.Unit(local - axis, new Vec3d(1, 0, 0));
                hit = new(obstacle, i, obstacle.ToWorld(axis + normal * part.Radius), obstacle.Rotation.Apply(normal), clearance);
            }
        }
        return double.IsFinite(nearest);
    }

    public static bool BoxContact(Obstacle obstacle, int partIndex, Vec3d shipPosition, Quaternion shipOrientation,
        HullBox box, out ObstacleBoxContact contact)
    {
        var rotation = new DoubleRotation(shipOrientation);
        var part = obstacle.Parts[partIndex];
        // Subtract world origins before rotation/addition so large world coordinates never become floats.
        Vec3d relative = obstacle.Position - shipPosition;
        Vec3d a = rotation.Inverse(relative + obstacle.Rotation.Apply(part.A)) - Vec3d.From(box.Center);
        Vec3d b = rotation.Inverse(relative + obstacle.Rotation.Apply(part.B)) - Vec3d.From(box.Center);
        Vec3d half = Vec3d.From(box.HalfSize);
        double distance = ObstacleMath.SegmentBoxDistance(a, b, half, out Vec3d axisPoint, out Vec3d boxPoint);
        contact = default;
        if (distance > part.Radius) return false;
        Vec3d normal; double penetration;
        if (distance > 1e-10) { normal = (boxPoint - axisPoint) * (1 / distance); penetration = part.Radius - distance; }
        else
        {
            // Centreline inside the box: choose a separating face, not a zero normal.
            penetration = double.PositiveInfinity; normal = default;
            for (int axis = 0; axis < 3; axis++)
            {
                double h = ObstacleMath.Component(half, axis), lo = Math.Min(ObstacleMath.Component(a, axis), ObstacleMath.Component(b, axis)),
                    hi = Math.Max(ObstacleMath.Component(a, axis), ObstacleMath.Component(b, axis));
                double plus = hi + part.Radius + h, minus = h + part.Radius - lo;
                if (plus < penetration) { penetration = plus; normal = ObstacleMath.Axis(axis); }
                if (minus < penetration) { penetration = minus; normal = -ObstacleMath.Axis(axis); }
            }
            boxPoint = new(-normal.X * half.X, -normal.Y * half.Y, -normal.Z * half.Z);
        }
        contact = new(shipPosition + rotation.Apply(boxPoint + Vec3d.From(box.Center)), rotation.Apply(normal), penetration);
        return true;
    }
}

/// <summary>Quaternion rotation with double arithmetic, including normalization. No absolute float positions.</summary>
internal readonly struct DoubleRotation
{
    private readonly Vec3d _x, _y, _z;
    public DoubleRotation(Quaternion q)
    {
        double x = q.X, y = q.Y, z = q.Z, w = q.W, n = x*x + y*y + z*z + w*w;
        if (!double.IsFinite(n) || n < 1e-20) throw new ArgumentException("Invalid obstacle orientation");
        double s = 2 / n;
        _x = new(1 - s*(y*y + z*z), s*(x*y + z*w), s*(x*z - y*w));
        _y = new(s*(x*y - z*w), 1 - s*(x*x + z*z), s*(y*z + x*w));
        _z = new(s*(x*z + y*w), s*(y*z - x*w), 1 - s*(x*x + y*y));
    }
    public Vec3d Apply(Vec3d v) => _x*v.X + _y*v.Y + _z*v.Z;
    public Vec3d Inverse(Vec3d v) => new(v.Dot(_x), v.Dot(_y), v.Dot(_z));
}

internal static class ObstacleMath
{
    public static bool Finite(Vec3d v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
    public static double Component(Vec3d v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
    public static Vec3d Axis(int axis) => axis == 0 ? new(1, 0, 0) : axis == 1 ? new(0, 1, 0) : new(0, 0, 1);
    public static Vec3d Unit(Vec3d v, Vec3d fallback)
    { double length = v.Length(); return length > 1e-12 ? v * (1 / length) : fallback.Length() > 1e-12 ? fallback * (1 / fallback.Length()) : new(1, 0, 0); }
    public static Vec3d OnSegment(Vec3d p, Vec3d a, Vec3d b)
    { Vec3d d = b - a; return a + d * (d.LengthSquared() > 1e-20 ? Math.Clamp((p - a).Dot(d) / d.LengthSquared(), 0, 1) : 0); }
    public static bool SegmentSphere(Vec3d a, Vec3d b, Vec3d center, double radius, out double t)
    {
        Vec3d p = a - center, d = b - a;
        return Quadratic(d.LengthSquared(), p.Dot(d), p.LengthSquared() - radius*radius, out t);
    }
    private static bool Quadratic(double a, double b, double c, out double t)
    {
        t = 0;
        if (c <= 0) return true;
        if (a < 1e-24 || b >= 0) return false;
        double disc = b*b - a*c;
        if (disc < -1e-13 * Math.Max(1, b*b + Math.Abs(a*c))) return false;
        // Stable first root (avoids cancellation when already close to the surface).
        t = c / (-b + Math.Sqrt(Math.Max(0, disc)));
        return t >= 0 && t <= 1;
    }
    public static bool SegmentCapsule(Vec3d start, Vec3d end, Vec3d a, Vec3d b, double radius, out double t)
    {
        t = double.PositiveInfinity;
        if ((start - OnSegment(start, a, b)).LengthSquared() <= radius*radius) { t = 0; return true; }
        if (SegmentSphere(start, end, a, radius, out double first)) t = first;
        if (SegmentSphere(start, end, b, radius, out double last)) t = Math.Min(t, last);
        Vec3d axis = b - a; double length = axis.Length();
        if (length > 1e-12)
        {
            axis *= 1 / length;
            Vec3d offset = start - a, travel = end - start;
            double u = offset.Dot(axis), v = travel.Dot(axis);
            Vec3d p = offset - axis*u, d = travel - axis*v;
            if (Quadratic(d.LengthSquared(), p.Dot(d), p.LengthSquared() - radius*radius, out double side)
                && u + v*side >= 0 && u + v*side <= length) t = Math.Min(t, side);
        }
        return double.IsFinite(t);
    }

    /// <summary>Exact closest pair of a segment and axis-aligned box via piecewise quadratic distance.</summary>
    public static double SegmentBoxDistance(Vec3d a, Vec3d b, Vec3d half, out Vec3d axisPoint, out Vec3d boxPoint)
    {
        Vec3d d = b - a;
        Span<double> cuts = stackalloc double[8]; cuts[0] = 0; cuts[1] = 1; int count = 2;
        for (int axis = 0; axis < 3; axis++)
        {
            double v = Component(d, axis), p = Component(a, axis), h = Component(half, axis);
            if (Math.Abs(v) < 1e-15) continue;
            double lo = (-h-p)/v, hi = (h-p)/v;
            if (lo > 0 && lo < 1) cuts[count++] = lo;
            if (hi > 0 && hi < 1) cuts[count++] = hi;
        }
        cuts = cuts[..count]; cuts.Sort();
        double best = double.PositiveInfinity, bestT = 0;
        for (int i = 0; i < count - 1; i++)
        {
            double middle = (cuts[i] + cuts[i+1]) * .5, aa = 0, bb = 0;
            for (int axis = 0; axis < 3; axis++)
            {
                double p = Component(a, axis), v = Component(d, axis), h = Component(half, axis), q = p + v*middle;
                if (q >= -h && q <= h) continue;
                double offset = p - (q < -h ? -h : h); aa += v*v; bb += offset*v;
            }
            double t = aa > 1e-24 ? Math.Clamp(-bb/aa, cuts[i], cuts[i+1]) : cuts[i];
            Vec3d point = a + d*t, clamp = Clamp(point, half);
            double distance = (point - clamp).LengthSquared();
            if (distance < best) { best = distance; bestT = t; }
        }
        axisPoint = a + d*bestT; boxPoint = Clamp(axisPoint, half); return Math.Sqrt(best);
    }
    private static Vec3d Clamp(Vec3d p, Vec3d half) => new(Math.Clamp(p.X,-half.X,half.X), Math.Clamp(p.Y,-half.Y,half.Y), Math.Clamp(p.Z,-half.Z,half.Z));
}

public sealed partial class SimWorld
{
    public ObstacleSet Obstacles { get; } = new();
}
