using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>
/// 미사일 표시. 내 진영 미사일은 파란 갈매기(탐색기 잠금이면 채움), 적 미사일은 빨간 삼각형.
/// 나를 노리는 미사일은 크게, 화면 밖이면 가장자리 빨간 화살표. 조준선 아래 호 = 미사일 잔량, 위 호 = 디코이 잔량.
/// </summary>
public partial class Hud
{
    private static readonly Color MissileOwn = new(0.55f, 0.8f, 1f, 0.95f);
    private static readonly Color DecoyColor = new(1f, 0.95f, 0.8f, 0.9f);
    /// <summary>이 거리 안의 적 미사일은 경보 수신기로 보인다(m).</summary>
    private const float MissileWarningRange = 60_000f;

    /// <summary>나를 노리는 적 미사일 수(계기판 칩용).</summary>
    public int IncomingMissiles { get; private set; }

    private void DrawMissileMarkers(Camera3D cam, ShipView controlled, Vector2 screen)
    {
        ShipBody me = controlled.Body;
        int incoming = 0;
        foreach (Missile m in Game.World.Missiles)
        {
            bool own = m.Faction == me.Faction;
            bool threat = !own && m.Target == me;
            if (threat) incoming++;
            if (!own && (m.Position - me.Position).Length() > MissileWarningRange) continue;

            Vector3 render = (m.Position - Game.RenderOrigin).ToVector3();
            bool behind = cam.IsPositionBehind(render);
            Vector2 p = behind ? Vector2.Zero : cam.UnprojectPosition(render);
            bool onScreen = !behind && new Rect2(Vector2.Zero, screen).HasPoint(p);
            if (!onScreen)
            {
                if (threat)
                    DrawThreatArrow(cam, (m.Position - me.Position).ToVector3().Normalized(), screen);
                continue;
            }

            if (own)
            {
                // 갈매기: 탐색기가 무언가를 잡았으면 채운다.
                Vector2[] chevron = { p + new Vector2(-6, 4), p + new Vector2(0, -5), p + new Vector2(6, 4), p + new Vector2(0, 0) };
                if (m.SeekerLocked) DrawColoredPolygon(chevron, MissileOwn);
                else DrawPolyline(new[] { chevron[0], chevron[1], chevron[2] }, MissileOwn, 1.5f);
            }
            else
            {
                float s = threat ? 9f : 6f;
                Vector2[] tri = { p + new Vector2(0, -s), p + new Vector2(s * 0.9f, s * 0.7f), p + new Vector2(-s * 0.9f, s * 0.7f) };
                if (threat) DrawColoredPolygon(tri, new Color(Hostile, 0.85f));
                else DrawPolyline(new[] { tri[0], tri[1], tri[2], tri[0] }, Hostile, 1.5f);
            }
        }
        IncomingMissiles = incoming;
    }

    /// <summary>화면 밖 위협 방향의 빨간 화살표(이동 방향 화살표와 같은 배치, 색만 다르다).</summary>
    private void DrawThreatArrow(Camera3D cam, Vector3 worldDir, Vector2 screen)
    {
        Vector3 local = cam.GlobalBasis.Inverse() * worldDir;
        var d = new Vector2(local.X, -local.Y);
        if (d.LengthSquared() < 1e-6f) d = Vector2.Down;
        d = d.Normalized();
        Vector2 center = screen * 0.5f;
        float rx = screen.X * 0.5f - 52f, ry = screen.Y * 0.5f - 52f;
        float t = Mathf.Min(rx / Mathf.Max(Mathf.Abs(d.X), 1e-4f), ry / Mathf.Max(Mathf.Abs(d.Y), 1e-4f));
        Vector2 at = center + d * t;
        Vector2 n = new(-d.Y, d.X);
        DrawColoredPolygon(new[] { at + d * 12f, at - d * 6f + n * 8f, at - d * 6f - n * 8f }, Hostile);
    }

    /// <summary>조준선 아래·위 호: 미사일·디코이 잔량.</summary>
    private void DrawOrdnanceArcs(ShipView controlled, Vector2 c)
    {
        OrdnanceState o = controlled.Body.Ordnance;
        if (o.MissileDefinition is MissileDefinition md)
        {
            Color color = o.MissileReady ? MissileOwn : new Color(MissileOwn, 0.45f);
            ArcGauge(c, CrosshairRadius + 6f, Mathf.DegToRad(120), Mathf.DegToRad(-60), o.Missiles / (float)md.Rounds, color, 3f);
        }
        if (o.DecoyDefinition is DecoyDefinition dd)
            ArcGauge(c, CrosshairRadius + 6f, Mathf.DegToRad(240), Mathf.DegToRad(60), o.Decoys / (float)dd.Count,
                o.DecoyReady ? DecoyColor : new Color(DecoyColor, 0.4f), 3f);
    }
}
