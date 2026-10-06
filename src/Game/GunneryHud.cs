using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

public partial class Hud
{
    private void DrawGunnery(Camera3D cam, ShipView controlled, Vector2 screen)
    {
        if (Game.Gunnery is not { } order || controlled.Body.Railgun is not { } gun) return;
        Vector2 c = new(screen.X * 0.5f, screen.Y - 225);
        DrawRect(new Rect2(c - new Vector2(170, 40), new Vector2(340, 90)), PanelBack);
        Color status = order.Status == GunneryStatus.HullBlocked ? Hostile : order.Status is GunneryStatus.Firing ? Good : Dim;
        DrawRect(new Rect2(c - new Vector2(36, 12), new Vector2(72, 24)), new Color(Friendly, 0.15f));
        CenteredLabel(c, GunneryLabels.Doctrine(order.Doctrine), 14, Text);
        ArcGauge(c, 48, Mathf.DegToRad(145), Mathf.DegToRad(70), gun.Rounds / (float)gun.Definition.Rounds, Friendly, 4);
        ArcGauge(c, 48, Mathf.DegToRad(35), Mathf.DegToRad(-70), gun.Output <= 0.01f || controlled.Body.Power.Overheated ? 0
            : 1 - gun.ReloadRemaining / gun.Definition.ReloadSeconds, gun.Ready ? Good : Motion, 4);
        CenteredLabel(c + new Vector2(0, 33), GunneryLabels.Status(order.Status), 13, status);
        if (order.Engaged is { } target) CenteredLabel(c + new Vector2(0, -28), target.Callsign, 12, Hostile);
        DrawLeadMarker(cam, screen);
        DrawHitMarker(cam, controlled, c);
        DrawOrdnanceArcs(controlled, c + new Vector2(90, 0));
    }
}
