using System.Collections.Generic;

namespace SpaceFleet.Sim;

public sealed record PowerPreset(string Label, float AngleDeg, int Engines, int Shields, int Weapons, int Sensors, int Ecm)
{
    public void Apply(ShipPower power) => power.SetPips(Engines, Shields, Weapons, Sensors, Ecm);
}

public static class PowerPresets
{
    public static IReadOnlyList<PowerPreset> All { get; } = new[]
    {
        new PowerPreset("센서/EW", 0, 1, 1, 1, 3, 2),
        new PowerPreset("화력", 90, 1, 1, 4, 2, 0),
        new PowerPreset("기동", 180, 4, 2, 1, 1, 0),
        new PowerPreset("방어", 270, 1, 4, 2, 1, 0),
        new PowerPreset("균형", 45, 2, 2, 2, 2, 0),
    };
}
