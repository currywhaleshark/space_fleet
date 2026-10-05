using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>
/// 하단 중앙 계기판.
/// 왼쪽 세로 막대 = 속도(▶ 스로틀 목표, 가로선 순항 한계), 가운데 다이얼 = 미끄럼(가운데 기수, ◇ 실제 이동 방향,
/// 바깥 호 = 정렬까지 남은 시간), 오른쪽 호 = 추력 G. 아래 칩 = 비행보조·방식·부스트·사격보조 상태.
/// </summary>
public partial class Hud
{
    private const float SettleScaleSeconds = 12f;

    private void DrawInstruments(ShipView controlled, Vector2 screen)
    {
        ShipBody body = controlled.Body;
        var panel = new Rect2(screen.X * 0.5f - 200f, screen.Y - 150f, 400f, 118f);
        DrawRect(panel, PanelBack);
        float cx = panel.Position.X + panel.Size.X * 0.5f;
        float top = panel.Position.Y;

        DrawSpeedTape(new Rect2(panel.Position.X + 28f, top + 12f, 14f, 74f), body);
        DrawDriftDial(new Vector2(cx, top + 50f), 38f, body);
        DrawGGauge(new Vector2(panel.End.X - 70f, top + 52f), 30f, body);
        DrawChips(new Vector2(cx, top + 104f), body);
    }

    private void DrawSpeedTape(Rect2 tape, ShipBody body)
    {
        ShipClass cls = body.Class;
        float max = cls.MaxSpeed * cls.BoostMultiplier;
        float speed = body.Velocity.Length();
        // 부스트 구간은 옅게 칠해 순항 한계를 넘었는지 바로 보이게 한다.
        float cruise = cls.MaxSpeed / max;
        DrawRect(new Rect2(tape.Position.X, tape.Position.Y, tape.Size.X, tape.Size.Y * (1f - cruise)), new Color(Motion, 0.08f));
        VBar(tape, speed / max, speed > cls.MaxSpeed + 1f ? Motion : Friendly);
        float cruiseY = tape.End.Y - tape.Size.Y * cruise;
        DrawLine(new Vector2(tape.Position.X - 3f, cruiseY), new Vector2(tape.End.X + 3f, cruiseY), Dim, 1f);

        // 스로틀 목표 ▶. 후진 스로틀은 막대 아래쪽에 빨갛게.
        float throttle = Game.Throttle;
        float ty = throttle >= 0f
            ? tape.End.Y - tape.Size.Y * cruise * throttle
            : tape.End.Y + 6f;
        Color tc = throttle >= 0f ? Text : Hostile;
        DrawColoredPolygon(new[] { new Vector2(tape.Position.X - 9f, ty - 5f), new Vector2(tape.Position.X - 2f, ty), new Vector2(tape.Position.X - 9f, ty + 5f) }, tc);

        Label(new Vector2(tape.End.X + 8f, tape.Position.Y + 12f), $"{speed:0}", 16, Text);
        Label(new Vector2(tape.End.X + 8f, tape.Position.Y + 28f), "m/s", 11, Dim);
    }

    /// <summary>
    /// 미끄럼 다이얼. 가운데가 기수, ◇가 실제 이동 방향이다. 중심에서 멀수록 많이 벗어났고,
    /// 안쪽 원(90°)을 넘으면 옆으로, 바깥 원(180°)에 가까우면 뒤로 가는 중이다.
    /// </summary>
    private void DrawDriftDial(Vector2 c, float r, ShipBody body)
    {
        DrawArc(c, r, 0, Mathf.Tau, 48, Faint, 1.5f);
        DrawArc(c, r * 0.5f, 0, Mathf.Tau, 32, Faint, 1f);
        DrawLine(c + new Vector2(-r, 0), c + new Vector2(r, 0), Faint, 1f);
        DrawLine(c + new Vector2(0, -r), c + new Vector2(0, r), Faint, 1f);
        DrawArc(c, 4f, 0, Mathf.Tau, 12, Friendly, 1.5f); // 기수

        float speed = body.Velocity.Length();
        if (speed < 1f)
            return;
        Vector3 local = body.Orientation.Inverse() * body.Velocity;
        var lateral = new Vector2(local.X, -local.Y);
        float drift = body.DriftDegrees;
        Vector2 dir = lateral.LengthSquared() > 1e-6f ? lateral.Normalized() : Vector2.Zero;
        Vector2 p = c + dir * r * (drift / 180f);
        Color color = drift > 30f ? Hostile : Motion;
        if (drift > 1f)
            DrawLine(c, p, new Color(color, 0.5f), 1.5f);
        DrawDiamond(p, 6f, color);

        // 바깥 호: 지금 기수를 유지할 때 이동 방향이 기수에 맞춰지기까지 남은 시간.
        float settle = body.LateralSettleSeconds;
        if (body.Control.FlightAssist && drift > 1f && float.IsFinite(settle))
            ArcGauge(c, r + 6f, -Mathf.Pi / 2f, Mathf.Tau, settle / SettleScaleSeconds, new Color(color, 0.8f), 3f);
    }

    /// <summary>추력 G 호 게이지. 270° 눈금, 끝이 함종 G 상한.</summary>
    private void DrawGGauge(Vector2 c, float r, ShipBody body)
    {
        float fraction = body.GLoad / body.Class.MaxAccelG;
        ArcGauge(c, r, Mathf.DegToRad(135), Mathf.DegToRad(270), fraction, fraction > 0.95f ? Motion : Friendly, 5f);
        CenteredLabel(c + new Vector2(0, -2), $"{body.GLoad:0.0}", 15, Text);
        CenteredLabel(c + new Vector2(0, 14), "G", 11, Dim);
    }

    /// <summary>상태 칩. 켜진 것만 밝게.</summary>
    private void DrawChips(Vector2 center, ShipBody body)
    {
        bool assist = body.Control.FlightAssist;
        (string Text, bool On, Color Color)[] chips =
        {
            ("보조", assist, Friendly),
            (Game.AssistStyle == AssistStyle.Space ? "우주식" : "항공식", assist, Friendly),
            ("부스트", body.Control.Boost, Motion),
            (body.TurnBraking ? "선회 감속" : "사격보조", body.TurnBraking || Game.FireAssist, body.TurnBraking ? Motion : Lead),
            body.Power.Overheated ? ("과열", true, Hostile) : body.Power.Supply < 0.99f ? ("전력 부족", true, Motion) : ("", false, Faint),
        };
        chips = System.Array.FindAll(chips, c => c.Text.Length > 0);
        const int size = 12;
        float gap = 6f;
        float total = -gap;
        foreach (var chip in chips)
            total += _font.GetStringSize(chip.Text, HorizontalAlignment.Left, -1, size).X + 14f + gap;
        float x = center.X - total * 0.5f;
        foreach (var chip in chips)
        {
            float w = _font.GetStringSize(chip.Text, HorizontalAlignment.Left, -1, size).X + 14f;
            var rect = new Rect2(x, center.Y - 9f, w, 17f);
            Color color = chip.On ? chip.Color : Faint;
            DrawRect(rect, chip.On ? new Color(chip.Color, 0.15f) : new Color(0, 0, 0, 0));
            DrawRect(rect, color, false, 1f);
            Label(new Vector2(x + 7f, center.Y + 4f), chip.Text, size, chip.On ? Text : Dim);
            x += w + gap;
        }
    }
}
