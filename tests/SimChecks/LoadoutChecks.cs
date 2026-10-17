using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Sim;

static class LoadoutChecks
{
    private static int _checks;
    private static void Check(bool valid, string message)
    { if (!valid) throw new Exception(message); _checks++; }

    public static void Run()
    {
        EmptyLoadouts();
        Composition();
        InvalidLoadouts();
        BaseRoles();
        LegacyMatchmaking();
        Console.WriteLine($"PASS: {_checks} loadout/role data/legacy equivalence checks");
    }

    private static void EmptyLoadouts()
    {
        foreach (var original in ShipDefinitions.All)
        {
            string before = original.ToJson(), source = ShipDefinitions.SourceJson(original.Id);
            var resolved = ShipLoadouts.Resolve(new(original.Id));
            var clone = resolved.Definition;
            Check(clone.ToJson() == before, $"{original.Id}: empty loadout preserves every definition value");
            Check(!ReferenceEquals(clone, original) && !ReferenceEquals(clone.Modules, original.Modules), "Loadout owns its definition arrays");
            Check(clone.Hull.Boxes.SequenceEqual(original.Hull.Boxes), "Derived collision boxes identical");
            Check(clone.ShieldEnvelope.Center == original.ShieldEnvelope.Center && clone.ShieldEnvelope.Radii == original.ShieldEnvelope.Radii,
                "Derived shield envelope identical");
            Check(JsonNode.DeepEquals(JsonNode.Parse(ShipDefinition.Parse(resolved.DefinitionJson).ToJson()), JsonNode.Parse(before)),
                "Resolved JSON round-trips to the same effective definition");
            var empty = ShipLoadouts.Resolve(new(original.Id, "{}", new[] { "{}" }));
            Check(resolved.LoadoutId == empty.LoadoutId && resolved.ContentHash == empty.ContentHash, "Empty patches preserve content identity");
            var a = new ShipBody("SAME", original, Faction.Blue);
            var b = new ShipBody("SAME", clone, Faction.Blue);
            for (int tick = 0; tick < 180; tick++)
            {
                a.Control = b.Control = new ShipControl { Thrust = new(.2f, -.1f, 1), Roll = .2f,
                    HelmForward = new Vector3(.1f, .05f, -1).Normalized(), FlightAssist = true, Boost = tick > 90 };
                a.Step(SimWorld.TickDelta); b.Step(SimWorld.TickDelta);
                Check(a.Position == b.Position && a.Velocity == b.Velocity && a.Orientation == b.Orientation,
                    "Empty loadout produces bit-identical flight");
            }
            a.Place(Vec3d.Zero, Quaternion.Identity); b.Place(Vec3d.Zero, Quaternion.Identity);
            for (uint shot = 1; shot <= 4; shot++)
            {
                var packet = new DamagePacket(50000, 5000, 500);
                var start = new Vec3d(0, 0, -original.Flight.Length * 2);
                DamageRay.Apply(a, start, Vector3.Back, packet, shot, shot);
                DamageRay.Apply(b, start, Vector3.Back, packet, shot, shot);
                Check(a.Damage.StructureHealth == b.Damage.StructureHealth && a.Damage.Destroyed == b.Damage.Destroyed
                    && a.Damage.Modules.Select(m => m.Health).SequenceEqual(b.Damage.Modules.Select(m => m.Health)),
                    "Empty loadout preserves penetration, modules, structure and critical outcome");
            }
            Check(original.ToJson() == before && ShipDefinitions.SourceJson(original.Id) == source, "Composition and combat never mutate catalog/source");
        }
    }

    private static void Composition()
    {
        var baseHull = ShipDefinitions.ById("escort");
        var resolved = ShipLoadouts.Resolve(new("escort",
            """{"id":"test-gun-cruiser","branch":"GunCruiser","shield":{"capacity":200},"ai":{"profile":{"standoffMeters":45000}}}""",
            new[] { """{"shield":{"capacity":300},"structureHitPoints":3000}""", """{"shield":{"capacity":350}}""" }));
        var def = resolved.Definition;
        Check(def.Id == "test-gun-cruiser" && def.Branch == ShipBranch.GunCruiser && def.Kind == HullKind.Escort,
            "Variant ID/branch are independent from physical hull kind");
        Check(def.ModelId == "escort", "Variant retains an independently selected model");
        Check(def.Shield.Capacity == 350 && def.StructureHitPoints == 3000 && def.Shield.RechargeDelay == baseHull.Shield.RechargeDelay,
            "Tree then ordered upgrades merge leaf values without losing siblings");
        var reordered = ShipLoadouts.Resolve(new("escort",
            """{"STRUCTUREHITPOINTS":3000.0,"SHIELD":{"CAPACITY":350.0},"AI":{"PROFILE":{"STANDOFFMETERS":45000}},"BRANCH":"GunCruiser","ID":"test-gun-cruiser"}"""));
        Check(resolved.ContentHash == reordered.ContentHash && resolved.LoadoutId == reordered.LoadoutId,
            "Effective identity ignores property order, case, whitespace and numeric spelling");
        Check(resolved.ContentHash.Length == 64 && resolved.LoadoutId.Contains(resolved.ContentHash), "Identity carries the full SHA-256 digest");
        Check(ShipLoadouts.Resolve(new("escort")).ContentHash != resolved.ContentHash, "Effective changes change the content hash");
        var world = new SimWorld();
        var ship = world.Add(new ShipBody("VARIANT", def, Faction.Red));
        var brain = world.AttachBrain(ship, ShipOrder.HoldAt(Vec3d.Zero));
        Check(brain.Profile.StandoffMeters == 45000, "Runtime AI consumes composed profile");
        brain.DefaultPips[0] = 0;
        Check(def.Ai.DefaultPips[0] == 2 && baseHull.Ai.DefaultPips[0] == 2, "Brain power changes do not mutate definition presets");
        var weighted = ShipLoadouts.Resolve(new("escort", """{"battleValue":{"weight":9,"captureStrength":2.5,"hudMarkerRadius":6}}""")).Definition;
        var w = new ShipBody("WEIGHT", weighted, Faction.Blue);
        Check(BattleRules.Strength(new[] { w }, Faction.Blue) == 9, "Battle rules use definition weight");
        var zone = new CaptureZone("TEST", Vec3d.Zero);
        zone.Update(new[] { w }, 1);
        Check(zone.Progress == 6.25, "Capture strength comes from the definition");
        var unarmed = ShipLoadouts.Resolve(new("interceptor", """{"railgun":null,"missiles":null,"antimatter":null}""")).Definition;
        Check(unarmed.Railgun is null && unarmed.Missiles is null && unarmed.Antimatter is null,
            "Null removes optional equipment, without recreating default weapons");
        var pips = ShipLoadouts.Resolve(new("escort", """{"ai":{"defaultPips":[4,1,1,1,1]}}""")).Definition;
        Check(pips.Ai.DefaultPips.SequenceEqual(new[] { 4, 1, 1, 1, 1 }), "Array patches replace the entire array");
        Check(ShipLoadouts.Resolve(new("escort")).Definition.ToJson() == baseHull.ToJson(), "Earlier variants cannot contaminate later resolutions");
    }

    private static void InvalidLoadouts()
    {
        foreach (string patch in new[]
        {
            "[]", "null", "{", """{"shield":{"capacity":1,"Capacity":2}}""",
            """{"unknownField":1}""", """{"shield":{"capacitty":1}}""",
            """{"unknownField":null}""", """{"shield":{"capacitty":null}}""",
            """{"railgun":{"packet":{"energy":99999}}}""", """{"hull":{}}""",
            """{"structureHitPoints":-1}""", """{"flight":{"massKg":0}}""",
            """{"power":{"outputMw":1}}""", """{"modelId":"../other"}""",
            """{"branch":"Unknown"}""", """{"branch":1}""", """{"branch":null}""",
            """{"matchClass":"Unknown"}""", """{"squadron":null}""",
            """{"squadron":{"role":"Unknown"}}""", """{"squadron":{"members":[]}}""",
            """{"squadron":{"members":[null]}}""",
            """{"squadron":{"members":[{"hullId":"escort","offset":[0,0,0]}]}}""",
            """{"squadron":{"members":[{"hullId":null,"offset":[0,0,0]},{"hullId":null,"offset":[0,0,0]}]}}""",
            """{"squadron":{"members":[{"hullId":null,"offset":[0,0,0]},{"hullId":"missing","offset":[1,0,0]}]}}""",
            """{"squadron":{"members":[{"hullId":null,"offset":[0,0,0]},{"hullId":"mars_escort","offset":[1,0,0]}]}}""",
            """{"ai":{"defaultPips":[4,4,4,4,4]}}""", """{"ai":{"defaultPips":[2,2]}}""",
            """{"ai":{"profile":{"standoffMeters":0}}}""", """{"ai":{"targetPreference":{"Battleship":-1}}}""",
            """{"battleValue":{"weight":0}}""", """{"battleValue":{"captureStrength":-1}}""",
            """{"modules":[]}""", """{"shield":{"capacity":"+40%"}}""",
        })
        {
            bool rejected = false;
            try { ShipLoadouts.Resolve(new("escort", patch)); }
            catch (Exception e) when (e is InvalidDataException or JsonException or ArgumentException) { rejected = true; }
            Check(rejected, $"Invalid loadout rejected: {patch}");
        }
        bool missing = false;
        try { ShipLoadouts.Resolve(new("unknown")); } catch (ArgumentException) { missing = true; }
        Check(missing, "Unknown base hull rejected");
        bool support = false;
        try { ShipLoadouts.Resolve(new("battleship", """{"squadron":{"members":[{"hullId":null,"offset":[0,0,0]},{"hullId":"interceptor","offset":[100,0,0]}]}}""")); }
        catch (InvalidDataException) { support = true; }
        Check(support, "Battle group does not silently accept interceptor support");
    }

    private static void BaseRoles()
    {
        foreach (var def in ShipDefinitions.All)
        {
            bool mars = def.Design == DesignFamily.Mars;
            var expected = def.Kind switch
            {
                HullKind.Battleship => (new AiProfile(mars ? 90000 : 60000, mars ? 7f : 10.5f, 140000, false), mars ? new[] {2,1,2,2,1} : new[] {2,2,1,2,1}, 6.0, 2.0),
                HullKind.Escort => (new AiProfile(mars ? 52000 : 30000, mars ? 6.5f : 5, 110000, false), mars ? new[] {2,1,1,2,2} : new[] {2,2,1,2,1}, 2.0, 1.5),
                _ => (new AiProfile(mars ? 7000 : 6000, 2.5f, 15000, true), mars ? new[] {3,1,2,2,0} : new[] {2,2,2,2,0}, .5, 1.0),
            };
            Check(def.Ai.Profile == expected.Item1 && def.Ai.DefaultPips.SequenceEqual(expected.Item2), "Base hull AI profile and power tuning match the doctrine contract");
            Check(def.BattleValue.Weight == expected.Item3 && def.BattleValue.CaptureStrength == expected.Item4, "Legacy victory/capture weights preserved");
        }
    }

    private static void LegacyMatchmaking()
    {
        // Frozen algorithm from before data extraction. Keep its RNG calls/order and offsets
        // here to catch deterministic lineup/spawn regressions, rather than comparing two new runs.
        foreach (var def in ShipDefinitions.All)
        for (int seed = 0; seed < 20; seed++)
        {
            var random = new Random(seed);
            int bb, dd, ic;
            do { bb = random.Next(2, 5); dd = random.Next(2, 5); ic = 10 - bb - dd; } while (ic is < 2 or > 4);
            var ids = new List<string>();
            foreach (var (kind, count) in new[] { (HullKind.Battleship, bb), (HullKind.Escort, dd), (HullKind.Interceptor, ic) })
                for (int i = 0; i < count; i++) ids.Add(ShipDefinitions.For(kind, random.Next(2) == 0 ? DesignFamily.Earth : DesignFamily.Mars).Id);
            int chosen = ids.FindIndex(id => ShipDefinitions.ById(id).Kind == def.Kind); ids[chosen] = def.Id;
            var blue = ids.ToArray(); (blue[0], blue[chosen]) = (blue[chosen], blue[0]);
            void Shuffle(string[] a, int first) { for (int i = a.Length - 1; i > first; i--) { int j = random.Next(first, i + 1); (a[i], a[j]) = (a[j], a[i]); } }
            Shuffle(blue, 1); var red = ids.ToArray(); Shuffle(red, 0);
            var match = Matchmaking.Create(seed, def.Id);
            Check(match.Participants.Select(p => p.HullId).SequenceEqual(blue.Concat(red)), "Seeded roster matches the frozen legacy algorithm");
            foreach (var p in match.Participants)
            for (int i = 0; i < p.ShipCount; i++)
            {
                var expected = p.Kind switch
                {
                    HullKind.Battleship => i == 0 ? Vector3.Zero : new Vector3(i == 1 ? -1500 : 1500, i == 1 ? 150 : -150, -600),
                    HullKind.Escort => new Vector3((i % 2 == 0 ? -1 : 1) * 1200 * (i / 2 + 1), 0, 900 * (i / 2)),
                    _ => new Vector3(60 * i, -25 * i, 50 * i),
                };
                Check(p.Hull.Squadron.Members[i].Offset == expected, "Legacy spawn offset preserved exactly");
            }
        }
    }
}
