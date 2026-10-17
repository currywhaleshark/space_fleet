using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>
/// 조함 방식 사격 패널(하단, 계기판 오른쪽 — 내 함선 모델을 가리지 않는다).
/// 왼쪽 고리 = 교리(가운데) · 탄약(왼쪽) · 재장전(오른쪽) · 미사일(아래) · 디코이(위).
/// 가운데 = 교전 표적 호출부호 · 거리 · 사격 상태/불가 사유. 좁은 창에서도 방위구와 함께 유지.
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
        var battery = controlled.Body.Railguns;
        Rect2 panel = CombatHudLayout.For(screen, true, battery.Length).Gunnery;
        DrawRect(panel, PanelBack);
        float x = panel.Position.X, cy = panel.Position.Y + 53;
        var ring = new Vector2(x + 46, cy);
        CenteredLabel(ring, GunneryLabels.Doctrine(order.Doctrine), 14, order.Doctrine == FireDoctrine.Hold ? Motion : Text);
        float ammo = gun.IsLaser ? 1-Mathf.Clamp(controlled.Body.Power.HeatFraction,0,1)
            : battery.Sum(g => g.Rounds) / (float)System.Math.Max(1,battery.Sum(g => g.Definition.Rounds));
        ArcGauge(ring, 34, Mathf.DegToRad(145), Mathf.DegToRad(70), ammo, Friendly, 4);
        float reload = battery.Average(g => g.Output <= .01f || controlled.Body.Power.Overheated ? 0
            : g.Laser is { } laser ? laser.FocusFraction : 1-g.ReloadRemaining/g.Definition.ReloadSeconds);
        ArcGauge(ring, 34, Mathf.DegToRad(35), Mathf.DegToRad(-70), reload,
            battery.Any(g => g.Ready && g.Laser?.Cooling != true) ? Good : Motion, 4);
        DrawOrdnanceArcs(controlled, ring, 34);

        float mx = x + (panel.Size.X - 100) * .5f + 40;
        if (Game.DisplayLeadSolution is { Valid: true, ViaDatalink: true })
            CenteredLabel(new(mx, cy-39), "아군 링크", 12, Friendly);
        ContactSnapshot? lost = Game.FireTarget is null && Game.InspectTarget is { } inspect
            && Game.ContactOf(inspect) is { SignalLost: true } memory ? memory : null;
        if (Game.SelectedSmallContact is { } small)
        {
            CenteredLabel(new(mx,cy-19), small.Kind==SmallContactKind.Missile?"지정 미사일":"지정 프로브",14,Hostile);
            CenteredLabel(new(mx,cy+1),FormatDistance((small.Predict(Game.World.Time)-controlled.SimPosition).Length()),13,Text);
        }
        else if (Game.FireTarget is ShipView target)
        {
            CenteredLabel(new(mx,cy-19),"주포 · "+(Game.ContactOf(target)?.Name??"미식별 접촉"),13,Hostile);
            SensorTrack track=Game.TrackOf(target);
            CenteredLabel(new(mx,cy+1),FormatDistance((target.SimPosition+track.Offset-controlled.SimPosition).Length()),13,Text);
        }
        else if(lost is not null)
        {
            CenteredLabel(new(mx,cy-19),lost.Name,14,ContactMemory.LostColor);
            CenteredLabel(new(mx,cy+1),FormatDistance((lost.Position-controlled.SimPosition).Length()),13,ContactMemory.LostColor);
        }
        string reason=lost is not null?"신호 소실":FiringStatus(controlled.Body,order.Status);
        CenteredLabel(new(mx,cy+25),reason,13,lost is not null?ContactMemory.LostColor
            :order.Status==GunneryStatus.Firing?Good:order.Status==GunneryStatus.Obstructed?EnvironmentSymbols.Terrain:Motion);
        int columns=System.Math.Min(4,battery.Length);
        float cell=(panel.Size.X-12)/columns;
        for(int i=0;i<battery.Length;i++)
        {
            RailgunState mount=battery[i];
            string state=MountStatus(mount,controlled.Body.Power);
            CenteredLabel(new(x+6+cell*(i%columns+.5f),panel.Position.Y+111+(i/columns)*18),$"{i+1} {state}",12,
                mount.Output<=.01f||!mount.HasAmmunition?Hostile:state is "준비" or "조사"?Good:Motion);
        }
        DrawFiringSphere(new(panel.End.X-SphereRadius-10,cy),controlled.Body);

        DrawLeadMarker(cam, screen);
        DrawHitMarker(cam, controlled, screen * 0.5f);
    }

    internal static string MountStatus(RailgunState gun,ShipPower power) => gun.Output<=.01f?"파손"
        :!gun.HasAmmunition?"소진":power.Overheated||gun.Laser is {Active:false,Cooling:true}?"냉각"
        :gun.Laser?.Active==true?"조사":gun.Ready?"준비":gun.IsLaser?"조사 대기":"장전";

    internal static string FiringStatus(ShipBody ship,GunneryStatus status) =>
        status==GunneryStatus.Reload && ship.Railgun?.IsLaser==true
            ?ship.Power.Overheated||ship.Railguns.Any(g=>g.Laser?.Cooling==true)?"냉각 중":"조사 대기"
            :GunneryLabels.Status(status);

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
        if (Game.Gunnery?.Status==GunneryStatus.Obstructed) EnvironmentSymbols.Blocked(this,p);
        else if (_engagedLine == FireFailure.None)
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
