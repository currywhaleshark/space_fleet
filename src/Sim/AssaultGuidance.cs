using System;
using Godot;

namespace SpaceFleet.Sim;

/// <summary>Constant thrust interception with inherited carrier velocity, in metres and seconds.</summary>
internal static class AssaultGuidance
{
    internal static bool Intercept(Vector3 relative, Vector3 relativeVelocity, float ejectSpeed, float acceleration,
        double maxTime, out Vector3 direction, out double flight)
    {
        direction=relative.Normalized(); flight=0;
        double Error(double t) => (relative+relativeVelocity*(float)t).Length()-ejectSpeed*t-.5*acceleration*t*t;
        // Find the first crossing, including short terminal passes where the target moves behind us later.
        double low=0, high=0;
        for(int i=1;i<=24;i++)
        {
            high=maxTime*i/24;
            if(Error(high)<=0) break;
            low=high;
        }
        if(low==high) return false;
        for(int i=0;i<22;i++)
        {
            double middle=(low+high)*.5;
            if(Error(middle)>0) low=middle; else high=middle;
        }
        flight=(low+high)*.5;
        direction=(relative+relativeVelocity*(float)flight).Normalized();
        return direction.IsFinite() && direction.LengthSquared()>.5f;
    }
}

public sealed partial class SimWorld
{
    internal static Vector3 AntimatterLaunchDirection(ShipBody shooter, ShipBody target, SensorTrack track,
        Vector3? localAim, out double flight)
    {
        var def=shooter.Definition.Antimatter!.Flight;
        Vec3d start=shooter.Position+Vec3d.From(shooter.Orientation*def.LaunchPoint);
        Vector3 relative=(track.EstimatedPosition-start).ToVector3()+target.Orientation*(localAim??Vector3.Zero);
        Vector3 targetVelocity=track.Level>=TrackLevel.Identified
            ? target.Velocity+target.Orientation*target.AngularVelocity.Cross(localAim??Vector3.Zero) : Vector3.Zero;
        // A player may still waste a round beyond its lifetime. AI separately gates range and time.
        AssaultGuidance.Intercept(relative,targetVelocity-shooter.Velocity,def.EjectSpeed,def.Accel,30,out var direction,out flight);
        return direction;
    }
}
