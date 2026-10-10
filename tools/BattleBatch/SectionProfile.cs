using SpaceFleet.Sim;

/// <summary>Take detached snapshots only at BattleLog's 30-second boundaries.</summary>
sealed class SectionProfile
{
    private sealed record Window(long Start, long End,
        IReadOnlyDictionary<SimSection, (double TotalMs, long Calls)> Values);
    private readonly List<Window> _windows = new();
    private long _start;
    private IReadOnlyDictionary<SimSection, (double TotalMs, long Calls)> _previous = SimProfiler.Snapshot();
    private const int WindowTicks = (int)(BattleLog.IntervalSeconds * SimWorld.TickRate);

    public void Capture(long tick, bool final = false)
    {
        if (tick <= _start || (!final && tick % WindowTicks != 0)) return;
        var next = SimProfiler.Snapshot();
        var difference = next.ToDictionary(p => p.Key,
            p => (p.Value.TotalMs - _previous[p.Key].TotalMs, p.Value.Calls - _previous[p.Key].Calls));
        _windows.Add(new(_start, tick, difference)); _previous = next; _start = tick;
    }

    public IEnumerable<SectionRow> Rows(int seed, bool mirror, SimWorld world)
    {
        var groups = new List<(string Name, IEnumerable<Window> Windows)> { ("All", _windows) };
        foreach (var phase in Enum.GetValues<BattlePhase>())
            groups.Add(($"phase:{phase}", _windows.Where(w => world.Log!.Intervals.Any(i => i.Phase == phase &&
                w.Start >= (long)Math.Round(i.Start * SimWorld.TickRate) &&
                w.Start < (long)Math.Round((i.Start + i.Duration) * SimWorld.TickRate)))));
        groups.Add(("time:0-300", _windows.Where(w => w.Start < 300 * SimWorld.TickRate)));
        groups.Add(("time:300-540", _windows.Where(w => w.Start >= 300 * SimWorld.TickRate && w.Start < 540 * SimWorld.TickRate)));
        groups.Add(("time:540+", _windows.Where(w => w.Start >= 540 * SimWorld.TickRate)));
        foreach (var (name, windows) in groups)
        {
            var included = windows.ToArray(); long ticks = included.Sum(w => w.End - w.Start);
            if (ticks == 0) continue;
            foreach (var section in Enum.GetValues<SimSection>())
                yield return new(seed, mirror, name, section, ticks,
                    included.Sum(w => w.Values[section].Calls), included.Sum(w => w.Values[section].TotalMs));
        }
    }
}

sealed record SectionRow(int Seed, bool Mirror, string Scope, SimSection Section, long Ticks, long Calls, double TotalMs)
{
    public const string Header = "seed,mirror,scope,section,ticks,calls,totalMs,usPerTick";
    public double UsPerTick => TotalMs * 1000 / Ticks;
    public string Csv() => $"{Seed},{(Mirror ? 1 : 0)},{Scope},{Section},{Ticks},{Calls},{TotalMs:F6},{UsPerTick:F3}";
}

sealed record SummaryRow(int Seed, bool Mirror, string Summary);
