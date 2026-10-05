using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

public partial class Hud
{
    private static readonly Color Lead = new(0.3f, 1f, 0.66f, 0.95f);

    private void DrawCombat(Camera3D cam, ShipView controlled, Vector2 screen)
    {
        if (controlled.Body.Railgun is not RailgunState weapon) return;
        float top = controlled.Body.LastCollision is CollisionImpact c && Game.World.Time - c.Time < 3 ? 174 : 144;
        DrawRect(new Rect2(8, top, 545, 135), new Color(0.01f, 0.02f, 0.03f, 0.9f));
        Line(0, $"레일건 · 사격보조 {(Game.FireAssist ? "ON" : "OFF")} (T) · {weapon.Status} · 탄약 {weapon.Rounds}", weapon.Ready ? Text : Motion);
        Line(1, $"탄속 {weapon.Definition.MuzzleSpeed / 1000:0.0} km/s · 사거리 {weapon.Definition.MaxRange / 1000:0} km · 비행 중 {Game.World.Projectiles.Count}", Dim);
        if (!Game.FireAssist) Line(2, "수동 사격 · 조준선 방향 · 선행 보정 없음", Motion);
        else if (Game.FiringSolution is { Valid: true } solution)
        {
            Line(2, $"{Game.InspectTarget?.Body.Callsign} · 예상 {solution.FlightTime:0.00}s · 관측 오차 ~{solution.ErrorMeters:0.0} m", Lead);
            Line(3, Game.CorrectingAim ? "선행 보정 활성 · 초록 표시는 포구의 발사 방향" : "표적을 조준선 8° 안에 두면 선행 보정", Game.CorrectingAim ? Lead : Dim);
            Vector3 point = (solution.AimPoint - Game.RenderOrigin).ToVector3();
            if (!cam.IsPositionBehind(point))
            {
                Vector2 p = cam.UnprojectPosition(point);
                DrawRect(new Rect2(p - new Vector2(6, 6), new Vector2(12, 12)), Lead, false, 1.5f);
                float pixels = screen.Y * 0.5f / Mathf.Tan(Mathf.DegToRad(cam.Fov) * 0.5f);
                float error = Mathf.Clamp((float)(solution.ErrorMeters / Math.Max(1, solution.Range)) * pixels, 2, 80);
                DrawArc(p, error, 0, Mathf.Tau, 24, new Color(Lead, 0.3f), 1f);
                DrawString(_font, p + new Vector2(10, -8), $"선행 {solution.FlightTime:0.00}s", HorizontalAlignment.Left, -1, 12, Lead);
            }
        }
        else Line(2, Game.FiringSolution?.Reason ?? "검사 표적을 선택하세요 (R)", Motion);
        if (Game.World.Time - Game.LastFireTime < 2) Line(4, Game.LastFireMessage, Dim);
        ProjectileImpact? impact = Game.World.Impacts.LastOrDefault(i => i.Shooter == controlled.Body);
        if (impact is null) return;
        Line(5, $"명중: {impact.Hit.Target?.Callsign} · {impact.Hit.Summary}", impact.Hit.ShieldStopped ? Friendly : Hostile);
        if (Game.World.Time - impact.Time < 0.7)
        {
            Vector3 point = (impact.Hit.Point - Game.RenderOrigin).ToVector3();
            if (!cam.IsPositionBehind(point)) DrawArc(cam.UnprojectPosition(point), 13, 0, Mathf.Tau, 24, Hostile, 2);
        }

        void Line(int row, string text, Color color) => DrawString(_font, new Vector2(18, top + 20 + row * 20), text, HorizontalAlignment.Left, -1, 13, color);
    }
}
