using System;
using Godot;
namespace SpaceFleet.Sim;
public sealed partial class SimWorld {
internal Vector3 ObstacleAvoidanceCurrent(ShipBody ship,Vector3 desired)=>ObstacleAvoidance(ship,desired);
    internal Vector3 ObstacleAvoidanceBaseline(ShipBody ship,Vector3 desired)
    {
        Vector3 push=Vector3.Zero;
        foreach(var obstacle in Obstacles)
        {
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
        return push;
    }

}
