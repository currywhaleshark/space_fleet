using System;
using System.Linq;
using Godot;

namespace SpaceFleet.Game;

public partial class Hud
{
    private void DrawOffscreenExplosions(Vector2 screen)
    {
        foreach(var ship in Game.World.Ships)
        {
            double age=Game.World.Time-ship.Damage.DestroyedAt;
            if(!ship.Damage.Destroyed || age<0 || age>1.2) continue;
            Vector3 point=(ship.Position-Game.RenderOrigin).ToVector3();
            if(!Game.Camera.IsPositionBehind(point) && new Rect2(Vector2.Zero,screen).HasPoint(Game.Camera.UnprojectPosition(point))) continue;
            Vector2 d=EdgeDirection(Game.Camera,(point-Game.Camera.Position).Normalized());
            Vector2 at=EdgePoint(d,screen,130);
            Color color=new(1,.8f,.5f,(float)(1-age/1.2));
            DrawDiamond(at,8,color); DrawLine(at-d*5,at+d*14,color,2);
            CenteredLabel(at-d*30+new Vector2(0,5),"섬광",11,color);
        }
    }
    private static Color FeedbackColor(HitKind kind) => kind switch
    { HitKind.Shield => Friendly, HitKind.Armor => Motion, HitKind.Penetration => Hostile, _ => new Color(1, .23f, .16f) };

    private void DrawIncomingFeedback(Vector2 screen)
    {
        if (Game.Spectating) return;
        Vector2 center = screen * .5f;
        foreach (var hit in Game.Feedback.Incoming)
        {
            Vector3 local = Game.Camera.Basis.Inverse() * hit.SourceDirection;
            bool knownDirection = hit.SourceDirection.LengthSquared() > .01f;
            Vector2 direction = new(local.X, -local.Y);
            if (direction.LengthSquared() < .025f) direction = local.Z > 0 ? Vector2.Down : Vector2.Up;
            float angle = direction.Angle();
            Color color = new(FeedbackColor(hit.Kind), hit.Fade * .85f);
            float radiusX = screen.X * .42f, radiusY = screen.Y * .36f;
            if (knownDirection)
            {
                var points = new Vector2[19];
                float spread = hit.Kind == HitKind.Critical ? .15f : .10f;
                for (int i = 0; i < points.Length; i++)
                {
                    float a = angle - spread + 2 * spread * i / (points.Length - 1);
                    points[i] = center + new Vector2(Mathf.Cos(a) * radiusX, Mathf.Sin(a) * radiusY);
                }
                DrawPolyline(points, color, hit.Kind >= HitKind.Penetration ? 5 : 3, true);
                Vector2 tip = center + new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY);
                Vector2 inward = (center - tip).Normalized(), tangent = inward.Orthogonal();
                DrawColoredPolygon(new[] { tip + inward * 14, tip + tangent * 6, tip - tangent * 6 }, color);
                string side = local.Z > .45f ? "후방" : local.Z < -.45f ? "전방" : "";
                string vertical = local.Y > .3f ? "상부" : local.Y < -.3f ? "하부" : "";
                string lateral = local.X > .35f ? "우측" : local.X < -.35f ? "좌측" : "";
                string label = string.Join(" · ", new[] {side, lateral, vertical}.Where(s => s.Length > 0));
                CenteredLabel(tip + inward * 37 + new Vector2(0, 5), label, 13, color);
            }
        }
        var strongest = Game.Feedback.Incoming.OrderByDescending(h => h.Kind).ThenBy(h => h.Age).FirstOrDefault();
        if (strongest is null) return;
        Color alert = new(FeedbackColor(strongest.Kind), strongest.Fade);
        string message = "피격 · " + strongest.Label;
        Vector2 textSize = _font.GetStringSize(message, fontSize: 17);
        Vector2 at = new(center.X, screen.Y * .25f);
        DrawRect(new Rect2(at - new Vector2(textSize.X * .5f + 15, 22), textSize + new Vector2(30, 12)),
            new Color(PanelBack, .7f * strongest.Fade));
        CenteredLabel(at, message, 17, alert);
        // 작은 가장자리 빛만 남겨 중앙의 표적과 계기를 계속 읽을 수 있게 한다.
        float opacity = strongest.Fade * strongest.Strength * .12f;
        for (int i = 0; i < 4; i++)
            DrawRect(new Rect2(Vector2.One * (i * 4), screen - Vector2.One * (i * 8)),
                new Color(alert, opacity * (1 - i / 4f)), false, 4);
    }

    private void DrawTargetFeedback(SpaceFleet.Sim.ShipBody target, Vector2 point, float radius)
    {
        if (Game.Feedback.Outgoing is not { } hit || hit.Target != target) return;
        float r = Mathf.Max(radius + 5, 17) + 8 * Mathf.Exp(-hit.Age * 12);
        Color color = new(FeedbackColor(hit.Kind), hit.Fade);
        DrawArc(point, r, -.8f, .8f, 16, color, 2.5f);
        DrawArc(point, r, Mathf.Pi - .8f, Mathf.Pi + .8f, 16, color, 2.5f);
    }
}
