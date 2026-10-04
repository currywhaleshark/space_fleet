using System;
using Godot;

namespace SpaceFleet.View;

/// <summary>
/// 전함 주위를 도는 요격드론 무리(수 m급). 0단계에서는 시뮬레이션 개체가 아니라
/// 크기 비교용 연출이다. 모함 노드의 자식이라 모함 좌표계에서 궤도를 그린다.
/// </summary>
public partial class DroneSwarm : MultiMeshInstance3D
{
    private struct Orbit
    {
        public Basis Plane;
        public float Radius;
        public float Phase;
        public float Omega;
    }

    private Orbit[] _orbits = Array.Empty<Orbit>();
    private double _time;

    public static DroneSwarm Create(Palette palette, int count, float minRadius, float maxRadius, int seed)
    {
        var rng = new Random(seed);
        var orbits = new Orbit[count];
        for (int i = 0; i < count; i++)
        {
            // 몇 개의 편대 궤도면으로 묶어야 무리처럼 보인다.
            int group = i % 4;
            var axis = new Vector3(0.3f * group - 0.45f, 1f, 0.2f * (group - 1.5f)).Normalized();
            float tilt = 0.25f + group * 0.35f + (float)rng.NextDouble() * 0.08f;
            float radius = minRadius + (maxRadius - minRadius) * (float)rng.NextDouble();
            float speed = 110f + 40f * (float)rng.NextDouble();
            orbits[i] = new Orbit
            {
                Plane = new Basis(axis.Cross(Vector3.Forward).Normalized(), tilt) * new Basis(Vector3.Up, group * 1.3f),
                Radius = radius,
                Phase = group * 1.6f + (float)rng.NextDouble() * 0.5f,
                Omega = speed / radius,
            };
        }

        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = DroneMesh(palette),
            InstanceCount = count,
        };
        return new DroneSwarm { Name = "Drones", Multimesh = mm, _orbits = orbits };
    }

    private static ArrayMesh DroneMesh(Palette palette)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        MeshKit.AddTaper(st, new(0, 0, 2.5f), new(3.2f, 1.4f), new(0, 0, -3f), new(1.2f, 0.8f));
        MeshKit.AddTaper(st, new(0, 0, 1.2f), new(7f, 0.25f), new(0, 0, -0.6f), new(2f, 0.2f));
        ArrayMesh mesh = st.Commit();

        st.Clear();
        st.Begin(Mesh.PrimitiveType.Triangles);
        MeshKit.AddTaper(st, new(0, 0.6f, -1.4f), new(0.6f, 0.4f), new(0, 0.6f, -2.2f), new(0.6f, 0.4f));
        st.Commit(mesh);

        mesh.SurfaceSetMaterial(0, palette.Dark);
        mesh.SurfaceSetMaterial(1, palette.NavRed);
        return mesh;
    }

    public override void _Process(double delta)
    {
        _time += delta;
        for (int i = 0; i < _orbits.Length; i++)
        {
            Orbit o = _orbits[i];
            float a = o.Phase + (float)(_time * o.Omega) + i * 0.37f;
            Vector3 pos = o.Plane * new Vector3(Mathf.Cos(a) * o.Radius, 0, Mathf.Sin(a) * o.Radius);
            Vector3 tangent = o.Plane * new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a));
            Vector3 up = o.Plane * Vector3.Up;
            Multimesh.SetInstanceTransform(i, new Transform3D(Basis.LookingAt(tangent, up), pos));
        }
    }
}
