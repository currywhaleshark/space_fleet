using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>
/// 부위 조준 표시. 표적의 고른 부위 위치에 마름모 + 부위 이름.
/// 관통 여부는 착탄 결과로 알린다. 실드가 남아 있으면 둘레에 파란 호.
/// </summary>
public partial class Hud
{
    private void DrawAimPart(Camera3D cam, ShipView controlled)
    {
        if ((Game.Scheme == ControlScheme.Pilot && Game.AimPart == AimSubsystem.Center) || Game.FireTarget is not ShipView target) return;
        if (Game.TrackOf(target).Level < TrackLevel.Identified) return; // 무엇인지 모르면 부위도 모른다.
        ShipBody me = controlled.Body;
        if (me.Railgun is not RailgunState gun) return;

        ModuleState? module = Game.AimModule;
        Vector2 labelAt;
        if (module is null)
        {
            if (Game.Scheme == ControlScheme.Helm) return;
            // 그 부위가 남아 있지 않다.
            Vector3 center = target.Position;
            if (cam.IsPositionBehind(center)) return;
            labelAt = cam.UnprojectPosition(center) + new Vector2(14, 22);
            Label(labelAt, $"{Subsystems.Label(Game.AimPart)} 없음", 12, Dim);
            return;
        }

        double alpha = Engine.GetPhysicsInterpolationFraction();
        Vec3d world = target.Body.InterpolatedPosition(alpha)
            + Vec3d.From(target.Body.InterpolatedOrientation((float)alpha) * module.Definition.Center);
        Vector3 render = (world - Game.RenderOrigin).ToVector3();
        if (cam.IsPositionBehind(render)) return;
        Vector2 p = cam.UnprojectPosition(render);

        Color color = Motion;
        DrawDiamond(p, 7f, color);
        DrawDiamond(p, 3f, color);
        if (target.Body.Damage.Shield > 1f)
            DrawArc(p, 11f, 0, Mathf.Tau, 20, new Color(Friendly, 0.7f), 1.5f);
        Label(p + new Vector2(12, -8), module.Definition.Name, 12, color);
    }
}
