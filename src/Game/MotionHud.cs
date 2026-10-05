using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>
/// 이동 방향 단서: 진행 ◇, 역진행 ⊗, 화면 밖 진행 방향 화살표, 예측 경로 점, 미끄럼 각도와 정렬 예상 시간.
/// 기수와 이동 방향이 벌어지는 우주식 비행에서 "지금 어디로 가는가"를 바로 읽게 하는 것이 목적이다.
/// </summary>
public partial class Hud
{
    private static readonly float[] PredictSeconds = { 1f, 2f, 3f, 4f, 5f };

    private void DrawMotionCues(Camera3D cam, ShipView controlled, Vector2 screen)
    {
        ShipBody body = controlled.Body;
        float speed = body.Velocity.Length();
        if (speed < 1f)
            return;
        Vector3 dir = body.Velocity / speed;

        // 예측 경로: 지금 속도로 1~5초 뒤 위치. 미끄러지는 중이면 점들이 기수와 다른 쪽으로 늘어선다.
        for (int i = 0; i < PredictSeconds.Length; i++)
        {
            Vector3 point = controlled.Position + body.Velocity * PredictSeconds[i];
            if (cam.IsPositionBehind(point))
                continue;
            float fade = 1f - i * 0.15f;
            DrawCircle(cam.UnprojectPosition(point), 2.5f, new Color(Motion, 0.8f * fade));
        }

        // 진행 방향 ◇. 화면 밖이면 가장자리 화살표로 위치를 알린다.
        Vector3 prograde = controlled.Position + dir * 100_000f;
        Rect2 safe = new Rect2(Vector2.One * 40f, screen - Vector2.One * 80f);
        Vector2 p = cam.UnprojectPosition(prograde);
        if (!cam.IsPositionBehind(prograde) && safe.HasPoint(p))
            DrawDiamond(p, 7f, Motion);
        else
            DrawEdgeArrow(cam, dir, screen, "이동 방향");

        // 역진행 ⊗. 뒤로 미끄러질 때 화면 앞쪽에 나타난다.
        Vector3 retrograde = controlled.Position - dir * 100_000f;
        if (!cam.IsPositionBehind(retrograde))
        {
            Vector2 r = cam.UnprojectPosition(retrograde);
            DrawArc(r, 7f, 0, Mathf.Tau, 20, Motion, 1.5f);
            DrawLine(r + new Vector2(-5, -5), r + new Vector2(5, 5), Motion, 1.5f);
            DrawLine(r + new Vector2(-5, 5), r + new Vector2(5, -5), Motion, 1.5f);
        }

        // 미끄럼 각도와 정렬 예상 시간. 화면 중앙 아래에 크게 둔다.
        float drift = body.DriftDegrees;
        if (drift > 3f && speed > 5f)
        {
            float settle = body.LateralSettleSeconds;
            string text = drift > 90f
                ? $"역방향 이동 {drift:0}° · {speed:0} m/s"
                : $"미끄럼 {drift:0}°{(body.Control.FlightAssist && float.IsFinite(settle) ? $" · 정렬 ~{settle:0.0}s" : "")}";
            Color color = drift > 30f ? Hostile : Motion;
            Vector2 textSize = _font.GetStringSize(text, HorizontalAlignment.Left, -1, 16);
            DrawString(_font, new Vector2(screen.X * 0.5f - textSize.X * 0.5f, screen.Y * 0.5f + 70f), text,
                HorizontalAlignment.Left, -1, 16, color);
        }
    }

    private void DrawDiamond(Vector2 p, float r, Color color)
    {
        DrawLine(p + new Vector2(0, -r), p + new Vector2(r, 0), color, 1.5f);
        DrawLine(p + new Vector2(r, 0), p + new Vector2(0, r), color, 1.5f);
        DrawLine(p + new Vector2(0, r), p + new Vector2(-r, 0), color, 1.5f);
        DrawLine(p + new Vector2(-r, 0), p + new Vector2(0, -r), color, 1.5f);
    }

    /// <summary>화면 밖 방향을 가장자리 화살표로 표시한다. 카메라 뒤쪽도 올바른 쪽을 가리킨다.</summary>
    private void DrawEdgeArrow(Camera3D cam, Vector3 worldDir, Vector2 screen, string label)
    {
        Vector3 local = cam.GlobalBasis.Inverse() * worldDir;
        var d = new Vector2(local.X, -local.Y);
        if (d.LengthSquared() < 1e-6f)
            d = Vector2.Down; // 정확히 뒤쪽이면 아래를 가리킨다.
        d = d.Normalized();

        Vector2 center = screen * 0.5f;
        float rx = screen.X * 0.5f - 70f, ry = screen.Y * 0.5f - 70f;
        float t = Mathf.Min(rx / Mathf.Max(Mathf.Abs(d.X), 1e-4f), ry / Mathf.Max(Mathf.Abs(d.Y), 1e-4f));
        Vector2 at = center + d * t;
        Vector2 n = new(-d.Y, d.X);
        Vector2[] tri = { at + d * 14f, at - d * 6f + n * 9f, at - d * 6f - n * 9f };
        DrawColoredPolygon(tri, new Color(Motion, 0.85f));
        // 라벨은 화살표 안쪽(화면 중앙 쪽)에 둔다. 가로 방향이면 글자 폭만큼 더 물린다.
        Vector2 textSize = _font.GetStringSize(label, HorizontalAlignment.Left, -1, 13);
        float inset = 16f + Mathf.Abs(d.X) * textSize.X * 0.5f + Mathf.Abs(d.Y) * textSize.Y;
        DrawString(_font, at - d * inset - textSize * new Vector2(0.5f, -0.35f), label, HorizontalAlignment.Left, -1, 13, Motion);
    }
}
