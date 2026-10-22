using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

public partial class ScaleTest
{
    public SmallContact? SelectedSmallContact { get; private set; }
    private bool _explicitShipTarget;
    private Vector2? _selectionPointer;

    private void ClearSmallSelection()
    {
        SelectedSmallContact = null; SmallAimTarget = null; CorrectingSmallAim = false;
        TrackingSmallAim = false; SmallApproachSeconds = null; _smallTrackingOverrideUntil = 0;
        if (Gunnery is { } order) order.PlayerSmallTarget = false;
    }

    private void SelectPointerTarget() => SelectTargetAt(Camera.TelescopeHeld || Input.MouseMode == Input.MouseModeEnum.Captured
        ? Camera.AimScreenPosition() : _selectionPointer ?? GetViewport().GetMousePosition());

    // Selection uses visible sensor contacts; cycling list order never moves the camera or nose.
    internal void SelectTargetAt(Vector2 pointer)
    {
        if (Controlled is not { } me) return;
        if (ScopeHullBlocked) { Notify("선체에 시야 가림", true); return; }
        ShipView? ship = null; SmallContact? small = null;
        float nearest = float.PositiveInfinity, nearestRange = float.PositiveInfinity;
        Vec3d origin = RenderOrigin + Vec3d.From(Camera.ProjectRayOrigin(pointer));
        Vector3 ray = Camera.ProjectRayNormal(pointer);
        bool Score(Vec3d position, out float pixels, out float range)
        {
            Vector3 p = (position - RenderOrigin).ToVector3();
            pixels = range = float.PositiveInfinity;
            if (Camera.IsPositionBehind(p)) return false;
            Vector2 screen = Camera.UnprojectPosition(p);
            if (!GetViewport().GetVisibleRect().HasPoint(screen)) return false;
            pixels = screen.DistanceSquaredTo(pointer); range = (p - Camera.Position).Length();
            return true;
        }
        foreach (var view in Views)
        {
            if (view.Body.Faction == me.Body.Faction || ContactOf(view) is not { } contact || contact.State?.Destroyed == true) continue;
            // A remembered contact stays selectable, without reading its hidden live pose.
            Vec3d position = contact.SignalLost ? contact.Position : contact.DisplayPosition(view.SimPosition);
            bool visible = Score(position, out float pixels, out float range);
            // A large identified hull directly under the cursor takes priority over its center marker.
            if (!contact.SignalLost && contact.Identified && DamageRay.FirstHitAtPose(view.Body, origin, ray, Camera.Far,
                position, contact.Track.ObservedOrientation, out float hit)) { pixels = 0; range = hit; visible = true; }
            if (!visible || pixels > nearest || (pixels == nearest && range >= nearestRange)) continue;
            ship = view; small = null; nearest = pixels; nearestRange = range;
        }
        foreach (var contact in World.Sensors.SmallContacts(me.Body.Faction))
        {
            if (!contact.Fresh(World.Time) || !Score(contact.Predict(World.Time), out float pixels, out float range)
                || pixels > nearest || (pixels == nearest && range >= nearestRange)) continue;
            ship = null; small = contact; nearest = pixels; nearestRange = range;
        }
        if (small is { } chosen)
        {
            if(SelectedSmallContact?.Target!=chosen.Target) GuardTargetChange();
            SelectedSmallContact = chosen; InspectTarget = null; _explicitShipTarget = false;
            _smallTrackingOverrideUntil = 0;
            if (Gunnery is { } order)
            {
                order.PlayerSmallTarget=true; order.Target=null; order.PriorityModuleId=null;
                order.Engaged=null; order.EngagedModule=null; order.Solution=null; order.Status=GunneryStatus.Manual;
            }
            UpdateSmallAim(me.Body);
            Notify(!chosen.Locked(World.Time)?"소형 표적 지정 · 잠금 대기":AutoSmallBattery ? "소형 표적 지정 · 자동 요격"
                : chosen.Kind == SmallContactKind.Missile ? InputBindings.HintKeys("적 미사일 지정 · 1 주포 / 2 미사일 · 좌클릭 요격") : InputBindings.HintKeys("적 프로브 지정 · 1 주포 / 좌클릭 요격"), false);
        }
        else if (ship is not null) { SelectEnemy(ship); Notify("표적 · " + ContactOf(ship)!.Name, false); }
        else Notify("화면 안에 탐지된 적 표적 없음", true);
    }

    public string SmallAimStatus { get; private set; } = "";
    public bool SmallAimSolved { get; private set; }
}
