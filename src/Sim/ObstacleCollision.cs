using System;
using System.Collections.Generic;
using Godot;

namespace SpaceFleet.Sim;

public static partial class ShipCollision
{
    public static void ResolveObstacles(IReadOnlyList<ShipBody> ships, ObstacleSet obstacles, double time, BattleLog? log = null)
    {
        if (obstacles.Count == 0) return;
        long terrainTiming=SimProfiler.Begin();
        Span<int> candidates=obstacles.Count<=1024?stackalloc int[obstacles.Count]:new int[obstacles.Count];
        foreach (var ship in ships)
        {
            // Preserve primitive and hull-box ordering for simultaneous contacts.
            double earliest = double.PositiveInfinity; Obstacle? first = null; ObstacleBoxContact contact = default;
            int count=obstacles.QueryCandidates(ship.PrevPosition,ship.Position,ship.Hull.BoundingRadius+Skin,candidates);
            for(int candidate=0;candidate<count;candidate++)
            {
                var obstacle=obstacles[candidates[candidate]];
                if (!ObstacleMath.SegmentSphere(ship.PrevPosition-obstacle.Position,ship.Position-obstacle.Position,
                    Vec3d.Zero,obstacle.BoundingRadius+ship.Hull.BoundingRadius+Skin,out _)) continue;
                for(int p=0;p<obstacle.Parts.Count;p++) foreach(var box in ship.Hull.Boxes)
                    if (SweepObstacleBox(obstacle,p,ship.PrevPosition,ship.Position,ship.Orientation,box,out double t,out var hit) && t<earliest)
                    { earliest=t; first=obstacle; contact=hit; }
            }
            if(first is not null)
            {
                Vec3d remaining=(ship.Position-ship.PrevPosition)*(1-earliest);
                double correction=contact.Penetration + Math.Max(0,-remaining.Dot(contact.Normal)) + Skin;
                ApplyObstacle(ship,first,contact,correction,time,log,Vec3d.Lerp(ship.PrevPosition,ship.Position,earliest));
            }
            // Resolve intersecting primitives and initially embedded ships without inventing velocity.
            for(int pass=0;pass<OverlapPasses;pass++)
            {
                bool corrected=false;
                count=obstacles.QueryCandidates(ship.Position,ship.Position,ship.Hull.BoundingRadius+Skin,candidates);
                int nextIndex=0;
                for(int candidate=0;candidate<count;candidate++)
                {
                    int index=candidates[candidate];if(index<nextIndex)continue;
                    var obstacle=obstacles[index];
                    if((ship.Position-obstacle.Position).Length()>obstacle.BoundingRadius+ship.Hull.BoundingRadius+Skin) continue;
                    ObstacleBoxContact deepest=default; bool found=false;
                    for(int p=0;p<obstacle.Parts.Count;p++) foreach(var box in ship.Hull.Boxes)
                        if(ObstacleSet.BoxContact(obstacle,p,ship.Position,ship.Orientation,box,out var hit)
                            && (!found || hit.Penetration>deepest.Penetration)) { found=true; deepest=hit; }
                    if(!found) continue;
                    ApplyObstacle(ship,obstacle,deepest,deepest.Penetration+Skin,time,log); corrected=true;
                    // A correction can move into a different leaf during this same
                    // pass. Refresh candidates, but never revisit an earlier index.
                    nextIndex=index+1;
                    count=obstacles.QueryCandidates(ship.Position,ship.Position,ship.Hull.BoundingRadius+Skin,candidates);
                    candidate=-1;
                }
                if(!corrected) break;
            }
        }
        SimProfiler.End(SimSection.TerrainCollision,terrainTiming);
    }

    /// <summary>Continuous translation of the current OBB pose, matching the existing 240 Hz rotation approximation.</summary>
    public static bool SweepObstacleBox(Obstacle obstacle,int partIndex,Vec3d start,Vec3d end,Quaternion orientation,
        HullBox box,out double fraction,out ObstacleBoxContact contact)
    {
        fraction=0; contact=default;
        var part=obstacle.Parts[partIndex]; var rotation=new DoubleRotation(orientation);
        Vec3d center=rotation.Apply(Vec3d.From(box.Center)), relative=obstacle.Position-start;
        Vec3d wa=obstacle.Rotation.Apply(part.A),wb=obstacle.Rotation.Apply(part.B);
        if(!ObstacleMath.SegmentCapsule(center-relative,end-start+center-relative,wa,wb,part.Radius+box.HalfSize.Length()+Skin,out _)) return false;
        Vec3d a=rotation.Inverse(relative+wa)-Vec3d.From(box.Center),b=rotation.Inverse(relative+wb)-Vec3d.From(box.Center),
            travel=rotation.Inverse(end-start),half=Vec3d.From(box.HalfSize);
        double radius=part.Radius, t=0;
        Vec3d axisPoint=default,boxPoint=default; double distance=0;
        double Distance(double at) => ObstacleMath.SegmentBoxDistance(a-travel*at,b-travel*at,half,out _,out _);
        for(int i=0;i<64;i++)
        {
            distance=ObstacleMath.SegmentBoxDistance(a-travel*t,b-travel*t,half,out axisPoint,out boxPoint);
            double gap=distance-radius;
            if(gap<=.0005) break;
            double closing=travel.Dot(axisPoint-boxPoint)/Math.Max(distance,1e-15);
            if(closing<=1e-12) return false;
            double next=t+gap/closing;
            if(next>1) return false;
            t=next;
            if(i!=63) continue;
            // Rare nearly tangent convergence: convex distance has one minimum.
            double lo=t,hi=1;
            for(int j=0;j<64;j++) { double x=(2*lo+hi)/3,y=(lo+2*hi)/3; if(Distance(x)<Distance(y)) hi=y; else lo=x; }
            double minimum=(lo+hi)*.5;
            if(Distance(minimum)>radius+.0005) return false;
            lo=t; hi=minimum;
            for(int j=0;j<64;j++) { double middle=(lo+hi)*.5; if(Distance(middle)>radius+.0005) lo=middle; else hi=middle; }
            t=hi; distance=ObstacleMath.SegmentBoxDistance(a-travel*t,b-travel*t,half,out axisPoint,out boxPoint);
        }
        fraction=t;
        if(distance<=1e-10)
            return ObstacleSet.BoxContact(obstacle,partIndex,Vec3d.Lerp(start,end,t),orientation,box,out contact);
        contact=new(Vec3d.Lerp(start,end,t)+rotation.Apply(boxPoint+Vec3d.From(box.Center)),
            rotation.Apply((boxPoint-axisPoint)*(1/distance)),Math.Max(0,radius-distance));
        return true;
    }

    private static void ApplyObstacle(ShipBody ship,Obstacle obstacle,ObstacleBoxContact contact,double correction,double time,BattleLog? log,Vec3d? contactOrigin=null)
    {
        Vector3 normal=contact.Normal.ToVector3(),arm=(contact.Position-(contactOrigin??ship.Position)).ToVector3();
        ship.Position+=contact.Normal*correction;
        Vector3 surface=ship.Velocity+(ship.Orientation*ship.AngularVelocity).Cross(arm);
        float closing=-surface.Dot(normal);
        if(closing<=0) return;
        Vector3 before=ship.Velocity; float restitution=closing>2?Restitution:0;
        ship.Velocity+=normal*((1+restitution)*closing);
        Vector3 tangent=surface-normal*surface.Dot(normal); float speed=tangent.Length();
        if(speed>1e-6f) ship.Velocity-=tangent/speed*Mathf.Min(Friction*(1+restitution)*closing,speed);
        if(closing<=2) return;
        ship.LastCollision=new(obstacle.Name,time,closing,ship.Velocity.DistanceTo(before));
        double energy=.5*ship.Class.MassKg*closing*closing*(1-restitution*restitution);
        double beforeHealth=0; foreach(var module in ship.Damage.Modules) beforeHealth+=module.Health;
        CollisionDamage.Apply(ship,obstacle.Name,arm,normal,energy,time);
        double afterHealth=0; foreach(var module in ship.Damage.Modules) afterHealth+=module.Health;
        log?.Environment.Collision(ship,obstacle,time,energy,Math.Max(0,beforeHealth-afterHealth));
    }
}
