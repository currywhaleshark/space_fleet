using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

public sealed partial class SimWorld
{
    private static bool Cruiser(ShipBody ship) => ship.Definition.Branch is ShipBranch.GunCruiser
        or ShipBranch.AirDefenseCruiser or ShipBranch.MissileCruiser;

    private ShipBody CruiserAnchor(ShipBody ship) => ship.Squadron?.SupportGroup?.Leader
        ?? _ships.Where(s=>s!=ship&&s.Faction==ship.Faction&&s.Definition.MatchClass==MatchWeightClass.Large&&Squadron.Active(s))
            .OrderBy(s=>(s.Position-ship.Position).LengthSquared()).FirstOrDefault()
        ?? ship.Squadron?.Leader ?? ship;

    // Branch orders follow faction posture, while player and capture orders retain precedence.
    private void CommandCruisers(Faction faction, List<(ShipBody Ship, SensorTrack Track)> known, Dictionary<ShipBody,int> assigned)
    {
        foreach(var brain in _brains.Values)
        {
            var ship=brain.Ship;
            if(!brain.Enabled||ship.Faction!=faction||!Cruiser(ship)||!Squadron.Active(ship)
                ||ship.Squadron is { PlayerLed:true } or { ObjectiveOrders:true })continue;
            if(brain.Target is { } remembered&&Sensors.Memory(faction,remembered,Time)is not null)continue;
            var target=known.OrderBy(k=>k.Track.Level>=TrackLevel.Identified ? ship.Definition.Ai.TargetPreference[k.Ship.Class.Kind] : int.MaxValue)
                .ThenBy(k=>(k.Track.EstimatedPosition-ship.Position).LengthSquared()).Select(k=>k.Ship).FirstOrDefault();
            brain.CommandStandoff=null;
            if(ship.Definition.Branch==ShipBranch.AirDefenseCruiser)
            {
                var anchor=CruiserAnchor(ship);
                int slot=ship.Squadron?.Members.TakeWhile(s=>s!=ship).Count()??0;
                bool inner=ship.Squadron?.Role==SquadronRole.BattleGroup;
                if(inner)slot=Math.Max(0,slot-1);
                SetFormation(ship,new Vector3((slot%2==0?1:-1)*((inner?1400:3200)+slot/2*1800),400,-2400)
                    +(ship.Squadron?.SupportOffset??Vector3.Zero));
                Assign(ship,anchor==ship?ShipOrder.HoldAt(ship.Position,target):ShipOrder.EscortOf(anchor,target),assigned);
            }
            else Assign(ship,target is null?ShipOrder.HoldAt(ship.Position):ShipOrder.AttackOn(target),assigned);
            if(ship.Squadron is { Role: not SquadronRole.BattleGroup } squad)
            {
                squad.Target=target;
                squad.Activity=ship.Definition.Branch switch { ShipBranch.AirDefenseCruiser=>"함대 방공",ShipBranch.MissileCruiser=>"장거리 일제 사격",_=>"측면 포격" };
            }
        }
    }

    private void SelectCruiserDefenseTarget(ShipBrain brain)
    {
        var ship=brain.Ship;
        if(ship.Definition.Branch!=ShipBranch.AirDefenseCruiser)return;
        brain.SmallTarget=null;
        if(ship.Definition.Missiles is not {CanInterceptMissiles:true} def||!ship.Ordnance.MissileReady)return;
        var anchor=CruiserAnchor(ship);double best=double.PositiveInfinity;
        foreach(var threat in _aiThreats[(int)ship.Faction])
        {
            var contact=threat.Contact;
            if(contact.Target is not Missile missile||(threat.Position-ship.Position).Length()>def.InterceptRangeMeters
                ||!Threatens(contact,anchor,out double eta)&&!Threatens(contact,ship,out eta)
                ||eta>=best||!CounterMissileSolution(ship,missile,out _,out _,requireLaunchAlignment:false)
                ||_missiles.Any(m=>m.Faction==ship.Faction&&m.InterceptTarget==missile&&!m.InterceptPassed))continue;
            brain.SmallTarget=missile;best=eta;
        }
    }

    private bool EngageCruiserDefense(ShipBrain brain)
    {
        if(brain.Ship.Definition.Branch!=ShipBranch.AirDefenseCruiser)return false;
        if(brain.SmallTarget is Missile missile && !_missiles.Any(m=>m.Faction==brain.Ship.Faction&&m.InterceptTarget==missile&&!m.InterceptPassed))
            LaunchCounterMissile(brain.Ship,missile);
        return true;
    }
}
