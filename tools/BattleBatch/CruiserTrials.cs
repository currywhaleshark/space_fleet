using System.Text.Json;
using Godot;
using SpaceFleet.Sim;

// Controlled role measurements, deliberately separate from fleet win-rate samples.
internal static class CruiserTrials
{
    public sealed record Result(string Trial, string Hull, double Seconds, string LoadoutHash,
        int Rails, int RailHits, int Missiles, int MissileHits, int Intercepts, int CounterKills,
        int DroneKills, int Incoming, int IncomingHits, int RemainingMissiles, double ShieldDamage);

    public static ResolvedLoadout Resolve(string id, params string[] patches) => ShipVariants.All.Any(v=>v.Node.Id==id)
        ? ShipVariants.Resolve(id,patches) : ShipLoadouts.Resolve(new(id,UpgradePatches:patches));

    public static ShipDefinition PassiveTarget() => Resolve("battleship",
        """{"railgun":null,"missiles":null,"pointDefense":null,"defenseDrones":null,"decoys":null,"sensors":{"jammer":0},"shield":{"capacity":100000000}}""").Definition;

    public static void Step(SimWorld world, double seconds)
    { int ticks=(int)Math.Ceiling(seconds/SimWorld.TickDelta);for(int i=0;i<ticks;i++)world.Step(); }

    public static Result Offense(string id, bool missiles)
    {
        var resolved=Resolve(id,missiles?"""{"railgun":null}""":"""{"missiles":null}""");
        var w=new SimWorld();
        var ship=w.Add(new ShipBody("TEST",resolved.Definition,Faction.Blue));
        var target=w.Add(new ShipBody("TARGET",PassiveTarget(),Faction.Red));
        target.Place(new(0,0,missiles?-25000:-16000),Quaternion.Identity);
        ship.Control=target.Control=new ShipControl {FlightAssist=false};
        w.AttachBrain(ship,ShipOrder.HoldAt(ship.Position,target));
        w.Sensors.Update(w.Ships,0,true);w.Log=new BattleLog(w);
        Step(w,missiles?120:60);
        return Measure(missiles?"missile-static-25km":"gun-static-16km",resolved,w,ship,0,0);
    }

    public static (SimWorld World,ShipBody Guard,ShipBody Attacker,ShipBody Anchor) DefenseFixture(string id, bool dronesOnly=false)
    {
        var w=new SimWorld();
        var guard=w.Add(new ShipBody("GUARD",Resolve(id,"""{"railgun":null,"decoys":null}""",
            dronesOnly ? """{"pointDefense":null,"missiles":null}""" : "{}").Definition,Faction.Blue));
        var anchor=w.Add(new ShipBody("ANCHOR",PassiveTarget(),Faction.Blue));
        anchor.Place(new(0,-200,5000),Quaternion.Identity);
        var attacker=w.Add(new ShipBody("LAUNCHER",Resolve("escort","""{"railgun":null,"pointDefense":null,"decoys":null,"sensors":{"jammer":0}}""").Definition,Faction.Red));
        attacker.Place(new(0,0,-22000),new Quaternion(Vector3.Up,Mathf.Pi));
        foreach(var s in w.Ships)s.Control=new ShipControl{FlightAssist=false};
        w.AttachBrain(guard,ShipOrder.HoldAt(guard.Position));
        w.Sensors.Update(w.Ships,0,true);w.Log=new BattleLog(w);
        return(w,guard,attacker,anchor);
    }

    public static Missile AddThreat(SimWorld w,ShipBody attacker,ShipBody target,Vec3d position,Vector3 velocity)
    {
        attacker.Ordnance.Reset(); // Fixture launch rack; each injected round still uses the real missile definition.
        w.Sensors.Update(w.Ships,w.Time,true);
        if(!w.LaunchMissile(attacker,target).Fired)throw new Exception("Trial incoming missile launch failed");
        var m=w.Missiles.Last();m.Position=m.PrevPosition=position;m.Velocity=velocity;
        m.NoseDirection=m.PreviousNoseDirection=velocity.Normalized();
        m.Age=m.Definition.BurnSeconds+1; // Repeatable terminal/coasting approach without a second acceleration phase.
        return m;
    }

    public static Result Defense(string id, bool dronesOnly=false)
    {
        var(w,guard,attacker,anchor)=DefenseFixture(id,dronesOnly);
        const int incoming=12;
        for(int i=0;i<incoming;i++)
        {
            var p=new Vec3d((i%3-1)*120,300,-12000-i/3*1500);
            var threat=AddThreat(w,attacker,anchor,p,(anchor.Position-p).ToVector3().Normalized()*2000);
            // Isolate the last layer's finishing role after prior layers have
            // damaged a missile, without letting PD/counter rounds steal the kill.
            if(dronesOnly)threat.Health=guard.Definition.DefenseDrones!.DamagePerHit;
        }
        w.Sensors.Update(w.Ships,0,true,w.Probes,w.Missiles);
        Step(w,35);
        var loadout=Resolve(id,"""{"railgun":null,"decoys":null}""",
            dronesOnly ? """{"pointDefense":null,"missiles":null}""" : "{}");
        return Measure(dronesOnly?"screen-drones-only-damaged":"screen-12-coasting-2kmps",loadout,w,guard,incoming,w.Log!.Ship(attacker).MissileHits);
    }

    private static Result Measure(string name,ResolvedLoadout def,SimWorld w,ShipBody s,int incoming,int hits)
    {
        var stat=w.Log!.Ship(s);
        return new(name,def.Definition.Id,w.Time,def.ContentHash,stat.Rails,stat.RailHits,stat.Missiles,stat.MissileHits,
            stat.MissilesIntercepted,stat.CounterMissileKills,(int)s.Ordnance.Drones.Interceptions,
            incoming,hits,s.Ordnance.Missiles,stat.ShieldDamageInflicted);
    }

    public static Result[] Run() => new[]{Offense("escort",false),Offense("earth_gun_cruiser",false),
        Offense("mars_escort",false),Offense("mars_gun_cruiser",false),
        Offense("mars_escort",true),Offense("mars_missile_cruiser",true),Defense("escort"),Defense("earth_air_defense_cruiser")};

    public static void Save(string output)
    {
        var results=Run();Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        foreach(var r in results)Console.WriteLine($"{r.Hull}/{r.Trial}: rail {r.RailHits}/{r.Rails}, missile {r.MissileHits}/{r.Missiles}, intercepted {r.Intercepts}/{r.Incoming}, counter {r.CounterKills}, drone {r.DroneKills}, incoming hits {r.IncomingHits}");
    }
}
