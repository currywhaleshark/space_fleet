using Godot;
using SpaceFleet.Shared;
using SpaceFleet.Sim;

static class RadialChecks
{
    public static void Run()
    {
        int checks = 0;
        void Require(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        Vector2 Offset(float deg, float radius = 100) => new Vector2(Mathf.Sin(Mathf.DegToRad(deg)), -Mathf.Cos(Mathf.DegToRad(deg))) * radius;
        float[] angles = { 0, 90, 180, 270, 45 };
        Require(RadialMath.Pick(Vector2.Zero, angles, 36) == -1, "Center must cancel");
        Require(RadialMath.Pick(Offset(90, 35.99f), angles, 36) == -1, "Dead-zone must cancel");
        for (int i = 0; i < angles.Length; i++) Require(RadialMath.Pick(Offset(angles[i]), angles, 36) == i, "Cardinal/diagonal pick");
        foreach (var (angle, expected) in new[] { (22.4f, 0), (22.6f, 4), (67.4f, 4), (67.6f, 1), (134.9f, 1),
                     (135.1f, 2), (224.9f, 2), (225.1f, 3), (314.9f, 3), (315.1f, 0), (359.9f, 0), (-0.1f, 0) })
            Require(RadialMath.Pick(Offset(angle), angles, 36) == expected, $"Nonuniform radial boundary {angle}");
        var gesture = new RadialGesture();
        Require(gesture.Open(angles), "Open radial");
        Require(!gesture.Open(new[] { 180f }), "Only one radial may open");
        gesture.FeedMotion(new Vector2(1000, 0));
        Require(gesture.Offset.Length() <= 140.001f && gesture.Highlighted == 1, "Virtual cursor clamp/pick");
        Require(gesture.Release() == 1 && !gesture.IsOpen, "Release must select once and unblock input");
        Require(gesture.Release() == -1, "Repeated key release must not execute");
        gesture.Open(angles); gesture.SetOffset(Offset(90));
        Require(gesture.Release(_ => false) == -1, "Disabled item must not execute");
        gesture.Open(angles); gesture.Cancel();
        Require(gesture.Release() == -1, "Esc cancel must not execute");
        gesture.Open(angles);
        Require(gesture.Release() == -1, "Dead-zone release must not execute");
        var ship = new ShipBody("POWER", ShipClass.Escort, Faction.Blue);
        foreach (var preset in PowerPresets.All)
        {
            preset.Apply(ship.Power);
            int[] pips = Enum.GetValues<PowerChannel>().Select(ship.Power.Pips).ToArray();
            Require(pips.Sum() == 8 && pips.All(n => n is >= 0 and <= 4), "Preset pip budget");
        }
        PowerPresets.All.Single(p => p.Label == "화력").Apply(ship.Power);
        Require(Enum.GetValues<PowerChannel>().Select(ship.Power.Pips).SequenceEqual(new[] { 1, 1, 4, 2, 0 }), "Fire preset values");
        Console.WriteLine($"PASS: {checks} radial/preset checks");
    }
}
