using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

public sealed partial class SimWorld
{
    public TerrainNavigation? Navigation { get; private set; }
    public double NavigationBuildMilliseconds { get; private set; }
    private readonly double[] _navigationRadii=new double[3];
    private sealed class RouteState
    {
        public TerrainPath? Path;
        public TerrainExposure? Exposure;
        public Vec3d Goal,ProgressPosition;
        public int Waypoint;
        public double RetryAt,ProgressAt;
    }
    private readonly Dictionary<ShipBrain,RouteState> _routes=new();
    public double NavigationRadius(HullKind kind)=>_navigationRadii[(int)kind];
    internal void PrepareNavigation(BattleConfig config)
    {
        if(Obstacles.Count==0)return;
        long start=Stopwatch.GetTimestamp();
        Vec3d center=(Vec3d.From(config.RedOffset)+new Vec3d(0,0,-config.StartDistance))*.5;
        Navigation=new(Obstacles,center,new(90000,40000,Math.Max(90000,config.StartDistance*.5+15000)));
        foreach(var kind in Enum.GetValues<HullKind>())
        {
            double radius=_ships.Where(s=>s.Class.Kind==kind).Select(s=>(double)s.Hull.BoundingRadius).DefaultIfEmpty(kind==HullKind.Battleship?700:kind==HullKind.Escort?180:40).Max();
            _navigationRadii[(int)kind]=radius+(kind==HullKind.Battleship?180:kind==HullKind.Escort?100:40);
            Navigation.Prepare(_navigationRadii[(int)kind]);
        }
        NavigationBuildMilliseconds+=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if(Log is not null)Log.Environment.NavigationBuildMs=NavigationBuildMilliseconds;
    }
    private bool NavigationConnected(BattleConfig config)
    {
        if(Navigation is null)return false;
        Vec3d back=Vec3d.From(config.RedOffset)+new Vec3d(0,0,-config.StartDistance),center=back*.5;
        var destinations=new List<Vec3d>{center};
        foreach(var zone in BattleRules.DefaultZones(config.StartDistance,Vec3d.From(config.RedOffset)))
            destinations.Add(zone.Position);
        foreach(Vec3d from in new[]{Vec3d.Zero,back})
        foreach(Vec3d point in destinations)
        {
            // The structure's centre is solid. Connect to its 4 km holding shell.
            Vec3d goal=point==center?point:point+ObstacleMath.Unit(from-point,new(0,0,1))*HoldClearance;
            if(!Navigation.FindPath(from,goal,_navigationRadii[0],out _))return false;
        }
        return true;
    }
    private Vec3d TerrainFormationSlot(ShipBrain brain,ShipBody leader)
    {
        double radius=NavigationRadius(brain.Ship.Class.Kind);
        var normal=leader.Position+Vec3d.From(leader.Orientation*brain.FormationOffset);
        bool narrow=BrainOf(leader)?.NavigationActive==true || Obstacles.ClosestApproach(leader.Position,brain.FormationOffset.Length()+radius+1500,out _)
            || Obstacles.BlocksSegment(leader.Position,normal,radius);
        float before=brain.FormationCompression;
        brain.FormationCompression=Mathf.MoveToward(before,narrow?.12f:1f,.12f);
        if(before==1 && narrow && Log is not null)Log.Environment.FormationCompressions++;
        Vec3d direction=BrainOf(leader)?.NavigationWaypoint is { } waypoint?waypoint-leader.Position:Vec3d.From(leader.Velocity);
        direction=ObstacleMath.Unit(direction,Vec3d.From(leader.Forward));
        int slot=1;if(brain.Ship.Squadron is { } squad)slot=1+squad.Members.TakeWhile(s=>s!=brain.Ship).Count();
        double trail=(leader.Hull.BoundingRadius+brain.Ship.Hull.BoundingRadius+500)*slot;
        Vec3d column=leader.Position-direction*trail+Vec3d.From(leader.Orientation*new Vector3(brain.FormationOffset.X*.12f,brain.FormationOffset.Y*.12f,0));
        Vec3d desired=Vec3d.Lerp(column,normal,(brain.FormationCompression-.12f)/.88f);
        return Navigation?.NearestFree(desired,radius)??desired;
    }
    private Vector3 NavigateTerrain(ShipBrain brain,Vector3 desired)
    {
        var ship=brain.Ship;brain.NavigationActive=false;brain.NavigationWaypoint=null;
        if(Navigation is null || brain.NavigationGoal is not { } goal)return desired;
        double radius=NavigationRadius(ship.Class.Kind);
        Vec3d requested=goal;goal=Navigation.NearestFree(goal,radius);
        if((requested-goal).LengthSquared()>1)
            desired=((goal-ship.Position).ToVector3()*.05f).LimitLength(ship.Class.MaxSpeed);
        if(!Obstacles.BlocksSegment(ship.Position,goal,radius))
        {_routes.Remove(brain);return desired;}
        if(!_routes.TryGetValue(brain,out var state))
        {_routes[brain]=state=new(){Goal=goal,ProgressPosition=ship.Position,ProgressAt=Time};}
        if((ship.Position-state.ProgressPosition).Length()>500)
        {state.ProgressPosition=ship.Position;state.ProgressAt=Time;}
        bool stuck=Time-state.ProgressAt>=12;
        bool changed=(state.Goal-goal).Length()>5000;
        if(Time>=state.RetryAt && (state.Path is null || changed || stuck))
        {
            long start=Stopwatch.GetTimestamp();
            state.Exposure=CoverCostEnabled && CoverMission(ship)?TerrainExposure.Observe(this,ship.Faction):null;
            bool found=Navigation.FindPath(ship.Position,goal,radius,out var path,maxExpanded:state.Exposure is null?30000:1024,exposure:state.Exposure);
            int expanded=path.Expanded;
            if(!found && state.Exposure is not null)
            {
                if(Log is not null)Log.Environment.CoverBudgetFallbacks++;
                state.Exposure=null;found=Navigation.FindPath(ship.Position,goal,radius,out path);expanded+=path.Expanded;
            }
            if(Log is not null)
            {
                var metrics=Log.Environment;metrics.PathPlans++;metrics.PathExpanded+=expanded;
                metrics.PathMilliseconds+=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if(!found)metrics.PathFailures++;if(stuck)metrics.StuckReplans++;
                if(found && CoverMission(ship) && ship.Squadron?.Mission==InterceptorMission.Assault)
                {
                    var sampled=state.Exposure??TerrainExposure.Observe(this,ship.Faction);
                    Vec3d previous=ship.Position;
                    foreach(var point in path.Points)
                    {
                        double length=(point-previous).Length();
                        metrics.CoverPathLength+=length;
                        metrics.CoverPathExposure+=length*sampled.At(Vec3d.Lerp(previous,point,.5));previous=point;
                    }
                }
            }
            state.Path=found?path:null;state.Waypoint=0;state.Goal=goal;state.RetryAt=Time+5;
            state.ProgressPosition=ship.Position;state.ProgressAt=Time;
        }
        if(state.Path is not { Points.Count:>0 } route)
        {
            // Local avoidance can push out of an inflated safety margin. Do not
            // accelerate into a blocked destination while a route is unavailable.
            Vec3d escape=Navigation.NearestFree(ship.Position,radius)-ship.Position;
            return (escape.ToVector3()*.4f).LimitLength(ship.Class.MaxSpeed*.35f);
        }
        for(int i=route.Points.Count-1;i>state.Waypoint;i--)
            if(!Obstacles.BlocksSegment(ship.Position,route.Points[i],radius) && (state.Exposure is null
                || state.Exposure.Cost(ship.Position,route.Points[i])<=state.Exposure.Cost(ship.Position,route.Points[state.Waypoint])
                    +Enumerable.Range(state.Waypoint,i-state.Waypoint).Sum(j=>state.Exposure.Cost(route.Points[j],route.Points[j+1]))))
            {state.Waypoint=i;break;}
        Vec3d next=route.Points[state.Waypoint];
        if((next-ship.Position).Length()<350 && state.Waypoint<route.Points.Count-1)next=route.Points[++state.Waypoint];
        Vec3d offset=next-ship.Position;double distance=offset.Length();
        brain.NavigationActive=true;brain.NavigationWaypoint=next;
        float speed=Mathf.Min(Mathf.Max(desired.Length(),ship.Class.MaxSpeed*.4f),ship.Class.MaxSpeed);
        // Arrive slowly enough to turn the corner using the existing acceleration.
        speed=Mathf.Min(speed,(float)Math.Sqrt(2*Math.Max(1,ship.Class.StrafeAccel)*Math.Max(30,distance-150)));
        if(speed<ship.Class.MaxSpeed)brain.WantBoost=false;
        return ObstacleMath.Unit(offset,Vec3d.From(ship.Forward)).ToVector3()*speed;
    }
}
