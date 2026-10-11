using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

public sealed partial class SimWorld
{
    private sealed class SightState
    {
        public ShipBody Target=null!;
        public double BlockedSince,LastBlocked,RetryAt,Started;
        public long CountedTick=-1;
        public Vec3d? Destination;
        public bool Completed;
        public Vec3d TargetAt,OrderPoint;
        public OrderKind OrderKind;
        public ShipBody? OrderTarget;
    }
    private readonly Dictionary<ShipBrain,SightState> _sightStates=new();
    private void RecordTerrainFire(ShipBrain brain,ShipBody target,bool blocked,bool fired)
    {
        if(!_sightStates.TryGetValue(brain,out var state) || state.Target!=target)
        {
            if(!blocked)return;
            if(state?.Destination is not null && !state.Completed && Log is not null)Log.Environment.SightFailures++;
            _sightStates[brain]=state=new(){Target=target,BlockedSince=Time};
        }
        if(fired)
        {
            if(state.Destination is not null && !state.Completed && Log is not null)
            {Log.Environment.SightSuccesses++;Log.Environment.SightReopenSeconds+=Time-state.Started;}
            if(state.Destination is null)_sightStates.Remove(brain);
            else {state.Completed=true;state.LastBlocked=double.NegativeInfinity;}
            return;
        }
        if(!blocked)return;
        if(Time-state.LastBlocked>.6)state.BlockedSince=Time;
        state.LastBlocked=Time;
        if(state.CountedTick!=Tick && Log is not null)
        {Log.Environment.BlockedFireSeconds+=TickDelta;state.CountedTick=Tick;}
    }
    private bool SightWithinOrder(ShipBrain brain,Vec3d position)
    {
        if(brain.Order.Kind==OrderKind.Hold)
        {
            if(brain.Ship.Squadron?.ObjectiveOrders==true && Rules?.Objective==BattleObjective.Domination)
            {
                var zone=Rules.CaptureZones.OrderBy(z=>(z.Position-brain.Order.Point).LengthSquared()).First();
                return (position-zone.Position).Length()<=zone.RadiusMeters-brain.Ship.Hull.BoundingRadius;
            }
            return (position-brain.Order.Point).Length()<=4500;
        }
        if(brain.Order.Kind==OrderKind.Escort && brain.Order.Target is { } leader)
            return (position-leader.Position).Length()<=Math.Max(6000,brain.FormationOffset.Length()+4000);
        return true;
    }
    private Vector3 FindFiringPosition(ShipBrain brain,Vector3 desired)
    {
        if(Navigation is null || !_sightStates.TryGetValue(brain,out var state))return desired;
        var ship=brain.Ship;
        var track=Sensors.Track(ship.Faction,state.Target);
        bool valid=brain.Target==state.Target && !track.ObservedDestroyed && track.Level>=TrackLevel.Identified && Sensors.Memory(ship.Faction,state.Target,Time) is null;
        if(state.Completed && (brain.Order.Kind!=state.OrderKind || brain.Order.Target!=state.OrderTarget
            || (brain.Order.Point-state.OrderPoint).Length()>5000 || (track.EstimatedPosition-state.TargetAt).Length()>5000))
        {_sightStates.Remove(brain);return desired;}
        if(!valid || state.Destination is null && Time-state.LastBlocked>.6)
        {
            if(state.Destination is not null && !state.Completed && Log is not null)Log.Environment.SightFailures++;
            _sightStates.Remove(brain);return desired;
        }
        if(state.Completed && Time-state.LastBlocked<.6 && Time-state.BlockedSince>=5)
        {state.Destination=null;state.Completed=false;state.RetryAt=Time;}
        if(state.Destination is { } active && (!SightWithinOrder(brain,active) || !state.Completed && Time-state.Started>45))
        {
            if(!state.Completed && Log is not null)Log.Environment.SightFailures++;
            state.Destination=null;state.RetryAt=Time+10;state.BlockedSince=Time;
        }
        if(state.Destination is null && Time-state.BlockedSince>=5 && Time>=state.RetryAt)
        {
            if(Log is not null)Log.Environment.SightAttempts++;
            state.RetryAt=Time+10;
            Vec3d forward=ObstacleMath.Unit(track.EstimatedPosition-ship.Position,Vec3d.From(ship.Forward));
            Vector3 right=forward.ToVector3().Cross(Math.Abs(forward.Y)>.9?Vector3.Right:Vector3.Up).Normalized();
            Vector3 up=right.Cross(forward.ToVector3()).Normalized();
            double best=double.PositiveInfinity,radius=NavigationRadius(ship.Class.Kind),range=(track.EstimatedPosition-ship.Position).Length();
            Vec3d? chosen=null;
            foreach(double distance in new[]{1000.0,2000,4000})
            for(int direction=0;direction<8;direction++)
            {
                double angle=direction*Math.Tau/8;
                Vec3d candidate=ship.Position+Vec3d.From(right*(float)(Math.Cos(angle)*distance)+up*(float)(Math.Sin(angle)*distance));
                if(!SightWithinOrder(brain,candidate) || Obstacles.ClosestApproach(candidate,radius,out _)
                    || Obstacles.BlocksSegment(candidate,track.EstimatedPosition))continue;
                double score=distance+Math.Abs((track.EstimatedPosition-candidate).Length()-range)*.25;
                if(score>=best || !Navigation.FindPath(ship.Position,candidate,radius,out var path))continue;
                // A nearby point that needs a long detour is not a short sidestep.
                score+=Math.Max(0,path.Length-distance)*.5;if(score>=best)continue;
                chosen=candidate;best=score;
            }
            if(chosen is { } point)
            {state.Destination=point;state.Started=Time;state.Completed=false;state.TargetAt=track.EstimatedPosition;
                state.OrderPoint=brain.Order.Point;state.OrderKind=brain.Order.Kind;state.OrderTarget=brain.Order.Target;
                if(Log is not null)Log.Environment.SightStarts++;}
            else if(Log is not null)Log.Environment.SightFailures++;
        }
        if(state.Destination is not { } goal)return desired;
        brain.NavigationGoal=goal;
        brain.WantBoost=false;brain.Activity="사선 확보";
        return ((goal-ship.Position).ToVector3()*.12f).LimitLength(ship.Class.MaxSpeed*.6f);
    }
}
