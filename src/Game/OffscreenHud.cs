using System.Collections.Generic;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>
/// 화면 밖 방향 표시. 가장자리 띠를 바깥부터 미사일 · 함선 · 진행 방향으로 나눈다.
/// 나를 노리는 미사일 = 깜박이는 빨간 채운 삼각형, 적 함선 = 빈 빨간 화살촉 + 함종 점,
/// 사격 표적 = 굵은 화살촉 + 고리(쏠 수 있으면 초록, 포각 밖·선체 가림이면 빨강 ⊘).
/// 화면 안의 사격 표적도 쏠 수 없으면 ⊘를 겹친다(조함 방식만).
/// </summary>
public partial class Hud
{
    /// <summary>이 거리 안의 식별된 적 함선은 화면 밖이어도 방향을 보인다(m).</summary>
    private const double OffscreenShipRange = 80_000;
    private const float MissileBand = 44f, ShipBand = 72f;

    /// <summary>이번 프레임 사격 표적으로의 사선 상태(조함 방식). None이면 쏠 수 있는 자세.</summary>
    private FireFailure _engagedLine = FireFailure.None;
    private Vector3 _engagedLocal;
    private bool _hasEngagedLine;

    private readonly List<Vector2> _edgeMarks = new();

    /// <summary>사격 표적 방향과 그 방향의 사선 상태. 표적은 센서가 아는 위치(추정)로 잰다.</summary>
    private void UpdateEngagedLine(ShipView controlled)
    {
        _hasEngagedLine = false;
        _engagedLine = FireFailure.None;
        if (Game.FireTarget is not ShipView target || controlled.Body.Railgun is null) return;
        Vector3 direction = Game.Scheme == ControlScheme.Helm && Game.Gunnery?.Solution is { Valid: true } solution
            ? solution.Direction
            : EstimatedDirection(controlled, target);
        if (direction.LengthSquared() < 1e-8f) return;
        _engagedLocal = (controlled.Body.Orientation.Inverse() * direction).Normalized();
        _engagedLine = SimWorld.RailLineLocal(controlled.Body, _engagedLocal);
        _hasEngagedLine = true;
    }

    private Vector3 EstimatedDirection(ShipView controlled, ShipView target)
    {
        SensorTrack track = Game.TrackOf(target);
        return (target.SimPosition + track.Offset - controlled.SimPosition).ToVector3().Normalized();
    }

    private void DrawOffscreenShips(Camera3D cam, ShipView controlled, Vector2 screen)
    {
        _edgeMarks.Clear();
        ShipView? focus = Game.FireTarget;
        var candidates = new List<(ShipView View, double Dist)>();
        foreach (ShipView view in Game.Views)
        {
            if (view == controlled || view.Body.Faction == controlled.Body.Faction || view.Body.Damage.Destroyed) continue;
            SensorTrack track = Game.TrackOf(view);
            bool isFocus = view == focus;
            if (track.Level < (isFocus ? TrackLevel.Contact : TrackLevel.Identified)) continue;
            Vec3d at = view.SimPosition + track.Offset;
            double dist = (at - controlled.SimPosition).Length();
            if (!isFocus && dist > OffscreenShipRange) continue;
            Vector3 render = (at - Game.RenderOrigin).ToVector3();
            if (!cam.IsPositionBehind(render) && new Rect2(Vector2.Zero, screen).Grow(-8f).HasPoint(cam.UnprojectPosition(render)))
            {
                if (isFocus) DrawOnscreenBlocked(cam.UnprojectPosition(render));
                continue;
            }
            candidates.Add((view, isFocus ? -1 : dist));
        }
        // 표적이 먼저, 그다음 가까운 순. 거의 같은 자리의 화살촉은 하나만 그린다.
        candidates.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        foreach (var (view, dist) in candidates)
        {
            Vector3 dir = EstimatedDirection(controlled, view);
            Vector2 d = EdgeDirection(cam, dir);
            Vector2 at = EdgePoint(d, screen, ShipBand);
            if (_edgeMarks.Exists(p => p.DistanceTo(at) < 16f)) continue;
            _edgeMarks.Add(at);
            Vector2 n = new(-d.Y, d.X);
            Vector2[] head = { at + d * 11f, at - d * 5f + n * 9f, at - d * 5f - n * 9f, at + d * 11f };
            if (view != focus)
            {
                DrawPolyline(head, Hostile, 1.5f);
                DrawClassPipsCentered(at - d * 16f, view.Body.Class.Kind, Hostile);
                continue;
            }
            bool helm = Game.Scheme == ControlScheme.Helm;
            bool blocked = helm && _hasEngagedLine && _engagedLine != FireFailure.None;
            Color c = !helm ? Hostile : blocked ? Hostile : Lead;
            // 화살촉 안쪽 고리: 쏠 수 있으면 함종 점, 포각 밖·선체 가림이면 ⊘.
            DrawPolyline(head, c, 2.5f);
            Vector2 ring = at - d * 22f;
            DrawArc(ring, 10f, 0, Mathf.Tau, 24, c, 2f);
            if (blocked) DrawNoFire(ring, 10f);
            else DrawClassPipsCentered(ring, view.Body.Class.Kind, c);
        }
    }

    /// <summary>화면 안 사격 표적이 포각 밖이거나 선체에 가리면 ⊘를 겹친다.</summary>
    private void DrawOnscreenBlocked(Vector2 p)
    {
        if (Game.Scheme != ControlScheme.Helm || !_hasEngagedLine || _engagedLine == FireFailure.None) return;
        DrawArc(p, 20f, 0, Mathf.Tau, 32, Hostile, 2f);
        DrawNoFire(p, 20f);
    }

    /// <summary>금지 표시의 사선(원은 호출하는 쪽이 그린다).</summary>
    private void DrawNoFire(Vector2 p, float r)
    {
        Vector2 slash = new Vector2(1, -1).Normalized() * r * 0.72f;
        DrawLine(p - slash, p + slash, Hostile, r < 14f ? 2f : 2.5f);
    }

    private void DrawOffscreenMissile(Camera3D cam, Vector3 worldDir, Vector2 screen)
    {
        Vector2 d = EdgeDirection(cam, worldDir);
        Vector2 at = EdgePoint(d, screen, MissileBand);
        Vector2 n = new(-d.Y, d.X);
        // 깜박임은 실제 시간 기준(시간 배속·정지와 무관하게 눈에 띄게).
        float blink = (Time.GetTicksMsec() / 180) % 2 == 0 ? 1f : 0.45f;
        DrawColoredPolygon(new[] { at + d * 10f, at - d * 5f + n * 6f, at - d * 5f - n * 6f }, new Color(Hostile, blink));
    }

    internal static Vector2 EdgeDirection(Camera3D cam, Vector3 worldDir)
    {
        Vector3 local = cam.GlobalBasis.Inverse() * worldDir;
        var d = new Vector2(local.X, -local.Y);
        return d.LengthSquared() < 1e-6f ? Vector2.Down : d.Normalized(); // 정확히 뒤쪽이면 아래를 가리킨다.
    }

    /// <summary>화면 중앙에서 d 방향으로 나가 가장자리 띠(inset)에 닿는 점. 아래쪽은 하단 패널 위에서 멈춘다.</summary>
    private static Vector2 EdgePoint(Vector2 d, Vector2 screen, float inset)
    {
        const float BottomPanels = 110f;
        Vector2 c = screen * 0.5f;
        float tx = (d.X >= 0 ? screen.X - inset - c.X : c.X - inset) / Mathf.Max(Mathf.Abs(d.X), 1e-4f);
        float ty = (d.Y >= 0 ? screen.Y - inset - BottomPanels - c.Y : c.Y - inset) / Mathf.Max(Mathf.Abs(d.Y), 1e-4f);
        return c + d * Mathf.Min(tx, ty);
    }

    private void DrawClassPipsCentered(Vector2 at, HullKind kind, Color color)
    {
        int count = kind switch { HullKind.Battleship => 3, HullKind.Escort => 2, _ => 1 };
        ClassPips(at - new Vector2((count - 1) * 3f, 0), kind, color);
    }
}
