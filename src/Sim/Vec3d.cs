using System;
using Godot;

namespace SpaceFleet.Sim;

/// <summary>
/// 배정밀도 위치 벡터. 시뮬레이션 좌표는 전부 이것으로 두고,
/// 렌더링할 때만 렌더 원점을 뺀 뒤 float(Vector3)로 내린다.
/// </summary>
public readonly record struct Vec3d(double X, double Y, double Z)
{
    public static readonly Vec3d Zero = new(0, 0, 0);

    public static Vec3d operator +(Vec3d a, Vec3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3d operator -(Vec3d a, Vec3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3d operator -(Vec3d a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3d operator *(Vec3d a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3d operator +(Vec3d a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public double Length() => Math.Sqrt(X * X + Y * Y + Z * Z);

    public Vector3 ToVector3() => new((float)X, (float)Y, (float)Z);

    public static Vec3d From(Vector3 v) => new(v.X, v.Y, v.Z);

    public static Vec3d Lerp(Vec3d a, Vec3d b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

    public override string ToString() => $"({X:0.0}, {Y:0.0}, {Z:0.0})";
}
