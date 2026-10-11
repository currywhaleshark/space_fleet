using System;
using System.Collections.Generic;
using System.Linq;

namespace SpaceFleet.Sim;

/// <summary>Immutable observation snapshot, never an enemy body-position query.</summary>
public sealed class TerrainExposure
{
    private readonly ObstacleSet _obstacles;
    private readonly Vec3d[] _observers;
    private readonly Dictionary<Vec3d,double> _samples=new();
    public int Observers=>_observers.Length;
    public TerrainExposure(ObstacleSet obstacles,IEnumerable<Vec3d> observers)
    { _obstacles=obstacles;_observers=observers.ToArray(); }
    public static TerrainExposure Observe(SimWorld world,Faction side)
    {
        var positions=new List<Vec3d>();
        foreach(var enemy in world.Ships)
        {
            if(enemy.Faction==side)continue;
            var track=world.Sensors.Track(side,enemy);
            if(track.Level>=TrackLevel.Contact && !track.ObservedDestroyed)positions.Add(track.EstimatedPosition);
            else if(world.Sensors.Memory(side,enemy,world.Time) is { } memory)positions.Add(memory.Predict(world.Time));
        }
        return new(world.Obstacles,positions);
    }
    public double At(Vec3d point)
    {
        if(_samples.TryGetValue(point,out double value))return value;
        int visible=0;foreach(var observer in _observers)if(!_obstacles.BlocksSegment(observer,point))visible++;
        value=Observers==0?0:(double)visible/Observers;
        _samples[point]=value;return value;
    }
    // 0..1 visible fraction; up to +150% edge cost, independent of fleet size.
    public double Cost(Vec3d a,Vec3d b)
    {
        double exposed=0;for(int i=0;i<4;i++)exposed+=At(Vec3d.Lerp(a,b,(i+.5)/4));
        return (b-a).Length()*(1+1.5*exposed/4);
    }
    public bool CanShortcut(IReadOnlyList<Vec3d> points,int from,int to)
    {
        double original=0;for(int i=from;i<to;i++)original+=Cost(points[i],points[i+1]);
        return Cost(points[from],points[to])<=original*1.01;
    }
}

public sealed partial class SimWorld
{
    public bool CoverCostEnabled { get; set; }=true;
    private double _nextExposureSample;
    private readonly HashSet<ShipBody> _assaultStarted=new(),_assaultArrived=new(),_assaultLost=new();
    internal static bool CoverMission(ShipBody ship)=>ship.Class.Kind==HullKind.Interceptor
        && ship.Squadron is {ObjectiveOrders:false,Mission:InterceptorMission.Recon or InterceptorMission.Assault};
    private void RecordAssaultExposure()
    {
        if(Time<_nextExposureSample || Log is null)return;
        _nextExposureSample=Time+1;
        foreach(var side in Enum.GetValues<Faction>())
        {
            TerrainExposure? exposure=null;
            foreach(var ship in _ships)
            {
                if(ship.Faction!=side || ship.Class.Kind!=HullKind.Interceptor)continue;
                var metrics=Log.Environment;
                if(ship.Ordnance.Interceptor.FirstAssault is not null && _assaultStarted.Add(ship))metrics.AssaultEntrants++;
                if(!Squadron.Active(ship))
                {
                    if(_assaultStarted.Contains(ship) && !_assaultArrived.Contains(ship) && _assaultLost.Add(ship))metrics.AssaultLostBeforeArrival++;
                    continue;
                }
                if(!CoverMission(ship) || ship.Squadron?.Mission!=InterceptorMission.Assault)continue;
                exposure??=TerrainExposure.Observe(this,side);
                metrics.ExposureSamples++;metrics.ExposureSum+=exposure.At(ship.Position);
                if(ship.Squadron?.Mission!=InterceptorMission.Assault || ship.Squadron.Target is not { } target)continue;
                var track=Sensors.Track(side,target);
                if(track.Level>=TrackLevel.Identified && !track.ObservedDestroyed
                    && (track.EstimatedPosition-ship.Position).Length()<=8000 && _assaultArrived.Add(ship))metrics.AssaultArrivals++;
            }
        }
    }
}
