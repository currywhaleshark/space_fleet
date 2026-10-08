using System;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public readonly record struct RadarContact(Vector3 Local, double Distance, float Uncertainty, bool Enemy, bool Identified, bool SignalLost = false);

/// <summary>조종함 기준의 실제 3D 상대 좌표. 거리 축척은 선형, 구 밖 표적은 별도 표식으로 그린다.</summary>
public sealed class TacticalRadar
{
    private static readonly float[] Ranges = { 2_000, 10_000, 50_000, 200_000, 500_000 };
    private int _rangeIndex = 3;
    public float Range => Ranges[_rangeIndex];
    public void Zoom(int step) => _rangeIndex = Math.Clamp(_rangeIndex + step, 0, Ranges.Length - 1);

    // 함선 전방/위쪽 기준 고정 사시도. 자유 관찰이나 피격 카메라에 끌려가지 않는다.
    private static readonly Basis View = new Basis(Vector3.Right, .58f) * new Basis(Vector3.Up, -.32f);
    public static Vector3 Project(Vector3 point)
    {
        Vector3 p = View * point;
        return new Vector3(p.X, -p.Y, p.Z);
    }

    public static RadarContact? Locate(ShipBody observer, Vec3d origin, Quaternion orientation,
        ShipBody target, Vec3d targetPosition, SensorTrack track, ContactSnapshot? memory = null)
    {
        if (target == observer) return null;
        bool enemy = target.Faction != observer.Faction;
        if (enemy && track.Level == TrackLevel.None && memory is null) return null;
        bool identified = !enemy || memory?.Identified == true || track.Level >= TrackLevel.Identified;
        bool destroyed = memory is null ? target.Damage.Destroyed : memory.State?.Destroyed == true;
        if (identified && destroyed) return null;
        // HUD와 같은 센서 추정 위치를 사용. 절대 좌표를 float로 바꾸기 전에 double로 뺀다.
        Vec3d relative = memory is null ? targetPosition + (enemy ? track.Offset : Vector3.Zero) - origin
            : memory.DisplayPosition(targetPosition) - origin;
        return new RadarContact(orientation.Inverse() * relative.ToVector3(), relative.Length(),
            enemy ? (memory?.Track ?? track).ErrorMeters : 0, enemy, identified, memory?.SignalLost == true);
    }
}
