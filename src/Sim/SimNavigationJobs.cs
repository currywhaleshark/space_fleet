using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SpaceFleet.Sim;

public sealed partial class SimWorld
{
    public const int NavigationWorkBudget=12000;
    private const int NavigationQuantum=1500;
    public int NavigationWorkLastTick { get; private set; }
    public int PendingNavigation { get; private set; }
    private readonly Queue<PathRequest> _navigationQueue=new();
    private sealed class PathRequest
    {
        public required ShipBrain Brain;
        public required RouteState State;
        public required TerrainNavigation Navigation;
        public required TerrainNavigation.Search Search;
        public required ShipOrder Order;
        public required Vec3d Start,Goal,ObservedGoal;
        public required double Radius,RequestedAt;
        public TerrainExposure? Exposure;
        public TerrainExposure? Measurement;
        public int Expanded;
        public double Milliseconds;
        public bool Cancelled;
    }
    private void QueuePath(ShipBrain brain,RouteState state,Vec3d goal,double radius,bool stuck)
    {
        var exposure=CoverCostEnabled && CoverMission(brain.Ship)?PlanningExposure(brain.Ship.Faction):null;
        var measurement=CoverMission(brain.Ship) && brain.Ship.Squadron?.Mission==InterceptorMission.Assault
            ?exposure??PlanningExposure(brain.Ship.Faction):null;
        var request=new PathRequest{Brain=brain,State=state,Navigation=Navigation!,Order=brain.Order,
            Start=brain.Ship.Position,Goal=goal,ObservedGoal=brain.NavigationGoal!.Value,Radius=radius,RequestedAt=Time,Exposure=exposure,Measurement=measurement,
            Search=Navigation!.BeginPath(brain.Ship.Position,goal,radius,exposure is null?30000:1024,exposure,measurement)};
        state.Pending=request;state.Goal=goal;_navigationQueue.Enqueue(request);PendingNavigation++;
        if(Log is not null)
        { Log.Environment.PathQueuePeak=Math.Max(Log.Environment.PathQueuePeak,PendingNavigation);if(stuck)Log.Environment.StuckReplans++; }
    }
    private void CancelRequest(RouteState state)
    {
        if(state.Pending is not { } pending)return;
        pending.Cancelled=true;pending.Search.Dispose();state.Pending=null;PendingNavigation--;
        if(Log is not null)Log.Environment.PathCancelled++;
    }
    private void CancelRoute(ShipBrain brain)
    {
        if(_routes.Remove(brain,out var state))CancelRequest(state);
    }
    private void ResetNavigationJobs()
    {
        foreach(var state in _routes.Values)CancelRequest(state);
        _navigationQueue.Clear();_routes.Clear();NavigationWorkLastTick=0;
    }
    private bool ValidRequest(PathRequest request)
    {
        var brain=request.Brain;var order=brain.Order;
        return !request.Cancelled && ReferenceEquals(request.Navigation,Navigation)
            && ReferenceEquals(BrainOf(brain.Ship),brain) && brain.Enabled && Squadron.Active(brain.Ship)
            && order.Kind==request.Order.Kind && order.Target==request.Order.Target
            && (order.Kind!=OrderKind.Hold || (order.Point-request.Order.Point).LengthSquared()<=5000*5000)
            && brain.NavigationGoal is { } goal && (goal-request.ObservedGoal).LengthSquared()<=5000*5000;
    }
    private void StepNavigationJobs()
    {
        NavigationWorkLastTick=0;if(_navigationQueue.Count==0)return;
        long timing=SimProfiler.Begin(),start=Stopwatch.GetTimestamp();
        while(_navigationQueue.Count>0 && NavigationWorkLastTick<NavigationWorkBudget)
        {
            var request=_navigationQueue.Dequeue();
            if(request.Cancelled)continue;
            if(!ValidRequest(request))
            { CancelRequest(request.State);request.State.RetryAt=0;continue; }
            long slice=Stopwatch.GetTimestamp();
            NavigationWorkLastTick+=request.Search.Step(Math.Min(NavigationQuantum,NavigationWorkBudget-NavigationWorkLastTick));
            request.Milliseconds+=Stopwatch.GetElapsedTime(slice).TotalMilliseconds;
            if(!request.Search.Complete){_navigationQueue.Enqueue(request);continue;}
            request.Expanded+=request.Search.Expanded;
            if(!request.Search.Found && request.Exposure is not null)
            {
                if(Log is not null)Log.Environment.CoverBudgetFallbacks++;
                request.Search.Dispose();request.Exposure=null;
                request.Search=Navigation!.BeginPath(request.Start,request.Goal,request.Radius,measurement:request.Measurement);
                _navigationQueue.Enqueue(request);continue;
            }
            CompletePath(request);
        }
        if(Log is not null)
        {
            var metrics=Log.Environment;
            metrics.PathWorkPeak=Math.Max(metrics.PathWorkPeak,NavigationWorkLastTick);
            metrics.PathSliceMaxMs=Math.Max(metrics.PathSliceMaxMs,Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        SimProfiler.End(SimSection.NavigationPlan,timing);
    }
    private void CompletePath(PathRequest request)
    {
        var state=request.State;var brain=request.Brain;bool found=request.Search.Found;
        var path=request.Search.Path;
        state.Pending=null;PendingNavigation--;request.Search.Dispose();
        state.Path=found?path:null;state.Exposure=request.Exposure;state.Waypoint=0;
        state.RetryAt=Time+5;state.ProgressPosition=brain.Ship.Position;state.ProgressAt=Time;
        // Apply on this tick's normal Think, including collision avoidance and
        // a fresh observed destination check; never write acceleration directly.
        brain.NextThink=Time;
        if(Log is null)return;
        var metrics=Log.Environment;metrics.PathPlans++;metrics.PathExpanded+=request.Expanded;
        metrics.PathMilliseconds+=request.Milliseconds;
        double delay=Time-request.RequestedAt;
        metrics.PathLatencySeconds+=delay;metrics.PathLatencyMaxSeconds=Math.Max(metrics.PathLatencyMaxSeconds,delay);
        if(!found)metrics.PathFailures++;
        metrics.CoverPathLength+=request.Search.CoverLength;metrics.CoverPathExposure+=request.Search.CoverExposure;
    }
}
