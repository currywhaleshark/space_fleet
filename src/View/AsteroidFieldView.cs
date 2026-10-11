using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.View;

/// <summary>Twelve shared mesh variants; coordinates remain local to the field's double origin.</summary>
public partial class AsteroidFieldView : Node3D
{
    private AsteroidField _field=null!;
    public static AsteroidFieldView Create(AsteroidField field)=>new(){Name="AsteroidCluster",_field=field};
    public override void _Ready()
    {
        var material=RockMaterial();
        for(int variant=0;variant<AsteroidField.VariantCount;variant++)
        {
            var rocks=_field.Rocks.Where(r=>r.Variant==variant).ToArray();
            var multi=new MultiMesh { TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,
                Mesh=BuildMesh(variant),InstanceCount=rocks.Length };
            for(int i=0;i<rocks.Length;i++)
            {
                var rock=rocks[i]; var basis=new Basis(rock.Body.Orientation).Scaled(Vector3.One*(float)rock.Radius);
                multi.SetInstanceTransform(i,new(basis,(rock.Body.Position-_field.Center).ToVector3()));
            }
            AddChild(new MultiMeshInstance3D {Name=$"RockVariant{variant}",Multimesh=multi,MaterialOverride=material,
                CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        }
        if(Array.IndexOf(OS.GetCmdlineUserArgs(),"--show-obstacles")>=0)
            foreach(var rock in _field.Rocks)
            { var wire=ObstacleWireframe.Create(rock.Body);wire.Position=(rock.Body.Position-_field.Center).ToVector3();AddChild(wire); }
    }
    public static ArrayMesh BuildMesh(int variant)
    {
        const int rings=32,sides=64;
        var vertices=new Vector3[(rings+1)*(sides+1)];
        for(int y=0;y<=rings;y++)for(int x=0;x<=sides;x++)
        {
            double a=x*Math.Tau/sides,b=y*Math.PI/rings;
            Vec3d direction=new(Math.Sin(b)*Math.Cos(a),Math.Cos(b),Math.Sin(b)*Math.Sin(a));
            int i=y*(sides+1)+x;vertices[i]=AsteroidField.SurfaceVertex(variant,direction).ToVector3();
        }
        var indices=new List<int>();
        void Triangle(int a,int b,int c)
        {
            // Godot front faces are clockwise; supplied normals point outwards.
            if((vertices[b]-vertices[a]).Cross(vertices[c]-vertices[a]).Dot(vertices[a]+vertices[b]+vertices[c])>0)(b,c)=(c,b);
            indices.Add(a);indices.Add(b);indices.Add(c);
        }
        for(int y=0;y<rings;y++)for(int x=0;x<sides;x++)
        {int a=y*(sides+1)+x,b=a+sides+1; if(y>0)Triangle(a,a+1,b);if(y<rings-1)Triangle(a+1,b+1,b);}
        // Flat geometric normals emphasize rock facets without moving the surface.
        var flatVertices=new Vector3[indices.Count];var flatNormals=new Vector3[indices.Count];
        for(int i=0;i<indices.Count;i+=3)
        {
            var a=vertices[indices[i]];var b=vertices[indices[i+1]];var c=vertices[indices[i+2]];
            var normal=-(b-a).Cross(c-a).Normalized();
            flatVertices[i]=a;flatVertices[i+1]=b;flatVertices[i+2]=c;
            flatNormals[i]=flatNormals[i+1]=flatNormals[i+2]=normal;
        }
        var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex]=flatVertices;arrays[(int)Mesh.ArrayType.Normal]=flatNormals;
        var mesh=new ArrayMesh();mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);return mesh;
    }
    public void Sync(Vec3d origin)=>Position=(_field.Center-origin).ToVector3();
    public static ShaderMaterial RockMaterial()=>new(){Shader=new Shader {Code="""
        shader_type spatial;
        render_mode diffuse_burley, specular_disabled;
        varying vec3 rock_pos;
        float hash(vec3 p) { return fract(sin(dot(p,vec3(127.1,311.7,74.7)))*43758.5453); }
        float noise(vec3 p) {
            vec3 i=floor(p),f=fract(p);f=f*f*(3.0-2.0*f);
            return mix(mix(mix(hash(i),hash(i+vec3(1,0,0)),f.x),mix(hash(i+vec3(0,1,0)),hash(i+vec3(1,1,0)),f.x),f.y),
                mix(mix(hash(i+vec3(0,0,1)),hash(i+vec3(1,0,1)),f.x),mix(hash(i+vec3(0,1,1)),hash(i+vec3(1,1,1)),f.x),f.y),f.z);
        }
        void vertex() { rock_pos=VERTEX; }
        void fragment() {
            float grain=noise(rock_pos*14.0)*0.50+noise(rock_pos*43.0)*0.30+noise(rock_pos*131.0)*0.20;
            float seam=smoothstep(0.08,0.19,abs(noise(rock_pos*8.0)-0.5));
            ALBEDO=mix(vec3(0.105,0.113,0.12),vec3(0.32,0.305,0.28),grain)*(0.70+0.30*seam);
            ROUGHNESS=1.0;RIM=0.16;RIM_TINT=0.15;
        }
        """}};
}
