using System;
using System.Collections.Generic;
using System.Linq;

namespace SpaceFleet.Sim;

/// <summary>
/// Simultaneous lock channels (docs/DESIGN_modular_weapons.md §22): how many locked targets one ship can split a missile
/// salvo across. Small craft 1, escort/cruiser 2, battleship 3; a missile cruiser and a long-range sensor each add one.
/// Only launchers with more than one cell split a salvo, so single-tube stock ships fight exactly as before.
/// </summary>
public static class SensorLocks
{
    public static int Channels(ShipDefinition def) => (def.Kind switch { HullKind.Interceptor => 1, HullKind.Escort => 2, _ => 3 })
        + (def.Branch == ShipBranch.MissileCruiser ? 1 : 0) + (def.Components?.Sensor == "sensor-longrange" ? 1 : 0);

    /// <summary>
    /// Targets for one salvo: the primary first, then other locked enemy ships within missile range, nearest to the primary
    /// (a salvo saturates one formation rather than scattering across the field), up to the ship's lock channels.
    /// </summary>
    public static IReadOnlyList<ShipBody> SalvoTargets(SimWorld world, ShipBody ship, ShipBody primary, double missileRange)
    {
        int channels = ship.Definition.Missiles?.CellCount > 1 ? Channels(ship.Definition) : 1;
        if (channels <= 1) return new[] { primary };
        var primaryAt = world.Sensors.Track(ship.Faction, primary).EstimatedPosition;
        return world.Ships.Where(s => s != primary && s.Faction != ship.Faction && !s.Damage.Destroyed
                && world.Sensors.Track(ship.Faction, s) is { Level: >= TrackLevel.Locked } t
                && (t.EstimatedPosition - ship.Position).Length() <= missileRange)
            .OrderBy(s => (world.Sensors.Track(ship.Faction, s).EstimatedPosition - primaryAt).Length())
            .Take(channels - 1).Prepend(primary).ToArray();
    }
}
