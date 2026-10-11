using Godot;

namespace SpaceFleet.Sim;

public sealed partial class SimWorld
{
    // Value-type specialization lets the JIT remove terrain branches and hit
    // temporaries from empty-space projectile loops without duplicating physics.
    private readonly struct EmptyEnvironment { }
    private readonly struct FixedEnvironment { }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private bool TerrainBlocked(Vec3d start,Vec3d end)=>Obstacles.Count>0 && Obstacles.SegmentBlocked(start,end,out _);
    private void StopMissileAtObstacle(Missile missile,ObstacleHit hit,double time)
    {
        missile.TravelMeters-=(missile.Position-missile.PrevPosition).Length()*(1-hit.Fraction);
        missile.Position=hit.Position; missile.Health=0;
        Log?.Environment.Weapon(time,missile.Shooter.Callsign,hit,missile.Weapon);
        Log?.EndAntimatter(missile,AntimatterOutcome.Obstacle,time);
        _ordnanceEvents.Add(new(OrdnanceEventKind.Detonation,hit.Position,time,missile.Faction,missile.Weapon,missile.Velocity.Normalized()));
    }
    private bool BlockedSeeker(Missile missile)=>missile.SeekerTarget switch
    {
        ShipBody ship=>TerrainBlocked(missile.Position,ship.Position),
        Missile target=>TerrainBlocked(missile.Position,target.Position),
        Decoy decoy=>TerrainBlocked(missile.Position,decoy.Position),
        _=>false,
    };
}
