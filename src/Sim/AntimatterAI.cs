using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

public sealed partial class SimWorld
{
    private void EngageAntimatter(ShipBrain brain, ShipBody target, SensorTrack track)
    {
        var ship = brain.Ship; var state = ship.Ordnance.Antimatter;
        if (state.Definition is not { } def || state.Rounds == 0) return;
        if (state.Warning) { state.Jettison(); return; }
        double range = (track.EstimatedPosition-ship.Position).Length();
        bool inSector = (ship.Position-track.EstimatedPosition).ToVector3().AngleTo(target.Orientation*InfiltrationSector)
            < Mathf.DegToRad(InfiltrationConeDegrees);
        if (target.Class.Kind == HullKind.Interceptor || track.Level < TrackLevel.Locked || Time < brain.BreakUntil
            || !inSector || range > def.RecommendedMaxMeters+1500)
        { state.Cancel(); return; }
        if (state.Mode == AntimatterMode.Safe) state.BeginArming();
        if (!state.Ready || range > def.RecommendedMaxMeters) return;
        Vector3 localAim = brain.AimModule?.Definition.Center ?? Vector3.Zero;
        Vec3d launch = ship.Position+Vec3d.From(ship.Orientation*def.Flight.LaunchPoint);
        Vector3 aim = (track.EstimatedPosition+Vec3d.From(target.Orientation*localAim)-launch).ToVector3();
        if (FriendlyInLine(ship,launch,aim.Normalized(),aim.Length())) return;
        if (!LaunchAntimatter(ship,target,localAim).Fired) return;
        brain.BreakDirection = (target.Orientation*InfiltrationSector + (ship.Orientation*Vector3.Right)*brain._side*.4f).Normalized();
        brain.BreakUntil = Time+6; brain.InRun=false; brain.Activity="AM 발사 · 이탈";
    }

    /// <summary>Use identified contacts only. Roll to bring dorsal defense toward an approaching payload carrier.</summary>
    private float AntimatterDefenseRoll(ShipBody ship)
    {
        if (ship.Class.Kind != HullKind.Battleship) return 0;
        var threat = _ships.Where(s=>s.Faction!=ship.Faction && !s.Damage.Destroyed)
            .Select(s=>(Ship:s, Track:Sensors.Track(ship.Faction,s)))
            .Where(s=>s.Track.Level>=TrackLevel.Identified && s.Ship.Ordnance.Antimatter.Rounds>0)
            .Where(s=>(s.Track.EstimatedPosition-ship.Position).Length()<10_000)
            .OrderBy(s=>(s.Track.EstimatedPosition-ship.Position).Length()).FirstOrDefault();
        if (threat.Ship is null) return 0;
        Vector3 local = ship.Orientation.Inverse()*(threat.Track.EstimatedPosition-ship.Position).ToVector3();
        if (new Vector2(local.X,local.Y).LengthSquared()<1) return 0;
        return Mathf.Clamp(Mathf.Atan2(local.X,local.Y)*2,-1,1);
    }
}
