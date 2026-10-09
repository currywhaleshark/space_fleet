using Godot;
using System.Collections.Generic;
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
    public const float NormalFov = 70f;
    public const float TelescopeMagnification = 4f;
    public static float TelescopeFov => Mathf.RadToDeg(2 * Mathf.Atan(Mathf.Tan(Mathf.DegToRad(NormalFov / 2)) / TelescopeMagnification));
    public bool TelescopeHeld { get; private set; }
    public event System.Action<bool>? TelescopeChanged;
    public void SetTelescope(bool held)
    {
        if (held == TelescopeHeld) return;
        if (Mode == CameraMode.ShipFollow)
        {
            if (!held || !FreeLooking)
            {
                Vector3 local = _followOrientation.Inverse() * AimForward;
                _look = new Vector2(Mathf.Atan2(-local.X, -local.Z), Mathf.Asin(Mathf.Clamp(local.Y, -1, 1)));
            }
            FreeLookHoldRemaining = held ? 0 : FreeLookHoldSeconds;
            if (held) FreeLooking = false;
        }
        _pendingMouse = Vector2.Zero;
        TelescopeHeld = held;
        TelescopeChanged?.Invoke(held);
    }
    public void ResetTelescope() { SetTelescope(false); Fov = NormalFov; }
    private Vector2 _look;
    private Quaternion _followOrientation = Quaternion.Identity;
    public const float FreeLookHoldSeconds = 3f;
    public float FreeLookHoldRemaining { get; private set; }
    private sealed class Kick
    {
        public Vector3 Source;
        public float Strength, Age, Duration, Frequency;
    }
    private readonly List<Kick> _kicks = new();
    public Vector3 VisualRotation { get; private set; }
    public CameraMode Mode { get; set; }
    public bool FreeLooking { get; private set; }

    public void SetFreeLook(bool pressed)
    {
        if (pressed == FreeLooking) return;
        FreeLooking = pressed;
        FreeLookHoldRemaining = pressed ? 0 : FreeLookHoldSeconds;
        if (!pressed && Mode == CameraMode.ShipFollow)
        {
            // 마우스를 놓은 순간 실제로 보고 있던 상대 시점을 유지한다.
            Vector3 local = _followOrientation.Inverse() * AimForward;
            _look = new Vector2(Mathf.Atan2(-local.X, -local.Z), Mathf.Asin(Mathf.Clamp(local.Y, -1, 1)));
            _pendingMouse = Vector2.Zero;
        }
    }

    public float Sensitivity { get; set; } = 0.0022f;

    /// <summary>화면 중앙 조준선이 가리키는 월드 방향.</summary>
    public Vector3 AimForward => -_aim.Z;

    public void ClearImpacts() { _kicks.Clear(); VisualRotation = Vector3.Zero; }

    public void AddImpact(Vector3 sourceDirection, float strength, HullKind hull)
    {
        if (_kicks.Count >= 8) _kicks.RemoveAt(0);
        _kicks.Add(new Kick { Source = sourceDirection, Strength = strength,
            Duration = hull == HullKind.Battleship ? .7f : hull == HullKind.Escort ? .52f : .36f,
            Frequency = hull == HullKind.Battleship ? 10f : hull == HullKind.Escort ? 15f : 22f });
    }

    /// <summary>표현용 각도만 바꾼다. _aim·카메라 위치·함선 조종 입력은 보존한다.</summary>
    public void AdvanceImpacts(float delta, float amount)
    {
        VisualRotation = Vector3.Zero;
        foreach (var kick in _kicks)
        {
            kick.Age += delta;
            float t = kick.Age / kick.Duration;
            if (t >= 1) continue;
            Vector3 local = _aim.Inverse() * kick.Source;
            float attack = Mathf.Min(1, kick.Age / .025f);
            float envelope = attack * (1 - t) * (1 - t);
            float wave = Mathf.Cos(kick.Age * kick.Frequency);
            var axis = new Vector3(local.Y + .35f * local.Z, -local.X, -.45f * local.X);
            if (axis.LengthSquared() < .01f) axis = new Vector3(.3f, 0, .15f);
            float massScale = kick.Duration > .6f ? .65f : kick.Duration > .4f ? .82f : 1f;
            VisualRotation += axis * (Mathf.DegToRad(1.6f) * kick.Strength * massScale * envelope * wave);
        }
        _kicks.RemoveAll(kick => kick.Age >= kick.Duration);
        // 집중 포화도 1.8도 이상 누적하지 않는다. 0% 설정은 즉시 적용된다.
        VisualRotation = VisualRotation.LimitLength(Mathf.DegToRad(1.8f)) * Mathf.Clamp(amount, 0, 1);
    }

    public Vector2 AimScreenPosition() => UnprojectPosition(Position + AimForward * 100_000f);

    public override void _Ready()
    {
        Fov = NormalFov;
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
        FreeLookHoldRemaining = 0;
        _followOrientation = orientation;
        ClearImpacts();
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

    public void Follow(ShipDefinition definition, Vector3 shipPosition, Quaternion shipOrientation, float delta)
    {
        _followOrientation = shipOrientation;
        Fov = Mathf.Lerp(Fov, TelescopeHeld ? TelescopeFov : NormalFov, 1 - Mathf.Exp(-delta / .12f));
        float sens = Sensitivity * Mathf.Tan(Mathf.DegToRad(Fov / 2)) / Mathf.Tan(Mathf.DegToRad(NormalFov / 2));
        if (Mode == CameraMode.ShipFollow)
        {
            if (TelescopeHeld)
            {
                _look.X = Mathf.Wrap(_look.X - _pendingMouse.X * sens, -Mathf.Pi, Mathf.Pi);
                _look.Y = Mathf.Clamp(_look.Y - _pendingMouse.Y * sens, -Mathf.DegToRad(85), Mathf.DegToRad(85));
            }
            else if (FreeLooking)
            {
                _look.X = Mathf.Clamp(_look.X - _pendingMouse.X * sens, -Mathf.DegToRad(170), Mathf.DegToRad(170));
                _look.Y = Mathf.Clamp(_look.Y - _pendingMouse.Y * sens, -Mathf.DegToRad(80), Mathf.DegToRad(80));
            }
            else
            {
                float returning = Mathf.Max(0, delta - FreeLookHoldRemaining);
                FreeLookHoldRemaining = Mathf.Max(0, FreeLookHoldRemaining - delta);
                _look *= Mathf.Exp(-returning / 0.6f);
            }
            _pendingMouse = Vector2.Zero;
            Quaternion offsetRotation = new Quaternion(Vector3.Up, _look.X) * new Quaternion(Vector3.Right, _look.Y);
            _aim = new Basis(TelescopeHeld ? shipOrientation * offsetRotation
                : _aim.GetRotationQuaternion().Slerp(shipOrientation * offsetRotation, 1 - Mathf.Exp(-delta / 0.25f)));
            Place(definition, shipPosition, shipOrientation);
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

        Place(definition, shipPosition, shipOrientation);
    }

    private static Vector3 TelescopeOffset(ShipDefinition definition, Vector3 localAim)
    {
        float clearance = Mathf.Max(1, definition.Flight.Length * .01f);
        Vector3 outward = new Vector3(localAim.X, 0, localAim.Z).Normalized();
        if (outward.LengthSquared() < .01f) outward = Vector3.Forward;
        foreach (var section in definition.HullSections)
            if (section.Id == "bridge")
            {
                // A circle outside the bridge's corners stays clear at every azimuth, including astern.
                float radius = new Vector2(section.HalfSize.X, section.HalfSize.Z).Length() + clearance;
                return section.Center + Vector3.Up * section.HalfSize.Y * .6f + outward * radius;
            }
        // Small craft retain mouse-to-nose flight. Orbit outside their envelope during turn lag.
        float hullRadius = 0;
        foreach (var section in definition.HullSections)
            hullRadius = Mathf.Max(hullRadius, new Vector2(Mathf.Abs(section.Center.X) + section.HalfSize.X,
                Mathf.Abs(section.Center.Z) + section.HalfSize.Z).Length());
        return Vector3.Up * definition.Flight.CameraHeight * .25f + outward * (hullRadius + clearance);
    }

    private void Place(ShipDefinition definition, Vector3 shipPosition, Quaternion shipOrientation)
    {
        var shipClass = definition.Flight;
        var offset = new Vector3(0, shipClass.CameraHeight, shipClass.CameraDistance) * _zoom;
        // Cut to the exterior optic instead of interpolating through the ship's superstructure.
        // Orbit horizontally at bridge height; looking down can still naturally reveal/occlude the deck.
        Vector3 position = TelescopeHeld ? shipOrientation * TelescopeOffset(definition, shipOrientation.Inverse() * AimForward) : _aim * offset;
        Transform = new Transform3D(_aim * Basis.FromEuler(VisualRotation), shipPosition + position);
    }
}
