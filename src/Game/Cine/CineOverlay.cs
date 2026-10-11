using System;
using Godot;

namespace SpaceFleet.Game.Cine;

/// <summary>
/// 티저의 2D 층: 시네마 레터박스(2.35:1), 국면 띠(아래 막대), 짧은 자막, 탐지 화면, 제목.
/// 글자는 적게, 도형으로 읽힌다(게임 HUD와 같은 원칙).
/// </summary>
public partial class CineOverlay : Control
{
    private static readonly Color Ink = new(0.92f, 0.95f, 1f);
    private static readonly Color Dim = new(0.55f, 0.62f, 0.72f);
    private static readonly Color Faint = new(0.3f, 0.38f, 0.48f, 0.6f);
    private static readonly Color Accent = new(0.4f, 0.76f, 1f);
    private static readonly Color Hostile = new(1f, 0.42f, 0.3f);
    private static readonly Color Lock = new(0.35f, 1f, 0.66f);
    private static readonly string[] Phases = { "탐지", "미사일", "포격", "정밀타격", "난전" };

    private SystemFont _latin = null!, _latinBold = null!, _korean = null!, _koreanBold = null!;

    public CineShot? Shot { get; set; }
    public double Local { get; set; }
    public float Fade { get; set; }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _latin = new SystemFont { FontNames = new[] { "Bahnschrift", "Segoe UI" }, FontWeight = 300 };
        _latinBold = new SystemFont { FontNames = new[] { "Bahnschrift", "Segoe UI" }, FontWeight = 600 };
        _korean = new SystemFont { FontNames = new[] { "Malgun Gothic" }, FontWeight = 300 };
        _koreanBold = new SystemFont { FontNames = new[] { "Malgun Gothic" }, FontWeight = 700 };
    }

    public override void _Process(double delta) => QueueRedraw();

    // Control 크기 대신 화면 크기를 쓴다(CanvasLayer 아래에서는 앵커가 늦게 잡힌다).
    private Vector2 Screen => GetViewportRect().Size;
    private float S => Mathf.Max(Screen.Y, 108f) / 1080f;
    private float Bar => Mathf.Max(0, (Screen.Y - Screen.X / 2.35f) * 0.5f);

    public override void _Draw()
    {
        if (Shot is null) return;
        Vector2 size = Screen;
        if (Shot.Card) DrawRect(new Rect2(Vector2.Zero, size), new Color(0.015f, 0.022f, 0.032f));
        if (Shot.Name == "contact") DrawContact(size);
        if (!Shot.Card) DrawCaptions(size);
        if (Shot.Name == "title") DrawTitle(size);
        if (Fade > 0) DrawRect(new Rect2(Vector2.Zero, size), new Color(0, 0, 0, Fade));

        // 레터박스와 막대 위 표시는 페이드 위에 그린다.
        DrawRect(new Rect2(0, 0, size.X, Bar), Colors.Black);
        DrawRect(new Rect2(0, size.Y - Bar, size.X, Bar), Colors.Black);
        DrawPhaseStrip(size);
        Text(_korean, new Vector2(size.X - 48 * S, Bar * 0.5f + 6 * S), "실제 게임 화면 · 개발 중", 14, new Color(Dim, 0.55f), HorizontalAlignment.Right);
    }

    private void Text(Font font, Vector2 at, string text, float px, Color color, HorizontalAlignment align = HorizontalAlignment.Left, float spacing = 0)
    {
        int size = Mathf.RoundToInt(px * S);
        if (spacing <= 0)
        {
            float w = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
            float x = align switch { HorizontalAlignment.Center => at.X - w / 2, HorizontalAlignment.Right => at.X - w, _ => at.X };
            DrawString(font, new Vector2(x, at.Y), text, HorizontalAlignment.Left, -1, size, color);
            return;
        }
        // 자간을 벌린 제목 글자.
        float total = 0;
        foreach (char c in text) total += font.GetStringSize(c.ToString(), HorizontalAlignment.Left, -1, size).X + spacing * S;
        total -= spacing * S;
        float cx = align switch { HorizontalAlignment.Center => at.X - total / 2, HorizontalAlignment.Right => at.X - total, _ => at.X };
        foreach (char c in text)
        {
            DrawString(font, new Vector2(cx, at.Y), c.ToString(), HorizontalAlignment.Left, -1, size, color);
            cx += font.GetStringSize(c.ToString(), HorizontalAlignment.Left, -1, size).X + spacing * S;
        }
    }

    private static float Window(double t, double from, double to, double ramp = 0.35) =>
        (float)Math.Clamp(Math.Min((t - from) / ramp, (to - t) / ramp), 0, 1);

    private void DrawCaptions(Vector2 size)
    {
        foreach (var (from, to, text, sub) in Shot!.Captions)
        {
            float a = Window(Local, from, to);
            if (a <= 0) continue;
            float slide = (1 - a) * 18 * S;
            var at = new Vector2(96 * S - slide, size.Y - Bar - 92 * S);
            DrawRect(new Rect2(at.X - 18 * S, at.Y - 34 * S, 3 * S, 64 * S), new Color(Accent, a));
            Text(_koreanBold, at, text, 30, new Color(Ink, a));
            Text(_korean, at + new Vector2(0, 30 * S), sub, 17, new Color(Dim, a));
        }
    }

    private void DrawPhaseStrip(Vector2 size)
    {
        int phase = Shot!.Phase;
        float y = size.Y - Bar * 0.5f;
        float item = 190 * S;
        float x0 = size.X * 0.5f - item * 2.5f;
        float show = Shot.Phase == 0 && !Shot.Card ? 0 : 1;
        if (show <= 0) return;
        for (int i = 0; i < Phases.Length; i++)
        {
            bool active = i + 1 == phase;
            bool past = i + 1 < phase;
            float x = x0 + item * i;
            Color c = active ? Ink : past ? new Color(Dim, 0.8f) : new Color(Faint, 0.8f);
            DrawRect(new Rect2(x + 8 * S, y - 26 * S, item - 16 * S, (active ? 3 : 1) * S), active ? Accent : c);
            if (active)
            {
                // 장면 진행에 따라 차오르는 선.
                float p = (float)Math.Clamp(Local / Shot.Duration, 0, 1);
                DrawRect(new Rect2(x + 8 * S, y - 26 * S, (item - 16 * S) * p, 3 * S), Ink);
            }
            Text(_latin, new Vector2(x + 8 * S, y + 2 * S), $"0{i + 1}", 13, active ? Accent : c);
            Text(active ? _koreanBold : _korean, new Vector2(x + 36 * S, y + 2 * S), Phases[i], 15, c);
        }
    }

    // ── 탐지 화면 ─────────────────────────────────────────────

    private static readonly (float Angle, float Range, int Pips)[] Contacts =
    {
        (-92, 0.86f, 3), (-97, 0.82f, 2), (-88, 0.83f, 2), (-104, 0.9f, 2), (-80, 0.91f, 2),
        (-110, 0.95f, 1), (-71, 0.96f, 1), (-100, 0.97f, 1), (-84, 0.98f, 1),
    };

    private void DrawContact(Vector2 size)
    {
        double t = Local;
        var c = new Vector2(size.X * 0.63f, size.Y * 0.84f);
        float r = size.Y * 0.62f;
        // 거리 고리와 방위선(위쪽 반원만).
        for (int i = 1; i <= 4; i++)
            DrawArc(c, r * i / 4f, Mathf.Pi, Mathf.Tau, 96, i == 4 ? new Color(Faint, 0.9f) : Faint, 1.2f * S);
        for (int a = 0; a <= 12; a++)
        {
            float ang = Mathf.Pi + Mathf.Pi * a / 12f;
            DrawLine(c + Vector2.FromAngle(ang) * r * 0.08f, c + Vector2.FromAngle(ang) * r, new Color(Faint, 0.35f), 1 * S);
        }
        // 회전하는 탐색 빔.
        float sweep = Mathf.Pi + (float)(t * 1.6 % 1.0) * Mathf.Pi;
        for (int k = 0; k < 18; k++)
        {
            float a0 = sweep - k * 0.025f;
            DrawLine(c, c + Vector2.FromAngle(a0) * r, new Color(Accent, 0.22f * (1 - k / 18f)), 2.2f * S);
        }
        // 접촉 → 식별 → 잠금.
        for (int i = 0; i < Contacts.Length; i++)
        {
            var (angle, range, pips) = Contacts[i];
            double appear = 0.35 + i * 0.09;
            if (t < appear) continue;
            float a = (float)Math.Min(1, (t - appear) / 0.25);
            double identify = 1.9 + i * 0.07;
            double jitterT = Math.Min(t, identify);
            var p = c + Vector2.FromAngle(Mathf.DegToRad(angle)) * r * range
                + new Vector2((float)Math.Sin(jitterT * 3.1 + i) * 6, (float)Math.Cos(jitterT * 2.7 + i * 2) * 6) * S * (t < identify ? 1 : 0);
            if (t < identify)
            {
                float err = (26 - (float)(t - appear) * 4) * S;
                DrawArc(p, err, 0, Mathf.Tau, 32, new Color(Hostile, 0.35f * a), 1.2f * S);
                Text(_latinBold, p + new Vector2(0, 6 * S), "?", 16, new Color(Hostile, a), HorizontalAlignment.Center);
                continue;
            }
            float b = (float)Math.Min(1, (t - identify) / 0.2);
            float h = (pips == 3 ? 15 : pips == 2 ? 11 : 8) * S;
            float arm = h * 0.6f;
            foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            {
                var corner = p + new Vector2(sx * h, sy * h);
                DrawLine(corner, corner - new Vector2(sx * arm, 0), new Color(Hostile, b), 1.6f * S);
                DrawLine(corner, corner - new Vector2(0, sy * arm), new Color(Hostile, b), 1.6f * S);
            }
            for (int k = 0; k < pips; k++)
                DrawCircle(p + new Vector2(-h + k * 6 * S, h + 7 * S), 2 * S, new Color(Hostile, b));
            if (i == 0 && t > 3.0)
            {
                float l = (float)Math.Min(1, (t - 3.0) / 0.25);
                float d = h * 0.9f;
                DrawPolyline(new[] { p + new Vector2(0, -d), p + new Vector2(d, 0), p + new Vector2(0, d), p + new Vector2(-d, 0), p + new Vector2(0, -d) }, new Color(Lock, l), 2 * S);
                DrawDashedLine(c, p, new Color(Lock, 0.45f * l), 1.2f * S, 8 * S);
            }
        }
        DrawCircle(c, 5 * S, Accent);

        // 거리와 단계.
        double km = 151.4 - Math.Min(t, 4.6) * 0.62;
        var left = new Vector2(size.X * 0.1f, size.Y * 0.42f);
        Text(_latin, left, $"{km:0.0} km", 92, Ink);
        Text(_korean, left + new Vector2(4 * S, 52 * S), "적은 아직 점 하나다.", 22, Dim);
        string[] steps = { "접촉", "식별", "잠금" };
        double[] at = { 0.4, 1.9, 3.0 };
        for (int i = 0; i < 3; i++)
        {
            bool on = t >= at[i];
            var s = left + new Vector2(4 * S + i * 120 * S, 130 * S);
            Color col = on ? (i == 2 ? Lock : i == 1 ? Ink : Hostile) : Faint;
            DrawRect(new Rect2(s.X, s.Y - 24 * S, 96 * S, (on ? 3 : 1) * S), col);
            Text(on ? _koreanBold : _korean, s, steps[i], 18, col);
        }
    }

    // ── 제목 ──────────────────────────────────────────────────

    private void DrawTitle(Vector2 size)
    {
        double t = Local;
        float a = (float)Math.Clamp((t - 1.3) / 1.0, 0, 1);
        if (a <= 0) return;
        var c = new Vector2(size.X * 0.5f, size.Y * 0.47f);
        float line = (float)Math.Clamp((t - 1.6) / 1.2, 0, 1);
        Text(_latinBold, c, "PROJECT FLEET", 84, new Color(Ink, a), HorizontalAlignment.Center, spacing: 16);
        float w = 520 * S * (float)CineMath.Ease(line);
        DrawRect(new Rect2(c.X - w / 2, c.Y + 26 * S, w, 2 * S), new Color(Accent, a));
        float b = (float)Math.Clamp((t - 2.4) / 0.8, 0, 1);
        Text(_korean, c + new Vector2(0, 74 * S), "탐지 · 미사일 · 포격 · 정밀타격 · 난전", 22, new Color(Dim, b), HorizontalAlignment.Center);
        Text(_korean, c + new Vector2(0, 108 * S), "지구 함대 대 화성 함대 · 3D 우주 함대전 · 개발 중", 17, new Color(Dim, b * 0.8f), HorizontalAlignment.Center);
    }
}
