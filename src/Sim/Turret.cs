using Godot;

namespace SpaceFleet.Sim;

/// <summary>Local mount frame: -Z forward, +Y away from the deck; inverted for ventral turrets.</summary>
public sealed record TurretDefinition(string ModuleId, Vector3 Pivot, Vector3 Trunnion, Vector3[] Muzzles,
    bool Ventral, float YawDegrees, float MinElevation, float MaxElevation,
    float YawRate, float ElevationRate, int Rounds, float ToleranceDegrees = .03f,
    Vector3 HousingCenter = default, Vector3 HousingHalfSize = default)
{
    public Basis MountBasis => Ventral ? new Basis(Vector3.Forward, Mathf.Pi) : Basis.Identity;
    public Basis AimBasis(float yaw, float pitch) => MountBasis * new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, pitch);
    public Vector3 Breech(float yaw) => Pivot + MountBasis * new Basis(Vector3.Up, yaw) * Trunnion;
    public Vector3 Muzzle(float yaw, float pitch, int barrel) => Breech(yaw) + AimBasis(yaw, pitch) * Muzzles[barrel];
    public Vector2 Angles(Vector3 localDirection)
    {
        Vector3 d = MountBasis.Inverse() * localDirection.Normalized();
        return new(Mathf.Atan2(-d.X, -d.Z), Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()));
    }
    public bool Contains(Vector3 localDirection)
    {
        Vector2 angles = Angles(localDirection);
        return Mathf.Abs(angles.X) <= Mathf.DegToRad(YawDegrees) + 1e-6f
            && angles.Y >= Mathf.DegToRad(MinElevation) - 1e-6f && angles.Y <= Mathf.DegToRad(MaxElevation) + 1e-6f;
    }
}
