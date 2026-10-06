using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Shared;

namespace SpaceFleet.Game;

public sealed record RadialItem(string Label, float AngleDeg, Action Execute, Func<bool>? Enabled = null);

public partial class RadialMenu : Control
{
    private readonly RadialGesture _gesture = new();
    private IReadOnlyList<RadialItem> _items = Array.Empty<RadialItem>();
    private string _title = "";
    private Vector2 _center;
    private bool _virtual;
    private Font _font = null!;
    public bool IsOpen => _gesture.IsOpen;
    public int Highlighted => _gesture.Highlighted;
    public Vector2 Center => _center;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        _font = new SystemFont { FontNames = new[] { "Malgun Gothic", "Segoe UI" } };
    }
    public void Open(string title, IReadOnlyList<RadialItem> items, Vector2 center, bool virtualPointer)
    {
        if (!_gesture.Open(items.Select(i => i.AngleDeg).ToArray())) return;
        _items = items; _title = title; _center = center; _virtual = virtualPointer;
        QueueRedraw();
    }
    public void FeedMotion(Vector2 relative) { if (_virtual) _gesture.FeedMotion(relative); QueueRedraw(); }
    public void SetPointer(Vector2 screen) { if (!_virtual) _gesture.SetOffset(screen - _center); QueueRedraw(); }
    public void Release()
    {
        int choice = _gesture.Release(i => _items[i].Enabled?.Invoke() ?? true);
        QueueRedraw();
        if (choice >= 0) _items[choice].Execute();
    }
    public void Cancel() { _gesture.Cancel(); QueueRedraw(); }

    public override void _Draw()
    {
        if (!IsOpen) return;
        // Keep all labels on-screen while retaining the original gesture origin near viewport edges.
        Vector2 size = GetViewportRect().Size;
        Vector2 c = _center.Clamp(new Vector2(132, 132), size - new Vector2(132, 132));
        DrawCircle(c, 120, new Color(0.01f, 0.02f, 0.04f, 0.9f));
        var sorted = _items.Select((item, index) => (item, index)).OrderBy(v => v.item.AngleDeg).ToArray();
        for (int slot = 0; slot < sorted.Length; slot++)
        {
            var (item, index) = sorted[slot];
            float previous = sorted[(slot + sorted.Length - 1) % sorted.Length].item.AngleDeg;
            float next = sorted[(slot + 1) % sorted.Length].item.AngleDeg;
            float left = item.AngleDeg - Mathf.PosMod(item.AngleDeg - previous, 360f) * 0.5f;
            float right = item.AngleDeg + Mathf.PosMod(next - item.AngleDeg, 360f) * 0.5f;
            bool enabled = item.Enabled?.Invoke() ?? true;
            bool selected = index == Highlighted;
            Color color = enabled ? new Color(0.45f, 0.75f, 1f, 0.95f) : new Color(0.45f, 0.55f, 0.65f, 0.3f);
            if (selected)
            {
                var points = new List<Vector2> { c };
                for (int k = 0; k <= 24; k++) points.Add(c + Direction(Mathf.Lerp(left, right, k / 24f)) * 119);
                DrawColoredPolygon(points.ToArray(), new Color(color, enabled ? 0.25f : 0.07f));
            }
            DrawArc(c, 117, Mathf.DegToRad(left - 90), Mathf.DegToRad(right - 90), 32, color, selected ? 4 : 1);
            DrawLine(c + Direction(left) * 36, c + Direction(left) * 117, new Color(color, 0.25f), 1);
            Label(c + Direction(item.AngleDeg) * 88, item.Label, enabled ? color : new Color(color, 0.45f), 13);
        }
        DrawCircle(c, 36, new Color(0.01f, 0.02f, 0.04f, 0.95f));
        DrawArc(c, 36, 0, Mathf.Tau, 40, new Color(0.7f, 0.8f, 1f, 0.4f), 1);
        Label(c + new Vector2(0, -7), _title, Colors.White, 13);
        Label(c + new Vector2(0, 12), "취소", new Color(0.7f, 0.8f, 1f, 0.5f), 11);
        if (_gesture.Offset.LengthSquared() > 1)
        {
            Vector2 pointer = _gesture.Offset.LimitLength(110);
            DrawLine(c + pointer.Normalized() * 40, c + pointer, new Color(1f, 0.8f, 0.4f), 2);
            DrawCircle(c + pointer, 4, new Color(1f, 0.8f, 0.4f));
        }
    }
    private static Vector2 Direction(float degrees) => new(Mathf.Sin(Mathf.DegToRad(degrees)), -Mathf.Cos(Mathf.DegToRad(degrees)));
    private void Label(Vector2 center, string label, Color color, int size)
    {
        Vector2 dimensions = _font.GetStringSize(label, HorizontalAlignment.Left, -1, size);
        DrawString(_font, center + new Vector2(-dimensions.X * 0.5f, dimensions.Y * 0.3f), label, HorizontalAlignment.Left, -1, size, color);
    }
}
