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
        // Prepare on identification before the firing window. A short lock dip or the
        // defender rolling its stern cone must not restart the entire arming sequence.
        double prepareRange = def.RecommendedMaxMeters + 3000;
        double cancelRange = prepareRange + 2000;
        if (target.Class.Kind == HullKind.Interceptor || track.Level < TrackLevel.Identified || Time < brain.BreakUntil
            || range > cancelRange)
        { state.Cancel(); return; }
        if (state.Mode == AntimatterMode.Safe && range <= prepareRange) state.BeginArming();
        if (!state.Ready || track.Level < TrackLevel.Locked || range > def.RecommendedMaxMeters) return;
        Vector3 localAim = brain.AimModule?.Definition.Center ?? Vector3.Zero;
        Vec3d launch = ship.Position+Vec3d.From(ship.Orientation*def.Flight.LaunchPoint);
        Vector3 aim = (track.EstimatedPosition+Vec3d.From(track.ObservedOrientation*localAim)-launch).ToVector3();
        if (FriendlyInLine(ship,launch,aim.Normalized(),aim.Length())) return;
        if (!LaunchAntimatter(ship,target,localAim).Fired) return;
        brain.BreakDirection = (track.ObservedOrientation*InfiltrationSector + (ship.Orientation*Vector3.Right)*brain._side*.4f).Normalized();
        brain.BreakUntil = Time+6; brain.InRun=false; brain.Activity="AM 발사 · 이탈";
    }

    /// <summary>Use identified contacts only. Roll to bring dorsal defense toward an approaching payload carrier.</summary>
    private float AntimatterDefenseRoll(ShipBody ship)
    {
        if (ship.Class.Kind != HullKind.Battleship) return 0;
        var threat = _ships.Where(s=>s.Faction!=ship.Faction && !s.Damage.Destroyed)
            .Select(s=>(Ship:s, Track:Sensors.Track(ship.Faction,s)))
            .Where(s=>s.Track.Level>=TrackLevel.Identified && s.Ship.Definition.Antimatter is not null)
            .Where(s=>(s.Track.EstimatedPosition-ship.Position).Length()<10_000)
            .OrderBy(s=>(s.Track.EstimatedPosition-ship.Position).Length()).FirstOrDefault();
        if (threat.Ship is null) return 0;
        Vector3 local = ship.Orientation.Inverse()*(threat.Track.EstimatedPosition-ship.Position).ToVector3();
        if (new Vector2(local.X,local.Y).LengthSquared()<1) return 0;
        return Mathf.Clamp(Mathf.Atan2(local.X,local.Y)*2,-1,1);
    }
}
