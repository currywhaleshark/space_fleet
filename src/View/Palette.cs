using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.View;

/// <summary>진영·함종별 재질 묶음.</summary>
public sealed class Palette
{
    private static ImageTexture? _panelTexture;
    private static Shader? _plumeShader;

    public required StandardMaterial3D Hull { get; init; }
    /// <summary>그리블용. 인스턴스 색으로 명암을 흩뿌린다.</summary>
    public required StandardMaterial3D Greeble { get; init; }
    public required StandardMaterial3D Dark { get; init; }
    public required StandardMaterial3D Light { get; init; }
    public required StandardMaterial3D NavRed { get; init; }
    public required StandardMaterial3D NavGreen { get; init; }
    public required StandardMaterial3D Glow { get; init; }
    public required ShaderMaterial Plume { get; init; }
    /// <summary>보조 추진기 분사. 메인 화염보다 희고 짧다.</summary>
    public required ShaderMaterial RcsPlume { get; init; }
    public required StandardMaterial3D Radiator { get; init; }
    public required StandardMaterial3D Canopy { get; init; }

    /// <param name="panelMeters">패널 텍스처 한 장이 덮는 길이(m). 함선 크기에 맞춰 키운다.</param>
    public static Palette For(Faction faction, float panelMeters, DesignFamily design = DesignFamily.Earth)
    {
        _panelTexture ??= MeshKit.PanelTexture();
        bool blue = faction == Faction.Blue;

        Color hull = blue ? new Color(0.46f, 0.49f, 0.54f) : new Color(0.36f, 0.30f, 0.29f);
        Color light = blue ? new Color(0.55f, 0.82f, 1.0f) : new Color(1.0f, 0.55f, 0.28f);
        Color engine = blue ? new Color(0.45f, 0.68f, 1.0f) : new Color(1.0f, 0.48f, 0.18f);
        if (design == DesignFamily.Mars) engine = new Color(1f, .32f, .065f);
        float uv = 1f / panelMeters;

        return new Palette
        {
            Hull = new StandardMaterial3D
            {
                AlbedoColor = hull,
                AlbedoTexture = _panelTexture,
                Metallic = 0.55f,
                Roughness = 0.62f,
                RoughnessTexture = _panelTexture,
                Uv1Triplanar = true,
                Uv1TriplanarSharpness = 4f,
                Uv1Scale = new Vector3(uv, uv, uv),
            },
            Greeble = new StandardMaterial3D
            {
                AlbedoColor = hull * 0.9f,
                VertexColorUseAsAlbedo = true,
                Metallic = 0.5f,
                Roughness = 0.55f,
            },
            Dark = new StandardMaterial3D
            {
                AlbedoColor = hull * 0.42f,
                Metallic = 0.6f,
                Roughness = 0.45f,
            },
            Light = Emissive(light, 5f),
            NavRed = Emissive(new Color(1f, 0.15f, 0.12f), 6f),
            NavGreen = Emissive(new Color(0.2f, 1f, 0.35f), 6f),
            Glow = Emissive(engine, 2.2f),
            Plume = PlumeMaterial(engine),
            RcsPlume = PlumeMaterial(new Color(0.85f, 0.92f, 1f), intensity: 2.6f),
            Radiator = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.16f, 0.15f, 0.15f),
                AlbedoTexture = _panelTexture,
                Uv1Triplanar = true,
                Uv1Scale = new Vector3(uv * 3f, uv * 3f, uv * 3f),
                Metallic = 0.4f,
                Roughness = 0.6f,
                EmissionEnabled = true,
                Emission = new Color(1f, 0.3f, 0.1f),
                EmissionEnergyMultiplier = 0.12f,
            },
            Canopy = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.05f, 0.08f, 0.1f),
                Metallic = 0.9f,
                Roughness = 0.08f,
                EmissionEnabled = true,
                Emission = light,
                EmissionEnergyMultiplier = 0.8f,
            },
        };
    }

    private static ShaderMaterial PlumeMaterial(Color engine, float intensity = 1.1f)
    {
        _plumeShader ??= GD.Load<Shader>("res://shaders/plume.gdshader");
        var mat = new ShaderMaterial { Shader = _plumeShader };
        mat.SetShaderParameter("color", engine);
        mat.SetShaderParameter("intensity", intensity);
        return mat;
    }

    private static StandardMaterial3D Emissive(Color c, float energy) => new()
    {
        AlbedoColor = Colors.Black,
        EmissionEnabled = true,
        Emission = c,
        EmissionEnergyMultiplier = energy,
    };
}
