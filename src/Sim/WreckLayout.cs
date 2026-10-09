using System;
using System.Collections.Generic;
using Godot;

namespace SpaceFleet.Sim;

/// <summary>Deterministic breakup cells shared by the rendered cuts and approximate debris collision boxes.</summary>
public sealed class WreckLayout
{
    public sealed record Piece(Vector3 Minimum,Vector3 Maximum,Vector3 Center,Vector3 Drift,Vector3 Spin,HullBox[] Boxes)
    {
        public Transform3D Pose(float age)
        {
            var rotation=new Basis(Spin.Normalized(),Spin.Length()*age);
            return new(rotation,Center+Drift*age-rotation*Center);
        }
    }
    public IReadOnlyList<Piece> Pieces { get; }
    public WreckLayout(ShipDefinition def,DestructionKind kind,Vector3 cause)
    {
        var b=def.Hull.Bounds;
        Vector3 min=b.Center-b.HalfSize-Vector3.One*def.Flight.Length*.05f;
        Vector3 max=b.Center+b.HalfSize+Vector3.One*def.Flight.Length*.05f;
        float cut=Mathf.Clamp(cause.Z,b.Center.Z-b.HalfSize.Z*.35f,b.Center.Z+b.HalfSize.Z*.35f);
        bool shatter=kind is DestructionKind.Reactor or DestructionKind.Antimatter;
        var pieces=new List<Piece>();
        for(int i=0;i<(shatter?8:2);i++)
        {
            Vector3 lo=min,hi=max;
            if((i&1)==0) hi.Z=cut; else lo.Z=cut;
            if(shatter) { if((i&2)==0) hi.X=b.Center.X; else lo.X=b.Center.X; if((i&4)==0) hi.Y=b.Center.Y; else lo.Y=b.Center.Y; }
            var boxes=new List<HullBox>();
            foreach(var box in def.Hull.Boxes)
            {
                Vector3 low=(box.Center-box.HalfSize).Max(lo), high=(box.Center+box.HalfSize).Min(hi);
                if(high.X-low.X>.001 && high.Y-low.Y>.001 && high.Z-low.Z>.001) boxes.Add(new((low+high)*.5f,(high-low)*.5f));
            }
            if(boxes.Count==0) continue;
            Vector3 center=(lo+hi)*.5f;
            Vector3 outward=shatter?(center-b.Center).Normalized():new Vector3((i==0?-.15f:.15f),i==0?.1f:-.1f,i==0?-1:1).Normalized();
            float speed=Mathf.Clamp(def.Flight.Length*.028f,2,32)*(shatter?1.4f:1);
            Vector3 spin=new Vector3(.3f*(i%2==0?1:-1),.5f,.2f).Normalized()*Mathf.Clamp(1.2f/Mathf.Sqrt(def.Flight.Length),.025f,.2f);
            pieces.Add(new(lo,hi,center,outward*speed,spin,boxes.ToArray()));
        }
        Pieces=pieces;
    }
    public CollisionHull Hull(float age)
    {
        var boxes=new List<HullBox>();
        foreach(var piece in Pieces)
        {
            Transform3D pose=piece.Pose(age);
            foreach(var box in piece.Boxes)
                boxes.Add(new(pose*box.Center,pose.Basis.X.Abs()*box.HalfSize.X+pose.Basis.Y.Abs()*box.HalfSize.Y+pose.Basis.Z.Abs()*box.HalfSize.Z));
        }
        return new CollisionHull(boxes.ToArray());
    }
}
