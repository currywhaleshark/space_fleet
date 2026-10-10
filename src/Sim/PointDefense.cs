using Godot;

namespace SpaceFleet.Sim;

public enum PointDefenseTarget { None, Missile, Interceptor, Drone }

/// <summary>Mechanical PD drive shared by interception and rendering; angles use the local deck frame.</summary>
public sealed class PointDefenseMountState
{
    private readonly Basis _inverseMountBasis;
    private readonly float _arcCos, _yawLimit, _minElevation, _maxElevation, _tolerance;
    public PointDefenseMountState(PointDefenseDefinition definition, int index)
    {
        Definition = definition;
        MountBasis = definition.Mounts[index].Y < 0 ? new Basis(Vector3.Forward, Mathf.Pi) : Basis.Identity;
        _inverseMountBasis = MountBasis.Inverse();
        _arcCos = Mathf.Cos(Mathf.DegToRad(definition.ArcDegrees));
        _yawLimit = Mathf.DegToRad(definition.YawDegrees);
        _minElevation = Mathf.DegToRad(definition.MinElevation);
        _maxElevation = Mathf.DegToRad(definition.MaxElevation);
        _tolerance = Mathf.DegToRad(definition.ToleranceDegrees);
        Normal = definition.Normals?[index].Normalized();
        Vector3 facing = _inverseMountBasis * (Normal ?? MountBasis * Vector3.Forward);
        // A purely dorsal/ventral normal has no azimuth: face that mount's fore/aft position instead.
        if (new Vector2(facing.X, facing.Z).LengthSquared() < 1e-6f)
            facing = _inverseMountBasis * new Vector3(definition.Mounts[index].X, 0, definition.Mounts[index].Z);
        CenterYaw = new Vector2(facing.X, facing.Z).LengthSquared() < 1e-6f ? 0 : Mathf.Atan2(-facing.X, -facing.Z);
        Reset();
    }

    public PointDefenseDefinition Definition { get; }
    public Basis MountBasis { get; }
    public Vector3? Normal { get; }
    public float CenterYaw { get; }
    public float Traverse { get; private set; }
    public float Yaw => CenterYaw + Traverse;
    public float Elevation { get; private set; }
    public float PreviousYaw { get; internal set; }
    public float PreviousElevation { get; internal set; }
    public Vector3 LocalDirection => MountBasis * new Basis(Vector3.Up, Yaw) * new Basis(Vector3.Right, Elevation) * Vector3.Forward;
    public Vector3? LocalAim { get; private set; }
    public bool Aligned => LocalAim is Vector3 aim && Contains(aim)
        && LocalDirection.AngleTo(aim) <= _tolerance;
    public double LastFiredAt { get; internal set; }
    public uint ShotCount { get; internal set; }
    public PointDefenseTarget TargetKind { get; internal set; }
    internal float FireAccumulator;

    private Vector2 Angles(Vector3 local)
    {
        Vector3 direction = _inverseMountBasis * local;
        float yaw = Mathf.Atan2(-direction.X, -direction.Z);
        return new(Mathf.Wrap(yaw - CenterYaw, -Mathf.Pi, Mathf.Pi),
            Mathf.Atan2(direction.Y, new Vector2(direction.X, direction.Z).Length()));
    }

    public bool Contains(Vector3 local)
    {
        if (!local.IsFinite() || local.LengthSquared() < 1e-8f) return false;
        local = local.Normalized();
        if (Normal is Vector3 normal && normal.Dot(local) < _arcCos) return false;
        Vector2 angles = Angles(local);
        return Mathf.Abs(angles.X) <= _yawLimit + 1e-6f
            && angles.Y >= _minElevation - 1e-6f
            && angles.Y <= _maxElevation + 1e-6f;
    }

    internal void Track(Vector3 local, double dt, float power)
    {
        LocalAim = local.Normalized();
        Vector2 desired = Angles(LocalAim.Value);
        float limit = _yawLimit;
        float driveTime = (float)dt * Mathf.Clamp(power, 0, 1); // Power boosts never exceed rated mechanical speed.
        Traverse = Mathf.MoveToward(Traverse, Mathf.Clamp(desired.X, -limit, limit), Mathf.DegToRad(Definition.YawRate) * driveTime);
        Elevation = Mathf.MoveToward(Elevation,
            Mathf.Clamp(desired.Y, _minElevation, _maxElevation),
            Mathf.DegToRad(Definition.ElevationRate) * driveTime);
        // No shortest-path wrap through the mechanical stops, and no stored burst after losing alignment.
        if (!Aligned) FireAccumulator = 0;
    }

    internal void ClearTracking() { LocalAim = null; FireAccumulator = 0; TargetKind=PointDefenseTarget.None; }
    internal void Reset()
    {
        Traverse = Elevation = PreviousElevation = 0; PreviousYaw = Yaw;
        LastFiredAt = double.NegativeInfinity; ShotCount = 0; ClearTracking();
    }
}
