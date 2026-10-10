using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

public partial class ScaleTest
{
    private Input.MouseModeEnum _scopePreviousMouse;
    private Vector2 _scopePointer;
    private bool _scopeCaptured;
    private GunneryOrder? _scopeOrder;
    private FireDoctrine _scopeDoctrine;

    private void OnTelescopeChanged(bool held)
    {
        if (held)
        {
            if (Gunnery is { } order)
            {
                _scopeOrder=order; _scopeDoctrine=order.Doctrine;
                order.Doctrine=FireDoctrine.Manual;
            }
            if (_shot is null && Scheme==ControlScheme.Helm)
            {
                _scopePreviousMouse=Input.MouseMode; _scopePointer=GetViewport().GetMousePosition();
                _scopeCaptured=true; Input.MouseMode=Input.MouseModeEnum.Captured;
            }
        }
        else
        {
            if (_scopeOrder is { Doctrine: FireDoctrine.Manual } order) order.Doctrine=_scopeDoctrine;
            _scopeOrder=null;
            if (_scopeCaptured)
            {
                _scopeCaptured=false;
                Input.MouseMode=Paused || Spectating || WorldMapOpen ? Input.MouseModeEnum.Visible : _scopePreviousMouse;
                if (Input.MouseMode==Input.MouseModeEnum.Visible) { GetViewport().WarpMouse(_scopePointer); _selectionPointer = _scopePointer; }
            }
        }
    }

    // Use the same interpolated hull pose as the visible model, relative to its render position.
    public bool ScopeHullBlocked => Camera.TelescopeHeld && Controlled is { } me
        && DamageRay.FirstHitAtPose(me.Body,Vec3d.From(Camera.Position-me.Position),Camera.AimForward,
            me.Body.Class.Length*2,Vec3d.Zero,me.Quaternion,out _);

}
