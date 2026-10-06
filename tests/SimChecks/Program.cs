using Godot;
using SpaceFleet.Sim;

static class SimChecks
{
    private const float Dt = (float)SimWorld.TickDelta;
    private static int _checks;

    public static void Main()
    {
        CheckCombinedG();
        CheckInertialFlight();
        CheckTurnBySpeed();
        CheckHalfTurn();
        CheckPitchAndRoll();
        CheckStrafeAndReverse();
        CheckSpaceStyle();
        Console.WriteLine($"PASS: {_checks} simulation checks");
        CollisionChecks.Run();
        DamageChecks.Run();
        BallisticsChecks.Run();
        PowerChecks.Run();
        SensorChecks.Run();
        MissileChecks.Run();
        InfiltrationChecks.Run();
        HelmChecks.Run();
        GunneryChecks.Run();
        RadialChecks.Run();
        SquadCommandChecks.Run();
        AIChecks.Run();
        BattleChecks.Run();
    }

    private static ShipBody Create(ShipClass? shipClass = null, float speed = 0f)
    {
        var ship = new ShipBody("TEST", shipClass ?? ShipClass.Interceptor, Faction.Blue);
        ship.Place(Vec3d.Zero, Quaternion.Identity);
        ship.Velocity = Vector3.Forward * speed;
        return ship;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
        _checks++;
    }

    private static float StepAndCheckG(ShipBody ship)
    {
        Vector3 before = ship.Velocity;
        ship.Step(SimWorld.TickDelta);
        float measuredG = (ship.Velocity - before).Length() / Dt / ShipBody.StandardGravity;
        if (!float.IsFinite(measuredG) || measuredG > ship.Class.MaxAccelG + 0.002f)
            throw new InvalidOperationException($"G limit: {ship.Class.Kind} measured {measuredG} > {ship.Class.MaxAccelG}");
        return measuredG;
    }

    private static void CheckCombinedG()
    {
        foreach (ShipClass shipClass in new[] { ShipClass.Battleship, ShipClass.Escort, ShipClass.Interceptor })
        foreach (bool assist in new[] { false, true })
        {
            var ship = Create(shipClass);
            ship.Control = new ShipControl
            {
                Thrust = new Vector3(1, 1, 1), Boost = true, FlightAssist = assist,
            };
            float peak = 0;
            for (int tick = 0; tick < 600; tick++)
                peak = Math.Max(peak, StepAndCheckG(ship));
            Require(peak > 0f, "Combined thrust must accelerate");
            Require(!assist || ship.Velocity.Length() <= shipClass.MaxSpeed * shipClass.BoostMultiplier + 0.1f,
                "Assisted diagonal thrust must respect target speed");
        }
        var boost = Create();
        boost.Control = new ShipControl { Thrust = new Vector3(0, 0, 1), Boost = true, FlightAssist = true };
        float firstG = StepAndCheckG(boost);
        Require(firstG > 4.9f, "Interceptor boost should use its available 5G budget");
        Console.WriteLine($"Interceptor straight boost: {firstG:0.00} G");
    }

    private static void CheckInertialFlight()
    {
        var ship = Create(speed: 380);
        Vector3 original = ship.Velocity;
        ship.Control = new ShipControl { HelmForward = Vector3.Back, FlightAssist = false };
        for (int tick = 0; tick < 300; tick++)
            StepAndCheckG(ship);
        Require(ship.Forward.Dot(Vector3.Back) > 0.999f, "Assist OFF should allow an independent nose flip");
        Require(ship.Velocity.DistanceTo(original) < 0.001f, "Rotating with no thrust must preserve world velocity");
        Require(ship.GLoad == 0f && !ship.TurnBraking, "No fictitious G or automatic braking with assist OFF");
    }

    private static void CheckTurnBySpeed()
    {
        float Rate(float speed)
        {
            var ship = Create(speed: speed);
            ship.Control = new ShipControl { HelmForward = Vector3.Right, FlightAssist = true };
            for (int tick = 0; tick < 6; tick++)
                StepAndCheckG(ship);
            return ship.AngularVelocity.Length();
        }
        float slow = Rate(20), fast = Rate(380);
        Require(slow > fast * 5f, "Slow flight must permit a much faster turn");
        Console.WriteLine($"Turn rate at 0.1s: 20m/s={Mathf.RadToDeg(slow):0.0}deg/s, 380m/s={Mathf.RadToDeg(fast):0.0}deg/s");
    }

    private static void CheckHalfTurn()
    {
        foreach (float initialSpeed in new[] { 100f, 380f, 760f })
        {
            var ship = Create(speed: initialSpeed);
            ship.Control = new ShipControl
            {
                Thrust = new Vector3(0, 0, 1), FlightAssist = true,
                HelmForward = Vector3.Back, Boost = initialSpeed > 380f,
            };
            bool braked = false;
            float minimumSpeed = initialSpeed, peakG = 0, peakSlip = 0;
            double completedAt = -1;
            for (int tick = 0; tick < 3600; tick++)
            {
                peakG = Math.Max(peakG, StepAndCheckG(ship));
                braked |= ship.TurnBraking;
                minimumSpeed = Math.Min(minimumSpeed, ship.Velocity.Length());
                if (ship.Velocity.LengthSquared() > 25f)
                    peakSlip = Math.Max(peakSlip, Mathf.RadToDeg(ship.Forward.AngleTo(ship.Velocity)));
                if (completedAt < 0 && ship.Forward.Dot(Vector3.Back) > 0.999f
                    && ship.Velocity.LengthSquared() > 25f && ship.Velocity.Normalized().Dot(Vector3.Back) > 0.99f)
                    completedAt = (tick + 1) * SimWorld.TickDelta;
            }
            Require(braked, "180-degree command must trigger automatic braking");
            Require(minimumSpeed < initialSpeed * 0.6f, "Half turn must shed speed before completing");
            Require(peakSlip < 90f, $"Assisted turn must avoid flying backwards: slip={peakSlip}");
            Require(completedAt > 0, "180-degree assisted turn must complete without getting stuck");
            Require(ship.Velocity.Dot(Vector3.Back) > ship.Class.MaxSpeed * 0.9f,
                "Throttle must resume forward acceleration after the turn");
            Console.WriteLine($"180deg from {initialSpeed:0}m/s: aligned at {completedAt:0.00}s, min speed={minimumSpeed:0.0}, peak={peakG:0.00}G, max slip={peakSlip:0.0}deg");
        }
    }

    private static void CheckStrafeAndReverse()
    {
        var ship = Create();
        ship.Control = new ShipControl { Thrust = Vector3.Right, FlightAssist = true };
        for (int tick = 0; tick < 900; tick++)
            StepAndCheckG(ship);
        Require(ship.Velocity.X > 370f, "Manual strafe must remain available");
        ship = Create();
        ship.Control = new ShipControl { Thrust = new Vector3(0, 0, -0.3f), FlightAssist = true };
        for (int tick = 0; tick < 600; tick++)
            StepAndCheckG(ship);
        Require(Math.Abs(ship.Velocity.Z - 114f) < 0.1f, "Reverse throttle must retain its target speed");
        ship.Control = ShipControl.Idle;
        for (int tick = 0; tick < 600; tick++)
            StepAndCheckG(ship);
        Require(ship.Velocity.Length() < 0.01f, "Zero throttle with assist ON must come to rest");
    }

    private static void CheckSpaceStyle()
    {
        // 우주식: 기수 선회율이 속도와 무관하다.
        float Rate(float speed)
        {
            var ship = Create(speed: speed);
            ship.Control = new ShipControl { HelmForward = Vector3.Right, FlightAssist = true, Style = AssistStyle.Space };
            for (int tick = 0; tick < 6; tick++)
                StepAndCheckG(ship);
            return ship.AngularVelocity.Length();
        }
        float slow = Rate(20), fast = Rate(380);
        Require(Math.Abs(slow - fast) < 1e-4f, "Space style nose rate must not depend on speed");

        // 380 m/s에서 90도 꺾기: 기수는 먼저 돌고, 이동 방향은 G 한계 안에서 뒤따라 모인다.
        var ship = Create(speed: 380);
        ship.Control = new ShipControl
        {
            Thrust = new Vector3(0, 0, 1), FlightAssist = true, Style = AssistStyle.Space, HelmForward = Vector3.Right,
        };
        double noseAt = -1, alignedAt = -1;
        float minimumSpeed = float.PositiveInfinity;
        bool braked = false;
        for (int tick = 0; tick < 3600 && alignedAt < 0; tick++)
        {
            StepAndCheckG(ship);
            braked |= ship.TurnBraking;
            minimumSpeed = Math.Min(minimumSpeed, ship.Velocity.Length());
            double t = (tick + 1) * SimWorld.TickDelta;
            if (noseAt < 0 && ship.Forward.Dot(Vector3.Right) > 0.999f) noseAt = t;
            if (noseAt > 0 && ship.DriftDegrees < 2f) alignedAt = t;
        }
        Require(!braked, "Space style must not use automatic turn braking");
        Require(noseAt > 0 && noseAt < 2.0, $"Space style nose must swing quickly at speed: {noseAt:0.00}s");
        Require(alignedAt > noseAt, "Velocity must converge on the nose after the swing");
        Require(minimumSpeed > 150f, $"Space style keeps momentum through a turn: min {minimumSpeed:0}");
        Console.WriteLine($"Space 90deg at 380m/s: nose {noseAt:0.00}s, velocity aligned {alignedAt:0.00}s, min speed {minimumSpeed:0}");

        // 정렬 시간 추정은 실제 횡속도 제거 시간과 같은 규모여야 한다.
        var drift = Create(speed: 0);
        drift.Velocity = Vector3.Right * 200f;
        drift.Control = new ShipControl { FlightAssist = true, Style = AssistStyle.Space };
        float estimate = drift.LateralSettleSeconds;
        int ticks = 0;
        while (drift.Velocity.Length() > 1f && ticks < 3600) { StepAndCheckG(drift); ticks++; }
        float actual = ticks * Dt;
        Require(Math.Abs(actual - estimate) < 0.2f, $"Settle estimate {estimate:0.00}s vs actual {actual:0.00}s");
    }

    private static void CheckPitchAndRoll()
    {
        var pitch = Create(speed: 380);
        pitch.Control = new ShipControl
        {
            Thrust = new Vector3(0, 0, 1), FlightAssist = true,
            HelmForward = (Vector3.Back + Vector3.Up * 0.05f).Normalized(),
        };
        float minimumForwardSpeed = float.PositiveInfinity;
        for (int tick = 0; tick < 1800; tick++)
        {
            StepAndCheckG(pitch);
            minimumForwardSpeed = Math.Min(minimumForwardSpeed, pitch.Velocity.Dot(pitch.Forward));
        }
        Require(minimumForwardSpeed >= -0.01f, "Pitch half turn must avoid backward drift");
        Require(pitch.Forward.Dot(pitch.Control.HelmForward!.Value) > 0.999f, "Pitch half turn must finish");
        var mixed = Create(speed: 380);
        mixed.Control = new ShipControl
        {
            Thrust = Vector3.One, FlightAssist = true, Boost = true, Roll = 1,
            HelmForward = (Vector3.Up + Vector3.Right).Normalized(),
        };
        for (int tick = 0; tick < 1200; tick++)
            StepAndCheckG(mixed);
        Require(Math.Abs(mixed.Orientation.Length() - 1f) < 0.0001f,
            "Combined pitch, yaw, roll and thrust must retain a valid orientation");
    }
}
