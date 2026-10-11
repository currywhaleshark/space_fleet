using System;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.View;

/// <summary>Draws exactly the simulation primitives, in obstacle-local coordinates.</summary>
public static class ObstacleWireframe
{
    public static MeshInstance3D Create(Obstacle obstacle)
    {
        var material=new StandardMaterial3D { ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor=new Color(.2f,1f,.65f,.75f), Transparency=BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest=true };
        var mesh=new ImmediateMesh(); mesh.SurfaceBegin(Mesh.PrimitiveType.Lines,material);
        void Line(Vector3 a,Vector3 b) { mesh.SurfaceAddVertex(a); mesh.SurfaceAddVertex(b); }
        void Circle(Vector3 center,Vector3 u,Vector3 v,float radius)
        {
            for(int i=0;i<48;i++) { float a=i*Mathf.Tau/48,b=(i+1)*Mathf.Tau/48;
                Line(center+(u*Mathf.Cos(a)+v*Mathf.Sin(a))*radius,center+(u*Mathf.Cos(b)+v*Mathf.Sin(b))*radius); }
        }
        foreach(var part in obstacle.Parts)
        {
            Vector3 a=part.A.ToVector3(),b=part.B.ToVector3(); float r=(float)part.Radius;
            foreach(var center in part.A == part.B ? new[]{a} : new[]{a,b})
            { Circle(center,Vector3.Right,Vector3.Up,r); Circle(center,Vector3.Right,Vector3.Back,r); Circle(center,Vector3.Up,Vector3.Back,r); }
            if(a==b) continue;
            Vector3 axis=(b-a).Normalized(),u=axis.Cross(Mathf.Abs(axis.Y)>.9f?Vector3.Right:Vector3.Up).Normalized(),v=axis.Cross(u);
            Circle(a,u,v,r); Circle(b,u,v,r);
            foreach(var n in new[]{u,-u,v,-v}) Line(a+n*r,b+n*r);
        }
        mesh.SurfaceEnd();
        return new MeshInstance3D { Name="ObstacleGeometry", Mesh=mesh, Quaternion=obstacle.Orientation,
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off };
    }
}
