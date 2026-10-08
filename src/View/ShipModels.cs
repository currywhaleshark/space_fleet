using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.View;

/// <summary>
/// Blender 제작 모델을 사용한다. 임포트 누락을 정적인 구형 모델로 숨기지 않는다.
/// 상부와 하부의 무장 배치를 일부러 다르게 둔다(롤 전투의 근거).
/// </summary>
public static class ShipModels
{
    public static ShipModel Build(ShipClass shipClass, Faction faction, int seed) =>
        ImportedShipModels.TryBuild(shipClass, faction, seed)
        ?? throw new System.InvalidOperationException($"Missing {shipClass.Kind} model import. Run tools/build.ps1 before playing.");

    // 약 1,200m. 전방(-Z) -680 ~ 후방 +580.
    private static ShipModel Battleship(Faction faction, int seed)
    {
        var b = new HullBuilder(Palette.For(faction, 140f), seed);

        // 주 선체와 뱃머리
        b.Taper(new(0, 0, 520), new(170, 120), new(0, 0, -450), new(150, 100));
        b.Taper(new(0, 0, -450), new(150, 100), new(0, -12, -680), new(36, 26));
        // 상부 갑판 융기부, 하부 용골
        b.Taper(new(0, 72, 420), new(96, 26), new(0, 66, -420), new(70, 14));
        b.Taper(new(0, -72, 380), new(116, 30), new(0, -66, -300), new(56, 14));
        // 측면 장갑 띠
        b.Box(new(88, -8, 60), new(10, 52, 760), b.Palette.Dark);
        b.Box(new(-88, -8, 60), new(10, 52, 760), b.Palette.Dark);

        // 함교 구조물
        b.Box(new(0, 120, 300), new(56, 70, 110));
        b.Box(new(0, 168, 284), new(92, 22, 54));
        b.Box(new(0, 190, 300), new(30, 22, 40), b.Palette.Dark);
        b.CylinderY(new(0, 230, 310), 2.5f, 70f, b.Palette.Dark, 8);
        b.LightRow(new(-44, 170, 256), new(44, 170, 256), 5f, 2.2f, 0.85f);
        b.LightRow(new(-27, 140, 244), new(27, 140, 244), 5f, 2.2f, 0.7f);

        // 상부 주포 3기(전방 2, 후방 1) / 하부 주포 2기(중앙) — 배치가 다르다.
        b.Turret(new(0, 76, -370), 2.0f, 3, ventral: false);
        b.Turret(new(0, 78, -200), 2.0f, 3, ventral: false);
        b.Turret(new(0, 82, 150), 2.0f, 3, ventral: false);
        b.Turret(new(0, -84, -160), 1.8f, 2, ventral: true);
        b.Turret(new(0, -84, 60), 1.8f, 2, ventral: true);

        // 측면 부포·근접방어 포대
        foreach (float z in new[] { -260f, -100f, 60f, 220f })
        {
            b.Turret(new(66, 56, z), 0.6f, 2, ventral: false);
            b.Turret(new(-66, 56, z), 0.6f, 2, ventral: false);
        }

        // 방열판(취약부): 좌우로 펼친 얇은 판
        foreach (float side in new[] { 1f, -1f })
        {
            b.Box(new(side * 170, 0, 330), new(170, 3, 150), b.Palette.Radiator);
            b.Box(new(side * 92, 0, 330), new(14, 14, 160), b.Palette.Dark);
        }

        // 엔진 블록
        b.Box(new(0, 0, 540), new(180, 130, 60), b.Palette.Dark);
        foreach (var (x, y) in new[] { (-48f, -32f), (48f, -32f), (-48f, 32f), (48f, 32f) })
            b.Engine(new(x, y, 580), 26f, 420f);

        // 센서 마스트(뱃머리 상부)
        b.Box(new(0, 64, -520), new(18, 30, 60), b.Palette.Dark);
        b.CylinderY(new(0, 100, -520), 1.8f, 60f, b.Palette.Dark, 8);

        // 그리블: 주 선체는 앞으로 갈수록 좁아지므로(반폭 85→75, 반높이 60→50)
        // 면을 따라 기울어진 v 벡터로 깐다. z -410..490 구간, 중앙 z=40.
        var topV = new Vector3(0, 9.28f, 900);
        b.Greebles(new(0, 55, 40), new(150, 0, 0), topV, Vector3.Up, 260, 5f, 26f);
        b.Greebles(new(0, 79, 0), new(64, 0, 0), new(0, 11.1f, 780), Vector3.Up, 140, 3f, 14f);
        foreach (float side in new[] { 1f, -1f })
        {
            var sideV = new Vector3(side * 9.28f, 0, 900);
            b.Greebles(new(side * 80, 30, 40), sideV, new(0, 36, 0), new(side, 0, 0), 140, 4f, 20f);
            b.Greebles(new(side * 80, -40, 40), sideV, new(0, 24, 0), new(side, 0, 0), 90, 4f, 18f);
        }
        b.Greebles(new(0, -55, 40), new(150, 0, 0), new(0, -9.28f, 900), Vector3.Down, 200, 5f, 24f);

        // 현창 열: 1km 선체 옆에 작은 불빛이 늘어서야 크기가 읽힌다.
        // 측면 x(z) = 75 + 10*(z+450)/970 위를 지나는 직선.
        foreach (float side in new[] { 1f, -1f })
        {
            for (int row = 0; row < 3; row++)
            {
                float y = 26f - row * 14f;
                b.LightRow(new(side * 75.9f, y, -420), new(side * 85.2f, y, 480), 9f, 2.2f, 0.45f);
            }
            b.NavLight(new(side * 255, 0, 330), 6f, red: side < 0);
        }

        // 보조 추진기: 선체 반폭·반높이(함수 75×50, 함미 85×60) 바로 바깥.
        b.RcsClusters(bowZ: -430, bowHalf: new(76, 51), sternZ: 470, sternHalf: new(86, 61), size: 8f, plume: 90f);

        return b.Finish();
    }

    // 약 300m. 전방 -185 ~ 후방 +135.
    private static ShipModel Escort(Faction faction, int seed)
    {
        var b = new HullBuilder(Palette.For(faction, 50f), seed);

        b.Taper(new(0, 0, 120), new(52, 36), new(0, 0, -110), new(44, 30));
        b.Taper(new(0, 0, -110), new(44, 30), new(0, -4, -185), new(10, 8));
        b.Taper(new(0, 22, 90), new(30, 10), new(0, 20, -80), new(22, 6));

        // 함교·레이더
        b.Box(new(0, 30, 40), new(20, 22, 36));
        b.Box(new(0, 44, 34), new(30, 7, 16));
        b.CylinderY(new(0, 56, 50), 1f, 18f, b.Palette.Dark, 8);
        b.Add(new SphereMesh { Radius = 7f, Height = 5f }, b.Palette.Dark, new Transform3D(Basis.Identity, new(0, 66, 50)));
        b.LightRow(new(-14, 45, 25.5f), new(14, 45, 25.5f), 2.2f, 1f, 0.8f);

        // 상부 포탑 1 + 수직발사기 블록, 하부 포탑 1
        b.Turret(new(0, 25, -70), 0.7f, 2, ventral: false);
        b.Box(new(0, 26, -20), new(24, 6, 30), b.Palette.Dark);
        b.Turret(new(0, -18, -30), 0.6f, 2, ventral: true);

        foreach (float side in new[] { 1f, -1f })
        {
            b.Box(new(side * 44, 0, 70), new(36, 1.5f, 46), b.Palette.Radiator);
            b.LightRow(new(side * 26.2f, 6, -90), new(side * 26.2f, 6, 100), 4f, 1f, 0.5f);
            b.NavLight(new(side * 62, 0, 70), 2.4f, red: side < 0);
        }

        b.Box(new(0, 0, 124), new(54, 38, 14), b.Palette.Dark);
        b.Engine(new(-14, 0, 134), 11f, 150f);
        b.Engine(new(14, 0, 134), 11f, 150f);

        b.Greebles(new(0, 18, 10), new(42, 0, 0), new(0, 0, 220), Vector3.Up, 110, 1.5f, 8f);
        b.Greebles(new(0, -18, 10), new(42, 0, 0), new(0, 0, 200), Vector3.Down, 80, 1.5f, 7f);
        foreach (float side in new[] { 1f, -1f })
            b.Greebles(new(side * 25, 0, 10), new(0, 0, 210), new(0, 28, 0), new(side, 0, 0), 60, 1.5f, 6f);

        b.RcsClusters(bowZ: -95, bowHalf: new(22.5f, 15.5f), sternZ: 105, sternHalf: new(26.5f, 18.5f), size: 3f, plume: 28f);

        return b.Finish();
    }

    // 약 30m. 전방 -18 ~ 후방 +12.
    private static ShipModel Interceptor(Faction faction, int seed)
    {
        var b = new HullBuilder(Palette.For(faction, 6f), seed);

        b.Taper(new(0, 0, 10), new(4.2f, 2.8f), new(0, -0.2f, -18), new(0.8f, 0.6f));
        // 델타익
        b.Taper(new(0, -0.2f, 9), new(20, 0.5f), new(0, -0.2f, -6), new(3.5f, 0.4f));
        // 수직 핀 2장
        foreach (float side in new[] { 1f, -1f })
        {
            b.Taper(new(side * 1.6f, 2.0f, 9), new(0.3f, 3.2f), new(side * 1.4f, 1.4f, 2), new(0.2f, 0.4f));
            b.NavLight(new(side * 10f, -0.2f, 8.6f), 0.5f, red: side < 0);
        }
        // 조종석
        b.Taper(new(0, 1.0f, -2), new(1.6f, 1.2f), new(0, 0.6f, -9), new(0.6f, 0.3f), b.Palette.Canopy);
        // 기수 고정 주포
        b.CylinderZ(new(0, -0.9f, -14), 0.22f, 0.18f, 10f, b.Palette.Dark, 8);
        // 측후방 소형 자동터렛
        b.CylinderY(new(0, -1.6f, 5), 0.8f, 0.6f, b.Palette.Dark, 10);

        b.Box(new(0, 0, 10.6f), new(4.4f, 3f, 1.2f), b.Palette.Dark);
        b.Engine(new(-1.1f, 0, 12), 0.9f, 9f);
        b.Engine(new(1.1f, 0, 12), 0.9f, 9f);

        b.Greebles(new(0, 1.35f, 4), new(3.2f, 0, 0), new(0, 0, 9), Vector3.Up, 18, 0.2f, 0.8f);

        // 측면 노즐은 델타익과 겹치지 않게 날개 위로 올린다.
        b.RcsClusters(bowZ: -11, bowHalf: new(0.85f, 0.6f), sternZ: 7, sternHalf: new(2.0f, 1.45f), size: 0.5f, plume: 5f, sideLift: 0.7f);

        return b.Finish();
    }
}
