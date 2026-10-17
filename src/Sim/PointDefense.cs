using System;
using Godot;

namespace SpaceFleet.Sim;

public enum PointDefenseTarget { None, Missile, Interceptor, Drone, Probe }

/// <summary>Mechanical PD drive shared by interception and rendering; angles use the local deck frame.</summary>
public sealed class PointDefenseMountState
{
    private readonly Basis _inverseMountBasis;
    private readonly float _arcCos, _yawLimit, _minElevation, _maxElevation, _tolerance;
    private readonly int _solutionPhase;
    public const double SolutionInterval = 1.0 / 15;
    private object? _observedTarget;
    private int _observedPart;
    private Vec3d _observedPosition, _relativeVelocity;
    private Vector3 _observedVelocity, _observedAcceleration;
    private double _observedAt, _nextSolution, _sampleFlight, _flightRate;
    private bool _solutionValid;
    public double FlightSeconds { get; private set; }
    public uint SolutionCount { get; private set; }

    public PointDefenseMountState(PointDefenseDefinition definition, int index, uint phaseSeed = 0)
    {
        Definition = definition;
        _solutionPhase = (int)((phaseSeed + (uint)index * 7) % 16);
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

    // Called only for a locally visible candidate: existing range, sensor power,
    // mechanical sector and occlusion gates establish the PD director observation.
    // Between samples, aiming never reads fresh target motion. Each gun retains
    // one sample/solution; there are no simulated PD projectiles or per-shot jobs.
    internal bool PredictAim(object target, int part, Vec3d position, Vector3 velocity,
        Vec3d muzzle, Vector3 shooterVelocity, double time, out Vec3d aim, out Vec3d contact)
    {
        bool same = ReferenceEquals(target, _observedTarget) && part == _observedPart;
        if (!same || time + 1e-9 >= _nextSolution)
        {
            double elapsed = time - _observedAt;
            _observedAcceleration = same && elapsed >= 1.0 / 240 && elapsed <= SolutionInterval * 2
                ? ((velocity - _observedVelocity) / (float)elapsed).LimitLength(2000) : Vector3.Zero;
            _observedTarget = target; _observedPart = part;
            _observedPosition = position; _observedVelocity = velocity; _observedAt = time;
            _relativeVelocity = Vec3d.From(velocity - shooterVelocity);
            _solutionValid = FireControl.Intercept(position - muzzle, _relativeVelocity, Definition.MuzzleSpeed,
                Definition.RangeMeters * 2 / Definition.MuzzleSpeed, out double flight);
            _sampleFlight = _solutionValid ? flight : 0;
            Vec3d intercept = position - muzzle + _relativeVelocity * _sampleFlight;
            double closing = _relativeVelocity.Dot(intercept) / Math.Max(1, intercept.Length());
            // Linear time-of-flight interpolation keeps a constant-velocity lead
            // moving smoothly between solves; no fresh enemy state is required.
            _flightRate = _solutionValid ? Math.Clamp(closing / Math.Max(1, Definition.MuzzleSpeed - closing), -1, 4) : 0;
            // 16 collision substeps = 15 Hz. Stable phases distribute recurring
            // solves across ships and mounts, independently of render frame rate.
            long step = (long)Math.Floor(time * 240 + 1e-7);
            _nextSolution = (Math.Floor((step - _solutionPhase) / 16.0) * 16 + 16 + _solutionPhase) / 240.0;
            SolutionCount++;
            // Keep the gun's fractional cycle when changing between aligned
            // targets. Track/range/visibility gates clear it if fire is interrupted.
        }
        double age = Math.Clamp(time - _observedAt, 0, SolutionInterval * 2);
        FlightSeconds = Math.Max(0, _sampleFlight + _flightRate * age);
        contact = _observedPosition + Vec3d.From(_observedVelocity) * age;
        aim = contact + _relativeVelocity * FlightSeconds;
        return _solutionValid;
    }

    // Existing distance/size terms describe baseline dispersion. This factor
    // describes observed maneuver uncertainty, not constant crossing speed
    // (already compensated by lead and limited by physical turret slew).
    internal float PredictionFactor(Vector3 direction, double time, float sizeFactor)
    {
        float horizon = (float)(FlightSeconds + Math.Clamp(time - _observedAt, 0, SolutionInterval * 2));
        Vector3 lateral = _observedAcceleration - direction * _observedAcceleration.Dot(direction);
        float error = .5f * lateral.Length() * horizon * horizon;
        float tolerance = Definition.TrackingErrorMeters * sizeFactor;
        float ratio = error / tolerance;
        return 1 / (1 + ratio * ratio);
    }

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

    internal void ClearTracking()
    {
        LocalAim = null; FireAccumulator = 0; TargetKind=PointDefenseTarget.None;
        _observedTarget = null; _solutionValid = false; _observedAcceleration = Vector3.Zero;
        FlightSeconds = 0; _nextSolution = 0;
    }
    internal void Reset()
    {
        Traverse = Elevation = PreviousElevation = 0; PreviousYaw = Yaw;
        LastFiredAt = double.NegativeInfinity; ShotCount = SolutionCount = 0; ClearTracking();
    }
}
