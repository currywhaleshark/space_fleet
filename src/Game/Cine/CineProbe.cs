using System;
using System.Collections.Generic;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game.Cine;

/// <summary>
/// 티저 근접 촬영용 정찰 프로브 외형과 센서 작동 맥동. 시뮬레이션 프로브 위치를 따라가며 판정에는 관여하지 않는다.
/// 비행 중에는 접힌 센서 날개, 정지(OnStation) 뒤에는 날개가 펴지고 접시가 돌며 깜박이는 표지등과 퍼져 나가는 탐지 파동을 낸다.
/// </summary>
public partial class CineProbe : Node3D
{
    private const string PulseShader = @"
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;
uniform vec4 tint : source_color = vec4(0.35, 0.8, 1.0, 1.0);
uniform float fade = 1.0;
void fragment() {
    ALBEDO = tint.rgb;
    EMISSION = tint.rgb * 2.5;
    ALPHA = clamp(fade, 0.0, 1.0);
}";

    private Node3D _body = null!, _dish = null!;
    private readonly List<Node3D> _wings = new();
    private MeshInstance3D _beacon = null!;
    private StandardMaterial3D _beaconMaterial = null!;
    private readonly List<(MeshInstance3D Node, ShaderMaterial Material, double Born)> _pulses = new();
    private TorusMesh _pulseMesh = null!;
    private Shader _pulseShader = null!;
    private double _stationAt = -1;

    public ReconProbe? Probe { get; set; }
    /// <summary>정지 후 경과(시뮬레이션 초). 정지 전이면 음수.</summary>
    public double SinceStation(double time) => _stationAt < 0 ? -1 : time - _stationAt;
    public event Action<Vec3d>? Pinged;

    public override void _Ready()
    {
        var hull = new StandardMaterial3D { AlbedoColor = new Color(.13f, .15f, .17f), Metallic = .7f, Roughness = .45f };
        var plate = new StandardMaterial3D { AlbedoColor = new Color(.32f, .34f, .36f), Metallic = .6f, Roughness = .5f };
        var panel = new StandardMaterial3D { AlbedoColor = new Color(.05f, .09f, .16f), Metallic = .4f, Roughness = .3f,
            EmissionEnabled = true, Emission = new Color(.1f, .3f, .6f), EmissionEnergyMultiplier = .4f };
        _beaconMaterial = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(.5f, .85f, 1f), EmissionEnabled = true, Emission = new Color(.5f, .85f, 1f), EmissionEnergyMultiplier = 6 };
        _body = new Node3D(); AddChild(_body);
        Add(_body, new CylinderMesh { TopRadius = .55f, BottomRadius = .7f, Height = 2.6f, RadialSegments = 12 }, hull,
            new Transform3D(new Basis(Vector3.Right, Mathf.Pi / 2), Vector3.Zero));
        Add(_body, new CylinderMesh { TopRadius = .72f, BottomRadius = .72f, Height = .25f, RadialSegments = 12 }, plate,
            new Transform3D(new Basis(Vector3.Right, Mathf.Pi / 2), new Vector3(0, 0, .9f)));
        Add(_body, new CylinderMesh { TopRadius = .25f, BottomRadius = .45f, Height = .5f, RadialSegments = 10 }, plate,
            new Transform3D(new Basis(Vector3.Right, Mathf.Pi / 2), new Vector3(0, 0, 1.5f)));
        // 접힌 센서 날개 4장(정지 후 펴짐).
        for (int i = 0; i < 4; i++)
        {
            var hinge = new Node3D { Basis = new Basis(Vector3.Forward, i * Mathf.Pi / 2) };
            _body.AddChild(hinge);
            var wing = new Node3D { Position = new Vector3(.6f, 0, 0) };
            hinge.AddChild(wing);
            Add(wing, new BoxMesh { Size = new Vector3(2.4f, .05f, 1.6f) }, panel, new Transform3D(Basis.Identity, new Vector3(1.2f, 0, 0)));
            Add(wing, new BoxMesh { Size = new Vector3(2.5f, .08f, .08f) }, plate, new Transform3D(Basis.Identity, new Vector3(1.2f, .04f, -.8f)));
            _wings.Add(wing);
        }
        _dish = new Node3D { Position = new Vector3(0, 0, -1.4f) }; _body.AddChild(_dish);
        Add(_dish, new CylinderMesh { TopRadius = .9f, BottomRadius = .2f, Height = .35f, RadialSegments = 16 }, plate,
            new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2), Vector3.Zero));
        Add(_dish, new CylinderMesh { TopRadius = .04f, BottomRadius = .04f, Height = 1.1f, RadialSegments = 6 }, plate,
            new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2), new Vector3(0, 0, -.55f)));
        _beacon = Add(_body, new SphereMesh { Radius = .14f, Height = .28f }, _beaconMaterial, new Transform3D(Basis.Identity, new Vector3(0, .75f, .2f)));
        _pulseMesh = new TorusMesh { InnerRadius = .982f, OuterRadius = 1f, Rings = 96, RingSegments = 6 };
        _pulseShader = new Shader { Code = PulseShader };
    }

    private static MeshInstance3D Add(Node3D parent, Mesh mesh, Material material, Transform3D xf)
    {
        var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Transform = xf };
        parent.AddChild(node);
        return node;
    }

    /// <summary>시뮬레이션 틱마다: 정지 시각을 기록한다(화면을 그리지 않고 건너뛸 때도 시각이 맞도록).</summary>
    public void Track(double time)
    {
        if (_stationAt < 0 && Probe is { OnStation: true }) _stationAt = time;
    }

    /// <summary>매 프레임: 렌더 원점 기준 위치·자세와 전개·맥동 상태.</summary>
    public void Sync(Vec3d origin, double alpha, double time)
    {
        if (Probe is not { } p) { Visible = false; return; }
        Visible = true;
        Vec3d at = Vec3d.Lerp(p.PreviousPosition, p.Position, alpha);
        Position = (at - origin).ToVector3();
        Vector3 forward = p.Velocity.LengthSquared() > 4 ? p.Velocity.Normalized() : p.Direction;
        if (forward.LengthSquared() > .5f)
            Basis = Basis.LookingAt(forward, Mathf.Abs(forward.Dot(Vector3.Up)) > .95f ? Vector3.Right : Vector3.Up);
        if (_stationAt < 0 && p.OnStation) _stationAt = time;
        double since = SinceStation(time);
        float open = since < 0 ? 0 : Mathf.Clamp((float)(since / 1.2), 0, 1);
        open = open * open * (3 - 2 * open);
        foreach (Node3D wing in _wings) wing.Basis = new Basis(Vector3.Forward, 0) * new Basis(Vector3.Up, Mathf.Lerp(Mathf.Pi * .48f, 0, open));
        _dish.Basis = new Basis(Vector3.Forward, since < 0 ? 0 : (float)since * 2.2f);
        bool blink = since >= 0 ? (time * 2.5 % 1) < .18 : (time * 1.2 % 1) < .08;
        _beaconMaterial.EmissionEnergyMultiplier = blink ? 9 : .6f;
        // 탐지 파동: 날개가 다 펴진 뒤 1.1초마다 하나.
        if (since >= 1.3)
        {
            int n = (int)((since - 1.3) / 1.1);
            while (_pulses.Count <= n && _pulses.Count < 6)
            {
                var material = new ShaderMaterial { Shader = _pulseShader };
                var node = new MeshInstance3D { Mesh = _pulseMesh, MaterialOverride = material,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, TopLevel = true };
                AddChild(node);
                _pulses.Add((node, material, _stationAt + 1.3 + _pulses.Count * 1.1));
                Pinged?.Invoke(at);
            }
        }
        foreach (var (node, material, born) in _pulses)
        {
            float age = (float)(time - born);
            node.Visible = age >= 0 && age < 2.4f;
            if (!node.Visible) continue;
            float k = age / 2.4f;
            // 프로브 수평면의 고리가 퍼져 나간다(위에서 비스듬히 보면 타원).
            float radius = 2 + 60 * (1 - Mathf.Pow(1 - k, 2.4f));
            node.GlobalTransform = new Transform3D(Basis.Identity.Scaled(new Vector3(radius, radius * .3f, radius)), GlobalPosition);
            material.SetShaderParameter("fade", (1 - k) * (1 - k) * .9f);
        }
    }
}
