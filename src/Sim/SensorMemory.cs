using System;
using System.Collections.Generic;
using Godot;

namespace SpaceFleet.Sim;

/// <summary>A frozen observation, not a live track. Never eligible for weapon locking.</summary>
public sealed record SensorMemory(Vec3d Position,Vector3 Velocity,double SeenAt,float InitialError,SensorTrack LastTrack)
{
    public const double Lifetime=60;
    public bool Fresh(double time)=>time>=SeenAt && time-SeenAt<Lifetime;
    public Vec3d Predict(double time)=>Position+Vec3d.From(Velocity)*Math.Clamp(time-SeenAt,0,Lifetime);
    // One-sigma velocity uncertainty, not a guaranteed reachable-set boundary.
    public double ErrorMeters(double time)=>InitialError+Math.Clamp(time-SeenAt,0,Lifetime)*Math.Max(100,Velocity.Length()*.5);
}
public readonly record struct SensorOcclusionStats(int LockLosses,double LockLostSeconds,int Reacquisitions);

public sealed partial class SensorNet
{
    public ObstacleSet? Obstacles { get; set; }
    private Dictionary<(Faction,ShipBody),SensorMemory>? _memories;
    private Dictionary<(Faction,ShipBody),double>? _occludedLocks;
    private SensorOcclusionStats[]? _occlusionStats;
    private double _lastSampleTime;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public SensorMemory? Memory(Faction side,ShipBody target,double? time=null) =>
        _memories?.GetValueOrDefault((side,target)) is { } memory && memory.Fresh(time??_lastSampleTime)?memory:null;
    public SensorOcclusionStats OcclusionStats(Faction side)=>_occlusionStats is null?default:_occlusionStats[(int)side];
    private bool Occluded(Vec3d a,Vec3d b)=>Obstacles is { Count: > 0 } && Obstacles.SegmentBlocked(a,b,out _);

    private float VisibleScreen(Vec3d observer,ShipBody target,IReadOnlyList<ShipBody> ships)
    {
        float screen=0;
        foreach(var support in ships)
        {
            if(support==target || support.Faction!=target.Faction || support.Definition.Design!=DesignFamily.Mars
                || support.Class.Kind!=HullKind.Escort || !Squadron.Active(support) || !support.Power.EcmActive
                || Occluded(support.Position,observer)) continue;
            float falloff=Mathf.Clamp(1f-(float)(support.Position-target.Position).Length()/12000f,0,1);
            screen=Mathf.Max(screen,support.Definition.Sensors.Jammer*support.Power.EcmEffect*.6f*falloff);
        }
        return screen;
    }
    private void ObserveOcclusion(Faction side,ShipBody target,SensorTrack before,SensorTrack now,float blockedTrack,float blockedDetect,double time)
    {
        var key=(side,target); int index=(int)side;
        var stats=_occlusionStats??=new SensorOcclusionStats[2];
        bool lockOccluded=now.Level<TrackLevel.Locked && Classify(blockedTrack,blockedDetect,TrackLevel.Locked)>=TrackLevel.Locked;
        if(_occludedLocks?.TryGetValue(key,out double since)==true)
        {
            stats[index]=stats[index] with { LockLostSeconds=stats[index].LockLostSeconds+Math.Max(0,time-since) };
            if(lockOccluded) _occludedLocks[key]=time; else _occludedLocks.Remove(key);
        }
        else if(before.Level>=TrackLevel.Locked && lockOccluded)
        {
            (_occludedLocks??=new())[key]=time;
            stats[index]=stats[index] with { LockLosses=stats[index].LockLosses+1 };
        }
        if(_memories?.GetValueOrDefault(key) is { } old && !old.Fresh(time)) _memories.Remove(key);
        if(now.Level>=TrackLevel.Contact)
        {
            if(_memories?.Remove(key)==true) stats[index]=stats[index] with { Reacquisitions=stats[index].Reacquisitions+1 };
            return;
        }
        // Only the transition from a real observation can create memory. Later
        // unseen movement/state changes never rewrite or clear that observation.
        if(before.Level>=TrackLevel.Contact && Classify(blockedTrack,blockedDetect,before.Level)>=TrackLevel.Contact)
            (_memories??=new())[key]=new(before.EstimatedPosition,before.ObservedVelocity,
                before.Observation?.Time??_lastSampleTime,before.ErrorMeters,before);
    }
}
