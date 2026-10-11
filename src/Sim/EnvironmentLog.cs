using System;
using System.Collections.Generic;

namespace SpaceFleet.Sim;

public sealed record EnvironmentEvent(double Time,string Kind,string Source,string Obstacle,Vec3d Position);
/// <summary>Environment-only telemetry; does not change the legacy battle summary or deep-space event stream.</summary>
public sealed class EnvironmentLog
{
    public int ShipCollisions { get; private set; }
    public double CollisionEnergy { get; private set; }
    public double CollisionDamage { get; private set; }
    public int StuckDetections { get; private set; }
    public int BlockedRails { get; private set; }
    public int BlockedMissiles { get; private set; }
    public int BlockedTorpedoes { get; private set; }
    public int BlockedProbes { get; private set; }
    public double NavigationBuildMs { get; internal set; }
    public int PathPlans { get; internal set; }
    public int PathFailures { get; internal set; }
    public long PathExpanded { get; internal set; }
    public double PathMilliseconds { get; internal set; }
    public int StuckReplans { get; internal set; }
    public int FormationCompressions { get; internal set; }
    public double BlockedFireSeconds { get; internal set; }
    public int SightAttempts { get; internal set; }
    public int SightStarts { get; internal set; }
    public int SightSuccesses { get; internal set; }
    public int SightFailures { get; internal set; }
    public double SightReopenSeconds { get; internal set; }
    public int CoverBudgetFallbacks { get; internal set; }
    public double CoverPathLength { get; internal set; }
    public double CoverPathExposure { get; internal set; }
    public double ExposureSum { get; internal set; }
    public int ExposureSamples { get; internal set; }
    public int AssaultEntrants { get; internal set; }
    public int AssaultArrivals { get; internal set; }
    public int AssaultLostBeforeArrival { get; internal set; }
    internal void Weapon(double time,string source,ObstacleHit hit,BattleWeapon? weapon)
    {
        if(weapon is null) BlockedProbes++;
        else if(weapon==BattleWeapon.Railgun) BlockedRails++;
        else if(weapon==BattleWeapon.Antimatter) BlockedTorpedoes++;
        else BlockedMissiles++;
        Record(time,weapon is null?"프로브 충돌":weapon+" 장애물 명중",source,hit.Obstacle,hit.Position);
    }
    private readonly Dictionary<(ShipBody,Obstacle),double> _contacts=new();
    private readonly List<EnvironmentEvent> _events=new();
    public IReadOnlyList<EnvironmentEvent> Events=>_events;
    internal void Record(double time,string kind,string source,Obstacle obstacle,Vec3d position)
    { if(_events.Count<BattleLog.EventCapacity) _events.Add(new(time,kind,source,obstacle.Name,position)); }
    internal void Collision(ShipBody ship,Obstacle obstacle,double time,double energy,double damage)
    {
        var key=(ship,obstacle);
        if(!_contacts.TryGetValue(key,out double last) || time-last>1)
        { ShipCollisions++; Record(time,"함선 충돌",ship.Callsign,obstacle,ship.Position); }
        _contacts[key]=time; CollisionEnergy+=energy; CollisionDamage+=damage;
    }
    internal void Stuck(ShipBody ship,Obstacle obstacle,double time)
    { StuckDetections++; Record(time,"회피 갇힘",ship.Callsign,obstacle,ship.Position); }
}
