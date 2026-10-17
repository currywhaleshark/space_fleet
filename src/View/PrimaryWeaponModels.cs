using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.View;

/// <summary>Replace only the moving primary barrel group; stock Blender housings and other systems stay intact.</summary>
public static class PrimaryWeaponModels
{
    public static void Apply(Node3D hull,ShipDefinition def,Palette palette)
    {
        if(def.Components?.MainGun is not { } id)return;
        if(def.Railgun?.Mounts is not { } mounts)
        {
            // Fixed guns are part of the imported hull mesh. Their replacement acceleration
            // pack fits over the existing receiver and does not move its muzzle or damage module.
            var gun=def.Modules.Single(m=>m.Id==def.Railgun!.ModuleId);
            float length=Mathf.Min(gun.HalfSize.Z*2,Mathf.Abs(def.Railgun!.Muzzle.Z-gun.Center.Z));
            float width=Mathf.Max(gun.HalfSize.X*3,.28f);
            var st=new SurfaceTool();st.Begin(Mesh.PrimitiveType.Triangles);
            MeshKit.AddTaper(st,new(0,0,gun.Center.Z+length*.4f),new(width*1.5f,width),
                new(0,0,gun.Center.Z-length*.6f),new(width*1.3f,width));
            int rings=id=="main-precision"?5:3;
            for(int i=0;i<rings;i++)
            {
                float z=gun.Center.Z+length*.35f-i*length/(rings+1);
                MeshKit.AddTaper(st,new(0,0,z+.06f*length),new(width*2,width*1.5f),
                    new(0,0,z-.06f*length),new(width*2,width*1.5f));
            }
            var mesh=st.Commit();mesh.SurfaceSetMaterial(0,id=="main-precision"?palette.Hull:palette.Dark);
            hull.AddChild(new MeshInstance3D{Name="ReplacementFixedReceiver",Position=new(gun.Center.X,gun.Center.Y,0),Mesh=mesh});
            return;
        }
        foreach(var mount in mounts)
        {
            string key=mount.ModuleId.Replace('-','_');
            var recoil=hull.FindChild("recoil_"+key,true,false) as Node3D
                ??throw new InvalidOperationException("Missing primary recoil socket: "+key);
            foreach(Node old in recoil.GetChildren()){recoil.RemoveChild(old);old.Free();}
            var body=new SurfaceTool();body.Begin(Mesh.PrimitiveType.Triangles);
            var rails=new SurfaceTool();rails.Begin(Mesh.PrimitiveType.Triangles);
            for(int i=0;i<mount.Muzzles.Length;i++)
            {
                Vector3 muzzle=mount.Muzzles[i];float length=-muzzle.Z;
                float w=length*.035f*(mount.Muzzles.Length==1?1.25f:.85f);
                Vector3 At(float z,float x=0,float y=0)=>new(muzzle.X+x,muzzle.Y+y,z);
                MeshKit.AddTaper(body,At(w),new(w*3,w*2),At(-length*.22f),new(w*2,w*1.6f));
                // Two exposed electromagnetic rails leave a dark open muzzle at the physical socket.
                foreach(float side in new[]{-1f,1f})
                    MeshKit.AddTaper(rails,At(-length*.15f,side*w*.65f),new(w*.65f,w*.9f),
                        At(muzzle.Z,side*w*.65f),new(w*.55f,w*.8f));
                for(int ring=0;ring<5;ring++)
                {
                    float z=-length*(.28f+ring*.13f);
                    MeshKit.AddTaper(body,At(z+w*.25f),new(w*2,w*1.45f),At(z-w*.25f),new(w*2,w*1.45f));
                }
                recoil.AddChild(new Node3D{Name=$"muzzle_{key}_{i}",Position=muzzle});
            }
            var mesh=body.Commit();rails.Commit(mesh);
            mesh.SurfaceSetMaterial(0,palette.Hull);mesh.SurfaceSetMaterial(1,palette.Dark);
            recoil.AddChild(new MeshInstance3D{Name="ReplacementPrimaryBarrels",Mesh=mesh});
        }
    }
}
