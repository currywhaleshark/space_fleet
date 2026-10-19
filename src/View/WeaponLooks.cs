using System.Runtime.CompilerServices;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.View;

/// <summary>
/// How much bigger a main gun's flash, tracer, recoil and report should be than the hull's stock gun:
/// caliber ratio × √overcharge (docs/DESIGN_modular_weapons.md §11). Stock batteries are exactly 1.
/// </summary>
public static class WeaponLooks
{
    private sealed class Box { public float Value; }
    private static readonly ConditionalWeakTable<RailgunState, Box> Cache = new();

    public static float Scale(RailgunState gun) => Cache.GetValue(gun, g => new Box { Value = Compute(g) }).Value;

    /// <summary>Caliber of a socket's part relative to the hull's stock part (1 when unknown or stock).</summary>
    public static float CaliberRatio(ShipDefinition hull, PrimaryWeaponDefinition weapon)
    {
        if (hull.Battery?.SocketWeapons is null || MainGunParts.Find(weapon.Id) is not { CaliberMm: > 0 } part) return 1;
        try { return MainGunParts.StockFor(hull) is { CaliberMm: > 0 } stock ? part.CaliberMm / stock.CaliberMm : 1; }
        catch (System.IO.InvalidDataException) { return 1; }
    }

    private static float Compute(RailgunState gun)
    {
        float ratio = gun.SupplyRatio == 1 ? 1 : Mathf.Sqrt(gun.Overcharge);
        var hull = gun.Ship.Definition;
        return Mathf.Clamp(CaliberRatio(hull, gun.Weapon) * ratio, .4f, 2.5f);
    }
}
