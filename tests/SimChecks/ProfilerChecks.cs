using System.Collections.Concurrent;
using Godot;
using SpaceFleet.Sim;

static class ProfilerChecks
{
    private static int _checks;
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); _checks++; }
    public static void Run()
    {
        try
        {
            CheckCounters(); CheckParallelIsolation(); CheckBattle();
            Console.WriteLine($"PASS: {_checks} simulation profiler checks");
        }
        finally { SimProfiler.Enabled = false; SimProfiler.Reset(); }
    }

    private static void CheckCounters()
    {
        SimProfiler.Enabled = false; SimProfiler.Reset();
        Require(SimProfiler.Begin() == 0, "Disabled profiler returns zero timestamp");
        SimProfiler.End(SimSection.AI, 1);
        Require(SimProfiler.Snapshot().Values.All(v => v.Calls == 0 && v.TotalMs == 0), "Disabled timing remains empty");
        for (int i = 0; i < 100; i++) SimProfiler.End(SimSection.AI, SimProfiler.Begin());
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) SimProfiler.End(SimSection.AI, SimProfiler.Begin());
        Require(GC.GetAllocatedBytesForCurrentThread() == allocated, "Disabled timing path allocates nothing");
        SimProfiler.Enabled = true; SimProfiler.Reset();
        var world = new SimWorld(); world.Add(new ShipBody("TIMED", ShipClass.Interceptor, Faction.Blue));
        world.Step();
        var snapshot = SimProfiler.Snapshot();
        Require(snapshot[SimSection.Tick].Calls == 1 && snapshot[SimSection.Tick].TotalMs > 0, "Whole tick is measured once");
        foreach (var section in new[] { SimSection.ShipStep, SimSection.Collision, SimSection.Projectiles,
            SimSection.Missiles, SimSection.PointDefense, SimSection.Drones })
            Require(snapshot[section].Calls == 4, $"Four substeps measured for {section}");
        foreach (var section in new[] { SimSection.AI, SimSection.Gunnery, SimSection.Sensors })
            Require(snapshot[section].Calls == 1, $"One outer update measured for {section}");
        double exclusive = new[] { SimSection.AI, SimSection.Gunnery, SimSection.ShipStep, SimSection.Collision,
            SimSection.Projectiles, SimSection.Missiles, SimSection.PointDefense, SimSection.Drones, SimSection.Sensors, SimSection.Post }
            .Sum(s => snapshot[s].TotalMs);
        Require(exclusive <= snapshot[SimSection.Tick].TotalMs, "Exclusive sections do not double count parent timings");
        SimProfiler.Reset();
        Require(snapshot[SimSection.Tick].Calls == 1 && SimProfiler.Snapshot().Values.All(v => v.Calls == 0),
            "Reset preserves detached snapshots");
        allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) SimProfiler.End(SimSection.AI, SimProfiler.Begin());
        Require(GC.GetAllocatedBytesForCurrentThread() == allocated, "Enabled timing path also allocates nothing");
        Require(SimProfiler.Snapshot()[SimSection.AI].Calls == 10000, "Enabled calls accumulate exactly");
        long start = SimProfiler.Begin(); SimProfiler.Enabled = false;
        SimProfiler.End(SimSection.Tick, start);
        Require(SimProfiler.Snapshot()[SimSection.Tick].Calls == 0, "Disabling cancels a pending measurement");
    }

    private static void CheckParallelIsolation()
    {
        SimProfiler.Enabled = true; SimProfiler.Reset();
        SimProfiler.End(SimSection.AI, SimProfiler.Begin());
        using var barrier = new Barrier(2);
        var counts = new long[2]; var errors = new ConcurrentQueue<Exception>();
        Thread Worker(int index, int calls) => new(() =>
        {
            try
            {
                if (SimProfiler.Enabled) throw new Exception("Worker inherited enabled state");
                SimProfiler.Enabled = true; SimProfiler.Reset();
                for (int i = 0; i < calls; i++) SimProfiler.End(SimSection.Drones, SimProfiler.Begin());
                if (!barrier.SignalAndWait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Profiler worker barrier");
                counts[index] = SimProfiler.Snapshot()[SimSection.Drones].Calls;
            }
            catch (Exception ex) { errors.Enqueue(ex); }
            finally { SimProfiler.Enabled = false; SimProfiler.Reset(); }
        });
        var a = Worker(0, 3); var b = Worker(1, 7); a.Start(); b.Start();
        Require(a.Join(15000) && b.Join(15000), "Parallel timing workers finish");
        Require(errors.IsEmpty && counts[0] == 3 && counts[1] == 7, "Parallel workers keep independent counters");
        Require(SimProfiler.Enabled && SimProfiler.Snapshot()[SimSection.AI].Calls == 1 &&
            SimProfiler.Snapshot()[SimSection.Drones].Calls == 0, "Worker cleanup cannot clear main-thread timings");
    }

    private static void CheckBattle()
    {
        string Run(bool enabled)
        {
            SimProfiler.Enabled = enabled; SimProfiler.Reset();
            var world = new SimWorld();
            var blue = world.Add(new ShipBody("BLUE", ShipClass.Escort, Faction.Blue));
            var red = world.Add(new ShipBody("RED", ShipClass.Battleship, Faction.Red));
            red.Place(new Vec3d(500, 200, -6000), new Quaternion(Vector3.Up, Mathf.Pi));
            world.AttachBrain(blue, ShipOrder.AttackOn(red)); world.AttachBrain(red, ShipOrder.AttackOn(blue));
            world.Sensors.Update(world.Ships, 0, force: true); world.Log = new BattleLog(world);
            for (int tick = 0; tick < 2400; tick++) world.Step();
            world.Log.Finish();
            Require(world.Log.Side(Faction.Blue).Rails + world.Log.Side(Faction.Red).Rails > 0, "Profile fixture fires real weapons");
            if (enabled)
            {
                var counters = SimProfiler.Snapshot();
                Require(counters[SimSection.Tick].Calls == 2400 && counters[SimSection.PointDefense].Calls == 9600,
                    "Full encounter maintains exact tick/substep counts");
                Require(counters[SimSection.AIThink].Calls > 0 && counters[SimSection.AIDrive].Calls > 0 && counters[SimSection.AIEngage].Calls > 0,
                    "AI child measurements are populated");
            }
            return world.Log.Summary();
        }
        Require(Run(false) == Run(true), "Profiling leaves battle summary exactly unchanged");
    }
}
