using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

/// <summary>Role within a design tree; independent of team, design and physical hull class.</summary>
public enum ShipBranch
{
    Escort, Interceptor, LineBattleship, GunCruiser, AirDefenseCruiser,
    MissileCruiser, Recon, Assault, DroneCarrier, ArtilleryPlatform,
}

public enum MatchWeightClass { Large, Medium, Small }

/// <summary>Null HullId uses the participant's resolved hull, including its loadout.</summary>
public sealed record SquadronSlot(string? HullId, Vector3 Offset, HullKind? AllowedKind = null);

public sealed record SquadronDefinition(SquadronRole Role, string Composition, IReadOnlyList<SquadronSlot> Members);

// AssaultReachMeters is the maximum observed target distance from the friendly anchor
// for planning an approach. Zero keeps the legacy design default; weapon ranges do not change.
public sealed record ShipAiDefinition(AiProfile Profile, IReadOnlyList<int> DefaultPips,
    IReadOnlyDictionary<HullKind, int> TargetPreference, int MissilesPerSalvo = 2,
    float AssaultReachMeters = 0);

public sealed record BattleValueDefinition(double Weight, double CaptureStrength, float HudMarkerRadius);

internal static class ShipRoleValidation
{
    public static void Validate(ShipDefinition ship)
    {
        void Require(bool valid, string message)
        {
            if (!valid) throw new InvalidDataException($"{ship.Id}: {message}");
        }
        Require(Enum.IsDefined(ship.Branch) && Enum.IsDefined(ship.MatchClass), "invalid branch/match class");
        Require(!string.IsNullOrWhiteSpace(ship.ModelId) && ship.ModelId.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'),
            "invalid model ID");
        var squad = ship.Squadron;
        Require(squad is not null && Enum.IsDefined(squad.Role) && !string.IsNullOrWhiteSpace(squad.Composition)
            && squad.Members is { Count: > 0 and <= 16 }, "invalid squadron definition");
        foreach (var slot in squad!.Members)
            Require(slot is not null && slot.Offset.IsFinite() && (slot.HullId is null || !string.IsNullOrWhiteSpace(slot.HullId))
                && (slot.AllowedKind is null || Enum.IsDefined(slot.AllowedKind.Value)),
                "invalid squadron slot");
        Require(squad.Members[0].HullId is null, "squad leader must use the participant hull");
        Require(squad.Members.Select(s => s.Offset).Distinct().Count() == squad.Members.Count, "overlapping squadron slots");
        var ai = ship.Ai;
        Require(ai is not null && float.IsFinite(ai.Profile.StandoffMeters) && ai.Profile.StandoffMeters > 0
            && float.IsFinite(ai.Profile.RailFlightSeconds) && ai.Profile.RailFlightSeconds > 0
            && float.IsFinite(ai.Profile.MissileRangeMeters) && ai.Profile.MissileRangeMeters > 0, "invalid AI profile");
        Require(ai!.DefaultPips is not null && ai.DefaultPips.Count == ShipPower.ChannelCount
            && ai.DefaultPips.All(p => p >= 0 && p <= ShipPower.MaxPips) && ai.DefaultPips.Sum() == ShipPower.TotalPips,
            "invalid AI power allocation");
        Require(ai.TargetPreference is not null && ai.TargetPreference.Count == Enum.GetValues<HullKind>().Length
            && ai.TargetPreference.All(p => Enum.IsDefined(p.Key) && p.Value >= 0), "invalid AI target preferences");
        Require(ai.MissilesPerSalvo is >= 1 and <= 8, "invalid AI salvo size");
        Require(float.IsFinite(ai.AssaultReachMeters) && ai.AssaultReachMeters >= 0, "invalid assault planning reach");
        var value = ship.BattleValue;
        Require(value is not null && double.IsFinite(value.Weight) && value.Weight > 0
            && double.IsFinite(value.CaptureStrength) && value.CaptureStrength > 0
            && float.IsFinite(value.HudMarkerRadius) && value.HudMarkerRadius > 0, "invalid battle value");
    }

    public static void ValidateReferences(ShipDefinition ship, Func<string, ShipDefinition> lookup)
    {
        foreach (var slot in ship.Squadron.Members)
        {
            ShipDefinition member = ship;
            if (slot.HullId is { } id)
            {
                try { member = lookup(id); }
                catch (ArgumentException e) { throw new InvalidDataException($"{ship.Id}: unknown squad member {id}", e); }
            }
            if (member.Design != ship.Design)
                throw new InvalidDataException($"{ship.Id}: squad member {member.Id} has a different design");
            if (member.Kind != (slot.AllowedKind ?? ship.Kind))
                throw new InvalidDataException($"{ship.Id}: squad member {member.Id} violates the slot's allowed hull kind");
        }
    }
}
