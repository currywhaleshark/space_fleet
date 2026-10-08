using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

public partial class Hud
{
    public Rect2 RadarBounds { get; private set; }
    public bool RadarContains(Vector2 point) => RadarBounds.Size.X > 0
        && point.DistanceTo(RadarBounds.GetCenter()) <= RadarBounds.Size.X * .5f;

    private void DrawRadar(ShipView controlled, Vector2 screen)
    {
        RadarBounds = default;
        if (Game.ShowHelp) return;
        float scale = Mathf.Clamp(Mathf.Min(screen.X / 1600f, screen.Y / 900f), .6f, 1);
        Vector2 origin = new(16, 16);
        DrawSetTransform(origin, 0, Vector2.One * scale);
        Vector2 center = new(124, 124);
        const float radius = 112;
        RadarBounds = new Rect2(origin + (center - Vector2.One * radius) * scale, Vector2.One * radius * 2 * scale);
        Vector2 Project(Vector3 point) { var p = TacticalRadar.Project(point); return center + new Vector2(p.X, p.Y) * radius; }
        DrawCircle(center, radius, new Color(.015f, .04f, .065f, .3f));
        DrawArc(center, radius, 0, Mathf.Tau, 80, new Color(Friendly, .25f), 1, true);

        // 기준면과 높이 축: 가까운 반구는 밝게, 뒤쪽은 옅은 점선으로.
        void Ring(float latitude, bool meridian = false, float longitude = 0, float size = 1)
        {
            Vector3 At(float t) => meridian ? new Vector3(Mathf.Cos(t) * Mathf.Cos(longitude), Mathf.Sin(t), Mathf.Cos(t) * Mathf.Sin(longitude))
                : new Vector3(Mathf.Cos(t) * Mathf.Cos(latitude), Mathf.Sin(latitude), Mathf.Sin(t) * Mathf.Cos(latitude));
            for (int i = 0; i < 64; i++)
            {
                Vector3 a = At(i * Mathf.Tau / 64) * size, b = At((i + 1) * Mathf.Tau / 64) * size;
                bool front = TacticalRadar.Project((a + b) * .5f).Z > 0;
                if (!front && i % 2 == 0) continue;
                float alpha = front ? .26f : .10f;
                if (!meridian && latitude == 0) alpha *= 1.5f;
                DrawLine(Project(a), Project(b), new Color(Friendly, alpha), 1, true);
            }
        }
        Ring(0); Ring(0, size: .5f); Ring(.52f); Ring(-.52f);
        Ring(0, meridian: true); Ring(0, meridian: true, longitude: Mathf.Pi / 2);
        DrawLine(Project(Vector3.Down), Project(Vector3.Up), new Color(Friendly, .3f), 1, true);
        DrawLine(center, Project(Vector3.Forward), new Color(Friendly, .5f), 1, true);
        CenteredLabel(Project(Vector3.Forward * 1.1f), "전", 12, Text);
        CenteredLabel(Project(Vector3.Up * 1.1f), "상", 12, Dim);
        CenteredLabel(Project(Vector3.Back * 1.1f), "후", 12, Dim);

        var contacts = new List<(ShipView View, RadarContact Contact)>();
        ShipView? selected = Game.InspectTarget ?? Game.FireTarget;
        bool Focus(ShipView view) => view == selected || view == Game.SelectedFriendly;
        foreach (ShipView view in Game.Views)
        {
            var contact = TacticalRadar.Locate(controlled.Body, controlled.SimPosition, controlled.Basis.GetRotationQuaternion(),
                view.Body, view.SimPosition, Game.TrackOf(view), Game.ContactOf(view));
            if (contact is not { } located) continue;
            if (located.Distance > Game.Radar.Range && !Focus(view)) continue;
            contacts.Add((view, located));
        }
        // 선별은 표적·가까운 함선 우선, 그리기는 깊이 순. 밀집된 점은 숫자로 묶는다.
        var groups = new List<(ShipView View, RadarContact Contact, Vector2 Point, int Count)>();
        foreach (var (view, contact) in contacts.OrderBy(c => Focus(c.View) ? -1 : c.Contact.Distance))
        {
            Vector3 unit = contact.Local / Game.Radar.Range;
            Vector2 point = Project(unit.LimitLength(1));
            int index = groups.FindIndex(g => g.Point.DistanceTo(point) < 10 && g.Contact.Enemy == contact.Enemy
                && g.Contact.Local.DistanceTo(contact.Local) < Game.Radar.Range * .08f
                && g.Contact.Identified == contact.Identified && g.Contact.SignalLost == contact.SignalLost && !Focus(g.View) && !Focus(view));
            if (index >= 0) { var old = groups[index]; groups[index] = (old.View, old.Contact, old.Point, old.Count + 1); }
            else groups.Add((view, contact, point, 1));
        }
        foreach (var group in groups.OrderBy(g => TacticalRadar.Project(g.Contact.Local).Z))
        {
            var (view, contact, point, count) = group;
            bool focus = Focus(view), beyond = contact.Distance > Game.Radar.Range;
            Vector3 unit = (contact.Local / Game.Radar.Range).LimitLength(1);
            Vector2 foot = Project(new Vector3(unit.X, 0, unit.Z));
            Color color = contact.SignalLost ? ContactMemory.LostColor : !contact.Enemy ? Friendly : contact.Identified ? Hostile : Motion;
            if (TacticalRadar.Project(unit).Z < 0 && !focus) color = new Color(color, .6f);
            if (point.DistanceTo(foot) > 3)
            {
                DrawLine(foot, point, new Color(color, .5f), focus ? 2 : 1, true);
                DrawCircle(foot, 2, new Color(color, .4f));
            }
            if (contact.Uncertainty > 0)
                DrawArc(point, Mathf.Clamp(contact.Uncertainty / Game.Radar.Range * radius, 5, 18),
                    0, Mathf.Tau, 24, new Color(color, .24f), 1, true);
            if (!contact.Identified) DrawArc(point, 4, 0, Mathf.Tau, 16, color, 1.5f, true);
            else if (contact.Enemy) DrawDiamond(point, 4.5f, color);
            else DrawCircle(point, 3.5f, color);
            if (focus)
            {
                DrawArc(point, 9, 0, Mathf.Tau, 24, contact.SignalLost ? color : Text, 1.5f, true);
                if (contact.SignalLost) CenteredLabel(point + new Vector2(0, -16), "신호 소실", 11, color);
                else if (beyond) CenteredLabel(point + new Vector2(0, -16), "범위 밖", 11, Motion);
            }
            if (count > 1) Label(point + new Vector2(7, -4), count.ToString(), 11, color);
        }
        Vector2 forward = (Project(Vector3.Forward) - center).Normalized(), side = forward.Orthogonal();
        DrawColoredPolygon(new[] { center + forward * 7, center - forward * 5 + side * 4, center - forward * 5 - side * 4 }, Colors.White);
        DrawCircle(center, 2, new Color(.02f, .07f, .11f));

        DrawSetTransform(Vector2.Zero);
    }
}

public partial class ScaleTest
{
    public TacticalRadar Radar { get; } = new();
    private bool RadarPointerCaptured => !MenuOpen && !ShowHelp && !Camera.FreeLooking
        && Input.MouseMode == Input.MouseModeEnum.Visible && _hud.RadarContains(GetViewport().GetMousePosition());

    private bool HandleRadarInput(InputEvent e)
    {
        if (e.IsActionPressed(InputSetup.RadarNear)) Radar.Zoom(-1);
        else if (e.IsActionPressed(InputSetup.RadarFar)) Radar.Zoom(1);
        else if (e is InputEventMouseButton button && Input.MouseMode == Input.MouseModeEnum.Visible
            && !ShowHelp && !Camera.FreeLooking && _hud.RadarContains(button.Position)
            && button.ButtonIndex != MouseButton.Middle)
        {
            if (button.Pressed)
            {
                if (button.ButtonIndex == MouseButton.WheelUp) Radar.Zoom(-1);
                else if (button.ButtonIndex == MouseButton.WheelDown) Radar.Zoom(1);
                else if (button.ButtonIndex == MouseButton.Left) ToggleWorldMap();
            }
        }
        else return false;
        GetViewport().SetInputAsHandled(); return true;
    }
}
