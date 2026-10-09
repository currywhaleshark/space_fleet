using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SpaceFleet.View;

/// <summary>Project visual impacts onto authored armor without changing the simulation hit.</summary>
internal sealed class VisualHullSurface
{
    private readonly ShipView _ship;
    private readonly MeshInstance3D[] _meshes;
    private readonly Dictionary<Mesh,(Vector3[] Points,int[] Indices)[]> _geometry=new();
    public VisualHullSurface(ShipView ship,Node3D model)
    { _ship=ship; _meshes=ImportedShipModels.Meshes(model).Where(m=>m.Mesh is ArrayMesh).ToArray(); }

    public (Vector3 Point,Vector3 Normal) Project(Vector3 point,Vector3 incoming)
    {
        if(incoming.LengthSquared()<.001f) return (point,Vector3.Up);
        Vector3 dir=incoming.Normalized(); float reach=_ship.Body.Class.Length*1.5f;
        Vector3 start=point-dir*reach;
        float best=reach*2; bool found=false; Vector3 normal=-dir;
        Transform3D worldToShip=_ship.GlobalTransform.AffineInverse();
        foreach(var mesh in _meshes)
        {
            Transform3D pose=worldToShip*mesh.GlobalTransform, inverse=pose.AffineInverse();
            Vector3 from=inverse*start,ray=inverse.Basis*dir;
            if(!BoxHit(mesh.GetAabb(),from,ray,best)) continue;
            if(!_geometry.TryGetValue(mesh.Mesh,out var surfaces))
            {
                surfaces=new (Vector3[],int[])[mesh.Mesh.GetSurfaceCount()];
                for(int s=0;s<surfaces.Length;s++)
                {
                    var data=mesh.Mesh.SurfaceGetArrays(s);
                    surfaces[s]=(data[(int)Mesh.ArrayType.Vertex].AsVector3Array(),
                        data[(int)Mesh.ArrayType.Index].VariantType==Variant.Type.Nil?Array.Empty<int>():data[(int)Mesh.ArrayType.Index].AsInt32Array());
                }
                _geometry.Add(mesh.Mesh,surfaces);
            }
            foreach(var (vertices,indices) in surfaces)
            {
                int count=indices.Length>0?indices.Length:vertices.Length;
                for(int i=0;i+2<count;i+=3)
                {
                    Vector3 a=vertices[indices.Length>0?indices[i]:i];
                    Vector3 b=vertices[indices.Length>0?indices[i+1]:i+1];
                    Vector3 c=vertices[indices.Length>0?indices[i+2]:i+2];
                    Vector3 e1=b-a,e2=c-a,h=ray.Cross(e2); float determinant=e1.Dot(h);
                    if(Mathf.Abs(determinant)<1e-8f) continue;
                    float inv=1/determinant; Vector3 offset=from-a;
                    float u=offset.Dot(h)*inv; if(u<0 || u>1) continue;
                    Vector3 q=offset.Cross(e1); float v=ray.Dot(q)*inv;
                    if(v<0 || u+v>1) continue;
                    float distance=e2.Dot(q)*inv;
                    if(distance>=0 && distance<best) {
                        best=distance; found=true;
                        normal=(pose.Basis.Inverse().Transposed()*e1.Cross(e2)).Normalized();
                        if(normal.Dot(dir)>0) normal=-normal;
                    }
                }
            }
        }
        return (found?start+dir*(best-Mathf.Max(.03f,_ship.Body.Class.Length*.0008f)):point,normal);
    }
    private static bool BoxHit(Aabb box,Vector3 from,Vector3 ray,float far)
    {
        float near=0;
        for(int axis=0;axis<3;axis++)
        {
            float lo=box.Position[axis],hi=box.End[axis];
            if(Mathf.Abs(ray[axis])<1e-8f) { if(from[axis]<lo || from[axis]>hi) return false; continue; }
            float a=(lo-from[axis])/ray[axis],b=(hi-from[axis])/ray[axis];
            near=Mathf.Max(near,Mathf.Min(a,b)); far=Mathf.Min(far,Mathf.Max(a,b));
            if(near>far) return false;
        }
        return true;
    }
}
