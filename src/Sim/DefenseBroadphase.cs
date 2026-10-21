using System;

namespace SpaceFleet.Sim;

public sealed partial class SimWorld
{
    private readonly SweepIndex[] _pdMissileIndices = { new(), new() }, _pdShipIndices = { new(), new() };
    private Missile[] _pdMissileSnapshot = Array.Empty<Missile>();
    private int _pdMissileSnapshotCount;
    private bool _pdTargetsPrepared;

    // Build after missile flight/fuzes, not from the earlier projectile snapshot.
    // PD can remove missiles or destroy ships as it fires. Stable references plus
    // the existing per-mount live checks preserve both removals and tie order.
    private bool PreparePointDefenseTargets()
    {
        Array.Clear(_pdMissileSnapshot, 0, _pdMissileSnapshotCount);
        _pdMissileSnapshotCount = 0;
        if (_ships.Count < 48) return false;
        if (_pdMissileSnapshot.Length < _missiles.Count)
            Array.Resize(ref _pdMissileSnapshot, Math.Max(_missiles.Count, _pdMissileSnapshot.Length * 2));
        foreach (var index in _pdMissileIndices) index.Begin(_missiles.Count);
        foreach (var missile in _missiles)
        {
            _pdMissileSnapshot[_pdMissileSnapshotCount] = missile;
            var seen=Sensors.SmallTrack((Faction)(1-(int)missile.Faction),missile);
            if(seen is { } contact && contact.Fresh(Time))
            {
                Vec3d point=contact.Predict(Time);
                if(!IndexableSweep(point,point))return false;
                _pdMissileIndices[(int)missile.Faction].Add(_pdMissileSnapshotCount, SpatialBounds.Segment(point,point));
            }
            _pdMissileSnapshotCount++;
        }
        foreach (var index in _pdShipIndices) index.Begin(_ships.Count);
        for (int i = 0; i < _ships.Count; i++)
        {
            var ship = _ships[i];
            double orbit = ship.Definition.DefenseDrones?.OrbitMeters ?? 0;
            if (!IndexableSweep(ship.Position, ship.Position) || !double.IsFinite(orbit) || orbit > 1e6) return false;
            // The old carrier test expands the query by OrbitMeters (including
            // Expanded's rounding allowance). Both indexed positions and queries
            // are bounded to 1e15, so the old query's allowance is at most 11m;
            // 32m covers it conservatively even with opposite rounding directions.
            _pdShipIndices[(int)ship.Faction].Add(i, SpatialBounds.Segment(ship.Position, ship.Position).Expanded(orbit + 32));
        }
        foreach (var index in _pdMissileIndices) index.Build();
        foreach (var index in _pdShipIndices) index.Build();
        return true;
    }
}
