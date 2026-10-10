using System.Collections.Generic;
using Godot;

namespace SpaceFleet.View;

/// <summary>전투 장면의 하늘·톤매핑·조명. 게임과 함선 검토 장면(ModelGallery)이 같은 값을 쓴다.</summary>
public static class BattleLook
{
    public static Environment Environment() => new()
    {
        BackgroundMode = Godot.Environment.BGMode.Sky,
        Sky = new Sky
        {
            SkyMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/starfield.gdshader") },
            RadianceSize = Sky.RadianceSizeEnum.Size256,
        },
        AmbientLightSource = Godot.Environment.AmbientSource.Color,
        AmbientLightColor = new Color(0.24f, 0.28f, 0.36f),
        AmbientLightEnergy = 0.3f,
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
            LightEnergy = 2.0f,
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
            LightEnergy = 0.35f,
            LightColor = new Color(0.62f, 0.74f, 1f),
            SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly,
            Basis = Basis.LookingAt(new Vector3(0.85f, 0.15f, 0.5f).Normalized(), Vector3.Up),
        };
        // 행성 반사광 역할의 약한 보조광(하늘에 원반은 그리지 않는다)
        yield return new DirectionalLight3D
        {
            Name = "PlanetFill",
            LightEnergy = 0.18f,
            LightColor = new Color(0.55f, 0.62f, 0.85f),
            SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly,
            Basis = Basis.LookingAt(new Vector3(0.6f, 0.55f, 0.3f).Normalized(), Vector3.Up),
        };
    }
}
