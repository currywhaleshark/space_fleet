using System.Collections.Generic;
using Godot;

namespace SpaceFleet.View;

/// <summary>전투 장면의 하늘·톤매핑·조명. 게임과 함선 검토 장면(ModelGallery)이 같은 값을 쓴다.</summary>
public static class BattleLook
{
    // Rocks receive the sun and local lights, but not the readability lights for ships.
    public const uint AsteroidLayer = 1u << 1;
    /// <summary>
    /// 선체 금속성 배율. 반사할 것이 거의 없는 검은 하늘 아래에서 금속면은 해를 받아도 어둡게 남는다.
    /// 배율을 낮춰 확산광으로 형태가 읽히게 한다.
    /// </summary>
    public const float HullMetalScale = 0.45f;
    private const uint ShipFillLayers = 0xfffffu & ~AsteroidLayer;

    public static Environment Environment() => new()
    {
        BackgroundMode = Godot.Environment.BGMode.Sky,
        Sky = new Sky
        {
            SkyMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/starfield.gdshader") },
            RadianceSize = Sky.RadianceSizeEnum.Size256,
        },
        AmbientLightSource = Godot.Environment.AmbientSource.Color,
        AmbientLightColor = new Color(0.26f, 0.31f, 0.4f),
        AmbientLightEnergy = 0.55f,
        ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
        TonemapMode = Godot.Environment.ToneMapper.Agx,
        GlowEnabled = true,
        GlowIntensity = 0.7f,
        GlowBloom = 0.02f,
        GlowHdrThreshold = 1.4f,
        GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive,
    };

    public static IEnumerable<DirectionalLight3D> Lights()
    {
        yield return new DirectionalLight3D
        {
            Name = "Sun",
            LightEnergy = 2.4f,
            LightColor = new Color(1f, 0.95f, 0.88f),
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 6000f,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            Basis = Basis.LookingAt(new Vector3(-0.75f, -0.35f, -0.35f).Normalized(), Vector3.Up),
        };
        // 테두리광: 해 반대쪽에서 차갑게 비춰 그늘진 쪽 윤곽과 판 이음새가 읽히게 한다.
        yield return new DirectionalLight3D
        {
            Name = "Rim",
            LightCullMask = ShipFillLayers,
            LightEnergy = 0.9f,
            LightColor = new Color(0.62f, 0.74f, 1f),
            SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly,
            Basis = Basis.LookingAt(new Vector3(0.85f, 0.15f, 0.5f).Normalized(), Vector3.Up),
        };
        // 행성 반사광 역할의 약한 보조광(하늘에 원반은 그리지 않는다)
        yield return new DirectionalLight3D
        {
            Name = "PlanetFill",
            LightCullMask = ShipFillLayers,
            LightEnergy = 0.5f,
            LightColor = new Color(0.55f, 0.62f, 0.85f),
            SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly,
            Basis = Basis.LookingAt(new Vector3(0.6f, 0.55f, 0.3f).Normalized(), Vector3.Up),
        };
        // 시점 보조광: 카메라 조금 위에서 보는 방향으로 약하게 비춘다. 해를 등진 함선도 보이는 면의 형태가 읽힌다.
        // 방향은 AimViewFill이 카메라를 따라 맞춘다(기본: -Z).
        yield return new DirectionalLight3D
        {
            Name = ViewFillName,
            LightCullMask = ShipFillLayers,
            LightEnergy = 0.35f,
            LightColor = new Color(0.85f, 0.9f, 1f),
            SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly,
        };
    }

    public const string ViewFillName = "ViewFill";
    /// <summary>시점 보조광을 카메라 방향에서 약 20° 아래로 기울여 맞춘다(빛이 카메라 위쪽에서 온다).</summary>
    public static void AimViewFill(Node lightsParent, Basis camera)
    {
        if (lightsParent.GetNodeOrNull<DirectionalLight3D>(ViewFillName) is { } fill)
            fill.Basis = camera.Orthonormalized() * new Basis(Vector3.Right, -0.35f);
    }
}
