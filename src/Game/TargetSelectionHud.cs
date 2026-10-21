using System;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

// Selection is a presentation of an observed contact, never a firing solution or permission to fire.
internal sealed record SelectedTargetDisplay(object Key, Vec3d Position, string Name,
    TrackLevel Level, bool Lost, bool Destroyed = false)
{
    public string SensorStatus => Lost ? "신호 소실" : Destroyed ? "격침 확인"
        : Level == TrackLevel.Locked ? "센서 잠금" : Level == TrackLevel.Identified ? "식별 · 잠금 전" : "접촉 · 잠금 전";
}

public partial class ScaleTest
{
    internal SelectedTargetDisplay? SelectedTargetDisplay()
    {
        if (Controlled is not { } me) return null;
        if (SelectedSmallContact is { } small)
        {
            bool fresh = small.Fresh(World.Time);
            return new(small.Target, fresh ? small.Predict(World.Time) : small.Position,
                small.Level<TrackLevel.Identified?"소형 접촉":small.Kind == SmallContactKind.Missile ? "적 미사일" : "적 프로브", small.Level, !fresh);
        }
        if (InspectTarget is not { } view || view.Body.Faction == me.Body.Faction || ContactOf(view) is not { } memory)
            return null;
        return new(view, memory.DisplayPosition(view.SimPosition), memory.Name, memory.Track.Level,
            memory.SignalLost, memory.State?.Destroyed == true);
    }
}

public partial class Hud
{
    private static readonly Color SelectionColor = new(1f, .83f, .36f, 1f);
    internal SelectedTargetDisplay? DrawnSelection { get; private set; }
    internal Vector2 SelectionMarkerPosition { get; private set; }
    internal bool SelectionOffscreen { get; private set; }

    private void DrawSelectedTarget(Camera3D camera, Vector2 screen)
    {
        DrawnSelection = Game.SelectedTargetDisplay();
        SelectionOffscreen = false;
        if (DrawnSelection is not { } target) return;
        Color color = target.Lost || target.Destroyed ? ContactMemory.LostColor : SelectionColor;
        Vector3 render = (target.Position - Game.RenderOrigin).ToVector3();
        bool behind = camera.IsPositionBehind(render);
        Vector2 p = behind ? Vector2.Zero : camera.UnprojectPosition(render);
        SelectionOffscreen = behind || !new Rect2(Vector2.One * 30, screen - Vector2.One * 60).HasPoint(p);
        float radius = 24;
        if (SelectionOffscreen)
        {
            Vector2 d = EdgeDirection(camera, render - camera.GlobalPosition);
            p = EdgePoint(d, screen, 80);
            // Keep the selected arrow above the responsive battery/weapon panels.
            var layout = CombatHudLayout.For(screen, Game.Scheme == ControlScheme.Helm, Game.Controlled!.Body.Railguns.Length, HudSettings.TextScale);
            float bottom = Math.Min(layout.Selection.Position.Y, layout.Gunnery.Size.X > 0 ? layout.Gunnery.Position.Y : screen.Y) - 30;
            p.Y = Math.Min(p.Y, bottom);
            Vector2 n = new(-d.Y, d.X);
            Vector2[] arrow = { p + d * 13, p - d * 7 + n * 9, p - d * 7 - n * 9, p + d * 13 };
            DrawPolyline(arrow, Colors.Black, 6, true);
            DrawPolyline(arrow, color, 3, true);
            radius = 18;
        }
        else
        {
            if (target.Level >= TrackLevel.Identified && !target.Lost && Game.InspectTarget is { } ship)
            {
                float pixels = screen.Y * .5f / Mathf.Tan(Mathf.DegToRad(camera.Fov) * .5f);
                radius = Mathf.Clamp((float)(ship.Body.Class.Length * .5 / Math.Max(1, (render - camera.GlobalPosition).Length())) * pixels + 8, 24, 80);
            }
            // Bounded selection corners survive zoom and huge nearby hulls. Hollow center keeps the impact point visible.
            foreach (var (sx, sy) in new[] { (-1,-1), (1,-1), (-1,1), (1,1) })
            {
                Vector2 corner = p + new Vector2(sx * radius, sy * radius);
                Vector2[] lines = { corner - new Vector2(sx * 10, 0), corner, corner - new Vector2(0, sy * 10) };
                DrawPolyline(lines, Colors.Black, 6, true);
                DrawPolyline(lines, color, 3, true);
            }
            if (!target.Lost && Game.FireTarget == Game.InspectTarget) DrawOnscreenBlocked(p);
        }
        SelectionMarkerPosition = p;
        _markerLabels.ProtectSymbol(p, radius);
        double range = (target.Position - Game.Controlled!.SimPosition).Length();
        string label = $"선택 · {target.Name}  {FormatDistance(range)} · {target.SensorStatus}";
        if (SelectionOffscreen) label += behind ? " · 후방" : " · 화면 밖";
        _markerLabels.Add(("selection", target.Key), p, new Vector2(radius + 8, -radius - 6), label, color, 120, 14);
    }

    private void DrawSelectedTargetReadout(CombatHudLayout layout)
    {
        Rect2 row = layout.Selection;
        var target = DrawnSelection;
        Color color = target?.Lost == true || target?.Destroyed == true ? ContactMemory.LostColor : SelectionColor;
        DrawRect(row, PanelBack);
        DrawRect(new Rect2(row.Position, new Vector2(3, row.Size.Y)), target is null ? Dim : color);
        string name = target is null ? "선택 표적 없음 · R 표적 선택"
            : $"{(Game.SelectedWeapon == PlayerWeapon.Missile ? "미사일 표적" : "선택")} · {target.Name}  {FormatDistance((target.Position - Game.Controlled!.SimPosition).Length())}";
        // A scoped main battery follows the crosshair; missile guidance still uses the selected contact.
        string mode = target?.Lost == true ? "신호 소실" : Game.SelectedWeapon != PlayerWeapon.MainGun ? ""
            : Game.SelectedSmallContact is not null ? "지정 표적 요격"
            : Game.Camera.TelescopeHeld ? Game.Scheme == ControlScheme.Pilot ? "기수 연동 조준" : "중앙 직접 조준"
            : Game.Scheme == ControlScheme.Helm && Game.Gunnery?.Doctrine == FireDoctrine.Manual ? "마우스 직접 조준" : "";
        float available = row.Size.X - 24 - _font.GetStringSize(mode, fontSize: HudFont(12)).X;
        string shown = name;
        while (shown.Length > 1 && _font.GetStringSize(shown, fontSize: HudFont(13)).X > available)
            shown = shown[..^1];
        if (shown != name) shown = shown[..^1] + "…";
        Label(row.Position + new Vector2(9, 17), shown, 13, target is null ? Dim : color);
        Label(row.Position + new Vector2(0, 17), mode, 12, Dim, HorizontalAlignment.Right, row.Size.X - 7);
    }
}
