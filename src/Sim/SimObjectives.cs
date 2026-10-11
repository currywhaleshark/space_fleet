using System;
using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

public sealed partial class SimWorld
{
    /// <summary>
    /// Capture-mode orders remain inside the ordinary command/AI pipeline.
    /// Squadrons manoeuvre to world-space points rather than chasing unobserved enemies.
    /// The closest *known* enemy is a defensive fire target, never a navigation oracle.
    /// Player-led squads retain their own commands.
    /// </summary>
    private void CommandCaptureZones(Faction faction)
    {
        if (Rules is not { Objective: BattleObjective.Domination } rules) return;
        foreach (Squadron squad in _squadrons.Where(s => s.Faction == faction && !s.PlayerLed && s.Leader is not null))
        {
            int index = squad.Role switch
            {
                SquadronRole.BattleGroup => 1,
                SquadronRole.EscortSquadron => faction == Faction.Blue ? 0 : 2,
                _ => 1,
            };
            CaptureZone zone = rules.CaptureZones[Math.Min(index, rules.CaptureZones.Count - 1)];
            ShipBody leader = squad.Leader!;
            // Prefer the assigned sector; reinforce another if it becomes friendly and uncontested.
            if (zone.Owner == faction && zone.ScoringOwner == faction)
            {
                var needed = rules.CaptureZones.Where(z => z.Owner != faction)
                    .OrderBy(z => (z.Position - leader.Position).LengthSquared()).FirstOrDefault();
                if (needed is not null && (zone.Position - leader.Position).Length() < zone.RadiusMeters)
                    zone = needed;
            }
            var known = _ships.Where(s => s.Faction != faction && Squadron.Active(s))
                .Select(s => (Ship: s, Track: Sensors.Track(faction, s)))
                .Where(x => x.Track.Level >= TrackLevel.Contact)
                .OrderBy(x => (x.Track.EstimatedPosition - zone.Position).LengthSquared()).ToList();
            ShipBody? threat = known.FirstOrDefault().Ship;
            // Use stand-off anchor offsets so friendly ships do not all aim at the identical point.
            int slot = 0;
            foreach (ShipBody ship in squad.ActiveMembers)
            {
                Vector3 offset = squad.Role switch
                {
                    SquadronRole.BattleGroup => new Vector3((slot % 2 == 0 ? 1 : -1) * (slot / 2 + 1) * 900, 0, 0),
                    SquadronRole.EscortSquadron => new Vector3((slot % 2 == 0 ? 1 : -1) * (slot / 2 + 1) * 600, 400, 0),
                    _ => new Vector3((slot % 2 == 0 ? 1 : -1) * (slot / 2 + 1) * 250, -300, 0),
                };
                if (ship == leader) offset = Vector3.Zero;
                Assign(ship, ShipOrder.HoldAt(zone.Position + Vec3d.From(offset), threat), new System.Collections.Generic.Dictionary<ShipBody, int>(), false);
                slot++;
            }
            squad.Target = threat;
            squad.Activity = "거점 " + zone.Name;
        }
        // Ships outside a squadron also contribute to the closest objective.
        foreach (ShipBrain brain in _brains.Values.Where(b => b.Ship.Faction == faction && b.Ship.Squadron is null && b.Enabled))
        {
            CaptureZone zone = rules.CaptureZones.OrderBy(z => (z.Position - brain.Ship.Position).LengthSquared()).First();
            brain.Order = ShipOrder.HoldAt(zone.Position);
            brain.CommandStandoff = null;
        }
    }
}
