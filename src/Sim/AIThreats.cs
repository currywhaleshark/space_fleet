using System;
using System.Collections.Generic;

namespace SpaceFleet.Sim;

public sealed partial class SimWorld
{
    private readonly record struct ObservedThreat(SmallContact Contact, Vec3d Position);
    private readonly List<ObservedThreat>[] _aiThreats = { new(), new() };
    private readonly SweepIndex[] _aiThreatIndices = { new(), new() };
    private readonly bool[] _aiThreatIndexed = new bool[2];

    // SensorNet is unchanged throughout StepAI. Cache this tick's *observations*,
    // never the live missile position/target/health. A remembered destroyed missile
    // therefore remains a threat until the same Fresh check expires as before.
    private void PrepareAIThreats()
    {
        for (int side = 0; side < _aiThreats.Length; side++)
        {
            var threats = _aiThreats[side];
            threats.Clear();
            _aiThreatIndexed[side] = false;
            // Filled even without AI brains: a player air-defense cruiser's Free battery reads the same list.
            foreach (var contact in Sensors.SmallContacts((Faction)side))
                if (contact.Kind == SmallContactKind.Missile && contact.Fresh(Time))
                    threats.Add(new(contact, contact.Predict(Time)));
            if (_brains.Count == 0 || _ships.Count < 48 || threats.Count < 16) continue;
            var index = _aiThreatIndices[side];
            index.Begin(threats.Count);
            bool valid = true;
            for (int i = 0; i < threats.Count; i++)
            {
                var position = threats[i].Position;
                if (!IndexableSweep(position, position)) { valid = false; break; }
                index.Add(i, SpatialBounds.Segment(position, position));
            }
            if (valid) { index.Build(); _aiThreatIndexed[side] = true; }
        }
    }

    private bool IncomingMissileThreat(ShipBody ship, float range)
    {
        int side = (int)ship.Faction;
        var threats = _aiThreats[side];
        if (threats.Count == 0) return false;
        var bounds = _aiThreatIndexed[side] ? SpatialBounds.Segment(ship.Position, ship.Position).Expanded(range) : default;
        bool indexed = _aiThreatIndexed[side] && IndexableSweep(bounds.Min, bounds.Max);
        ReadOnlySpan<int> candidates = indexed ? _aiThreatIndices[side].Query(bounds) : default;
        int count = indexed ? candidates.Length : threats.Count;
        for (int i = 0; i < count; i++)
        {
            var threat = threats[indexed ? candidates[i] : i];
            if ((threat.Position - ship.Position).Length() < range && Threatens(threat.Contact, ship, out _)) return true;
        }
        return false;
    }
}
