using Godot;
using SpaceFleet.Sim;

static class CounterMissileChecks
{
    private static int checks;
    private static void Check(bool ok, string why)
    { if (!ok) throw new InvalidOperationException(why); checks++; }
    private static void Step(SimWorld world, double seconds)
    { for (int i = 0; i < Math.Ceiling(seconds * 60); i++) world.Step(); }
    private static (SimWorld W, ShipBody Ship, ShipBody Enemy, Missile Threat) Fixture(string id, double offset = 0, float lateral = 0, float speed = 3000)
    {
        var w = new SimWorld();
        var def = AntimatterChecks.Variant(id); var enemyDef = AntimatterChecks.Variant("escort");
        var ship = w.Add(new ShipBody("DEFENDER", def.Flight, Faction.Blue, def));
        var enemy = w.Add(new ShipBody("LAUNCHER", enemyDef.Flight, Faction.Red, enemyDef));
        ship.Place(new(offset, offset, offset), Quaternion.Identity);
        enemy.Place(ship.Position + new Vec3d(0, 0, -18000), Quaternion.Identity);
        ship.Control = enemy.Control = new ShipControl { FlightAssist = false };
        w.Sensors.Update(w.Ships, 0, true); w.LaunchMissile(enemy, ship);
        var threat = w.Missiles.Single();
        threat.Position = ship.Position + new Vec3d(lateral, 0, -8000);
        threat.Velocity = Vector3.Back * speed; threat.NoseDirection = Vector3.Back;
        w.Sensors.Update(w.Ships, 0, true, w.Probes, w.Missiles);
        return (w, ship, enemy, threat);
    }
    public static void Run()
    {
        foreach (string id in new[] { "interceptor", "mars_interceptor" })
        foreach (double offset in new[] { 0d, 1e12 })
        foreach (float speed in new[] { 3000f, 6000f })
        foreach (float lateral in new[] { 0f, 600f })
        {
            var (w, ship, enemy, threat) = Fixture(id, offset, lateral, speed);
            var launch=w.LaunchCounterMissile(ship,threat);
            Check(launch.Fired, $"Locked approaching missile can be engaged: {id}/{offset}/{speed}/{lateral}: {launch.Reason}, track={w.Sensors.SmallTrack(ship.Faction,threat)}");
            var counter = w.Missiles.Last();
            Check(counter.Target is null && counter.InterceptTarget == threat && counter.NoseDirection == ship.Forward,
                "Counter round targets the missile and ejects along the real nose, not toward a hidden ship");
            Check(ship.Ordnance.Missiles == 3 && ship.Ordnance.Antimatter.Rounds == 3, "Shared light missile stock and independent three torpedoes");
            Check(!w.LaunchCounterMissile(ship, threat).Fired, "Reload prevents repeated instantaneous launches");
            double began = w.Time;
            for (int i = 0; i < 300 && w.Missiles.Contains(counter); i++)
                w.Step();
            Check(threat.Health <= 0 && ship.Ordnance.Interceptor.MissileKills == 1,
                $"Counter interception {id} offset={offset} speed={speed} lateral={lateral}, age={counter.Age:F3}, miss={(counter.Position-threat.Position).Length():F1}");
            Check(!w.Missiles.Contains(counter) && !w.Missiles.Contains(threat), "Both spent bodies removed safely");
            Check(!w.Impacts.Any(i => i.Shooter == ship), "Counter detonation cannot apply its warhead to the enemy launcher");
            Console.WriteLine($"Counter trial {id} offset={offset:0} speed={speed:0} lateral={lateral:0}: {w.Time-began:F3}s, one round");
        }
        Gates(); AiDefense();
        Console.WriteLine($"PASS: {checks} counter-missile checks");
    }
    private static void Gates()
    {
        var (w, ship, enemy, threat) = Fixture("interceptor");
        Check(!w.LaunchCounterMissile(enemy, threat).Fired, "Capital launcher cannot acquire the interceptor-only mode");
        threat.Velocity = Vector3.Forward * 3000;
        w.Sensors.Update(w.Ships, 0, true, w.Probes, w.Missiles);
        Check(!w.LaunchCounterMissile(ship, threat).Fired && ship.Ordnance.Missiles == 4, "Receding target cannot consume ammunition");
        threat.Velocity = Vector3.Back * 3000; threat.Position = ship.Position + new Vec3d(0, 0, -13000);
        // A genuine allied observation does not extend the weapon envelope.
        var observer = w.Add(new ShipBody("SENSOR", ship.Class, Faction.Blue, ship.Definition));
        observer.Place(ship.Position + new Vec3d(2000, 0, -10000), Quaternion.Identity);
        w.Sensors.Update(w.Ships, 0, true, w.Probes, w.Missiles);
        Check(!w.CounterMissileSolution(ship, threat, out _, out var reason) && reason.Contains("사거리"), "An allied observation does not extend weapon range");
        var next = Fixture("interceptor");
        Check(next.W.LaunchCounterMissile(next.Ship, next.Threat).Fired, "Loss fixture launches");
        var counter = next.W.Missiles.Last();
        next.Threat.Health = 0; next.W.Step();
        Check(!next.W.Missiles.Contains(counter) && next.Ship.Ordnance.Interceptor.MissileKills == 0,
            "Already destroyed target retires the counter round without false credit or ship retarget");
        var lost = Fixture("interceptor");
        lost.Threat.Position += new Vec3d(100000, 0, 0); Step(lost.W, .6);
        Check(!lost.W.LaunchCounterMissile(lost.Ship, lost.Threat).Fired && lost.Ship.Ordnance.Missiles == 4,
            "Stale or undetected missile cannot authorize launch");
        var guidance = Fixture("interceptor");
        guidance.W.LaunchCounterMissile(guidance.Ship, guidance.Threat);
        var guided = guidance.W.Missiles.Last();
        guidance.Threat.Position += new Vec3d(100000, 0, 0); Step(guidance.W, .65);
        Check(Math.Abs((guided.AimPoint - guidance.Ship.Position).X) < 100,
            "Lost track guidance never reads the hidden live missile position");
        foreach (string id in new[] { "interceptor", "mars_interceptor" })
        {
            var light = ShipDefinitions.ById(id).Missiles!;
            foreach (string capital in new[] { "battleship", "escort", "mars_battleship", "mars_escort" })
            {
                var heavy = ShipDefinitions.ById(capital).Missiles!;
                Check(light.AccelG > heavy.AccelG && light.TurnRateDegrees > heavy.TurnRateDegrees
                    && light.BodyScale < heavy.BodyScale && light.Energy < heavy.Energy, "Light missile trades warhead and size for acceleration and slew");
            }
        }
    }

    private static void AiDefense()
    {
        var (w, ship, _, threat) = Fixture("interceptor");
        var wingmate = w.Add(new ShipBody("WINGMATE", ship.Class, Faction.Blue, ship.Definition));
        wingmate.Place(ship.Position + new Vec3d(100, 0, 0), Quaternion.Identity);
        foreach (var member in new[] { ship, wingmate })
        {
            var brain = w.AttachBrain(member, ShipOrder.HoldAt(member.Position));
            brain.SmallTarget = threat; brain.NextThink = 100;
        }
        w.Step();
        Check(w.Missiles.Count(m => m.InterceptTarget == threat) == 1 && ship.Ordnance.Missiles + wingmate.Ordnance.Missiles == 7,
            "AI launches one counter round instead of saturating one threat with the whole wing");
        Step(w, .5);
        Check(ship.Ordnance.Missiles + wingmate.Ordnance.Missiles == 7, "AI waits while the assigned friendly counter missile is in flight");
    }
}
