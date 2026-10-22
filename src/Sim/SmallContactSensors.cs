using System;
using System.Collections.Generic;
using Godot;

namespace SpaceFleet.Sim;

public enum SmallContactKind { Missile, Probe }
public readonly record struct SmallContact(object Target, SmallContactKind Kind, TrackLevel Level,
    Vec3d Position, Vector3 Velocity, double SeenAt)
{
    public Vector3 Acceleration { get; init; }
    public float Signal { get; init; }
    public float ErrorMeters { get; init; }
    public ShipBody? Observer { get; init; }
    public bool Locked(double time) => Level >= TrackLevel.Locked && Fresh(time);
    public bool Fresh(double time) => Level >= TrackLevel.Contact && time >= SeenAt && time - SeenAt <= .5;
    public Vec3d Predict(double time) => Position + Vec3d.From(Velocity) * Math.Clamp(time - SeenAt, 0, .5);
}

public sealed partial class SensorNet
{
    private readonly HashSet<(Faction, ShipBody, TrackLevel)> _probeDiscoveries = new();
    private readonly Dictionary<(Faction, object), SmallContact> _smallContacts = new();
    private readonly List<(Faction, object)> _expiredSmallContacts = new();
    private readonly List<ShipBody> _smallJammers = new();
    public IEnumerable<SmallContact> SmallContacts(Faction faction)
    {
        foreach (var pair in _smallContacts) if (pair.Key.Item1 == faction) yield return pair.Value;
    }
    public SmallContact? SmallTrack(Faction faction, object target) =>
        _smallContacts.TryGetValue((faction, target), out var track) ? track : null;

    public static float Signature(Missile missile) => (missile.Burning ? missile.Definition.BurningSignature
        : missile.Definition.SensorSignature) * missile.Definition.BodyScale * missile.Definition.BodyScale;
    // Dedicated small-object tracking. These gains do not improve hostile ship locks.
    public static float SmallTrackingGain(ShipBody ship) => ship.Definition.Branch == ShipBranch.AirDefenseCruiser ? 8f
        : ship.Class.Kind == HullKind.Interceptor ? 16f : 1f;

    private void UpdateSmallContacts(IReadOnlyList<ShipBody> ships, IReadOnlyList<ReconProbe> probes,
        IReadOnlyList<Missile> missiles, double time)
    {
        _smallJammers.Clear();
        foreach(var ship in ships)if(ship.Definition.Design==DesignFamily.Mars&&ship.Class.Kind==HullKind.Escort
            &&Squadron.Active(ship)&&ship.Power.EcmActive)_smallJammers.Add(ship);
        foreach (Faction side in Enum.GetValues<Faction>())
        {
            foreach (var missile in missiles)
                if (missile.Faction != side && missile.Health > 0)
                    Observe(side, missile, SmallContactKind.Missile, missile.Position, missile.Velocity,
                        Signature(missile), ships, probes, time);
            foreach (var probe in probes)
                if (probe.Faction != side && probe.Active)
                    Observe(side, probe, SmallContactKind.Probe, probe.Position, probe.Velocity,
                        probe.Definition.Signature, ships, probes, time);
        }
        _expiredSmallContacts.Clear();
        foreach (var pair in _smallContacts)
            if (time - pair.Value.SeenAt > 20) _expiredSmallContacts.Add(pair.Key);
        foreach (var key in _expiredSmallContacts) _smallContacts.Remove(key);
    }

    private void Observe(Faction side, object target, SmallContactKind kind, Vec3d position, Vector3 velocity,
        float signature, IReadOnlyList<ShipBody> ships, IReadOnlyList<ReconProbe> probes, double time)
    {
        float signal = 0, detect = 0; bool blocked=false;
        ShipBody? source=null; double sourceKm=0;
        foreach (var observer in ships)
        {
            if (observer.Faction != side) continue;
            double km = Math.Max(.01, (position - observer.Position).Length() / 1000);
            double measured=Strength(observer)*signature/(km*km)*SmallTrackingGain(observer)*observer.Tactical.ObservationFactor((position-observer.Position).ToVector3());
            if(measured<ContactSnr*DropFactor || measured<=signal)continue;
            float candidate=(float)measured/(1+SmallJamming(side,observer.Position,position,observer.Power.SensorEffect));
            // Below the lowest hysteresis threshold this observer cannot change
            // the contact or its occlusion memory. Nor can a weaker observation
            // improve an already visible signal. Preserve double blocked tests.
            if(candidate<ContactSnr*DropFactor || candidate<=signal)continue;
            if(Occluded(observer.Position,position)) { blocked|=measured>=ContactSnr; continue; }
            if(candidate>signal){signal=candidate;source=observer;sourceKm=km;}
            detect=Math.Max(detect,candidate);
        }
        foreach (var probe in probes)
        {
            if (probe.Faction != side || !probe.Active) continue;
            double km = Math.Max(.01, (position - probe.Position).Length() / 1000);
            double measured=ProbeStrength(probe,position)*signature/(km*km);
            float candidate=Math.Min(5.5f,(float)measured/(1+SmallJamming(side,probe.Position,position,1))); // Recon probes cannot supply a precision lock alone.
            if(candidate<ContactSnr*DropFactor || candidate<=signal)continue;
            if(Occluded(probe.Position,position)) { blocked|=measured>=ContactSnr; continue; }
            if(candidate>signal){signal=candidate;source=probe.Owner;sourceKm=km;}
            detect=Math.Max(detect,(float)measured);
        }
        var key = (side, target);
        var previous = _smallContacts.GetValueOrDefault(key);
        var level = Classify(signal, detect, previous.Level);
        if (level == TrackLevel.None)
        {
            if(blocked && previous.Target==target) _smallContacts[key]=previous with { Level=TrackLevel.None };
            return; // Preserve last observed location; AI rejects stale samples.
        }
        double elapsed = time - previous.SeenAt;
        Vector3 acceleration = previous.Target == target && elapsed >= .1 && elapsed <= .5
            ? ((velocity - previous.Velocity) / (float)elapsed).LimitLength(2000) : Vector3.Zero;
        float error=(.12f+.035f*(float)sourceKm)/MathF.Sqrt(Math.Max(.1f,signal/LockSnr));
        if(level<TrackLevel.Locked)error*=level==TrackLevel.Contact?12:4;
        uint id=target is Missile m?m.Id:((ReconProbe)target).Id;
        Vec3d offset=FireControl.SmoothNoise(id^(uint)side*2654435761u,time,.5)*error;
        _smallContacts[key] = new(target, kind, level, position+offset, velocity, time)
            { Acceleration = acceleration,Signal=signal,ErrorMeters=error,Observer=source };
    }

    private static float ProbeStrength(ReconProbe probe, Vec3d target)
    {
        Vector3 delta = (target - probe.Position).ToVector3();
        float facing = delta.LengthSquared() > 1 ? probe.Direction.Dot(delta.Normalized()) : 1;
        return ProbeDirectionalStrength(probe.Definition, facing);
    }

    private float SmallJamming(Faction observerSide,Vec3d observer,Vec3d point,float eccm)
    {
        float jam=0;
        foreach(var support in _smallJammers)
        {
            if(support.Faction==observerSide)continue;
            float falloff=Mathf.Clamp(1-(float)(support.Position-point).Length()/12000,0,1);
            if(falloff>0&&!Occluded(observer,support.Position))
                jam=Math.Max(jam,Jammer(support)*support.Power.EcmEffect*.6f*falloff);
        }
        return EffectiveJamming(jam/Math.Max(.05f,eccm));
    }

    public static float ProbeDirectionalStrength(ReconProbeDefinition definition, float facing)
        => definition.SensorStrength * (.65f + .35f * Mathf.Clamp(facing, 0, 1));

    /// <summary>Nominal new-contact range in meters: signature 1, no jamming or engine/heat boost.</summary>
    public static double NominalContactRange(float strength) => Math.Sqrt(Math.Max(0, strength) / ContactSnr) * 1000;

    private (float Track, float Detect, float Range, float ErrorScale, float BlockedTrack, float BlockedDetect, ReconProbe? Source, ReconProbe? DetectSource) ProbeObservation(Faction side,
        ShipBody target, IReadOnlyList<ReconProbe> probes, float shipTrack, float shipDetect, float screen, double time,IReadOnlyList<ShipBody> ships)
    {
        float best = 0, detect = 0, range = 0, diversity = 0;
        ReconProbe? source=null, detectSource=null;
        float blockedTrack=0,blockedDetect=0;
        Vector3 firstDirection = Vector3.Zero;
        TrackLevel withoutProbes = Classify(shipTrack, shipDetect, TrackLevel.None);
        foreach (var probe in probes)
        {
            if (probe.Faction != side || !probe.Active) continue;
            Vector3 delta = (target.Position - probe.Position).ToVector3();
            float km = Mathf.Max(.001f, delta.Length() / 1000);
            float signal = ProbeStrength(probe, target.Position) * Signature(target) / (km * km);
            float jammer = Mathf.Max(Obstacles is {Count:>0}?VisibleScreen(probe.Position,target,ships):screen,
                target.Power.EcmActive ? Jammer(target) * target.Power.EcmEffect : 0);
            // Below even the lock hysteresis threshold. Multiple probes do not stack lock strength.
            float track = Mathf.Min(5.5f, signal / (1 + EffectiveJamming(jammer)));
            if(Occluded(probe.Position,target.Position))
            { blockedTrack=Mathf.Max(blockedTrack,track); blockedDetect=Mathf.Max(blockedDetect,signal); continue; }
            if(signal>detect)detectSource=probe;
            detect = Mathf.Max(detect, signal);
            if (track > best) { best = track; range = delta.Length(); source=probe; }
            if (track < ContactSnr) continue;
            probe.Observing++;
            Vector3 direction = delta.Normalized();
            if (firstDirection == Vector3.Zero) firstDirection = direction;
            else diversity += 1 - Mathf.Abs(firstDirection.Dot(direction));
            if (withoutProbes < TrackLevel.Contact && probe.Contacts.Add(target))
            { if (_probeDiscoveries.Add((side, target, TrackLevel.Contact))) probe.Owner.Ordnance.Interceptor.NewContacts++; probe.Owner.Ordnance.Interceptor.FirstAction ??= time; }
            if (withoutProbes < TrackLevel.Identified && track >= IdentifySnr && probe.Identifications.Add(target)
                && _probeDiscoveries.Add((side, target, TrackLevel.Identified)))
                probe.Owner.Ordnance.Interceptor.NewIdentifications++;
        }
        return (best, detect, range, 1 / Mathf.Sqrt(1 + Mathf.Min(1.8f, diversity)),blockedTrack,blockedDetect,source,detectSource);
    }
}
