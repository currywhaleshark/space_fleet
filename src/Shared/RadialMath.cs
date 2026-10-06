using System;
using System.Collections.Generic;
using Godot;

namespace SpaceFleet.Shared;

public readonly record struct RadialItemSpec(float AngleDeg);

public static class RadialMath
{
    public static int Pick(Vector2 offset, IReadOnlyList<float> anglesDeg, float deadZone)
    {
        if (!offset.IsFinite() || offset.Length() < deadZone || offset == Vector2.Zero) return -1;
        float angle = Mathf.RadToDeg(Mathf.Atan2(offset.X, -offset.Y));
        int best = -1;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < anglesDeg.Count; i++)
        {
            float distance = Math.Abs(Mathf.Wrap(angle - anglesDeg[i], -180f, 180f));
            if (distance < nearest) { nearest = distance; best = i; }
        }
        return best;
    }
}

/// <summary>Shared gesture lifetime: one open menu, dead-zone cancel, single release.</summary>
public sealed class RadialGesture
{
    private IReadOnlyList<float> _angles = Array.Empty<float>();
    public bool IsOpen { get; private set; }
    public Vector2 Offset { get; private set; }
    public int Highlighted => IsOpen ? RadialMath.Pick(Offset, _angles, 36) : -1;
    public bool Open(IReadOnlyList<float> angles)
    {
        if (IsOpen) return false;
        _angles = angles;
        Offset = Vector2.Zero;
        IsOpen = true;
        return true;
    }
    public void SetOffset(Vector2 offset) { if (IsOpen) Offset = offset; }
    public void FeedMotion(Vector2 relative) { if (IsOpen) Offset = (Offset + relative).LimitLength(140); }
    public int Release(Func<int, bool>? enabled = null)
    {
        int picked = Highlighted;
        IsOpen = false;
        return picked >= 0 && (enabled is null || enabled(picked)) ? picked : -1;
    }
    public void Cancel() => IsOpen = false;
}
