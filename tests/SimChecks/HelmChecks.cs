using Godot;
using SpaceFleet.Sim;

static class HelmChecks
{
    public static void Run()
    {
        int checks = 0;
        void Require(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        ShipBody Make(ShipClass cls) { var s = new ShipBody("HELM", cls, Faction.Blue); s.Place(Vec3d.Zero, Quaternion.Identity); return s; }
        void Step(ShipBody s, int ticks) { for (int i = 0; i < ticks; i++) s.Step(SimWorld.TickDelta); }
        var dd = Make(ShipClass.Escort);
        dd.Control = new ShipControl { Yaw = 1, Style = AssistStyle.Space };
        Step(dd, 120);
        Require(dd.Forward.X > 0, "Helm yaw must turn right");
        Require(Mathf.Abs(dd.AngularVelocity.Y) <= Mathf.DegToRad(dd.Class.PitchYawRateDeg) + 1e-5f, "Helm yaw rate limit");
        dd.Control = ShipControl.Idle;
        Step(dd, 120);
        var stopped = dd.Orientation;
        Step(dd, 300);
        Require(dd.AngularVelocity.Length() < 1e-5f, "Helm release must stop rotation");
        Require(stopped.AngleTo(dd.Orientation) < Mathf.DegToRad(1), "Helm release must hold attitude");
        dd = Make(ShipClass.Escort);
        dd.Control = new ShipControl { Pitch = 1 };
        Step(dd, 120);
        Require(dd.Forward.Y > 0, "Helm pitch must turn up");
        var bb = Make(ShipClass.Battleship);
        bb.Control = new ShipControl { Roll = 1 };
        double elapsed = 0;
        while (bb.Up.Y > -0.99f && elapsed < 30) { Step(bb, 1); elapsed += SimWorld.TickDelta; }
        Require(elapsed is > 15 and < 25, $"Battleship half roll took {elapsed:F1}s");
        Console.WriteLine($"PASS: {checks} helm checks");
    }
}
