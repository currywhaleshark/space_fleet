using System;
using System.Collections.Generic;
using Godot;

namespace SpaceFleet.Sim;

public sealed partial class SimWorld
{
    // Reused across ships: one observed candidate set, no live enemy pose/health reads.
    private readonly List<(SmallContact Contact,double Eta)> _batteryThreats=new();
    private readonly Dictionary<object,int> _batteryAssignments=new();
    // Missiles closing on another ally rank after threats to this ship/anchor (seconds added to ETA).
    private const double AllyThreatDelay=8;
    // Idle mounts slew toward a missile due within this time, so the first shot does not wait on a 90° swing.
    private const double PreAimSeconds=12;
    // Per side and tick: earliest arrival of each observed missile on any active ally (+∞ when none).
    private readonly List<double>[] _allyThreatEta={new(),new()};
    private readonly long[] _allyThreatTick={-1,-1};

    /// <summary>Observed missiles only (this tick's `_aiThreats`); own-side ship positions are known.</summary>
    private List<double> AllyThreatEtas(Faction faction)
    {
        int side=(int)faction;var etas=_allyThreatEta[side];
        if(_allyThreatTick[side]==Tick)return etas;
        _allyThreatTick[side]=Tick;etas.Clear();
        foreach(var threat in _aiThreats[side])
        {
            double best=double.PositiveInfinity;
            foreach(var ally in _ships)
                if(ally.Faction==faction&&Squadron.Active(ally)&&Threatens(threat.Contact,ally,out double eta)&&eta<best)best=eta;
            etas.Add(best);
        }
        return etas;
    }

    /// <summary>An overcharged rail (heat ∝ overcharge²) holds a salvo that would tip the ship into overheat (§12).</summary>
    private static bool AutoOverchargeHold(ShipBody ship,RailgunState gun)=>gun.Laser is null&&gun.Overcharge>1
        &&ship.Power.HeatMj+gun.Definition.ShotHeatMj*gun.SalvoRounds>ship.Definition.Power.HeatCapacityMj*.95f;
    internal bool AutoLaserCooling(ShipBody ship,RailgunState gun)
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
        float reach=0;foreach(var mount in ship.Railguns)reach=Math.Max(reach,SmallTargetRange(mount));
        // Own ship/anchor first; a missile closing on any other active ally within reach is still engaged.
        var threats=_aiThreats[(int)ship.Faction];var allyEtas=AllyThreatEtas(ship.Faction);
        for(int i=0;i<threats.Count;i++)
        {
            var contact=threats[i].Contact;
            if(contact.Level<TrackLevel.Identified||(threats[i].Position-ship.Position).LengthSquared()>(double)reach*reach)continue;
            if(Threatens(contact,anchor,out double eta)||Threatens(contact,ship,out eta))
                _batteryThreats.Add((contact,eta));
            else if(allyEtas[i]<double.PositiveInfinity)
                _batteryThreats.Add((contact,allyEtas[i]+AllyThreatDelay));
        }
        bool reserved=false,firing=false;int statusRank=-1;
        foreach(var gun in ship.Railguns)
        {
            var prior=gun.DefenseTarget;gun.DefenseTarget=null;gun.DefenseTick=-1;
            if(gun.Output<=.01f||!gun.HasAmmunition||AutoLaserCooling(ship,gun))continue;
            SmallContact? best=null;Vector3 direction=default;Vec3d point=default;double score=double.PositiveInfinity;
            foreach(var candidate in _batteryThreats)
            {
                var contact=candidate.Contact;
                if(!SmallTargetAim(ship,contact,out var aim,out var at,out _,gun,requireLock:false))continue;
                // Split available targets before doubling up. Keep an existing track
                // unless urgency/coverage changes enough to justify a new slew.
                double turn=gun.Direction.AngleTo(aim)/Math.Max(.01,Mathf.DegToRad(gun.Mount?.YawRate??30));
                double rank=_batteryAssignments.GetValueOrDefault(contact.Target)*1000+candidate.Eta+turn*2
                    -(prior==contact.Target?2:0);
                if(rank>=score||RailLineLocal(ship,gun,ship.Orientation.Inverse()*aim)!=FireFailure.None
                    ||TerrainBlocked(gun.MuzzlePosition,at)
                    ||FriendlyInLine(ship,gun.MuzzlePosition,aim,(float)(at-gun.MuzzlePosition).Length(),includeShields:gun.IsLaser))continue;
                score=rank;best=contact;direction=aim;point=at;
            }
            if(best is not { } chosen)
            {
                if(PreAim(ship,gun,threats,reach)){gun.DefenseTick=Tick;gun.DefenseStatus=GunneryStatus.Traversing;reserved=true;}
                continue;
            }
            gun.DefenseTarget=chosen.Target;gun.DefenseTick=Tick;reserved=true;
            _batteryAssignments[chosen.Target]=_batteryAssignments.GetValueOrDefault(chosen.Target)+1;
            gun.Aim(direction);
            if(!chosen.Locked(Time))
            {gun.Laser?.Stop();gun.DefenseStatus=GunneryStatus.WaitLock;if(statusRank<0){status=gun.DefenseStatus;statusRank=0;}continue;}
            // Inspect the real bore as well as the requested line while traversing.
            if(FriendlyInLine(ship,gun.MuzzlePosition,gun.Direction,(float)(point-gun.MuzzlePosition).Length(),includeShields:gun.IsLaser))
            {gun.Laser?.Stop();gun.DefenseStatus=GunneryStatus.FriendlyLine;if(statusRank<1){status=gun.DefenseStatus;statusRank=1;}continue;}
            var shot=FireRailgun(ship,gun,direction,automatic:true);
            firing|=shot.Fired;
            gun.DefenseStatus=shot.Fired?GunneryStatus.Firing:shot.Failure==FireFailure.Traversing
                ?GunneryStatus.Traversing:GunneryStatus.Reload;
            if(statusRank<2){status=gun.DefenseStatus;statusRank=2;}
        }
        if(firing)status=GunneryStatus.Firing;
        return reserved;
    }

    /// <summary>
    /// Points an otherwise idle mount at the soonest observed missile closing on this ship, before it is a
    /// threat or locked. The mount is held for defense (ship gunnery skips it) but never fires from here.
    /// </summary>
    private bool PreAim(ShipBody ship,RailgunState gun,List<ObservedThreat> threats,float reach)
    {
        double soonest=PreAimSeconds;Vector3 aim=default;bool found=false;
        foreach(var threat in threats)
        {
            var contact=threat.Contact;
            Vec3d r=threat.Position-ship.Position;Vec3d v=Vec3d.From(contact.Velocity-ship.Velocity);
            if(r.LengthSquared()>(double)reach*reach||v.LengthSquared()<1)continue;
            double arrival=-r.Dot(v)/v.LengthSquared();
            if(arrival<=0||arrival>=soonest||(r+v*arrival).Length()>reach)continue;
            if(!SmallTargetAim(ship,contact,out var direction,out _,out _,gun,requireLock:false)
                ||RailLineLocal(ship,gun,ship.Orientation.Inverse()*direction)==FireFailure.Arc)continue;
            soonest=arrival;aim=direction;found=true;
        }
        if(found)gun.Aim(aim);
        return found;
    }
}
