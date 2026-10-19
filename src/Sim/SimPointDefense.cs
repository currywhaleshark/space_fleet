using System;
using Godot;

namespace SpaceFleet.Sim;

public sealed partial class SimWorld
{
    private void EngagePointDefense(ShipBody ship, int mountIndex, Vec3d mount, Vec3d clearOrigin,
        Quaternion inverse, PointDefenseMountState tracking, float quality, Missile? threat,
        ShipBody? raider, ShipBody? droneCarrier, int droneIndex, ReconProbe? probeTarget, double dt, double time)
    {
        var pd = tracking.Definition;
        Vec3d position = threat?.Position ?? raider?.Position ?? probeTarget?.Position
            ?? droneCarrier!.Ordnance.Drones.WorldPosition(droneIndex);
        Vector3 velocity = threat?.Velocity ?? raider?.Velocity ?? probeTarget?.Velocity
            ?? droneCarrier!.Ordnance.Drones.WorldVelocity(droneIndex);
        Vector3 muzzleVelocity = ship.Velocity + ship.Orientation * ship.AngularVelocity.Cross(pd.Mounts[mountIndex]);
        object target = (object?)threat ?? (object?)raider ?? (object?)probeTarget ?? droneCarrier!;
        bool solved = tracking.PredictAim(target, droneIndex, position, velocity, mount, muzzleVelocity, time,
            out Vec3d aim, out Vec3d contact);
        tracking.TargetKind = threat is not null ? PointDefenseTarget.Missile : raider is not null
            ? PointDefenseTarget.Interceptor : probeTarget is not null ? PointDefenseTarget.Probe : PointDefenseTarget.Drone;
        Vector3 bore = (aim - mount).ToVector3();
        tracking.Track(inverse * bore, dt, ship.Power.WeaponEffect);
        float distance = (float)(contact - mount).Length();
        // Current visibility/sector checks still run every substep in selection.
        // A sampled lead must also fit the real barrel arc and clear the owner hull.
        if (!solved || !tracking.Aligned || distance > pd.RangeMeters
            || tracking.FlightSeconds * pd.MuzzleSpeed > pd.RangeMeters)
        { tracking.FireAccumulator = 0; return; }
        tracking.FireAccumulator += pd.ShotsPerSecond * (float)dt;
        if (tracking.FireAccumulator < 1) return;
        Vector3 direction = bore.Normalized();
        Vector3 exit = (aim - clearOrigin).ToVector3();
        float length = exit.Length();
        if (length <= 0 || DamageRay.FirstHit(ship, clearOrigin, exit / length, length, out _)
            || TerrainBlocked(clearOrigin, aim)
            || ((probeTarget is not null || droneCarrier is not null) && !ClearDefenseLine(clearOrigin, aim)))
        { tracking.FireAccumulator = 0; return; }
        float size = raider is not null ? PointDefenseShipSizeFactor : droneCarrier is not null ? 1.5f
            : probeTarget is not null || threat!.Assault is null ? 1f : .8f;
        // Dispersion grows over the bullet's travel to the intercept, not the
        // target's current distance. An inbound missile meets the burst closer;
        // a crossing/receding target requires a longer shot.
        float shotDistance = (float)tracking.FlightSeconds * pd.MuzzleSpeed;
        float chance = Mathf.Clamp(pd.HitChance * (1 - .7f * shotDistance / pd.RangeMeters) * quality * size
            * tracking.PredictionFactor(direction, time, size), 0, 1);
        uint salt = threat?.Id ?? probeTarget?.Id ?? (raider is not null ? ShipBrain.Hash(raider.Callsign)
            : ShipBrain.Hash(droneCarrier!.Callsign) ^ (uint)(droneIndex + 1) * 2654435761u);
        while (tracking.FireAccumulator >= 1 && (threat is null || threat.Health > 0)
            && raider?.Damage.Destroyed != true && (probeTarget is null || probeTarget.Active)
            && (droneCarrier is null || droneCarrier.Ordnance.Drones.Alive(droneIndex)))
        {
            tracking.FireAccumulator--;
            bool hit = Roll(++_pointDefenseSequence * 2246822519u ^ salt) < chance;
            _pointDefenseShots.Add(new(mount, aim, time, hit, ship.Faction));
            tracking.LastFiredAt = time; tracking.ShotCount++;
            if (!hit) continue;
            if (threat is not null) threat.Health -= pd.EffectiveMissileDamage;
            else if (probeTarget is not null) DamageProbe(ship, probeTarget, pd.DamagePerHit, time);
            else if (droneCarrier is not null) DamageDrone(ship, droneCarrier, droneIndex, pd.DamagePerHit, time);
            else
            {
                // Statistical PD resolves immediately, not as a traveling body.
                // Apply the successful hit to the current physical shield/armor pose;
                // the sampled lead remains the mount's aim and the miss tracer endpoint.
                Vector3 hitDirection = (raider!.Position - mount).ToVector3().Normalized();
                float shieldBefore = raider.Damage.Shield;
                var packet = pd.EffectiveShipPacket;
                ShotResult result = DamageRay.Apply(raider, mount, hitDirection, packet, time, ++_shotSequence);
                if (result.Target is not null)
                    _pointDefenseShots[^1] = _pointDefenseShots[^1] with { To = result.ShieldPoint ?? result.Point };
                CreditHit(_shotSequence,ship, result, BattleWeapon.PointDefense, time, shieldBefore);
                RecordImpact(_shotSequence, ship, result, time, hitDirection, packet.Energy,
                    shieldBefore, BattleWeapon.PointDefense);
            }
        }
        if (threat is not null && threat.Health <= 0) DestroyMissile(ship, threat, time);
    }
}
