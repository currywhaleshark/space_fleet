using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.View;

public partial class ShipView
{
    private MeshInstance3D? _jumpRing;
    private MeshInstance3D[] _heatVents=Array.Empty<MeshInstance3D>();
    private StandardMaterial3D? _jumpLight,_ventLight;
    private void BuildTacticalVisuals()
    {
        if(Body.Tactical.Slots.Length==0)return;
        StandardMaterial3D Glow(Color color)=>new(){ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency=BaseMaterial3D.TransparencyEnum.Alpha,AlbedoColor=new(color,.65f),EmissionEnabled=true,Emission=color,
            EmissionEnergyMultiplier=2,NoDepthTest=false};
        if(Body.Tactical.ActiveSlots.Any(s=>s.Definition.Effect==TacticalEffect.ShortJump))
        {
            _jumpLight=Glow(new(.55f,.85f,1));
            float r=Math.Max(1,Body.Class.Length*.22f);
            _jumpRing=new MeshInstance3D{Mesh=new TorusMesh{InnerRadius=r*.97f,OuterRadius=r,Rings=32,RingSegments=8},
                Rotation=new(Mathf.Pi/2,0,0),MaterialOverride=_jumpLight,Visible=false,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};AddChild(_jumpRing);
        }
        if(Body.Tactical.ActiveSlots.Any(s=>s.Definition.Effect==TacticalEffect.HeatVent))
        {
            _ventLight=Glow(new(.45f,.85f,1));
            _heatVents=Body.Definition.Modules.Where(m=>m.Kind==ModuleKind.Cooling).Take(6).Select(m=>
            {
                Vector3 direction=m.Center.X<0?Vector3.Left:Vector3.Right;
                var surface=ProjectFxSurface(m.Center,direction);
                var mesh=new MeshInstance3D{Position=surface.Point+surface.Normal*Math.Max(.15f,Body.Class.Length*.004f),
                    Mesh=new SphereMesh{Radius=Math.Max(.2f,Body.Class.Length*.008f),Height=Math.Max(.4f,Body.Class.Length*.016f),RadialSegments=12,Rings=6},
                    MaterialOverride=_ventLight,Visible=false,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};AddChild(mesh);return mesh;
            }).ToArray();
        }
    }
    private void SyncTacticalVisuals()
    {
        if(Body.Tactical.Slots.Length==0)return;
        bool vent=false;float jump=0;
        foreach(var slot in Body.Tactical.ActiveSlots)
        {
            var status=Body.Tactical.Status(slot);
            if(slot.Definition.Effect==TacticalEffect.HeatVent&&status==TacticalStatus.Active)vent=true;
            if(slot.Definition.Effect==TacticalEffect.ShortJump&&status==TacticalStatus.Preparing)jump=(float)(1-(slot.ActiveUntil-Body.SimTime)/slot.Definition.Duration);
        }
        if(_jumpRing is not null)
        {
            _jumpRing.Visible=jump>0&&!Body.Damage.Destroyed;
            _jumpRing.Position=new(0,0,Body.Class.Length*(.45f-.9f*jump));
            _jumpRing.Scale=Vector3.One*(1.2f-.45f*jump);
        }
        foreach(var mesh in _heatVents){mesh.Visible=vent&&!Body.Damage.Destroyed;mesh.Scale=Vector3.One*(1+.2f*Mathf.Sin((float)Body.SimTime*9));}
    }
}
