using System;
using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

public sealed record DefenseDroneDefinition(int Count, float OrbitMeters, float RangeMeters,
    float ShotsPerSecond, float HitChance, float DamagePerHit, int RoundsPerDrone, float RepositionSpeed = 240f,
    float HitPoints = 8f, float RadiusMeters = 4f, float ShipEnergy = 10f, float ShipPenetrationMm = 30f, float ShipModuleDamage = 6f)
{
    public DamagePacket ShipPacket => new(ShipEnergy, ShipPenetrationMm, ShipModuleDamage, RangeMeters);
}

/// <summary>Carrier-local azimuth sectors; dorsal/ventral approaches are included, not flattened.</summary>
public enum DroneSector { AllAround, Fore, Starboard, Aft, Port }

/// <summary>Recoverable tethered defensive drones; ammunition belongs to the carrier's battle state.</summary>
public sealed class DefenseDroneState
{
    private readonly ShipBody _ship;
    private readonly Vector3[] _positions, _previous, _substepPrevious, _aim, _velocities;
    private readonly float[] _health;
    public DefenseDroneState(ShipBody ship)
    {
        _ship=ship;
        int count=ship.Definition.DefenseDrones?.Count??0;
        Rounds=new int[count]; Accum=new float[count]; LastFiredAt=new double[count];
        _positions=new Vector3[count]; _previous=new Vector3[count]; _substepPrevious=new Vector3[count];
        _aim=new Vector3[count]; _velocities=new Vector3[count]; _health=new float[count]; Reset();
    }
    public int[] Rounds { get; }
    internal float[] Accum { get; }
    public double[] LastFiredAt { get; }
    public DroneSector Sector { get; private set; }
    public uint Interceptions { get; internal set; }
    public uint ShipHits { get; internal set; }
    public int SurvivingCount => _health.Count(h => h > 0);
    public bool Alive(int index) => !_ship.Damage.Destroyed && _health[index]>0;
    public float HealthFraction(int index) => _health[index]/_ship.Definition.DefenseDrones!.HitPoints;
    public int RemainingRounds => Enumerable.Range(0,Rounds.Length).Where(Alive).Sum(i=>Rounds[i]);
    public bool Active => Rounds.Length > 0 && !_ship.Damage.Destroyed && SurvivingCount>0 && _ship.Damage.SensorFraction > .01f
        && _ship.Damage.PowerFraction > .01f && _ship.Power.SensorEffect > .01f && _ship.Power.WeaponEffect > .01f;
    public int ArmedCount => Enumerable.Range(0,Rounds.Length).Count(i => Alive(i) && Rounds[i]>0);
    public int RepositioningCount { get; private set; }
    public static string Label(DroneSector sector) => sector switch
    { DroneSector.Fore => "전방", DroneSector.Aft => "후방", DroneSector.Port => "좌현", DroneSector.Starboard => "우현", _ => "전방위" };
    public static Vector3 Direction(DroneSector sector) => sector switch
    { DroneSector.Fore => Vector3.Forward, DroneSector.Aft => Vector3.Back, DroneSector.Port => Vector3.Left, DroneSector.Starboard => Vector3.Right, _ => Vector3.Zero };

    public bool Assign(DroneSector sector)
    {
        if (!Enum.IsDefined(sector) || !Active) return false;
        if (Sector == sector) return true;
        Sector=sector;
        ClearTracking();
        UpdateRepositioning();
        return true;
    }

    /// <summary>90-degree azimuth wedge measured from the carrier, at every elevation.</summary>
    public bool Covers(Vector3 localTarget)
    {
        if (Sector == DroneSector.AllAround) return true;
        Vector3 axis=Direction(Sector);
        float forward=localTarget.Dot(axis), sideways=Mathf.Abs(localTarget.Dot(axis.Cross(Vector3.Up)));
        return forward >= sideways - .001f;
    }

    // Keep the old query signature for callers; time no longer teleports a drone along its orbit.
    public Vector3 LocalPosition(int index, double time)
        => _positions[index];
    public Vector3 InterpolatedPosition(int index, float alpha) => _previous[index].Lerp(_positions[index], alpha);
    public Vec3d WorldPosition(int index) => _ship.Position+Vec3d.From(_ship.Orientation*_positions[index]);
    internal Vec3d PreviousWorldPosition(int index) => _ship.PrevPosition+Vec3d.From(_ship.PrevOrientation*_substepPrevious[index]);
    public Vector3 WorldVelocity(int index) => _ship.Velocity+_ship.Orientation*(_velocities[index]+_ship.AngularVelocity.Cross(_positions[index]));
    public Vector3 LocalDirection(int index) => _aim[index].LengthSquared() > .001f ? _aim[index] : _positions[index].Normalized();
    internal void CapturePrevious() => Array.Copy(_positions, _previous, _positions.Length);
    internal void Aim(int index, Vector3 localDirection) => _aim[index]=localDirection;
    internal void ClearTracking() { Array.Clear(Accum); Array.Clear(_aim); }
    internal bool Hurt(int index, float damage)
    {
        if (!Alive(index) || !float.IsFinite(damage) || damage<=0) return false;
        _health[index]=Mathf.Max(0,_health[index]-damage);
        if (_health[index]>0) return false;
        Accum[index]=0; _aim[index]=Vector3.Zero; _velocities[index]=Vector3.Zero;
        UpdateRepositioning();
        return true;
    }

    private Vector3 Station(int index, double time)
    {
        var def=_ship.Definition.DefenseDrones!;
        if (Sector != DroneSector.AllAround)
        {
            // Eight stations distributed above and below the selected bearing, on the same safe shell.
            Vector3 axis=Direction(Sector), side=axis.Cross(Vector3.Up);
            float phase=Mathf.Tau*index/def.Count+(float)(time*.08%Math.Tau);
            float spread=Mathf.DegToRad(25);
            return (axis*Mathf.Cos(spread)+(side*Mathf.Cos(phase)+Vector3.Up*Mathf.Sin(phase))*Mathf.Sin(spread))*def.OrbitMeters;
        }
        float angle=Mathf.Tau*index/def.Count+(float)time*.12f;
        // Four inclined rings expose some, but not all, drones to a given approach.
        var plane=new Basis(Vector3.Forward, (index%4)*Mathf.Pi/4);
        return plane*new Vector3(Mathf.Cos(angle)*def.OrbitMeters,0,Mathf.Sin(angle)*def.OrbitMeters);
    }

    internal void Step(double dt)
    {
        Array.Copy(_positions,_substepPrevious,_positions.Length);
        Array.Clear(_velocities);
        if (!Active) { ClearTracking(); return; }
        var def=_ship.Definition.DefenseDrones!;
        float maxAngle=def.RepositionSpeed*(float)dt/def.OrbitMeters;
        for (int i=0;i<_positions.Length;i++)
        {
            if (!Alive(i)) continue;
            Vector3 goal=Station(i,_ship.SimTime), current=_positions[i].Normalized(), desired=goal.Normalized();
            float angle=current.AngleTo(desired);
            if (angle<=maxAngle) _positions[i]=goal;
            else
            {
                Vector3 axis=current.Cross(desired);
                if (axis.LengthSquared()<1e-10f)
                    axis=current.Cross(Mathf.Abs(current.Y)<.9f ? Vector3.Up : Vector3.Right);
                _positions[i]=(new Quaternion(axis.Normalized(),maxAngle)*current).Normalized()*def.OrbitMeters;
            }
            _velocities[i]=(_positions[i]-_substepPrevious[i])/(float)dt;
        }
        UpdateRepositioning();
    }

    private void UpdateRepositioning()
    {
        RepositioningCount=0;
        for (int i=0;i<_positions.Length;i++)
            if (Alive(i) && _positions[i].DistanceSquaredTo(Station(i,_ship.SimTime))>60*60) RepositioningCount++;
    }

    internal void Reset()
    {
        Sector=DroneSector.AllAround; Interceptions=ShipHits=0; RepositioningCount=0;
        Array.Fill(_health,_ship.Definition.DefenseDrones?.HitPoints??0); Array.Clear(_velocities);
        Array.Fill(Rounds,_ship.Definition.DefenseDrones?.RoundsPerDrone??0);
        Array.Fill(LastFiredAt,double.NegativeInfinity); ClearTracking();
        for (int i=0;i<_positions.Length;i++) _positions[i]=_previous[i]=_substepPrevious[i]=Station(i,_ship.SimTime);
    }
}

public sealed partial class SimWorld
{
    private void StepDefenseDrones(double dt, double time)
    {
        foreach(var ship in _ships)
        {
            if (ship.Definition.DefenseDrones is not { } def) continue;
            var drones=ship.Ordnance.Drones;
            if (!drones.Active) { drones.ClearTracking(); continue; }
            float quality=Mathf.Clamp(ship.Damage.SensorFraction*ship.Power.SensorEffect*ship.Power.WeaponEffect,0,1.5f);
            for(int i=0;i<def.Count;i++)
            {
                drones.Aim(i,Vector3.Zero);
                if(!drones.Alive(i) || drones.Rounds[i]==0) { drones.Accum[i]=0; continue; }
                Vec3d origin=ship.Position+Vec3d.From(ship.Orientation*drones.LocalPosition(i,time));
                Missile? target=null; double nearest=def.RangeMeters;
                foreach(var m in _missiles)
                {
                    double distance=(m.Position-origin).Length();
                    if(m.Faction==ship.Faction || m.Health<=0 || distance>=nearest
                        || !drones.Covers(ship.Orientation.Inverse()*(m.Position-ship.Position).ToVector3())) continue;
                    if(!ClearDefenseLine(origin,m.Position)) continue;
                    target=m; nearest=distance;
                }
                ShipBody? raider=null;
                if (target is null)
                    foreach (var other in _ships)
                    {
                        if(other.Faction==ship.Faction || other.Class.Kind!=HullKind.Interceptor || other.Damage.Destroyed) continue;
                        double distance=(other.Position-origin).Length();
                        if(distance>=nearest || !drones.Covers(ship.Orientation.Inverse()*(other.Position-ship.Position).ToVector3())
                            || !ClearDefenseLine(origin,other.Position,other)) continue;
                        raider=other; nearest=distance;
                    }
                if(target is null && raider is null) { drones.Accum[i]=0; continue; }
                Vec3d aim=target?.Position??raider!.Position;
                drones.Aim(i,ship.Orientation.Inverse()*(aim-origin).ToVector3().Normalized());
                drones.Accum[i]+=(float)dt*def.ShotsPerSecond;
                while(drones.Accum[i]>=1 && drones.Rounds[i]>0 && (target is null || target.Health>0) && raider?.Damage.Destroyed!=true)
                {
                    drones.Accum[i]--; drones.Rounds[i]--;
                    drones.LastFiredAt[i]=time;
                    float chance=def.HitChance*quality*(1-.6f*(float)nearest/def.RangeMeters);
                    Vector3 direction=(aim-origin).ToVector3().Normalized();
                    if(raider is not null)
                    {
                        Vector3 relative=raider.Velocity-drones.WorldVelocity(i);
                        float crossing=(relative-direction*relative.Dot(direction)).Length();
                        chance*=PointDefenseShipSizeFactor*Mathf.Min(1,PointDefenseCrossingSpeed/Mathf.Max(crossing,1));
                    }
                    uint salt=target?.Id??ShipBrain.Hash(raider!.Callsign);
                    bool hit=Roll(++_pointDefenseSequence*2246822519u ^ salt)<chance;
                    _pointDefenseShots.Add(new(origin,aim,time,hit,ship.Faction));
                    if(!hit) continue;
                    if(target is not null) target.Health-=def.DamagePerHit;
                    else
                    {
                        float shieldBefore=raider!.Damage.Shield;
                        ShotResult result=DamageRay.Apply(raider,origin,direction,def.ShipPacket,time,++_shotSequence);
                        if(result.Target is not null)
                        {
                            drones.ShipHits++;
                            _pointDefenseShots[^1]=_pointDefenseShots[^1] with { To=result.ShieldPoint??result.Point };
                        }
                        Log?.Hit(ship,result,BattleWeapon.DefenseDrone,time,shieldBefore);
                        RecordImpact(_shotSequence,ship,result,time,direction,def.ShipEnergy,shieldBefore,BattleWeapon.DefenseDrone);
                    }
                }
                if(target is not null && target.Health<=0 && _missiles.Remove(target))
                {
                    drones.Interceptions++;
                    Log?.EndAntimatter(target,AntimatterOutcome.Drone,time);
                    _ordnanceEvents.Add(new(OrdnanceEventKind.Intercepted,target.Position,time,target.Faction,target.Weapon));
                }
            }
        }
    }
}
