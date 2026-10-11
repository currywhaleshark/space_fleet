using System;
using System.Collections.Generic;
using Godot;

namespace SpaceFleet.Sim;

public sealed partial class SimWorld
{
    private sealed record ObstacleProgress(Obstacle Obstacle,Vec3d Position,Vector3 Direction,double Since,bool Reported=false);
    private readonly Dictionary<ShipBody,ObstacleProgress> _obstacleProgress=new();
    private Vector3 ObstacleAvoidance(ShipBody ship,Vector3 desired)
    {
        long timing=SimProfiler.Begin();
        Vector3 push=Vector3.Zero;
        Span<int> candidates=Obstacles.Count<=1024?stackalloc int[Obstacles.Count]:new int[Obstacles.Count];
        double broad=Obstacles.MaximumRadius+ship.Hull.BoundingRadius;
        double padding=ship.Hull.BoundingRadius+(Doctrine is null?Math.Max(150,broad*.3):Math.Max(300,broad*.5))+desired.Length()*10;
        int count=Obstacles.QueryCandidates(ship.Position,ship.Position,padding,candidates);
        for(int candidate=0;candidate<count;candidate++)
        {
            var obstacle=Obstacles[candidates[candidate]];
            double hull=ship.Hull.BoundingRadius;
            double bound=obstacle.BoundingRadius+hull;
            bound+=Doctrine is null?Math.Max(150,bound*.3):Math.Max(300,bound*.5);
            if((ship.Position-obstacle.Position).Length()>bound+desired.Length()*10) continue;
            Vec3d start=obstacle.ToLocal(ship.Position),travel=obstacle.Rotation.Inverse(Vec3d.From(desired))*10;
            Vector3 bestPush=Vector3.Zero; double worst=0;
            foreach(var part in obstacle.Parts)
            {
                double safe=hull+part.Radius;
                safe+=Doctrine is null?Math.Max(150,safe*.3):Math.Max(300,safe*.5);
                SegmentPair(start,start+travel,part.A,part.B,out double t,out var moving,out var axis);
                double miss=(moving-axis).Length(); if(miss>=safe) continue;
                double urgency=(safe-miss)/Math.Max(t*10,1); if(urgency<=worst) continue;
                worst=urgency;
                Vector3 away=obstacle.Rotation.Apply(ObstacleMath.Unit(moving-axis,start-axis)).ToVector3();
                Vector3 direction=desired.LengthSquared()>1?desired.Normalized():ship.Forward;
                if(Mathf.Abs(away.Dot(direction))>.85f || miss<1)
                {
                    Vector3 side=direction.Cross(Mathf.Abs(direction.Y)>.9f?Vector3.Right:Vector3.Up).Normalized();
                    away=(side+away*.25f).Normalized();
                }
                bestPush=away*(float)Math.Min(urgency*2,ship.Class.MaxSpeed);
            }
            push+=bestPush;
        }
        RecordObstacleProgress(ship,desired);
        SimProfiler.End(SimSection.TerrainAvoidance,timing);
        return push;
    }
    private void RecordObstacleProgress(ShipBody ship,Vector3 desired)
    {
        if(desired.LengthSquared()<4 || !Obstacles.ClosestApproach(ship.Position,ship.Hull.BoundingRadius+700,out var near))
        { _obstacleProgress.Remove(ship); return; }
        Vector3 direction=desired.Normalized();
        if(!_obstacleProgress.TryGetValue(ship,out var progress) || progress.Obstacle!=near.Obstacle
            || direction.Dot(progress.Direction)<.5f || (ship.Position-progress.Position).Dot(Vec3d.From(progress.Direction))>Math.Max(25,ship.Hull.BoundingRadius*.05))
        { _obstacleProgress[ship]=new(near.Obstacle,ship.Position,direction,Time); return; }
        if(!progress.Reported && Time-progress.Since>=30)
        { Log?.Environment.Stuck(ship,near.Obstacle,Time); _obstacleProgress[ship]=progress with { Reported=true }; }
    }
    internal static void SegmentPair(Vec3d a,Vec3d b,Vec3d c,Vec3d d,out double t,out Vec3d first,out Vec3d second)
    {
        Vec3d u=b-a,v=d-c,w=a-c; double uu=u.Dot(u),uv=u.Dot(v),vv=v.Dot(v),uw=u.Dot(w),vw=v.Dot(w);
        if(uu<1e-20) { t=0; first=a; second=ObstacleMath.OnSegment(a,c,d); return; }
        double denominator=uu*vv-uv*uv;
        t=denominator>1e-20?Math.Clamp((uv*vw-vv*uw)/denominator,0,1):0;
        double s=vv>1e-20?(uv*t+vw)/vv:0;
        if(s<0) { s=0; t=Math.Clamp(-uw/uu,0,1); }
        else if(s>1) { s=1; t=Math.Clamp((uv-uw)/uu,0,1); }
        else if(vv<1e-20) t=Math.Clamp(-uw/uu,0,1);
        first=a+u*t; second=c+v*s;
    }
}
