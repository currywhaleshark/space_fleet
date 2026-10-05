using System.Collections.Generic;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.View;

/// <summary>
/// ShipBody 하나를 화면에 그린다. 매 프레임 두 틱 사이를 보간하고,
/// 렌더 원점을 뺀 상대 좌표(float)로 놓는다.
/// </summary>
public partial class ShipView : Node3D
{
    private IReadOnlyList<Node3D> _plumes = new List<Node3D>();
    private float _plumeLevel;
    private IReadOnlyList<RcsJet> _rcs = new List<RcsJet>();
    private float[] _rcsLevel = System.Array.Empty<float>();
    /// <summary>롤 축(Z)에서 가장 먼 노즐까지의 거리(m).</summary>
    private float _rollArm = 1f;

    public ShipBody Body { get; private set; } = null!;
    public Palette Palette { get; private set; } = null!;

    /// <summary>이번 프레임에 보간된 시뮬레이션 위치.</summary>
    public Vec3d SimPosition { get; private set; }

    public static ShipView Create(ShipBody body, int seed)
    {
        ShipModel model = ShipModels.Build(body.Class, body.Faction, seed);
        var view = new ShipView
        {
            Name = body.Callsign, Body = body, Palette = model.Palette, _plumes = model.Plumes,
            _rcs = model.RcsJets, _rcsLevel = new float[model.RcsJets.Count],
        };
        foreach (RcsJet jet in model.RcsJets)
            view._rollArm = Mathf.Max(view._rollArm, new Vector2(jet.Position.X, jet.Position.Y).Length());
        view.AddChild(model.Root);
        return view;
    }

    public void Sync(Vec3d renderOrigin, double alpha, float delta)
    {
        SimPosition = Body.InterpolatedPosition(alpha);
        Transform = new Transform3D(
            new Basis(Body.InterpolatedOrientation((float)alpha)),
            (SimPosition - renderOrigin).ToVector3());

        _plumeLevel = Mathf.Lerp(_plumeLevel, Body.EngineOutput, 1f - Mathf.Exp(-delta * 6f));
        for (int i = 0; i < _plumes.Count; i++)
        {
            Node3D plume = _plumes[i];
            float level = _plumeLevel * Body.Damage.EngineFraction(i);
            bool burning = level > 0.03f;
            // 정지 상태의 짧은 원뿔은 뒤에서 보면 노즐 위에 겹쳐 하얗게 포화되므로 숨긴다.
            plume.Visible = burning;
            if (burning)
                plume.Scale = new Vector3(1f, level, 1f);
        }
        SyncRcs(delta);
    }

    /// <summary>
    /// 보조 추진기 분사. 노즐 위치에서 필요한 가속 = 병진(횡·상하·제동) + 회전(α × r).
    /// 노즐은 분사 방향의 반대로 밀므로, 필요한 가속이 그쪽을 향할 때만 켠다.
    /// </summary>
    private void SyncRcs(float delta)
    {
        if (_rcs.Count == 0)
            return;
        ShipClass cls = Body.Class;
        Vector3 a = Body.LocalAcceleration;
        // 전방 가속은 메인 추진 몫이라 뺀다(전력 계산과 같은 구분).
        Vector3 linear = new Vector3(a.X, a.Y, Mathf.Max(0f, a.Z)) / Mathf.Max(cls.StrafeAccel, 1e-3f);
        // 피치·요는 최대 각가속일 때 함수·함미 끝의 선가속도로, 롤은 최대 롤 각가속일 때
        // 가장 바깥 노즐의 선가속도로 정규화한다(롤 팔은 선체 반폭 정도라 길이 기준이면 거의 보이지 않는다).
        float pitchYawScale = Mathf.Max(Mathf.DegToRad(cls.PitchYawAccelDeg) * cls.Length * 0.5f, 1e-3f);
        float rollScale = Mathf.Max(Mathf.DegToRad(cls.RollAccelDeg) * _rollArm, 1e-3f);
        Vector3 angular = Body.AngularAcceleration;
        var pitchYaw = new Vector3(angular.X, angular.Y, 0f) / pitchYawScale;
        var roll = new Vector3(0f, 0f, angular.Z) / rollScale;
        bool alive = Body.Damage.ManeuverFraction > 0.01f;
        float follow = 1f - Mathf.Exp(-delta * 15f);
        for (int i = 0; i < _rcs.Count; i++)
        {
            RcsJet jet = _rcs[i];
            Vector3 need = linear + pitchYaw.Cross(jet.Position) + roll.Cross(jet.Position);
            float target = alive ? Mathf.Clamp(-jet.Exhaust.Dot(need), 0f, 1f) : 0f;
            float level = _rcsLevel[i] = Mathf.Lerp(_rcsLevel[i], target, follow);
            bool firing = level > 0.05f;
            jet.Pivot.Visible = firing;
            if (firing)
                jet.Pivot.Scale = new Vector3(1f, level, 1f);
        }
    }
}
