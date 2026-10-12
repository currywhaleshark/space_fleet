using System;

namespace SpaceFleet.Sim;

public sealed partial class SimWorld
{
    private readonly SweepIndex _projectileSmallIndex = new(), _projectileShipIndex = new(), _projectileDroneIndex = new();
    private object[] _projectileSmallTargets = Array.Empty<object>();
    private float[] _projectileShipRadii = Array.Empty<float>();
    private bool _indexedProjectileSmall, _indexedProjectileShips;
    private int _projectileSmallCount;

    private void PrepareSmallProjectileBounds(double dt, double time)
    {
        int count = _missiles.Count + _probes.Count;
        _indexedProjectileSmall = count >= 8 && (long)count * _projectiles.Count >= 128;
        // Release references to ended missiles/probes even when using the short-list path.
        Array.Clear(_projectileSmallTargets, 0, _projectileSmallCount);
        _projectileSmallCount = 0;
        if (!_indexedProjectileSmall) return;
        if (_projectileSmallTargets.Length < count)
            Array.Resize(ref _projectileSmallTargets, Math.Max(count, _projectileSmallTargets.Length * 2));
        _projectileSmallIndex.Begin(count);
        foreach (var missile in _missiles)
            Add(missile, missile.Position, missile.Velocity, 3);
        foreach (var probe in _probes)
            Add(probe, probe.Position + Vec3d.From(probe.Velocity) * Math.Clamp(time - Time, 0, TickDelta),
                probe.Velocity, probe.Definition.Radius);
        if (_indexedProjectileSmall) _projectileSmallIndex.Build();

        void Add(object target, Vec3d start, Godot.Vector3 velocity, double radius)
        {
            Vec3d end = start + Vec3d.From(velocity) * dt;
            _indexedProjectileSmall &= IndexableSweep(start, end) && radius >= 0 && radius <= 1e6;
            int id = _projectileSmallCount++;
            _projectileSmallTargets[id] = target;
            _projectileSmallIndex.Add(id, SpatialBounds.Segment(start, end).Expanded(radius));
        }
    }

    // Very long sweeps can make the legacy quadratic accept numerically tangential
    // contacts outside a tight AABB. Keep the original full scan for those cases.
    // Within this domain, the 1 m + coordinate rounding allowance dominates the
    // quadratic error (relative travel <= 2e6 m) as well as subtraction rounding.
    internal static bool IndexableSweep(Vec3d start, Vec3d end) =>
        Math.Abs(start.X) <= 1e15 && Math.Abs(start.Y) <= 1e15 && Math.Abs(start.Z) <= 1e15 &&
        Math.Abs(end.X) <= 1e15 && Math.Abs(end.Y) <= 1e15 && Math.Abs(end.Z) <= 1e15 &&
        Math.Abs(end.X-start.X) <= 1e6 && Math.Abs(end.Y-start.Y) <= 1e6 && Math.Abs(end.Z-start.Z) <= 1e6;

    private void BuildProjectileShipIndex()
    {
        _projectileShipIndex.Begin(_ships.Count);
        for (int i = 0; i < _ships.Count; i++)
            _projectileShipIndex.Add(i, _projectileShipBounds[i].Expanded(_projectileShipRadii[i]));
        _projectileShipIndex.Build();
    }
}
