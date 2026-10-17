using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

internal static class ShipAffiliation
{
    public static readonly Color Owned = new(.35f, 1f, .48f, .95f);
    public static readonly Color Allied = new(.45f, .75f, 1f, .85f);
    public static readonly Color Enemy = new(1f, .42f, .32f, .9f);
}

public partial class ScaleTest
{
    // Match ownership survives control handover, disability and spectating another allied squad.
    internal bool OwnsShip(ShipBody ship)
    {
        Squadron? squad = OwnedSquadron ?? Controlled?.Body.Squadron;
        return squad is not null && ship.Faction == squad.Faction && squad.Members.Contains(ship);
    }
    internal Color ShipMarkerColor(ShipBody ship) => OwnsShip(ship) ? ShipAffiliation.Owned
        : ship.Faction == (OwnedSquadron?.Faction ?? Controlled?.Body.Faction ?? Faction.Blue)
            ? ShipAffiliation.Allied : ShipAffiliation.Enemy;
}
