using Godot;

namespace SpaceFleet.Sim;

/// <summary>
/// 한 틱 동안 함선에 들어가는 조종 입력.
/// Thrust는 함선 기준 (x=우, y=상, z=전방), 각 성분 -1..1.
/// </summary>
public struct ShipControl
{
    public Vector3 Thrust;
    /// <summary>+1 = 우롤.</summary>
    public float Roll;
    public bool Boost;
    public bool FlightAssist;
    /// <summary>기수를 돌릴 월드 방향. null이면 현재 자세 유지.</summary>
    public Vector3? AimForward;

    public static ShipControl Idle => new() { FlightAssist = true };
}

/// <summary>
/// 6DOF 함선 강체. 고정 틱으로만 전진하며 Godot 노드에 의존하지 않는다.
/// 위치는 double, 속도·자세는 float(정밀도가 필요 없는 양).
/// </summary>
public sealed class ShipBody
{
    public ShipBody(string callsign, ShipClass shipClass, Faction faction)
    {
        Callsign = callsign;
        Class = shipClass;
        Faction = faction;
    }

    public string Callsign { get; }
    public ShipClass Class { get; }
    public Faction Faction { get; }

    public Vec3d Position;
    public Vec3d PrevPosition;
    public Vector3 Velocity;
    public Quaternion Orientation = Quaternion.Identity;
    public Quaternion PrevOrientation = Quaternion.Identity;
    /// <summary>함선 로컬 축 기준 각속도(rad/s).</summary>
    public Vector3 AngularVelocity;

    public ShipControl Control = ShipControl.Idle;

    /// <summary>주추진기 출력 0..1. 연출(엔진 화염)용.</summary>
    public float EngineOutput { get; private set; }

    public Vector3 Forward => Orientation * Vector3.Forward;
    public Vector3 Up => Orientation * Vector3.Up;

    public void Place(Vec3d position, Quaternion orientation)
    {
        Position = PrevPosition = position;
        Orientation = PrevOrientation = orientation.Normalized();
        Velocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
    }

    /// <summary>위치를 순간이동시킨다. 보간이 튀지 않도록 이전 상태도 같이 옮긴다.</summary>
    public void Teleport(Vec3d offset)
    {
        Position += offset;
        PrevPosition += offset;
    }

    public Vec3d InterpolatedPosition(double alpha) => Vec3d.Lerp(PrevPosition, Position, alpha);

    public Quaternion InterpolatedOrientation(float alpha) => PrevOrientation.Slerp(Orientation, alpha);

    public void Step(double dt)
    {
        PrevPosition = Position;
        PrevOrientation = Orientation;

        StepRotation((float)dt);
        StepTranslation((float)dt);

        Position += Velocity * (float)dt;
    }

    private void StepRotation(float dt)
    {
        float maxPitchYaw = Mathf.DegToRad(Class.PitchYawRateDeg);
        float accPitchYaw = Mathf.DegToRad(Class.PitchYawAccelDeg);
        float maxRoll = Mathf.DegToRad(Class.RollRateDeg);
        float accRoll = Mathf.DegToRad(Class.RollAccelDeg);

        Vector3 desired = Vector3.Zero;
        if (Control.AimForward is Vector3 aim && aim.LengthSquared() > 1e-8f)
        {
            // 목표 방향을 함선 로컬로. 로컬 전방은 -Z.
            Vector3 t = (Orientation.Inverse() * aim).Normalized();
            float angle = Mathf.Acos(Mathf.Clamp(-t.Z, -1f, 1f));
            // cross(-Z, t) = (t.y, -t.x, 0): 피치(X)·요(Y)만 쓰고 롤은 건드리지 않는다.
            Vector3 axis = new(t.Y, -t.X, 0f);
            if (axis.LengthSquared() > 1e-10f)
                axis = axis.Normalized();
            else
                axis = angle > 1f ? Vector3.Up : Vector3.Zero;

            // 최대 각가속으로 멈출 수 있는 속도까지만 돌린다(오버슈트 방지).
            float rate = Mathf.Min(maxPitchYaw, Mathf.Sqrt(2f * accPitchYaw * angle) * 0.85f);
            rate = Mathf.Min(rate, angle * 8f);
            desired = axis * rate;
        }

        // 로컬 +Z 축 양의 회전 = 좌롤.
        desired.Z = -Control.Roll * maxRoll;

        Vector3 dw = desired - AngularVelocity;
        float stepPY = accPitchYaw * dt;
        float stepRoll = accRoll * dt;
        dw.X = Mathf.Clamp(dw.X, -stepPY, stepPY);
        dw.Y = Mathf.Clamp(dw.Y, -stepPY, stepPY);
        dw.Z = Mathf.Clamp(dw.Z, -stepRoll, stepRoll);
        AngularVelocity += dw;

        float w = AngularVelocity.Length();
        if (w > 1e-7f)
            Orientation = (Orientation * new Quaternion(AngularVelocity / w, w * dt)).Normalized();
    }

    private void StepTranslation(float dt)
    {
        float boost = Control.Boost ? Class.BoostMultiplier : 1f;
        float forwardAccel = Class.ForwardAccel * boost;
        float strafeAccel = Class.StrafeAccel;

        // 함선 로컬 속도. Godot 로컬 축: 우=+X, 상=+Y, 전방=-Z.
        Vector3 v = Orientation.Inverse() * Velocity;
        Vector3 thrust = new(Control.Thrust.X, Control.Thrust.Y, -Control.Thrust.Z);

        Vector3 dv;
        if (Control.FlightAssist)
        {
            // 비행보조: 입력이 가리키는 목표 속도로 맞추되 추진기 한계 안에서만.
            Vector3 target = thrust * Class.MaxSpeed * boost;
            dv = target - v;
        }
        else
        {
            dv = new Vector3(
                thrust.X * strafeAccel,
                thrust.Y * strafeAccel,
                thrust.Z < 0f ? thrust.Z * forwardAccel : thrust.Z * strafeAccel) * dt;
        }

        dv.X = Mathf.Clamp(dv.X, -strafeAccel * dt, strafeAccel * dt);
        dv.Y = Mathf.Clamp(dv.Y, -strafeAccel * dt, strafeAccel * dt);
        // 전방 가속은 주추진기, 감속·후진은 약한 역추진기.
        dv.Z = Mathf.Clamp(dv.Z, -forwardAccel * dt, strafeAccel * dt);

        EngineOutput = dv.Z < 0f ? Mathf.Clamp(-dv.Z / (Class.ForwardAccel * dt), 0f, 2f) : 0f;

        Velocity = Orientation * (v + dv);
    }
}
