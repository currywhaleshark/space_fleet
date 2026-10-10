using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace SpaceFleet.Sim;

public enum SimSection
{
    Tick, AI, AICommand, AIThink, AIDrive, AIEngage, Gunnery, ShipStep,
    Collision, Projectiles, Missiles, PointDefense, Drones, Sensors, Post,
}

/// <summary>
/// Optional, thread-local simulation timings. A synchronous world stays on its
/// caller's thread; parallel batches must enable/reset inside each worker.
/// Tick and AI are inclusive parents, not additional exclusive work.
/// </summary>
public static class SimProfiler
{
    [ThreadStatic] private static bool _enabled;
    [ThreadStatic] private static long[]? _elapsed;
    [ThreadStatic] private static long[]? _calls;
    private const int SectionCount = (int)SimSection.Post + 1;

    public static bool Enabled
    {
        get => _enabled;
        set
        {
            if (value && _elapsed is null)
            { _elapsed = new long[SectionCount]; _calls = new long[SectionCount]; }
            _enabled = value;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Begin() => _enabled ? Stopwatch.GetTimestamp() : 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void End(SimSection section, long start)
    {
        if (!_enabled || start == 0) return;
        _elapsed![(int)section] += Stopwatch.GetTimestamp() - start;
        _calls![(int)section]++;
    }

    /// <summary>A detached read-only snapshot; allocations occur only when reporting.</summary>
    public static IReadOnlyDictionary<SimSection, (double TotalMs, long Calls)> Snapshot()
    {
        var result = new Dictionary<SimSection, (double, long)>(SectionCount);
        for (int i = 0; i < SectionCount; i++)
            result[(SimSection)i] = ((_elapsed?[i] ?? 0) * 1000.0 / Stopwatch.Frequency, _calls?[i] ?? 0);
        return new ReadOnlyDictionary<SimSection, (double, long)>(result);
    }

    /// <summary>Reset only at a measurement boundary, with no open Begin/End pair.</summary>
    public static void Reset()
    {
        if (_elapsed is null) return;
        Array.Clear(_elapsed); Array.Clear(_calls!);
    }
}
