using Godot;

namespace SpaceFleet.Sim;

/// <summary>비행보조 ON일 때의 방식. 비행보조 OFF에는 적용되지 않는다.</summary>
public enum AssistStyle
{
    /// <summary>기수 선회를 속도에 묶고, 크게 꺾으면 자동 감속한다. 이동 방향이 기수를 크게 벗어나지 않는다.</summary>
    Aircraft,
    /// <summary>기수는 속도와 무관하게 돌고, 보조가 횡추력을 우선 써서 이동 방향을 기수 쪽으로 맞춘다.</summary>
    Space,
}

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
    public AssistStyle Style;
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
    public const float StandardGravity = 9.80665f;
    // 제동과 횡미끄럼 보정에 쓸 추력을 남긴다. 횡추력을 전부 선회에 쓰면 감속 중 경로가 뒤처진다.
    private const float TurnThrustFraction = 0.7f;

    public ShipBody(string callsign, ShipClass shipClass, Faction faction, ShipDefinition? definition = null)
    {
        Callsign = callsign;
        Class = shipClass;
        Faction = faction;
        Definition = definition ?? ShipDefinitions.For(shipClass.Kind);
        Damage = new ShipDamage(Definition, callsign);
        Railgun = Definition.Railgun is null ? null : new RailgunState(this);
        Power = new ShipPower(this);
    }

    public string Callsign { get; }
    public ShipClass Class { get; }
    public Faction Faction { get; }
    public ShipDefinition Definition { get; }
    public ShipDamage Damage { get; }
    public RailgunState? Railgun { get; }
    public ShipPower Power { get; }
    public CollisionHull Hull => Definition.Hull;
    /// <summary>이 함선이 진행한 시뮬레이션 시간(초). 월드에 처음부터 있던 함선은 SimWorld.Time과 같다.</summary>
    public double SimTime { get; private set; }
    public CollisionImpact? LastCollision { get; internal set; }

    public Vec3d Position;
    public Vec3d PrevPosition;
    public Vector3 Velocity;
    /// <summary>각 축의 추력을 합산한 뒤의 무게중심 병진 가속도.</summary>
    public Vector3 Acceleration { get; private set; }
    /// <summary>함선 로컬 축 기준 병진 가속도(-Z 전방). 메인 추진(전방)과 보조 추진기(횡·상하·제동)를 나누는 데 쓴다.</summary>
    public Vector3 LocalAcceleration { get; private set; }
    /// <summary>함선 로컬 축 기준 각가속도(rad/s²). 자세 제어 추진기 연출용.</summary>
    public Vector3 AngularAcceleration { get; private set; }
    public float GLoad => Acceleration.Length() / StandardGravity;
    public bool TurnBraking { get; private set; }
    public float AssistedTargetSpeed { get; private set; }
    public Quaternion Orientation = Quaternion.Identity;
    public Quaternion PrevOrientation = Quaternion.Identity;
    /// <summary>함선 로컬 축 기준 각속도(rad/s).</summary>
    public Vector3 AngularVelocity;

    public ShipControl Control = ShipControl.Idle;

    /// <summary>주추진기 출력 0..2(부스트 포함). 연출(엔진 화염)용.</summary>
    public float EngineOutput { get; private set; }

    public Vector3 Forward => Orientation * Vector3.Forward;
    public Vector3 Up => Orientation * Vector3.Up;

    /// <summary>기수와 실제 이동 방향 사이 각도(도). 거의 정지 상태면 0.</summary>
    public float DriftDegrees => Velocity.LengthSquared() > 1f ? Mathf.RadToDeg(Forward.AngleTo(Velocity)) : 0f;

    /// <summary>기수가 지금 방향을 유지할 때, 기수에 수직인 속도를 횡추력으로 없애는 데 걸리는 시간(초).</summary>
    public float LateralSettleSeconds
    {
        get
        {
            Vector3 v = Orientation.Inverse() * Velocity;
            float accel = Mathf.Min(Class.StrafeAccel * Damage.ManeuverFraction * Power.EngineEffect, Class.MaxAccelG * StandardGravity);
            return accel > 1e-4f ? new Vector2(v.X, v.Y).Length() / accel : float.PositiveInfinity;
        }
    }

    public void Place(Vec3d position, Quaternion orientation)
    {
        Position = PrevPosition = position;
        Orientation = PrevOrientation = orientation.Normalized();
        Velocity = Vector3.Zero;
        Acceleration = Vector3.Zero;
        LocalAcceleration = Vector3.Zero;
        AngularAcceleration = Vector3.Zero;
        EngineOutput = 0f;
        TurnBraking = false;
        AssistedTargetSpeed = 0f;
        LastCollision = null;
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
        SimTime += dt;
        Damage.Step(dt, Power.ShieldEffect);
        Power.Step(dt);
        Railgun?.Step(dt);

        StepRotation((float)dt);
        StepTranslation((float)dt);

        Position += Velocity * (float)dt;
    }

    private void StepRotation(float dt)
    {
        float maneuver = Damage.ManeuverFraction;
        float maxPitchYaw = Mathf.DegToRad(Class.PitchYawRateDeg) * maneuver;
        if (Control.FlightAssist && Control.Style == AssistStyle.Aircraft)
        {
            // a = v * ω. 고속에서는 넓게 선회하고, 감속하면서 선회가 빨라진다.
            float turnAccel = Mathf.Min(Class.StrafeAccel * maneuver, Class.MaxAccelG * StandardGravity) * TurnThrustFraction;
            maxPitchYaw = Mathf.Min(maxPitchYaw, turnAccel / Mathf.Max(Velocity.Length(), 1f));
        }
        float accPitchYaw = Mathf.DegToRad(Class.PitchYawAccelDeg) * maneuver;
        float maxRoll = Mathf.DegToRad(Class.RollRateDeg) * maneuver;
        float accRoll = Mathf.DegToRad(Class.RollAccelDeg) * maneuver;

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
        AngularAcceleration = dw / dt;

        float w = AngularVelocity.Length();
        if (w > 1e-7f)
            Orientation = (Orientation * new Quaternion(AngularVelocity / w, w * dt)).Normalized();
    }

    private void StepTranslation(float dt)
    {
        // 부스트의 대가는 폐열이다(추진 채널 소비가 늘어난다). 추진 채널 배율은 모든 병진 추력에 곱한다.
        float boost = Control.Boost ? Class.BoostMultiplier : 1f;
        float engine = Power.EngineEffect;
        float forwardAccel = Class.ForwardAccel * boost * Damage.PropulsionFraction * engine;
        float strafeAccel = Class.StrafeAccel * Damage.ManeuverFraction * engine;

        // 함선 로컬 속도. Godot 로컬 축: 우=+X, 상=+Y, 전방=-Z.
        Vector3 v = Orientation.Inverse() * Velocity;
        Vector3 thrust = new(Control.Thrust.X, Control.Thrust.Y, -Control.Thrust.Z);
        if (thrust.LengthSquared() > 1f)
            thrust = thrust.Normalized();

        Vector3 dv;
        TurnBraking = false;
        AssistedTargetSpeed = 0f;
        if (Control.FlightAssist)
        {
            // 비행보조: 입력이 가리키는 목표 속도로 맞추되 추진기 한계 안에서만.
            Vector3 target = thrust * Class.MaxSpeed * boost;
            if (Control.Style == AssistStyle.Aircraft && Control.Thrust.Z > 0f
                && Control.AimForward is Vector3 aim && aim.LengthSquared() > 1e-8f)
            {
                // 큰 선회 입력은 스로틀을 유지한 채 감속한다. 방향이 맞으면 자동으로 재가속한다.
                float aimAngle = Forward.AngleTo(aim);
                float driftAngle = Velocity.LengthSquared() > 1f ? Forward.AngleTo(Velocity) : 0f;
                float turnAmount = Mathf.SmoothStep(Mathf.DegToRad(5f), Mathf.DegToRad(60f), Mathf.Max(aimAngle, driftAngle));
                float turnAccel = Mathf.Min(strafeAccel, Class.MaxAccelG * StandardGravity) * TurnThrustFraction;
                float turnSpeed = turnAccel / Mathf.DegToRad(Class.PitchYawRateDeg);
                float cruiseSpeed = -target.Z;
                float forwardTarget = Mathf.Lerp(cruiseSpeed, Mathf.Min(cruiseSpeed, turnSpeed), turnAmount);
                target.Z = -forwardTarget;
                TurnBraking = turnAmount > 0.01f && -v.Z > forwardTarget + 1f;
            }
            AssistedTargetSpeed = target.Length();
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
        // 감속은 제동 추력, 후진 가속은 보조 추력. 부스트도 합산 G 상한을 넘지 않는다.
        float reverseAccel = v.Z < 0f ? Class.BrakeAccel * Damage.ManeuverFraction * engine : strafeAccel;
        dv.Z = Mathf.Clamp(dv.Z, -forwardAccel * dt, reverseAccel * dt);
        float maxDelta = Class.MaxAccelG * StandardGravity * dt;
        if (dv.LengthSquared() > maxDelta * maxDelta)
        {
            if (Control.FlightAssist && Control.Style == AssistStyle.Space)
            {
                // 횡미끄럼 제거를 먼저 하고 남는 G로 전후 가속한다. 기수 쪽으로 이동 방향이 빨리 모인다.
                var lateral = new Vector2(dv.X, dv.Y).LimitLength(maxDelta);
                float axial = Mathf.Sqrt(Mathf.Max(0f, maxDelta * maxDelta - lateral.LengthSquared()));
                dv = new Vector3(lateral.X, lateral.Y, Mathf.Clamp(dv.Z, -axial, axial));
            }
            else
                dv = dv.Normalized() * maxDelta;
        }

        EngineOutput = dv.Z < 0f && Damage.PropulsionFraction > 1e-6f
            ? Mathf.Clamp(-dv.Z / (Class.ForwardAccel * Damage.PropulsionFraction * dt), 0f, 2f) : 0f;

        // 속도를 자세와 함께 돌리지 않고 추력으로만 바꾼다. 보조 OFF·무입력이면 관성을 유지한다.
        Vector3 worldDelta = Orientation * dv;
        LocalAcceleration = dv / dt;
        Acceleration = worldDelta / dt;
        Velocity += worldDelta;
    }
}
