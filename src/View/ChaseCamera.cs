using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.View;

public enum CameraMode { MouseAim, ShipFollow }

/// <summary>
/// 마우스 조준 추적 카메라. 마우스는 "조준 방향"을 돌리고 함선은 그 방향으로 기수를 튼다.
/// 우주에는 위아래가 없으므로 카메라 수평선은 조종함의 위쪽(롤)을 따라간다.
/// </summary>
public partial class ChaseCamera : Camera3D
{
    private Basis _aim = Basis.Identity;
    private Vector2 _pendingMouse;
    private float _zoom = 1f;
    private Vector2 _look;
    public CameraMode Mode { get; set; }
    public bool FreeLooking { get; set; }

    public float Sensitivity { get; set; } = 0.0022f;

    /// <summary>화면 중앙 조준선이 가리키는 월드 방향.</summary>
    public Vector3 AimForward => -_aim.Z;

    public override void _Ready()
    {
        Fov = 70f;
        Near = 0.5f;
        // 1,000km. 이보다 먼 천체는 시야각을 유지한 채 당겨 그린다(ScaleTest.PlaceBackdrop).
        // 수억 m로 키우면 라이트 컬링의 절두체 계산이 깨진다.
        Far = 1.0e6f;
    }

    public void ResetAim(Quaternion orientation)
    {
        _aim = new Basis(orientation);
        _look = _pendingMouse = Vector2.Zero;
        FreeLooking = false;
    }

    /// <summary>현재 조준 기준으로 요/피치(도)만큼 돌린다. 스크린샷 연출용.</summary>
    public void Turn(float yawDeg, float pitchDeg)
    {
        if (Mode == CameraMode.ShipFollow)
        {
            if (yawDeg == 0 && pitchDeg == 0) return;
            _look += new Vector2(Mathf.DegToRad(yawDeg), Mathf.DegToRad(pitchDeg));
            _look.X = Mathf.Clamp(_look.X, -Mathf.DegToRad(170), Mathf.DegToRad(170));
            _look.Y = Mathf.Clamp(_look.Y, -Mathf.DegToRad(80), Mathf.DegToRad(80));
            FreeLooking = true;
            return;
        }
        _aim = _aim.Rotated(_aim.Y.Normalized(), Mathf.DegToRad(yawDeg));
        _aim = _aim.Rotated(_aim.X.Normalized(), Mathf.DegToRad(pitchDeg)).Orthonormalized();
    }

    /// <summary>검증 장면의 관찰 방향. 조함 입력에는 전달하지 않는다.</summary>
    public void Observe(Vector3 direction, Quaternion orientation)
    {
        if (Mode == CameraMode.MouseAim) { ResetAim(Basis.LookingAt(direction, orientation * Vector3.Up).GetRotationQuaternion()); return; }
        Vector3 local = (orientation.Inverse() * direction).Normalized();
        _look = new Vector2(Mathf.Atan2(-local.X, -local.Z), Mathf.Asin(Mathf.Clamp(local.Y, -1, 1)));
        FreeLooking = true;
    }

    public void AddMouse(Vector2 relative) => _pendingMouse += relative;

    public void Zoom(float factor) => _zoom = Mathf.Clamp(_zoom * factor, 0.35f, 4f);

    public void Follow(ShipClass shipClass, Vector3 shipPosition, Quaternion shipOrientation, float delta)
    {
        float sens = Sensitivity * Fov / 70f;
        if (Mode == CameraMode.ShipFollow)
        {
            if (FreeLooking)
            {
                _look.X = Mathf.Clamp(_look.X - _pendingMouse.X * sens, -Mathf.DegToRad(170), Mathf.DegToRad(170));
                _look.Y = Mathf.Clamp(_look.Y - _pendingMouse.Y * sens, -Mathf.DegToRad(80), Mathf.DegToRad(80));
            }
            else _look *= Mathf.Exp(-delta / 0.6f);
            _pendingMouse = Vector2.Zero;
            Quaternion offsetRotation = new Quaternion(Vector3.Up, _look.X) * new Quaternion(Vector3.Right, _look.Y);
            _aim = new Basis(_aim.GetRotationQuaternion().Slerp(shipOrientation * offsetRotation, 1 - Mathf.Exp(-delta / 0.25f)));
            Place(shipClass, shipPosition);
            return;
        }
        if (_pendingMouse != Vector2.Zero)
        {
            _aim = _aim.Rotated(_aim.Y.Normalized(), -_pendingMouse.X * sens);
            _aim = _aim.Rotated(_aim.X.Normalized(), -_pendingMouse.Y * sens);
            _pendingMouse = Vector2.Zero;
        }

        // 수평선 추종: 조준 전방축을 중심으로 돌려 카메라 위쪽을 함선 위쪽에 맞춘다.
        Vector3 forward = -_aim.Z;
        Vector3 shipUp = shipOrientation * Vector3.Up;
        Vector3 projected = shipUp - forward * forward.Dot(shipUp);
        if (projected.LengthSquared() > 0.05f)
        {
            float angle = _aim.Y.SignedAngleTo(projected.Normalized(), forward);
            _aim = _aim.Rotated(forward, angle * (1f - Mathf.Exp(-delta * 8f)));
        }
        _aim = _aim.Orthonormalized();

        Place(shipClass, shipPosition);
    }

    private void Place(ShipClass shipClass, Vector3 shipPosition)
    {
        var offset = new Vector3(0, shipClass.CameraHeight, shipClass.CameraDistance) * _zoom;
        Transform = new Transform3D(_aim, shipPosition + _aim * offset);
    }
}
