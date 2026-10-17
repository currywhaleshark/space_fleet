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
        if(def.Battery?.Weapon.Laser is not null) { ApplyLasers(hull,def,palette); return; }
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

    private static void ApplyLasers(Node3D hull,ShipDefinition def,Palette palette)
    {
        foreach(var binding in def.PrimaryMounts)
        {
            Node3D parent; Vector3 muzzle;
            if(binding.Turret is { } turret)
            {
                string key=turret.ModuleId.Replace('-','_');
                parent=hull.FindChild("recoil_"+key,true,false) as Node3D
                    ??throw new InvalidOperationException("Missing laser socket: "+key);
                foreach(Node old in parent.GetChildren()){parent.RemoveChild(old);old.Free();}
                muzzle=turret.Muzzles[0];
                parent.AddChild(new Node3D{Name=$"muzzle_{key}_0",Position=muzzle});
            }
            else
            {
                var module=def.Modules.Single(m=>m.Id==binding.Hardpoint.ModuleId);
                parent=new Node3D{Name="ReplacementFixedReceiver",Position=module.Center};hull.AddChild(parent);
                muzzle=def.Railgun!.Muzzle-module.Center;
            }
            float length=Mathf.Max(.3f,-muzzle.Z), width=length*.075f;
            var body=new SurfaceTool();body.Begin(Mesh.PrimitiveType.Triangles);
            // Optical shroud ends at the physical muzzle. Collars visibly distinguish it from exposed rail pairs.
            MeshKit.AddTaper(body,new(muzzle.X,muzzle.Y,width),new(width*2.6f,width*1.8f),
                muzzle,new(width*1.8f,width*1.8f));
            for(int i=0;i<4;i++)
            {
                float z=-length*(.15f+i*.2f);
                MeshKit.AddTaper(body,new(muzzle.X,muzzle.Y,z+width*.15f),new(width*3,width*2.2f),
                    new(muzzle.X,muzzle.Y,z-width*.15f),new(width*3,width*2.2f));
            }
            var mesh=body.Commit();mesh.SurfaceSetMaterial(0,palette.Hull);
            parent.AddChild(new MeshInstance3D{Name="ReplacementLaserEmitter",Mesh=mesh});
            var lens=new StandardMaterial3D{AlbedoColor=new(.04f,.22f,.3f),Metallic=.6f,Roughness=.18f,
                EmissionEnabled=true,Emission=new(.12f,.65f,.85f),EmissionEnergyMultiplier=.6f};
            parent.AddChild(new MeshInstance3D{Name="LaserOptic",Position=muzzle,RotationDegrees=new(90,0,0),
                Mesh=new CylinderMesh{TopRadius=width*.65f,BottomRadius=width*.65f,Height=width*.03f,RadialSegments=16,Material=lens}});
        }
    }
}
