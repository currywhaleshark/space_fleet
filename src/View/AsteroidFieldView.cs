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
                Layers=BattleLook.AsteroidLayer,
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
    public static ShaderMaterial RockMaterial()=>new(){Shader=GD.Load<Shader>("res://shaders/asteroid.gdshader")};
}
