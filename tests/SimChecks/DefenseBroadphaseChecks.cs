using Godot;
using SpaceFleet.Sim;

static class DefenseBroadphaseChecks
{
    private static int _checks;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); _checks++; }

    public static void Run()
    {
        long shots = 0, removed = 0;
        foreach (int count in new[] { 24, 48, 96, 240 })
        foreach (double offset in new[] { 0.0, 1e12, -1e15, 1e16 })
        {
            var indexed = Fixture(count, offset); var reference = Fixture(count, offset);
            for (int step = 0; step < 280; step++)
            {
                foreach (var world in new[] { indexed, reference })
                {
                    foreach (var missile in world.Missiles) missile.Position += Vec3d.From(missile.Velocity) * (SimWorld.TickDelta / 4);
                    if (step == 70) world.Ships[1].Damage.Catastrophe(step / 240.0, "defense regression");
                    if (step == 140) world.ResetWeapons();
                    if (step == 141) Populate(world, offset);
                    world.ThreatTimeForCheck(step / 4);
                    if(step % 60 == 0 || step == 141)
                        world.Sensors.Update(world.Ships,world.Time,true,world.Probes,world.Missiles);
                }
                indexed.DefenseForCheck(step / 240.0, false); reference.DefenseForCheck(step / 240.0, true);
                Check(indexed.PointDefenseShots.SequenceEqual(reference.PointDefenseShots), "PD shot order, aim, timing or roll changed");
                Check(indexed.Missiles.Select(m => (m.Id, m.Health)).SequenceEqual(reference.Missiles.Select(m => (m.Id, m.Health))), "PD missile damage/removal changed");
                Check(indexed.Probes.Select(p => (p.Id, p.Health)).SequenceEqual(reference.Probes.Select(p => (p.Id, p.Health))), "PD probe damage changed");
                for (int i = 0; i < count; i++)
                {
                    var a = indexed.Ships[i]; var b = reference.Ships[i];
                    Check(a.Damage.Shield == b.Damage.Shield && a.Damage.Destroyed == b.Damage.Destroyed, "PD hull outcome changed");
                    Check(a.Damage.Modules.Select(m => m.Health).SequenceEqual(b.Damage.Modules.Select(m => m.Health)), "PD module damage changed");
                    for (int m = 0; m < a.Ordnance.PointDefense.Length; m++)
                    {
                        var x = a.Ordnance.PointDefense[m]; var y = b.Ordnance.PointDefense[m];
                        bool same=(x.Yaw, x.Elevation, x.LocalAim, x.TargetKind, x.FireAccumulator, x.ShotCount)
                            == (y.Yaw, y.Elevation, y.LocalAim, y.TargetKind, y.FireAccumulator, y.ShotCount);
                        Check(same,same ? "" :
                            $"PD selection/tracking changed: count={count}, offset={offset:R}, step={step}, ship={i}, mount={m}; " +
                            $"indexed={(x.Yaw,x.Elevation,x.LocalAim,x.TargetKind,x.FireAccumulator,x.ShotCount)}, reference={(y.Yaw,y.Elevation,y.LocalAim,y.TargetKind,y.FireAccumulator,y.ShotCount)}");
                    }
                    for (int d = 0; d < a.Ordnance.Drones.Rounds.Length; d++)
                    {
                        Check(a.Ordnance.Drones.HealthFraction(d) == b.Ordnance.Drones.HealthFraction(d), "PD drone damage changed");
                        Check((a.Ordnance.Drones.Rounds[d], a.Ordnance.Drones.Accum[d], a.Ordnance.Drones.LocalDirection(d))
                            == (b.Ordnance.Drones.Rounds[d], b.Ordnance.Drones.Accum[d], b.Ordnance.Drones.LocalDirection(d)), "Drone selection/ammunition changed");
                    }
                }
                if (step == 139) { shots += indexed.PointDefenseShots.Count; removed += count * 2 - indexed.Missiles.Count; }
            }
        }
        Check(shots > 100 && removed > 0, "Reference fixture must actually fire and remove targets");
        ThreatChecks();
        Console.WriteLine($"PASS: {_checks} defense broadphase checks (shots={shots}, removed={removed})");
    }

    private static SimWorld Fixture(int count, double offset)
    {
        var world = new SimWorld();
        for (int i = 0; i < count; i++)
        {
            var def = ShipDefinitions.For((HullKind)(i % 3), i % 2 == 0 ? DesignFamily.Earth : DesignFamily.Mars);
            var ship = world.Add(new ShipBody($"D{i}", def.Flight, (Faction)(i % 2), def));
            ship.Place(new Vec3d(offset + i % 12 * 1700, -offset + i / 12 * 2200, offset), Quaternion.Identity);
            if (i == 7) ship.Damage.ModuleDamaged += (_, time) => ship.Damage.Catastrophe(time, "secondary defense failure");
        }
        Populate(world, offset);
        return world;
    }

    private static void Populate(SimWorld world, double offset)
    {
        var missiles = (List<Missile>)world.Missiles;
        for (int i = 0; i < world.Ships.Count * 2; i++)
        {
            var defender = world.Ships[i / 2]; var shooter = world.Ships[(i / 2 + 1) % world.Ships.Count];
            var pd = defender.Definition.PointDefense!;
            // Coincident pairs exercise stable tie order, including a kill between mounts.
            double distance = i % 8 < 2 ? pd.RangeMeters * 1.5 : 1100;
            missiles.Add(new Missile { Id = (uint)i, Shooter = shooter, Definition = shooter.Definition.Missiles!, Health = 1,
                Position = defender.Position + Vec3d.From(pd.Mounts[0]) + new Vec3d(0, 220, -distance), Velocity = new Vector3(15, 0, 35) });
            if (i % 8 != 0) continue;
            var probe = new ReconProbe { Id = (uint)i, Owner = shooter, Definition = new(), Health = 4,
                Position = defender.Position + new Vec3d(0, 100, -700) };
            ((List<ReconProbe>)world.Probes).Add(probe);
            world.Sensors.SetSmallForCheck(defender.Faction, new(probe, SmallContactKind.Probe, TrackLevel.Identified, probe.Position, Vector3.Zero, 0));
        }
    }

    private static void ThreatChecks()
    {
        var random = new Random(121026);
        foreach (int count in new[] { 24, 48, 240 })
        foreach (double offset in new[] { 0.0, 1e12, -1e15, 1e16 })
        {
            var world = Fixture(count, offset);
            foreach (var ship in world.Ships) world.AttachBrain(ship, ShipOrder.HoldAt(ship.Position));
            for (int i = 0; i < 320; i++)
            {
                var ship = world.Ships[i % count];
                var observed = ship.Position + new Vec3d(random.Next(-22000, 22000), random.Next(-6000, 6000), random.Next(-22000, 22000));
                var missile = new Missile { Id = (uint)(9000+i), Shooter = world.Ships[(i+1)%count], Definition = ship.Definition.Missiles!, Health = 0,
                    Position = new Vec3d(1e20, 1e20, 1e20) };
                world.Sensors.SetSmallForCheck(ship.Faction, new(missile, i % 11 == 0 ? SmallContactKind.Probe : SmallContactKind.Missile,
                    (TrackLevel)(i % 4), observed, new Vector3(random.Next(-2000,2000),0,random.Next(-2000,2000)), 1+(i%7-3)*.25));
            }
            foreach (long tick in new long[] { 0, 60, 75, 90, 120, 180 })
            {
                world.ThreatTimeForCheck(tick);
                foreach (var ship in world.Ships)
                foreach (float range in new[] { 6000f, 20000f })
                    Check(world.IndexedThreatForCheck(ship, range) == world.ReferenceThreatForCheck(ship, range), "Observed threat freshness/range or hidden-state behavior changed");
            }
            world.ResetWeapons();
            world.ThreatTimeForCheck(60);
            Check(world.IndexedThreatForCheck(world.Ships[0], 20000) == world.ReferenceThreatForCheck(world.Ships[0], 20000), "Threat cache reset changed observations");
        }
    }
}
