using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SpaceFleet.View;

public partial class WreckView
{
    internal static bool IsHullPanel(ShaderMaterial material) =>
        material.Shader?.ResourcePath == "res://shaders/hull_panels.gdshader";

    // Never alter imported/shared materials: another live ship may use the same resource.
    private static Material Burn(Material? original, Dictionary<Material,Material> cache)
    {
        if(original is null) return new StandardMaterial3D { AlbedoColor=new(.035f,.04f,.045f),Roughness=.95f };
        if(cache.TryGetValue(original,out var previous)) return previous;
        Material result=(Material)original.Duplicate();
        if(result is ShaderMaterial shader && IsHullPanel(shader))
        { shader.SetShaderParameter("power",0f); shader.SetShaderParameter("wreck_burn",1f); }
        else if(result is StandardMaterial3D standard)
        {
            standard.EmissionEnabled=false; standard.EmissionEnergyMultiplier=0;
            standard.AlbedoColor=standard.AlbedoColor.Darkened(.48f);
            standard.Roughness=Mathf.Max(.85f,standard.Roughness);
            standard.ShadingMode=BaseMaterial3D.ShadingModeEnum.PerPixel;
        }
        cache[original]=result;
        return result;
    }

    private readonly record struct Triangle(Vertex A,Vertex B,Vertex C,int Surface,int Component);
    private sealed class Chunk
    {
        public required int Id;
        public readonly List<int> Triangles=new();
        public Vector3 Min=new(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity);
        public Vector3 Max=new(float.NegativeInfinity,float.NegativeInfinity,float.NegativeInfinity);
        public bool Armor,Thermal;
        public Vector3 Center => (Min+Max)*.5f;
    }
    private sealed class Source
    {
        public required Transform3D Pose;
        public required Material[] Materials;
        public readonly List<Triangle> Triangles=new();
        public readonly List<Chunk> Chunks=new();
        public readonly HashSet<int> Detached=new();
        public bool StaticHull;
    }

    // The GLB batches authored objects. Weld only coincident positions to recover its
    // disconnected pieces, including normal/UV/material seams, without changing the GLB.
    private static readonly Dictionary<ulong,Source> GeometryCache=new();
    internal static void PrepareGeometry(Node3D model,float length)
    {
        foreach(var mesh in ImportedShipModels.Meshes(model))
            if(mesh.Mesh is ArrayMesh && !GeometryCache.ContainsKey(mesh.Mesh.GetInstanceId()))
                GeometryCache.Add(mesh.Mesh.GetInstanceId(),ReadGeometry(mesh,length));
    }
    private static Source Snapshot(MeshInstance3D mesh,Transform3D pose,float length,Dictionary<Material,Material> materials)
    {
        if(!GeometryCache.TryGetValue(mesh.Mesh.GetInstanceId(),out var template))
        { template=ReadGeometry(mesh,length); GeometryCache.Add(mesh.Mesh.GetInstanceId(),template); }
        var source=new Source { Pose=pose,StaticHull=template.StaticHull,Materials=new Material[template.Materials.Length] };
        for(int i=0;i<source.Materials.Length;i++) source.Materials[i]=Burn(mesh.GetActiveMaterial(i),materials);
        Basis normalBasis=pose.Basis.Inverse().Transposed();
        Vertex V(Vertex v) => new(pose*v.P,(normalBasis*v.N).Normalized(),v.U);
        if(pose.IsEqualApprox(Transform3D.Identity))
        { source.Triangles.AddRange(template.Triangles); source.Chunks.AddRange(template.Chunks); }
        else
        {
            foreach(var t in template.Triangles) source.Triangles.Add(new(V(t.A),V(t.B),V(t.C),t.Surface,t.Component));
            foreach(var c in template.Chunks)
            {
                var copy=new Chunk { Id=c.Id,Armor=c.Armor,Thermal=c.Thermal }; copy.Triangles.AddRange(c.Triangles);
                foreach(int i in copy.Triangles) { var t=source.Triangles[i]; copy.Min=copy.Min.Min(t.A.P).Min(t.B.P).Min(t.C.P); copy.Max=copy.Max.Max(t.A.P).Max(t.B.P).Max(t.C.P); }
                source.Chunks.Add(copy);
            }
        }
        return source;
    }
    private static Source ReadGeometry(MeshInstance3D mesh,float length)
    {
        var pose=Transform3D.Identity;
        int surfaces=mesh.Mesh.GetSurfaceCount();
        var source=new Source { Pose=pose,Materials=new Material[surfaces],StaticHull=mesh.Name.ToString().StartsWith("FleetHull",StringComparison.Ordinal) };
        var names=new string[surfaces];
        float epsilon=Mathf.Max(.00001f,length*.000001f);
        var ids=new Dictionary<(long,long,long),int>(); var parents=new List<int>();
        int Id(Vector3 p)
        {
            var key=((long)Math.Round(p.X/epsilon),(long)Math.Round(p.Y/epsilon),(long)Math.Round(p.Z/epsilon));
            if(ids.TryGetValue(key,out int id)) return id;
            id=parents.Count; ids.Add(key,id); parents.Add(id); return id;
        }
        int Find(int i) { while(parents[i]!=i) { parents[i]=parents[parents[i]]; i=parents[i]; } return i; }
        void Join(int a,int b) { a=Find(a); b=Find(b); if(a!=b) parents[b]=a; }
        Basis normalBasis=pose.Basis.Inverse().Transposed();
        for(int surface=0;surface<surfaces;surface++)
        {
            names[surface]=mesh.Mesh.SurfaceGetMaterial(surface)?.ResourceName??"";
            if(((ArrayMesh)mesh.Mesh).SurfaceGetPrimitiveType(surface)!=Mesh.PrimitiveType.Triangles) continue;
            var arrays=mesh.Mesh.SurfaceGetArrays(surface);
            var positions=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var uv=arrays[(int)Mesh.ArrayType.TexUV].VariantType==Variant.Type.Nil?Array.Empty<Vector2>():arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            var indices=arrays[(int)Mesh.ArrayType.Index].VariantType==Variant.Type.Nil?Array.Empty<int>():arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            int count=indices.Length>0?indices.Length:positions.Length;
            Vertex V(int i) { int index=indices.Length>0?indices[i]:i; return new(pose*positions[index],(normalBasis*normals[index]).Normalized(),uv.Length>index?uv[index]:Vector2.Zero); }
            for(int i=0;i+2<count;i+=3)
            {
                var a=V(i); var b=V(i+1); var c=V(i+2);
                int id=Id(a.P); Join(id,Id(b.P)); Join(id,Id(c.P));
                source.Triangles.Add(new(a,b,c,surface,id));
            }
        }
        var chunks=new Dictionary<int,Chunk>();
        for(int i=0;i<source.Triangles.Count;i++)
        {
            var t=source.Triangles[i]; int id=Find(t.Component); t=t with { Component=id }; source.Triangles[i]=t;
            if(!chunks.TryGetValue(id,out var c)) { c=new Chunk { Id=id }; chunks.Add(id,c); }
            c.Triangles.Add(i); c.Min=c.Min.Min(t.A.P).Min(t.B.P).Min(t.C.P); c.Max=c.Max.Max(t.A.P).Max(t.B.P).Max(t.C.P);
            string name=names[t.Surface];
            c.Armor|=name.StartsWith("Armor",StringComparison.Ordinal);
            c.Thermal|=name.StartsWith("Thermal - graphite",StringComparison.Ordinal);
        }
        source.Chunks.AddRange(chunks.Values);
        return source;
    }

    private static ArrayMesh MeshFrom(Surface[] surfaces,Material[] materials,Transform3D pose)
    {
        var mesh=new ArrayMesh(); Transform3D inverse=pose.AffineInverse();
        // Restore source-local coordinates after clipping. Object-space panel patterns
        // then stay identical on hull, turret and detached plates, with no UV jump.
        for(int i=0;i<surfaces.Length;i++)
        {
            var s=surfaces[i]; if(s.Positions.Count==0) continue;
            var positions=new Vector3[s.Positions.Count]; var normals=new Vector3[positions.Length];
            for(int j=0;j<positions.Length;j++) { positions[j]=inverse*s.Positions[j]; normals[j]=(pose.Basis.Transposed()*s.Normals[j]).Normalized(); }
            var output=new Godot.Collections.Array(); output.Resize((int)Mesh.ArrayType.Max);
            output[(int)Mesh.ArrayType.Vertex]=positions; output[(int)Mesh.ArrayType.Normal]=normals; output[(int)Mesh.ArrayType.TexUV]=s.UV.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,output); mesh.SurfaceSetMaterial(mesh.GetSurfaceCount()-1,materials[i]);
        }
        return mesh;
    }
    private static Surface[] Surfaces(int count) => Enumerable.Range(0,count).Select(_=>new Surface()).ToArray();

    private sealed record LoosePart(Node3D Node,int ParentPiece,Vector3 Center,Vector3 Velocity,Vector3 Spin,float Release,float Lifetime);
    private readonly List<LoosePart> _loose=new();
    public int DetachedPartCount => _loose.Count;
    public int VisibleDetachedParts => _loose.Count(p=>p.Node.Visible);
    public int DetachedTriangleCount { get; private set; }
    public int SourceTriangleCount { get; private set; }
    public int RetainedTriangleCount { get; private set; }
    public int InteriorSectionCount { get; private set; }
    public double BuildMilliseconds { get; private set; }

    private void Detach(ShipView ship,List<Source> sources,SpaceFleet.Sim.WreckLayout layout)
    {
        float length=ship.Body.Class.Length;
        int limit=length>600?24:length>100?16:8;
        if(layout.Pieces.Count==2) limit=limit*3/4;
        var candidates=new List<(Source Source,Chunk Chunk,float Score)>();
        foreach(var source in sources.Where(s=>s.StaticHull))
        foreach(var c in source.Chunks)
        {
            Vector3 size=c.Max-c.Min;
            float longest=Mathf.Max(size.X,Mathf.Max(size.Y,size.Z)),shortest=Mathf.Min(size.X,Mathf.Min(size.Y,size.Z));
            float middle=size.X+size.Y+size.Z-longest-shortest;
            if(!(c.Armor||c.Thermal) || c.Triangles.Count>1400 || longest>length*.24f || longest<length*.008f
                || middle<length*.004f || shortest>longest*.3f) continue;
            // Prefer plates near a fracture, with a small quota advantage for radiator faces.
            float distance=Mathf.Abs(c.Center.Z-layout.Pieces[0].Maximum.Z);
            candidates.Add((source,c,distance/length+(c.Thermal?-.45f:0)+c.Center.DistanceTo(ship.Body.Damage.DestructionPoint)/length*.15f));
        }
        int number=0;
        foreach(var (source,c,_) in candidates.OrderBy(c=>c.Score).ThenBy(c=>c.Chunk.Id).Take(limit))
        {
            source.Detached.Add(c.Id); DetachedTriangleCount+=c.Triangles.Count;
            var surfaces=Surfaces(source.Materials.Length);
            foreach(int index in c.Triangles) { var t=source.Triangles[index]; surfaces[t.Surface].Triangle(t.A,t.B,t.C); }
            var root=new Node3D { Name=$"DetachedPlate{number}",Visible=false }; AddChild(root);
            var node=CombatFx.Make(root,MeshFrom(surfaces,source.Materials,source.Pose),null!); node.Transform=source.Pose;
            int parent=ContainingPiece(layout,c.Center);
            Vector3 outward=new(c.Center.X,c.Center.Y,c.Center.Z*.15f);
            if(outward.LengthSquared()<.001f) outward=Vector3.Up;
            outward=outward.Normalized();
            float speed=Mathf.Clamp(length*.035f,1.8f,38)*(1+(number%5)*.12f);
            Vector3 spin=new Vector3(.5f+number%3,-.7f+number%2,.3f).Normalized()*(.22f+(number%4)*.17f);
            _loose.Add(new(root,parent,c.Center,outward*speed,spin,.15f+(number%6)*.07f,22+(number%4)));
            number++;
        }
    }
    private void SyncLoose(float age,SpaceFleet.Sim.WreckLayout layout)
    {
        foreach(var p in _loose)
        {
            float time=Mathf.Max(0,age-p.Release); p.Node.Visible=!ConstructionPending && time<p.Lifetime;
            if(!p.Node.Visible) continue;
            if(age<=p.Release) { p.Node.Transform=layout.Pieces[p.ParentPiece].Pose(age); continue; }
            var parent=layout.Pieces[p.ParentPiece]; var released=parent.Pose(p.Release);
            Vector3 center=released*p.Center;
            Vector3 inherited=parent.Drift+parent.Spin.Cross(released.Basis*(p.Center-parent.Center));
            Vector3 position=center+(inherited+p.Velocity)*time;
            float fade=Mathf.Clamp((p.Lifetime-time)/2,0,1);
            Basis basis=(new Basis(p.Spin.Normalized(),p.Spin.Length()*time)*released.Basis).Scaled(Vector3.One*fade);
            p.Node.Transform=new(basis,position-basis*p.Center);
        }
    }
    private void ClearGeometry()
    {
        foreach(var p in _pieces) { p.Visible=false; RemoveChild(p); p.QueueFree(); } _pieces.Clear();
        foreach(var p in _loose) { p.Node.Visible=false; RemoveChild(p.Node); p.Node.QueueFree(); } _loose.Clear();
        _interiorHeat.Clear(); DetachedTriangleCount=SourceTriangleCount=RetainedTriangleCount=InteriorSectionCount=0;
    }
}
