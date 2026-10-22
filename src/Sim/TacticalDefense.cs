using System;
using System.Collections.Generic;

namespace SpaceFleet.Sim;

public sealed partial class SimWorld
{
    private readonly Dictionary<Missile,int> _defenseAssignments=new();
    private readonly Dictionary<(Faction,Missile),int> _defenseThreatRanks=new();
    // Only fresh observed trajectories and friendly positions contribute. Never consult hidden enemy target intent.
    private int TacticalDefensePriority(ShipBody defender,Missile missile,SmallContact seen,double time)
    {
        if(!defender.Tactical.AirDefense)return 0;
        var key=(defender.Faction,missile);
        if(!_defenseThreatRanks.TryGetValue(key,out int rank))
        {
            rank=1;var point=seen.Predict(time);
            foreach(var ally in _ships)
            {
                if(ally.Faction!=defender.Faction||ally.Damage.Destroyed)continue;
                var r=point-ally.Position;var v=Vec3d.From(seen.Velocity-ally.Velocity);
                double vv=v.LengthSquared();if(vv<1)continue;
                double approach=-(r.X*v.X+r.Y*v.Y+r.Z*v.Z)/vv;
                if(approach<0||approach>12)continue;
                double radius=ally.Hull.BoundingRadius+200;
                if((r+v*approach).LengthSquared()<radius*radius){rank=-3;break;}
            }
            _defenseThreatRanks[key]=rank;
        }
        return rank+Math.Min(3,_defenseAssignments.GetValueOrDefault(missile));
    }
    private void AssignTacticalDefense(ShipBody ship,Missile? missile)
    {if(ship.Tactical.AirDefense&&missile is not null)_defenseAssignments[missile]=_defenseAssignments.GetValueOrDefault(missile)+1;}
}
