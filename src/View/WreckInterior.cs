using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.View;

public partial class WreckView
{
    private readonly List<StandardMaterial3D> _interiorHeat=new();

    // Cap each connected visual component, not the gameplay bounding box. This keeps
    // open trusses and the new narrow Mars core open instead of filling their gaps.
    private void BuildInterior(Node3D root,List<Source> sources,WreckLayout.Piece part,float length)
    {
        var skin=new Surface(); var steel=new Surface(); var machinery=new Surface(); var hot=new Surface();
        int detailed=0;
        foreach(var source in sources)
        foreach(var chunk in source.Chunks.OrderByDescending(c=>(c.Max-c.Min).LengthSquared()))
        {
            if(source.Detached.Contains(chunk.Id) || chunk.Max.X<part.Minimum.X || chunk.Min.X>part.Maximum.X
                || chunk.Max.Y<part.Minimum.Y || chunk.Min.Y>part.Maximum.Y || chunk.Max.Z<part.Minimum.Z || chunk.Min.Z>part.Maximum.Z) continue;
            for(int axis=0;axis<3;axis++)
            for(int side=0;side<2;side++)
            {
                float plane=side==0?part.Minimum[axis]:part.Maximum[axis];
                float epsilon=Mathf.Max(.00001f,length*.0000001f);
                if(chunk.Min[axis]>=plane-epsilon || chunk.Max[axis]<=plane+epsilon) continue;
                int u=(axis+1)%3,v=(axis+2)%3;
                var crossings=new List<Vector2>();
                foreach(int index in chunk.Triangles)
                {
                    var t=source.Triangles[index];
                    Edge(t.A.P,t.B.P); Edge(t.B.P,t.C.P); Edge(t.C.P,t.A.P);
                }
                void Edge(Vector3 a,Vector3 b)
                {
                    float da=a[axis]-plane,db=b[axis]-plane;
                    if((da<0)==(db<0) || Mathf.Abs(da-db)<epsilon) return;
                    Vector3 p=a.Lerp(b,da/(da-db)); crossings.Add(new(p[u],p[v]));
                }
                if(crossings.Count<3) continue;
                var outline=Geometry2D.ConvexHull(crossings.ToArray());
                if(outline.Length<4) continue; // Godot repeats the closing vertex.
                var polygon=new List<Vector3>();
                foreach(var p in outline.Take(outline.Length-1)) { Vector3 at=Vector3.Zero; at[axis]=plane; at[u]=p.X; at[v]=p.Y; polygon.Add(at); }
                polygon=ClipPolygon(polygon,part.Minimum,part.Maximum);
                if(polygon.Count<3) continue;
                Vector3 center=Vector3.Zero; foreach(var p in polygon) center+=p; center/=polygon.Count;
                Vector3 normal=Vector3.Zero; normal[axis]=side==0?-1:1;
                float area=0;
                for(int i=0;i<polygon.Count;i++) area+=(polygon[i]-center).Cross(polygon[(i+1)%polygon.Count]-center).Length()*.5f;
                if(area<epsilon*epsilon) continue;
                // Slight recess leaves the clipped authored rim readable.
                float rib=Mathf.Clamp(Mathf.Sqrt(area)*.026f,length*.00015f,length*.003f);
                var back=polygon.Select(p=>p-normal*rib*.45f).ToList();
                Fan(skin,back,normal);
                bool detail=area>length*length*.00012f && detailed<8;
                if(!detail) continue;
                detailed++; InteriorSectionCount++;
                // Uneven short struts and exposed bright edges interrupt the clean cut.
                for(int i=0;i<polygon.Count;i++)
                {
                    var a=polygon[i]; var b=polygon[(i+1)%polygon.Count];
                    Beam(steel,a,b,rib,normal);
                    if(i%2==0) Beam(steel,a.Lerp(b,.3f),a.Lerp(b,.3f)+normal*rib*(1.5f+i%3),rib*.6f,Vector3.Up);
                }
                Vector2[] poly=polygon.Select(p=>new Vector2(p[u],p[v])).ToArray();
                Vector2 lo=poly.Aggregate(new Vector2(float.PositiveInfinity,float.PositiveInfinity),(a,b)=>a.Min(b));
                Vector2 hi=poly.Aggregate(new Vector2(float.NegativeInfinity,float.NegativeInfinity),(a,b)=>a.Max(b));
                Vector2 span=hi-lo;
                // Cabinets/cell banks are decorative exposed systems, bounded by the
                // actual cut outline; they do not create new gameplay modules.
                for(int row=0;row<3;row++)
                for(int col=0;col<4;col++)
                {
                    Vector2 p=lo+span*new Vector2((col+.5f)/4,(row+.5f)/3);
                    Vector2 half=span*new Vector2(.075f,.10f);
                    if(!Inside(p,half,poly)) continue;
                    Vector3 at=center; at[u]=p.X; at[v]=p.Y; at-=normal*rib*.8f;
                    Vector3 size=Vector3.Zero; size[u]=half.X*2; size[v]=half.Y*2; size[axis]=rib*1.8f;
                    Box(machinery,at,size,Basis.Identity);
                    Vector3 beamA=at,beamB=at; beamA[u]-=half.X*.78f; beamB[u]+=half.X*.78f;
                    beamA+=normal*rib*1.1f; beamB+=normal*rib*1.1f;
                    Beam((row+col)%3==0?hot:steel,beamA,beamB,rib*.6f,normal);
                    if(col%2==0)
                    {
                        Vector3 cable=at+normal*rib*1.7f;
                        Vector3 end=cable; end[v]-=half.Y*.8f; end+=normal*rib*2;
                        Beam(hot,cable,end,rib*.22f,normal);
                    }
                }
                // Structural ribs remain visible between equipment rows.
                for(int i=1;i<3;i++)
                {
                    Vector2 a=new(lo.X+span.X*.12f,lo.Y+span.Y*i/3),b=new(hi.X-span.X*.12f,a.Y);
                    if(!Geometry2D.IsPointInPolygon(a,poly)||!Geometry2D.IsPointInPolygon(b,poly)) continue;
                    Vector3 x=center,y=center; x[u]=a.X; x[v]=a.Y; y[u]=b.X; y[v]=b.Y;
                    Beam(steel,x+normal*rib*.5f,y+normal*rib*.5f,rib*.85f,normal);
                }
            }
        }
        var heat=new StandardMaterial3D { AlbedoColor=new(.18f,.065f,.024f),Metallic=.5f,Roughness=.85f,
            EmissionEnabled=true,Emission=new(1f,.20f,.025f),EmissionEnergyMultiplier=1.8f };
        var materials=new Material[] {
            new StandardMaterial3D { AlbedoColor=new(.027f,.032f,.038f),Metallic=.45f,Roughness=.98f },
            new StandardMaterial3D { AlbedoColor=new(.22f,.24f,.25f),Metallic=.8f,Roughness=.72f },
            new StandardMaterial3D { AlbedoColor=new(.08f,.095f,.11f),Metallic=.6f,Roughness=.9f },heat };
        var mesh=MeshFrom(new[]{skin,steel,machinery,hot},materials,Transform3D.Identity);
        if(mesh.GetSurfaceCount()>0) { CombatFx.Make(root,mesh,null!).Name="ExposedStructure"; _interiorHeat.Add(heat); }
    }
    private static bool Inside(Vector2 p,Vector2 half,Vector2[] polygon) =>
        Geometry2D.IsPointInPolygon(p-half,polygon) && Geometry2D.IsPointInPolygon(p+half,polygon)
        && Geometry2D.IsPointInPolygon(p+new Vector2(half.X,-half.Y),polygon) && Geometry2D.IsPointInPolygon(p+new Vector2(-half.X,half.Y),polygon);
    private static List<Vector3> ClipPolygon(List<Vector3> input,Vector3 min,Vector3 max)
    {
        for(int plane=0;plane<6 && input.Count>0;plane++)
        {
            int axis=plane/2; bool lower=plane%2==0; float edge=lower?min[axis]:max[axis];
            var result=new List<Vector3>(); var previous=input[^1]; float pd=(previous[axis]-edge)*(lower?1:-1);
            foreach(var current in input)
            {
                float cd=(current[axis]-edge)*(lower?1:-1);
                if((pd>=0)!=(cd>=0)) result.Add(previous.Lerp(current,pd/(pd-cd)));
                if(cd>=0) result.Add(current);
                previous=current; pd=cd;
            }
            input=result;
        }
        return input;
    }
    private static void Face(Surface surface,Vector3 a,Vector3 b,Vector3 c,Vector3 normal)
    {
        if((b-a).Cross(c-a).Dot(normal)>0) (b,c)=(c,b); // Godot front faces are clockwise.
        surface.Triangle(new(a,normal,Vector2.Zero),new(b,normal,Vector2.Right),new(c,normal,Vector2.One));
    }
    private static void Fan(Surface surface,List<Vector3> polygon,Vector3 normal)
    { for(int i=1;i+1<polygon.Count;i++) Face(surface,polygon[0],polygon[i],polygon[i+1],normal); }
    private static void Box(Surface surface,Vector3 center,Vector3 size,Basis basis)
    {
        for(int axis=0;axis<3;axis++)
        for(int side=0;side<2;side++)
        {
            int u=(axis+1)%3,v=(axis+2)%3; float sign=side==0?-1:1;
            Vector3 normal=Vector3.Zero; normal[axis]=sign;
            Vector3 a=Vector3.Zero; a[axis]=sign*size[axis]*.5f; a[u]=-size[u]*.5f; a[v]=-size[v]*.5f;
            Vector3 b=a,c=a,d=a; b[u]+=size[u]; c[u]+=size[u]; c[v]+=size[v]; d[v]+=size[v];
            normal=basis*normal;
            Face(surface,center+basis*a,center+basis*b,center+basis*c,normal);
            Face(surface,center+basis*a,center+basis*c,center+basis*d,normal);
        }
    }
    private static void Beam(Surface surface,Vector3 a,Vector3 b,float width,Vector3 hint)
    {
        Vector3 z=b-a; float length=z.Length(); if(length<.00001f) return; z/=length;
        Vector3 x=hint.Cross(z);
        if(x.LengthSquared()<.01f) x=(Mathf.Abs(z.Y)<.9f?Vector3.Up:Vector3.Right).Cross(z);
        x=x.Normalized(); Vector3 y=z.Cross(x);
        Box(surface,(a+b)*.5f,new Vector3(width,width,length),new Basis(x,y,z));
    }
}
