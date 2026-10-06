using System;
using System.Collections.Generic;
using System.Linq;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public sealed record ControlSegment(string Ship, double Start, double End);
public sealed class PlayerRecord
{
    public int Rails, RailHits, Missiles, MissileHits, ModulesDestroyed, ArmorPenetrations, ModulesLost;
    public float ShieldDamage;
    public static PlayerRecord Sample(ShipBody ship, BattleLog log)
    {
        var s = log.Ship(ship);
        return new() { Rails=s.Rails, RailHits=s.RailHits, Missiles=s.Missiles, MissileHits=s.MissileHits,
            ModulesDestroyed=s.ModulesDestroyed, ArmorPenetrations=s.ArmorPenetrations, ShieldDamage=s.ShieldDamage,
            ModulesLost=ship.Damage.Modules.Count(m=>m.Destroyed) };
    }
    public void AddDifference(PlayerRecord end, PlayerRecord start)
    {
        Rails+=end.Rails-start.Rails; RailHits+=end.RailHits-start.RailHits; Missiles+=end.Missiles-start.Missiles;
        MissileHits+=end.MissileHits-start.MissileHits; ModulesDestroyed+=end.ModulesDestroyed-start.ModulesDestroyed;
        ArmorPenetrations+=end.ArmorPenetrations-start.ArmorPenetrations; ShieldDamage+=end.ShieldDamage-start.ShieldDamage;
        ModulesLost+=end.ModulesLost-start.ModulesLost;
    }
    public PlayerRecord Copy() => (PlayerRecord)MemberwiseClone();
}
public static class BattleArgs
{
    public static Dictionary<string,string> Parse(string[] args)
    {
        var map=new Dictionary<string,string>();
        foreach(string arg in args) { string a=arg.TrimStart('-'); int i=a.IndexOf('='); map[i<0?a:a[..i]]=i<0?"true":a[(i+1)..]; }
        return map;
    }
}
