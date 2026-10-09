using System;
using System.Collections.Generic;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.View;

/// <summary>Cut the current posed model into shared simulation cells, retaining its authored materials.</summary>
public partial class WreckView : Node3D
{
    private readonly List<Node3D> _pieces=new();
    private WreckLayout? _layout;
    private CombatBurstBatch? _bursts;
    private CarbonCombatFx.ExplosionTimeline? _timeline;
    private readonly List<(int Piece,Vector3 Position,Vector3 Normal)> _blastSites=new();
    private (int Piece,Vector3 Position) _mainBlastSite;
    public int PieceCount => _pieces.Count;
    public int VisibleExplosionFlashes => _bursts?.VisibleFlashes??0;
    public int VisibleExplosionSparks => _bursts?.VisibleSparks??0;
    public int ExplosionPoolSize => _bursts?.PoolSize??0;
    public float MainExplosionAt => _timeline?.GlobalTime??0;
    public float ExplosionEndAt => _timeline?.EndTime??0;
    public IReadOnlyList<float> LocalExplosionTimes => _timeline?.LocalTimes??Array.Empty<float>();
    private readonly record struct Vertex(Vector3 P,Vector3 N,Vector2 U)
    {
        public Vertex Lerp(Vertex b,float t) => new(P.Lerp(b.P,t),N.Lerp(b.N,t).Normalized(),U.Lerp(b.U,t));
    }
    private sealed class Surface
    {
        public readonly List<Vector3> Positions=new(),Normals=new();
        public readonly List<Vector2> UV=new();
        public void Triangle(Vertex a,Vertex b,Vertex c)
        {
            Positions.Add(a.P); Positions.Add(b.P); Positions.Add(c.P);
            Normals.Add(a.N); Normals.Add(b.N); Normals.Add(c.N);
            UV.Add(a.U); UV.Add(b.U); UV.Add(c.U);
        }
    }
    public void Sync(ShipView ship,Node3D model,Camera3D camera)
    {
        _bursts?.Begin();
        if(ship.Body.Wreck is not {} layout)
        {
            if(_layout is not null) { foreach(var p in _pieces) p.QueueFree(); _pieces.Clear(); _layout=null; model.Visible=true; }
            _timeline=null; _blastSites.Clear(); _bursts?.End();
            return;
        }
        if(_layout!=layout)
        {
            foreach(var p in _pieces) p.QueueFree(); _pieces.Clear();
            model.Visible=true; Build(ship,model,layout); _layout=layout; model.Visible=false;
            PrepareExplosions(ship,layout,camera);
        }
        for(int i=0;i<_pieces.Count;i++) _pieces[i].Transform=layout.Pieces[i].Pose(ship.Body.WreckAge);
        float age=(float)(ship.Body.SimTime-ship.Body.Damage.DestroyedAt);
        if(_timeline is null || _bursts is null) return;
        uint seed=CarbonCombatFx.Seed(ship.Body.Callsign);
        float size=ship.Body.Class.Length;
        for(int i=0;i<_blastSites.Count;i++)
        {
            var site=_blastSites[i];
            Transform3D pose=ship.GlobalTransform*layout.Pieces[site.Piece].Pose(ship.Body.WreckAge);
            Vector3 at=pose*site.Position,normal=pose.Basis*site.Normal;
            // Each secondary event stays with its physical piece, including its rotation.
            _bursts.Draw(camera,at,at,normal,age-_timeline.LocalTimes[i],Mathf.Clamp(size*.075f,4,100),
                seed+(uint)i*43,18,duration:1.7f);
        }
        Vector3 center=ship.GlobalTransform*layout.Pieces[_mainBlastSite.Piece].Pose(ship.Body.WreckAge)*_mainBlastSite.Position;
        _bursts.Draw(camera,center,center,ship.GlobalBasis*Vector3.Up,age,Mathf.Max(6,size*.24f),seed,16,
            true,true,duration:.45f);
        _bursts.Draw(camera,center,center,ship.GlobalBasis*Vector3.Up,age-_timeline.GlobalTime,
            Mathf.Max(10,size*.42f),seed+91,32,false,true,duration:1.5f);
        _bursts.End();
    }
    private void PrepareExplosions(ShipView ship,WreckLayout layout,Camera3D camera)
    {
        _bursts??=new CombatBurstBatch(this);
        _blastSites.Clear();
        int count=ship.Body.Class.Length>600?8:ship.Body.Class.Length>100?5:3;
        Vector3 cause=ship.Body.Damage.DestructionPoint;
        // Alternate hull sides and follow each clipped cell; no hard-coded model sockets.
        for(int i=0;i<count;i++)
        {
            int part=i%layout.Pieces.Count;
            var piece=layout.Pieces[part];
            var box=piece.Boxes[(i/layout.Pieces.Count)%piece.Boxes.Length];
            Vector3 normal=i%3==0?Vector3.Up:i%2==0?Vector3.Right:Vector3.Left;
            var surface=ship.ProjectFxSurface(box.Center+normal*box.HalfSize,-normal);
            _blastSites.Add((ContainingPiece(layout,surface.Point),surface.Point,surface.Normal));
        }
        _blastSites.Sort((a,b)=>a.Position.DistanceSquaredTo(cause).CompareTo(b.Position.DistanceSquaredTo(cause)));
        _timeline=CarbonCombatFx.ExplosionTimes(count,CarbonCombatFx.Seed(ship.Body.Callsign));
        Vector3 toCamera=ship.GlobalBasis.Inverse()*(camera.GlobalPosition-ship.GlobalTransform*cause);
        Vector3 main=ship.ProjectFxPoint(cause,-toCamera.Normalized());
        _mainBlastSite=(ContainingPiece(layout,main),main);
    }
    private static int ContainingPiece(WreckLayout layout,Vector3 at)
    {
        int closest=0; float best=float.MaxValue;
        for(int i=0;i<layout.Pieces.Count;i++)
        {
            var p=layout.Pieces[i];
            float distance=at.DistanceSquaredTo(at.Clamp(p.Minimum,p.Maximum));
            if(distance<best) { best=distance; closest=i; }
        }
        return closest;
    }
    private void Build(ShipView ship,Node3D model,WreckLayout layout)
    {
        var sources=new List<(MeshInstance3D Node,Transform3D Pose)>();
        foreach(var mesh in ImportedShipModels.Meshes(model))
        {
            if(mesh.Mesh is not ArrayMesh || !mesh.IsVisibleInTree()) continue;
            sources.Add((mesh,ship.GlobalTransform.AffineInverse()*mesh.GlobalTransform));
        }
        foreach(var part in layout.Pieces)
        {
            var root=new Node3D(); AddChild(root); _pieces.Add(root);
            foreach(var (source,pose) in sources)
            {
                var result=new ArrayMesh();
                for(int surface=0;surface<source.Mesh.GetSurfaceCount();surface++)
                {
                    if(((ArrayMesh)source.Mesh).SurfaceGetPrimitiveType(surface)!=Mesh.PrimitiveType.Triangles) continue;
                    var arrays=source.Mesh.SurfaceGetArrays(surface);
                    var positions=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                    var uv=arrays[(int)Mesh.ArrayType.TexUV].VariantType==Variant.Type.Nil?Array.Empty<Vector2>():arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                    var indices=arrays[(int)Mesh.ArrayType.Index].VariantType==Variant.Type.Nil?Array.Empty<int>():arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                    var data=new Surface();
                    int count=indices.Length>0?indices.Length:positions.Length;
                    Vertex V(int i) { int index=indices.Length>0?indices[i]:i; return new(pose*positions[index],(pose.Basis*normals[index]).Normalized(),uv.Length>index?uv[index]:Vector2.Zero); }
                    for(int i=0;i+2<count;i+=3) Cut(data,V(i),V(i+1),V(i+2),part.Minimum,part.Maximum);
                    if(data.Positions.Count==0) continue;
                    var output=new Godot.Collections.Array(); output.Resize((int)Mesh.ArrayType.Max);
                    output[(int)Mesh.ArrayType.Vertex]=data.Positions.ToArray(); output[(int)Mesh.ArrayType.Normal]=data.Normals.ToArray(); output[(int)Mesh.ArrayType.TexUV]=data.UV.ToArray();
                    result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,output);
                    Material material=source.GetActiveMaterial(surface);
                    if(material is StandardMaterial3D standard)
                    { var dark=(StandardMaterial3D)standard.Duplicate(); dark.EmissionEnabled=false; dark.AlbedoColor=standard.AlbedoColor.Darkened(.22f); material=dark; }
                    result.SurfaceSetMaterial(result.GetSurfaceCount()-1,material);
                }
                if(result.GetSurfaceCount()>0) CombatFx.Make(root,result,null!);
            }
            // Dark structural bulkheads cover exposed interiors; no transparent hollow hulls at the fracture.
            var interior=new StandardMaterial3D { AlbedoColor=new Color(.07f,.09f,.11f),Metallic=.65f,Roughness=.9f };
            Vector3 allMin=ship.Body.Definition.Hull.Bounds.Center-ship.Body.Definition.Hull.Bounds.HalfSize;
            Vector3 allMax=ship.Body.Definition.Hull.Bounds.Center+ship.Body.Definition.Hull.Bounds.HalfSize;
            foreach(var box in part.Boxes)
            for(int axis=0;axis<3;axis++)
            for(int side=0;side<2;side++)
            {
                float plane=side==0?part.Minimum[axis]:part.Maximum[axis];
                if(plane<=allMin[axis]+.01f || plane>=allMax[axis]-.01f) continue;
                float face=box.Center[axis]+box.HalfSize[axis]*(side==0?-1:1);
                if(Mathf.Abs(face-plane)>.01f) continue;
                Vector3 size=box.HalfSize*1.7f,position=box.Center;
                size[axis]=Mathf.Max(.05f,ship.Body.Class.Length*.0003f); position[axis]=plane;
                var core=CombatFx.Make(root,new BoxMesh { Size=size },interior); core.Position=position;
            }
        }
    }
    private static void Cut(Surface output,Vertex a,Vertex b,Vertex c,Vector3 minimum,Vector3 maximum)
    {
        Vector3 lo=a.P.Min(b.P).Min(c.P),hi=a.P.Max(b.P).Max(c.P);
        if(hi.X<minimum.X || hi.Y<minimum.Y || hi.Z<minimum.Z || lo.X>maximum.X || lo.Y>maximum.Y || lo.Z>maximum.Z) return;
        Span<Vertex> first=stackalloc Vertex[16],second=stackalloc Vertex[16];
        first[0]=a; first[1]=b; first[2]=c; int count=3;
        for(int plane=0;plane<6 && count>0;plane++)
        {
            int axis=plane/2; bool lower=plane%2==0; float edge=lower?minimum[axis]:maximum[axis]; int written=0;
            Vertex previous=first[count-1]; float pd=(previous.P[axis]-edge)*(lower?1:-1);
            for(int j=0;j<count;j++)
            {
                Vertex current=first[j]; float cd=(current.P[axis]-edge)*(lower?1:-1);
                if((pd>=0)!=(cd>=0)) second[written++]=previous.Lerp(current,pd/(pd-cd));
                if(cd>=0) second[written++]=current;
                previous=current; pd=cd;
            }
            second[..written].CopyTo(first); count=written;
        }
        for(int j=1;j+1<count;j++) output.Triangle(first[0],first[j],first[j+1]);
    }
}
