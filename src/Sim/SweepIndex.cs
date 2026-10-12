using System;
using System.Collections.Generic;

namespace SpaceFleet.Sim;

/// <summary>
/// Reusable broad phase for a substep's moving targets. Sorted lower bounds and
/// prefix upper bounds reject distant intervals; exact tests keep their original order.
/// No quantization, fixed cell capacity, or candidate limit.
/// </summary>
internal sealed class SweepIndex
{
    private struct Entry { public SpatialBounds Bounds; public int Id; public double Lower, Upper; }
    private sealed class LowerComparer : IComparer<Entry>
    {
        public static readonly LowerComparer Instance = new();
        public int Compare(Entry a, Entry b) => a.Lower.CompareTo(b.Lower);
    }
    private Entry[] _entries = Array.Empty<Entry>();
    private double[] _prefix = Array.Empty<double>();
    private int[] _candidates = Array.Empty<int>();
    private int _count, _axis;

    public void Begin(int capacity)
    {
        _count = 0;
        if (_entries.Length >= capacity) return;
        int size = Math.Max(capacity, Math.Max(16, _entries.Length * 2));
        Array.Resize(ref _entries, size); Array.Resize(ref _prefix, size); Array.Resize(ref _candidates, size);
    }

    public void Add(int id, SpatialBounds bounds) => _entries[_count++] = new() { Id = id, Bounds = bounds };

    public void Build()
    {
        if (_count == 0) return;
        var extent = _entries[0].Bounds;
        for (int i = 1; i < _count; i++) extent = extent.Include(_entries[i].Bounds.Min).Include(_entries[i].Bounds.Max);
        Vec3d size = extent.Max - extent.Min;
        _axis = size.Y > size.X ? 1 : 0;
        if (size.Z > (_axis == 0 ? size.X : size.Y)) _axis = 2;
        for (int i = 0; i < _count; i++)
        { _entries[i].Lower = Axis(_entries[i].Bounds.Min); _entries[i].Upper = Axis(_entries[i].Bounds.Max); }
        Array.Sort(_entries, 0, _count, LowerComparer.Instance);
        double upper = double.NegativeInfinity;
        for (int i = 0; i < _count; i++) _prefix[i] = upper = Math.Max(upper, _entries[i].Upper);
    }

    // Returned storage remains valid until the next query on this index.
    public ReadOnlySpan<int> Query(SpatialBounds bounds)
    {
        double lower = Axis(bounds.Min), upper = Axis(bounds.Max);
        int lo = 0, hi = _count;
        while (lo < hi)
        { int mid = (lo + hi) / 2; if (_entries[mid].Lower <= upper) lo = mid + 1; else hi = mid; }
        int count = 0;
        for (int i = lo - 1; i >= 0 && _prefix[i] >= lower; i--)
            if (bounds.Overlaps(_entries[i].Bounds)) _candidates[count++] = _entries[i].Id;
        // Strict '<' in narrow phase makes original list order significant for ties.
        Array.Sort(_candidates, 0, count);
        return _candidates.AsSpan(0, count);
    }

    private double Axis(Vec3d p) => _axis == 0 ? p.X : _axis == 1 ? p.Y : p.Z;
}
