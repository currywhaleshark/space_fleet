using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Sim;

/// <summary>5b단계 미사일·디코이·근접방어 검증.</summary>
static class MissileChecks
{
    private static int _checks;

    public static void Run()
    {
        CheckStationaryHit();
        CheckCrossingHit();
        CheckPointDefense();
        CheckDecoys();
        CheckGroupDefense();
        CheckLaunchRules();
        CheckLargeCoordinates();
        Console.WriteLine($"PASS: {_checks} missile/PD/decoy checks");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    /// <summary>데이터 변형: 근접방어·디코이를 빼거나 치명 판정을 끈 함선(결과를 운에 덜 흔들리게).</summary>
    private static ShipDefinition Variant(string name, bool pointDefense = true, bool decoys = true)
    {
        using var stream = typeof(ShipDefinitions).Assembly.GetManifestResourceStream($"SpaceFleet.data.ships.{name}.json")!;
        using var reader = new StreamReader(stream);
        JsonObject data = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
        if (!pointDefense) data.Remove("pointDefense");
        if (!decoys) data.Remove("decoys");
        foreach (JsonNode? m in data["modules"]!.AsArray()) m!["criticalChance"] = 0;
        return ShipDefinition.Parse(data.ToJsonString());
    }

    private static ShipBody Add(SimWorld world, ShipDefinition def, Faction faction, Vec3d position, string callsign)
    {
        var ship = world.Add(new ShipBody(callsign, def.Flight, faction, def));
        ship.Place(position, Quaternion.Identity);
        ship.Control = new ShipControl { FlightAssist = false };
        return ship;
    }

    /// <summary>미사일이 모두 사라질 때까지(또는 limit초) 진행하며 결과를 센다.</summary>
    private static (int Hits, int Intercepted, int Wasted, double Seconds) Fly(SimWorld world, ShipBody target, double limit,
        Action<int>? everyTick = null)
    {
        int intercepted = 0, wasted = 0;
        var seen = new HashSet<(OrdnanceEventKind, double)>();
        // 명중은 위치가 아니라 실제로 표적에 피해 계산이 들어간 폭발(Impacts)로 센다.
        // 디코이에 끌려간 미사일은 함선 수십 m 옆에서 터져도 피해가 없다.
        var hitIds = new HashSet<uint>();
        int tick = 0;
        while (tick < limit * 60 && (world.Missiles.Count > 0 || tick == 0))
        {
            everyTick?.Invoke(tick);
            world.Step();
            tick++;
            foreach (ProjectileImpact impact in world.Impacts)
                if (impact.Hit.Target == target)
                    hitIds.Add(impact.Id);
            foreach (OrdnanceEvent e in world.OrdnanceEvents)
                if (seen.Add((e.Kind, e.Time)))
                {
                    if (e.Kind == OrdnanceEventKind.Intercepted) intercepted++;
                    else wasted++;
                }
        }
        return (hitIds.Count, intercepted, wasted - hitIds.Count, tick / 60.0);
    }

    private static void CheckStationaryHit()
    {
        var world = new SimWorld();
        ShipBody dd = Add(world, Variant("escort"), Faction.Blue, Vec3d.Zero, "DD");
        ShipBody bb = Add(world, Variant("battleship", pointDefense: false), Faction.Red, new Vec3d(0, 0, -60_000), "BB");
        world.Step();
        float shield = bb.Damage.Shield;
        Require(world.LaunchMissile(dd, bb).Fired, "A locked escort must launch at a 60 km battleship");
        var (hits, _, _, seconds) = Fly(world, bb, 120);
        Require(hits == 1 && bb.Damage.Shield < shield, "A missile must reach and damage a stationary battleship");
        Console.WriteLine($"Missile DD->BB 60 km stationary: impact after {seconds:0.0}s");
    }

    private static void CheckCrossingHit()
    {
        var world = new SimWorld();
        ShipBody dd = Add(world, Variant("escort"), Faction.Blue, Vec3d.Zero, "DD");
        ShipBody target = Add(world, Variant("escort", pointDefense: false, decoys: false), Faction.Red, new Vec3d(0, 0, -30_000), "T");
        target.Velocity = Vector3.Right * 200f;
        world.Step();
        Require(world.LaunchMissile(dd, target).Fired, "Launch at a crossing escort");
        var (hits, _, _, seconds) = Fly(world, target, 120, _ => target.Velocity = Vector3.Right * 200f);
        Require(hits == 1, "Proportional navigation must hit a 200 m/s crossing escort at 30 km");
        Console.WriteLine($"Missile DD->DD 30 km crossing 200 m/s: impact after {seconds:0.0}s");
    }

    private static (int Hits, int Intercepted) Salvo(bool pointDefense, bool useDecoys, int count = 6)
    {
        var world = new SimWorld();
        ShipBody shooter = Add(world, Variant("battleship"), Faction.Blue, Vec3d.Zero, "S");
        ShipBody target = Add(world, Variant("escort", pointDefense, decoys: useDecoys), Faction.Red, new Vec3d(0, 0, -40_000), "T");
        world.Step();
        int launched = 0;
        var (hits, intercepted, _, _) = Fly(world, target, 240, tick =>
        {
            if (launched < count && shooter.Ordnance.MissileReady && world.LaunchMissile(shooter, target).Fired)
                launched++;
            // 디코이: 미사일이 8 km 안에 들어오면 쿨다운마다 사출.
            if (useDecoys && target.Ordnance.DecoyReady
                && world.Missiles.Any(m => (m.Position - target.Position).Length() < 8000))
                world.LaunchDecoys(target);
        });
        Require(launched == count, "Salvo must launch every missile");
        return (hits, intercepted);
    }

    private static void CheckPointDefense()
    {
        var bare = Salvo(pointDefense: false, useDecoys: false);
        var defended = Salvo(pointDefense: true, useDecoys: false);
        Require(bare.Hits == 6 && bare.Intercepted == 0, $"Without point defense all six missiles hit: {bare.Hits}");
        Require(defended.Intercepted >= 1 && defended.Hits < bare.Hits, $"Point defense must shoot some missiles down: {defended.Intercepted} intercepted, {defended.Hits} hit");
        Console.WriteLine($"Salvo BB->DD x6 at 40 km: no PD {bare.Hits} hits / PD {defended.Intercepted} intercepted, {defended.Hits} hits");
    }

    private static void CheckDecoys()
    {
        var bare = Salvo(pointDefense: false, useDecoys: false);
        var decoyed = Salvo(pointDefense: false, useDecoys: true);
        Require(decoyed.Hits < bare.Hits, $"Decoys must pull some missiles away: {decoyed.Hits} vs {bare.Hits} hits");
        Console.WriteLine($"Salvo BB->DD x6 at 40 km: decoys {decoyed.Hits} hits (no decoys {bare.Hits})");
    }

    /// <summary>호위함 4척이 2발씩(8발) 동시에 전투단(전함+호위함 2)을 노린다. 막을 수는 있지만 다 막지는 못해야 한다.</summary>
    private static void CheckGroupDefense()
    {
        var world = new SimWorld();
        ShipBody bb = Add(world, Variant("battleship"), Faction.Red, Vec3d.Zero, "BB");
        Add(world, Variant("escort"), Faction.Red, new Vec3d(-1600, 0, -600), "D1");
        Add(world, Variant("escort"), Faction.Red, new Vec3d(1600, 0, -600), "D2");
        var shooters = Enumerable.Range(0, 4)
            .Select(i => Add(world, Variant("escort"), Faction.Blue, new Vec3d(-6000 + i * 4000, 0, -60_000), $"S{i}")).ToList();
        world.Step();
        var launched = new Dictionary<ShipBody, int>();
        var (hits, intercepted, _, _) = Fly(world, bb, 240, tick =>
        {
            foreach (ShipBody s in shooters)
                if (launched.GetValueOrDefault(s) < 2 && s.Ordnance.MissileReady && world.LaunchMissile(s, bb).Fired)
                    launched[s] = launched.GetValueOrDefault(s) + 1;
        });
        Require(launched.Values.Sum() == 8, "All eight missiles must launch");
        Require(intercepted >= 2 && hits >= 2, $"A battle group must stop some but not all of an 8-missile salvo: {intercepted} intercepted, {hits} hit");
        Console.WriteLine($"Group defense (BB+2DD vs 4xDD salvo of 8, 60 km): {intercepted} intercepted, {hits} hit");
    }

    private static void CheckLaunchRules()
    {
        var world = new SimWorld();
        ShipBody bb = Add(world, Variant("battleship"), Faction.Blue, Vec3d.Zero, "B");
        ShipBody ic = Add(world, Variant("interceptor"), Faction.Red, new Vec3d(0, 0, -100_000), "I");
        ShipBody dd = Add(world, Variant("escort"), Faction.Red, new Vec3d(0, 0, -40_000), "D");
        world.Step();
        Require(!world.LaunchMissile(bb, ic).Fired, "An undetected target cannot be engaged");
        Require(world.LaunchMissile(bb, dd).Fired && !world.LaunchMissile(bb, dd).Fired, "Reload must block an immediate second launch");
        Require(!world.LaunchMissile(bb, bb).Fired, "A ship cannot target itself or friends");
        ModuleState mag = bb.Damage.Module("magazine-port");
        bb.Damage.Hurt(mag, mag.Health, 0, 0, 1);
        ModuleState mag2 = bb.Damage.Module("magazine-starboard");
        bb.Damage.Hurt(mag2, mag2.Health, 0, 0, 1);
        for (int i = 0; i < 600; i++) world.Step();
        Require(!world.LaunchMissile(bb, dd).Fired && bb.Ordnance.MissileStatus.Contains("탄약고"), "Destroyed magazines must stop launches");
        world.ResetWeapons();
        Require(world.Missiles.Count == 0 && bb.Ordnance.Missiles == bb.Definition.Missiles!.Rounds, "Weapon reset must clear missiles and restore rounds");
    }

    private static void CheckLargeCoordinates()
    {
        var world = new SimWorld();
        var far = new Vec3d(1e9, -2e9, 3e9);
        ShipBody dd = Add(world, Variant("escort"), Faction.Blue, far, "DD");
        ShipBody bb = Add(world, Variant("battleship", pointDefense: false), Faction.Red, far + new Vec3d(0, 0, -30_000), "BB");
        world.Step();
        Require(world.LaunchMissile(dd, bb).Fired, "Launch at large coordinates");
        Require(Fly(world, bb, 120).Hits == 1, "Missiles must hit at 1e9 m world coordinates");
    }
}
