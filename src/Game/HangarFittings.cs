using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;
using Category = SpaceFleet.Game.HangarView.FittingCategory;

namespace SpaceFleet.Game;

/// <summary>Preview-only surface picking. Uses the fitted model, including replacement barrels.
/// The merged hull is partitioned by authored module/socket coordinates; the hull itself occludes picks.</summary>
internal sealed class HangarFittings : IDisposable
{
    internal sealed class Part
    {
        public required string Id, Location, Name;
        public required HangarView.FittingCategory Category;
        public int Socket;
        public Node3D? Rig;
        public Aabb Region;
        public Vector3 Anchor;
        public readonly List<Vector3> Faces=new();
        public Vector3[] Samples=Array.Empty<Vector3>();
        public MeshInstance3D? Highlight;
        public Vector2? Screen;
        public string Caption=>Location+" · "+Name;
        public string Scope=>Category==HangarView.FittingCategory.MainGun?"클릭하여 이 포좌 교체":"클릭하여 함선 전체 계통 교체";
    }
    public readonly List<Part> Parts=new();
    private readonly TriangleMesh _surface=new();
    private readonly List<Part?> _owners=new();
    private readonly Material _hoverMaterial, _selectedMaterial;

    public HangarFittings(ShipDefinition def,Node3D model,Func<HangarView.FittingCategory,int,string> name)
    {
        Part Add(string id,string location,Category category,int socket,Vector3 at,Vector3 half,Node3D? rig=null)
        {
            var part=new Part{Id=id,Location=location,Category=category,Socket=socket,Name=name(category,socket),
                Anchor=at,Region=new(at-half,half*2),Rig=rig};Parts.Add(part);return part;
        }
        Node3D? Node(string id)=>model.FindChild(id,true,false) as Node3D;
        float length=def.Flight.Length;
        foreach(var binding in def.PrimaryMounts)
        {
            int i=Array.IndexOf(def.PrimaryMounts,binding);
            var module=def.Modules.Single(m=>m.Id==binding.Hardpoint.ModuleId);
            var rig=Node("turret_"+module.Id.Replace('-','_'));
            var part=Add("main:"+i,$"주포 {i+1}",Category.MainGun,i,module.Center,module.HalfSize*1.6f,rig);
            if(rig is not null)part.Anchor=rig.GlobalPosition+rig.GlobalBasis.Y*module.HalfSize.Y;
            else
            {
                part.Region=part.Region.Expand(def.Railgun!.Muzzle).Grow(length*.012f);
                part.Anchor=(module.Center+def.Railgun.Muzzle)*.5f;
            }
        }
        for(int i=0;i<(def.PointDefense?.Mounts.Length??0);i++)
        {
            var rig=Node("pd_yaw_"+i);var at=def.PointDefense!.Mounts[i];
            Add("pd:"+i,$"부포 {i+1}",Category.Secondary,i,at,Vector3.One*length*.012f,rig);
        }
        if(def.Missiles is { } missile)
        {
            // Small craft use paired underwing cassettes; larger ships use deck launch cells.
            bool small=def.Kind==HullKind.Interceptor;
            var half=small?new Vector3(length*.14f,length*.05f,length*.10f)
                :new Vector3(Mathf.Max(length*.018f,missile.CellCount*1.25f+1),length*.012f,length*.025f);
            Add("missiles","미사일 모듈",Category.Missiles,0,missile.LaunchPoint,half,Node("MissileCells"));
        }
        var sensor=def.Modules.FirstOrDefault(m=>m.Kind==ModuleKind.Sensor);
        if(sensor is not null)
        {
            bool small=def.Kind==HullKind.Interceptor;
            var half=(sensor.HalfSize*2).Max(new Vector3(length*.016f,length*.015f,length*.02f));
            var part=Add("sensor",small?"센서 어레이":"센서 마스트",Category.Sensor,0,sensor.Center,half);
            // The exposed mast continues above the protected sensor module.
            part.Region=part.Region.Expand(sensor.Center+Vector3.Up*length*(small?.08f:.12f));
            if(small)part.Region=part.Region.Grow(length*.012f);
        }
        foreach(var engine in def.Modules.Where(m=>m.Kind==ModuleKind.Thruster))
        {
            int index=engine.VisualEngineIndex;
            var part=Add("engine:"+engine.Id,$"엔진 {(index>=0?index+1:1)}",Category.Engine,index,
                engine.Center,engine.HalfSize*1.35f);
            if(Node("fx_engine_"+index) is { } nozzle)
            {
                float radius=Mathf.Max(engine.HalfSize.X,engine.HalfSize.Y)*1.5f;
                var rim=new Vector3(radius,radius,length*.005f);
                part.Region=part.Region.Expand(nozzle.GlobalPosition-rim).Expand(nozzle.GlobalPosition+rim);
                part.Anchor=nozzle.GlobalPosition;
            }
        }
        // No collision bodies, simulation data, shared mesh or shared material is modified.
        var faces=new List<Vector3>();
        foreach(var mesh in ImportedShipModels.Meshes(model).Where(m=>m.IsVisibleInTree()&&m.Mesh is ArrayMesh))
        {
            Part? whole=Parts.FirstOrDefault(p=>p.Rig is not null&&(p.Rig==mesh||p.Rig.IsAncestorOf(mesh)));
            var points=mesh.Mesh.GetFaces();var transform=mesh.GlobalTransform;
            for(int i=0;i+2<points.Length;i+=3)
            {
                var a=transform*points[i];var b=transform*points[i+1];var c=transform*points[i+2];
                var center=(a+b+c)/3;
                var owner=whole??Parts.FirstOrDefault(p=>p.Rig is null&&p.Region.HasPoint(center));
                // Single-cell launchers are baked into FleetHull; a replacement cell rig can coexist with them.
                owner??=Parts.FirstOrDefault(p=>p.Category==Category.Missiles&&p.Region.HasPoint(center));
                faces.Add(a);faces.Add(b);faces.Add(c);_owners.Add(owner);
                if(owner is not null){owner.Faces.Add(a);owner.Faces.Add(b);owner.Faces.Add(c);}
            }
        }
        _surface.CreateFromFaces(faces.ToArray());
        var shader=new Shader{Code="shader_type spatial; render_mode unshaded, blend_mix, cull_disabled, depth_draw_never; uniform vec4 tint : source_color; uniform float lift; void vertex(){ vec4 p=MODELVIEW_MATRIX*vec4(VERTEX,1.0); p.xyz-=normalize(p.xyz)*lift; POSITION=PROJECTION_MATRIX*p; } void fragment(){ ALBEDO=tint.rgb; ALPHA=tint.a; }"};
        Material Material(Color color){var m=new ShaderMaterial{Shader=shader};m.SetShaderParameter("tint",color);m.SetShaderParameter("lift",length*.0001f);return m;}
        _hoverMaterial=Material(new(.25f,.82f,1,.48f));_selectedMaterial=Material(new(.3f,.7f,.9f,.23f));
        foreach(var part in Parts)
        {
            if(part.Faces.Count==0)continue;
            var centers=new List<Vector3>();
            for(int i=0;i<part.Faces.Count;i+=3)centers.Add((part.Faces[i]+part.Faces[i+1]+part.Faces[i+2])/3);
            // Near the authored anchor, plus extremes for rear/ventral views. Cached; ray queries use Godot's BVH.
            var samples=centers.OrderBy(v=>v.DistanceSquaredTo(part.Anchor)).Take(12).ToList();
            for(int axis=0;axis<3;axis++)
            {int a=axis;samples.AddRange(centers.OrderBy(v=>v[a]).Take(3));samples.AddRange(centers.OrderByDescending(v=>v[a]).Take(3));}
            part.Samples=samples.Distinct().ToArray();
            var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex]=part.Faces.ToArray();
            var geometry=new ArrayMesh();geometry.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);
            part.Highlight=new MeshInstance3D{Name="FittingHighlight",Mesh=geometry,MaterialOverride=_hoverMaterial,
                Visible=false,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
            // Preview ship is at identity; all vertices above are in this isolated world's coordinates.
            model.GetParent().AddChild(part.Highlight);
            part.Faces.Clear();part.Faces.TrimExcess();
        }
    }

    internal Part? Hit(Vector3 origin,Vector3 direction)
    {
        var hit=_surface.IntersectRay(origin,direction);
        return hit.Count==0?null:_owners[hit["face_index"].AsInt32()];
    }
    internal void Project(Camera3D camera,Vector2 viewportSize,Vector2 controlSize)
    {
        foreach(var part in Parts)
        {
            part.Screen=null;
            foreach(var candidate in part.Samples)
            {
                if(camera.IsPositionBehind(candidate))continue;
                var point=camera.UnprojectPosition(candidate)/viewportSize*controlSize;
                if(!new Rect2(Vector2.One*12,controlSize-Vector2.One*24).HasPoint(point))continue;
                if(Hit(camera.GlobalPosition,(candidate-camera.GlobalPosition).Normalized())!=part)continue;
                part.Screen=point;break;
            }
        }
    }
    internal void Emphasize(Part? hover,string? selected)
    {
        foreach(var part in Parts)if(part.Highlight is { } mesh)
        {mesh.Visible=part==hover||part.Id==selected;mesh.MaterialOverride=part==hover?_hoverMaterial:_selectedMaterial;}
    }
    public void Dispose()=>_surface.Dispose();
}
