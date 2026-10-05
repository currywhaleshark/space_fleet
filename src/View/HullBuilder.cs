using System;
using System.Collections.Generic;
using Godot;

namespace SpaceFleet.View;

/// <summary>
/// 함선 외형을 부품 단위로 조립한다. 함선 로컬 축: 전방 -Z, 위 +Y, 우 +X.
/// 그리블과 조명은 인스턴스로 모아 마지막에 MultiMesh 하나씩으로 만든다.
/// </summary>
public sealed class HullBuilder
{
    private readonly List<(Transform3D Xf, Color Tint)> _greebles = new();
    private readonly List<Transform3D> _lights = new();
    private readonly Random _rng;

    public HullBuilder(Palette palette, int seed)
    {
        Palette = palette;
        _rng = new Random(seed);
    }

    public Node3D Root { get; } = new() { Name = "Model" };
    public Palette Palette { get; }
    public List<Node3D> Plumes { get; } = new();
    public List<RcsJet> RcsJets { get; } = new();

    public MeshInstance3D Add(Mesh mesh, Material material, Transform3D xf, bool castShadow = true)
    {
        var mi = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = material,
            Transform = xf,
            CastShadow = castShadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        };
        Root.AddChild(mi);
        return mi;
    }

    public void Box(Vector3 center, Vector3 size, Material? material = null) =>
        Add(new BoxMesh { Size = size }, material ?? Palette.Hull, new Transform3D(Basis.Identity, center));

    public void Taper(Vector3 c0, Vector2 s0, Vector3 c1, Vector2 s1, Material? material = null) =>
        Add(MeshKit.Taper(c0, s0, c1, s1), material ?? Palette.Hull, Transform3D.Identity);

    /// <summary>Z축 방향 원통. rFront는 -Z(전방) 끝의 반지름.</summary>
    public void CylinderZ(Vector3 center, float rBack, float rFront, float length, Material? material = null, int segments = 16)
    {
        var mesh = new CylinderMesh
        {
            TopRadius = rFront,
            BottomRadius = rBack,
            Height = length,
            RadialSegments = segments,
            Rings = 1,
        };
        // 원통 기본축 +Y를 -Z(전방)로 눕힌다.
        var basis = new Basis(Vector3.Right, -Mathf.Pi / 2f);
        Add(mesh, material ?? Palette.Hull, new Transform3D(basis, center));
    }

    public void CylinderY(Vector3 center, float radius, float height, Material? material = null, int segments = 20) =>
        Add(new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = height, RadialSegments = segments, Rings = 1 },
            material ?? Palette.Hull, new Transform3D(Basis.Identity, center));

    /// <summary>
    /// 포탑. ventral이면 아래로 매달린다. 포신은 전방(-Z)을 향한다.
    /// </summary>
    public void Turret(Vector3 mount, float scale, int barrels, bool ventral)
    {
        float s = ventral ? -1f : 1f;
        CylinderY(mount + new Vector3(0, s * 4f * scale, 0), 15f * scale, 8f * scale, Palette.Dark);
        Box(mount + new Vector3(0, s * 13f * scale, 0), new Vector3(24f, 11f, 30f) * scale);
        float spacing = 7f * scale;
        float x0 = -spacing * (barrels - 1) * 0.5f;
        for (int i = 0; i < barrels; i++)
        {
            var muzzleCenter = mount + new Vector3(x0 + spacing * i, s * 13f * scale, -15f * scale - 21f * scale);
            CylinderZ(muzzleCenter, 1.7f * scale, 1.3f * scale, 42f * scale, Palette.Dark, 10);
        }
    }

    /// <summary>엔진 노즐 + 발광 디스크 + 화염. 화염 노드는 ShipView가 출력에 맞춰 늘인다.</summary>
    public void Engine(Vector3 nozzleExit, float radius, float plumeLength)
    {
        CylinderZ(nozzleExit + new Vector3(0, 0, -radius * 0.9f), radius, radius * 0.72f, radius * 1.8f, Palette.Dark, 20);
        CylinderZ(nozzleExit + new Vector3(0, 0, -radius * 0.05f), radius * 0.86f, radius * 0.86f, radius * 0.1f, Palette.Glow, 20);

        // 화염: 노즐 출구에서 +Z(후방)로 뻗는 원뿔. 부모 노드의 Y 스케일로 길이를 조절한다.
        var pivot = new Node3D
        {
            Name = "Plume",
            Transform = new Transform3D(new Basis(Vector3.Right, Mathf.Pi / 2f), nozzleExit),
        };
        var cone = new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 0f,
                BottomRadius = radius * 0.8f,
                Height = plumeLength,
                RadialSegments = 16,
                Rings = 1,
                CapTop = false,
                CapBottom = false,
            },
            MaterialOverride = Palette.Plume,
            Position = new Vector3(0, plumeLength * 0.5f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        pivot.AddChild(cone);
        Root.AddChild(pivot);
        Palette.Plume.SetShaderParameter("half_length", plumeLength * 0.5f);
        Plumes.Add(pivot);
    }

    /// <summary>
    /// 보조 추진기 노즐 하나. exhaust는 분사 방향(함선 로컬)이며 함선은 그 반대로 밀린다.
    /// 분사 세기는 ShipView가 횡·상하·제동 가속과 회전 가속으로 정한다.
    /// </summary>
    public void RcsNozzle(Vector3 position, Vector3 exhaust, float size, float plumeLength)
    {
        Vector3 y = exhaust.Normalized();
        Box(position - y * size * 0.25f, Vector3.One * size, Palette.Dark);

        // 원뿔 기본축 +Y를 분사 방향으로. x × y = z가 되게 직교 기저를 만든다.
        Vector3 helper = Mathf.Abs(y.Dot(Vector3.Up)) > 0.9f ? Vector3.Right : Vector3.Up;
        Vector3 x = helper.Cross(y).Normalized();
        Vector3 z = x.Cross(y).Normalized();
        var pivot = new Node3D
        {
            Name = "Rcs",
            Transform = new Transform3D(new Basis(x, y, z), position + y * size * 0.25f),
            Visible = false,
        };
        pivot.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 0f,
                BottomRadius = size * 0.6f,
                Height = plumeLength,
                RadialSegments = 10,
                Rings = 1,
                CapTop = false,
                CapBottom = false,
            },
            MaterialOverride = Palette.RcsPlume,
            Position = new Vector3(0, plumeLength * 0.5f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        Root.AddChild(pivot);
        Palette.RcsPlume.SetShaderParameter("half_length", plumeLength * 0.5f);
        RcsJets.Add(new RcsJet(pivot, position, y));
    }

    /// <summary>
    /// 모든 함종에 같은 규칙으로 다는 보조 추진기 묶음. 함수·함미마다 좌·우 노즐 1개씩과
    /// 상·하 노즐을 좌우로 갈라 2개씩(롤은 좌상·우하처럼 대각으로 분사한다),
    /// 함수 묶음 좌우 바깥에 앞으로 분사하는 역추진 노즐 2개. sideLift는 측면 노즐을 날개 위로 올릴 때 쓴다.
    /// </summary>
    public void RcsClusters(float bowZ, Vector2 bowHalf, float sternZ, Vector2 sternHalf, float size, float plume, float sideLift = 0f)
    {
        const float VerticalSpread = 0.6f; // 상·하 노즐을 반폭의 이 비율만큼 좌우로 벌린다(롤 팔 길이).
        foreach (var (z, half) in new[] { (bowZ, bowHalf), (sternZ, sternHalf) })
        {
            RcsNozzle(new Vector3(half.X, sideLift, z), Vector3.Right, size, plume);
            RcsNozzle(new Vector3(-half.X, sideLift, z), Vector3.Left, size, plume);
            foreach (float side in new[] { 1f, -1f })
            {
                float x = side * half.X * VerticalSpread;
                RcsNozzle(new Vector3(x, half.Y, z), Vector3.Up, size, plume);
                RcsNozzle(new Vector3(x, -half.Y, z), Vector3.Down, size, plume);
            }
        }
        RcsNozzle(new Vector3(bowHalf.X + size * 0.5f, sideLift, bowZ), Vector3.Forward, size, plume);
        RcsNozzle(new Vector3(-bowHalf.X - size * 0.5f, sideLift, bowZ), Vector3.Forward, size, plume);
    }

    /// <summary>
    /// 평면 위에 작은 상자를 흩뿌린다. origin은 면 중심, u/v는 면의 두 반변 벡터(전체 길이), normal은 바깥 방향.
    /// </summary>
    public void Greebles(Vector3 origin, Vector3 u, Vector3 v, Vector3 normal, int count, float minSize, float maxSize)
    {
        Vector3 ud = u.Normalized(), vd = v.Normalized(), n = normal.Normalized();
        for (int i = 0; i < count; i++)
        {
            float a = (float)_rng.NextDouble() - 0.5f;
            float b = (float)_rng.NextDouble() - 0.5f;
            float su = Lerp(minSize, maxSize, (float)_rng.NextDouble());
            float sv = Lerp(minSize, maxSize, (float)_rng.NextDouble());
            float h = Lerp(minSize * 0.25f, maxSize * 0.5f, (float)Math.Pow(_rng.NextDouble(), 2));
            Vector3 p = origin + u * a + v * b + n * (h * 0.5f);
            var basis = new Basis(ud * su, n * h, vd * sv);
            float tint = 0.65f + (float)_rng.NextDouble() * 0.5f;
            _greebles.Add((new Transform3D(basis, p), new Color(tint, tint, tint)));
        }
    }

    /// <summary>창문·항해등 열. fill은 켜진 비율.</summary>
    public void LightRow(Vector3 from, Vector3 to, float spacing, float size, float fill)
    {
        float length = from.DistanceTo(to);
        int n = Math.Max(1, (int)(length / spacing));
        for (int i = 0; i <= n; i++)
        {
            if (_rng.NextDouble() > fill)
                continue;
            Vector3 p = from.Lerp(to, i / (float)n);
            _lights.Add(new Transform3D(Basis.Identity.Scaled(Vector3.One * size), p));
        }
    }

    public void NavLight(Vector3 position, float size, bool red) =>
        Add(new SphereMesh { Radius = size * 0.5f, Height = size, RadialSegments = 8, Rings = 4 },
            red ? Palette.NavRed : Palette.NavGreen, new Transform3D(Basis.Identity, position), castShadow: false);

    public ShipModel Finish()
    {
        if (_greebles.Count > 0)
        {
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = new BoxMesh { Size = Vector3.One },
            };
            mm.InstanceCount = _greebles.Count;
            for (int i = 0; i < _greebles.Count; i++)
            {
                mm.SetInstanceTransform(i, _greebles[i].Xf);
                mm.SetInstanceColor(i, _greebles[i].Tint);
            }
            Root.AddChild(new MultiMeshInstance3D { Name = "Greebles", Multimesh = mm, MaterialOverride = Palette.Greeble });
        }

        if (_lights.Count > 0)
        {
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = new BoxMesh { Size = Vector3.One },
            };
            mm.InstanceCount = _lights.Count;
            for (int i = 0; i < _lights.Count; i++)
                mm.SetInstanceTransform(i, _lights[i]);
            Root.AddChild(new MultiMeshInstance3D
            {
                Name = "Lights",
                Multimesh = mm,
                MaterialOverride = Palette.Light,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }

        return new ShipModel(Root, Plumes, Palette, RcsJets);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}

/// <param name="Position">노즐 위치(함선 로컬, m).</param>
/// <param name="Exhaust">분사 방향(함선 로컬, 단위 벡터). 함선은 반대로 밀린다.</param>
public sealed record RcsJet(Node3D Pivot, Vector3 Position, Vector3 Exhaust);

public sealed record ShipModel(Node3D Root, IReadOnlyList<Node3D> Plumes, Palette Palette, IReadOnlyList<RcsJet> RcsJets);
