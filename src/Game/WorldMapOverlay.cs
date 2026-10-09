using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>실시간 전장 지도. 배경 전투와 입력을 분리하고 전장 좌표를 자유롭게 탐색한다.</summary>
public partial class WorldMapOverlay : Control
{
    public ScaleTest Game { get; set; } = null!;
    public WorldMapState Map { get; } = new();
    public event Action? CloseRequested;
    private Font _font = null!;
    private WorldMapSurface _surface = null!;
    private ShipView? _selected;
    private MouseButton _drag;
    private Vector2 _press;
    private bool _moved;
    private Rect2 _closeRect, _fitRect;
    private readonly List<(Vector2 Point, ShipView Ship)> _picks = new();
    private static readonly Color Blue = new(.42f, .78f, 1), Red = new(1, .47f, .34f), Amber = new(1, .77f, .38f), Ink = new(.82f, .91f, 1);
    private sealed record Contact(ShipView Ship, ContactSnapshot Memory, bool Enemy)
    {
        public Vec3d Position => Memory.Position;
        public SensorTrack Track => Memory.Track;
        public bool Identified => Memory.Identified;
        public bool Lost => Memory.SignalLost;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _font = new SystemFont { FontNames = new[] { "Malgun Gothic", "Segoe UI" } };
        _surface = new WorldMapSurface { OwnerMap = this, ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_surface); Layout();
    }

    private List<Contact> Contacts()
    {
        var result = new List<Contact>();
        Faction side = Game.Controlled?.Body.Faction ?? Faction.Blue;
        foreach (ShipView view in Game.Views)
        {
            if (Game.ContactOf(view) is not { } memory) continue;
            bool enemy = view.Body.Faction != side;
            result.Add(new(view, memory, enemy));
        }
        return result;
    }

    public void Open()
    {
        if (!Map.Initialized) FitAll();
        _drag = MouseButton.None; _moved = false; Visible = true; Layout(); QueueRedraw();
    }
    public void Close() { Visible = false; _drag = MouseButton.None; }
    public void FitAll() => Map.Fit(Contacts().Select(c => c.Position));
    private float Radius => Mathf.Min(_surface.Size.X, _surface.Size.Y) * .46f;
    private Vector2 Normalized(Vector2 screen) => (screen - _surface.Position - _surface.Size * .5f) / Radius;

    private void Layout()
    {
        Size = GetViewportRect().Size;
        float sidebar = Size.X >= 1000 ? 260 : 190;
        _surface.Position = new Vector2(24, 95);
        _surface.Size = new Vector2(Mathf.Max(200, Size.X - sidebar - 60), Mathf.Max(160, Size.Y - 165));
        _closeRect = new Rect2(Size.X - 154, 25, 130, 38);
        _fitRect = new Rect2(Size.X - 330, 25, 162, 38);
    }
    public override void _Process(double delta)
    { if (Visible) { Layout(); QueueRedraw(); _surface.QueueRedraw(); } }

    public bool HandleInput(InputEvent e)
    {
        if (!Visible || e.IsActionPressed(InputSetup.ToggleMute)) return false;
        if (e.IsActionPressed(InputSetup.WorldMap) || e.IsActionPressed(InputSetup.ReleaseMouse)) CloseRequested?.Invoke();
        else if (e is InputEventKey { Pressed: true, Echo: false } key && (key.PhysicalKeycode == Key.Home || key.Keycode == Key.Home)) FitAll();
        else if (e is InputEventMouseButton button)
        {
            if (button.Pressed && button.ButtonIndex == MouseButton.Left && _closeRect.HasPoint(button.Position)) CloseRequested?.Invoke();
            else if (button.Pressed && button.ButtonIndex == MouseButton.Left && _fitRect.HasPoint(button.Position)) FitAll();
            else if (button.Pressed && new Rect2(_surface.Position, _surface.Size).HasPoint(button.Position))
            {
                if (button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
                    Map.Zoom(button.ButtonIndex == MouseButton.WheelUp ? .8f : 1.25f, Normalized(button.Position));
                else if (button.ButtonIndex is MouseButton.Left or MouseButton.Right or MouseButton.Middle)
                { _drag = button.ButtonIndex; _press = button.Position; _moved = false; }
            }
            else if (!button.Pressed && button.ButtonIndex == _drag)
            {
                if (!_moved && _drag == MouseButton.Left)
                    _selected = _picks.AsEnumerable().Reverse().Where(p => p.Point.DistanceTo(button.Position) < 12)
                        .OrderBy(p => p.Point.DistanceSquaredTo(button.Position)).Select(p => p.Ship).FirstOrDefault();
                _drag = MouseButton.None;
            }
        }
        else if (e is InputEventMouseMotion motion && _drag != MouseButton.None)
        {
            _moved |= motion.Position.DistanceTo(_press) > 4;
            if (_moved)
            {
                if (_drag == MouseButton.Middle) Map.Pan(motion.Relative / Radius);
                else Map.Orbit(motion.Relative);
            }
        }
        GetViewport().SetInputAsHandled(); return true;
    }

    private void Text(Control canvas, Vector2 at, string text, int size = 14, Color? color = null)
        => canvas.DrawString(_font, at, text, HorizontalAlignment.Left, -1, size, color ?? Ink);

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(.008f, .017f, .03f, .985f));
        Text(this, new(28, 44), "전장 전체 지도", 24);
        Text(this, new(28, 70), "구형 전장 좌표 · 실시간", 13, Blue);
        foreach (var (rect, title) in new[] { (_fitRect, "전체 보기 · Home"), (_closeRect, "닫기 · M / Esc") })
        {
            DrawRect(rect, new Color(.035f, .085f, .13f)); DrawRect(rect, new Color(Blue, .35f), false);
            Text(this, rect.Position + new Vector2(12, 25), title, 14);
        }
        DrawLine(new(24, 84), new(Size.X - 24, 84), new Color(Blue, .2f));
        float x = _surface.Position.X + _surface.Size.X + 26;
        var contacts = Contacts();
        Text(this, new(x, 126), $"아군  {contacts.Count(c => !c.Enemy)}", 15, Blue);
        Text(this, new(x, 156), $"적 접촉 {contacts.Count(c => c.Enemy && !c.Lost)} · 소실 {contacts.Count(c => c.Lost)}", 15, Red);
        int outside = contacts.Count(c => !Map.Contains(c.Position));
        if (outside > 0) Text(this, new(x, 178), $"구 밖 {outside} · Home 전체 보기", 12, Amber);
        Text(this, new(x, 199), "함선을 클릭해 정보 확인", 13, new Color(Ink, .6f));
        Contact? selected = contacts.FirstOrDefault(c => c.Ship == _selected);
        if (selected is not null)
        {
            Color color = selected.Lost ? ContactMemory.LostColor : selected.Enemy ? Amber : Blue;
            Text(this, new(x, 244), selected.Memory.Name, 21, color);
            Text(this, new(x, 272), selected.Memory.State?.ClassName ?? "미식별", 14, color);
            if (selected.Lost) Text(this, new(x, 295), $"신호 소실 · {Game.World.Time - selected.Memory.LastSeenAt:0}초 전", 13, color);
            Text(this, new(x, 318), $"X  {selected.Position.X / 1000:0.0} km", 14, selected.Lost ? color : Ink);
            Text(this, new(x, 345), $"Y  {selected.Position.Y / 1000:0.0} km", 14, selected.Lost ? color : Ink);
            Text(this, new(x, 372), $"Z  {selected.Position.Z / 1000:0.0} km", 14, selected.Lost ? color : Ink);
            if (selected.Enemy) Text(this, new(x, 409), $"추정 오차  {selected.Track.ErrorMeters / 1000:0.00} km", 13, selected.Lost ? color : Amber);
            if (selected.Memory.State is { } state)
            {
                bool cached = selected.Lost || selected.Track.Level < TrackLevel.Identified;
                Text(this, new(x, 439), (cached ? "최종 상태 · " : "상태 · ") + state.Status, 14, color);
                Text(this, new(x, 464), $"실드 {state.Shield:P0} · 파괴 모듈 {state.LostModules}", 13, color);
                if (cached) Text(this, new(x, 489), $"상태 확인 {Game.World.Time - selected.Memory.LastIdentifiedAt:0}초 전", 12, color);
            }
        }
        else
        {
            Text(this, new(x, 256), "지도 중심", 14, Blue);
            Text(this, new(x, 286), $"X  {Map.Center.X / 1000:0.0} km", 13);
            Text(this, new(x, 312), $"Y  {Map.Center.Y / 1000:0.0} km", 13);
            Text(this, new(x, 338), $"Z  {Map.Center.Z / 1000:0.0} km", 13);
        }
        DrawLine(new(24, Size.Y - 56), new(Size.X - 24, Size.Y - 56), new Color(Blue, .2f));
        Text(this, new(28, Size.Y - 29), "좌/우드래그 회전 · 중드래그 이동 · 휠 확대 · Home 전체 보기", Size.X < 1000 ? 12 : 14);
        Text(this, new(x, Size.Y - 29), $"구 반경 {Map.HalfSpan / 1000:0.##} km", 13, Blue);
    }

    internal void DrawSpace(Control canvas)
    {
        _picks.Clear();
        Vector2 center = canvas.Size * .5f;
        Vector2 At(Vec3d p) { Vector3 v = Map.Project(p); return center + new Vector2(v.X, v.Y) * Radius; }
        DrawGlobe(canvas, center);
        var labels = new List<Rect2>();
        foreach (Contact c in Contacts().OrderBy(c => c.Ship == _selected || c.Ship == Game.Controlled ? double.MaxValue : Map.Project(c.Position).Z))
        {
            if (!Map.Contains(c.Position)) continue;
            Vector2 p = At(c.Position);
            if (!new Rect2(Vector2.Zero, canvas.Size).Grow(-12).HasPoint(p)) continue;
            bool self = c.Ship == Game.Controlled, selected = c.Ship == _selected;
            bool wreck = c.Memory.State?.Destroyed == true;
            Color color = c.Lost ? ContactMemory.LostColor : c.Enemy ? c.Identified ? Red : Amber : Blue;
            if (wreck) color = new Color(color, .35f);
            else if (!self && !selected && Map.Project(c.Position).Z < 0) color = new Color(color, .6f);
            // 구의 적도면에 내려 공간상의 높이를 읽는다. 좌표 자체는 세계 좌표다.
            Vector2 foot = At(new Vec3d(c.Position.X, Map.Center.Y, c.Position.Z));
            if (foot.DistanceTo(p) > 3)
            { canvas.DrawLine(foot, p, new Color(color, .4f), 1, true); canvas.DrawCircle(foot, 2, new Color(color, .35f)); }
            if (c.Enemy && c.Track.ErrorMeters > 0)
                canvas.DrawArc(p, Mathf.Clamp((float)(c.Track.ErrorMeters / Map.HalfSpan) * Radius, 6, 40),
                    0, Mathf.Tau, 32, new Color(color, .2f), 1, true);
            if (self)
            {
                Vector3 nose = Map.Project(c.Position + Vec3d.From(c.Ship.Body.Forward) * 1000) - Map.Project(c.Position);
                Vector2 forward = new Vector2(nose.X, nose.Y).Normalized();
                if (forward.LengthSquared() < .01f) forward = Vector2.Up;
                Vector2 side = forward.Orthogonal();
                canvas.DrawColoredPolygon(new[] { p + forward * 9, p - forward * 5 + side * 5, p - forward * 5 - side * 5 }, Colors.White);
            }
            else if (wreck)
            { canvas.DrawLine(p - new Vector2(4,4), p + new Vector2(4,4), color, 2); canvas.DrawLine(p + new Vector2(-4,4), p + new Vector2(4,-4), color, 2); }
            else if (!c.Identified) canvas.DrawArc(p, 5, 0, Mathf.Tau, 16, color, 1.5f, true);
            else if (c.Enemy) canvas.DrawPolyline(new[] { p+Vector2.Up*5, p+Vector2.Right*5, p+Vector2.Down*5, p+Vector2.Left*5, p+Vector2.Up*5 }, color, 2, true);
            else canvas.DrawCircle(p, 4, color);
            if (selected) canvas.DrawArc(p, 12, 0, Mathf.Tau, 32, c.Lost ? color : Ink, 2, true);
            string label = self ? c.Memory.Name + " · 조종함" : c.Memory.Name + (c.Lost ? " · 신호 소실" : "");
            Vector2 textSize = _font.GetStringSize(label, fontSize: 12), at = p + new Vector2(12, -8);
            var bounds = new Rect2(at - new Vector2(0, 14), textSize + new Vector2(8, 5));
            if (self || selected || !labels.Any(r => r.Intersects(bounds)))
            { Text(canvas, at, label, 12, color); labels.Add(bounds); }
            _picks.Add((p + _surface.Position, c.Ship));
        }
        // 원점과 화면 평면 축척. 조종함과 독립된 전장 공간을 읽는 기준.
        Vector2 zero = At(Vec3d.Zero);
        if (Map.Contains(Vec3d.Zero) && new Rect2(Vector2.Zero, canvas.Size).HasPoint(zero))
        { canvas.DrawArc(zero, 16, 0, Mathf.Tau, 32, new Color(Ink,.16f),1); Text(canvas, zero+new Vector2(18,20),"원점",11,new Color(Ink,.4f)); }
        float length = Mathf.Min(120, Radius * .5f);
        canvas.DrawLine(new(16,canvas.Size.Y-25), new(16+length,canvas.Size.Y-25), Blue, 2);
        Text(canvas, new(16,canvas.Size.Y-36), $"{Map.HalfSpan * length / Radius / 1000:0.0} km", 12, Blue);
    }

    private void DrawGlobe(Control canvas, Vector2 center)
    {
        Vector2 At(Vector3 unit)
        { Vector3 p = Map.ProjectUnit(unit); return center + new Vector2(p.X, p.Y) * Radius; }

        canvas.DrawCircle(center, Radius, new Color(.014f, .034f, .053f, .9f));
        canvas.DrawArc(center, Radius, 0, Mathf.Tau, 128, new Color(Blue, .4f), 1.5f, true);

        // 구 내부의 적도 원판과 반경 눈금. 평면 격자를 깔지 않고 구의 부피를 보여 준다.
        var equator = new Vector2[96];
        for (int i = 0; i < equator.Length; i++)
        {
            float t = i * Mathf.Tau / equator.Length;
            equator[i] = At(new Vector3(Mathf.Cos(t), 0, Mathf.Sin(t)));
        }
        canvas.DrawColoredPolygon(equator, new Color(Blue, .028f));
        void Ring(Func<float, Vector3> point, float strength = 1)
        {
            const int segments = 128;
            for (int i = 0; i < segments; i++)
            {
                Vector3 a = point(i * Mathf.Tau / segments), b = point((i + 1) * Mathf.Tau / segments);
                bool front = Map.ProjectUnit((a + b) * .5f).Z >= 0;
                if (!front && i % 3 == 0) continue;
                canvas.DrawLine(At(a), At(b), new Color(Blue, (front ? .24f : .07f) * strength), 1, true);
            }
        }
        foreach (float latitude in new[] { -Mathf.Pi / 3, -Mathf.Pi / 6, 0, Mathf.Pi / 6, Mathf.Pi / 3 })
            Ring(t => new Vector3(Mathf.Cos(t) * Mathf.Cos(latitude), Mathf.Sin(latitude), Mathf.Sin(t) * Mathf.Cos(latitude)), latitude == 0 ? 1.7f : 1);
        foreach (float longitude in new[] { 0, Mathf.Pi / 4, Mathf.Pi / 2, Mathf.Pi * .75f })
            Ring(t => new Vector3(Mathf.Cos(t) * Mathf.Cos(longitude), Mathf.Sin(t), Mathf.Cos(t) * Mathf.Sin(longitude)));
        Ring(t => new Vector3(Mathf.Cos(t), 0, Mathf.Sin(t)) * .5f, .75f);

        // 함선 방향과 독립된 세계 축. 지도를 돌리면 좌표망과 함께 회전한다.
        foreach (var (axis, label) in new[] { (Vector3.Right, "+X"), (Vector3.Up, "+Y"), (Vector3.Back, "+Z") })
        {
            Vector2 positive = At(axis), negative = At(-axis);
            canvas.DrawDashedLine(negative, center, new Color(Blue, .18f), 1, 6);
            canvas.DrawLine(center, positive, new Color(Blue, .35f), 1, true);
            canvas.DrawCircle(positive, 2.5f, new Color(Blue, .7f));
            Vector2 labelAt = At(axis * 1.06f);
            Text(canvas, labelAt - _font.GetStringSize(label, fontSize: 12) * new Vector2(.5f, 0) + new Vector2(0, 4), label, 12, Blue);
        }
        canvas.DrawArc(center, 4, 0, Mathf.Tau, 16, new Color(Ink, .45f), 1, true);
    }
}

public partial class WorldMapSurface : Control
{
    public WorldMapOverlay OwnerMap { get; set; } = null!;
    public override void _Draw() => OwnerMap.DrawSpace(this);
}
