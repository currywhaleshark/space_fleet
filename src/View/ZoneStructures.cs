using System;
using System.Collections.Generic;
using Godot;

namespace SpaceFleet.View;

/// <summary>
/// 점령 거점 중심의 구조물(그림만, 충돌·피해 없음). A 자원기지(소행성 채굴), B 회전 고리 정거장, C 연료 저장고.
/// 크기는 반지름 약 2 km 안(전함 1.2 km와 비교되는 대형 시설). AI는 <c>SimWorld.HoldClearance</c>만큼 떨어져 머문다.
/// 소유 표시등은 <see cref="Beacons"/>로 모아 CaptureZoneView가 진영색으로 바꾼다.
/// </summary>
public static class ZoneStructures
{
    public sealed class Built
    {
        public Node3D Root = null!;
        public readonly List<MeshInstance3D> Beacons = new();
        /// <summary>모든 표시등이 함께 쓰는 재질. 색을 바꾸면 한 번에 바뀐다.</summary>
        public readonly StandardMaterial3D BeaconMaterial = new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1f, .8f, .45f),
        };
        public Node3D? Spinner;
        public float SpinRate;
    }

    private static Shader? _hullShader;

    public static Built Create(string name) => name switch
    {
        "A" => MiningBase(),
        "C" => FuelDepot(),
        _ => RingStation(),
    };

    // ── 공통 부품 ──────────────────────────────────────────────

    private static ShaderMaterial Hull(Color color, float panel, float lights, float seed)
    {
        var m = new ShaderMaterial { Shader = _hullShader ??= GD.Load<Shader>("res://shaders/hull_panels.gdshader") };
        m.SetShaderParameter("base_color", new Vector3(color.R, color.G, color.B));
        m.SetShaderParameter("metallic_value", .5f);
        m.SetShaderParameter("roughness_value", .62f);
        m.SetShaderParameter("panel", panel);
        m.SetShaderParameter("seam", .03f);
        m.SetShaderParameter("variation", .14f);
        m.SetShaderParameter("hatch", .3f);
        m.SetShaderParameter("light_density", lights);
        m.SetShaderParameter("light_color", new Vector3(1f, .62f, .25f));
        m.SetShaderParameter("light_energy", 3f);
        m.SetShaderParameter("seed", seed);
        return m;
    }

    private static readonly Color Graphite = new(.09f, .1f, .11f), Pale = new(.32f, .31f, .29f), Rust = new(.2f, .12f, .08f);

    /// <summary>방열판: 밝은 회색 판에 촘촘한 이음새(검은 판으로 두면 거대한 구멍처럼 보인다).</summary>
    private static ShaderMaterial Radiator() => Hull(new Color(.3f, .31f, .32f), 9, 0, 9.2f);

    private static StandardMaterial3D Solar() => new()
    {
        AlbedoColor = new Color(.05f, .08f, .16f), Metallic = .7f, Roughness = .3f,
    };

    private static MeshInstance3D Add(Node3D parent, Mesh mesh, Material material, Vector3 position, Vector3? euler = null)
    {
        var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = position };
        if (euler is { } e) node.RotationDegrees = e;
        parent.AddChild(node);
        return node;
    }

    private static CylinderMesh Cylinder(float radius, float height, int sides = 24) =>
        new() { TopRadius = radius, BottomRadius = radius, Height = height, RadialSegments = sides, Rings = 1 };

    /// <summary>두 점을 잇는 각기둥 빔(트러스 부재).</summary>
    private static void Beam(Node3D parent, Vector3 a, Vector3 b, float thickness, Material material)
    {
        Vector3 d = b - a;
        float length = d.Length();
        if (length < 1e-3f) return;
        var node = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(thickness, thickness, length) }, MaterialOverride = material };
        Vector3 forward = d / length;
        Vector3 up = Mathf.Abs(forward.Dot(Vector3.Up)) > .95f ? Vector3.Right : Vector3.Up;
        node.Transform = new Transform3D(Basis.LookingAt(forward, up), (a + b) * .5f);
        parent.AddChild(node);
    }

    /// <summary>사각 단면 트러스: 네 모서리 부재 + 칸마다 X 가새.</summary>
    private static void Truss(Node3D parent, Vector3 a, Vector3 b, float width, int bays, float member, Material material)
    {
        Vector3 axis = (b - a).Normalized();
        Vector3 side = axis.Cross(Mathf.Abs(axis.Dot(Vector3.Up)) > .95f ? Vector3.Right : Vector3.Up).Normalized();
        Vector3 up = side.Cross(axis).Normalized();
        Vector3[] corner = { (side + up) * width * .5f, (side - up) * width * .5f, (-side - up) * width * .5f, (-side + up) * width * .5f };
        foreach (Vector3 c in corner) Beam(parent, a + c, b + c, member, material);
        for (int i = 0; i <= bays; i++)
        {
            Vector3 p = a.Lerp(b, i / (float)bays);
            for (int k = 0; k < 4; k++) Beam(parent, p + corner[k], p + corner[(k + 1) % 4], member * .7f, material);
            if (i == bays) continue;
            Vector3 q = a.Lerp(b, (i + 1) / (float)bays);
            for (int k = 0; k < 4; k++) Beam(parent, p + corner[k], q + corner[(k + 1) % 4], member * .5f, material);
        }
    }

    private static void Beacon(Built built, Node3D parent, Vector3 position, float radius)
    {
        var node = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = radius, Height = radius * 2, RadialSegments = 12, Rings = 6 },
            MaterialOverride = built.BeaconMaterial, Position = position,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(node);
        built.Beacons.Add(node);
    }

    // ── A: 자원기지 ─────────────────────────────────────────────

    /// <summary>울퉁불퉁한 소행성 위에 정제 시설·사일로·채굴탑, 옆으로 뻗은 적재 트러스와 부두.</summary>
    private static Built MiningBase()
    {
        var built = new Built { Root = new Node3D { Name = "MiningBase" } };
        Node3D root = built.Root;
        var rock = new StandardMaterial3D { AlbedoColor = new Color(.16f, .14f, .12f), Roughness = .95f, Metallic = 0 };
        Add(root, Asteroid(new Vector3(950, 640, 820), 11), rock, Vector3.Zero, new Vector3(8, 25, -6));
        Add(root, Asteroid(new Vector3(260, 200, 230), 23), rock, new Vector3(-1050, -380, 420));
        Add(root, Asteroid(new Vector3(170, 140, 150), 37), rock, new Vector3(880, -520, -640));

        ShaderMaterial hull = Hull(Pale, 22, .05f, 3.1f), dark = Hull(Graphite, 18, .08f, 5.7f), rust = Hull(Rust, 16, 0, 2.3f);
        var top = new Node3D { Position = new Vector3(0, 470, 0) };
        root.AddChild(top);
        // 기초: 본동 아래로 바위에 박힌 받침.
        Add(top, Cylinder(330, 420, 32), rust, new Vector3(-40, -170, 40));
        // 정제 시설: 낮고 넓은 본동 두 채 + 사일로 세 개.
        Add(top, new BoxMesh { Size = new Vector3(620, 160, 360) }, dark, new Vector3(-80, 60, 0));
        Add(top, new BoxMesh { Size = new Vector3(300, 110, 260) }, hull, new Vector3(320, 30, 120));
        for (int i = 0; i < 3; i++)
            Add(top, Cylinder(70, 420), rust, new Vector3(-300 + i * 170, 240, -260));
        // 채굴탑: 소행성에 박힌 시추 기둥과 탑 트러스.
        Truss(top, new Vector3(260, -40, -180), new Vector3(260, 620, -180), 90, 6, 10, dark);
        Add(top, Cylinder(32, 900), hull, new Vector3(260, 120, -180));
        Beacon(built, top, new Vector3(260, 650, -180), 30);
        // 적재 트러스와 화물 부두.
        Vector3 dockStart = new(560, 40, 120), dockEnd = new(1700, 180, 360);
        Truss(top, dockStart, dockEnd, 70, 9, 8, dark);
        Add(top, new BoxMesh { Size = new Vector3(260, 120, 200) }, hull, dockEnd + new Vector3(110, 0, 0));
        for (int i = 0; i < 4; i++)
            Add(top, new BoxMesh { Size = new Vector3(90, 70, 70) }, rust, dockEnd + new Vector3(-80 - i * 120, -80, 0));
        Beacon(built, top, dockEnd + new Vector3(250, 70, 0), 24);
        // 방열판 두 장.
        Add(top, new BoxMesh { Size = new Vector3(6, 220, 440) }, Radiator(), new Vector3(-470, 250, 200), new Vector3(0, 0, 18));
        Add(top, new BoxMesh { Size = new Vector3(6, 220, 440) }, Radiator(), new Vector3(-610, 250, 200), new Vector3(0, 0, -18));
        Beacon(built, top, new Vector3(-80, 160, 190), 22);
        return built;
    }

    /// <summary>위치 기반 잡음으로 구를 일그러뜨린 소행성. 같은 위치 정점을 합쳐 이음선 없이 부드러운 법선을 만든다.</summary>
    private static ArrayMesh Asteroid(Vector3 scale, int seed)
    {
        var sphere = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 48, Rings = 24 };
        var arrays = sphere.GetMeshArrays();
        var vertices = (Vector3[])arrays[(int)Mesh.ArrayType.Vertex];
        var indices = (int[])arrays[(int)Mesh.ArrayType.Index];
        var noise = new FastNoiseLite { Seed = seed, Frequency = 1.1f, FractalOctaves = 4, NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin };
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (int index in indices)
        {
            Vector3 v = vertices[index];
            float n = noise.GetNoise3Dv(v * 1.3f) * .38f + noise.GetNoise3Dv(v * 4.1f) * .08f;
            st.AddVertex(v * (1 + n) * scale);
        }
        st.Index();
        st.GenerateNormals();
        return st.Commit();
    }

    // ── B: 회전 고리 정거장 ─────────────────────────────────────

    /// <summary>중앙 허브 + 바퀴살 4개 + 회전 거주 고리, 축 양끝 도킹 포트와 태양광 날개.</summary>
    private static Built RingStation()
    {
        var built = new Built { Root = new Node3D { Name = "RingStation" } };
        Node3D root = built.Root;
        // 고리 축은 접근축(Z)에 맞춰 양쪽 함대에서 고리 전체가 보이게, 살짝 기울인다.
        var frame = new Node3D { RotationDegrees = new Vector3(90 + 16, 0, 12) };
        root.AddChild(frame);
        ShaderMaterial hull = Hull(Pale, 24, .06f, 7.3f), dark = Hull(Graphite, 20, .03f, 1.9f);
        // 허브(축 = 로컬 Y).
        Add(frame, Cylinder(170, 1100, 32), dark, Vector3.Zero);
        Add(frame, Cylinder(240, 260, 32), hull, Vector3.Zero);
        foreach (float end in new[] { -1f, 1f })
        {
            Add(frame, Cylinder(110, 160, 24), hull, new Vector3(0, end * 630, 0));
            Beacon(built, frame, new Vector3(0, end * 740, 0), 30);
        }
        // 태양광 날개: 허브 한쪽 끝에서 수직으로 뻗은 마스트 두 개.
        foreach (float side in new[] { -1f, 1f })
        {
            Beam(frame, new Vector3(0, -420, 0), new Vector3(side * 1300, -420, 0), 26, dark);
            Add(frame, new BoxMesh { Size = new Vector3(820, 6, 300) }, Solar(), new Vector3(side * 950, -420, 0));
        }
        // 회전부: 고리 + 바퀴살.
        var spinner = new Node3D { Name = "Habitat" };
        frame.AddChild(spinner);
        built.Spinner = spinner;
        built.SpinRate = Mathf.Tau / 240f;
        Add(spinner, new TorusMesh { InnerRadius = 1380, OuterRadius = 1600, Rings = 96, RingSegments = 16 }, hull, Vector3.Zero);
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.Tau / 4;
            Vector3 dir = new(Mathf.Cos(a), 0, Mathf.Sin(a));
            // Keep non-axisymmetric hardware aligned with fixed simulation collision.
            // Only the symmetric habitat tube rotates around it.
            Truss(frame, dir * 230, dir * 1400, 60, 5, 9, dark);
            // 고리 위 모듈과 표시등.
            Add(frame, new BoxMesh { Size = new Vector3(260, 200, 300) }, dark, dir * 1490, new Vector3(0, -Mathf.RadToDeg(a), 0));
            Beacon(built, frame, dir * 1490 + new Vector3(0, 130, 0), 24);
        }
        return built;
    }

    // ── C: 연료 저장고 ──────────────────────────────────────────

    /// <summary>긴 트러스 척추에 구형 탱크 묶음, 끝에 관제 모듈과 안테나, 옆에 방열판.</summary>
    private static Built FuelDepot()
    {
        var built = new Built { Root = new Node3D { Name = "FuelDepot" } };
        Node3D root = built.Root;
        var frame = new Node3D { RotationDegrees = new Vector3(0, 70, 10) };
        root.AddChild(frame);
        ShaderMaterial tank = Hull(new Color(.36f, .34f, .3f), 20, 0, 4.4f), dark = Hull(Graphite, 16, .05f, 8.8f), band = Hull(Rust, 14, 0, 6.1f);
        Vector3 a = new(0, 0, -1500), b = new(0, 0, 1500);
        Truss(frame, a, b, 150, 14, 14, dark);
        // 탱크 묶음 다섯 줄, 줄마다 네 개.
        var sphere = new SphereMesh { Radius = 150, Height = 300, RadialSegments = 32, Rings = 16 };
        for (int row = 0; row < 5; row++)
        {
            float z = -1100 + row * 520;
            Add(frame, Cylinder(60, 30, 24), band, new Vector3(0, 0, z), new Vector3(90, 0, 0));
            for (int k = 0; k < 4; k++)
            {
                float ang = k * Mathf.Tau / 4 + Mathf.Pi / 4;
                Vector3 p = new(Mathf.Cos(ang) * 250, Mathf.Sin(ang) * 250, z);
                Add(frame, sphere, tank, p);
                Beam(frame, new Vector3(p.X * .3f, p.Y * .3f, z), p * new Vector3(.62f, .62f, 1), 18, dark);
            }
        }
        // 관제 모듈과 안테나.
        Add(frame, Cylinder(130, 320, 28), dark, b + new Vector3(0, 0, 160), new Vector3(90, 0, 0));
        Beam(frame, b + new Vector3(0, 120, 200), b + new Vector3(0, 620, 260), 14, dark);
        Add(frame, new CylinderMesh { TopRadius = 140, BottomRadius = 10, Height = 70, RadialSegments = 24 }, tank, b + new Vector3(0, 640, 260));
        Beacon(built, frame, b + new Vector3(0, 690, 260), 26);
        Beacon(built, frame, a + new Vector3(0, 110, 0), 26);
        Beacon(built, frame, new Vector3(0, -110, 0), 22);
        // 방열판: 척추 옆 두 장.
        foreach (float side in new[] { -1f, 1f })
            Add(frame, new BoxMesh { Size = new Vector3(5, 360, 760) }, Radiator(), new Vector3(side * 420, 0, -300), new Vector3(0, 0, side * 10));
        return built;
    }
}
