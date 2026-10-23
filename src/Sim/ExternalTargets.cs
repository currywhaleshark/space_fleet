using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Godot;

namespace SpaceFleet.Sim;

/// <summary>Authored outer surface, independent of damage compartments. Null ModuleId
/// means ordinary hull-ray damage (PD has no independent damage compartment yet).</summary>
public sealed record ExternalPart(string Id,string Label,AimSubsystem Kind,string? ModuleId,string Node,
    Vector3 Center,Vector3 HalfSize,Vector3 Normal,Vector3[] Samples);
public readonly record struct ExternalAim(ExternalPart Part,Vector3 Point,float Size,float Error);

/// <summary>Immutable measurement inputs; visibility is memoized on demand. A distant
/// hull lock need not cast rays for every tiny fitting in an 80-ship fleet.</summary>
public sealed class ExternalObservation
{
    private readonly ExternalModel _model;
    private readonly Vector3 _from;
    private readonly ulong _damage;
    private ulong _tested,_visible;
    internal ExternalObservation(ExternalModel model,Vector3 from,ulong damage)
    {_model=model;_from=from;_damage=damage;}
    public bool Visible(int index)
    {
        if((uint)index>=(uint)_model.Parts.Length)return false;
        ulong bit=1UL<<index;
        if((_tested&bit)==0)
        {
            if(_model.Visible(_model.Parts[index],_from,out _,out _,precision:false))_visible|=bit;
            _tested|=bit;
        }
        return(_visible&bit)!=0;
    }
    public bool Destroyed(int index)=>(_damage&(1UL<<index))!=0&&Visible(index);
    public int KnownLosses
    {
        get {int count=0;for(int i=0;i<_model.Parts.Length;i++)if(Destroyed(i))count++;return count;}
    }
}

public sealed class ExternalModel
{
    public ExternalPart[] Parts { get; }
    private readonly SurfaceRayMesh _mesh;
    internal ExternalModel(ExternalPart[] parts,Stream triangles){Parts=parts;_mesh=new(triangles);}
    public bool Visible(ExternalPart part,Vector3 from,out Vector3 point,out float size,bool precision=true)
    {
        point=default;size=0;
        Vector3 toward=(from-part.Center).Normalized();
        if(part.Normal.LengthSquared()>.1f&&part.Normal.Dot(toward)<=.05f)return false;
        // Projected area, not a long thin antenna's longest dimension.
        var h=part.HalfSize;
        size=MathF.Sqrt(Math.Abs(toward.X)*h.Y*h.Z+Math.Abs(toward.Y)*h.X*h.Z+Math.Abs(toward.Z)*h.X*h.Y);
        int exposed=0;float closest=float.PositiveInfinity;
        // Test the near-facing sample first. Observation needs only one visible surface;
        // projected precision is evaluated only when a weapon can resolve this part.
        int first=0;float front=float.NegativeInfinity;
        for(int i=0;i<part.Samples.Length;i++){float d=part.Samples[i].Dot(toward);if(d>front){front=d;first=i;}}
        for(int i=-1;i<part.Samples.Length;i++)
        {
            if(i==first)continue;var p=part.Samples[i<0?first:i];
            Vector3 delta=p-from;float length=delta.Length();if(length<.01f)continue;
            if(_mesh.Blocked(from,delta/length,length-Math.Max(.015f,h.Length()*.002f)))continue;
            if(!precision){point=p;return true;}
            exposed++;if(length<closest){closest=length;point=p;}
        }
        size*=MathF.Sqrt(exposed/(float)Math.Max(1,part.Samples.Length));
        return exposed>0;
    }
}

/// <summary>Pure managed BVH baked from the GLB. No Godot nodes/native calls on sim ticks.</summary>
internal sealed class SurfaceRayMesh
{
    private readonly record struct Tri(Vector3 A,Vector3 B,Vector3 C)
    {public Vector3 Center=>(A+B+C)/3;}
    private readonly record struct Branch(Vector3 Min,Vector3 Max,int Start,int Count,int Left,int Right);
    private readonly Tri[] _tri;
    private readonly List<Branch> _nodes=new();
    internal SurfaceRayMesh(Stream source)
    {
        using var zipped=new GZipStream(source,CompressionMode.Decompress);using var r=new BinaryReader(zipped);
        int count=r.ReadInt32();if(count<1||count>2_000_000)throw new InvalidDataException("Invalid external mesh");
        Vector3 V()=>new(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
        _tri=new Tri[count];for(int i=0;i<count;i++)_tri[i]=new(V(),V(),V());Build(0,count);
    }
    private int Build(int start,int count)
    {
        Vector3 lo=_tri[start].A,hi=lo;
        for(int i=start;i<start+count;i++){var t=_tri[i];lo=lo.Min(t.A).Min(t.B).Min(t.C);hi=hi.Max(t.A).Max(t.B).Max(t.C);}
        int index=_nodes.Count;_nodes.Add(default);
        int left=-1,right=-1;
        if(count>12)
        {
            var extent=hi-lo;int axis=extent.X>extent.Y?(extent.X>extent.Z?0:2):(extent.Y>extent.Z?1:2);
            Array.Sort(_tri,start,count,Comparer<Tri>.Create((a,b)=>a.Center[axis].CompareTo(b.Center[axis])));
            int half=count/2;left=Build(start,half);right=Build(start+half,count-half);
        }
        _nodes[index]=new(lo,hi,start,count,left,right);return index;
    }
    internal bool Blocked(Vector3 origin,Vector3 direction,float range)=>Visit(0,origin,direction,range);
    private bool Visit(int id,Vector3 origin,Vector3 dir,float range)
    {
        var n=_nodes[id];float near=0,far=range;
        for(int a=0;a<3;a++)
        {
            if(Math.Abs(dir[a])<1e-9f){if(origin[a]<n.Min[a]||origin[a]>n.Max[a])return false;continue;}
            float t0=(n.Min[a]-origin[a])/dir[a],t1=(n.Max[a]-origin[a])/dir[a];
            near=Math.Max(near,Math.Min(t0,t1));far=Math.Min(far,Math.Max(t0,t1));if(far<near)return false;
        }
        if(n.Left>=0)return Visit(n.Left,origin,dir,range)||Visit(n.Right,origin,dir,range);
        for(int i=n.Start;i<n.Start+n.Count;i++)
        {
            var t=_tri[i];Vector3 e1=t.B-t.A,e2=t.C-t.A,p=dir.Cross(e2);float det=e1.Dot(p);
            if(Math.Abs(det)<1e-8f)continue;
            float inv=1/det;Vector3 s=origin-t.A;float u=s.Dot(p)*inv;if(u<0||u>1)continue;
            Vector3 q=s.Cross(e1);float v=dir.Dot(q)*inv;if(v<0||u+v>1)continue;
            float distance=e2.Dot(q)*inv;if(distance>.001f&&distance<range)return true;
        }
        return false;
    }
}

public static class ExternalTargets
{
    // Different guns solve different trajectories but query the same observed hull surface.
    // Reuse only EXACT geometry inputs; no time window, rounding or stale-pose allowance.
    private sealed class AimGeometry
    {
        internal ExternalModel? Model;
        internal Vector3 From;
        internal ulong Tested,Visible;
        internal Vector3[] Points=Array.Empty<Vector3>();
        internal float[] Sizes=Array.Empty<float>();
    }
    private static readonly ConditionalWeakTable<ShipBody,Dictionary<ShipBody,AimGeometry>> Geometry=new();
    private static bool VisibleFor(ShipBody shooter,ShipBody target,ExternalModel model,int index,Vector3 from,out Vector3 point,out float size)
    {
        var map=Geometry.GetValue(shooter,static _=>new());
        if(!map.TryGetValue(target,out var cache))map[target]=cache=new();
        if(cache.Model!=model||cache.From!=from)
        {
            cache.Model=model;cache.From=from;cache.Tested=cache.Visible=0;
            if(cache.Points.Length!=model.Parts.Length){cache.Points=new Vector3[model.Parts.Length];cache.Sizes=new float[model.Parts.Length];}
        }
        ulong bit=1UL<<index;
        if((cache.Tested&bit)==0)
        {
            if(model.Visible(model.Parts[index],from,out cache.Points[index],out cache.Sizes[index]))cache.Visible|=bit;
            cache.Tested|=bit;
        }
        point=cache.Points[index];size=cache.Sizes[index];return(cache.Visible&bit)!=0;
    }
    private static readonly ConcurrentDictionary<string,Lazy<ExternalModel?>> Cache=new(StringComparer.Ordinal);
    private static readonly Assembly Assembly=typeof(ExternalTargets).Assembly;
    private static readonly Lazy<JsonDocument> Catalog=new(()=>JsonDocument.Parse(Assembly.GetManifestResourceStream("SpaceFleet.data.targeting.external_parts.json")
        ??throw new InvalidDataException("External targeting metadata missing")));
    public static ExternalModel? For(ShipDefinition def)=>Cache.GetOrAdd(def.ModelId,static id=>new Lazy<ExternalModel?>(()=>Load(id))).Value;
    private static ExternalModel? Load(string modelId)
    {
        if(!Catalog.Value.RootElement.TryGetProperty(modelId,out var data))return null;
        static Vector3 V(JsonElement a)=>new(a[0].GetSingle(),a[1].GetSingle(),a[2].GetSingle());
        var parts=data.GetProperty("parts").EnumerateArray().Select(p=>new ExternalPart(p.GetProperty("id").GetString()!,p.GetProperty("label").GetString()!,
            Enum.Parse<AimSubsystem>(p.GetProperty("kind").GetString()!),p.GetProperty("moduleId").GetString(),p.GetProperty("node").GetString()!,
            V(p.GetProperty("center")),V(p.GetProperty("halfSize")),V(p.GetProperty("normal")),p.GetProperty("samples").EnumerateArray().Select(V).ToArray())).ToArray();
        using var stream=Assembly.GetManifestResourceStream($"SpaceFleet.data.targeting.{modelId}.mesh.gz")??throw new InvalidDataException("External mesh missing");
        return new(parts,stream);
    }
    public static (ulong Visible,ulong Destroyed) Observe(ShipBody target,Vec3d source)
    {
        var model=For(target.Definition);if(model is null)return default;
        Vector3 from=target.Orientation.Inverse()*(source-target.Position).ToVector3();ulong visible=0,destroyed=0;
        for(int i=0;i<model.Parts.Length;i++)
        {
            var part=model.Parts[i];if(!model.Visible(part,from,out _,out _,precision:false))continue;
            visible|=1UL<<i;
            if(part.ModuleId is {} id&&target.Damage.Modules.FirstOrDefault(m=>m.Definition.Id==id)?.Destroyed==true)destroyed|=1UL<<i;
        }
        return(visible,destroyed);
    }
    public static ExternalObservation? Capture(ShipBody target,Vec3d source)
    {
        var model=For(target.Definition);if(model is null)return null;
        ulong damage=0;
        for(int i=0;i<model.Parts.Length;i++)
            if(model.Parts[i].ModuleId is {} id&&target.Damage.Module(id).Destroyed)damage|=1UL<<i;
        return new(model,target.Orientation.Inverse()*(source-target.Position).ToVector3(),damage);
    }
    public static bool TryAim(ShipBody shooter,ShipBody target,SensorTrack track,double time,string id,out ExternalAim aim,
        RailgunState? weapon=null,ObstacleSet? obstacles=null)
    {
        aim=default;if(track.Level<TrackLevel.Locked||time-track.SampleTime>FireControl.MaxObservationAge||time<track.SampleTime)return false;
        var model=For(target.Definition);if(model is null)return false;
        int index=Array.FindIndex(model.Parts,p=>p.Id==id);if(index<0)return false;
        var part=model.Parts[index];
        float error=float.PositiveInfinity;
        if(weapon is not null)
        {
            var solution=FireControl.Solve(shooter,target,time,track:track,weapon:weapon);
            if(solution.Valid)error=solution.ErrorMeters;
        }
        else foreach(var gun in shooter.Railguns)
        {
            var solution=FireControl.Solve(shooter,target,time,track:track,weapon:gun);
            if(solution.Valid)error=Math.Min(error,solution.ErrorMeters);
        }
        var h=part.HalfSize;
        if(error>MathF.Sqrt(h.X*h.Y+h.X*h.Z+h.Y*h.Z))return false;
        if(!track.PartVisible(index)||track.PartDestroyed(index))return false;
        // Only observation pose and sample position enter geometry queries.
        Vector3 from=track.ObservedOrientation.Inverse()*(shooter.Position-track.EstimatedPosition).ToVector3();
        if(!VisibleFor(shooter,target,model,index,from,out var point,out float size))return false;
        var world=track.EstimatedPosition+Vec3d.From(track.ObservedOrientation*point);
        if(obstacles is {Count:>0}&&obstacles.BlocksSegment(shooter.Position,world))return false;
        if(error>size)return false;
        aim=new(part,point,size,error);return true;
    }
    public static ModuleState? Pick(ShipBody shooter,ShipBody target,SensorTrack track,double time,AimSubsystem kind,
        string? preferred=null,ObstacleSet? obstacles=null)
    {
        var model=For(target.Definition);if(model is null)return null;
        foreach(var p in model.Parts)
        {
            if(p.ModuleId is not {} id||!((preferred is not null&&id==preferred)||(preferred is null&&p.Kind==kind)))continue;
            if(TryAim(shooter,target,track,time,p.Id,out _,obstacles:obstacles))return target.Damage.Modules.FirstOrDefault(m=>m.Definition.Id==id);
        }
        return null;
    }
    /// <summary>Compatibility for legacy module-centre callers: never accept an arbitrary internal point.</summary>
    public static Vector3? ResolvePoint(ShipBody shooter,ShipBody target,SensorTrack track,double time,Vector3 requested,RailgunState? weapon=null)
    {
        var model=For(target.Definition);if(model is null)return null;
        foreach(var p in model.Parts)
        {
            bool match=p.Samples.Any(s=>s.DistanceSquaredTo(requested)<.0001f)
                ||p.ModuleId is {} id&&target.Definition.Modules.Any(m=>m.Id==id&&m.Center.DistanceSquaredTo(requested)<.0001f);
            if(match&&TryAim(shooter,target,track,time,p.Id,out var aim,weapon))return aim.Point;
        }
        return null;
    }
}
