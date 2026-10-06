using System;
using System.Collections.Generic;

namespace SpaceFleet.Sim;

public enum FleetPosture { Approach, Missile, Gunline, Close, Withdraw }
/// <summary>Shared fleet tuning values; no faction-dependent statistics.</summary>
public sealed record FleetDoctrine
{
    public double MissileBandMeters { get; init; } = 120_000;
    public double GunlineMeters { get; init; } = 60_000;
    public double CloseMeters { get; init; } = 12_000;
    public float MissileStockToGunline { get; init; } = 0.35f;
    public double MissilePhaseMaxSeconds { get; init; } = 90;
    public double GunlineMaxSeconds { get; init; } = 360;
    public double GunlineMinimumSeconds { get; init; } = 360;
    public float CloseStrengthRatio { get; init; } = 1.25f;
    public float WithdrawStrengthRatio { get; init; } = 0.4f;
    public double MinimumHoldSeconds { get; init; } = 30;
}
public readonly record struct FleetFacts(double IdentifiedMainRange, float MissileStock, double OwnStrength,
    double KnownEnemyStrength, float? KnownFlagshipPropulsion);
public sealed record FleetPostureChange(double Time, FleetPosture Posture);
public sealed class FleetState
{
    public FleetPosture Posture { get; private set; }
    public double Since { get; private set; }
    private readonly List<FleetPostureChange> _changes = new() { new(0, FleetPosture.Approach) };
    public IReadOnlyList<FleetPostureChange> Changes => _changes;
    public void Update(double time, FleetDoctrine doctrine, FleetFacts facts)
    {
        if (time - Since < doctrine.MinimumHoldSeconds || Posture == FleetPosture.Withdraw) return;
        double ratio = facts.KnownEnemyStrength > 0 ? facts.OwnStrength / facts.KnownEnemyStrength : double.PositiveInfinity;
        FleetPosture next = ratio <= doctrine.WithdrawStrengthRatio ? FleetPosture.Withdraw : Posture switch
        {
            FleetPosture.Approach when facts.IdentifiedMainRange <= doctrine.MissileBandMeters => FleetPosture.Missile,
            FleetPosture.Missile when facts.MissileStock <= doctrine.MissileStockToGunline || time - Since >= doctrine.MissilePhaseMaxSeconds => FleetPosture.Gunline,
            FleetPosture.Gunline when time - Since >= doctrine.GunlineMinimumSeconds && (ratio >= doctrine.CloseStrengthRatio
                || time - Since >= doctrine.GunlineMaxSeconds || facts.KnownFlagshipPropulsion <= 0.5f) => FleetPosture.Close,
            _ => Posture,
        };
        if (next == Posture) return;
        Posture = next; Since = time; _changes.Add(new(time, next));
    }
}
