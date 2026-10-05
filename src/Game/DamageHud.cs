using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>
/// 표적 피해 표시. 오른쪽 위 패널에 선체 도면(상면·측면)을 그리고 내부 모듈을 체력 색으로 칠한다.
/// 피해 리포트는 이 게임의 핵심 피드백이라 짧은 글로 남긴다.
/// </summary>
public partial class Hud
{
    private static readonly (int A, int B)[] BoxEdges =
    {
        (0, 1), (0, 2), (0, 4), (1, 3), (1, 5), (2, 3),
        (2, 6), (3, 7), (4, 5), (4, 6), (5, 7), (6, 7),
    };

    private const float PanelWidth = 320f;
    private const float ViewHeight = 76f;

    private void DrawTargetPanel(Vector2 screen)
    {
        if (Game.InspectTarget is not ShipView target || Game.Controlled is not ShipView me)
            return;
        ShipBody body = target.Body;
        ShipDamage damage = body.Damage;
        var reports = damage.Reports.Skip(Math.Max(0, damage.Reports.Count - 4)).ToArray();
        float height = 46f + (ViewHeight + 10f) * 2f + 70f + reports.Length * 17f + 8f;
        var panel = new Rect2(screen.X - PanelWidth - 12f, 8f, PanelWidth, height);
        DrawRect(panel, PanelBack);
        float x = panel.Position.X + 10f, inner = PanelWidth - 20f;
        float y = panel.Position.Y;

        Color identity = damage.Destroyed ? Dim : body.Faction == Faction.Blue ? Friendly : Hostile;
        Label(new Vector2(x, y + 20f), body.Callsign, 15, identity);
        ClassPips(new Vector2(x + _font.GetStringSize(body.Callsign, HorizontalAlignment.Left, -1, 15).X + 8f, y + 15f), body.Class.Kind, identity);
        // 거리가 주황이면 내 주포 사거리 밖이다.
        double distance = (body.Position - me.Body.Position).Length();
        bool outOfRange = me.Body.Railgun is RailgunState gun && distance > gun.Definition.MaxRange;
        Label(new Vector2(x, y + 20f), damage.Destroyed ? "격침" : FormatDistance(distance), 13,
            damage.Destroyed ? Hostile : outOfRange ? Motion : Dim, HorizontalAlignment.Right, inner);
        DrawShieldBar(new Rect2(x, y + 30f, inner, 8f), body);
        y += 46f;

        // 도면마다 칸에 맞춰 축척을 따로 잡는다. 날개·방열판 폭 때문에 측면도까지 작아지지 않게 한다.
        var bounds = HullBounds(body.Definition);
        float topScale = Mathf.Min(inner * 0.92f / bounds.Size.Z, ViewHeight / bounds.Size.X);
        float sideScale = Mathf.Min(inner * 0.92f / bounds.Size.Z, ViewHeight / bounds.Size.Y);
        DrawSchematic(new Rect2(x, y, inner, ViewHeight), body, bounds, topScale, top: true);
        y += ViewHeight + 10f;
        DrawSchematic(new Rect2(x, y, inner, ViewHeight), body, bounds, sideScale, top: false);
        y += ViewHeight + 10f;

        DrawSystemBars(new Rect2(x, y, inner, 62f), damage);
        y += 72f;

        // 최근 리포트 4줄. 오래된 것일수록 흐리게.
        for (int i = 0; i < reports.Length; i++)
        {
            DamageReport report = reports[i];
            bool severe = report.Message.Contains("격침") || report.Message.Contains("단절") || report.Message.Contains("파괴");
            float age = (float)(Game.World.Time - report.Time);
            float alpha = Mathf.Clamp(1f - age / 12f, 0.35f, 1f);
            Color color = severe ? Hostile : report.Message.StartsWith("실드") ? Friendly : Motion;
            Label(new Vector2(x, y + 12f), report.Message, 12, new Color(color, color.A * alpha), HorizontalAlignment.Left, inner);
            y += 17f;
        }
    }

    /// <summary>선체 구획 전체를 감싸는 로컬 경계 상자.</summary>
    private static Aabb HullBounds(ShipDefinition definition)
    {
        Aabb box = new(definition.HullSections[0].Center - definition.HullSections[0].HalfSize, definition.HullSections[0].HalfSize * 2f);
        foreach (HullSection s in definition.HullSections)
            box = box.Merge(new Aabb(s.Center - s.HalfSize, s.HalfSize * 2f));
        return box;
    }

    /// <summary>
    /// 함선 도면. 함수(-Z)가 오른쪽. 상면도는 좌현이 위, 측면도는 상부가 위.
    /// 모듈은 큰 것부터 그려 작은 모듈이 가려지지 않게 한다.
    /// </summary>
    private void DrawSchematic(Rect2 area, ShipBody body, Aabb bounds, float scale, bool top)
    {
        Vector2 origin = area.Position + area.Size * 0.5f;
        Vector3 mid = bounds.GetCenter();
        Rect2 Project(Vector3 center, Vector3 half)
        {
            float u = (mid.Z - center.Z) * scale;
            float v = top ? (center.X - mid.X) * scale : (mid.Y - center.Y) * scale;
            var size = new Vector2(half.Z * 2f * scale, (top ? half.X : half.Y) * 2f * scale);
            return new Rect2(origin + new Vector2(u, v) - size * 0.5f, size);
        }

        foreach (HullSection s in body.Definition.HullSections)
        {
            Rect2 r = Project(s.Center, s.HalfSize);
            DrawRect(r, new Color(Text, 0.05f));
            DrawRect(r, new Color(Text, 0.22f), false, 1f);
        }

        double now = Game.World.Time;
        foreach (ModuleState m in body.Damage.Modules.OrderByDescending(m =>
                     m.Definition.HalfSize.Z * (top ? m.Definition.HalfSize.X : m.Definition.HalfSize.Y)))
        {
            Rect2 r = Project(m.Definition.Center, m.Definition.HalfSize);
            r = r.GrowIndividual(0, 0, Mathf.Max(0, 2f - r.Size.X), Mathf.Max(0, 2f - r.Size.Y)); // 너무 작은 모듈도 보이게
            float f = m.HealthFraction;
            Color fill = m.Destroyed ? new Color(Hostile, 0.75f) : new Color(Health(f), f < 0.99f ? 0.65f : 0.3f);
            DrawRect(r, fill);
            if (m.Destroyed)
            {
                DrawLine(r.Position, r.End, Hostile, 1f);
                DrawLine(new Vector2(r.Position.X, r.End.Y), new Vector2(r.End.X, r.Position.Y), Hostile, 1f);
            }
            float flash = Mathf.Clamp(1f - (float)(now - m.LastHitTime) / 0.8f, 0f, 1f);
            if (flash > 0f)
                DrawRect(r.Grow(1.5f), new Color(1f, 1f, 1f, flash), false, 1.5f);
        }
        Label(area.Position + new Vector2(0, 11f), top ? "상면" : "측면", 11, Dim);
    }

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
                Vector2 at = bounds.Position + new Vector2(0, -5);
                Label(at + Vector2.One, def.Name, 12, Colors.Black);
                Label(at, def.Name, 12, color);
            }
        }
    }

    /// <summary>F4 시험 레이 착탄점 표시. 결과 글은 표적 패널의 리포트로 대신한다.</summary>
    private void DrawShotFeedback(Camera3D cam)
    {
        if (Game.LastTestShot is not ShotResult shot || Game.World.Time - Game.LastTestShotTime > 3.0) return;
        Color color = shot.ShieldStopped ? Friendly : shot.Modules.Count > 0 ? Hostile : Motion;
        Vector3 point = (shot.Point - Game.RenderOrigin).ToVector3();
        if (shot.Target is not null && !cam.IsPositionBehind(point))
            DrawArc(cam.UnprojectPosition(point), 14f, 0, Mathf.Tau, 24, color, 2f);
    }
}
