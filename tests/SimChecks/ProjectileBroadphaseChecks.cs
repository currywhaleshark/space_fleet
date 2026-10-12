using Godot;
using SpaceFleet.Sim;

static class ProjectileBroadphaseChecks
{
    public static void Run()
    {
        int checks = 0;
        void Require(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var random = new Random(97051);
        Vec3d Noise(double radius) => new((random.NextDouble()*2-1)*radius,
            (random.NextDouble()*2-1)*radius, (random.NextDouble()*2-1)*radius);
        var index = new SweepIndex();
        foreach (int count in new[] { 0, 1, 8, 48, 240 })
        foreach (double offset in new[] { 0.0, 1e12, -1e15 })
        {
            var origin = new Vec3d(offset, -offset, offset);
            var boxes = new SpatialBounds[count];
            index.Begin(count);
            for (int i = 0; i < count; i++)
            {
                Vec3d a = origin + Noise(10000);
                boxes[i] = SpatialBounds.Segment(a, a + Noise(i % 4 == 0 ? 40000 : 100)).Expanded(i % 9);
                index.Add(i, boxes[i]);
            }
            index.Build();
            for (int i = 0; i < 300; i++)
            {
                Vec3d a = origin + Noise(20000);
                var query = SpatialBounds.Segment(a, a + Noise(1000)).Expanded(i % 20);
                var expected = Enumerable.Range(0, count).Where(n => boxes[n].Overlaps(query)).ToArray();
                Require(index.Query(query).SequenceEqual(expected), "Sweep candidates and stable tie order differ from exhaustive AABB scan");
            }
        }
        var sample = SpatialBounds.Segment(Vec3d.Zero, new Vec3d(100, 200, 300));
        index.Query(sample);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) index.Query(sample);
        Require(GC.GetAllocatedBytesForCurrentThread() == before, "Warm broadphase queries allocate");
        Require(!SimWorld.IndexableSweep(Vec3d.Zero, new Vec3d(1e12, 0, 0)), "Long quadratic sweep needs full-scan fallback");
        Require(!SimWorld.IndexableSweep(new Vec3d(1e16, 0, 0), new Vec3d(1e16, 1, 0)), "Extreme coordinate fallback");
        // Grazing moving spheres, partial substeps, and double quadratic cancellation.
        // A broadphase may admit extra misses, but must never remove a legacy hit.
        foreach (double offset in new[] { 0.0, 1e8, 1e12, -1e12, 1e15, 1e16 })
        foreach (double travel in new[] { .01, 100.0, 1e6, 1e8, 1e12 })
        for (int n = 0; n < 200; n++)
        {
            var center = new Vec3d(offset, -offset, offset);
            double radius = n % 3 == 0 ? 40 : 3;
            var start = center + new Vec3d(-travel * .5, radius + (n % 7 - 3) * .00001, n % 2 * .001);
            var end = start + new Vec3d(travel, 0, 0);
            var movement = new Vec3d(0, (n % 5 - 2) * .1, 0);
            var r = start - center; var v = end - start - movement;
            double c = r.LengthSquared() - radius * radius, a = v.LengthSquared(), b = r.Dot(v), discriminant = b*b-a*c;
            double t = a < 1e-12 || discriminant < 0 ? double.NaN : (-b-Math.Sqrt(discriminant))/a;
            bool hit = c <= 0 || t >= 0 && t <= 1;
            bool indexed = SimWorld.IndexableSweep(start, end) && SimWorld.IndexableSweep(center, center + movement);
            if (indexed && hit)
                Require(SpatialBounds.Segment(start, end).Expanded(0).Overlaps(
                    SpatialBounds.Segment(center, center + movement).Expanded(radius)), "Grazing legacy quadratic hit was culled");
            else checks++;
        }

        foreach (int ships in new[] { 24, 48, 96 })
        foreach (double offset in new[] { 0.0, 1e12, -1e12, 1e16 })
        {
            var original = Create(ships, offset); var current = Create(ships, offset);
            for (int step = 0; step < 80; step++)
            {
                if (step % 8 == 0) { Shoot(original, step); Shoot(current, step); }
                double dt = SimWorld.TickDelta / 4, time = step * dt;
                original.StepProjectilesForCheck(dt, time, reference: true);
                current.StepProjectilesForCheck(dt, time, reference: false);
                Require(original.Projectiles.Select(p => (p.Id, p.Age, p.Position)).SequenceEqual(
                    current.Projectiles.Select(p => (p.Id, p.Age, p.Position))), "Projectile survival/pose differs from frozen traversal");
                Require(original.Missiles.Select(m => (m.Id, m.Health)).SequenceEqual(current.Missiles.Select(m => (m.Id, m.Health))), "Missile hit/removal differs");
                Require(original.Probes.Select(p => (p.Id, p.Health)).SequenceEqual(current.Probes.Select(p => (p.Id, p.Health))), "Probe hit/removal differs");
                Require(original.Impacts.Select(i => (i.Id, i.Hit.Target?.Callsign, i.Hit.Point, i.Hit.Summary)).SequenceEqual(
                    current.Impacts.Select(i => (i.Id, i.Hit.Target?.Callsign, i.Hit.Point, i.Hit.Summary))), "Ship hit order/point differs");
                for (int i = 0; i < ships; i++)
                {
                    var a = original.Ships[i]; var b = current.Ships[i];
                    Require((a.Damage.Shield, a.Damage.Destroyed, a.Damage.Destruction) == (b.Damage.Shield, b.Damage.Destroyed, b.Damage.Destruction), "Live shield/wreck state differs");
                    Require(a.Damage.Modules.Select(m => m.Health).SequenceEqual(b.Damage.Modules.Select(m => m.Health)), "Module damage differs");
                    for (int drone = 0; drone < a.Ordnance.Drones.Rounds.Length; drone++)
                        Require(a.Ordnance.Drones.HealthFraction(drone) == b.Ordnance.Drones.HealthFraction(drone), "Moving drone hit/order differs");
                    a.Step(dt); b.Step(dt);
                }
                foreach (var world in new[] { original, current })
                    foreach (var missile in world.Missiles) missile.Position += Vec3d.From(missile.Velocity) * dt;
            }
            Require(current.Impacts.Count > 0, "Fixture must exercise ship hits");
            Require(current.Ships.Where((_, i) => i > 0 && i % 5 != 0).Any(s => s.Damage.Destroyed),
                "Fixture must destroy a live hull and refresh its radius during a projectile burst");
            Require(current.Missiles.Count < 64 && current.Probes.Count < 16,
                $"Fixture must exercise small target removal: ships={ships}, offset={offset}, missiles={current.Missiles.Count}, probes={current.Probes.Count}");
            current.ResetWeapons();
            current.StepProjectilesForCheck(SimWorld.TickDelta / 4, 1, false);
            Require(current.Projectiles.Count == 0, "Reset must leave no indexed projectile work");
        }
        Console.WriteLine($"PASS: {checks} projectile broadphase/reference checks");
    }

    private static SimWorld Create(int count, double offset)
    {
        var world = new SimWorld(); var origin = new Vec3d(offset, -offset, offset);
        var definitions = ShipDefinitions.All.ToArray();
        for (int i = 0; i < count; i++)
        {
            var def = definitions[i % definitions.Length];
            var ship = world.Add(new ShipBody($"S{i}", def, i == 0 ? Faction.Blue : Faction.Red));
            ship.Place(origin + new Vec3d((i % 8) * 3000, (i / 8) * 4000, 0), Quaternion.Identity);
            ship.Velocity = new Vector3(i % 3 * 100, 0, i % 2 * 300);
            if (i % 5 == 0 && i > 0) ship.Damage.Catastrophe(0, "reference wreck");
            // Force a deterministic secondary failure on the first damaged module,
            // exercising a radius change between consecutive rounds in one substep.
            if (i == 1) ship.Damage.ModuleDamaged += (_, time) => ship.Damage.Catastrophe(time, "test secondary failure");
        }
        var missiles = (List<Missile>)world.Missiles;
        for (int i = 0; i < 64; i++)
            missiles.Add(new Missile { Id = (uint)i, Shooter = world.Ships[i % 4 == 0 ? 0 : 1],
                Definition = world.Ships[0].Definition.Missiles!, Health = 10,
                Position = origin + new Vec3d(i * 400, -3000, 0), Velocity = new Vector3(i % 3 * 1000, 0, 0) });
        var probes = (List<ReconProbe>)world.Probes;
        for (int i = 0; i < 16; i++)
            probes.Add(new ReconProbe { Id = (uint)i, Owner = world.Ships[1], Definition = new(),
                Health = 4, Position = origin + new Vec3d(i * 400, -6000, 0), Velocity = Vector3.Right * (i % 2 == 0 ? 250 : 0) });
        return world;
    }

    private static void Shoot(SimWorld world, int step)
    {
        var rounds = (List<RailProjectile>)world.Projectiles;
        for (int i = 0; i < 64; i++)
        {
            int kind = i % 4;
            Vec3d destination = kind == 0 || world.Missiles.Count == 0
                ? world.Ships[1 + i % (world.Ships.Count - 1)].Position
                : kind == 1 && world.Probes.Count > 0 ? world.Probes[i % world.Probes.Count].Position : world.Missiles[i % world.Missiles.Count].Position;
            var velocity = new Vector3(0, 0, -24000);
            if (kind == 0)
            {
                var target = world.Ships[1 + i % (world.Ships.Count - 1)];
                var module = target.Definition.Modules.First(m => m.Kind is ModuleKind.Reactor or ModuleKind.Generator);
                destination = target.Position + Vec3d.From(target.Orientation * module.Center);
            }
            bool droneShot = i % 8 == 7;
            if (droneShot)
            {
                var carrier = world.Ships.FirstOrDefault(s => s.Faction == Faction.Red && s.Ordnance.Drones.Rounds.Length > 0 && s.Ordnance.Drones.Alive(0));
                if (carrier is not null) destination = carrier.Ordnance.Drones.WorldPosition(0);
            }
            double lifetime = i % 7 == 0 ? .002 : .15;
            var projectile = new RailProjectile { Id = (uint)(step * 100 + i), Shooter = world.Ships[0],
                Velocity = velocity, Packet = new(100000, 10000, 10000), Lifetime = lifetime,
                Position = destination + new Vec3d(kind == 3 && !droneShot ? 30 : 0, 0, i % 2 == 0 ? 50 : 200) };
            if (kind == 3 && !droneShot && world.Missiles.Count > 0)
            { projectile.ProximityTarget = world.Missiles[i % world.Missiles.Count]; projectile.ProximityMeters = 40; projectile.Age = .05; }
            rounds.Add(projectile);
        }
    }
}
