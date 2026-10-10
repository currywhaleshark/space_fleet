using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

/// <summary>
/// 개발용 구간 시간 측정(--perf-trace). 1초마다 FPS, 구간별 프레임당 평균 ms, 렌더 통계(그리기 호출·객체·삼각형), 노드 수를 찍는다.
/// --perf-seconds=N이면 N초(실시간) 뒤 종료한다. 꺼져 있으면 Begin/End는 아무 일도 하지 않는다.
/// </summary>
internal static class PerfTrace
{
    public static bool On { get; private set; }
    private static readonly Dictionary<string, (double Ms, double Max)> Sections = new();
    private static double _windowStart, _started, _quitAfter = double.PositiveInfinity;
    private static int _frames;

    public static void Enable(double quitAfterSeconds, bool profileSim = true)
    {
        On = true; _quitAfter = quitAfterSeconds;
        _frames = 0; Sections.Clear(); SimProfiler.Enabled = profileSim; SimProfiler.Reset();
        _started = -1; // 첫 프레임부터 잰다(시작 전 빨리 감기 시간을 빼기 위해).
    }

    public static void Disable()
    { On = false; Sections.Clear(); SimProfiler.Enabled = false; SimProfiler.Reset(); }

    private static double Now() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    public static long Begin() => On ? Stopwatch.GetTimestamp() : 0;

    public static void End(string name, long start)
    {
        if (!On) return;
        double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        Sections.TryGetValue(name, out var s);
        Sections[name] = (s.Ms + ms, Math.Max(s.Max, ms));
    }

    /// <summary>매 프레임 한 번. 1초가 지나면 요약을 찍는다.</summary>
    public static void Frame(Node node, double simTime)
    {
        if (!On) return;
        double now = Now();
        if (_started < 0) { _started = _windowStart = now; Sections.Clear(); SimProfiler.Reset(); return; }
        _frames++;
        double window = now - _windowStart;
        if (window < 1) return;
        string parts = string.Join(" ", Sections.OrderByDescending(s => s.Value.Ms)
            .Select(s => $"{s.Key}={s.Value.Ms / _frames:0.0}/{s.Value.Max:0}"));
        GD.Print($"perf t={simTime:0}s fps={_frames / window:0.0} frame={window * 1000 / _frames:0}ms | {parts} | " +
            $"draws={Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):0} " +
            $"objects={Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame):0} " +
            $"prims={Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame):0} " +
            $"nodes={Performance.GetMonitor(Performance.Monitor.ObjectNodeCount):0} " +
            $"gpu={Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576:0}MB");
        if (SimProfiler.Enabled)
        {
            var sim = SimProfiler.Snapshot(); long ticks = sim[SimSection.Tick].Calls;
            if (ticks > 0)
                GD.Print($"sim-profile t={simTime:0.000}s ticks={ticks} us/tick | " + string.Join(" ", sim.Select(s =>
                    $"{s.Key}={s.Value.TotalMs * 1000 / ticks:0.00}")));
            SimProfiler.Reset();
        }
        Sections.Clear(); _frames = 0; _windowStart = now;
        if (now - _started > _quitAfter) node.GetTree().Quit();
    }
}
