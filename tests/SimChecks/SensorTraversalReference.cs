// Frozen 36a7254 full ship-observer traversal for occlusion regression checks.
using System;
using System.Collections.Generic;
using Godot;
namespace SpaceFleet.Sim;
public sealed partial class SensorNet
{
    internal void ReferenceUpdate(IReadOnlyList<ShipBody> ships, double time, bool force = false, IReadOnlyList<ReconProbe>? probes = null, IReadOnlyList<Missile>? missiles = null)
    {
        if (!force && time < _nextUpdate)
            return;
        _nextUpdate = time + UpdateInterval;
        bool environment = Obstacles is { Count: > 0 } || _memories is { Count: > 0 } || _occludedLocks is { Count: > 0 };
        probes ??= Array.Empty<ReconProbe>();
        foreach (var probe in probes) probe.Observing = 0;
        UpdateSmallContacts(ships, probes, missiles ?? Array.Empty<Missile>(), time);

        foreach (Faction side in Enum.GetValues<Faction>())
        foreach (ShipBody target in ships)
        {
            if (target.Faction == side)
                continue;
            float bestTrack = 0f, bestDetect = 0f, bestJam = 0f;
            float blockedTrack = 0, blockedDetect = 0;
            ShipBody? bestObserver = null;
            double bestRange = double.PositiveInfinity;
            float screen = 0;
            foreach (ShipBody support in ships)
            {
                if (support == target || support.Faction != target.Faction || support.Definition.Design != DesignFamily.Mars
                    || support.Class.Kind != HullKind.Escort || !Squadron.Active(support) || !support.Power.EcmActive) continue;
                float falloff = Mathf.Clamp(1f - (float)(support.Position - target.Position).Length() / 12_000f, 0, 1);
                screen = Mathf.Max(screen, support.Definition.Sensors.Jammer * support.Power.EcmEffect * .6f * falloff);
            }
            foreach (ShipBody observer in ships)
            {
                if (observer.Faction != side || observer.Damage.Destroyed)
                    continue;
                var (track, detect, jam) = Measure(observer, target, environment && screen>0 ? VisibleScreen(observer.Position,target,ships) : screen);
                if (environment && Occluded(observer.Position,target.Position))
                { blockedTrack=Mathf.Max(blockedTrack,track); blockedDetect=Mathf.Max(blockedDetect,detect); continue; }
                bestDetect = Mathf.Max(bestDetect, detect);
                if (track > bestTrack)
                {
                    bestTrack = track;
                    bestJam = jam;
                    bestRange = (target.Position - observer.Position).Length();
                    bestObserver = observer;
                }
            }
            if (!double.IsFinite(bestRange))
                bestRange = 0;

            var probeData = ProbeObservation(side, target, probes, bestTrack, bestDetect, screen, time, ships);
            if (probeData.Track > bestTrack)
            { bestTrack = probeData.Track; bestRange = probeData.Range; bestObserver = null; }
            bestDetect = Mathf.Max(bestDetect, probeData.Detect);
            SensorTrack before = Track(side,target);
            TrackLevel previous = before.Level;
            TrackLevel level = Classify(bestTrack, bestDetect, previous);
            float error = probeData.ErrorScale * (float)(bestRange * PositionErrorPerRange / Math.Sqrt(Math.Max(bestTrack, 0.25f)));
            uint seed = Hash(side.ToString()) ^ Hash(target.Callsign);
            Vector3 offset = (FireControl.SmoothNoise(seed, time, OffsetDriftRate) * error).ToVector3();
            _tracks[(side, target)] = level == TrackLevel.None ? SensorTrack.Unknown
                : new SensorTrack(level, bestTrack, bestDetect, bestJam, bestRange, error, target.Position + offset, offset)
                {
                    ObservedDestroyed = target.Damage.Destroyed, ObservedDisabled = target.Damage.Disabled,
                    ObservedShieldFraction = target.Damage.ShieldCapacity > 0 ? target.Damage.Shield / target.Damage.ShieldCapacity : 0,
                    ObservedVelocity = target.Velocity, ObservedOrientation = target.Orientation,
                    Observation = bestObserver is null ? null : new SensorObservation(bestObserver, time,
                        target.Position, target.Velocity, target.Orientation,
                        bestObserver.Damage.SensorFraction * bestObserver.Power.SensorEffect),
                };
            if(environment) ObserveOcclusion(side,target,before,_tracks[(side,target)],
                Mathf.Max(blockedTrack,probeData.BlockedTrack),Mathf.Max(blockedDetect,probeData.BlockedDetect),time);
        }
        _lastSampleTime=time;
    }

}
