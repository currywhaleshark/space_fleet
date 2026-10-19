using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>
/// 전투 HUD. 수치는 가능한 한 도형·게이지로 보이고, 글자는 호출부호·거리·피해 리포트·짧은 경고에만 쓴다.
/// 배치: 중앙 조준선(탄약·재장전 호), 하단 중앙 계기판, 하단 왼쪽 자함 계통, 오른쪽 위 표적 도면.
/// </summary>
public partial class Hud : Control
{
    private static readonly Color Text = new(0.82f, 0.9f, 1f, 0.92f);
    private static readonly Color Dim = new(0.82f, 0.9f, 1f, 0.5f);
    private static readonly Color Faint = new(0.82f, 0.9f, 1f, 0.16f);
    private static readonly Color Friendly = new(0.45f, 0.75f, 1f, 0.85f);
    private static readonly Color Hostile = new(1f, 0.42f, 0.32f, 0.9f);
    private static readonly Color Motion = new(1f, 0.78f, 0.35f, 0.9f);
    private static readonly Color Good = new(0.35f, 0.9f, 0.7f, 0.9f);
    private static readonly Color PanelBack = new(0.01f, 0.02f, 0.03f, 0.88f);

    private Font _font = null!;
    private readonly MarkerLabels _markerLabels = new();
    private ShipView? _hoveredShip;

    public ScaleTest Game { get; set; } = null!;
    public List<(Rect2 Rect, ShipView View, ModuleState Module)> ModuleRegions { get; } = new();
    public List<(Vector2 Position, ShipView View)> BracketPositions { get; } = new();

    public override void _Ready()
    {
        _font = new SystemFont { FontNames = new[] { "Malgun Gothic", "Segoe UI" } };
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public override void _Process(double delta)
    {
        _frameDelta = (float)delta;
        QueueRedraw();
    }

    private float _frameDelta;

    /// <summary>표시값을 목표로 시상수 tau(초)만큼 부드럽게 따라가게 한다. 판정에는 쓰지 않는다.</summary>
    private float Smooth(float shown, float target, float tau = 0.3f) =>
        Mathf.Lerp(shown, target, 1f - Mathf.Exp(-_frameDelta / tau));

    public override void _Draw()
    {
        long perf = PerfTrace.Begin();
        DrawHudFrame();
        PerfTrace.End("hudDraw", perf);
    }

    private void DrawHudFrame()
    {
        ModuleRegions.Clear();
        BracketPositions.Clear();
        AntimatterBounds=InterceptorStatusBounds=new();
        DrawnLeadSolution=null;
        if (Game?.Controlled is not ShipView controlled)
            return;

        Camera3D cam = Game.Camera;
        Vector2 size = GetViewportRect().Size;
        BeginMarkerLabels(size,controlled);
        if(Game.Spectating)
        {DrawBrackets(cam,controlled,size);_markerLabels.Draw(this,_font);DrawBattleHud(size);DrawMerits(size);DrawRadar(Game.SpectatedView??controlled,size);DrawHelp(controlled,size);return;}

        UpdateEngagedLine(controlled);
        DrawBrackets(cam, controlled, size);
        if (Game.ShowModules && Game.InspectTarget is ShipView target && Game.ContactOf(target)?.SignalLost != true)
            DrawModuleVolumes(cam, target);

        // 기수가 실제로 향하는 곳(먼 점을 투영해 시차를 줄인다)
        Vector3 nosePoint = controlled.Position + controlled.Body.Forward * 100_000f;
        if (!cam.IsPositionBehind(nosePoint))
            DrawArc(cam.UnprojectPosition(nosePoint), 9f, 0, Mathf.Tau, 24, Friendly, 1.5f);

        DrawMotionCues(cam, controlled, size);
        DrawSquadOrders(cam, controlled);
        DrawAimPart(cam, controlled);
        DrawMissileMarkers(cam, controlled, size);
        DrawSelectedTarget(cam, size);
        _markerLabels.Draw(this,_font);
        DrawOffscreenShips(cam, controlled, size);
        DrawOffscreenExplosions(size);
        if (Game.Camera.TelescopeHeld) DrawTelescope(controlled,size);
        if (Game.Scheme == ControlScheme.Pilot)
        {
            DrawCrosshair(cam, controlled, size);
            DrawAntimatter(controlled.Body, size);
            DrawOrdnanceArcs(controlled, Game.Camera.AimScreenPosition());
        }
        else DrawGunnery(cam, controlled, size);
        DrawSmallTarget(cam);
        DrawInstruments(controlled, size);
        DrawWeaponSelection(controlled.Body, size);
        DrawOwnSystems(controlled, size);
        DrawSquadrons(controlled, size);
        DrawPowerPanel(controlled, size);
        DrawDrones(cam, controlled, size);
        DrawTargetPanel(size);
        DrawShotFeedback(cam);
        DrawIncomingFeedback(size);
        DrawMerits(size);
        DrawBattleHud(size);
        DrawTerrainWarning(controlled,size);
        DrawRadar(controlled, size);
        DrawHelp(controlled, size);
    }

    private Vector2 MarkerPointer => Input.MouseMode==Input.MouseModeEnum.Captured
        ? Game.Camera.AimScreenPosition() : GetViewport().GetMousePosition();
    private int MarkerPriority(ShipView view,Vector2 point) => view==Game.InspectTarget||view==Game.SelectedFriendly?100
        :view==_hoveredShip?90
        :Game.OwnsShip(view.Body)?60:10;

    private void BeginMarkerLabels(Vector2 screen,ShipView controlled)
    {
        _markerLabels.TextScale = HudSettings.TextScale;
        _markerLabels.Begin(screen);
        float radarScale=Mathf.Clamp(Mathf.Min(screen.X/1600,screen.Y/900),.6f,1);
        _markerLabels.Reserve(new Rect2(16,16,248*radarScale,248*radarScale));
        if(MeritBounds.HasArea())_markerLabels.Reserve(MeritBounds);
        if(MeritNoticeBounds.HasArea())_markerLabels.Reserve(MeritNoticeBounds);
        if(Game.Spectating)return;
        var layout=CombatHudLayout.For(screen,Game.Scheme==ControlScheme.Helm,controlled.Body.Railguns.Length, HudSettings.TextScale);
        if(Game.Feedback.Outgoing is not null)
        {
            Vector2 at=layout.HitFeedback(screen*.5f);
            _markerLabels.Reserve(new Rect2(at-new Vector2(140,20),new Vector2(280,36)));
        }
        _markerLabels.Reserve(layout.Instruments);
        if(Game.Scheme==ControlScheme.Helm)_markerLabels.Reserve(layout.Gunnery);
        _markerLabels.Reserve(new Rect2(layout.Weapons,new Vector2(390,31)));
        _markerLabels.Reserve(layout.Selection);
        _markerLabels.Reserve(new Rect2(16,screen.Y-310,250,278));
        _markerLabels.Reserve(new Rect2(screen.X-266,screen.Y-254,250,222));
        if(Game.SelectedSmallContact is { } small)
        {
            Vector3 render=(small.Predict(Game.World.Time)-Game.RenderOrigin).ToVector3();
            if(!Game.Camera.IsPositionBehind(render))
            {
                Vector2 point=Game.Camera.UnprojectPosition(render);
                _markerLabels.Reserve(new Rect2(point-new Vector2(160,42),new Vector2(320,104)));
            }
        }
        if(Game.InspectTarget is { } target)
        {
            bool detailed=Game.TrackOf(target).Level>=TrackLevel.Identified;
            _markerLabels.Reserve(new Rect2(screen.X-332,8,320,detailed?390:64));
        }
    }

    // ── 공용 도형 ─────────────────────────────────────────────

    /// <summary>계통 상태 색: 정상 파랑 → 저하 주황 → 위험·정지 빨강.</summary>
    private static Color Health(float fraction) =>
        fraction >= 0.75f ? Friendly : fraction >= 0.35f ? Motion : Hostile;

    /// <summary>가로 막대. 배경 위에 비율만큼 채운다.</summary>
    private void HBar(Rect2 r, float fraction, Color fill)
    {
        DrawRect(r, Faint);
        DrawRect(new Rect2(r.Position, new Vector2(r.Size.X * Mathf.Clamp(fraction, 0f, 1f), r.Size.Y)), fill);
    }

    /// <summary>세로 막대. 아래에서 위로 채운다.</summary>
    private void VBar(Rect2 r, float fraction, Color fill)
    {
        DrawRect(r, Faint);
        float h = r.Size.Y * Mathf.Clamp(fraction, 0f, 1f);
        DrawRect(new Rect2(r.Position.X, r.End.Y - h, r.Size.X, h), fill);
    }

    /// <summary>호 게이지. start에서 sweep 방향으로 fraction만큼 채운다(라디안, 화면 기준 시계 방향 +).</summary>
    private void ArcGauge(Vector2 center, float radius, float start, float sweep, float fraction, Color fill, float width)
    {
        DrawArc(center, radius, start, start + sweep, 32, Faint, width);
        float f = Mathf.Clamp(fraction, 0f, 1f);
        if (f > 0.001f)
            DrawArc(center, radius, start, start + sweep * f, Math.Max(3, (int)(32 * f)), fill, width);
    }

    private bool _unscaledText;
    private int HudFont(int size) => _unscaledText ? size : HudSettings.FontSize(size);
    private Color Readable(Color color) => !_unscaledText && color == Dim ? new Color(Dim, .78f) : color;
    private void Label(Vector2 at, string text, int size, Color color, HorizontalAlignment align = HorizontalAlignment.Left, float width = -1)
        => DrawString(_font, at, HudText.Fit(_font,text,HudFont(size),width), align, width, HudFont(size), Readable(color));

    private void CenteredLabel(Vector2 center, string text, int size, Color color)
    {
        size = HudFont(size);
        Vector2 s = _font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        DrawString(_font, center + new Vector2(-s.X * 0.5f, s.Y * 0.3f), text, HorizontalAlignment.Left, -1, size, Readable(color));
    }

    /// <summary>함종 표식: 전함 ●●●, 호위함 ●●, 요격함 ●.</summary>
    private void ClassPips(Vector2 at, HullKind kind, Color color)
    {
        int count = kind switch { HullKind.Battleship => 3, HullKind.Escort => 2, _ => 1 };
        for (int i = 0; i < count; i++)
            DrawCircle(at + new Vector2(i * 6f, 0), 2f, color);
    }

    // ── 표적 브래킷 ────────────────────────────────────────────

    private void DrawBrackets(Camera3D cam, ShipView controlled, Vector2 screen)
    {
        float pxPerRad = screen.Y * 0.5f / Mathf.Tan(Mathf.DegToRad(cam.Fov) * 0.5f);
        _hoveredShip=null;
        float hoverDistance=24*24;
        var lostContacts=new List<(ShipView View,ContactSnapshot Contact)>();
        void Hover(ShipView view,Vector3 position)
        {
            if(cam.IsPositionBehind(position))return;
            float distance=cam.UnprojectPosition(position).DistanceSquaredTo(MarkerPointer);
            if(distance<hoverDistance){_hoveredShip=view;hoverDistance=distance;}
        }
        // 적은 진영 센서망이 아는 위치(추정)에 그린다. 아군은 데이터 링크로 실제 위치.
        var markers = new List<(ShipView View, SensorTrack Track, Vector3 Render, double Dist, double TrueDist)>();
        foreach (ShipView view in Game.Views)
        {
            if (view == controlled)
                continue;
            if (Game.ContactOf(view) is { SignalLost: true } lost)
            {
                lostContacts.Add((view,lost));
                Hover(view,(lost.Position-Game.RenderOrigin).ToVector3());
                continue;
            }
            SensorTrack track = Game.TrackOf(view);
            if (track.Level == TrackLevel.None)
                continue; // 탐지되지 않은 적은 그리지 않는다.
            // 적은 매 프레임 보간된 실제 이동 + 천천히 흐르는 추정 오차 위치에 그린다(4Hz 갱신마다 튀지 않게).
            bool enemy = view.Body.Faction != controlled.Body.Faction;
            Vec3d sim = enemy ? view.SimPosition + track.Offset : view.SimPosition;
            Vector3 render = (sim - Game.RenderOrigin).ToVector3();
            Hover(view,render);
            if (!cam.IsPositionBehind(render))
                markers.Add((view, track, render, (sim - controlled.SimPosition).Length(),
                    (view.SimPosition - controlled.SimPosition).Length()));
        }
        foreach(var (view,lost) in lostContacts)DrawLostContact(cam,controlled,view,lost,screen);
        // 가까운 순서는 안정적으로 유지하고, 글자는 선택/호버/내 편대 순으로 별도 배치한다.
        // 순서는 실제 거리로 고정해서, 겹친 접촉의 라벨이 오차 변화에 따라 번갈아 깜박이지 않게 한다.
        var contacts = new List<Contact>();
        foreach (var (view, track, render, dist, _) in markers.OrderBy(m => m.TrueDist))
        {
            Vector2 p = cam.UnprojectPosition(render);
            bool selected = view == Game.InspectTarget || view == Game.SelectedFriendly;

            if (track.Level == TrackLevel.Contact)
            {
                // 접촉은 모아 두었다가 묶음으로 그린다. 오차 원 크기는 부드럽게 따라간다.
                float errTarget = Mathf.Clamp(track.ErrorMeters / (float)Math.Max(dist, 1.0) * pxPerRad, 6f, 120f);
                float err = _errorShown[view] = _errorShown.TryGetValue(view, out float shown) ? Smooth(shown, errTarget, 0.5f) : errTarget;
                contacts.Add(new Contact(view, p, err, track, dist));
                DrawTargetFeedback(view.Body, p, err);
                BracketPositions.Add((p, view));
                continue;
            }

            float radius = (float)(view.Body.Class.Length * 0.5 / Math.Max(dist, 1.0)) * pxPerRad;
            if (radius > screen.Y * 0.35f && !selected)
                continue; // 가까워서 화면을 덮는 함선은 표시하지 않는다.
            if (selected) radius = Mathf.Min(radius, 65);
            BracketPositions.Add((p, view));
            _markerLabels.ProtectSymbol(p,10);

            bool destroyed = view.Body.Damage.Destroyed;
            // 무력화는 식별 이상에서만 보인다(적이면 겉보기 정보).
            bool disabled = view.Body.Damage.Disabled && track.Level >= TrackLevel.Identified;
            float h = Mathf.Max(9f, radius);
            DrawTargetFeedback(view.Body, p, h);
            float arm = Mathf.Min(8f, h * 0.6f);
            Color c = destroyed ? Dim : Game.ShipMarkerColor(view.Body);
            if (disabled) c = new Color(c, 0.45f);
            float width = selected ? 2.5f : 1.5f;
            foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            {
                var corner = p + new Vector2(sx * h, sy * h);
                DrawLine(corner, corner - new Vector2(sx * arm, 0), c, width);
                DrawLine(corner, corner - new Vector2(0, sy * arm), c, width);
            }
            if (destroyed)
            {
                DrawLine(p + new Vector2(-h, -h) * 0.6f, p + new Vector2(h, h) * 0.6f, c, 1.5f);
                DrawLine(p + new Vector2(-h, h) * 0.6f, p + new Vector2(h, -h) * 0.6f, c, 1.5f);
            }
            else if (disabled)
                DrawLine(p + new Vector2(-h * 0.7f, 0), p + new Vector2(h * 0.7f, 0), c, 2f); // 무력화: 가로줄
            else if (view.Body.Faction != controlled.Body.Faction && track.Level == TrackLevel.Locked)
                DrawDiamond(p, Mathf.Max(5f, h * 0.55f), c); // 사격통제 잠금
            if (track.Jammed)
                DrawJamMark(p + new Vector2(-h - 12f, -h + 2f), c);
            ClassPips(p + new Vector2(-h, h + 6f), view.Body.Class.Kind, c);

            string label = $"{view.Body.Callsign}  {FormatDistance(dist)}{(disabled ? "  무력화" : "")}";
            Vector2 at = p + new Vector2(h + 6, -h + 12);
            if (Game.Spectating || view != Game.InspectTarget || Game.SelectedTargetDisplay() is null)
                _markerLabels.Add(view,p,at-p,label,c,MarkerPriority(view,p));
        }
        DrawContactGroups(contacts);
    }

    private readonly Dictionary<ShipView, float> _errorShown = new();

    private readonly record struct Contact(ShipView View, Vector2 P, float Err, SensorTrack Track, double Dist);

    /// <summary>접촉이 속한 묶음의 크기(표적 패널용). 이번 프레임 DrawBrackets에서 채운다.</summary>
    private readonly Dictionary<ShipView, int> _contactGroupSize = new();
    /// <summary>지난 프레임에 같은 묶음이었던 첫 구성원. 경계에서 묶였다 풀렸다 하지 않게 한다.</summary>
    private Dictionary<ShipView, ShipView> _contactAnchor = new();

    /// <summary>
    /// 화면에서 가깝거나 오차 원이 크게 겹치는 접촉을 하나로 묶는다. 묶음은 구성원 평균 위치의 마름모,
    /// 모든 구성원 오차 원을 감싸는 원 하나, "? ×수"와 가장 가까운 구성원 거리로 그린다.
    /// 지난 프레임에 같이 묶였던 접촉은 조금 더 멀어져도 묶음을 유지한다.
    /// </summary>
    private void DrawContactGroups(List<Contact> contacts)
    {
        const float MergePixels = 36f;
        const float OverlapFactor = 0.6f;
        const float KeepFactor = 1.25f;
        var groups = new List<List<Contact>>();
        foreach (Contact c in contacts) // 실제 거리 순으로 들어와 묶음의 첫 구성원이 안정적이다.
        {
            if (c.View == Game.InspectTarget) { groups.Add(new List<Contact> { c }); continue; }
            List<Contact>? home = null;
            foreach (List<Contact> g in groups)
            {
                if (g[0].View == Game.InspectTarget) continue;
                Vector2 center = GroupCenter(g);
                float reach = Mathf.Max(MergePixels, (GroupRadius(g, center) + c.Err) * OverlapFactor);
                if (_contactAnchor.TryGetValue(c.View, out ShipView? anchor) && anchor == g[0].View)
                    reach *= KeepFactor;
                if (center.DistanceTo(c.P) <= reach)
                {
                    home = g;
                    break;
                }
            }
            if (home is null)
                groups.Add(new List<Contact> { c });
            else
                home.Add(c);
        }

        _contactGroupSize.Clear();
        var anchors = new Dictionary<ShipView, ShipView>();
        Color cc = new(Hostile, 0.7f);
        foreach (List<Contact> g in groups)
        {
            Vector2 center = GroupCenter(g);
            _markerLabels.ProtectSymbol(center,9);
            float radius = GroupRadius(g, center);
            bool selected = g.Any(m => m.View == Game.InspectTarget);
            foreach (Contact m in g)
            {
                _contactGroupSize[m.View] = g.Count;
                anchors[m.View] = g[0].View;
            }

            // 여러 척이면 겹친 마름모 두 개로 "여럿"임을 보인다.
            if (g.Count > 1)
                DrawDiamond(center + new Vector2(3f, -3f), 6f, new Color(cc, 0.45f));
            DrawDiamond(center, 6f, cc);
            DrawArc(center, radius, 0, Mathf.Tau, 40, new Color(Hostile, selected ? 0.6f : 0.3f), selected ? 2f : 1f);
            if (g.Any(m => m.Track.Jammed))
                DrawJamMark(center + new Vector2(-12f, -16f), cc);

            double nearest = g.Min(m => m.Dist);
            string text = g.Count > 1 ? $"? ×{g.Count}  {FormatDistance(nearest)}" : $"?  {FormatDistance(nearest)}";
            Vector2 at = center + new Vector2(12f, -6f);
            int priority=g.Max(c=>MarkerPriority(c.View,c.P));
            if (!selected || Game.Spectating) _markerLabels.Add(g[0].View,center,at-center,text,cc,priority);
        }
        _contactAnchor = anchors;

        static Vector2 GroupCenter(List<Contact> g)
        {
            Vector2 sum = Vector2.Zero;
            foreach (Contact m in g) sum += m.P;
            return sum / g.Count;
        }
        static float GroupRadius(List<Contact> g, Vector2 center)
        {
            float r = 0f;
            foreach (Contact m in g) r = Mathf.Max(r, center.DistanceTo(m.P) + m.Err);
            return r;
        }
    }

    /// <summary>방해 표시: 짧은 물결 두 줄. 이 표적은 ECM을 켜고 있어 정밀도가 떨어진다.</summary>
    private void DrawJamMark(Vector2 at, Color color)
    {
        for (int row = 0; row < 2; row++)
        {
            Vector2 o = at + new Vector2(0, row * 4f);
            DrawPolyline(new[] { o, o + new Vector2(2.5f, -2f), o + new Vector2(5f, 0), o + new Vector2(7.5f, -2f), o + new Vector2(10f, 0) }, color, 1.2f);
        }
    }

    // ── 자함 계통(하단 왼쪽) ───────────────────────────────────

    private void DrawOwnSystems(ShipView controlled, Vector2 screen)
    {
        ShipBody body = controlled.Body;
        var panel = new Rect2(16, screen.Y - 218, 250, 186);
        bool collided = body.LastCollision is CollisionImpact impact && Game.World.Time - impact.Time < 2.0;
        DrawRect(panel, PanelBack);
        if (collided)
            DrawRect(panel, Hostile, false, 2f);

        Label(panel.Position + new Vector2(10, 18), body.Callsign, 14, Game.ShipMarkerColor(body));
        ClassPips(panel.Position + new Vector2(_font.GetStringSize(body.Callsign, HorizontalAlignment.Left, -1, HudFont(14)).X + 18, 13), body.Class.Kind, Game.ShipMarkerColor(body));
        Label(panel.Position + new Vector2(140, 18), $"구조 {body.Damage.StructureFraction * 100:0}%", 12,
            body.Damage.StructureFraction <= .25f ? Hostile : body.Damage.StructureFraction <= .5f ? Motion : Dim,
            HorizontalAlignment.Right, 100);
        DrawShieldBar(new Rect2(panel.Position + new Vector2(10, 28), new Vector2(panel.Size.X - 20, 8)), body);
        DrawSystemBars(new Rect2(panel.Position + new Vector2(10, 44), new Vector2(panel.Size.X - 20, 66)), body.Damage);
        var dc = body.DamageControl;
        string state = body.Damage.Destroyed ? "복구 불가" : dc.Priority == RepairPriority.Hold ? "작업 중지"
            : dc.Kits == 0 ? "자재 소진" : dc.ActiveModule is { } module ? ModuleNames.Display(module.Definition) : $"대기 · {dc.RestoreFraction*100:0}% 응급복구";
        float textGap = Mathf.Round((HudSettings.TextScale-1)*25);
        Label(panel.Position + new Vector2(10, 124+textGap), $"5 복구 · {DamageControlState.Label(dc.Priority)} · {dc.Kits}/{dc.Capacity}", 13, Text, width:230);
        Label(panel.Position + new Vector2(10, 145+textGap), state, 13, dc.ActiveModule is null ? Dim : Motion, width:230);
        string progress = dc.ActiveModule is null ? ""
            : dc.QuietRemaining > .01 ? $"피격 중단 · {dc.QuietRemaining:0.0}초 후 재개" : $"작업 {dc.Progress * 100:0}% · {dc.WorkRemaining:0.0}초";
        Label(panel.Position + new Vector2(10, 166+textGap), progress, 12, dc.QuietRemaining > .01 ? Hostile : Good, width:230);
        HBar(new Rect2(panel.Position + new Vector2(10, 173+textGap), new Vector2(230, 4)), dc.Progress, Good);

        if (collided && body.LastCollision is CollisionImpact hit)
            Label(panel.Position + new Vector2(0, -8), $"충돌 {hit.OtherCallsign} · Δv {hit.DeltaSpeed:0}", 13, Hostile);
    }

    /// <summary>
    /// 실드 막대(설계 용량 기준). 발생기·전력 손상으로 줄어든 최대치는 오른쪽 끝의 빨간 구간으로 보인다.
    /// 최근 피격 시 밝게 번쩍인다.
    /// </summary>
    private void DrawShieldBar(Rect2 r, ShipBody body)
    {
        ShipDamage damage = body.Damage;
        float design = Mathf.Max(1f, body.Definition.Shield.Capacity);
        float flash = Mathf.Clamp(1f - (float)(Game.World.Time - damage.LastShieldHitTime) / 0.4f, 0f, 1f);
        HBar(r, damage.Shield / design, Friendly.Lerp(Colors.White, flash * 0.8f));
        float lost = 1f - damage.ShieldCapacity / design;
        if (lost > 0.005f)
            DrawRect(new Rect2(r.End.X - r.Size.X * lost, r.Position.Y, r.Size.X * lost, r.Size.Y), new Color(Hostile, 0.45f));
    }

    private static readonly (string Label, Func<ShipDamage, float> Value)[] Systems =
    {
        ("좌전", d => d.GridPower(PowerGrid.Port)),
        ("우전", d => d.GridPower(PowerGrid.Starboard)),
        ("추진", d => d.PropulsionFraction),
        ("자세", d => d.ManeuverFraction),
        ("무장", d => d.WeaponsFraction),
        ("센서", d => d.SensorFraction),
        ("냉각", d => d.CoolingFraction),
    };

    /// <summary>계통별 세로 막대 7개. 이름은 두 글자로 줄인다.</summary>
    private void DrawSystemBars(Rect2 area, ShipDamage damage)
    {
        float slot = area.Size.X / Systems.Length;
        float barHeight = area.Size.Y - 16f;
        for (int i = 0; i < Systems.Length; i++)
        {
            float value = Systems[i].Value(damage);
            float x = area.Position.X + slot * i + slot * 0.5f;
            var bar = new Rect2(x - 6f, area.Position.Y, 12f, barHeight);
            VBar(bar, value, Health(value));
            if (value <= 0.001f)
            {
                DrawLine(bar.Position, bar.End, Hostile, 1.5f);
                DrawLine(new Vector2(bar.Position.X, bar.End.Y), new Vector2(bar.End.X, bar.Position.Y), Hostile, 1.5f);
            }
            CenteredLabel(new Vector2(x, area.End.Y - 4f), Systems[i].Label, 11, value < 0.75f ? Health(value) : Dim);
        }
    }

    // ── 도움말·디버그(F1) ───────────────────────────────────────

    private static string FormatDistance(double meters) =>
        meters >= 1000 ? $"{meters / 1000:#,0.0} km" : $"{meters:0} m";
}
