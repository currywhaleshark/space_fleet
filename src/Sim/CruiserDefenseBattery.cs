using System;
using System.Collections.Generic;
using Godot;

namespace SpaceFleet.Sim;

public sealed partial class SimWorld
{
    // Reused across ships: one observed candidate set, no live enemy pose/health reads.
    private readonly List<(SmallContact Contact,double Eta)> _batteryThreats=new();
    private readonly Dictionary<object,int> _batteryAssignments=new();

    private bool AutoLaserCooling(ShipBody ship,RailgunState gun)
    {
        if(gun.Laser is not { } laser)return false;
        if(ship.Power.HeatFraction>=.85f)laser.AutoCooling=true;
        else if(ship.Power.HeatFraction<=.4f)laser.AutoCooling=false;
        if(laser.AutoCooling)laser.Stop();
        return laser.AutoCooling;
    }

    // Returns whether any mount is reserved for defense this tick. Unreserved mounts
    // may still engage ships; manual/focus/hold player orders never call this path.
    private bool EngageDefenseBattery(ShipBody ship,out GunneryStatus status)
    {
        status=GunneryStatus.NoTarget;
        if(ship.Definition.Branch!=ShipBranch.AirDefenseCruiser||!Squadron.Active(ship))return false;
        var anchor=CruiserAnchor(ship);
        _batteryThreats.Clear();_batteryAssignments.Clear();
        foreach(var contact in Sensors.SmallContacts(ship.Faction))
        {
            if(contact.Kind!=SmallContactKind.Missile||!contact.Fresh(Time)
                ||(contact.Predict(Time)-ship.Position).LengthSquared()>12000.0*12000)continue;
            if(Threatens(contact,anchor,out double eta)||Threatens(contact,ship,out eta))
                _batteryThreats.Add((contact,eta));
        }
        bool reserved=false,firing=false;
        foreach(var gun in ship.Railguns)
        {
            var prior=gun.DefenseTarget;gun.DefenseTarget=null;gun.DefenseTick=-1;
            if(gun.Output<=.01f||!gun.HasAmmunition||AutoLaserCooling(ship,gun))continue;
            SmallContact? best=null;Vector3 direction=default;Vec3d point=default;double score=double.PositiveInfinity;
            foreach(var candidate in _batteryThreats)
            {
                var contact=candidate.Contact;
                if(!SmallTargetAim(ship,contact,out var aim,out var at,gun))continue;
                // Split available targets before doubling up. Keep an existing track
                // unless urgency/coverage changes enough to justify a new slew.
                double turn=gun.Direction.AngleTo(aim)/Math.Max(.01,Mathf.DegToRad(gun.Mount?.YawRate??30));
                double rank=_batteryAssignments.GetValueOrDefault(contact.Target)*1000+candidate.Eta+turn*2
                    -(prior==contact.Target?2:0);
                if(rank>=score||RailLineLocal(ship,gun,ship.Orientation.Inverse()*aim)!=FireFailure.None
                    ||TerrainBlocked(gun.MuzzlePosition,at)
                    ||FriendlyInLine(ship,gun.MuzzlePosition,aim,(float)(at-gun.MuzzlePosition).Length()))continue;
                score=rank;best=contact;direction=aim;point=at;
            }
            if(best is not { } chosen)continue;
            gun.DefenseTarget=chosen.Target;gun.DefenseTick=Tick;reserved=true;
            _batteryAssignments[chosen.Target]=_batteryAssignments.GetValueOrDefault(chosen.Target)+1;
            gun.Aim(direction);
            // Inspect the real bore as well as the requested line while traversing.
            if(FriendlyInLine(ship,gun.MuzzlePosition,gun.Direction,(float)(point-gun.MuzzlePosition).Length()))
            {gun.Laser?.Stop();gun.DefenseStatus=GunneryStatus.FriendlyLine;continue;}
            var shot=FireRailgun(ship,gun,direction);
            firing|=shot.Fired;
            gun.DefenseStatus=shot.Fired?GunneryStatus.Firing:shot.Failure==FireFailure.Traversing
                ?GunneryStatus.Traversing:GunneryStatus.Reload;
        }
        if(reserved)status=firing?GunneryStatus.Firing:GunneryStatus.Traversing;
        return reserved;
    }
}
