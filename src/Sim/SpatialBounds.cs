using System;

namespace SpaceFleet.Sim;

/// <summary>Conservative double-precision broad phase; acceptance always requires the original narrow phase.</summary>
internal readonly record struct SpatialBounds(Vec3d Min, Vec3d Max)
{
    public static SpatialBounds Segment(Vec3d a, Vec3d b) => new(
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z)),
        new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z)));

    public SpatialBounds Include(Vec3d p) => new(
        new(Math.Min(Min.X, p.X), Math.Min(Min.Y, p.Y), Math.Min(Min.Z, p.Z)),
        new(Math.Max(Max.X, p.X), Math.Max(Max.Y, p.Y), Math.Max(Max.Z, p.Z)));

    public SpatialBounds Expanded(double radius)
    {
        // Cover rounding in alternate subtraction/addition orders, including floating-origin tests.
        double magnitude = Math.Max(Math.Max(Math.Abs(Min.X), Math.Abs(Max.X)),
            Math.Max(Math.Max(Math.Abs(Min.Y), Math.Abs(Max.Y)), Math.Max(Math.Abs(Min.Z), Math.Abs(Max.Z))));
        double r = radius + 1 + magnitude * 1e-14;
        return new(new(Min.X-r, Min.Y-r, Min.Z-r), new(Max.X+r, Max.Y+r, Max.Z+r));
    }

    public bool Contains(Vec3d p) => p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z;
    public bool Overlaps(SpatialBounds b) => Max.X >= b.Min.X && Min.X <= b.Max.X &&
        Max.Y >= b.Min.Y && Min.Y <= b.Max.Y && Max.Z >= b.Min.Z && Min.Z <= b.Max.Z;

    public bool Overlaps(SpatialBounds b, double radius) => Max.X >= b.Min.X-radius && Min.X <= b.Max.X+radius &&
        Max.Y >= b.Min.Y-radius && Min.Y <= b.Max.Y+radius && Max.Z >= b.Min.Z-radius && Min.Z <= b.Max.Z+radius;
}
