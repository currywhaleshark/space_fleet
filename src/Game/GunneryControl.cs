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

    private void SelectEnemy(ShipView view)
    {
        InspectTarget = view;
        if (Gunnery is not { } order) return;
        if (order.Target != view.Body) order.PriorityModuleId = null;
        order.Target = view.Body;
    }

    private void SelectAt(Vector2 pointer)
    {
        if (Gunnery is not { } order || Controlled is not { } me) return;
        foreach (var (rect, view, module) in _hud.ModuleRegions.AsEnumerable().Reverse())
            if (rect.HasPoint(pointer) && view.Body.Faction != me.Body.Faction && !module.Destroyed)
            {
                SelectEnemy(view);
                order.PriorityModuleId = module.Definition.Id;
                if (order.Doctrine == FireDoctrine.Free) order.Doctrine = FireDoctrine.Focus;
                return;
            }
        ShipView? picked = _hud.BracketPositions.Where(p => p.Position.DistanceTo(pointer) <= 36)
            .OrderBy(p => p.Position.DistanceSquaredTo(pointer)).Select(p => p.View).FirstOrDefault();
        if (picked is null) return;
        if (picked.Body.Faction == me.Body.Faction) SelectedFriendly = picked;
        else SelectEnemy(picked);
    }

    private void SetupGunnery(ShipBody? previous, ShipBody next)
    {
        if (previous is not null) previous.Gunnery = null;
        next.Gunnery = SchemeFor(next) == ControlScheme.Helm ? new GunneryOrder() : null;
        SelectedFriendly = null;
    }
}
