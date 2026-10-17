using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

public partial class ScaleTest
{
    private Hud _hud = null!;
    public ShipView? SelectedFriendly { get; private set; }
    public GunneryOrder? Gunnery => Controlled?.Body.Gunnery;

    private void SetDoctrine(FireDoctrine doctrine)
    {
        if (Gunnery is not { } order) return;
        ClearSmallSelection();
        order.Doctrine = doctrine;
        Notify($"사격 · {GunneryLabels.Doctrine(doctrine)}", false);
    }

    private void CycleDoctrine()
    {
        if (Gunnery is { } order) SetDoctrine((FireDoctrine)(((int)order.Doctrine + 1) % Enum.GetValues<FireDoctrine>().Length));
    }

    private void SelectEnemy(ShipView view)
    {
        bool changed=InspectTarget!=view || SelectedSmallContact is not null || Gunnery is { } g && g.Target!=view.Body;
        ClearSmallSelection(); _explicitShipTarget = true;
        InspectTarget = view;
        if(changed) GuardTargetChange();
        if (Gunnery is not { } order) return;
        if (changed)
        {
            order.PriorityModuleId=null;
            order.Engaged=null; order.EngagedModule=null; order.Solution=null;
            order.Status=GunneryStatus.NoTarget;
        }
        order.Target = view.Body;
    }

    private void GuardTargetChange()
    {
        if(Controlled?.Body is { } ship) SimWorld.StopLasers(ship);
        _fireReleaseGuard |= Input.IsActionPressed(InputSetup.Fire) || Input.IsMouseButtonPressed(MouseButton.Left);
        FiringSolution=null; CorrectingAim=false;
    }

    private bool SelectAt(Vector2 pointer)
    {
        if (Gunnery is not { } order || Controlled is not { } me) return false;
        foreach (var (rect, view, module) in _hud.ModuleRegions.AsEnumerable().Reverse())
            if (rect.HasPoint(pointer) && view.Body.Faction != me.Body.Faction && !module.Destroyed)
            {
                SelectEnemy(view);
                order.PriorityModuleId = module.Definition.Id;
                if (order.Doctrine == FireDoctrine.Free) order.Doctrine = FireDoctrine.Focus;
                order.EngagedModule=null; order.Solution=null;
                return true;
            }
        ShipView? picked = _hud.BracketPositions.Where(p => p.Position.DistanceTo(pointer) <= 36)
            .OrderBy(p => p.Position.DistanceSquaredTo(pointer)).Select(p => p.View).FirstOrDefault();
        if (picked is null) return false;
        if (picked.Body.Faction == me.Body.Faction) SelectedFriendly = picked;
        else SelectEnemy(picked);
        return true;
    }

    private void SetupGunnery(ShipBody? previous, ShipBody next)
    {
        if (previous is not null) previous.Gunnery = null;
        next.Gunnery = SchemeFor(next) == ControlScheme.Helm ? new GunneryOrder() : null;
        ClearSmallSelection(); _explicitShipTarget = false; _selectionPointer = null;
        SelectedFriendly = null;
    }
}
