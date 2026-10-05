using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>
/// 하단 오른쪽 전력·열 패널.
/// 위 막대 = 발전(채움 = 가용 발전량, 흰 선 = 수요, 빨강 = 공급 부족분).
/// 가운데 = 채널 4개의 핍(1~4 키), 옆 가는 막대 = 실제 소비 전력. 공급 부족이면 핍이 주황으로 바랜다.
/// 오른쪽 세로 막대 = 열(70% 회복선, 100% 과열선). 과열이면 패널 테두리가 빨갛게 깜박인다.
/// </summary>
public partial class Hud
{
    private static readonly (PowerChannel Channel, string Label, string Key, Color Color)[] Channels =
    {
        (PowerChannel.Engines, "추진", "1", new Color(1f, 0.78f, 0.35f, 0.95f)),
        (PowerChannel.Shields, "실드", "2", new Color(0.45f, 0.75f, 1f, 0.95f)),
        (PowerChannel.Weapons, "무장", "3", new Color(1f, 0.5f, 0.42f, 0.95f)),
        (PowerChannel.Sensors, "센서", "4", new Color(0.35f, 0.9f, 0.7f, 0.95f)),
    };

    // 소비 막대·수요선 표시값. 0.15초짜리 순간 피크가 깜박이지 않게 약 0.3초로 부드럽게 따라간다.
    private readonly float[] _drawShown = new float[ShipPower.ChannelCount];
    private float _demandShown;
    private ShipBody? _powerShip;

    private void DrawPowerPanel(ShipView controlled, Vector2 screen)
    {
        ShipPower power = controlled.Body.Power;
        if (_powerShip != controlled.Body)
        {
            // 함선을 바꾸면 이전 함선 값에서 미끄러져 오지 않게 바로 맞춘다.
            _powerShip = controlled.Body;
            for (int i = 0; i < ShipPower.ChannelCount; i++)
                _drawShown[i] = power.DrawMw((PowerChannel)i) / power.MaxDrawMw((PowerChannel)i);
            _demandShown = power.DemandMw / controlled.Body.Definition.Power.OutputMw;
        }
        PowerDefinition def = controlled.Body.Definition.Power;
        var panel = new Rect2(screen.X - 266f, screen.Y - 150f, 250f, 118f);
        DrawRect(panel, PanelBack);
        if (power.Overheated && (int)(Game.World.Time * 4) % 2 == 0)
            DrawRect(panel, Hostile, false, 2f);

        float x = panel.Position.X + 10f, y = panel.Position.Y;
        float barWidth = panel.Size.X - 56f;

        // 발전 막대: 정격 기준. 수요가 가용량을 넘으면 넘친 만큼 빨갛게.
        var gen = new Rect2(x, y + 10f, barWidth, 8f);
        float available = power.AvailableMw / def.OutputMw;
        float demand = _demandShown = Smooth(_demandShown, power.DemandMw / def.OutputMw);
        HBar(gen, available, power.Supply < 0.99f ? Motion : Friendly);
        if (demand > available + 0.005f)
            DrawRect(new Rect2(gen.Position.X + gen.Size.X * Mathf.Min(available, 1f), gen.Position.Y,
                gen.Size.X * (Mathf.Min(demand, 1.2f) - Mathf.Min(available, 1f)), gen.Size.Y), new Color(Hostile, 0.7f));
        float dx = gen.Position.X + gen.Size.X * Mathf.Min(demand, 1.2f);
        DrawLine(new Vector2(dx, gen.Position.Y - 3f), new Vector2(dx, gen.End.Y + 3f), Text, 1.5f);

        // 채널 핍
        float slot = barWidth / Channels.Length;
        float pipW = 16f, pipH = 11f, gap = 3f;
        float pipsBottom = y + 92f;
        for (int i = 0; i < Channels.Length; i++)
        {
            var (channel, label, key, color) = Channels[i];
            float cx = x + slot * i + slot * 0.5f;
            int pips = power.Pips(channel);
            Color lit = power.Supply < 0.99f ? color.Lerp(Motion, 0.6f) : color;
            if (power.Overheated)
                lit = new Color(lit, 0.5f);
            for (int p = 0; p < ShipPower.MaxPips; p++)
            {
                var r = new Rect2(cx - pipW * 0.5f - 4f, pipsBottom - (p + 1) * (pipH + gap), pipW, pipH);
                if (p < pips)
                    DrawRect(r, lit);
                else
                    DrawRect(r, Faint, false, 1f);
            }
            // 실제 소비 전력(4핍 최대 활동 기준).
            float draw = _drawShown[i] = Smooth(_drawShown[i], power.DrawMw(channel) / power.MaxDrawMw(channel));
            VBar(new Rect2(cx + pipW * 0.5f - 1f, pipsBottom - 4 * (pipH + gap) + gap, 3f, 4 * (pipH + gap) - gap), draw, new Color(color, 0.8f));
            CenteredLabel(new Vector2(cx - 2f, pipsBottom - 4 * (pipH + gap) - 3f), key, 10, Dim);
            CenteredLabel(new Vector2(cx - 2f, pipsBottom + 13f), label, 11, pips != ShipPower.BalancedPips ? color : Dim);
        }

        // 열 막대
        var heat = new Rect2(panel.End.X - 30f, y + 10f, 14f, 90f);
        float hf = power.HeatFraction / ShipPower.HeatCeiling;
        Color heatColor = power.Overheated ? Hostile : power.HeatFraction >= ShipPower.RecoverHeatFraction ? Motion : Friendly;
        VBar(heat, hf, heatColor);
        foreach (float mark in new[] { ShipPower.RecoverHeatFraction, 1f })
        {
            float my = heat.End.Y - heat.Size.Y * mark / ShipPower.HeatCeiling;
            DrawLine(new Vector2(heat.Position.X - 3f, my), new Vector2(heat.End.X + 3f, my), mark >= 1f ? Hostile : Dim, 1f);
        }
        // 냉각이 발열보다 느리면 위 화살표(열이 오르는 중).
        if (power.HeatInMw > power.CoolingMw + 0.5f)
            DrawColoredPolygon(new[] { new Vector2(heat.Position.X - 9f, heat.Position.Y + 6f), new Vector2(heat.Position.X - 5f, heat.Position.Y),
                new Vector2(heat.Position.X - 1f, heat.Position.Y + 6f) }, heatColor);
        CenteredLabel(new Vector2(heat.Position.X + 7f, pipsBottom + 13f), "열", 11, power.Overheated ? Hostile : Dim);
    }
}
