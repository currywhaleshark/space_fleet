using System;
using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

public sealed partial class SimWorld
{
    // Only observed target identity/position reaches this method. Readiness uses our own ships.
    private static bool PrepareAssaultRally(Squadron wing,ShipBody leader,ShipBody[] members,
        ShipBody? target,Vec3d rally,Vector3 anchorVelocity)
    {
        wing.Rallying=false;wing.RallyReady=0;wing.RallyRequired=0;
        if (wing.Mission == InterceptorMission.Assault && wing.AssaultCommittedTarget is not null)
        { wing.AssaultReleased=true; return false; }
        if(leader.Definition.Branch!=ShipBranch.Assault||wing.OrderedMission is not null
            ||wing.Mission!=InterceptorMission.Assault||!wing.AssaultAvailable||target is null)
        {wing.RallyTarget=null;wing.AssaultReleased=false;return false;}
        if(wing.RallyTarget!=target){wing.RallyTarget=target;wing.AssaultReleased=false;}
        var attackers=members.Where(s=>s.Ordnance.Antimatter.Rounds>0&&!s.Ordnance.Antimatter.Warning
            &&s.Damage.PropulsionFraction>=.25f).ToArray();
        wing.RallyReady=attackers.Count(s=>(s.Position-rally).Length()<2200
            &&(s.Velocity-anchorVelocity).Length()<600);
        wing.RallyRequired=Math.Max(1,(int)Math.Ceiling(attackers.Length*.6));
        if(wing.RallyReady>=wing.RallyRequired)wing.AssaultReleased=true;
        wing.Rallying=!wing.AssaultReleased;return wing.Rallying;
    }
}
