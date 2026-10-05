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
/// 함종 데이터. 가속은 m/s², MaxAccelG는 무게중심 병진 가속도의 합산 G 상한이다.
/// 각속도는 도/초 단위로 적고 런타임에 라디안으로 바꾼다.
/// </summary>
public sealed record ShipClass(
    HullKind Kind,
    string DisplayName,
    float Length,
    float MassKg,
    float ForwardAccel,
    float StrafeAccel,
    float BrakeAccel,
    float MaxAccelG,
    float MaxSpeed,
    float BoostMultiplier,
    float PitchYawRateDeg,
    float PitchYawAccelDeg,
    float RollRateDeg,
    float RollAccelDeg,
    float CameraDistance,
    float CameraHeight)
{
    public static ShipClass Battleship => ShipDefinitions.For(HullKind.Battleship).Flight;
    public static ShipClass Escort => ShipDefinitions.For(HullKind.Escort).Flight;
    public static ShipClass Interceptor => ShipDefinitions.For(HullKind.Interceptor).Flight;
}
