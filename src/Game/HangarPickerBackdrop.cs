using Godot;

namespace SpaceFleet.Game;

/// <summary>Dismiss on outside click while leaving ship orbit and component picking usable.</summary>
internal partial class HangarPickerBackdrop : Control
{
    public Rect2 Passage { get; init; }
    public override bool _HasPoint(Vector2 point)=>!Passage.HasPoint(point)&&new Rect2(Vector2.Zero,Size).HasPoint(point);
}
