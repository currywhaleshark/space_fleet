using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Sim;

static class DamageChecks
{
    private static int _checks;
    private static readonly DamagePacket Standard = new(600, 1200, 120);

    public static void Run()
    {
        CheckDefinitions();
        CheckShield();
        CheckArmor();
        CheckInternalPath();
        CheckSystems();
        CheckCriticalAndRepair();
        CheckWorldAndCoordinates();
        CheckCollisionDamage();
        Console.WriteLine($"PASS: {_checks} damage/data checks");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    private static bool Near(float a, float b, float tolerance = 0.001f) => Math.Abs(a - b) < tolerance;

    private static JsonObject Data(string name = "interceptor")
    {
        using var stream = typeof(ShipDefinitions).Assembly.GetManifestResourceStream($"SpaceFleet.data.ships.{name}.json")!;
        using var reader = new StreamReader(stream);
        return JsonNode.Parse(reader.ReadToEnd())!.AsObject();
    }

    private static ShipBody Create(ShipDefinition? definition = null, string callsign = "TEST")
    {
        definition ??= ShipDefinitions.For(HullKind.Interceptor);
        var ship = new ShipBody(callsign, definition.Flight, Faction.Blue, definition);
        ship.Place(Vec3d.Zero, Quaternion.Identity);
        ship.Control = new ShipControl { FlightAssist = false };
        return ship;
    }

    private static ShipDefinition Fixture(float armor = 0, float shield = 0, string? modules = null)
    {
        JsonObject data = Data();
        data.Remove("railgun");
        data["shield"] = JsonNode.Parse("""{"capacity":0,"rechargePerSecond":20,"rechargeDelay":2}""");
        data["shield"]!["capacity"] = shield;
        data["hullSections"] = JsonNode.Parse("""
            [{"id":"hull","name":"시험 선체","center":[0,0,0],"halfSize":[10,10,10],"armor":{"thicknessMm":0}}]
            """);
        data["hullSections"]![0]!["armor"]!["thicknessMm"] = armor;
        data["modules"] = JsonNode.Parse(modules ?? """
            [{"id":"sensor","name":"시험 센서","kind":"Sensor","center":[0,0,0],"halfSize":[1,1,1],"hitPoints":1000,"resistanceMm":0}]
            """);
        return ShipDefinition.Parse(data.ToJsonString());
    }

    private static void Reject(Action<JsonObject> mutate, string reason)
    {
        JsonObject data = Data();
        mutate(data);
        bool rejected = false;
        try { ShipDefinition.Parse(data.ToJsonString()); }
        catch (Exception e) when (e is InvalidDataException or JsonException) { rejected = true; }
        Require(rejected, reason);
    }

    private static void Destroy(ShipBody ship, string module, float energy = 0, uint sequence = 1)
    {
        ModuleState state = ship.Damage.Module(module);
        ship.Damage.Hurt(state, state.Health, 1, energy, sequence);
    }

    private static ShotResult Fire(ShipBody ship, DamagePacket? packet = null, Vector3? direction = null)
    {
        Vector3 d = direction ?? Vector3.Back;
        return DamageRay.Apply(ship, Vec3d.From(-d * 30), d, packet ?? Standard, 1, 1);
    }

    private static void CheckDefinitions()
    {
        foreach (HullKind kind in Enum.GetValues<HullKind>())
        {
            ShipDefinition def = ShipDefinitions.For(kind);
            ShipBody ship = Create(def);
            Require(def.Kind == def.Flight.Kind && def.Hull.Boxes.Count == def.HullSections.Length, "Data must drive the collision hull");
            Require(Near(ship.Damage.PowerFraction, 1) && Near(ship.Damage.PropulsionFraction, 1)
                && Near(ship.Damage.ManeuverFraction, 1) && Near(ship.Damage.WeaponsFraction, 1)
                && Near(ship.Damage.SensorFraction, 1) && Near(ship.Damage.CoolingFraction, 1), "Healthy ships must have full systems");
            Require(Near(ship.Damage.Shield, def.Shield.Capacity), "Healthy shield must start full");
        }
        Reject(d => d["flight"]!["massKg"] = -1, "Invalid mass must be rejected");
        Reject(d => d["modules"]![1]!["id"] = d["modules"]![0]!["id"]!.GetValue<string>(), "Duplicate module IDs must be rejected");
        Reject(d => d["modules"]![0]!["center"] = JsonNode.Parse("[100000,0,0]"), "Modules outside the hull must be rejected");
        Reject(d => d["hullSections"]![0]!["armor"]!["slopeDegrees"] = 90, "Invalid armor slope must be rejected");
        Reject(d => d["modules"]![0]!["criticalChance"] = 1.1, "Invalid critical probability must be rejected");
        Reject(d => d["modules"]![0]!["kind"] = "Unknown", "Unknown module kinds must be rejected");
        Reject(d => d["modules"]![0]!["halfSize"] = JsonNode.Parse("[0,1,1]"), "Zero-size module must be rejected");
        Reject(d => d["flight"] = null, "Null flight data must be rejected");
        Reject(d => d["modules"]![0] = null, "Null module entries must be rejected");
        Reject(d => d["modules"]![0]!["visualEngineIndex"] = 0, "A visual engine index must reference a thruster");
        foreach (DamagePacket packet in new[] { Standard with { Energy = 0 }, Standard with { Range = float.NaN }, Standard with { PenetrationMm = -1 } })
        {
            bool rejected = false;
            try { packet.Validate(); } catch (ArgumentException) { rejected = true; }
            Require(rejected, "Invalid damage packet must be rejected");
        }
    }

    private static void CheckShield()
    {
        var full = Create(Fixture(shield: 600));
        ShotResult result = Fire(full);
        Require(result.ShieldStopped && result.Modules.Count == 0, "Full shield must stop internal damage");
        Require(Near(full.Damage.Shield, 0) && Near(full.Damage.Module("sensor").Health, 1000), "Shield absorption must not damage a module");
        full.Damage.Step(1.9);
        Require(Near(full.Damage.Shield, 0), "Shield must wait for its recharge delay");
        full.Damage.Step(0.6);
        Require(Near(full.Damage.Shield, 10), "Only the part of a tick after recharge delay may recharge");
        full.Damage.Step(100);
        Require(Near(full.Damage.Shield, 600), "Shield recharge must stop at capacity");
        var partial = Create(Fixture(armor: 100, shield: 300));
        var bare = Create(Fixture(armor: 100));
        ShotResult reduced = Fire(partial), unreduced = Fire(bare);
        Require(!reduced.ShieldStopped && Near(partial.Damage.Shield, 0) && reduced.Modules.Count == 1, "Partial shield must pass residual energy to armor");
        Require(reduced.Modules[0].Damage < unreduced.Modules[0].Damage * 0.5f, "Shield must reduce subsequent penetration and module damage");
        partial.Damage.Step(1);
        Fire(partial);
        partial.Damage.Step(1.5);
        Require(Near(partial.Damage.Shield, 0), "A new hit must restart recharge delay");
        var production = Create();
        Destroy(production, "shield");
        production.Damage.Step(10);
        Require(Near(production.Damage.ShieldCapacity, 0) && Near(production.Damage.Shield, 0), "Destroyed emitter must prevent shields from recovering");
    }

    private static void CheckArmor()
    {
        var packet = new DamagePacket(500, 110, 100);
        var normal = Create(Fixture(100));
        var angled = Create(Fixture(100));
        ShotResult n = Fire(normal, packet);
        ShotResult a = Fire(angled, packet, new Vector3(0.5f, 0, 0.8660254f));
        Require(n.Modules.Count == 1 && n.Modules[0].Damage > 0, "Normal incidence must penetrate this plate");
        Require(a.ArmorStopped && a.Modules.Count == 0, "Oblique incidence must increase effective thickness");
        Require(n.ArmorStopped && n.Summary.Contains("출구"), "Exit armor must also consume remaining penetration");
        JsonObject data = Data();
        data.Remove("railgun");
        // Keep fixture geometry and change only the authored armor/face data.
        data["shield"]!["capacity"] = 0;
        data["hullSections"] = JsonNode.Parse("""
            [{"id":"hull","name":"시험 선체","center":[0,0,0],"halfSize":[10,10,10],
              "armor":{"thicknessMm":50,"slopeDegrees":60},"faces":{"Fore":{"thicknessMm":200},"Aft":{"thicknessMm":20}}}]
            """);
        data["modules"] = JsonNode.Parse("""
            [{"id":"sensor","name":"시험 센서","kind":"Sensor","center":[0,0,0],"halfSize":[1,1,1],"hitPoints":1000,"resistanceMm":0}]
            """);
        var front = Create(ShipDefinition.Parse(data.ToJsonString()));
        var aft = Create(front.Definition);
        var side = Create(front.Definition);
        Require(Fire(front, packet).Modules.Count == 0, "Fore plate override must apply to a fore hit");
        Require(Fire(aft, packet, Vector3.Forward).Modules.Count == 1, "Aft plate override must permit the weaker aft hit");
        Require(Fire(side, packet with { PenetrationMm = 90 }, Vector3.Right).Modules.Count == 0, "Authored slope must increase side armor thickness");
    }

    private static void CheckInternalPath()
    {
        const string layers = """
            [{"id":"front","name":"앞 모듈","kind":"Sensor","center":[0,0,-4],"halfSize":[1,1,1],"hitPoints":1000,"resistanceMm":200},
             {"id":"rear","name":"뒤 모듈","kind":"Gun","center":[0,0,4],"halfSize":[1,1,1],"hitPoints":1000,"resistanceMm":0}]
            """;
        var ship = Create(Fixture(modules: layers));
        ShotResult both = Fire(ship, new DamagePacket(500, 300, 100));
        Require(both.Modules.Count == 2 && both.Modules[0].Id == "front" && both.Modules[1].Id == "rear", "Internal modules must be hit in travel order");
        Require(both.Modules[1].Damage < both.Modules[0].Damage, "The front module must attenuate damage to the rear");
        var stopped = Create(ship.Definition);
        ShotResult firstOnly = Fire(stopped, new DamagePacket(500, 100, 100));
        Require(firstOnly.Modules.Count == 1 && firstOnly.Summary.Contains("내부"), "Internal resistance must stop penetration before the rear module");
        Require(Near(stopped.Damage.Module("rear").Health, 1000), "A stopped ray must leave the rear module intact");
        var shortRange = Create(ship.Definition);
        ShotResult shortShot = Fire(shortRange, Standard with { Range = 28 });
        Require(shortShot.Modules.Count == 1, "Range ending inside the hull must exclude farther modules");
        ShotResult miss = DamageRay.Apply(ship, new Vec3d(50, 0, -30), Vector3.Back, Standard, 2, 2);
        Require(miss.Target is null && miss.Modules.Count == 0, "A ray outside all hull sections must miss");
        var internalShot = Create(Fixture(armor: 100, shield: 600));
        ShotResult inside = DamageRay.Apply(internalShot, Vec3d.Zero, Vector3.Back, Standard, 1, 1);
        Require(inside.Modules.Count == 1 && Near(internalShot.Damage.Shield, 600), "A ray originating inside must not charge exterior shield or entry armor");
        JsonObject overlapData = Data();
        overlapData.Remove("railgun");
        overlapData["shield"]!["capacity"] = 0;
        overlapData["hullSections"] = JsonNode.Parse("""
            [{"id":"a","name":"A","center":[0,0,0],"halfSize":[10,10,10],"armor":{"thicknessMm":100}},
             {"id":"b","name":"B","center":[0,0,2],"halfSize":[10,10,10],"armor":{"thicknessMm":100}}]
            """);
        overlapData["modules"] = JsonNode.Parse("""
            [{"id":"sensor","name":"시험 센서","kind":"Sensor","center":[0,0,0],"halfSize":[1,1,1],"hitPoints":1000,"resistanceMm":0}]
            """);
        ShotResult overlap = Fire(Create(ShipDefinition.Parse(overlapData.ToJsonString())), Standard with { PenetrationMm = 150 });
        Require(overlap.Modules.Count == 1, "Overlapping hull sections must not count a hidden second entry plate");
    }

    private static void CheckSystems()
    {
        var bb = Create(ShipDefinitions.For(HullKind.Battleship));
        Destroy(bb, "bus-port");
        Require(Near(bb.Damage.GridPower(PowerGrid.Port), 0) && Near(bb.Damage.GridPower(PowerGrid.Starboard), 1), "Port bus failure must preserve the starboard grid");
        Require(Near(bb.Damage.PowerFraction, 0.5f) && Near(bb.Damage.PropulsionFraction, 0.5f) && Near(bb.Damage.ManeuverFraction, 0.5f), "Bus failure must disable consumers on only that side");
        Require(Near(bb.Damage.EngineFraction(0), 0) && Near(bb.Damage.EngineFraction(1), 1), "Visual engine state must follow the supplying grid");
        Require(bb.Damage.Reports.Any(r => r.Message.Contains("좌현 전력망 단절")), "Bus failure must produce a specific damage report");
        Require(Near(bb.Damage.SensorFraction, 1), "Shared consumers must draw from the surviving grid");

        // 반응로가 숨은 HP 막대가 되지 않아야 한다: 중간 피해는 아무 계통도 약화시키지 않는다.
        var margin = Create(bb.Definition);
        ModuleState main = margin.Damage.Module("reactor-main");
        margin.Damage.Hurt(main, main.Definition.HitPoints * 0.4f, 1, 0, 1);
        Require(Near(margin.Damage.GenerationFraction, 1) && Near(margin.Damage.PowerFraction, 1) && Near(margin.Damage.PropulsionFraction, 1)
            && Near(margin.Damage.SensorFraction, 1) && Near(margin.Damage.ShieldCapacity, bb.Definition.Shield.Capacity),
            "A reactor at 60% must not degrade generation or any system");
        margin.Damage.Hurt(main, main.Definition.HitPoints * 0.2f, 1, 0, 1);
        Require(margin.Damage.Reports.Any(r => r.Message.Contains("출력 저하")), "Crossing the degrade threshold must report reduced output");
        // 발전 용량 비중: 보조 발전기 0.3 × 2, 주반응로 1, 보조 반응로 0.5 = 2.1
        Require(Near(margin.Damage.GenerationFraction, 1.6f / 2.1f), "A degraded main reactor must cost a step of generation, not its health percentage");
        Require(Near(margin.Damage.PowerFraction, 1), "Reduced generation must not disconnect any grid");
        var smallLoss = Create(bb.Definition);
        Destroy(smallLoss, "generator-port");
        Require(Near(smallLoss.Damage.GridPower(PowerGrid.Port), 1) && Near(smallLoss.Damage.GenerationFraction, 1.8f / 2.1f),
            "Losing one auxiliary generator keeps the grid connected and costs only its share");

        var redundant = Create(bb.Definition);
        Destroy(redundant, "reactor-main");
        Require(Near(redundant.Damage.GenerationFraction, 1.1f / 2.1f), "Backup reactor and generators must retain useful power");
        Destroy(redundant, "reactor-backup");
        Require(redundant.Damage.GenerationFraction > 0 && Near(redundant.Damage.PowerFraction, 1), "Generators must provide remaining power after both reactors fail");
        Destroy(redundant, "generator-port");
        Require(Near(redundant.Damage.GridPower(PowerGrid.Port), 0) && redundant.Damage.GridPower(PowerGrid.Starboard) > 0, "Generator redundancy must remain local to its feeds");
        Destroy(redundant, "generator-starboard");
        Require(Near(redundant.Damage.PowerFraction, 0) && Near(redundant.Damage.GenerationFraction, 0) && !redundant.Damage.Destroyed,
            "A blackout may leave a recoverable physical hull");
        var healthy = Create();
        var engineLoss = Create();
        Destroy(engineLoss, "engine-0");
        Require(Near(engineLoss.Damage.PropulsionFraction, 0.5f) && Near(engineLoss.Damage.EngineFraction(1), 1), "One engine failure must leave the other engine running");
        healthy.Control = engineLoss.Control = new ShipControl { Thrust = new Vector3(0, 0, 1), FlightAssist = false };
        healthy.Step(SimWorld.TickDelta); engineLoss.Step(SimWorld.TickDelta);
        Require(Near(engineLoss.Velocity.Length() / healthy.Velocity.Length(), 0.5f), "Engine damage must reduce actual acceleration");
        var blackout = Create();
        Destroy(blackout, "bus-port"); Destroy(blackout, "bus-starboard");
        blackout.Velocity = Vector3.Forward * 100;
        blackout.Control = new ShipControl { Thrust = Vector3.One, AimForward = Vector3.Right, FlightAssist = true };
        blackout.Step(SimWorld.TickDelta);
        Require(blackout.Velocity == Vector3.Forward * 100 && Near(blackout.AngularVelocity.Length(), 0), "Power loss must preserve inertia and prevent thrust and commanded rotation");
        var systems = Create();
        Destroy(systems, "sensor"); Destroy(systems, "gun");
        Require(Near(systems.Damage.SensorFraction, 0) && Near(systems.Damage.WeaponsFraction, 0), "Sensor and gun damage must disable their systems");
        var ammo = Create();
        Destroy(ammo, "magazine");
        Require(Near(ammo.Damage.WeaponsFraction, 0) && !ammo.Damage.Destroyed, "Noncritical ammo loss must disable weapons without killing the ship");
        var cooling = Create();
        Destroy(cooling, "cooling-port"); Destroy(cooling, "cooling-starboard");
        Require(Near(cooling.Damage.CoolingFraction, 0) && Near(cooling.Power.CoolingMw, 0), "Both cooling modules must disable heat removal");
        // 냉각 손상은 추력을 직접 깎지 않는다. 대가는 열이 빠지지 않는 것이다(PowerChecks).
        cooling.Control = healthy.Control with { Boost = true };
        healthy.Place(Vec3d.Zero, Quaternion.Identity);
        healthy.Control = cooling.Control;
        cooling.Step(SimWorld.TickDelta); healthy.Step(SimWorld.TickDelta);
        Require(Near(cooling.Velocity.Length(), healthy.Velocity.Length()), "Loss of cooling must not directly remove thrust or boost");
    }

    private static void CheckCriticalAndRepair()
    {
        foreach (string kind in new[] { "Magazine", "Reactor" })
        {
            string modules = $$"""
                [{"id":"critical","name":"치명 모듈","kind":"{{kind}}","center":[0,0,0],"halfSize":[1,1,1],"hitPoints":10,"resistanceMm":0,"criticalChance":1,"criticalEnergy":400},
                 {"id":"spare","name":"생존 모듈","kind":"Sensor","center":[3,0,0],"halfSize":[1,1,1],"hitPoints":100,"resistanceMm":0}]
                """;
            var low = Create(Fixture(modules: modules));
            Destroy(low, "critical", energy: 399);
            Require(!low.Damage.Destroyed && Near(low.Damage.Module("spare").Health, 100), "Low-energy destruction must not trigger a critical event");
            var critical = Create(low.Definition);
            Destroy(critical, "critical", energy: 400);
            Require(critical.Damage.Destroyed && critical.Damage.Modules.All(m => m.Destroyed), "A guaranteed critical event must destroy the hull and all modules");
            Require(Near(critical.Damage.PowerFraction, 0) && Near(critical.Damage.PropulsionFraction, 0), "Critical destruction must leave no powered systems");
            Require(critical.Damage.Reports.Any(r => r.Message.Contains(kind == "Magazine" ? "유폭" : "폭주")), "Critical report must identify the failure type");
            critical.Damage.Reset();
            Require(!critical.Damage.Destroyed && critical.Damage.Modules.All(m => Near(m.HealthFraction, 1))
                && critical.Damage.Reports.Count == 0 && Near(critical.Damage.PowerFraction, 1), "Repair must fully reset health, critical status, and reports");
        }
        JsonObject data = Data();
        data["modules"]!.AsArray().First(m => m!["id"]!.GetValue<string>() == "magazine")!["criticalChance"] = 0.5;
        var deterministic = ShipDefinition.Parse(data.ToJsonString());
        var outcomes = new HashSet<bool>();
        for (uint seq = 1; seq <= 32; seq++)
        {
            ShipBody a = Create(deterministic, "SAME"), b = Create(deterministic, "SAME");
            Destroy(a, "magazine", 600, seq); Destroy(b, "magazine", 600, seq);
            Require(a.Damage.Destroyed == b.Damage.Destroyed, "Critical rolls must be deterministic for the same ship and shot sequence");
            outcomes.Add(a.Damage.Destroyed);
        }
        Require(outcomes.Count == 2, "Critical rolls must support both survival and destruction");
    }

    private static void CheckWorldAndCoordinates()
    {
        var definition = Fixture();
        var world = new SimWorld();
        ShipBody shooter = world.Add(Create(definition, "SHOOTER"));
        ShipBody near = world.Add(Create(definition, "NEAR"));
        ShipBody far = world.Add(Create(definition, "FAR"));
        near.Place(new(0, 0, 40), Quaternion.Identity);
        far.Place(new(0, 0, 80), Quaternion.Identity);
        ShotResult first = world.FireTestShot(shooter, Vec3d.Zero, Vector3.Back, Standard);
        Require(first.Target == near && first.Modules.Count == 1, "World ray must ignore the shooter and choose the nearest hull");
        Require(Near(far.Damage.Module("sensor").Health, 1000), "A nearer hull must block damage to farther ships");
        Destroy(near, "sensor");
        Require(world.FireTestShot(shooter, Vec3d.Zero, Vector3.Back, Standard).Target == near, "A wreck must remain a physical blocker");
        Require(world.FireTestShot(shooter, Vec3d.Zero, Vector3.Right, Standard).Target is null, "A world ray with no hull must miss");
        float expectedDamage = 0;
        foreach (Vec3d shift in new[] { Vec3d.Zero, new Vec3d(1e6, -2e6, 3e6), new Vec3d(1e12, -2e12, 3e12) })
        {
            var ship = Create(definition);
            Quaternion rotation = Quaternion.FromEuler(new Vector3(0.2f, 0.7f, 0.3f));
            ship.Place(shift, rotation);
            Vector3 direction = rotation * Vector3.Back;
            Vec3d origin = shift + Vec3d.From(rotation * new Vector3(0, 0, -30));
            ShotResult result = DamageRay.Apply(ship, origin, direction, Standard, 1, 1);
            Require(result.Modules.Count == 1 && Near(result.Distance, 20, 0.01f), "Rotated hull hits must work at large world coordinates");
            if (shift == Vec3d.Zero) expectedDamage = result.Modules[0].Damage;
            Require(Near(result.Modules[0].Damage, expectedDamage, 0.01f), "Floating-origin distance must not change module damage");
        }
    }

    private static void CheckCollisionDamage()
    {
        // 파쇄로 탄약고·반응로가 부서질 때의 낮은 확률 치명 판정이 결과를 흔들지 않게 치명 확률을 끈 요격함을 쓴다.
        JsonObject quiet = Data();
        foreach (JsonNode? m in quiet["modules"]!.AsArray())
            m!["criticalChance"] = 0;
        ShipDefinition interceptor = ShipDefinition.Parse(quiet.ToJsonString());

        // 정면 충돌: 같은 요격함 둘이 맞부딪힌다.
        (ShipBody A, ShipBody B) HeadOn(float speed)
        {
            var world = new SimWorld();
            ShipBody a = world.Add(Create(interceptor, "A")), b = world.Add(Create(interceptor, "B"));
            a.Place(new(0, 0, -100), Quaternion.Identity); b.Place(new(0, 0, 100), Quaternion.Identity);
            a.Velocity = Vector3.Back * speed; b.Velocity = Vector3.Forward * speed;
            for (int i = 0; i < 6000 && a.LastCollision is null; i++) world.Step();
            for (int i = 0; i < 10; i++) world.Step();
            return (a, b);
        }
        var gentle = HeadOn(1.5f);
        Require(gentle.A.LastCollision is not null, "Gentle head-on contact must still register");
        Require(gentle.A.Damage.Modules.All(m => Near(m.HealthFraction, 1)), "Gentle contact must not damage modules");
        var hard = HeadOn(50f);
        Require(hard.A.Damage.Modules.Any(m => m.HealthFraction < 1) && hard.B.Damage.Modules.Any(m => m.HealthFraction < 1),
            "A hard collision must damage both ships");
        Require(!hard.A.Damage.Destroyed && !hard.B.Damage.Destroyed, "A 100 m/s closing interceptor collision stays below breakup");
        var breakup = HeadOn(150f);
        Require(breakup.A.Damage.Destroyed && breakup.B.Damage.Destroyed
            && breakup.A.Damage.Reports.Any(r => r.Message.Contains("선체 붕괴")), "A 300 m/s closing interceptor collision must break both hulls");

        // 요격함이 전함 후미를 들이받는다: 가벼운 쪽은 붕괴, 무거운 쪽도 접촉 구획이 파쇄된다.
        ShipBody Ram(float speed, out ShipBody battleship)
        {
            var world = new SimWorld();
            ShipBody bb = world.Add(Create(ShipDefinitions.For(HullKind.Battleship), "BB"));
            ShipBody ic = world.Add(Create(interceptor, "IC"));
            ic.Place(new(0, 0, 700), Quaternion.Identity);
            ic.Velocity = Vector3.Forward * speed;
            for (int i = 0; i < 1200 && ic.LastCollision is null; i++) world.Step();
            for (int i = 0; i < 10; i++) world.Step();
            battleship = bb;
            return ic;
        }
        ShipBody kamikaze = Ram(380f, out ShipBody rammed);
        Require(kamikaze.Damage.Destroyed, "An interceptor ramming a battleship at 380 m/s must break up");
        Require(!rammed.Damage.Destroyed && rammed.Damage.Modules.Any(m => m.HealthFraction < 1),
            "The battleship must take local crush damage without being destroyed");
        ShipBody bump = Ram(60f, out ShipBody bumped);
        Require(!bump.Damage.Destroyed && bump.Damage.Modules.Any(m => m.HealthFraction < 1),
            "A 60 m/s ram must damage the interceptor without breakup");
        Require(bumped.Damage.Modules.All(m => Near(m.HealthFraction, 1)), "A 60 m/s interceptor ram must not crush battleship modules");
        Console.WriteLine($"Collision: 380 m/s ram → BB modules hit {rammed.Damage.Modules.Count(m => m.HealthFraction < 1)}, " +
            $"60 m/s ram → IC modules hit {bump.Damage.Modules.Count(m => m.HealthFraction < 1)}");
    }
}
