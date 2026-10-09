using System;
using Godot;

namespace SpaceFleet.Sim;

/// <summary>Shared local-space ellipsoid for weapon interception and the invisible shield skin.</summary>
public sealed class ShieldEnvelope
{
    public Vector3 Center { get; }
    public Vector3 Radii { get; }
    public float BoundingRadius => Center.Length() + Mathf.Max(Radii.X, Mathf.Max(Radii.Y, Radii.Z));
    public ShieldEnvelope(HullSection[] sections, float length)
    {
        Vector3 lo=sections[0].Center-sections[0].HalfSize, hi=sections[0].Center+sections[0].HalfSize;
        foreach(var s in sections) { lo=lo.Min(s.Center-s.HalfSize); hi=hi.Max(s.Center+s.HalfSize); }
        Center=(lo+hi)*.5f;
        Vector3 half=(hi-lo)*.5f+Vector3.One*Mathf.Max(.5f,length*.008f);
        float scale=1;
        foreach(var s in sections)
        for(int corner=0;corner<8;corner++)
        {
            Vector3 p=s.Center+s.HalfSize*new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1);
            scale=Mathf.Max(scale,((p-Center)/half).Length());
        }
        Radii=half*(scale*1.025f);
    }
    public bool Entry(Vector3 origin,Vector3 direction,float range,out float distance)
    {
        // Dot products must also be double: float b*b-a*c loses the tiny hull-sized
        // difference between long, oblique rays (especially against interceptors).
        double ox=((double)origin.X-Center.X)/Radii.X, oy=((double)origin.Y-Center.Y)/Radii.Y,
            oz=((double)origin.Z-Center.Z)/Radii.Z;
        double dx=(double)direction.X/Radii.X, dy=(double)direction.Y/Radii.Y, dz=(double)direction.Z/Radii.Z;
        double c=ox*ox+oy*oy+oz*oz-1, a=dx*dx+dy*dy+dz*dz, b=ox*dx+oy*dy+oz*dz;
        distance=0;
        // Outgoing fire and attacks already inside the field do not hit its inner face.
        if(c< -1e-5 || a<1e-16 || b>=0) return false;
        double discriminant=b*b-a*c;
        if(discriminant<0) return false;
        double t=(-b-Math.Sqrt(discriminant))/a;
        if(t<-.01 || t>range) return false;
        distance=(float)Math.Max(0,t); return true;
    }
    public Vector3 Normal(Vector3 point) => ((point-Center)/(Radii*Radii)).Normalized();
}
