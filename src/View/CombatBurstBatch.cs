using System;
using System.Collections.Generic;
using Godot;

namespace SpaceFleet.View;

/// <summary>Bounded, reusable flashes and ballistic spark ribbons; absolute ages allow seeking.</summary>
internal sealed class CombatBurstBatch
{
    private sealed record Flash(MeshInstance3D Node,ShaderMaterial Material);
    private readonly Node3D _owner;
    private readonly List<Flash> _flashes=new();
    private readonly List<MeshInstance3D> _sparks=new();
    private readonly List<OmniLight3D> _lights=new();
    private readonly ShaderMaterial _warm=CombatFx.Glow(new Color(1,.43f,.09f),true);
    private readonly ShaderMaterial _cold=CombatFx.Glow(new Color(.45f,.8f,1),true);
    private static Shader? _shader;
    private int _f,_s,_l;
    public int VisibleFlashes=>_f;
    public int VisibleSparks=>_s;
    public int PoolSize=>_flashes.Count+_sparks.Count;
    public CombatBurstBatch(Node3D owner) => _owner=owner;
    public void Begin() { _f=_s=_l=0; }

    public void Draw(Camera3D camera,Vector3 at,Vector3 particleOrigin,Vector3 normal,float age,
        float size,uint seed,int particles,bool electric=false,bool distantFlash=false,float duration=1.25f)
    {
        if(age<0 || age>=duration) return;
        float distance=at.DistanceTo(camera.GlobalPosition);
        if(!distantFlash && distance>camera.Far*.9f) return;
        bool close=distance<Mathf.Max(500,size*160);
        float flashLife=electric?.18f:.42f;
        if(age<flashLife && _f<96)
        {
            if(_f==_flashes.Count)
            {
                var mat=new ShaderMaterial { Shader=_shader??=GD.Load<Shader>("res://shaders/combat_burst.gdshader") };
                var node=CombatFx.Make(_owner,CombatFx.Quad,mat); node.TopLevel=true;
                _flashes.Add(new(node,mat));
            }
            var flash=_flashes[_f++];
            flash.Material.SetShaderParameter("age",age/flashLife);
            flash.Material.SetShaderParameter("seed",(seed%997)*.17f);
            flash.Material.SetShaderParameter("electric",electric?1f:0f);
            flash.Material.SetShaderParameter("tint",electric?new Color(.3f,.65f,1):new Color(1,.39f,.07f));
            CombatFx.Flare(flash.Node,camera,at,size*(1+age*1.4f),distantFlash?85:electric?48:28);
        }
        if(close && age<.18f && _l<4)
        {
            if(_l==_lights.Count)
            {
                var light=new OmniLight3D { ShadowEnabled=false,TopLevel=true }; _owner.AddChild(light); _lights.Add(light);
            }
            var node=_lights[_l++]; node.GlobalPosition=at; node.Visible=true;
            node.LightColor=electric?new Color(.35f,.7f,1):new Color(1,.4f,.12f);
            node.LightEnergy=5*Mathf.Exp(-age*22); node.OmniRange=Mathf.Clamp(size*2,8,300);
        }
        if(!close || age<.005f) return;
        float fade=Mathf.Clamp((duration-age)/.5f,0,1);
        for(int j=0;j<particles && _s<512;j++)
        {
            if(_s==_sparks.Count)
            { var node=CombatFx.Make(_owner,CombatFx.Quad,_warm); node.TopLevel=true; _sparks.Add(node); }
            var spark=_sparks[_s++]; spark.MaterialOverride=electric?_cold:_warm;
            Vector3 direction=CarbonCombatFx.Direction(seed,j,normal,electric?.85f:2.5f);
            float speed=Mathf.Max(10,size)*(1.5f+(j%7)*.55f);
            Vector3 head=particleOrigin+direction*speed*age;
            Vector3 tail=particleOrigin+direction*speed*Mathf.Max(0,age-(electric?.018f:.045f));
            float width=Mathf.Max(size*.006f,CombatFx.PixelSize(camera,head,1.2f))*fade;
            CombatFx.Streak(spark,camera,head,tail,width);
        }
    }
    public void End()
    {
        for(int i=_f;i<_flashes.Count;i++) _flashes[i].Node.Visible=false;
        for(int i=_s;i<_sparks.Count;i++) _sparks[i].Visible=false;
        for(int i=_l;i<_lights.Count;i++) _lights[i].Visible=false;
    }
}
