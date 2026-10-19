using System;
using System.Collections.Generic;
using Godot;

namespace SpaceFleet.Game;

// Declutters text only. World symbols, sensor data and picking positions are never changed.
internal sealed class MarkerLabels
{
    internal readonly record struct Request(object Key, Vector2 Point, Vector2 Offset, string Text,
        Color Color, int Priority, int FontSize, int Order);
    internal readonly record struct Placed(Request Label, Rect2 Bounds, Vector2 Baseline);
    private readonly List<Request> _requests = new();
    private readonly List<Placed> _placed = new();
    private readonly List<Rect2> _occupied = new();
    private readonly List<Rect2> _reserved = new();
    private readonly Dictionary<Vector2I, List<int>> _cells = new();
    private Dictionary<object, Vector2> _previous = new(), _next = new();
    private Rect2 _screen;
    public IReadOnlyList<Placed> Placements => _placed;
    public float TextScale { get; set; } = 1;

    public void Begin(Vector2 size)
    {
        _screen = new Rect2(Vector2.One * 8, size - Vector2.One * 16);
        _requests.Clear(); _placed.Clear(); _occupied.Clear(); _reserved.Clear();
        foreach (var cell in _cells.Values) cell.Clear();
        (_previous, _next) = (_next, _previous); _next.Clear();
    }
    public void Reserve(Rect2 area) => _reserved.Add(area.Grow(3));
    public void ProtectSymbol(Vector2 point, float radius = 8)
    {
        if (_screen.Grow(radius).HasPoint(point))
            Occupy(new Rect2(point-Vector2.One*radius,Vector2.One*radius*2).Grow(2));
    }
    public void Add(object key, Vector2 point, Vector2 offset, string text, Color color, int priority = 10, int size = 13)
    {
        if (!_screen.Grow(8).HasPoint(point)) return;
        _requests.Add(new(key, point, offset, text, color, priority, (int)MathF.Round(size * TextScale), _requests.Count));
    }
    private bool Fits(Rect2 rect)
    {
        if (!_screen.Encloses(rect)) return false;
        foreach (var block in _reserved) if (block.Intersects(rect)) return false;
        var a = Cell(rect.Position); var b = Cell(rect.End);
        for (int x = a.X; x <= b.X; x++) for (int y = a.Y; y <= b.Y; y++)
            if (_cells.TryGetValue(new(x, y), out var indices))
                foreach (int index in indices) if (_occupied[index].Intersects(rect)) return false;
        return true;
    }
    private static Vector2I Cell(Vector2 p) => new((int)MathF.Floor(p.X / 64), (int)MathF.Floor(p.Y / 64));
    private void Occupy(Rect2 rect)
    {
        int index = _occupied.Count; _occupied.Add(rect);
        var a = Cell(rect.Position); var b = Cell(rect.End);
        for (int x = a.X; x <= b.X; x++) for (int y = a.Y; y <= b.Y; y++)
        {
            var key = new Vector2I(x, y);
            if (!_cells.TryGetValue(key, out var indices)) _cells[key] = indices = new();
            indices.Add(index);
        }
    }
    public void Arrange(Func<string, int, Vector2> measure)
    {
        _requests.Sort((a, b) => a.Priority != b.Priority ? b.Priority.CompareTo(a.Priority) : a.Order.CompareTo(b.Order));
        foreach (var item in _requests)
        {
            Vector2 size = measure(item.Text, item.FontSize) + new Vector2(8, 4);
            Rect2 Rect(Vector2 offset) => new(item.Point + offset - new Vector2(4, item.FontSize + 1), size);
            Vector2 chosen = item.Offset;
            bool found = _previous.TryGetValue(item.Key, out var old) && Fits(Rect(old));
            if (found) chosen = old;
            for (int i = 0; !found && i < 12; i++)
            {
                float row = (i / 2) switch { 0 => 0, 1 => 22, 2 => -22, 3 => 44, 4 => -44, _ => 66 };
                Vector2 offset = new(i % 2 == 0 ? item.Offset.X : -size.X - 12, item.Offset.Y + row);
                if (Fits(Rect(offset))) { chosen = offset; found = true; }
            }
            // Selected/hovered labels get a longer callout if their immediate neighbourhood is full.
            if (!found && item.Priority >= 80)
                for (int row = 4; !found && row < 20; row++)
                    for (int side = -1; !found && side <= 1; side += 2)
                    {
                        var offset = new Vector2(item.Offset.X, item.Offset.Y + row * 22 * side);
                        if (Fits(Rect(offset))) { chosen = offset; found = true; }
                    }
            if (!found && item.Priority < 80) continue;
            Rect2 bounds = Rect(chosen);
            if (!found)
            {
                bounds.Position = new(Math.Clamp(bounds.Position.X, _screen.Position.X, Math.Max(_screen.Position.X, _screen.End.X - size.X)),
                    Math.Clamp(bounds.Position.Y, _screen.Position.Y, Math.Max(_screen.Position.Y, _screen.End.Y - size.Y)));
                chosen = bounds.Position - item.Point + new Vector2(4, item.FontSize + 1);
            }
            Occupy(bounds.Grow(2)); _next[item.Key] = chosen;
            _placed.Add(new(item, bounds, item.Point + chosen));
        }
    }
    public void Draw(Control canvas, Font font)
    {
        Arrange((s, n) => font.GetStringSize(s, fontSize: n));
        foreach (var item in _placed)
        {
            var r = item.Label;
            if ((item.Baseline - r.Point - r.Offset).LengthSquared() > 100)
                canvas.DrawLine(r.Point, new Vector2(Math.Clamp(r.Point.X, item.Bounds.Position.X, item.Bounds.End.X),
                    Math.Clamp(r.Point.Y, item.Bounds.Position.Y, item.Bounds.End.Y)), new Color(r.Color, .45f), 1, true);
            canvas.DrawRect(item.Bounds, new Color(.01f, .02f, .03f, r.Priority >= 80 ? .88f : .55f));
            canvas.DrawString(font, item.Baseline, r.Text, fontSize: r.FontSize, modulate: r.Color);
        }
    }
}
