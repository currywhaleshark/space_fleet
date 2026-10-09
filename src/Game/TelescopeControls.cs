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
                if (Input.MouseMode==Input.MouseModeEnum.Visible) GetViewport().WarpMouse(_scopePointer);
            }
        }
    }

    // Use the same interpolated hull pose as the visible model, relative to its render position.
    public bool ScopeHullBlocked => Camera.TelescopeHeld && Controlled is { } me
        && DamageRay.FirstHitAtPose(me.Body,Vec3d.From(Camera.Position-me.Position),Camera.AimForward,
            me.Body.Class.Length*2,Vec3d.Zero,me.Quaternion,out _);

    private void SelectScopeTarget()
    {
        if (Controlled is not { } me || ScopeHullBlocked) { Notify("선체에 시야 가림",true); return; }
        Vector2 center=Camera.AimScreenPosition();
        ShipView? picked=null,hitShip=null; float nearest=36*36,hitRange=Camera.Far;
        foreach (var view in Views)
        {
            if (view==me || ContactOf(view) is not { SignalLost: false } contact) continue;
            Vector3 point=(contact.DisplayPosition(view.SimPosition)-RenderOrigin).ToVector3();
            if (Camera.IsPositionBehind(point)) continue;
            if ((contact.Track.Level>=TrackLevel.Identified || view.Body.Faction==me.Body.Faction)
                && DamageRay.FirstHitAtPose(view.Body,RenderOrigin+Vec3d.From(Camera.Position),Camera.AimForward,hitRange,
                    contact.DisplayPosition(view.SimPosition),view.Quaternion,out float hit))
            { hitShip=view; hitRange=hit; }
            float distance=Camera.UnprojectPosition(point).DistanceSquaredTo(center);
            if (distance>=nearest) continue;
            picked=view; nearest=distance;
        }
        picked=hitShip??picked;
        if (picked is null) { Notify("조준선 안에 탐지된 표적 없음",true); return; }
        if (picked.Body.Faction==me.Body.Faction) SelectedFriendly=picked;
        else SelectEnemy(picked);
        Notify("망원 표적 · "+ContactOf(picked)!.Name,false);
    }
}
