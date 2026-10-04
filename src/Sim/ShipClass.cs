namespace SpaceFleet.Sim;

public enum Faction
{
    Blue,
    Red,
}

public enum HullKind
{
    Battleship,
    Escort,
    Interceptor,
}

/// <summary>
/// 함종 데이터. 가속은 m/s², 각속도는 도/초 단위로 적고 런타임에 라디안으로 바꾼다.
/// </summary>
public sealed record ShipClass(
    HullKind Kind,
    string DisplayName,
    float Length,
    float ForwardAccel,
    float StrafeAccel,
    float MaxSpeed,
    float BoostMultiplier,
    float PitchYawRateDeg,
    float PitchYawAccelDeg,
    float RollRateDeg,
    float RollAccelDeg,
    float CameraDistance,
    float CameraHeight)
{
    // 전함은 180도 롤에 약 20초. 사각으로 들어온 적에게 롤로 응수하는 감각의 기준값.
    public static readonly ShipClass Battleship = new(
        HullKind.Battleship, "전함", 1200f,
        ForwardAccel: 3f, StrafeAccel: 1.2f, MaxSpeed: 90f, BoostMultiplier: 1.5f,
        PitchYawRateDeg: 6f, PitchYawAccelDeg: 2f, RollRateDeg: 12f, RollAccelDeg: 3f,
        CameraDistance: 2400f, CameraHeight: 480f);

    public static readonly ShipClass Escort = new(
        HullKind.Escort, "호위함", 300f,
        ForwardAccel: 14f, StrafeAccel: 6f, MaxSpeed: 220f, BoostMultiplier: 1.6f,
        PitchYawRateDeg: 22f, PitchYawAccelDeg: 14f, RollRateDeg: 35f, RollAccelDeg: 25f,
        CameraDistance: 620f, CameraHeight: 120f);

    public static readonly ShipClass Interceptor = new(
        HullKind.Interceptor, "요격함", 30f,
        ForwardAccel: 60f, StrafeAccel: 30f, MaxSpeed: 380f, BoostMultiplier: 2f,
        PitchYawRateDeg: 110f, PitchYawAccelDeg: 360f, RollRateDeg: 200f, RollAccelDeg: 600f,
        CameraDistance: 42f, CameraHeight: 9f);
}
