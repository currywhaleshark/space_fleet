using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>
/// 조준선 주변 무장 표시. 왼쪽 호 = 탄약, 오른쪽 호 = 재장전(가득 차면 발사 준비),
/// 조준선 색 = 선행 보정 중(초록), 짧은 X = 히트마커(파랑 실드 · 주황 장갑 · 빨강 모듈).
/// </summary>
public partial class Hud
{
    private static readonly Color Lead = new(0.3f, 1f, 0.66f, 0.95f);
    private const float CrosshairRadius = 26f;

    private void DrawCrosshair(Camera3D cam, ShipView controlled, Vector2 screen)
    {
        Vector2 c = Game.Camera.AimScreenPosition();
        RailgunState? weapon = controlled.Body.Railgun;
        Color cross = Game.SelectedWeapon == PlayerWeapon.MainGun && Game.CorrectingAim ? Lead : Game.FireAssist ? Text : Motion;

        if (!Game.Camera.TelescopeHeld)
        {
        DrawLine(c + new Vector2(-14, 0), c + new Vector2(-5, 0), cross, 1.5f);
        DrawLine(c + new Vector2(5, 0), c + new Vector2(14, 0), cross, 1.5f);
        DrawLine(c + new Vector2(0, -14), c + new Vector2(0, -5), cross, 1.5f);
        DrawLine(c + new Vector2(0, 5), c + new Vector2(0, 14), cross, 1.5f);
        }
        if (weapon is not null && Game.SelectedWeapon == PlayerWeapon.MainGun)
        {
        // 왼쪽 호: 남은 탄약(아래에서 위로).
        float ammo = weapon.Rounds / (float)weapon.Definition.Rounds;
        ArcGauge(c, CrosshairRadius, Mathf.DegToRad(145), Mathf.DegToRad(70), ammo, ammo > 0.2f ? Dim : Hostile, 3f);

        // 오른쪽 호: 재장전 진행. 주포·전력·탄약고 손상으로 쏠 수 없으면 빨간 빈 호.
        bool disabled = weapon.Output <= 0.01f || weapon.Rounds <= 0 || controlled.Body.Damage.Destroyed || controlled.Body.Power.Overheated;
        float reload = disabled ? 0f : 1f - weapon.ReloadRemaining / weapon.Definition.ReloadSeconds;
        ArcGauge(c, CrosshairRadius, Mathf.DegToRad(35), Mathf.DegToRad(-70), reload, weapon.Ready ? Good : Motion, 3f);
        if (disabled)
            DrawArc(c, CrosshairRadius, Mathf.DegToRad(35), Mathf.DegToRad(-35), 12, Hostile, 3f);
        }

        DrawLeadMarker(cam, screen);
        DrawHitMarker(cam, controlled, c);

        // 발사 실패 사유는 쏘려고 했을 때만 조준선 아래에 잠깐 글자로 보인다.
        // 사거리 밖은 표적 패널의 거리 색(주황)으로 상시 보인다.
        if (Game.LastFireFailed && Game.World.Time - Game.LastFireTime < 1.5)
            CenteredLabel(c + new Vector2(0, 48), Game.LastFireMessage, 13, Motion);
    }

    private void DrawLeadMarker(Camera3D cam, Vector2 screen)
    {
        if (Game.SelectedWeapon == PlayerWeapon.Missile) return;
        bool amSelected=Game.Scheme==ControlScheme.Pilot && Game.AntimatterSelected;
        FiringSolution? current = amSelected ? Game.AntimatterSolution
            : Game.Scheme == ControlScheme.Helm ? Game.Gunnery?.Solution : Game.FiringSolution;
        if ((Game.Scheme == ControlScheme.Pilot && !Game.FireAssist) || current is not { Valid: true } solution)
            return;
        Vector3 point = (solution.AimPoint - Game.RenderOrigin).ToVector3();
        if (cam.IsPositionBehind(point))
            return;
        Vector2 p = cam.UnprojectPosition(point);
        // 조함 방식: 표적이 포각 밖이거나 선체에 가리면 선행점이 빨갛다(OffscreenHud의 사선 상태).
        Color color = Game.Scheme == ControlScheme.Helm ? (_hasEngagedLine && _engagedLine != FireFailure.None ? Hostile : Lead)
            : Game.CorrectingAim ? Lead : new Color(Lead, 0.45f);
        if(amSelected && Game.Controlled is { } carrier && carrier.Body.Definition.Antimatter is { } am)
        {
            bool aligned=carrier.Body.Forward.AngleTo(solution.Direction)<=Mathf.DegToRad(am.LaunchConeDegrees);
            color=aligned && solution.FlightTime<=am.Flight.MaxFlightSeconds && solution.Range<=am.MaxTravelMeters ? Lead : Motion;
            Label(p+new Vector2(18,31),$"AM {solution.FlightTime:0.0}s",12,color);
        }
        DrawRect(new Rect2(p - new Vector2(6, 6), new Vector2(12, 12)), color, false, 1.5f);
        // 관측 오차 원: 예상 탄착 분산의 크기.
        float pixels = screen.Y * 0.5f / Mathf.Tan(Mathf.DegToRad(cam.Fov) * 0.5f);
        float error = Mathf.Clamp((float)(solution.ErrorMeters / Math.Max(1, solution.Range)) * pixels, 2, 80);
        DrawArc(p, error, 0, Mathf.Tau, 24, new Color(Lead, 0.3f), 1f);
        // 비행 시간: 15초를 한 바퀴로 채우는 호. 근거리는 짧은 호, 150 km 전함 사격은 거의 한 바퀴.
        float seconds = (float)solution.FlightTime;
        ArcGauge(p, 11f, -Mathf.Pi / 2f, Mathf.Tau, Mathf.Clamp(seconds / 15f, 0f, 1f), color, 2f);
    }

    private void DrawHitMarker(Camera3D cam, ShipView controlled, Vector2 c)
    {
        if (Game.Feedback.Outgoing is not { } hit) return;
        Color color = new(FeedbackColor(hit.Kind), hit.Fade);
        float pulse = 1 + .35f * Mathf.Exp(-hit.Age * 16);
        if (hit.Kind == HitKind.Shield)
            DrawArc(c, 19 * pulse, 0, Mathf.Tau, 32, color, 2);
        else if (hit.Kind == HitKind.Armor)
            DrawDiamond(c, 19 * pulse, color);
        else
        {
            foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            {
                var d = new Vector2(sx, sy).Normalized();
                DrawLine(c + d * 9, c + d * (hit.Kind == HitKind.Critical ? 25 : 19) * pulse, color, 2.5f);
            }
            if (hit.Kind == HitKind.Critical) DrawDiamond(c, 30 * pulse, color);
        }
        string label = "명중 · " + hit.Label;
        Vector2 at = c + new Vector2(0, 83);
        Vector2 textSize = _font.GetStringSize(label, fontSize: 15);
        DrawRect(new Rect2(at - new Vector2(textSize.X * .5f + 9, 17), textSize + new Vector2(18, 6)), new Color(PanelBack, .65f * hit.Fade));
        CenteredLabel(at, label, 15, color);
    }
}
