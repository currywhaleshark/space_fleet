using Godot;
using SpaceFleet.Sim;

/// <summary>4단계 전력 배분·폐열 검증.</summary>
static class PowerChecks
{
    private static int _checks;

    public static void Run()
    {
        CheckPips();
        CheckBalancedSupply();
        CheckEngineChannel();
        CheckRcsCost();
        CheckWeaponChannel();
        CheckShieldChannel();
        CheckSensorChannel();
        CheckBrownout();
        CheckHeat();
        CheckCoolingLoss();
        Console.WriteLine($"PASS: {_checks} power/heat checks");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    private static bool Near(float a, float b, float tolerance = 0.001f) => Math.Abs(a - b) < tolerance;

    private static ShipBody Create(HullKind kind, SimWorld? world = null, string callsign = "P")
    {
        ShipDefinition definition = ShipDefinitions.For(kind);
        var ship = new ShipBody(callsign, definition.Flight, Faction.Blue, definition);
        ship.Place(Vec3d.Zero, Quaternion.Identity);
        ship.Control = new ShipControl { FlightAssist = false };
        world?.Add(ship);
        return ship;
    }

    private static void Steps(ShipBody ship, int ticks)
    {
        for (int i = 0; i < ticks; i++) ship.Step(SimWorld.TickDelta);
    }

    private static void CheckPips()
    {
        var power = Create(HullKind.Interceptor).Power;
        int Sum() => power.Pips(PowerChannel.Engines) + power.Pips(PowerChannel.Shields) + power.Pips(PowerChannel.Weapons) + power.Pips(PowerChannel.Sensors);
        Require(power.Pips(PowerChannel.Engines) == 2 && Sum() == 8, "Ships start balanced at 2 pips per channel");
        Require(power.AddPip(PowerChannel.Weapons) && power.AddPip(PowerChannel.Weapons) && power.Pips(PowerChannel.Weapons) == 4 && Sum() == 8,
            "Adding pips moves them from other channels and keeps the total");
        Require(!power.AddPip(PowerChannel.Weapons) && power.Pips(PowerChannel.Weapons) == 4, "A channel cannot exceed 4 pips");
        power.ResetPips();
        Require(power.Pips(PowerChannel.Weapons) == 2 && Sum() == 8, "Reset returns to balanced");
        bool rejected = false;
        try { power.SetPips(4, 4, 4, 0); } catch (ArgumentException) { rejected = true; }
        Require(rejected, "Explicit allocations must sum to 8");
    }

    private static void CheckBalancedSupply()
    {
        // 건강한 함선이 기준 배분으로 모든 채널을 최대로 써도 전압 강하가 없다.
        foreach (HullKind kind in new[] { HullKind.Battleship, HullKind.Escort, HullKind.Interceptor })
        {
            var world = new SimWorld();
            ShipBody ship = Create(kind, world);
            ship.Control = new ShipControl { Thrust = new Vector3(0, 0, 1), FlightAssist = false };
            ship.Damage.AbsorbShield(ship.Damage.Shield * 0.5f, 0);
            for (int i = 0; i < 60 * 6; i++)
            {
                if (ship.Railgun!.Ready) world.FireRailgun(ship, ship.Forward);
                world.Step();
            }
            Require(Near(ship.Power.Supply, 1) && Near(ship.Power.EngineEffect, 1), $"{kind}: balanced full activity must not brown out");
        }
    }

    private static float FirstTickAcceleration(int enginePips)
    {
        ShipBody ship = Create(HullKind.Battleship);
        switch (enginePips)
        {
            case 4: ship.Power.SetPips(4, 2, 2, 0); break;
            case 0: ship.Power.SetPips(0, 4, 4, 0); break;
            default: ship.Power.ResetPips(); break;
        }
        ship.Control = new ShipControl { Thrust = new Vector3(0, 0, 1), FlightAssist = false };
        ship.Step(SimWorld.TickDelta);
        return ship.Velocity.Length() / (float)SimWorld.TickDelta;
    }

    private static void CheckEngineChannel()
    {
        // 전함 전진 3 m/s²는 4핍(×1.5)에도 0.5G 상한(4.9 m/s²) 아래라 배율이 그대로 보인다.
        float balanced = FirstTickAcceleration(2), full = FirstTickAcceleration(4), none = FirstTickAcceleration(0);
        Require(Near(full / balanced, 1.5f, 0.01f), $"4 engine pips must give 1.5x thrust: {full / balanced:0.000}");
        Require(Near(none / balanced, 0.3f, 0.01f), $"0 engine pips must give 0.3x thrust: {none / balanced:0.000}");
    }

    private static void CheckRcsCost()
    {
        // 요격함 380 m/s 순항 중 기수를 3° 틀면 비행보조가 횡추력을 순간 최대로 쓴다. 보조 추진기 단가로 세야 한다.
        ShipDefinition def = ShipDefinitions.For(HullKind.Interceptor);
        ShipBody ship = Create(HullKind.Interceptor);
        ship.Velocity = Vector3.Forward * def.Flight.MaxSpeed;
        ship.Control = new ShipControl { Thrust = new Vector3(0, 0, 1), FlightAssist = true, Style = AssistStyle.Space, AimForward = Vector3.Forward };
        Steps(ship, 60);
        ship.Control = ship.Control with { AimForward = new Quaternion(Vector3.Up, Mathf.DegToRad(3)) * Vector3.Forward };
        float peak = 0, lateralPeak = 0;
        for (int i = 0; i < 300; i++)
        {
            ship.Step(SimWorld.TickDelta);
            peak = Math.Max(peak, ship.Power.DrawMw(PowerChannel.Engines));
            lateralPeak = Math.Max(lateralPeak, new Vector2(ship.LocalAcceleration.X, ship.LocalAcceleration.Y).Length());
        }
        float expected = ShipPower.RcsPowerFactor * def.Flight.StrafeAccel / def.Flight.ForwardAccel * def.Power.Engines;
        Require(lateralPeak > def.Flight.StrafeAccel * 0.99f, "The nudge must exercise full lateral thrust");
        // 횡추력 최대 동안 비행보조가 전진 속도도 조금 보정하므로 메인 몫이 약간 섞인다.
        Require(peak >= expected * 0.95f && peak < def.Power.Engines * 0.4f,
            $"Slip correction must be charged at the RCS rate: peak {peak:0.0} MW vs main burn {def.Power.Engines} MW");
        Console.WriteLine($"RCS cost (IC 3° nudge at 380 m/s): engines peak {peak:0.0} MW ({peak / def.Power.Engines:P0} of full main burn)");
    }

    private static int TicksToReload(int weaponPips)
    {
        var world = new SimWorld();
        ShipBody ship = Create(HullKind.Interceptor, world);
        if (weaponPips == 4) ship.Power.SetPips(2, 2, 4, 0);
        Require(world.FireRailgun(ship, ship.Forward).Fired, "Reload test must fire");
        int ticks = 0;
        while (!ship.Railgun!.Ready && ticks < 6000) { world.Step(); ticks++; }
        return ticks;
    }

    private static void CheckWeaponChannel()
    {
        int balanced = TicksToReload(2), full = TicksToReload(4);
        Require(Math.Abs(balanced / (float)full - 1.5f) < 0.05f, $"4 weapon pips must reload 1.5x faster: {balanced} vs {full} ticks");
    }

    private static float Recharge(int shieldPips)
    {
        ShipBody ship = Create(HullKind.Interceptor);
        ship.Power.SetPips(2, shieldPips, 2, 4 - shieldPips);
        ship.Damage.AbsorbShield(ship.Damage.Shield, 0);
        float delay = ship.Definition.Shield.RechargeDelay;
        Steps(ship, (int)(delay * 60) + 2);
        float before = ship.Damage.Shield;
        Steps(ship, 60);
        return ship.Damage.Shield - before;
    }

    private static void CheckShieldChannel()
    {
        float balanced = Recharge(2), full = Recharge(4), low = Recharge(1);
        Require(Near(full / balanced, 1.5f, 0.02f), $"4 shield pips must recharge 1.5x faster: {full / balanced:0.000}");
        Require(Near(low / balanced, 0.65f, 0.02f), $"1 shield pip must recharge at 0.65x: {low / balanced:0.000}");
    }

    private static void CheckSensorChannel()
    {
        ShipBody Shooter(int sensorPips)
        {
            ShipBody s = Create(HullKind.Battleship, callsign: "S");
            s.Power.SetPips(2, 2, 4 - sensorPips, sensorPips);
            s.Step(SimWorld.TickDelta);
            return s;
        }
        ShipBody target = Create(HullKind.Escort, callsign: "T");
        target.Place(new Vec3d(0, 0, -50_000), Quaternion.Identity);
        float balanced = FireControl.Solve(Shooter(2), target, 10).ErrorMeters;
        float sharp = FireControl.Solve(Shooter(4), target, 10).ErrorMeters;
        Require(Near(balanced / sharp, 1.5f, 0.01f), $"4 sensor pips must cut fire-control error by 1.5x: {balanced / sharp:0.000}");
    }

    private static void CheckBrownout()
    {
        var world = new SimWorld();
        ShipBody ship = Create(HullKind.Battleship, world);
        ModuleState reactor = ship.Damage.Module("reactor-main");
        ship.Damage.Hurt(reactor, reactor.Health, 0, 0, 1);
        ship.Control = new ShipControl { Thrust = new Vector3(0, 0, 1), FlightAssist = false };
        ship.Damage.AbsorbShield(ship.Damage.Shield, 0);
        for (int i = 0; i < 60 * 6; i++)
        {
            if (ship.Railgun!.Ready) world.FireRailgun(ship, ship.Forward);
            world.Step();
        }
        Require(ship.Power.DemandMw > ship.Power.AvailableMw && ship.Power.Supply < 1f
            && Near(ship.Power.Supply, ship.Power.AvailableMw / ship.Power.DemandMw),
            "Losing the main reactor under full activity must brown out every channel proportionally");
        Require(ship.Power.EngineEffect < 1f && ship.Power.ShieldEffect < 1f, "Brownout must reduce channel effects");
        Console.WriteLine($"Brownout (BB, main reactor lost, full activity): supply {ship.Power.Supply:P0}, {ship.Power.AvailableMw:0}/{ship.Power.DemandMw:0} MW");
    }

    private static void CheckHeat()
    {
        // 부스트만 계속하면 냉각이 감당한다.
        ShipBody cruiser = Create(HullKind.Interceptor);
        cruiser.Control = new ShipControl { Thrust = new Vector3(0, 0, 1), Boost = true, FlightAssist = false };
        Steps(cruiser, 60 * 60);
        Require(!cruiser.Power.Overheated, $"Boost alone must be sustainable: heat {cruiser.Power.HeatFraction:P0}");

        // 부스트 + 연사 + 실드 재충전을 겹치면 과열된다.
        var world = new SimWorld();
        ShipBody ship = Create(HullKind.Interceptor, world);
        ship.Power.SetPips(3, 1, 4, 0);
        ship.Control = new ShipControl { Thrust = new Vector3(0, 0, 1), Boost = true, FlightAssist = false };
        double overheatAt = -1;
        for (int i = 0; i < 60 * 90 && overheatAt < 0; i++)
        {
            if (ship.Railgun!.Ready) world.FireRailgun(ship, ship.Forward);
            if (i % 120 == 0) ship.Damage.AbsorbShield(5, world.Time);
            world.Step();
            if (ship.Power.Overheated) overheatAt = world.Time;
        }
        Require(overheatAt > 0, "Boost, continuous fire and shield use together must overheat");
        float engineHot = ship.Power.EngineEffect;
        Require(!ship.Railgun!.Ready && ship.Railgun.Status.Contains("과열"), "Overheat must block railgun fire");
        Require(Near(engineHot, 1.25f * ShipPower.OverheatThrottle * ship.Power.Supply, 0.01f), "Overheat must halve channel effects");
        Require(ship.Damage.Reports.Any(r => r.Message.Contains("과열")), "Overheat must be reported");

        // 손을 떼면 식고, 70% 아래에서 회복한다.
        ship.Control = new ShipControl { FlightAssist = false };
        double recoverAt = -1;
        for (int i = 0; i < 60 * 120 && recoverAt < 0; i++)
        {
            world.Step();
            if (!ship.Power.Overheated) recoverAt = world.Time;
        }
        Require(recoverAt > 0 && ship.Power.HeatFraction <= ShipPower.RecoverHeatFraction + 0.01f, "Idle cooling must recover below 70% heat");
        Require(ship.Damage.Reports.Any(r => r.Message.Contains("냉각 회복")), "Recovery must be reported");
        Console.WriteLine($"Heat (IC, boost+fire+shields): overheat at {overheatAt:0.0}s, recovered {recoverAt - overheatAt:0.0}s later");
    }

    private static void CheckCoolingLoss()
    {
        ShipBody ship = Create(HullKind.Interceptor);
        foreach (string id in new[] { "cooling-port", "cooling-starboard" })
        {
            ModuleState m = ship.Damage.Module(id);
            ship.Damage.Hurt(m, m.Health, 0, 0, 1);
        }
        int ticks = 0;
        while (!ship.Power.Overheated && ticks < 60 * 120) { ship.Step(SimWorld.TickDelta); ticks++; }
        Require(ship.Power.Overheated, "Without cooling even idle draw must eventually overheat");
        Console.WriteLine($"Cooling lost (IC idle): overheat at {ticks / 60.0:0.0}s");
    }
}
