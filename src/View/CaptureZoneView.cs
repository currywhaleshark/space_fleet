using System;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.View;

/// <summary>
/// 점령 거점 표시: 구형 범위를 보여 주는 세 축 고리 + 중심 구조물(<see cref="ZoneStructures"/>). 충돌·피해 없음.
/// 구조물 표시등은 소유 진영색, 경합 중이면 주황으로 깜박인다.
/// </summary>
public partial class CaptureZoneView : Node3D
{
    private const int Segments = 72;
    private CaptureZone _zone = null!;
    private MeshInstance3D _rings = null!;
    private StandardMaterial3D _material = null!;
    private ZoneStructures.Built _structure = null!;

    public static CaptureZoneView Create(CaptureZone zone)
    {
        var node = new CaptureZoneView { Name = "Capture_" + zone.Name, _zone = zone };
        return node;
    }

    public override void _Ready()
    {
        _material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = false,
            AlbedoColor = new Color(.85f, .72f, .38f, .5f),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _material);
        for (int plane = 0; plane < 3; plane++)
        {
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.Tau / Segments, b = (i + 1) * Mathf.Tau / Segments;
                Vector3 A = Point(plane, a), B = Point(plane, b);
                mesh.SurfaceAddVertex(A * (float)_zone.RadiusMeters);
                mesh.SurfaceAddVertex(B * (float)_zone.RadiusMeters);
            }
        }
        mesh.SurfaceEnd();
        _rings = new MeshInstance3D { Name = "CaptureVolume", Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_rings);
        _structure = ZoneStructures.Create(_zone.Name);
        AddChild(_structure.Root);
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--show-obstacles") >= 0)
            AddChild(ObstacleWireframe.Create(ZoneStructureShapes.For(_zone.Name, Vec3d.Zero)));
    }

    public override void _Process(double delta)
    {
        if (_structure.Spinner is { } spinner) spinner.RotateObjectLocal(Vector3.Up, _structure.SpinRate * (float)delta);
    }

    private static Vector3 Point(int plane, float angle) => plane switch
    {
        0 => new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0),
        1 => new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)),
        _ => new Vector3(0, Mathf.Cos(angle), Mathf.Sin(angle)),
    };

    public void Sync(Vec3d renderOrigin)
    {
        Position = (_zone.Position - renderOrigin).ToVector3();
        Color color = _zone.Owner == Faction.Blue ? new Color(.4f, .75f, 1f, .48f)
            : _zone.Owner == Faction.Red ? new Color(1f, .44f, .32f, .48f)
            : new Color(.85f, .72f, .38f, .48f);
        if (_zone.Contested) color = new Color(1f, .77f, .3f, .85f);
        _material.AlbedoColor = color;
        bool blinkOff = _zone.Contested && (Time.GetTicksMsec() / 400) % 2 == 1;
        _structure.BeaconMaterial.AlbedoColor = blinkOff ? new Color(.2f, .16f, .1f) : new Color(color, 1f).Lightened(.25f);
    }
}
