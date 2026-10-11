using System;
using SpaceFleet.Sim;

internal static class OriginalObstacleMath
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
