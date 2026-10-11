using System;
using System.Collections.Generic;
using Godot;
namespace SpaceFleet.Sim;
// Frozen phase-one reference. Test assembly only.
static class ObstacleLinearReference {
    public static bool SegmentBlocked(ObstacleSet set,Vec3d a, Vec3d b, out ObstacleHit hit, double padding = 0)
    {
        hit = default;
        if (set.Count == 0) return false;
        double nearest = double.PositiveInfinity;
        foreach (var obstacle in set)
        {
            if (!ObstacleMath.SegmentSphere(a - obstacle.Position, b - obstacle.Position, Vec3d.Zero,
                obstacle.BoundingRadius + padding, out _)) continue;
            Vec3d start = obstacle.ToLocal(a), end = obstacle.ToLocal(b);
            for (int i = 0; i < obstacle.Parts.Count; i++)
            {
                var part = obstacle.Parts[i];
                if (!ObstacleMath.SegmentCapsule(start, end, part.A, part.B, part.Radius + padding, out double t) || t >= nearest) continue;
                nearest = t;
                Vec3d point = Vec3d.Lerp(start, end, t), axis = ObstacleMath.OnSegment(point, part.A, part.B);
                hit = new(obstacle, i, Vec3d.Lerp(a, b, t), obstacle.Rotation.Apply(ObstacleMath.Unit(point - axis, start - end)), t);
            }
        }
        return double.IsFinite(nearest);
    }

    /// <summary>Nearest surface within radius; negative clearance means the query sphere penetrates it.</summary>
    public static bool ClosestApproach(ObstacleSet set,Vec3d point, double radius, out ObstacleProximity hit)
    {
        hit = default;
        if (set.Count == 0) return false;
        double nearest = double.PositiveInfinity;
        foreach (var obstacle in set)
        {
            if ((point - obstacle.Position).Length() > obstacle.BoundingRadius + radius) continue;
            Vec3d local = obstacle.ToLocal(point);
            for (int i = 0; i < obstacle.Parts.Count; i++)
            {
                var part = obstacle.Parts[i];
                Vec3d axis = ObstacleMath.OnSegment(local, part.A, part.B);
                double clearance = (local - axis).Length() - part.Radius - radius;
                if (clearance > 0 || clearance >= nearest) continue;
                nearest = clearance;
                Vec3d normal = ObstacleMath.Unit(local - axis, new Vec3d(1, 0, 0));
                hit = new(obstacle, i, obstacle.ToWorld(axis + normal * part.Radius), obstacle.Rotation.Apply(normal), clearance);
            }
        }
        return double.IsFinite(nearest);
    }


}
public static partial class ShipCollision {
    public static void ResolveObstaclesLinear(IReadOnlyList<ShipBody> ships, ObstacleSet obstacles, double time, BattleLog? log = null)
    {
        if (obstacles.Count == 0) return;
        foreach (var ship in ships)
        {
            // Preserve primitive and hull-box ordering for simultaneous contacts.
            double earliest = double.PositiveInfinity; Obstacle? first = null; ObstacleBoxContact contact = default;
            foreach (var obstacle in obstacles)
            {
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
                foreach(var obstacle in obstacles)
                {
                    if((ship.Position-obstacle.Position).Length()>obstacle.BoundingRadius+ship.Hull.BoundingRadius+Skin) continue;
                    ObstacleBoxContact deepest=default; bool found=false;
                    for(int p=0;p<obstacle.Parts.Count;p++) foreach(var box in ship.Hull.Boxes)
                        if(ObstacleSet.BoxContact(obstacle,p,ship.Position,ship.Orientation,box,out var hit)
                            && (!found || hit.Penetration>deepest.Penetration)) { found=true; deepest=hit; }
                    if(!found) continue;
                    ApplyObstacle(ship,obstacle,deepest,deepest.Penetration+Skin,time,log); corrected=true;
                }
                if(!corrected) break;
            }
        }
    }


}
