using Godot;
using SpaceFleet.Sim;

static class WreckCollisionChecks
{
    private static int _checks;
    private static void Check(bool value, string reason)
    {
        if (!value) throw new InvalidOperationException(reason);
        _checks++;
    }

    public static void Run()
    {
        CheckHullReuse();
        CheckRepairAndReplacement();
        CheckReferenceContacts();
        Console.WriteLine($"PASS: {_checks} wreck collider reuse/reference collision checks");
    }

    private static CollisionHull Snapshot(WreckLayout layout, float age)
    {
        // Original allocating transform path, independent of UpdateHull/RefreshBounds.
        var boxes = new List<HullBox>();
        foreach (var piece in layout.Pieces)
        {
            var pose = piece.Pose(age);
            foreach (var box in piece.Boxes)
                boxes.Add(new(pose * box.Center, pose.Basis.X.Abs() * box.HalfSize.X
                    + pose.Basis.Y.Abs() * box.HalfSize.Y + pose.Basis.Z.Abs() * box.HalfSize.Z));
        }
        return new CollisionHull(boxes.ToArray());
    }

    private static void CheckHullReuse()
    {
        foreach (var def in ShipDefinitions.All)
        foreach (var kind in new[] { DestructionKind.Collision, DestructionKind.Antimatter, DestructionKind.Reactor })
        {
            var layout = new WreckLayout(def, kind, new Vector3(1, 2, 3));
            var mutable = layout.Hull(0);
            var snapshot = layout.Hull(0);
            var savedBoxes = snapshot.Boxes.ToArray();
            var savedDefinition = def.Hull.Boxes.ToArray();
            foreach (float age in new[] { 0, .004166667f, .12f, 1, 30, 300, 900, 1500, 10000, 0 })
            {
                var expected = Snapshot(layout, age);
                Check(ReferenceEquals(mutable, layout.UpdateHull(mutable, age)), "Wreck owns one reusable collider");
                Check(expected.Boxes.SequenceEqual(mutable.Boxes), "All fragment poses match the old path exactly");
                Check(expected.Bounds == mutable.Bounds && expected.BoundingRadius == mutable.BoundingRadius,
                    "Bounds must shrink as well as grow and match the snapshot path");
                Check(savedBoxes.SequenceEqual(snapshot.Boxes), "Public snapshots remain independent across ages");
                Check(savedDefinition.SequenceEqual(def.Hull.Boxes), "Definition hull must never be modified");
            }
            for (int i = 0; i < 100; i++) layout.UpdateHull(mutable, i);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) layout.UpdateHull(mutable, i * .004166667f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(allocated == 0, $"Wreck updates allocate no objects after creation: {allocated} bytes");
        }
    }

    private static void CheckRepairAndReplacement()
    {
        foreach (var def in ShipDefinitions.All)
        {
            var ship = new ShipBody("REPAIR", def, Faction.Blue);
            ship.Damage.Breakup(0, "test");
            var first = ship.Hull;
            ship.Step((SimWorld.TickDelta / 4));
            Check(ReferenceEquals(first, ship.Hull), "Ship collider persists across substeps");
            ship.Damage.Reset();
            // No intermediate Hull getter: the reset version must invalidate even at the same destroyed time.
            ship.Damage.Catastrophe(0, "test");
            Check(!ReferenceEquals(first, ship.Hull), "New destruction layout must replace old buffer");
            Check(ship.Hull.Boxes.SequenceEqual(Snapshot(ship.Wreck!, ship.WreckAge).Boxes), "Replacement fragment counts and poses");
            ship.Damage.Reset();
            Check(ReferenceEquals(ship.Hull, def.Hull) && ship.Wreck is null, "Repair restores the immutable original hull");
        }
    }

    private static void CheckReferenceContacts()
    {
        var random = new Random(25011);
        var definitions = ShipDefinitions.All.ToArray();
        int contacts = 0;
        foreach (double coordinate in new[] { 0d, 1e12, -1e16 })
        for (int trial = 0; trial < 80; trial++)
        {
            var actual = new ShipBody[3]; var expected = new ShipBody[3];
            float age = (trial % 4) switch { 0 => 0, 1 => 30, 2 => 300, _ => 1500 };
            var origin = new Vec3d(coordinate, -coordinate, coordinate);
            float Noise(float scale) => ((float)random.NextDouble() * 2 - 1) * scale;
            for (int i = 0; i < 3; i++)
            {
                var def = definitions[(trial + i) % definitions.Length];
                var q = new Quaternion(new Vector3(Noise(1), Noise(1), Noise(1)).Normalized(), Noise(3));
                Vector3 local = new(Noise(300), Noise(90), Noise(300));
                Vector3 travel = new(Noise(1200), Noise(200), Noise(1200));
                bool destroyed = i != 2 || trial % 2 == 0;
                ShipBody Make()
                {
                    var ship = new ShipBody($"C{i}", def, i == 0 ? Faction.Blue : Faction.Red);
                    ship.Place(origin + local, q);
                    if (destroyed)
                    {
                        if (trial % 3 == 0) ship.Damage.Breakup(-age, "test");
                        else ship.Damage.Catastrophe(-age, "test");
                    }
                    ship.PrevPosition = ship.Position - Vec3d.From(travel);
                    ship.Velocity = travel * 240;
                    ship.AngularVelocity = new(.02f, -.015f, .01f);
                    return ship;
                }
                actual[i] = Make(); expected[i] = Make();
            }
            if (trial % 2 == 1)
            {
                // Deliberately hit a dispersed piece, not merely the empty space
                // around a 300/1500-second-old wreck's original centre.
                var a = actual[0]; var b = actual[1];
                Vector3 fragmentA = a.Orientation * a.Hull.Boxes[trial % a.Hull.Boxes.Count].Center;
                Vector3 fragmentB = b.Orientation * b.Hull.Boxes[trial % b.Hull.Boxes.Count].Center;
                Vec3d shift = a.Position + Vec3d.From(fragmentA - fragmentB) - b.Position;
                foreach (var ship in new[] { actual[1], expected[1] })
                { ship.Position += shift; ship.PrevPosition += shift; }
            }
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 0; i < 3; i++)
                for (int j = i + 1; j < 3; j++)
                    Check(ShipCollision.Overlaps(actual[i], actual[j]) == ReferenceShipCollision.Overlaps(expected[i], expected[j]),
                        $"Overlap parity {coordinate}/{trial}/{pass}/{i}/{j}");
                ShipCollision.Resolve(actual, pass * (SimWorld.TickDelta / 4));
                ReferenceShipCollision.Resolve(expected, pass * (SimWorld.TickDelta / 4));
                for (int i = 0; i < 3; i++)
                {
                    var a = actual[i]; var b = expected[i];
                    if (a.LastCollision is not null) contacts++;
                    Check(a.Position == b.Position && a.Velocity == b.Velocity && a.LastCollision == b.LastCollision,
                        $"Exact contact response parity {coordinate}/{trial}/{pass}/{i}");
                    Check(a.Damage.Shield == b.Damage.Shield && a.Damage.Destruction == b.Damage.Destruction
                        && a.Damage.DestroyedAt == b.Damage.DestroyedAt
                        && a.Damage.Modules.Select(m => m.Health).SequenceEqual(b.Damage.Modules.Select(m => m.Health)),
                        "Collision damage and destruction parity");
                }
            }
        }
        Check(contacts > 100, "Reference comparisons must include many real contacts, not only misses");
    }
}
