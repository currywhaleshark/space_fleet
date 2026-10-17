using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Sim;

static class PointDefenseChecks
{
    private static int _checks;
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); _checks++; }
    private static Vector3 Aim(PointDefenseMountState state, float traverse, float elevation) => state.MountBasis
        * new Basis(Vector3.Up, state.CenterYaw + Mathf.DegToRad(traverse))
        * new Basis(Vector3.Right, Mathf.DegToRad(elevation)) * Vector3.Forward;
    private static JsonObject Data(string name)
    {
        using var stream = typeof(ShipDefinitions).Assembly.GetManifestResourceStream($"SpaceFleet.data.ships.{name}.json")!;
        using var reader = new StreamReader(stream);
        return JsonNode.Parse(reader.ReadToEnd())!.AsObject();
    }
    private static ShipBody Add(SimWorld world, string name, Faction faction, Vec3d position, bool defense = false)
    {
        var json = Data(name);
        json.Remove("defenseDrones");
        if (defense)
        {
            json["pointDefense"]!["mounts"] = JsonNode.Parse("[[0,100,0]]");
            json["pointDefense"]!["normals"] = JsonNode.Parse("[[0,0,-1]]");
        }
        else json.Remove("pointDefense");
        var def = ShipDefinition.Parse(json.ToJsonString());
        var ship = world.Add(new ShipBody(faction.ToString(), def.Flight, faction, def));
        ship.Place(position, Quaternion.Identity); ship.Control = new ShipControl { FlightAssist = false };
        return ship;
    }

    public static void Run()
    {
        foreach (var kind in new[] { HullKind.Battleship, HullKind.Escort, HullKind.Interceptor })
        {
            var def = ShipDefinitions.For(kind).PointDefense!;
            for (int i = 0; i < def.Mounts.Length; i++)
            {
                var state = new PointDefenseMountState(def, i);
                Vector3 target = Aim(state, 70, 50);
                state.Track(target, .1, 1.5f);
                Require(Math.Abs(state.Traverse - Mathf.DegToRad(def.YawRate) * .1) < 1e-6
                    && Math.Abs(state.Elevation - Mathf.DegToRad(def.ElevationRate) * .1) < 1e-6,
                    $"{kind}/{i}: yaw/elevation cannot exceed rated speed even with weapon power boost");
                Require(!state.Aligned, "A new off-axis target cannot be snap-fired");
                state.Track(target, 5, 1);
                Require(state.Aligned && state.LocalDirection.Dot(target) > .99999f, "Both deck orientations reach the actual target bearing");
                Require(!state.Contains(Aim(state, def.YawDegrees + 5, 0)), "Traverse stop excludes out-of-sector targets");
                Require(!state.Contains(Aim(state, 0, def.MaxElevation + 1))
                    && !state.Contains(Aim(state, 0, def.MinElevation - 1)), "Elevation and depression stops exclude targets");
                state.Track(Aim(state, 179, 89), 10, 1);
                Require(Math.Abs(state.Traverse - Mathf.DegToRad(def.YawDegrees)) < 1e-6
                    && Math.Abs(state.Elevation - Mathf.DegToRad(def.MaxElevation)) < 1e-6 && !state.Aligned,
                    "Unreachable aim clamps at physical stops without firing permission");
                float before = state.Traverse;
                state.Track(Aim(state, -def.YawDegrees, 0), .1, 1);
                Require(state.Traverse < before && state.Traverse > 0, "Drive crosses the allowed sector instead of wrapping through rear stop");
                state.FireAccumulator = .9f; state.ClearTracking();
                Require(state.FireAccumulator == 0 && state.LocalAim is null, "Lost target cannot bank a burst");
                state.Reset(); state.Track(target, .1, .3f);
                Require(state.Traverse < Mathf.DegToRad(def.YawRate) * .031f, "Low weapon power slows the mount drive");
                before = state.Traverse; state.Track(target, 1, 0);
                Require(state.Traverse == before, "No drive power freezes the mount");
                state.LastFiredAt = 1; state.ShotCount = 5; state.Reset();
                Require(state.Traverse == 0 && state.Elevation == 0 && state.PreviousYaw == state.Yaw
                    && state.ShotCount == 0 && double.IsNegativeInfinity(state.LastFiredAt), "Reset clears physical and firing state");
            }
        }
        CheckFireGate();
        CheckPretrackPriority();
        CheckHullOcclusion();
        CheckPrediction();
        foreach (bool missile in new[] { false, true })
        {
            uint slow = Flyby(100, 350, missile), fast = Flyby(1200, 350, missile), distant = Flyby(1200, 3500, missile);
            Require(slow > 15 && fast < slow * .75f, $"Close fast flyby must reduce actual shots ({missile}): slow {slow}, fast {fast}");
            Require(distant > fast, $"Same fast target is easier to track at range ({missile}): near {fast}, distant {distant}");
            Console.WriteLine($"PD {(missile ? "missile" : "interceptor")} flyby / 5 s: slow-near {slow}, fast-near {fast}, fast-far {distant} shots");
        }
        foreach (var (field, value) in new[] { ("yawRate", 0f), ("elevationRate", -1f), ("yawDegrees", 181f),
                     ("minElevation", -91f), ("maxElevation", 91f), ("toleranceDegrees", 0f),
                     ("muzzleSpeed", 0f), ("muzzleSpeed", -1f), ("trackingErrorMeters", 0f) })
        {
            var data = Data("battleship"); data["pointDefense"]![field] = value;
            bool rejected = false;
            try { ShipDefinition.Parse(data.ToJsonString()); } catch (Exception e) when (e is InvalidDataException or ArgumentException) { rejected = true; }
            Require(rejected, $"Invalid PD {field} rejected");
        }
        Console.WriteLine($"PASS: {_checks} point-defense tracking checks");
    }

    private static void CheckPrediction()
    {
        var definition = ShipDefinitions.For(HullKind.Battleship).PointDefense!;
        foreach (double offset in new[] { 0d, 1e12 })
        foreach (Vector3 commonVelocity in new[] { Vector3.Zero, new Vector3(500, 100, -200) })
        {
            Vec3d origin = new(offset, -offset, offset);
            Vec3d point = origin + new Vec3d(0, 0, -3000);
            var target = new object();
            var state = new PointDefenseMountState(definition, 0);
            Vector3 velocity = new Vector3(1800, 0, 0) + commonVelocity;
            Require(state.PredictAim(target, -1, point, velocity, origin, commonVelocity, 0, out var aim, out _),
                "PD solves a fast crossing target using relative velocity");
            double t = state.FlightSeconds;
            Vector3 direction = (aim - origin).ToVector3().Normalized();
            Vec3d bullet = origin + Vec3d.From(commonVelocity + direction * definition.MuzzleSpeed) * t;
            Vec3d destination = point + Vec3d.From(velocity) * t;
            Require((bullet - destination).Length() < .002 && direction.X > .25,
                "Predicted bore intercepts the moving target with inherited shooter velocity, including large coordinates");
            Require(state.PredictionFactor(direction, 0, 1) == 1,
                "Fast constant crossing speed has no arbitrary accuracy penalty");
            var unchanged = new PointDefenseMountState(definition, 0);
            unchanged.PredictAim(target, -1, point, velocity, origin, commonVelocity, 0, out _, out _);
            unchanged.PredictAim(target, -1, point, velocity, origin, commonVelocity, .01, out var expectedAim, out _);
            state.PredictAim(target, -1, point + new Vec3d(999, 999, 999), Vector3.Up * 10000,
                origin, commonVelocity, .01, out var cachedAim, out var cachedContact);
            Require(state.SolutionCount == 1 && cachedAim == expectedAim
                && (cachedContact - (point + Vec3d.From(velocity) * .01)).Length() < .002,
                "Between observations PD extrapolates its sample rather than reading new target motion");
            state.FireAccumulator = .9f;
            state.PredictAim(new object(), -1, point, Vector3.Zero, origin, commonVelocity, .02, out _, out _);
            Require(state.SolutionCount == 2 && state.FireAccumulator == .9f,
                "Target switches replace the solution without restarting an aligned gun's fractional firing cycle");
            state.ClearTracking();
            Require(state.FlightSeconds == 0 && state.LocalAim is null && state.FireAccumulator == 0,
                "Lost contact clears prediction and firing state");
            state.Reset(); Require(state.SolutionCount == 0, "Weapon reset clears prediction counters");
        }

        float Maneuver(float speed, Vector3 acceleration)
        {
            var state = new PointDefenseMountState(definition with { MuzzleSpeed = speed }, 0);
            object target = new();
            Vec3d p = new(0, 0, -3000);
            state.PredictAim(target, -1, p, Vector3.Zero, Vec3d.Zero, Vector3.Zero, 0, out _, out _);
            const double dt = 1d / 15;
            state.PredictAim(target, -1, p + Vec3d.From(acceleration) * (.5 * dt * dt), acceleration * (float)dt,
                Vec3d.Zero, Vector3.Zero, dt, out var aim, out _);
            return state.PredictionFactor(aim.ToVector3().Normalized(), dt, 1);
        }
        Require(Maneuver(3000, Vector3.Zero) == 1 && Maneuver(12000, Vector3.Zero) == 1,
            "A stationary target is not penalized solely because the bullet is slower");
        float slow = Maneuver(3000, Vector3.Right * 100), fast = Maneuver(12000, Vector3.Right * 100);
        Require(slow < .1 && fast > .7 && fast > slow,
            "Observed lateral acceleration matters through time of flight; faster PD reduces prediction error");
        Require(Maneuver(6000, Vector3.Forward * 100) == 1,
            "Radial acceleration does not invent lateral displacement");

        var invalid = new PointDefenseMountState(definition, 0);
        Require(!invalid.PredictAim(new object(), -1, new(0, 0, -3000), Vector3.Forward * 12000,
            Vec3d.Zero, Vector3.Zero, 0, out _, out _), "A receding target faster than the bullet has no firing solution");

        var phases = Enumerable.Range(0, 16).Select(i => new PointDefenseMountState(definition, 0, (uint)i)).ToArray();
        object sharedTarget = new();
        int peak = 0;
        for (int step = 0; step <= 240; step++)
        {
            int solved = 0;
            foreach (var state in phases)
            {
                uint before = state.SolutionCount;
                state.PredictAim(sharedTarget, -1, new(0, 0, -3000), Vector3.Zero,
                    Vec3d.Zero, Vector3.Zero, step / 240d, out _, out _);
                solved += (int)(state.SolutionCount - before);
            }
            if (step > 0) peak = Math.Max(peak, solved);
        }
        Require(phases.All(s => s.SolutionCount == 16) && peak == 1,
            "After acquisition each mount solves 15 times per second with one of 16 distinct substep phases");
        var roundtrip = ShipDefinition.Parse(ShipDefinitions.For(HullKind.Battleship).ToJson());
        Require(roundtrip.PointDefense!.MuzzleSpeed == definition.MuzzleSpeed
            && roundtrip.PointDefense.TrackingErrorMeters == definition.TrackingErrorMeters,
            "PD ballistic calibration survives definition roundtrip and has compatible defaults");
    }

    private static void CheckFireGate()
    {
        var world = new SimWorld(); var bb = Add(world, "battleship", Faction.Blue, Vec3d.Zero, true);
        var state = bb.Ordnance.PointDefense[0];
        Vector3 direction = Aim(state, 80, 35);
        var enemy = Add(world, "interceptor", Faction.Red, new Vec3d(0, 100, 0) + Vec3d.From(direction) * 2000);
        for (int i = 0; i < 30; i++) world.Step();
        Require(state.ShotCount == 0, "No shots during initial traverse");
        var hits=new List<PointDefenseShot>();
        for (int i = 0; i < 150; i++)
        {
            enemy.Damage.Reset(); world.Step();
            hits.AddRange(world.PointDefenseShots.Where(s=>s.Hit && s.Faction==bb.Faction));
        }
        Require(state.Aligned && state.ShotCount > 0, "Stationary threat is engaged after acquisition");
        Require(world.Projectiles.Count == 0, "Statistical PD creates tracer events without physical rail projectiles");
        var shell=enemy.Definition.ShieldEnvelope;
        Require(hits.Count>0 && hits.All(s=>Mathf.Abs((((s.To-enemy.Position).ToVector3()-shell.Center)/shell.Radii).Length()-1)<.0001f),
            $"Successful PD tracers end at the active shield boundary rather than the target center: hits={hits.Count}, radii={string.Join(',',hits.Select(s=>(((s.To-enemy.Position).ToVector3()-shell.Center)/shell.Radii).Length()).Distinct())}");
        uint before = state.ShotCount;
        enemy.Place(new Vec3d(0, 100, 0) + Vec3d.From(Aim(state, 150, 0)) * 2000, Quaternion.Identity);
        for (int i = 0; i < 120; i++) world.Step();
        Require(state.ShotCount == before && state.LocalAim is null, "Outside mechanical sector: no target, shots, or damage");
        enemy.Place(new Vec3d(0, 100, 0) + Vec3d.From(direction) * (bb.Definition.PointDefense!.RangeMeters * 1.2f), Quaternion.Identity);
        for (int i = 0; i < 120; i++) world.Step();
        Require(state.LocalAim is not null && state.Aligned && state.ShotCount == before,
            "Early tracking turns toward approaching threats without extending the firing range or banking shots");
        enemy.Place(new Vec3d(0, 100, 0) + Vec3d.From(direction) * 2000, Quaternion.Identity);
        world.Step();
        Require(state.ShotCount == before, "Reacquisition cannot discharge a banked burst");
        foreach (var module in bb.Damage.Modules.Where(m => m.Definition.Kind == ModuleKind.Sensor))
            bb.Damage.Hurt(module, module.Health, world.Time, 0, 1);
        for (int i = 0; i < 120; i++) world.Step();
        Require(state.ShotCount == before && state.LocalAim is null, "Sensor loss stops tracking and fire");
        bb.Damage.Reset();
        bb.Damage.Catastrophe(world.Time, "test");
        for (int i = 0; i < 120; i++) world.Step();
        Require(state.ShotCount == before, "Destroyed ship cannot continue PD fire");
        world.ResetWeapons();
        Require(state.ShotCount == 0 && state.LocalAim is null && state.FireAccumulator == 0, "World reset clears drive and firing credit");
    }

    private static void CheckPretrackPriority()
    {
        var world = new SimWorld(); var bb = Add(world, "battleship", Faction.Blue, Vec3d.Zero, true);
        var raider = Add(world, "interceptor", Faction.Red, new Vec3d(0, 100, -2000));
        var launcher = Add(world, "escort", Faction.Red, new Vec3d(0, 0, -20000));
        world.Sensors.Update(world.Ships, world.Time, force: true);
        Require(world.LaunchMissile(launcher, bb).Fired, "Priority fixture launches a hostile missile");
        var missile = world.Missiles.Single();
        missile.Position = missile.PrevPosition = new Vec3d(0, 100, -6000);
        missile.Velocity = Vector3.Zero; missile.Health = 100000;
        missile.Age = missile.Definition.BurnSeconds + 1; missile.NextSeekerCheck = double.MaxValue;
        var state = bb.Ordnance.PointDefense[0];
        for (int i = 0; i < 120; i++) { raider.Damage.Reset(); world.Step(); }
        Require(state.ShotCount > 0 && missile.Health == 100000,
            "Out-of-range missile pretrack does not suppress fire at an in-range interceptor or extend range");
        missile.Position = missile.PrevPosition = new Vec3d(1200, 100, -2000);
        for (int i = 0; i < 120; i++) { raider.Damage.Reset(); world.Step(); }
        Require(state.LocalAim is Vector3 aim && aim.Dot(new Vector3(1200, 0, -2000).Normalized()) > .9999f,
            "Missile in gun range regains priority over the closer interceptor");
    }

    private static void CheckHullOcclusion()
    {
        foreach(string id in new[]{"earth_recon_t3","mars_assault_t3"})
        foreach(bool missileTarget in new[]{false,true})
        foreach(bool distant in new[]{false,true})
        {
            var world=new SimWorld();var def=ShipDefinitions.ById(id);
            var ship=world.Add(new ShipBody("OCCLUSION",def,Faction.Blue));
            var pose=distant?new Quaternion(new Vector3(1,2,3).Normalized(),.93f):Quaternion.Identity;
            ship.Place(distant?new Vec3d(1e12,-1e12,1e12):Vec3d.Zero,pose);
            ship.Control=new ShipControl {FlightAssist=false};
            int index=id.StartsWith("earth")?1:0;var state=ship.Ordnance.PointDefense[index];
            var mount=def.PointDefense!.Mounts[index];
            // Horizontal right-aft ray intersects the real T3 tail fin; straight aft is unobstructed.
            Vector3 blocked=new Vector3(.8f,0,.6f),clear=Vector3.Back;
            Vec3d Point(Vector3 direction)=>ship.Position+Vec3d.From(pose*(mount+direction*600));
            Vec3d origin=ship.Position+Vec3d.From(pose*(mount+Vector3.Up*.05f));
            Require(state.Contains(blocked)&&state.Contains(clear),id+": both rays are mechanically reachable");
            Require(DamageRay.FirstHit(ship,origin,pose*blocked,600,out float distance)&&distance>1&&distance<15,
                id+": blocked ray actually meets the tail fin");
            Require(!DamageRay.FirstHit(ship,origin,pose*clear,600,out _),id+": aft exit is clear");
            var target=Add(world,missileTarget?"escort":"interceptor",Faction.Red,
                missileTarget?ship.Position+Vec3d.From(pose*Vector3.Forward)*20000:Point(blocked));
            Missile? missile=null;
            if(missileTarget)
            {
                world.Sensors.Update(world.Ships,0,true);
                Require(world.LaunchMissile(target,ship).Fired,"Occlusion fixture launches missile normally");
                missile=world.Missiles.Single();missile.Age=missile.Definition.BurnSeconds+1;
                missile.NextSeekerCheck=double.MaxValue;missile.Health=100000;missile.Velocity=Vector3.Zero;
            }
            void PlaceThreat(Vector3 direction)
            {
                if(missile is not null)missile.Position=missile.PrevPosition=Point(direction);
                else {target.Place(Point(direction),Quaternion.Identity);target.Damage.Reset();}
            }
            PlaceThreat(blocked);
            for(int tick=0;tick<240;tick++)world.Step();
            Require(state.ShotCount==0&&state.LocalAim is null,
                $"{id}/{missileTarget}/{distant}: PD cannot shoot through its own tail fin");
            PlaceThreat(clear);
            for(int tick=0;tick<240;tick++){if(missile is null)target.Damage.Reset();world.Step();}
            Require(state.ShotCount>0&&state.Aligned,
                $"{id}/{missileTarget}/{distant}: the same threat is engaged after entering a clear sector");
            uint shots=state.ShotCount;PlaceThreat(blocked);
            for(int tick=0;tick<60;tick++)world.Step();
            Require(state.ShotCount==shots&&state.LocalAim is null,
                "Re-entering hull occlusion cancels firing immediately without retaining a target");
        }
    }

    // Controlled straight crossing, identical exposure duration. High-HP targets isolate tracking from random kills.
    private static uint Flyby(float speed, float standOff, bool missileTarget)
    {
        var world = new SimWorld(); var bb = Add(world, "battleship", Faction.Blue, Vec3d.Zero, true);
        var target = Add(world, missileTarget ? "escort" : "interceptor", Faction.Red, new Vec3d(0, 0, -20000));
        Missile? missile = null;
        if (missileTarget)
        {
            for (int i = 0; i < 120; i++) world.Step();
            Require(world.LaunchMissile(target, bb).Fired, "Flyby test missile launches through normal weapon path");
            missile = world.Missiles.Single();
            missile.Age = missile.Definition.BurnSeconds + 1; missile.NextSeekerCheck = double.MaxValue;
            missile.Health = 100000;
        }
        for (int i = 0; i < 300; i++)
        {
            var point = new Vec3d((i / 60d - 2.5) * speed, 400, -standOff);
            if (missile is not null) { missile.Position = missile.PrevPosition = point; missile.Velocity = Vector3.Right * speed; }
            else { target.Place(point, Quaternion.Identity); target.Velocity = Vector3.Right * speed; target.Damage.Reset(); }
            world.Step();
        }
        if (missile is not null) Require(world.Missiles.Contains(missile), "Flyby missile survives for the full measurement interval");
        return bb.Ordnance.PointDefense[0].ShotCount;
    }
}
