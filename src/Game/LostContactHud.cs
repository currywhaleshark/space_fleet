using System;
using System.Collections.Generic;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

public partial class Hud
{
    private void DrawLostContact(Camera3D cam, ShipView controlled, ShipView view, ContactSnapshot contact,
        List<Rect2> labels, Vector2 screen)
    {
        Vector3 render = (contact.Position - Game.RenderOrigin).ToVector3();
        if (cam.IsPositionBehind(render)) return;
        Vector2 p = cam.UnprojectPosition(render);
        if (!new Rect2(Vector2.Zero, screen).HasPoint(p)) return;
        bool selected = view == Game.InspectTarget;
        Color gray = ContactMemory.LostColor;
        DrawDiamond(p, selected ? 10 : 6, gray);
        if (selected) DrawArc(p, 15, 0, Mathf.Tau, 24, gray, 1.5f, true);
        BracketPositions.Add((p, view));
        string label = $"{contact.Name} · 신호 소실  {FormatDistance((contact.Position - controlled.SimPosition).Length())}";
        Vector2 at = p + new Vector2(18, -12);
        var rect = new Rect2(at - new Vector2(0, 14), _font.GetStringSize(label, fontSize: 13));
        if (!selected && labels.Exists(r => r.Intersects(rect))) return;
        labels.Add(rect); Label(at, label, 13, gray);
    }

    private void DrawLostTargetPanel(Vector2 screen, ContactSnapshot contact, ShipView me)
    {
        float x = screen.X - PanelWidth - 2, y = 28, width = PanelWidth - 20;
        Color gray = ContactMemory.LostColor;
        DrawRect(new Rect2(screen.X - PanelWidth - 12, 8, PanelWidth, contact.State is null ? 170 : 290), PanelBack);
        Label(new(x, y), contact.Name, 15, gray);
        Label(new(x, y), "신호 소실", 14, gray, HorizontalAlignment.Right, width); y += 25;
        Label(new(x, y), $"마지막 접촉 {Math.Max(0, Game.World.Time - contact.LastSeenAt):0}초 전", 13, gray); y += 24;
        Label(new(x, y), $"최종 위치 · {FormatDistance((contact.Position - me.Body.Position).Length())}", 13, gray); y += 22;
        Label(new(x, y), $"X {contact.Position.X / 1000:0.0} · Y {contact.Position.Y / 1000:0.0} · Z {contact.Position.Z / 1000:0.0} km", 12, gray); y += 26;
        if (contact.State is not { } state)
        { Label(new(x, y), "함종·피해 상태 미확인", 13, gray); return; }
        Label(new(x, y), $"{state.ClassName} · 최종 상태 {state.Status}", 13, gray); y += 24;
        Label(new(x, y), $"실드 {state.Shield:P0} · 파괴 모듈 {state.LostModules}", 13, gray); y += 24;
        foreach (var (label, value) in new[] { ("추진", state.Propulsion), ("자세", state.Maneuver),
            ("무장", state.Weapons), ("센서", state.Sensors), ("냉각", state.Cooling) })
        {
            Label(new(x, y), label, 12, gray);
            HBar(new Rect2(x + 42, y - 9, width - 92, 6), value, gray);
            Label(new(x, y), $"{value:P0}", 12, gray, HorizontalAlignment.Right, width); y += 19;
        }
        Label(new(x, y), $"상태 확인 {Math.Max(0, Game.World.Time - contact.LastIdentifiedAt):0}초 전", 12, gray);
    }
}
