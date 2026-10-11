namespace SpaceFleet.Sim;
// Test assembly only. Exercise the same AI decision path without advancing its sensor clock.
public sealed partial class SimWorld
{
    internal void ThinkTerrainCheck(ShipBrain brain)=>Think(brain);
    internal bool SightWithinOrderCheck(ShipBrain brain,Vec3d point)=>SightWithinOrder(brain,point);
}
