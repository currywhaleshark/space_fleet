using System;
using System.Collections.Generic;
using Godot;

namespace SpaceFleet.Sim;

// A beam hit measures an incoming bearing, not source range, class or hidden module status.
public readonly record struct LaserBearing(Faction Faction, Vec3d ReceiverPosition, Vector3 Direction, double Time);

public sealed partial class SensorNet
{
    private readonly Dictionary<(Faction, ShipBody), LaserBearing> _laserBearings = new();
    public IEnumerable<LaserBearing> LaserBearings(Faction faction, double time)
    {
        foreach (var bearing in _laserBearings.Values)
            if (bearing.Faction == faction && time >= bearing.Time && time - bearing.Time <= 2) yield return bearing;
    }
    internal void ObserveLaserBearing(ShipBody receiver, ShipBody source, Vector3 direction, double time)
    {
        if (receiver.Damage.GenerationFraction <= .001f) return;
        _laserBearings[(receiver.Faction, source)] = new(receiver.Faction, receiver.Position, direction, time);
    }
    private static float LaserSignature(ShipBody ship)
    {
        if (ship.Railgun?.IsLaser != true) return 0;
        float result = 0;
        foreach (var gun in ship.Railguns) if (gun.Laser is { } laser) result = Math.Max(result, laser.SignatureBoost);
        return result;
    }
    internal void ResetLasers() => _laserBearings.Clear();
}
