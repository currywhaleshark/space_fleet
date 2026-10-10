using Godot;
using SpaceFleet.Sim;

/// <summary>
/// 포탑 추적: 가로지르는 표적을 포탑 구동 속도 안에서는 따라가며 쏘고, 넘으면 못 쏜다.
/// 전함 주포는 호위함을 따라가지만 근거리로 스쳐가는 요격함은 놓친다. 호위함 포탑은 요격함도 따라간다.
/// </summary>
static class TrackingChecks
{
    private static int _checks;
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); _checks++; }

    /// <summary>
    /// 사수 앞 distance에서 lateral 속도로 가로지르는 표적. 가장 가까운 지점 앞뒤 ±0.6·distance 구간(각속도가 가장 큰 구간)에서
    /// 쏜 발 수를 센다. 시뮬레이션의 포탑 구동만 시험하도록 선행 조준은 실제 위치·속도로 계산한다.
    /// </summary>
    public static (int Pass, int Total) Cross(ShipClass shooterClass, ShipClass targetClass, double distance, float lateral)
    {
        var world = new SimWorld();
        var shooter = world.Add(new ShipBody("S", shooterClass, Faction.Blue));
        shooter.Place(Vec3d.Zero, Quaternion.Identity);
        shooter.Control = new ShipControl { FlightAssist = false };
        var target = world.Add(new ShipBody("T", targetClass, Faction.Red));
        double start = -distance * 3;
        target.Place(new Vec3d(start, 0, -distance), Quaternion.Identity);
        target.Velocity = Vector3.Right * lateral;
        target.Control = new ShipControl { FlightAssist = false };
        int pass = 0, total = 0;
        double seconds = distance * 6 / lateral;
        for (int tick = 0; tick < seconds * SimWorld.TickRate; tick++)
        {
            foreach (RailgunState gun in shooter.Railguns)
            {
                double flight = (target.Position - gun.MuzzlePosition).Length() / gun.Definition.MuzzleSpeed;
                Vec3d aim = target.Position + Vec3d.From(target.Velocity * (float)flight);
                FireAttempt shot = world.FireRailgun(shooter, gun, (aim - gun.MuzzlePosition).ToVector3());
                if (!shot.Fired) continue;
                total++;
                if (Math.Abs(target.Position.X) < distance * 0.6) pass++;
            }
            world.Step();
        }
        return (pass, total);
    }

    /// <summary>실제 사격통제(센서 추적·관측 오차) 경로로 쏠 때 상태 분포. 진단용.</summary>
    public static void Probe()
    {
        foreach (var (cls, tcls, d, v) in new[] { (ShipClass.Battleship, ShipClass.Escort, 20000.0, 150f), (ShipClass.Escort, ShipClass.Escort, 15000.0, 150f), (ShipClass.Battleship, ShipClass.Battleship, 60000.0, 60f) })
        {
            var world = new SimWorld();
            var shooter = world.Add(new ShipBody("S", cls, Faction.Blue));
            shooter.Place(Vec3d.Zero, Quaternion.Identity); shooter.Control = new ShipControl { FlightAssist = false };
            var target = world.Add(new ShipBody("T", tcls, Faction.Red));
            target.Place(new Vec3d(-2000, 0, -d), Quaternion.Identity); target.Velocity = Vector3.Right * v;
            target.Control = new ShipControl { FlightAssist = false };
            shooter.Gunnery = new GunneryOrder { Doctrine = FireDoctrine.Focus, Target = target };
            var counts = new Dictionary<GunneryStatus, int>();
            for (int i = 0; i < 60 * 40; i++) { world.Step(); counts[shooter.Gunnery.Status] = counts.GetValueOrDefault(shooter.Gunnery.Status) + 1; }
            Console.WriteLine($"Probe {cls.Kind}->{tcls.Kind} {d / 1000:0} km {v} m/s: shots {shooter.Railguns.Sum(g => (int)g.ShotCount)} | " + string.Join(" ", counts.OrderByDescending(c => c.Value).Select(c => $"{c.Key}:{c.Value}")));
        }
    }

    public static void Run()
    {
        var bbOnDd = Cross(ShipClass.Battleship, ShipClass.Escort, 3000, 200);
        var bbOnIc = Cross(ShipClass.Battleship, ShipClass.Interceptor, 1500, 400);
        var ddOnIc = Cross(ShipClass.Escort, ShipClass.Interceptor, 1500, 400);
        var bbOnDdFar = Cross(ShipClass.Battleship, ShipClass.Escort, 12000, 200);
        Console.WriteLine($"Tracking: BB->DD 3 km 200 m/s pass {bbOnDd.Pass} (total {bbOnDd.Total}), BB->IC 1.5 km 400 m/s pass {bbOnIc.Pass} (total {bbOnIc.Total}), "
            + $"DD->IC 1.5 km 400 m/s pass {ddOnIc.Pass} (total {ddOnIc.Total}), BB->DD 12 km pass {bbOnDdFar.Pass}");
        Require(bbOnDd.Pass >= 2, "Battleship guns must track an escort crossing at brawl range");
        Require(bbOnDdFar.Pass >= 2, "Battleship guns must track an escort at gunline range");
        Require(bbOnIc.Pass == 0, "Battleship guns must not track an interceptor sweeping past at close range");
        Require(ddOnIc.Pass >= 2, "Escort turrets must track a passing interceptor");
        Console.WriteLine($"PASS: {_checks} tracking checks");
    }
}
