using System;
using System.Collections.Generic;
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
        if(def.Battery?.SocketWeapons is not null) { ApplyParts(hull,def,palette); return; }
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

    /// <summary>A fixed-axis part (e.g. the medium axial gun after 포좌 확장): a kit receiver over the hull's own gun, ending at its muzzle.</summary>
    private static void FixedReceiver(Node3D hull,ShipDefinition def,ResolvedWeaponMount binding,MainGunPart part,Palette palette)
    {
        if(!WeaponKit.Available)return;
        var gun=def.Modules.Single(m=>m.Id==binding.Hardpoint.ModuleId);var muzzle=def.Railgun!.Muzzle;
        // Small craft carry the axial gun inside the nose; the receiver is a chin pod wide enough to read from outside.
        float w=MathF.Max(gun.HalfSize.X*2.4f,def.Flight.Length*.06f)*Mathf.Pow(WeaponLooks.CaliberRatio(def,binding.Weapon),.8f);
        var armour=new SurfaceTool();armour.Begin(Mesh.PrimitiveType.Triangles);
        var dark=new SurfaceTool();dark.Begin(Mesh.PrimitiveType.Triangles);
        var tools=new Dictionary<WeaponKit.Slot,SurfaceTool>{[WeaponKit.Slot.Armour]=armour,[WeaponKit.Slot.Dark]=dark,[WeaponKit.Slot.Glow]=dark};
        WeaponKit.Append(tools,"kit_axial_receiver",WeaponKit.Span(muzzle.X,muzzle.Y-w*.25f,gun.Center.Z+gun.HalfSize.Z,muzzle.Z+w*.2f,w,w*.9f));
        var mesh=armour.Commit();dark.Commit(mesh);
        mesh.SurfaceSetMaterial(0,palette.Hull);mesh.SurfaceSetMaterial(1,palette.Dark);
        hull.AddChild(new MeshInstance3D{Name="PartFixedReceiver",Mesh=mesh});
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
            float length=Mathf.Max(.3f,-muzzle.Z), width=length*.075f*(binding.Weapon.Size>=WeaponSize.Large&&def.Battery?.SocketWeapons is not null?1.35f:1);
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

    /// <summary>Insulator rings along the rails: more for bigger calibers (§10).</summary>
    public static int RingCount(float caliberMm) => caliberMm switch { <= 110 => 3, <= 180 => 5, <= 270 => 7, _ => 9 };

    /// <summary>
    /// Socket parts (§10): only sockets carrying a non-stock part get procedural barrels; stock sockets keep the
    /// imported Blender guns. Caliber sets barrel thickness and ring count, the faction sets the style, and the
    /// muzzles stay exactly where the data fires from.
    /// </summary>
    private static void ApplyParts(Node3D hull,ShipDefinition def,Palette palette)
    {
        var stock=MainGunParts.StockFor(def);
        Material? glow=null, armour=null;
        foreach(var binding in def.PrimaryMounts)
        {
            if(binding.Weapon.Id==stock.Id||MainGunParts.Find(binding.Weapon.Id) is not { } part)continue;
            if(binding.Turret is not { } mount){FixedReceiver(hull,def,binding,part,palette);continue;}
            string key=mount.ModuleId.Replace('-','_');
            var recoil=hull.FindChild("recoil_"+key,true,false) as Node3D
                ??throw new InvalidOperationException("Missing primary recoil socket: "+key);
            foreach(Node old in recoil.GetChildren()){recoil.RemoveChild(old);old.Free();}
            bool mars=part.Design==DesignFamily.Mars;
            float ratio=WeaponLooks.CaliberRatio(def,binding.Weapon);
            var muzzles=mount.Muzzles;
            float length=-muzzles.Average(m=>m.Z);
            float w=length*.03f*Mathf.Pow(ratio,.8f)*(mars?.8f:1);
            if(muzzles.Length>1)w=Mathf.Min(w,Mathf.Abs(muzzles[1].X-muzzles[0].X)/2.5f);
            // body = light armour (jackets, rails), rails = dark (insulator collars, slot, mantlet face), fins = Mars glow.
            var body=new SurfaceTool();body.Begin(Mesh.PrimitiveType.Triangles);
            var rails=new SurfaceTool();rails.Begin(Mesh.PrimitiveType.Triangles);
            var fins=new SurfaceTool();fins.Begin(Mesh.PrimitiveType.Triangles);
            float minX=muzzles.Min(m=>m.X),maxX=muzzles.Max(m=>m.X),y=muzzles.Average(m=>m.Y),cx=(minX+maxX)*.5f;
            float jacket=mars?.1f:part.Size==WeaponSize.ExtraLarge?.5f:.36f;
            int rings=Math.Clamp(Math.Min(RingCount(part.CaliberMm),(int)(length*(.9f-jacket)/(w*3.2f))),2,9);
            bool shared=muzzles.Length>1;
            if(shared)rings=Math.Min(rings,4);
            if(WeaponKit.Available)
            {
                // Blender kit (tools/build-weapon-kit.py): same layout as the fallback boxes, bevelled and round.
                var tools=new Dictionary<WeaponKit.Slot,SurfaceTool>{[WeaponKit.Slot.Armour]=body,[WeaponKit.Slot.Dark]=rails,[WeaponKit.Slot.Glow]=fins};
                float span=maxX-minX;
                if(!mars)WeaponKit.Append(tools,"kit_mantlet_earth",WeaponKit.Span(cx,y,w*1.2f,-length*.1f,span+w*2.8f,w*2.8f));
                if(shared)
                    for(int ring=0;ring<rings;ring++)
                    {
                        float z=-length*(jacket+.08f+ring*(.85f-jacket)/rings);
                        WeaponKit.Append(tools,"kit_collar",WeaponKit.Span(cx,y,z+w*.3f,z-w*.3f,span+w*2.1f,w*2.1f));
                    }
                for(int i=0;i<muzzles.Length;i++)
                {
                    var m=muzzles[i];
                    WeaponKit.Append(tools,"kit_jacket",WeaponKit.Span(m.X,m.Y,-length*.08f,-length*jacket,w*2.2f,w*2.2f));
                    WeaponKit.Append(tools,mars?"kit_rail_mars":"kit_rail_earth",WeaponKit.Span(m.X,m.Y,-length*jacket,m.Z,w*1.7f,w*1.7f));
                    for(int ring=0;ring<rings;ring++)
                    {
                        float z=-length*(jacket+.05f+ring*(.9f-jacket)/rings);
                        if(!shared)WeaponKit.Append(tools,"kit_collar",WeaponKit.Span(m.X,m.Y,z+w*.18f,z-w*.18f,w*2.05f,w*2.05f));
                        if(mars&&ring%2==0)WeaponKit.Append(tools,"kit_fin_mars",WeaponKit.Span(m.X,m.Y+w*.6f,z+w*.35f,z-w*.35f,w*.9f,w*1.1f));
                    }
                    recoil.AddChild(new Node3D{Name=$"muzzle_{key}_{i}",Position=m});
                }
            }
            else
            {
                if(!mars)
                {
                    // Earth: one angular mantlet across the whole mount, then short thick jackets.
                    float half=(maxX-minX)*.5f+w*1.4f;
                    MeshKit.AddTaper(body,new(cx,y,w*1.2f),new(half*2,w*2.8f),new(cx,y,-length*.1f),new(half*1.85f,w*2.5f));
                    MeshKit.AddTaper(rails,new(cx,y,-length*.1f),new(half*1.85f,w*2.5f),new(cx,y,-length*.1f-w*.3f),new(half*1.7f,w*2.2f));
                }
                // Multi-barrel mounts share clamp bands across all barrels (fewer, wider) instead of a grid of collars.
                if(shared)
                {
                    for(int ring=0;ring<rings;ring++)
                    {
                        float z=-length*(jacket+.08f+ring*(.85f-jacket)/rings);
                        MeshKit.AddTaper(rails,new(cx,y,z+w*.3f),new(maxX-minX+w*2.1f,w*2.1f),new(cx,y,z-w*.3f),new(maxX-minX+w*2.1f,w*2.1f));
                    }
                }
                for(int i=0;i<muzzles.Length;i++)
                {
                    Vector3 muzzle=muzzles[i];
                    Vector3 At(float z,float x=0,float yy=0)=>new(muzzle.X+x,muzzle.Y+yy,z);
                    MeshKit.AddTaper(body,At(-length*.08f),new(w*2.3f,w*2.3f),At(-length*jacket),new(w*2.1f,w*2.1f));
                    if(mars)
                        // Mars: open frame — two thin side beams instead of a closed housing.
                        foreach(float side in new[]{-1f,1f})
                            MeshKit.AddTaper(rails,At(w,side*w*1.35f,-w*.5f),new(w*.35f,w*.5f),At(-length*.6f,side*w*1.35f,-w*.5f),new(w*.25f,w*.4f));
                    // Two heavy rails with a dark slot between them run to the physical muzzle.
                    foreach(float side in new[]{-1f,1f})
                        MeshKit.AddTaper(body,At(-length*jacket,side*w*.55f),new(w*.8f,w*1.7f),At(muzzle.Z,side*w*.55f),new(w*.7f,w*1.5f));
                    MeshKit.AddTaper(rails,At(-length*jacket,0,-w*.1f),new(w*.35f,w*1.2f),At(muzzle.Z+w*.2f,0,-w*.1f),new(w*.3f,w*1.1f));
                    for(int ring=0;ring<rings;ring++)
                    {
                        float z=-length*(jacket+.05f+ring*(.9f-jacket)/rings);
                        if(!shared)MeshKit.AddTaper(rails,At(z+w*.18f),new(w*2.05f,w*2.05f),At(z-w*.18f),new(w*2.05f,w*2.05f));
                        // Mars heat sinks: thin glowing plates standing on top of the rails.
                        if(mars&&ring%2==0)
                            MeshKit.AddTaper(fins,At(z+w*.35f,0,w*1.1f),new(w*.15f,w*1.0f),At(z-w*.35f,0,w*1.1f),new(w*.15f,w*1.0f));
                    }
                    recoil.AddChild(new Node3D{Name=$"muzzle_{key}_{i}",Position=muzzle});
                }
            }
            var mesh=body.Commit();rails.Commit(mesh);
            armour??=new StandardMaterial3D{AlbedoColor=palette.Hull.AlbedoColor*.62f,Metallic=palette.Hull.Metallic,Roughness=.55f};
            mesh.SurfaceSetMaterial(0,armour);mesh.SurfaceSetMaterial(1,palette.Dark);
            if(mars)
            {
                glow??=new StandardMaterial3D{AlbedoColor=new(.3f,.06f,.03f),Metallic=.4f,Roughness=.5f,
                    EmissionEnabled=true,Emission=new(.95f,.24f,.07f),EmissionEnergyMultiplier=.9f};
                fins.Commit(mesh);mesh.SurfaceSetMaterial(2,glow);
            }
            recoil.AddChild(new MeshInstance3D{Name="PartPrimaryBarrels",Mesh=mesh});
        }
    }
}
