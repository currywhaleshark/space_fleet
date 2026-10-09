using System;
using Godot;

namespace SpaceFleet.Sim;

public sealed partial class SimWorld
{
    /// <summary>Close-range defensive fire cannot pass through the carrier, friendly hulls or intervening wrecks.</summary>
    private bool ClearDefenseLine(Vec3d origin,Vec3d target,ShipBody? intended=null)
    {
        Vec3d travel=target-origin;
        double length=travel.Length();
        if(length<.001) return false;
        Vector3 direction=(travel*(1/length)).ToVector3();
        foreach(var body in _ships)
        {
            if(body==intended) continue;
            Vec3d toCenter=body.Position-origin;
            double along=Math.Clamp(toCenter.Dot(travel)/(length*length),0,1);
            if((toCenter-travel*along).LengthSquared()>body.Hull.BoundingRadius*body.Hull.BoundingRadius) continue;
            if(DamageRay.FirstHit(body,origin,direction,(float)length,out float hit) && hit<length-.05) return false;
        }
        return true;
    }

    internal bool DamageDrone(ShipBody shooter,ShipBody carrier,int index,float damage,double time)
    {
        var drones=carrier.Ordnance.Drones;
        if(!drones.Hurt(index,damage)) return false;
        _ordnanceEvents.Add(new(OrdnanceEventKind.DroneDestroyed,drones.WorldPosition(index),time,carrier.Faction,BattleWeapon.DefenseDrone));
        if(Log is { } log && shooter.Faction!=carrier.Faction) log.Ship(shooter).DronesDestroyed++;
        return true;
    }

    /// <summary>Continuous point versus moving drone sphere, in double relative coordinates.</summary>
    internal static bool DroneIntersection(Vec3d relativeStart,Vec3d relativeTravel,float radius,out double fraction)
    {
        fraction=0;
        double c=relativeStart.LengthSquared()-radius*radius;
        if(c<=0) return true;
        double a=relativeTravel.LengthSquared();
        if(a<1e-16) return false;
        double b=relativeStart.Dot(relativeTravel), discriminant=b*b-a*c;
        if(b>=0 || discriminant<0) return false;
        fraction=c/(-b+Math.Sqrt(discriminant));
        return fraction>=0 && fraction<=1;
    }

    private bool FirstDroneHit(ShipBody shooter,Vec3d start,Vec3d end,double stepFraction,double maximum,
        out ShipBody? carrier,out int index,out double fraction)
    {
        carrier=null; index=-1; fraction=maximum;
        foreach(var ship in _ships)
        {
            if(ship==shooter || ship.Damage.Destroyed || ship.Definition.DefenseDrones is not { } def) continue;
            var drones=ship.Ordnance.Drones;
            for(int i=0;i<def.Count;i++)
            {
                if(!drones.Alive(i)) continue;
                Vec3d before=drones.PreviousWorldPosition(i);
                Vec3d movement=(drones.WorldPosition(i)-before)*stepFraction;
                if(!DroneIntersection(start-before,end-start-movement,def.RadiusMeters,out double t) || t>=fraction) continue;
                carrier=ship; index=i; fraction=t;
            }
        }
        return carrier is not null;
    }
}
