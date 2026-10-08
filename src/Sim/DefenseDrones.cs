using System;
using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

public sealed record DefenseDroneDefinition(int Count, float OrbitMeters, float RangeMeters,
    float ShotsPerSecond, float HitChance, float DamagePerHit, int RoundsPerDrone);

/// <summary>Recoverable tethered defensive drones; ammunition belongs to the carrier's battle state.</summary>
public sealed class DefenseDroneState
{
    private readonly ShipBody _ship;
    public DefenseDroneState(ShipBody ship)
    {
        _ship=ship;
        int count=ship.Definition.DefenseDrones?.Count??0;
        Rounds=new int[count]; Accum=new float[count]; Reset();
    }
    public int[] Rounds { get; }
    internal float[] Accum { get; }
    public bool Active => !_ship.Damage.Destroyed && _ship.Damage.SensorFraction > .01f && _ship.Damage.PowerFraction > .01f;
    public Vector3 LocalPosition(int index, double time)
    {
        var def=_ship.Definition.DefenseDrones!;
        float angle=Mathf.Tau*index/def.Count+(float)time*.12f;
        // Four inclined rings expose some, but not all, drones to a given approach.
        var plane=new Basis(Vector3.Forward, (index%4)*Mathf.Pi/4);
        return plane*new Vector3(Mathf.Cos(angle)*def.OrbitMeters,0,Mathf.Sin(angle)*def.OrbitMeters);
    }
    internal void Reset()
    { Array.Fill(Rounds,_ship.Definition.DefenseDrones?.RoundsPerDrone??0); Array.Clear(Accum); }
}

public sealed partial class SimWorld
{
    private void StepDefenseDrones(double dt, double time)
    {
        if (_missiles.Count==0) return;
        foreach(var ship in _ships)
        {
            if (ship.Definition.DefenseDrones is not { } def || !ship.Ordnance.Drones.Active) continue;
            var drones=ship.Ordnance.Drones;
            float quality=Mathf.Clamp(ship.Damage.SensorFraction*ship.Power.SensorEffect*ship.Power.WeaponEffect,0,1.5f);
            for(int i=0;i<def.Count;i++)
            {
                if(drones.Rounds[i]==0) continue;
                Vec3d origin=ship.Position+Vec3d.From(ship.Orientation*drones.LocalPosition(i,time));
                Missile? target=null; double nearest=def.RangeMeters;
                foreach(var m in _missiles)
                {
                    double distance=(m.Position-origin).Length();
                    if(m.Faction==ship.Faction || distance>=nearest) continue;
                    var direction=(m.Position-origin).ToVector3().Normalized();
                    if(DamageRay.FirstHit(ship,origin,direction,(float)distance,out _)) continue;
                    target=m; nearest=distance;
                }
                if(target is null) { drones.Accum[i]=0; continue; }
                drones.Accum[i]+=(float)dt*def.ShotsPerSecond;
                while(drones.Accum[i]>=1 && drones.Rounds[i]>0 && target.Health>0)
                {
                    drones.Accum[i]--; drones.Rounds[i]--;
                    float chance=def.HitChance*quality*(1-.6f*(float)nearest/def.RangeMeters);
                    bool hit=Roll(++_pointDefenseSequence*2246822519u ^ target.Id)<chance;
                    _pointDefenseShots.Add(new(origin,target.Position,time,hit,ship.Faction));
                    if(hit) target.Health-=def.DamagePerHit;
                }
                if(target.Health<=0 && _missiles.Remove(target))
                {
                    Log?.EndAntimatter(target,AntimatterOutcome.Drone,time);
                    _ordnanceEvents.Add(new(OrdnanceEventKind.Intercepted,target.Position,time,target.Faction,target.Weapon));
                }
            }
        }
    }
}
