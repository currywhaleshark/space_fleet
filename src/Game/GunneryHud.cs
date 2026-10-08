using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>
/// 조함 방식 사격 패널(하단, 계기판 오른쪽 — 내 함선 모델을 가리지 않는다).
/// 왼쪽 고리 = 교리(가운데) · 탄약(왼쪽) · 재장전(오른쪽) · 미사일(아래) · 디코이(위).
/// 가운데 = 교전 표적 호출부호 · 거리 · 짧은 실패 사유(포각·선체 가림은 글자 없이 방위구로).
/// 오른쪽 사격 방위구 = 함선 기준 전 방향. 가운데가 기수, 가장자리가 함미, 위쪽이 함선 위쪽.
/// 초록 = 주포가 쏠 수 있는 방향, 빨강 = 자함 선체가 가리는 방향, 바깥 = 포각 밖.
/// 표적 점이 초록 안이면 쏠 수 있고, 빨강·바깥이면 X — 표적이 초록 쪽으로 오도록 롤·기수를 돌린다.
/// </summary>
public partial class Hud
{
    private const float SphereRadius = 44f;
    private const int SphereAzimuthCells = 48;
    private const float SphereCellDegrees = 6f;

    /// <summary>함선별 사각 격자. 포탑 회전·모듈 파손을 반영해 주기적으로 갱신한다.</summary>
    private readonly Dictionary<ShipBody, bool[,]> _blindZones = new();

    private void DrawGunnery(Camera3D cam, ShipView controlled, Vector2 screen)
    {
        if (Game.Gunnery is not { } order || controlled.Body.Railgun is not { } gun) return;
        float x = screen.X * 0.5f + 212f;
        float width = Mathf.Min(310f, screen.X - 278f - x);
        var panel = new Rect2(x, screen.Y - 150f, width, 118f);
        DrawRect(panel, PanelBack);
        float cy = panel.Position.Y + panel.Size.Y * 0.5f;

        // 왼쪽 고리
        var ring = new Vector2(x + 52f, cy);
        CenteredLabel(ring, GunneryLabels.Doctrine(order.Doctrine), 14, order.Doctrine == FireDoctrine.Hold ? Motion : Text);
        var battery = controlled.Body.Railguns;
        float ammo = battery.Sum(g => g.Rounds) / (float)battery.Sum(g => g.Definition.Rounds);
        ArcGauge(ring, 40f, Mathf.DegToRad(145), Mathf.DegToRad(70), ammo, Friendly, 4);
        float reload = battery.Average(g => g.Output <= .01f || controlled.Body.Power.Overheated ? 0 : 1-g.ReloadRemaining/g.Definition.ReloadSeconds);
        ArcGauge(ring, 40f, Mathf.DegToRad(35), Mathf.DegToRad(-70), reload, battery.Any(g => g.Ready) ? Good : Motion, 4);
        DrawOrdnanceArcs(controlled, ring, 40f);

        // 가운데: 표적
        float mx = x + 150f;
        ContactSnapshot? lost = Game.FireTarget is null && Game.InspectTarget is { } inspect
            && Game.ContactOf(inspect) is { SignalLost: true } memory ? memory : null;
        if (Game.FireTarget is ShipView target)
        {
            CenteredLabel(new Vector2(mx, cy - 14f), target.Body.Callsign, 14, Hostile);
            SensorTrack track = Game.TrackOf(target);
            double dist = (target.SimPosition + track.Offset - controlled.SimPosition).Length();
            CenteredLabel(new Vector2(mx, cy + 6f), FormatDistance(dist), 12, Dim);
        }
        else if (lost is not null)
        {
            CenteredLabel(new(mx, cy - 14), lost.Name, 14, ContactMemory.LostColor);
            CenteredLabel(new(mx, cy + 6), FormatDistance((lost.Position - controlled.SimPosition).Length()), 12, ContactMemory.LostColor);
        }
        string? reason = lost is not null ? "신호 소실" : order.Status switch
        {
            GunneryStatus.WaitLock or GunneryStatus.Range or GunneryStatus.Armor or GunneryStatus.FriendlyLine or GunneryStatus.Traversing => GunneryLabels.Status(order.Status),
            _ => null,
        };
        if (reason is not null) CenteredLabel(new Vector2(mx, cy + 30f), reason, 12, lost is null ? Motion : ContactMemory.LostColor);
        for (int i = 0; i < battery.Length; i++)
        {
            RailgunState mount = battery[i];
            string label = mount.Output <= .01f ? $"{i+1} 파손" : mount.Rounds == 0 ? $"{i+1} 소진"
                : mount.Ready ? $"{i+1} 준비" : $"{i+1} 장전";
            CenteredLabel(new(mx + (i-(battery.Length-1)*.5f)*52, cy+49), label, 10,
                mount.Output <= .01f || mount.Rounds == 0 ? Hostile : mount.Ready ? Good : Motion);
        }

        if (panel.Size.X > 270f)
            DrawFiringSphere(new Vector2(panel.End.X - SphereRadius - 12f, cy), controlled.Body);

        DrawLeadMarker(cam, screen);
        DrawHitMarker(cam, controlled, screen * 0.5f);
    }

    /// <summary>방위구 좌표: 기수에서 벌어진 각을 반지름으로(가장자리 = 180°), 함선 위쪽이 화면 위쪽.</summary>
    private static Vector2 SpherePoint(Vector3 local, float radius)
    {
        float angle = Vector3.Forward.AngleTo(local);
        var flat = new Vector2(local.X, -local.Y);
        if (flat.LengthSquared() < 1e-8f) return Vector2.Zero;
        return flat.Normalized() * angle / Mathf.Pi * radius;
    }

    private void DrawFiringSphere(Vector2 c, ShipBody me)
    {
        RailgunState gun = me.Railgun!;
        float traverse = me.Definition.Railgun!.Mounts?.Max(m => m.YawDegrees) ?? gun.Definition.TraverseDegrees;
        float rTraverse = traverse / 180f * SphereRadius;
        DrawCircle(c, SphereRadius, new Color(0, 0, 0, 0.25f));
        DrawCircle(c, rTraverse, new Color(Good, 0.16f));

        bool[,] blind = BlindZone(me);
        float cellPx = SphereCellDegrees / 180f * SphereRadius;
        float step = Mathf.Tau / SphereAzimuthCells;
        var shade = new Color(Hostile, 0.45f);
        for (int i = 0; i < blind.GetLength(0); i++)
        {
            // 방위 한 줄에서 이어진 가림 칸을 부채꼴 조각 하나로 칠한다(겹침 줄무늬 없이).
            var (a0, a1) = (Vector2.FromAngle(i * step), Vector2.FromAngle((i + 1) * step));
            for (int j = 0; j < blind.GetLength(1); j++)
            {
                if (!blind[i, j]) continue;
                int start = j;
                while (j + 1 < blind.GetLength(1) && blind[i, j + 1]) j++;
                float r0 = start * cellPx, r1 = Mathf.Min((j + 1) * cellPx, rTraverse);
                Vector2[] piece = r0 < 0.5f
                    ? new[] { c, c + a0 * r1, c + a1 * r1 }
                    : new[] { c + a0 * r0, c + a0 * r1, c + a1 * r1, c + a1 * r0 };
                DrawColoredPolygon(piece, shade);
            }
        }

        DrawArc(c, rTraverse, 0, Mathf.Tau, 40, new Color(Good, 0.6f), 1f);
        DrawArc(c, SphereRadius * 0.5f, 0, Mathf.Tau, 32, Faint, 1f); // 90°: 옆
        DrawArc(c, SphereRadius, 0, Mathf.Tau, 48, Dim, 1f);           // 180°: 뒤
        DrawLine(c + new Vector2(-4, 0), c + new Vector2(4, 0), Dim, 1f);
        DrawLine(c + new Vector2(0, -4), c + new Vector2(0, 4), Dim, 1f);
        // 함선 위쪽 눈금
        Vector2 top = c + new Vector2(0, -SphereRadius);
        DrawColoredPolygon(new[] { top + new Vector2(-5, -7), top + new Vector2(5, -7), top + new Vector2(0, -1) }, Dim);

        if (!_hasEngagedLine) return;
        Vector2 p = c + SpherePoint(_engagedLocal, SphereRadius);
        if (_engagedLine == FireFailure.None)
        {
            DrawCircle(p, 4f, Lead);
            DrawArc(p, 7f, 0, Mathf.Tau, 16, Lead, 1.5f);
        }
        else
        {
            DrawLine(p + new Vector2(-5, -5), p + new Vector2(5, 5), Hostile, 2.5f);
            DrawLine(p + new Vector2(-5, 5), p + new Vector2(5, -5), Hostile, 2.5f);
        }
    }

    /// <summary>방위구 격자 칸마다 그 방향이 자함 선체에 가리는지(포각 안만).</summary>
    private bool[,] BlindZone(ShipBody me)
    {
        // Module losses can change the available hemisphere; refresh the small grid periodically.
        ulong now = Time.GetTicksMsec();
        if (now >= _blindRefresh) { _blindZones.Clear(); _blindRefresh = now + 300; }
        if (_blindZones.TryGetValue(me, out var cached)) return cached;
        float traverse = me.Definition.Railgun!.Mounts?.Max(m => m.YawDegrees) ?? me.Railgun!.Definition.TraverseDegrees;
        int rings = Mathf.CeilToInt(traverse / SphereCellDegrees);
        var blind = new bool[SphereAzimuthCells, rings];
        for (int i = 0; i < SphereAzimuthCells; i++)
        {
            float azimuth = (i + 0.5f) * Mathf.Tau / SphereAzimuthCells;
            for (int j = 0; j < rings; j++)
            {
                float polar = Mathf.DegToRad(Mathf.Min((j + 0.5f) * SphereCellDegrees, traverse - 0.1f));
                // SpherePoint의 역: 화면 (cos, sin) 방향 → 로컬 (x, -y).
                var local = new Vector3(Mathf.Sin(polar) * Mathf.Cos(azimuth), -Mathf.Sin(polar) * Mathf.Sin(azimuth), -Mathf.Cos(polar));
                blind[i, j] = SimWorld.RailLineLocal(me, local) != FireFailure.None;
            }
        }
        return _blindZones[me] = blind;
    }
    private ulong _blindRefresh;
}
