using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>
/// 0단계 디버그 HUD: 조준선, 기수 표시, 원거리 표적 브래킷, 상태 텍스트.
/// </summary>
public partial class Hud : Control
{
    private static readonly Color Text = new(0.82f, 0.9f, 1f, 0.92f);
    private static readonly Color Dim = new(0.82f, 0.9f, 1f, 0.5f);
    private static readonly Color Friendly = new(0.45f, 0.75f, 1f, 0.85f);
    private static readonly Color Hostile = new(1f, 0.42f, 0.32f, 0.9f);

    private Font _font = null!;

    public ScaleTest Game { get; set; } = null!;

    public override void _Ready()
    {
        _font = new SystemFont { FontNames = new[] { "Malgun Gothic", "Segoe UI" } };
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (Game?.Controlled is not ShipView controlled)
            return;

        Camera3D cam = Game.Camera;
        Vector2 size = GetViewportRect().Size;
        Vector2 center = size * 0.5f;

        DrawBrackets(cam, controlled, size);

        // 화면 중앙 = 조준 방향
        DrawLine(center + new Vector2(-14, 0), center + new Vector2(-5, 0), Text, 1.5f);
        DrawLine(center + new Vector2(5, 0), center + new Vector2(14, 0), Text, 1.5f);
        DrawLine(center + new Vector2(0, -14), center + new Vector2(0, -5), Text, 1.5f);
        DrawLine(center + new Vector2(0, 5), center + new Vector2(0, 14), Text, 1.5f);

        // 기수가 실제로 향하는 곳(먼 점을 투영해 시차를 줄인다)
        Vector3 nosePoint = controlled.Position + controlled.Body.Forward * 100_000f;
        if (!cam.IsPositionBehind(nosePoint))
            DrawArc(cam.UnprojectPosition(nosePoint), 9f, 0, Mathf.Tau, 24, Friendly, 1.5f);

        DrawStatus(controlled, size);
    }

    private void DrawBrackets(Camera3D cam, ShipView controlled, Vector2 screen)
    {
        float pxPerRad = screen.Y * 0.5f / Mathf.Tan(Mathf.DegToRad(cam.Fov) * 0.5f);
        var labels = new List<Rect2>();
        // 가까운 것부터 라벨 자리를 잡고, 겹치는 먼 라벨은 생략한다.
        var ordered = Game.Views
            .Where(v => v != controlled && !cam.IsPositionBehind(v.Position))
            .OrderBy(v => (v.SimPosition - controlled.SimPosition).Length());
        foreach (ShipView view in ordered)
        {
            double dist = (view.SimPosition - controlled.SimPosition).Length();
            Vector2 p = cam.UnprojectPosition(view.Position);
            float radius = (float)(view.Body.Class.Length * 0.5 / Math.Max(dist, 1.0)) * pxPerRad;
            if (radius > screen.Y * 0.35f)
                continue; // 가까워서 화면을 덮는 함선은 표시하지 않는다.

            float h = Mathf.Max(9f, radius);
            float arm = Mathf.Min(8f, h * 0.6f);
            Color c = view.Body.Faction == Faction.Blue ? Friendly : Hostile;
            foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            {
                var corner = p + new Vector2(sx * h, sy * h);
                DrawLine(corner, corner - new Vector2(sx * arm, 0), c, 1.5f);
                DrawLine(corner, corner - new Vector2(0, sy * arm), c, 1.5f);
            }

            string label = $"{view.Body.Callsign}  {view.Body.Class.DisplayName}  {FormatDistance(dist)}";
            Vector2 at = p + new Vector2(h + 6, -h + 12);
            var rect = new Rect2(at - new Vector2(0, 12), _font.GetStringSize(label, HorizontalAlignment.Left, -1, 13) + new Vector2(0, 2));
            if (labels.Any(r => r.Intersects(rect)))
                continue;
            labels.Add(rect);
            DrawString(_font, at, label, HorizontalAlignment.Left, -1, 13, c);
        }
    }

    private void DrawStatus(ShipView controlled, Vector2 screen)
    {
        ShipBody body = controlled.Body;
        string[] lines =
        {
            $"조종: {body.Callsign} ({body.Class.DisplayName}, {body.Class.Length:0} m)",
            $"속도 {body.Velocity.Length():0} m/s   스로틀 {Game.Throttle * 100:0}%   비행보조 {(body.Control.FlightAssist ? "ON" : "OFF")}{(body.Control.Boost ? "   부스트" : "")}",
            $"렌더 원점: {(Game.FloatingOrigin ? "카메라 기준(플로팅)" : "월드 0 고정")}   월드 0에서 {FormatDistance(body.Position.Length())}",
            $"FPS {Engine.GetFramesPerSecond():0}   틱 {Game.World.Tick}",
        };
        for (int i = 0; i < lines.Length; i++)
            DrawString(_font, new Vector2(18, 28 + i * 20), lines[i], HorizontalAlignment.Left, -1, 15, i == 0 ? Text : Dim);

        string help = "마우스 조준 · W/S 스로틀 · X 정지 · A/D/Space/Ctrl 평행이동 · Q/E 롤 · Shift 부스트 · Z 비행보조 · Tab 함선 전환 · 휠 줌 · F2 원점 방식 · F3 1,000 km 도약 · Esc 마우스 해제";
        DrawString(_font, new Vector2(18, screen.Y - 18), help, HorizontalAlignment.Left, -1, 13, Dim);
    }

    private static string FormatDistance(double meters) =>
        meters >= 1000 ? $"{meters / 1000:#,0.0} km" : $"{meters:0} m";
}
