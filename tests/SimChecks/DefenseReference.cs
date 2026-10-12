// Frozen 36a7254 exhaustive PD traversal. Test only; preserves selection,
// slew, firing, damage and same-substep removals for exact comparison.
using System;
using System.Linq;
using Godot;
namespace SpaceFleet.Sim;
public sealed partial class SimWorld
{
    internal TerrainExposure PlanningForCheck(Faction side) => PlanningExposure(side);
    internal void DefenseForCheck(double time, bool reference)
    { if(reference) { ReferencePointDefense(TickDelta/4,time); ReferenceDefenseDrones(TickDelta/4,time); } else { StepPointDefense(TickDelta/4,time); StepDefenseDrones(TickDelta/4,time); _pdTargetsPrepared=false; } }
    internal void ThreatTimeForCheck(long tick) { Tick=tick; PrepareAIThreats(); }
    internal bool IndexedThreatForCheck(ShipBody ship,float range) => IncomingMissileThreat(ship,range);
    internal bool ReferenceThreatForCheck(ShipBody ship,float range) => Sensors.SmallContacts(ship.Faction)
        .Any(t=>t.Kind==SmallContactKind.Missile && t.Fresh(Time)
            && (t.Predict(Time)-ship.Position).Length()<range && Threatens(t,ship,out _));
    private void ReferenceDefenseDrones(double dt, double time)
    {
        foreach(var ship in _ships)
        {
            if (ship.Definition.DefenseDrones is not { } def) continue;
            var drones=ship.Ordnance.Drones;
            if (!drones.Active) { drones.ClearTracking(); continue; }
            float quality=Mathf.Clamp(ship.Damage.SensorFraction*ship.Power.SensorEffect*ship.Power.WeaponEffect,0,1.5f);
            for(int i=0;i<def.Count;i++)
            {
                drones.Aim(i,Vector3.Zero);
                if(!drones.Alive(i) || drones.Rounds[i]==0) { drones.Accum[i]=0; continue; }
                Vec3d origin=ship.Position+Vec3d.From(ship.Orientation*drones.LocalPosition(i,time));
                Missile? target=null; double nearest=def.RangeMeters;
                foreach(var m in _missiles)
                {
                    double distance=(m.Position-origin).Length();
                    if(m.Faction==ship.Faction || m.Health<=0 || distance>=nearest
                        || !drones.Covers(ship.Orientation.Inverse()*(m.Position-ship.Position).ToVector3())) continue;
                    if(!ClearDefenseLine(origin,m.Position)) continue;
                    target=m; nearest=distance;
                }
                ShipBody? raider=null;
                if (target is null)
                    foreach (var other in _ships)
                    {
                        if(other.Faction==ship.Faction || other.Class.Kind!=HullKind.Interceptor || other.Damage.Destroyed) continue;
                        double distance=(other.Position-origin).Length();
                        if(distance>=nearest || !drones.Covers(ship.Orientation.Inverse()*(other.Position-ship.Position).ToVector3())
                            || !ClearDefenseLine(origin,other.Position,other)) continue;
                        raider=other; nearest=distance;
                    }
                if(target is null && raider is null) { drones.Accum[i]=0; continue; }
                Vec3d aim=target?.Position??raider!.Position;
                drones.Aim(i,ship.Orientation.Inverse()*(aim-origin).ToVector3().Normalized());
                drones.Accum[i]+=(float)dt*def.ShotsPerSecond;
                while(drones.Accum[i]>=1 && drones.Rounds[i]>0 && (target is null || target.Health>0) && raider?.Damage.Destroyed!=true)
                {
                    drones.Accum[i]--; drones.Rounds[i]--;
                    drones.LastFiredAt[i]=time;
                    float chance=def.HitChance*quality*(1-.6f*(float)nearest/def.RangeMeters);
                    Vector3 direction=(aim-origin).ToVector3().Normalized();
                    if(raider is not null)
                    {
                        Vector3 relative=raider.Velocity-drones.WorldVelocity(i);
                        float crossing=(relative-direction*relative.Dot(direction)).Length();
                        chance*=PointDefenseShipSizeFactor*Mathf.Min(1,PointDefenseCrossingSpeed/Mathf.Max(crossing,1));
                    }
                    uint salt=target?.Id??ShipBrain.Hash(raider!.Callsign);
                    bool hit=Roll(++_pointDefenseSequence*2246822519u ^ salt)<chance;
                    _pointDefenseShots.Add(new(origin,aim,time,hit,ship.Faction));
                    if(!hit) continue;
                    if(target is not null) target.Health-=def.DamagePerHit;
                    else
                    {
                        float shieldBefore=raider!.Damage.Shield;
                        ShotResult result=DamageRay.Apply(raider,origin,direction,def.ShipPacket,time,++_shotSequence);
                        if(result.Target is not null)
                        {
                            drones.ShipHits++;
                            _pointDefenseShots[^1]=_pointDefenseShots[^1] with { To=result.ShieldPoint??result.Point };
                        }
                        Log?.Hit(ship,result,BattleWeapon.DefenseDrone,time,shieldBefore);
                        RecordImpact(_shotSequence,ship,result,time,direction,def.ShipEnergy,shieldBefore,BattleWeapon.DefenseDrone);
                    }
                }
                if(target is not null && target.Health<=0 && _missiles.Remove(target))
                {
                    Log?.Intercept(ship, target);
                    drones.Interceptions++;
                    Log?.EndAntimatter(target,AntimatterOutcome.Drone,time);
                    _ordnanceEvents.Add(new(OrdnanceEventKind.Intercepted,target.Position,time,target.Faction,target.Weapon));
                }
            }
        }
    }

    private void ReferencePointDefense(double dt, double time)
    {
        bool anyInterceptors = false, anyDrones = false;
        foreach (ShipBody s in _ships)
        {
            anyInterceptors |= s.Class.Kind == HullKind.Interceptor && !s.Damage.Destroyed;
            anyDrones |= !s.Damage.Destroyed && s.Ordnance.Drones.SurvivingCount>0;
        }
        foreach (ShipBody ship in _ships)
        {
            if (ship.Definition.PointDefense is not PointDefenseDefinition pd) continue;
            float quality = Mathf.Clamp(ship.Damage.SensorFraction * ship.Power.SensorEffect, 0f, 1.5f) * ship.Power.WeaponEffect;
            if (ship.Damage.Destroyed || quality <= 0.001f || _missiles.Count == 0 && !anyInterceptors && !anyDrones && _probes.Count == 0)
            {
                foreach (var state in ship.Ordnance.PointDefense) state.ClearTracking();
                continue;
            }

            if (pd.Mounts.Length == 0) continue;
            if (_pdMountPositions.Length < pd.Mounts.Length) Array.Resize(ref _pdMountPositions, pd.Mounts.Length);
            var bounds = SpatialBounds.Segment(ship.Position, ship.Position);
            for (int i = 0; i < pd.Mounts.Length; i++)
            {
                _pdMountPositions[i] = ship.Position + Vec3d.From(ship.Orientation * pd.Mounts[i]);
                bounds = bounds.Include(_pdMountPositions[i]);
            }
            bounds = bounds.Expanded(pd.RangeMeters * PointDefenseTrackingRangeMultiplier);
            _pdMissileCandidates.Clear(); _pdRaiderCandidates.Clear(); _pdCarrierCandidates.Clear(); _pdProbeCandidates.Clear();
            foreach (var probe in _probes)
                if (probe.Faction != ship.Faction && probe.Active && bounds.Contains(probe.Position)
                    && Sensors.SmallTrack(ship.Faction, probe) is { } seen && seen.Fresh(Time)) _pdProbeCandidates.Add(probe);
            foreach (var missile in _missiles)
                if (missile.Faction != ship.Faction && missile.Health > 0 && bounds.Contains(missile.Position))
                    _pdMissileCandidates.Add(missile);
            foreach (var other in _ships)
            {
                if (other.Faction == ship.Faction || other.Damage.Destroyed) continue;
                if (other.Class.Kind == HullKind.Interceptor && bounds.Contains(other.Position)) _pdRaiderCandidates.Add(other);
                if (other.Definition.DefenseDrones is { } drones && bounds.Expanded(drones.OrbitMeters).Contains(other.Position))
                    _pdCarrierCandidates.Add(other);
            }
            if (_pdMissileCandidates.Count == 0 && _pdRaiderCandidates.Count == 0 && _pdCarrierCandidates.Count == 0 && _pdProbeCandidates.Count == 0)
            {
                foreach (var state in ship.Ordnance.PointDefense) state.ClearTracking();
                continue;
            }
            Quaternion inverse = ship.Orientation.Inverse();
            for (int i = 0; i < pd.Mounts.Length; i++)
            {
                Vec3d mount = _pdMountPositions[i];
                var tracking = ship.Ordnance.PointDefense[i];
                Missile? threat = null;
                ReconProbe? probeTarget = null;
                ShipBody? raider = null, droneCarrier = null;
                int droneIndex=-1, bestPriority=int.MaxValue;
                double nearest = pd.RangeMeters * PointDefenseTrackingRangeMultiplier;
                bool Candidate(Vec3d point,int priority, out double distance, out int rank)
                {
                    distance=(point-mount).Length(); rank=(distance<=pd.RangeMeters ? 0 : 10)+priority;
                    if(distance>pd.RangeMeters*PointDefenseTrackingRangeMultiplier) return false;
                    if(rank>bestPriority || rank==bestPriority && distance>=nearest) return false;
                    if(TerrainBlocked(mount,point)) return false;
                    return tracking.Contains(inverse * (point-mount).ToVector3());
                }
                foreach (Missile m in _pdMissileCandidates)
                {
                    if (m.Faction == ship.Faction || m.Health<=0) continue;
                    if (Candidate(m.Position,0,out double distance,out int rank))
                    { bestPriority=rank; nearest=distance; threat=m; }
                }

                if (anyInterceptors)
                    foreach (ShipBody other in _pdRaiderCandidates)
                    {
                        if (other.Faction == ship.Faction || other.Class.Kind != HullKind.Interceptor || other.Damage.Destroyed) continue;
                        if (Candidate(other.Position,ship.Class.Kind==HullKind.Interceptor ? 2 : 1,out double distance,out int rank))
                        { bestPriority=rank; nearest=distance; raider=other; threat=null; }
                    }
                if (anyDrones)
                    foreach (var carrier in _pdCarrierCandidates)
                    {
                        if (carrier.Faction==ship.Faction || carrier.Damage.Destroyed || carrier.Definition.DefenseDrones is not { } droneDef) continue;
                        double range=pd.RangeMeters*PointDefenseTrackingRangeMultiplier+droneDef.OrbitMeters;
                        if ((carrier.Position-mount).LengthSquared()>range*range) continue;
                        var drones=carrier.Ordnance.Drones;
                        for (int j=0;j<droneDef.Count;j++)
                        {
                            if (!drones.Alive(j)) continue;
                            Vec3d point=drones.WorldPosition(j);
                            if (!Candidate(point,ship.Class.Kind==HullKind.Interceptor ? 1 : 2,out double distance,out int rank)) continue;
                            // Only a candidate that can replace the current target needs an occlusion ray.
                            // The pivot lies on the armor surface; start just outside the local deck.
                            Vec3d clearOrigin=mount+Vec3d.From(ship.Orientation*(tracking.MountBasis*Vector3.Up)*.05f);
                            if (!ClearDefenseLine(clearOrigin,point)) continue;
                            bestPriority=rank; nearest=distance;
                            droneCarrier=carrier; droneIndex=j; threat=null; raider=null;
                        }
                    }
                foreach (var probe in _pdProbeCandidates)
                {
                    if (!probe.Active) continue;
                    if (!Candidate(probe.Position, 3, out double distance, out int rank)) continue;
                    if (!ClearDefenseLine(mount + Vec3d.From(ship.Orientation * (tracking.MountBasis * Vector3.Up) * .05f), probe.Position)) continue;
                    bestPriority = rank; nearest = distance; probeTarget = probe; threat = null; raider = null; droneCarrier = null;
                }
                if (threat is null && raider is null && droneCarrier is null && probeTarget is null) { tracking.ClearTracking(); continue; }

                Vec3d aim = threat?.Position ?? raider?.Position ?? probeTarget?.Position ?? droneCarrier!.Ordnance.Drones.WorldPosition(droneIndex);
                tracking.Track(inverse * (aim-mount).ToVector3(), dt, ship.Power.WeaponEffect);
                tracking.TargetKind=threat is not null ? PointDefenseTarget.Missile : raider is not null ? PointDefenseTarget.Interceptor : probeTarget is not null ? PointDefenseTarget.Probe : PointDefenseTarget.Drone;
                if (!tracking.Aligned || nearest > pd.RangeMeters) { tracking.FireAccumulator = 0; continue; }
                tracking.FireAccumulator += pd.ShotsPerSecond * (float)dt;
                Vector3 velocity = threat?.Velocity ?? raider?.Velocity ?? probeTarget?.Velocity ?? droneCarrier!.Ordnance.Drones.WorldVelocity(droneIndex);
                uint salt = threat?.Id ?? probeTarget?.Id ?? (raider is not null ? ShipBrain.Hash(raider.Callsign) : ShipBrain.Hash(droneCarrier!.Callsign)^(uint)(droneIndex+1)*2654435761u);
                while (tracking.FireAccumulator >= 1f && (threat is null || threat.Health > 0)
                    && raider?.Damage.Destroyed!=true && (probeTarget is null || probeTarget.Active) && (droneCarrier is null || droneCarrier.Ordnance.Drones.Alive(droneIndex)))
                {
                    tracking.FireAccumulator -= 1f;
                    Vector3 los = (aim - mount).ToVector3();
                    float d = Mathf.Max(los.Length(), 1f);
                    Vector3 rh = los / d;
                    Vector3 rel = velocity - ship.Velocity;
                    float crossing = (rel - rh * rel.Dot(rh)).Length();
                    float p = pd.HitChance * (1f - 0.7f * d / pd.RangeMeters)
                        * Mathf.Min(1f, PointDefenseCrossingSpeed / Mathf.Max(crossing, 1f)) * quality
                        * (raider is not null ? PointDefenseShipSizeFactor : droneCarrier is not null ? 1.5f : probeTarget is not null ? 1f : threat!.Assault is null ? 1f : .8f);
                    bool hit = Roll(++_pointDefenseSequence * 2246822519u ^ salt) < p;
                    _pointDefenseShots.Add(new PointDefenseShot(mount, aim, time, hit, ship.Faction));
                    tracking.LastFiredAt = time;
                    tracking.ShotCount++;
                    if (!hit) continue;
                    if (threat is not null)
                        threat.Health -= pd.DamagePerHit;
                    else if (probeTarget is not null)
                        DamageProbe(ship, probeTarget, pd.DamagePerHit, time);
                    else if (droneCarrier is not null)
                        DamageDrone(ship,droneCarrier,droneIndex,pd.DamagePerHit,time);
                    else
                    {
                        float shieldBefore = raider!.Damage.Shield;
                        ShotResult result = DamageRay.Apply(raider, mount, rh, PointDefenseShipPacket, time, ++_shotSequence);
                        if (result.Target is not null)
                            _pointDefenseShots[^1] = _pointDefenseShots[^1] with { To = result.ShieldPoint ?? result.Point };
                        Log?.Hit(ship, result, BattleWeapon.PointDefense, time, shieldBefore);
                        RecordImpact(_shotSequence, ship, result, time, rh, PointDefenseShipPacket.Energy,
                            shieldBefore, BattleWeapon.PointDefense);
                    }
                }
                if (threat is not null && threat.Health <= 0) DestroyMissile(ship, threat, time);
            }
        }
    }

}
public sealed partial class SensorNet
{
    internal void SetSmallForCheck(Faction faction,SmallContact contact) => _smallContacts[(faction,contact.Target)]=contact;
}
