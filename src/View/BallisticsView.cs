using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.View;

/// <summary>Readable rail cores/tails, electrical muzzle pulses and damage-scaled metal ejecta.</summary>
public partial class BallisticsView : Node3D
{
    private readonly List<MeshInstance3D> _streaks=new(), _flares=new(), _shards=new();
    private CombatBurstBatch _bursts=null!;
    private readonly Dictionary<ProjectileImpact,(Vector3 Point,Vector3 Normal)> _surfaceHits=new();
    private ShaderMaterial _tail=null!, _halo=null!, _enemyTail=null!, _enemyHalo=null!, _head=null!, _shield=null!, _impact=null!;
    private readonly BoxMesh _chip=new() { Size=Vector3.One };
    private readonly StandardMaterial3D _metal=new() { AlbedoColor=new Color(.24f,.28f,.32f),Metallic=.75f,Roughness=.65f };
    private readonly StandardMaterial3D _hotMetal=new() { AlbedoColor=new Color(.4f,.25f,.12f),Metallic=.6f,
        EmissionEnabled=true,Emission=new Color(1,.38f,.08f),EmissionEnergyMultiplier=2 };
    public int VisibleTrails { get; private set; }
    public int VisibleFragments { get; private set; }
    public int VisibleBurstSparks => _bursts.VisibleSparks;
    public int VisibleBurstFlashes => _bursts.VisibleFlashes;
    public override void _Ready()
    {
        _bursts=new CombatBurstBatch(this);
        _tail=CombatFx.Glow(new Color(.75f,.9f,1),true); _halo=CombatFx.Glow(new Color(.18f,.5f,1,.32f),true);
        _enemyTail=CombatFx.Glow(new Color(1,.85f,.65f),true); _enemyHalo=CombatFx.Glow(new Color(1,.3f,.08f,.32f),true);
        _head=CombatFx.Glow(new Color(.85f,.95f,1));
        _shield=CombatFx.Glow(new Color(.25f,.65f,1)); _impact=CombatFx.Glow(new Color(1,.68f,.32f));
    }
    private MeshInstance3D Next(List<MeshInstance3D> pool,int index,Mesh mesh,Material material)
    {
        if(index==pool.Count) pool.Add(CombatFx.Make(this,mesh,material));
        pool[index].MaterialOverride=material; pool[index].Visible=true; return pool[index];
    }
    private static void HideAfter(List<MeshInstance3D> pool,int count)
    { for(int i=count;i<pool.Count;i++) pool[i].Visible=false; }
    public static int FragmentCount(ProjectileImpact impact)
    {
        if(!impact.Hit.HullHit || impact.Hit.ShieldStopped) return 0;
        int tier=impact.Hit.Modules.Any(m=>m.Destroyed)?3:impact.Hit.Modules.Count>0?2:1;
        return Math.Clamp((int)(Mathf.Sqrt(Mathf.Max(impact.Energy,1)/100)*tier*3),2,48);
    }
    public void Sync(SimWorld world,Vec3d origin,double alpha,Camera3D camera,IReadOnlyList<ShipView>? ships=null)
    {
        _bursts.Begin();
        int t=0,f=0,s=0;
        foreach(var p in world.Projectiles)
        {
            Vector3 head=(Vec3d.Lerp(p.PrevPosition,p.Position,alpha)-origin).ToVector3();
            if(head.DistanceTo(camera.Position)>camera.Far*.95f) continue;
            float length=(float)(p.Velocity.Length()*Math.Min(.095,p.Age));
            Vector3 tail=head-p.Velocity.Normalized()*length;
            bool hostile=p.Shooter.Faction==Faction.Red;
            CombatFx.Streak(Next(_streaks,t++,CombatFx.Quad,hostile?_enemyHalo:_halo),camera,head,tail,Mathf.Max(.5f,CombatFx.PixelSize(camera,head,5)));
            var trail=Next(_streaks,t++,CombatFx.Quad,hostile?_enemyTail:_tail);
            CombatFx.Streak(trail,camera,head,tail,Mathf.Max(.18f,CombatFx.PixelSize(camera,head,2.2f)));
            CombatFx.Flare(Next(_flares,f++,CombatFx.Quad,_head),camera,head,.65f,14);
        }
        foreach(var ship in world.Ships)
        foreach(var gun in ship.Railguns)
        {
            float age=(float)(world.Time-gun.LastFiredAt);
            if(age<0 || age>.18f || ship.Damage.Destroyed) continue;
            for(int barrel=0;barrel<gun.LastSalvoRounds;barrel++)
            {
                float muzzleAge=CarbonCombatFx.MuzzleAge(world.Time,gun.LastFiredAt,barrel,0,gun.LastSalvoRounds);
                var q=ship.InterpolatedOrientation((float)alpha);
                Vector3 local=gun.Mount?.Muzzle(gun.Yaw,gun.Elevation,barrel)??gun.Definition.Muzzle;
                Vector3 at=(ship.InterpolatedPosition(alpha)-origin).ToVector3()+q*local;
                float size=Mathf.Clamp(ship.Class.Length*.028f,1.5f,40);
                Vector3 dir=q*gun.LocalDirection;
                _bursts.Draw(camera,at,at,dir,muzzleAge,size,gun.ShotCount+(uint)barrel*31,7,true,duration:.18f);
            }
        }
        foreach(var impact in world.Impacts)
        {
            float age=(float)(world.Time-impact.Time);
            if(age<0 || age>2.8f || impact.Hit.Target is not {} target) continue;
            var hit=impact.Hit;
            Vector3 at=hit.LocalPoint is {} local
                ? (target.InterpolatedPosition(alpha)-origin).ToVector3()+target.InterpolatedOrientation((float)alpha)*local
                : (hit.Point-origin).ToVector3();
            bool shield=hit.ShieldStopped || !hit.HullHit;
            Vector3 surfaceOffset=Vector3.Zero;
            Vector3 normal=impact.TargetOrientation*hit.LocalNormal;
            if(normal.LengthSquared()<.01f) normal=-impact.IncomingDirection;
            if(!shield && hit.LocalPoint is {} hitLocal && ships is not null)
            {
                if(!_surfaceHits.TryGetValue(impact,out var surface))
                {
                    var view=ships.FirstOrDefault(v=>v.Body==target);
                    surface=view?.ProjectFxSurface(hitLocal,impact.TargetOrientation.Inverse()*impact.IncomingDirection)??(hitLocal,hit.LocalNormal);
                    _surfaceHits[impact]=surface;
                }
                surfaceOffset=impact.TargetOrientation*(surface.Point-hitLocal);
                normal=impact.TargetOrientation*surface.Normal;
                at=(target.InterpolatedPosition(alpha)-origin).ToVector3()+target.InterpolatedOrientation((float)alpha)*surface.Point;
            }
            if(age<.25f)
            {
                float pulse=1-age/.25f;
                CombatFx.Flare(Next(_flares,f++,CombatFx.Quad,shield?_shield:_impact),camera,at,
                    Mathf.Clamp(Mathf.Sqrt(Mathf.Max(impact.Energy,1))*.8f,1,80)*pulse,(shield?8:12)*pulse);
                if(impact.Weapon==BattleWeapon.Railgun && age<.14f)
                {
                    float speed=impact.Shooter.Railgun?.Definition.MuzzleSpeed??6000;
                    CombatFx.Streak(Next(_streaks,t++,CombatFx.Quad,impact.Shooter.Faction==Faction.Red?_enemyTail:_tail),camera,at,at-impact.IncomingDirection*speed*.09f,
                        CombatFx.PixelSize(camera,at,2)*(1-age/.14f));
                }
            }
            int count=FragmentCount(impact);
            if(count==0) continue;
            // Tiny far-away chips would alias; retain the impact flash and reserve geometry for nearby debris.
            if(at.DistanceTo(camera.Position)>Mathf.Max(6000,target.Class.Length*18)) continue;
            float parentSize=target.Class.Length;
            float effectScale=CarbonCombatFx.ImpactScale(Mathf.Clamp(Mathf.Sqrt(impact.Energy/100)*3,2,10),parentSize);
            float pixelDiameter=parentSize/Mathf.Max(.001f,CombatFx.PixelSize(camera,at,1));
            count=Math.Max(2,(int)Mathf.Ceil(count*CarbonCombatFx.EmissionLod(pixelDiameter)));
            Vector3 effectOffset=normal*Mathf.Max(.1f,effectScale*.03f);
            Vector3 debrisOrigin=(hit.Point-origin).ToVector3()+surfaceOffset+effectOffset+impact.TargetVelocity*age;
            if(!shield)
                _bursts.Draw(camera,at+effectOffset,debrisOrigin,normal,age,
                    Mathf.Max(2,effectScale*(impact.Weapon==BattleWeapon.Antimatter?8:3)),impact.Id,
                    count*2,impact.Weapon==BattleWeapon.Antimatter,duration:1.35f);
            for(int j=0;j<count && s<512;j++)
            {
                float seed=impact.Id*.173f+j*2.39996f;
                Vector3 direction=CarbonCombatFx.Direction(impact.Id,j,normal,1.5f);
                float scale=Mathf.Clamp(effectScale*.18f,.08f,2.2f)
                    *(hit.Modules.Count>0?2:1)*(.5f+(j%5)*.22f);
                float fade=Mathf.Clamp((2.8f-age)/.5f,0,1);
                var shard=Next(_shards,s++,_chip,age<.45f?_hotMetal:_metal);
                Vector3 position=debrisOrigin+direction*(35+j*6)*age;
                shard.Transform=new(new Basis(new Vector3(.3f,.8f,.5f).Normalized(),seed+age*(2+j%7))
                    .Scaled(new Vector3(scale,scale*.22f,scale*1.8f)*fade),position);
                if(age<.45f && j<8) CombatFx.Flare(Next(_flares,f++,CombatFx.Quad,_impact),camera,position,scale*2,3*(1-age/.45f));
            }
        }
        VisibleTrails=t; VisibleFragments=s;
        HideAfter(_streaks,t); HideAfter(_flares,f); HideAfter(_shards,s);
        _bursts.End();
        foreach(var key in _surfaceHits.Keys.Where(k=>!world.Impacts.Contains(k)).ToArray()) _surfaceHits.Remove(key);
    }
}
