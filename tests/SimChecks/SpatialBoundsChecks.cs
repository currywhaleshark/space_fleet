using Godot;
using SpaceFleet.Sim;

static class SpatialBoundsChecks
{
    public static void Run()
    {
        int checks = 0;
        void Require(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var random = new Random(3719);
        foreach (double offset in new[] { 0.0, 1e9, -1e12, 1e16 })
        {
            var origin = new Vec3d(offset, -offset, offset);
            // A ray through a moving sphere must survive broad phase at endpoints and between them.
            for (int i = 0; i < 200; i++)
            {
                Vec3d Noise() => new(random.NextDouble()*20000-10000, random.NextDouble()*20000-10000, random.NextDouble()*20000-10000);
                var a = origin + Noise(); var b = origin + Noise();
                double radius = 4 + random.NextDouble()*2000;
                var bounds = SpatialBounds.Segment(a,b).Expanded(radius);
                double t = i % 3 == 0 ? 0 : i % 3 == 1 ? 1 : random.NextDouble();
                var point = Vec3d.Lerp(a,b,t);
                foreach (var direction in new[] { Vector3.Left,Vector3.Right,Vector3.Up,Vector3.Down,Vector3.Forward,Vector3.Back })
                    Require(bounds.Contains(point+Vec3d.From(direction)*radius), "Swept radius boundary survives broad phase at large coordinates");
                Require(bounds.Overlaps(SpatialBounds.Segment(point-Noise(),point+Noise()).Include(point)), "Crossing segment is never culled");
            }
            var near = SpatialBounds.Segment(origin,origin+new Vec3d(20,0,0)).Expanded(5);
            Require(!near.Contains(origin+new Vec3d(1e6,0,0)), "Distant point is culled");
            Require(!near.Overlaps(SpatialBounds.Segment(origin+new Vec3d(0,1e6,0),origin+new Vec3d(10,1e6,0))), "Parallel distant segment is culled");
        }
        Console.WriteLine($"PASS: {checks} conservative spatial bounds checks");
    }
}
