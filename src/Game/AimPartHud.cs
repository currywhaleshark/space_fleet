using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>
/// 부위 조준 표시. 표적의 고른 부위 위치에 마름모 + 부위 이름. 초록 = 지금 내 포구 위치에서 그 면 장갑을 뚫을 수 있다,
/// 빨강 = 못 뚫는다(돌아 들어가야 한다). 실드가 남아 있으면 둘레에 파란 호(먼저 벗겨야 한다).
/// </summary>
public partial class Hud
{
    private void DrawAimPart(Camera3D cam, ShipView controlled)
    {
        if (Game.AimPart == AimSubsystem.Center || Game.FireTarget is not ShipView target) return;
        if (Game.TrackOf(target).Level < TrackLevel.Identified) return; // 무엇인지 모르면 부위도 모른다.
        ShipBody me = controlled.Body;
        if (me.Railgun is not RailgunState gun) return;

        ModuleState? module = Game.AimModule;
        Vector2 labelAt;
        if (module is null)
        {
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

        Vector3 dir = (Subsystems.WorldPosition(target.Body, module.Definition) - gun.MuzzlePosition).ToVector3();
        bool penetrates = DamageRay.PreviewArmor(target.Body, gun.MuzzlePosition, dir, gun.Definition.PenetrationMm, out float effective);
        Color color = penetrates ? Good : Hostile;
        DrawDiamond(p, 7f, color);
        DrawDiamond(p, 3f, color);
        if (target.Body.Damage.Shield > 1f)
            DrawArc(p, 11f, 0, Mathf.Tau, 20, new Color(Friendly, 0.7f), 1.5f);
        Label(p + new Vector2(12, -8), $"{module.Definition.Name} · {(penetrates ? "관통" : $"장갑 {effective:0}mm")}", 12, color);
    }
}
