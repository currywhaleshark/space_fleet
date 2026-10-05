using System;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.View;

/// <summary>
/// 이동감 단서. 우주에는 흘러가는 배경이 없어서, 기수를 돌리면 실제로 어디로 가는지 몸으로 느낄 수 없다.
/// 입자는 월드에 고정되어 있고 카메라 주변 상자 안에서 반복 배치되므로, 화면에서는 내 속도의 반대로 흘러간다.
/// 연출 전용이며 시뮬레이션에 관여하지 않는다.
/// </summary>
public partial class SpaceDust : MultiMeshInstance3D
{
    private const int Count = 400;
    // 줄무늬 길이 = 속도 × 노출 시간. 고속일수록 길게 늘어진다.
    private const float Exposure = 0.035f;

    private Vector3[] _seeds = Array.Empty<Vector3>();
    private ShaderMaterial _material = null!;

    public static SpaceDust Create(int seed)
    {
        var rng = new Random(seed);
        var seeds = new Vector3[Count];
        for (int i = 0; i < Count; i++)
            seeds[i] = new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble());

        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/dust.gdshader") };
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = new BoxMesh { Size = Vector3.One },
            InstanceCount = Count,
            // 입자는 늘 카메라 주변에 있으므로 매 프레임 경계 상자를 다시 계산하지 않는다.
            CustomAabb = new Aabb(new Vector3(-50_000, -50_000, -50_000), new Vector3(100_000, 100_000, 100_000)),
        };
        return new SpaceDust
        {
            Name = "SpaceDust",
            Multimesh = mm,
            MaterialOverride = material,
            CastShadow = ShadowCastingSetting.Off,
            _seeds = seeds,
            _material = material,
        };
    }

    /// <param name="cameraSim">카메라의 시뮬레이션 위치(double).</param>
    /// <param name="cameraRender">카메라의 렌더 좌표 위치.</param>
    /// <param name="velocity">조종함 속도. 먼지는 월드에 고정이므로 화면에서는 이 반대로 흐른다.</param>
    /// <param name="boxSize">반복 상자 한 변(m). 함선 크기에 맞춰 키운다.</param>
    public void Sync(Vec3d cameraSim, Vector3 cameraRender, Vector3 velocity, float boxSize)
    {
        float l = boxSize;
        // 카메라 위치를 상자 크기로 나눈 나머지만 float로 내린다. 큰 좌표에서도 정밀도가 유지된다.
        var cam = new Vector3(Mod(cameraSim.X, l), Mod(cameraSim.Y, l), Mod(cameraSim.Z, l));

        float speed = velocity.Length();
        Vector3 dir = speed > 0.5f ? velocity / speed : Vector3.Forward;
        Vector3 side = Mathf.Abs(dir.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up;
        Vector3 x = side.Cross(dir).Normalized();
        Vector3 y = dir.Cross(x).Normalized();

        float width = l * 0.0008f;
        float length = Mathf.Clamp(speed * Exposure, width, l * 0.12f);
        float brightness = Mathf.Clamp(width * 4f / length, 0.3f, 1f);
        var basis = new Basis(x * width, y * width, dir * length);

        MultiMesh mm = Multimesh;
        for (int i = 0; i < _seeds.Length; i++)
        {
            // 상자 안 고정 위치에서 카메라 위치를 빼고 [-l/2, l/2)로 감싼다.
            Vector3 rel = _seeds[i] * l - cam;
            rel = new Vector3(Wrap(rel.X, l), Wrap(rel.Y, l), Wrap(rel.Z, l));
            mm.SetInstanceTransform(i, new Transform3D(basis, cameraRender + rel));
            mm.SetInstanceCustomData(i, new Color(brightness, 0, 0, 0));
        }

        // 카메라 바로 앞 입자가 화면을 가로지르는 굵은 막대로 보이지 않게 일찍 흐린다.
        _material.SetShaderParameter("near_fade", l * 0.06f);
        _material.SetShaderParameter("far_fade", l * 0.5f);
    }

    private static float Mod(double value, float size) => (float)(value - Math.Floor(value / size) * size);

    private static float Wrap(float value, float size) => value - Mathf.Floor(value / size + 0.5f) * size;
}
