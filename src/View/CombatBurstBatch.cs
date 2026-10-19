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
    /// <summary>Flashes drawn this frame in the crisp point form.</summary>
    public int PointFlashes { get; private set; }
    public int VisibleSparks=>_s;
    public int PoolSize=>_flashes.Count+_sparks.Count;
    public CombatBurstBatch(Node3D owner) => _owner=owner;
    public void Begin() { _f=_s=_l=PointFlashes=0; }
    /// <summary>Destruction flashes smaller than this on screen switch to the crisp point form.</summary>
    public const float PointFlashPixels=40;
    /// <summary>Screen size of the point-form quad; its core is about 3 px and the ring reaches about 20 px.</summary>
    public const float PointFlashQuad=64;

    public void Draw(Camera3D camera,Vector3 at,Vector3 particleOrigin,Vector3 normal,float age,
        float size,uint seed,int particles,bool electric=false,bool distantFlash=false,float duration=1.25f)
    {
        if(age<0 || age>=duration) return;
        float distance=at.DistanceTo(camera.GlobalPosition);
        if(!distantFlash && distance>camera.Far*.9f) return;
        bool close=distance<Mathf.Max(500,size*160);
        // 화면 크기 하한(포구 48px·착탄 28px)은 가까울 때만: 2 km 밖부터 거리에 반비례해 줄인다(먼 포격이 크게 번쩍이지 않게).
        float near=Mathf.Clamp(2000f/Mathf.Max(1f,distance),.15f,1f);
        float grown=size*(1+age*1.4f);
        // 격침 섬광이 화면에서 작으면 흐린 불덩이를 키우지 않고 작고 선명한 점광(심·십자 광선·고리)으로 그린다.
        // 작게 보이는 대신 조금 더 오래(전기 0.4초, 주 폭발 0.9초) 남겨 멀리서도 놓치지 않게 한다.
        bool point=distantFlash && grown/CombatFx.PixelSize(camera,at,1)<PointFlashPixels;
        float flashLife=point?Mathf.Min(duration,electric?.4f:.9f):electric?.18f:.42f;
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
            flash.Material.SetShaderParameter("point",point?1f:0f); if(point) PointFlashes++;
            if(point) { flash.Material.SetShaderParameter("pixels",PointFlashQuad); CombatFx.Flare(flash.Node,camera,at,0,PointFlashQuad); }
            else CombatFx.Flare(flash.Node,camera,at,grown,distantFlash?PointFlashPixels:(electric?48:28)*near);
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
