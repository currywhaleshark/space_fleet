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

    public ShipBody Body { get; private set; } = null!;
    public Palette Palette { get; private set; } = null!;

    /// <summary>이번 프레임에 보간된 시뮬레이션 위치.</summary>
    public Vec3d SimPosition { get; private set; }

    public static ShipView Create(ShipBody body, int seed)
    {
        ShipModel model = ShipModels.Build(body.Class, body.Faction, seed);
        var view = new ShipView { Name = body.Callsign, Body = body, Palette = model.Palette, _plumes = model.Plumes };
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
    }
}
