using System;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

public partial class Hud
{
    private static readonly (int A, int B)[] BoxEdges =
    {
        (0, 1), (0, 2), (0, 4), (1, 3), (1, 5), (2, 3),
        (2, 6), (3, 7), (4, 5), (4, 6), (5, 7), (6, 7),
    };

    private void DrawModuleVolumes(Camera3D cam, ShipView target)
    {
        foreach (ModuleState module in target.Body.Damage.Modules)
        {
            ModuleDefinition def = module.Definition;
            var points = new Vector2[8];
            bool behind = false;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = def.Center + new Vector3(
                    (i & 1) == 0 ? -def.HalfSize.X : def.HalfSize.X,
                    (i & 2) == 0 ? -def.HalfSize.Y : def.HalfSize.Y,
                    (i & 4) == 0 ? -def.HalfSize.Z : def.HalfSize.Z);
                Vector3 world = target.Transform * corner;
                if (cam.IsPositionBehind(world)) { behind = true; break; }
                points[i] = cam.UnprojectPosition(world);
            }
            if (behind) continue;
            Rect2 bounds = new(points[0], Vector2.Zero);
            foreach (Vector2 p in points) bounds = bounds.Expand(p);
            if (bounds.Size.Length() < 3f) continue;
            Color color = module.Destroyed ? Hostile : module.HealthFraction < 0.99f ? Motion
                : new Color(0.3f, 0.75f, 1f, 0.32f);
            foreach (var edge in BoxEdges) DrawLine(points[edge.A], points[edge.B], color, 1f);
            if (module.HealthFraction < 0.99f)
            {
                string label = $"{def.Name} · {module.HealthFraction:P0}";
                Vector2 at = bounds.Position + new Vector2(0, -5);
                DrawString(_font, at + Vector2.One, label, HorizontalAlignment.Left, -1, 12, Colors.Black);
                DrawString(_font, at, label, HorizontalAlignment.Left, -1, 12, color);
            }
        }
    }

    private void DrawDamagePanel(Vector2 screen)
    {
        if (Game.InspectTarget is not ShipView target) return;
        ShipDamage damage = target.Body.Damage;
        const float width = 340f;
        float x = screen.X - width - 12f;
        float height = 168 + damage.Modules.Count * 17 + (damage.Reports.Count > 0 ? 25 + damage.Reports.Count * 18 : 0);
        DrawRect(new Rect2(x, 8, width, height), new Color(0.01f, 0.02f, 0.03f, 0.9f));
        float y = 28;
        Line($"검사: {target.Body.Callsign} · {target.Body.Class.DisplayName}{(damage.Destroyed ? " · 격침" : "")}", Text, 15);
        Line($"실드 {damage.Shield:0}/{damage.ShieldCapacity:0}", Friendly);
        float shieldRatio = damage.ShieldCapacity > 0 ? damage.Shield / damage.ShieldCapacity : 0;
        DrawRect(new Rect2(x + 12, y - 6, width - 24, 4), new Color(0.2f, 0.3f, 0.4f, 0.5f));
        DrawRect(new Rect2(x + 12, y - 6, (width - 24) * shieldRatio, 4), Friendly);
        y += 10;
        Line($"좌현 전력 {damage.GridPower(PowerGrid.Port):P0}   우현 전력 {damage.GridPower(PowerGrid.Starboard):P0}", Text);
        Line($"추진 {damage.PropulsionFraction:P0}   자세 {damage.ManeuverFraction:P0}   무장 {damage.WeaponsFraction:P0}", Dim);
        Line($"센서 {damage.SensorFraction:P0}   냉각 {damage.CoolingFraction:P0}", Dim);
        Line($"내부 모듈 · F5 공간 표시 {(Game.ShowModules ? "ON" : "OFF")}", Text);
        foreach (ModuleState module in damage.Modules)
        {
            string status = module.Destroyed ? "파괴" : module.HealthFraction.ToString("P0");
            Color color = module.Destroyed ? Hostile : module.HealthFraction < 0.99f ? Motion : Dim;
            DrawString(_font, new Vector2(x + 12, y), module.Definition.Name, HorizontalAlignment.Left, -1, 13, color);
            DrawString(_font, new Vector2(x + width - 72, y), status, HorizontalAlignment.Right, 60, 13, color);
            y += 17;
        }
        if (damage.Reports.Count == 0) return;
        y += 6;
        Line("피해 리포트", Text);
        foreach (DamageReport report in damage.Reports)
            Line($"{report.Time:0.0}s  {report.Message}", report.Message.Contains("격침") || report.Message.Contains("단절") ? Hostile : Motion);

        void Line(string text, Color color, int fontSize = 13)
        {
            DrawString(_font, new Vector2(x + 12, y), text, HorizontalAlignment.Left, width - 24, fontSize, color);
            y += 20;
        }
    }

    private void DrawShotFeedback(Camera3D cam)
    {
        if (Game.LastTestShot is not ShotResult shot || Game.World.Time - Game.LastTestShotTime > 3.0) return;
        Color color = shot.ShieldStopped ? Friendly : shot.Modules.Count > 0 ? Hostile : Motion;
        DrawString(_font, new Vector2(18, 306), $"시험 레이: {shot.Target?.Callsign ?? "표적 없음"} · {shot.Summary}", HorizontalAlignment.Left, -1, 15, color);
        Vector3 point = (shot.Point - Game.RenderOrigin).ToVector3();
        if (shot.Target is not null && !cam.IsPositionBehind(point))
            DrawArc(cam.UnprojectPosition(point), 14f, 0, Mathf.Tau, 24, color, 2f);
    }
}
