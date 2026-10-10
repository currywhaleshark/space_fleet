using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

public sealed partial class SimWorld
{
    // Only known contact estimates and identified hull blueprints enter these decisions.
    // Do not select a module by its live health: internal damage is not intelligence.
    private ModuleState? MarsAimModule(ShipBrain brain, ShipBody target)
    {
        SensorTrack track = Sensors.Track(brain.Ship.Faction, target);
        if (track.Level < TrackLevel.Locked) return null;
        ModuleKind kind = brain.Profile.AttackRuns ? ModuleKind.Thruster : ((int)(Time / 30) % 3) switch
        { 0 => ModuleKind.Sensor, 1 => ModuleKind.Cooling, _ => ModuleKind.Gun };
        var module = target.Definition.Modules.Where(m => m.Kind == kind)
            .OrderBy(m => (track.EstimatedPosition + Vec3d.From(target.Orientation * m.Center) - brain.Ship.Position).LengthSquared())
            .ThenBy(m => m.Id, StringComparer.Ordinal).FirstOrDefault();
        return module is null ? null : target.Damage.Modules.First(m => m.Definition.Id == module.Id);
    }

    private void CommandMars(Faction faction, List<(ShipBody Ship, SensorTrack Track)> known, Dictionary<ShipBody, int> assigned)
    {
        foreach (ShipBrain brain in _brains.Values.Where(b => b.Enabled && b.Ship.Faction == faction
            && b.Ship.Definition.Design == DesignFamily.Mars && Squadron.Active(b.Ship) && b.Ship.Squadron?.PlayerLed != true))
        {
            ShipBody ship = brain.Ship;
            bool assault = ship.Class.Kind == HullKind.Interceptor && ship.Ordnance.Antimatter.Rounds > 0;
            var contacts = known.Where(k => k.Track.Level >= TrackLevel.Identified).ToArray();
            // AM carriers choose a capital instead of being drawn into prolonged dogfights.
            var target = contacts.OrderBy(k => assault ? (k.Ship.Class.Kind == HullKind.Battleship ? 0 : k.Ship.Class.Kind == HullKind.Escort ? 1 : 2)
                    : ship.Class.Kind == k.Ship.Class.Kind ? 0 : 1)
                .ThenBy(k => (k.Track.EstimatedPosition - ship.Position).Length() + assigned.GetValueOrDefault(k.Ship) * 3000)
                .Select(k => k.Ship).FirstOrDefault()
                ?? known.OrderBy(k => (k.Track.EstimatedPosition - ship.Position).Length()).Select(k => k.Ship).FirstOrDefault();
            if (target is null) { Assign(ship, ShipOrder.HoldAt(ship.Position), assigned); continue; }
            if (assault && ship.Squadron is not null && Time < 120)
            {
                // Wait for the fleet's opening salvo; racing alone into every loaded launcher wastes the payload.
                var cover = _ships.Where(s => s.Faction == faction && s.Class.Kind == HullKind.Escort && Squadron.Active(s))
                    .OrderBy(s => (s.Position-ship.Position).LengthSquared()).FirstOrDefault();
                if (cover is not null)
                {
                    SetFormation(ship,new Vector3(brain._side*350, -300, 650));
                    Assign(ship,ShipOrder.EscortOf(cover),assigned);
                    ship.Squadron.Activity="강습 대기 · 엄호";
                    continue;
                }
            }
            brain.CommandStandoff = brain.Profile.StandoffMeters;
            // Escorts stay near friendly precision batteries during the opening, sharing the existing sensor net and ECM screen.
            ShipBody? anchor = ship.Class.Kind != HullKind.Escort ? null : _ships
                .Where(s => s.Faction == faction && s.Class.Kind == HullKind.Battleship && Squadron.Active(s))
                .OrderBy(s => (s.Position - ship.Position).LengthSquared()).FirstOrDefault();
            double range = (Sensors.Track(faction, target).EstimatedPosition - ship.Position).Length();
            if (anchor is not null && range > brain.Profile.StandoffMeters * 1.3)
            {
                int slot = _ships.Where(s => s.Faction == faction && s.Class.Kind == HullKind.Escort).TakeWhile(s => s != ship).Count();
                SetFormation(ship, new Vector3(brain._side * (2600 + slot * 450), (slot % 3 - 1) * 1100, -3200 - slot * 650));
                Assign(ship, ShipOrder.EscortOf(anchor, target), assigned);
            }
            else Assign(ship, ShipOrder.AttackOn(target), assigned);
            if (ship.Squadron is { } squad) { squad.Target = target; squad.Activity = assault ? "강습 침투" : "분산 정밀교전"; }
        }
    }
}
