using System;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.View;

/// <summary>Lightweight, non-physical rings for spherical capture volumes. No physics or damage involved.</summary>
public partial class CaptureZoneView : Node3D
{
    private const int Segments = 72;
    private CaptureZone _zone = null!;
    private MeshInstance3D _rings = null!;
    private StandardMaterial3D _material = null!;

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
    }
}
