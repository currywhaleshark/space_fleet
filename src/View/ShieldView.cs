using System.Collections.Generic;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.View;

/// <summary>Only event patches exist visually; the shared simulation ellipsoid is invisible at rest.</summary>
public partial class ShieldView : Node3D
{
    private sealed record Pulse(MeshInstance3D Mesh,ShaderMaterial Material,double Time,float Lifetime);
    private readonly List<Pulse> _pulses=new();
    private readonly HashSet<uint> _seen=new();
    private static Shader? _shader;
    private static readonly SphereMesh Skin=new() { Radius=1,Height=2,RadialSegments=128,Rings=64 };
    private bool _primed, _hadShield;
    private double _lastTime;
    public int ActivePatches => _pulses.Count;
    public void Sync(ShipView view,SimWorld world)
    {
        var ship=view.Body;
        if(world.Time<_lastTime) Clear();
        _lastTime=world.Time;
        var retained=new HashSet<uint>();
        bool collapsed=false;
        foreach(var impact in world.Impacts)
        {
            if(impact.Hit.Target!=ship) continue;
            retained.Add(impact.Id);
            if(!_seen.Add(impact.Id) || world.Time-impact.Time>.8 || (impact.Hit.ShieldAbsorbed<=0 && !impact.ShieldBroken)) continue;
            Vector3 local=impact.Hit.ShieldLocalPoint??ship.Orientation.Inverse()*(impact.Hit.Point-ship.Position).ToVector3();
            Add(ship,local,impact.Time,impact.ShieldBroken?1:0,Mathf.Clamp(Mathf.Sqrt(impact.Hit.ShieldAbsorbed/250),.5f,1.5f));
            collapsed|=impact.ShieldBroken;
        }
        _seen.IntersectWith(retained);
        bool has=ship.Damage.Shield>0;
        if(_primed && _hadShield && !has && !collapsed)
            Add(ship,ship.Damage.DestructionPoint,world.Time,1,1);
        if(_primed && !_hadShield && has) Add(ship,ship.Definition.ShieldEnvelope.Center+Vector3.Forward*ship.Definition.ShieldEnvelope.Radii.Z,world.Time,2,.7f);
        _hadShield=has; _primed=true;
        for(int i=_pulses.Count-1;i>=0;i--)
        {
            var p=_pulses[i]; float age=(float)(world.Time-p.Time);
            if(age>=p.Lifetime) { p.Mesh.QueueFree(); _pulses.RemoveAt(i); }
            else p.Material.SetShaderParameter("age",Mathf.Max(0,age));
        }
    }
    private void Add(ShipBody ship,Vector3 local,double time,int effect,float strength)
    {
        if(_pulses.Count>=8) { _pulses[0].Mesh.QueueFree(); _pulses.RemoveAt(0); }
        var envelope=ship.Definition.ShieldEnvelope;
        var mat=new ShaderMaterial { Shader=_shader??=GD.Load<Shader>("res://shaders/shield_skin.gdshader") };
        Vector3 direction=((local-envelope.Center)/envelope.Radii).Normalized();
        mat.SetShaderParameter("hit_direction",direction.LengthSquared()<.01f?Vector3.Forward:direction);
        mat.SetShaderParameter("effect",effect); mat.SetShaderParameter("strength",strength);
        mat.SetShaderParameter("tint",effect==1?new Color(.55f,.8f,1):new Color(.15f,.55f,1));
        var mesh=CombatFx.Make(this,Skin,mat); mesh.Position=envelope.Center; mesh.Scale=envelope.Radii;
        _pulses.Add(new(mesh,mat,time,effect==1?.8f:.7f));
    }
    public void Clear()
    { foreach(var p in _pulses) p.Mesh.QueueFree(); _pulses.Clear(); _seen.Clear(); _primed=false; }
}
