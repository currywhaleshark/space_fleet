using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Sim;

static class CruiserChecks
{
    private static int checks;
    private static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
    private static void Reject(Action action,string why)
    { bool rejected=false;try{action();}catch(Exception e)when(e is ArgumentException or InvalidDataException or JsonException){rejected=true;}Check(rejected,why); }
    public static void Run()
    {
        Catalog();AuthoredGeometry();Orders();Defense();Salvo();Measurements();
        Console.WriteLine($"PASS: {checks} cruiser catalog/doctrine/defense/salvo checks");
    }
    private static void Catalog()
    {
        Check(ShipDefinitions.All.Count()==6&&ShipVariants.All.Where(v=>v.Node.Tier==3&&v.Definition.Kind==HullKind.Escort).Count()==4,"Six base hulls plus four independent cruiser nodes");
        var originals=ShipDefinitions.All.ToDictionary(d=>d.Id,d=>d.ToJson());
        foreach(var v in ShipVariants.All.Where(v=>v.Node.Tier==3&&v.Definition.Kind==HullKind.Escort))
        {
            var def=v.Definition;var source=ShipDefinitions.ById(v.Node.BaseHullId);
            Check(v.Node.Tier==3&&v.Definition.Kind==HullKind.Escort&&def.Kind==HullKind.Escort&&def.MatchClass==MatchWeightClass.Medium,"T3 cruiser retains medium physical class");
            Check(ShipDefinitions.ById(def.Id)==def&&def.ModelId==def.Id&&def.Design==source.Design,"Stable ID selects the independent cruiser model and inherited design faction");
            // Modules may be relocated to fit the traced silhouette; ids, sizes and statistics stay those of the base hull.
            static ModuleDefinition[] Unplaced(IEnumerable<ModuleDefinition> modules)=>modules.Select(m=>m with{Center=default}).ToArray();
            Check(!def.Hull.Boxes.SequenceEqual(source.Hull.Boxes)&&ShipDefinition.SerializeData(Unplaced(def.Modules))==ShipDefinition.SerializeData(Unplaced(source.Modules)),"Authored cruiser hull preserves core module sizes and statistics");
            Check(Squadron.IsSupportRole(def.Squadron.Role)&&def.Squadron.Members.Count==4,"Cruiser squads retain four medium ships");
            Check(JsonNode.DeepEquals(JsonNode.Parse(ShipDefinition.Parse(v.Loadout.DefinitionJson,true).ToJson()),JsonNode.Parse(def.ToJson())),"Resolved branch snapshot round trips");
            var fit=ShipVariants.Resolve(def.Id,equipment:new[]{new EquipmentMount("utility-port","shield-capacity")});
            Check(fit.Definition.Branch==def.Branch&&fit.Definition.InstalledEquipment.Length==1,"Branch accepts physical equipment after tree patch");
            Check(ShipVariants.Resolve(def.Id).ContentHash==v.Loadout.ContentHash,"Equipment does not mutate catalog node");
            var lineup=Matchmaking.Create(7,def.Id);
            var world=new SimWorld();Matchmaking.Spawn(world,new BattleConfig{Seed=7,Match=lineup});
            Check(Enum.GetValues<Faction>().All(f=>world.Ships.Count(s=>s.Faction==f&&s.Definition.Id==def.Id)==4),"Explicit variant participant spawns four branch members on each matched team");
            Check(world.Squadrons.First(q=>q.Members.Any(s=>s.Definition.Id==def.Id)).Role==def.Squadron.Role,"Spawn keeps branch squad role");
        }
        foreach(var d in ShipDefinitions.All)Check(d.ToJson()==originals[d.Id],"Resolving variants does not mutate any base ship");
        string[] names={"미스트랄급 호위함","소콜급 요격함","안데스급 전함","솔리스급 호위함","로웰급 요격함","아레스급 전함"};
        Check(names.All(n=>ShipDefinitions.All.Any(d=>d.Flight.DisplayName==n)),"Current class names follow the revised naming plan");
        Check(ShipVariants.All.Where(v=>v.Node.Tier==3&&v.Definition.Kind==HullKind.Escort).Select(v=>v.Definition.Flight.DisplayName).OrderBy(n=>n).SequenceEqual(
            new[]{"다뉴브급 포격순양함","시러스급 방공순양함","칸도르급 포격순양함","크리세급 미사일순양함"}.OrderBy(n=>n)),"New class names match the T3 table");
        Reject(()=>ShipDefinitions.ById("missing-cruiser"),"Unknown hull ID rejects");
        foreach(int n in new[]{0,9})Reject(()=>ShipVariants.Resolve("mars_missile_cruiser",new[]{$"{{\"ai\":{{\"missilesPerSalvo\":{n}}}}}"}),"Invalid salvo cap rejects");
        Reject(()=>ShipVariants.Resolve("mars_missile_cruiser",new[]{"""{"missiles":{"launchSignatureSeconds":0}}"""}),"Emission boost needs a positive duration");
    }
    private static void AuthoredGeometry()
    {
        foreach(var v in ShipVariants.All.Where(v=>(v.Node.Tier is 3 or 4 or 5&&v.Definition.Kind==HullKind.Escort)||(v.Node.Tier==5&&v.Definition.Kind==HullKind.Battleship)))
        foreach(var module in v.Definition.Modules.Where(m=>m.Kind is ModuleKind.Sensor or ModuleKind.Thruster or ModuleKind.Gun
            ||m.Id.StartsWith("radiator-")))
        {
            var ship=new ShipBody("MODULE-RAY",v.Definition,Faction.Blue);
            ship.Place(Vec3d.Zero,Quaternion.Identity);
            var outward=module.Kind==ModuleKind.Thruster?Vector3.Back:module.Id.StartsWith("radiator-")
                ?Vector3.Right*Mathf.Sign(module.Center.X):Vector3.Up*(module.Center.Y<0?-1:1);
            var origin=Vec3d.From(module.Center+outward*1000);
            var hit=DamageRay.ApplyAtPose(ship,origin,-outward,new DamagePacket(1000000,1000000,1000),
                0,1,ship.Position,ship.Orientation,shieldApplies:false);
            Check(hit.HullHit&&hit.Modules.Any(m=>m.Id==module.Id),
                $"{v.Node.Id}: actual hull entry reaches authored {module.Id} damage volume");
        }
    }
    private static void Orders()
    {
        foreach(var variant in ShipVariants.All.Where(v=>v.Node.Tier==3&&v.Definition.Kind==HullKind.Escort))
        {
            var w=new SimWorld();var anchor=w.Add(new ShipBody("ANCHOR",CruiserTrials.PassiveTarget(),Faction.Blue));
            w.AddSquadron("Main",Faction.Blue,SquadronRole.BattleGroup,new[]{anchor});
            var members=Enumerable.Range(0,4).Select(i=>w.Add(new ShipBody("CR"+i,variant.Definition,Faction.Blue))).ToArray();
            var squad=w.AddSquadron("Cruisers",Faction.Blue,variant.Definition.Squadron.Role,members);
            for(int i=0;i<4;i++){members[i].Place(new(2000+i*1500,0,1000),Quaternion.Identity);w.AttachBrain(members[i],ShipOrder.HoldAt(members[i].Position));}
            var target=w.Add(new ShipBody("ENEMY",CruiserTrials.PassiveTarget(),Faction.Red));
            target.Place(new(0,0,-25000),Quaternion.Identity);
            w.EnableCommander(Faction.Blue);w.Sensors.Update(w.Ships,0,true);w.Step();
            bool defense=variant.Definition.Branch==ShipBranch.AirDefenseCruiser;
            foreach(var member in members)
            {
                var b=w.BrainOf(member)!;
                Check(b.Order.Kind==(defense?OrderKind.Escort:OrderKind.Attack),"Branch gets its distinct fleet order");
                Check(b.Target==target,"Observed enemy remains the firing target");
                if(defense)Check(b.Order.Target==anchor,"AA protects allied capital instead of flanking");
            }
            if(defense)Check(members.Select(s=>w.BrainOf(s)!.FormationOffset).Distinct().Count()==4,"Four AA stations do not overlap");
            squad.PlayerLed=true;
            foreach(var s in members)w.BrainOf(s)!.Order=ShipOrder.HoldAt(s.Position);
            CruiserTrials.Step(w,2.1);
            Check(members.All(s=>w.BrainOf(s)!.Order.Kind==OrderKind.Hold),"Explicit player squad commands retain priority");
            squad.PlayerLed=false;target.Teleport(new(0,0,-10000000));CruiserTrials.Step(w,25);
            Check(members.All(s=>w.BrainOf(s)!.Target!=target||w.Sensors.Memory(Faction.Blue,target,w.Time)!=null),"Unobserved enemy is only followed through retained contact memory");
        }
        var capture=new SimWorld();BattleSetup.Spawn(capture,new BattleConfig{Objective=BattleObjective.Domination,
            HullOverrides=new Dictionary<string,string>{{"DD-31","earth_air_defense_cruiser"},{"DD-32","earth_air_defense_cruiser"},{"DD-33","earth_air_defense_cruiser"},{"DD-34","earth_air_defense_cruiser"}}});
        CruiserTrials.Step(capture,2.1);
        var group=capture.Squadrons.Single(s=>s.Role==SquadronRole.AirDefenseSquadron);
        Check(group.ObjectiveOrders&&group.Members.All(s=>s.Control.Thrust.IsFinite()),"Capture objective keeps precedence over branch station orders");
        var combined=new SimWorld();BattleSetup.Spawn(combined,new BattleConfig{HullOverrides=
            new[]{"DD-11","DD-12","DD-31","DD-32","DD-33","DD-34"}.ToDictionary(c=>c,c=>"earth_air_defense_cruiser")});
        combined.Step();
        Check(combined.Ships.Where(s=>s.Definition.Branch==ShipBranch.AirDefenseCruiser)
            .Select(s=>combined.BrainOf(s)!.FormationOffset).Distinct().Count()==6,"Capital escorts and outer defense squad occupy distinct stations");
    }
    private static void Defense()
    {
        var(w,guard,attacker,anchor)=CruiserTrials.DefenseFixture("earth_air_defense_cruiser");
        CruiserTrials.Step(w,1);
        Check(guard.Ordnance.Missiles==20,"AA reserves counter rounds when no missile is observed");
        var threat=CruiserTrials.AddThreat(w,attacker,anchor,new(0,0,-10000),Vector3.Back*1800);
        w.Sensors.Update(w.Ships,w.Time,true,w.Probes,w.Missiles);
        Check(w.CounterMissileSolution(guard,threat,out var observed,out _),"AA can use the shared counter-missile solution");
        var actual=threat.Position;threat.Position+=new Vec3d(1000000,500000,0);
        Check(w.CounterMissileSolution(guard,threat,out var unchanged,out _)&&observed.DistanceTo(unchanged)<.00001f,"Counter solution uses observed track, never hidden current position");
        threat.Position=actual;w.Step();
        Check(w.Missiles.Count(m=>m.Shooter==guard&&m.InterceptTarget==threat)==1&&guard.Ordnance.Missiles==19,"Defense AI launches one real round at a detected incoming missile");
        CruiserTrials.Step(w,5);
        Check(threat.Health<=0&&w.Log!.Ship(guard).CounterMissileKills==1,"Counter round physically destroys the incoming missile");
        Check(!w.LaunchCounterMissile(guard,threat).Fired,"Spent target cannot consume another round");

        var sideways=CruiserTrials.DefenseFixture("earth_air_defense_cruiser");
        sideways.Guard.Place(Vec3d.Zero,new Quaternion(Vector3.Up,Mathf.Pi/2));
        var lateral=CruiserTrials.AddThreat(sideways.World,sideways.Attacker,sideways.Anchor,new(0,0,-11000),Vector3.Back*1200);
        sideways.World.Sensors.Update(sideways.World.Ships,0,true,sideways.World.Probes,sideways.World.Missiles);
        sideways.World.Step();
        Check(sideways.Guard.Ordnance.Missiles==20,"Off-axis threat cannot snap-launch through the real firing arc");
        Check(sideways.World.BrainOf(sideways.Guard)!.SmallTarget==lateral&&sideways.Guard.Control.HelmForward!=null,"AA turns toward an observed off-axis threat without abandoning its station");
        CruiserTrials.Step(sideways.World,2);
        Check(sideways.Guard.Ordnance.Missiles<20,"Finite hull turn eventually admits an in-arc launch");

        var lost=CruiserTrials.DefenseFixture("earth_air_defense_cruiser");
        CruiserTrials.AddThreat(lost.World,lost.Attacker,lost.Anchor,new(1000000,0,-10000),Vector3.Back*1000);
        CruiserTrials.Step(lost.World,2);
        Check(lost.Guard.Ordnance.Missiles==20&&lost.World.BrainOf(lost.Guard)!.SmallTarget==null,"Undetected missiles do not cause defensive launches");
    }
    private static void Salvo()
    {
        var w=new SimWorld();var def=CruiserTrials.Resolve("mars_missile_cruiser","""{"railgun":null}""").Definition;
        var ship=w.Add(new ShipBody("MISSILE",def,Faction.Blue));
        var target=w.Add(new ShipBody("TARGET",CruiserTrials.PassiveTarget(),Faction.Red));target.Place(new(0,0,-25000),Quaternion.Identity);
        var b=w.AttachBrain(ship,ShipOrder.HoldAt(ship.Position,target));w.Sensors.Update(w.Ships,0,true);w.Log=new BattleLog(w);
        w.Step();
        float expected=def.Sensors.Signature*(1+SensorNet.EngineSignature*ship.EngineOutput+SensorNet.HeatSignature*ship.Power.HeatFraction);
        Check(Math.Abs(SensorNet.Signature(ship)/expected-1.6f)<.0001,"Actual launch increases sensor signature by 60 percent");
        CruiserTrials.Step(w,8);
        Check(w.Log.Ship(ship).Missiles==4&&ship.Ordnance.Missiles==28,"One salvo fires exactly four rounds and consumes each once");
        target.Teleport(new(0,0,-85000));w.Sensors.Update(w.Ships,w.Time,true);b.Order=ShipOrder.AttackOn(target);b.NextThink=0;
        w.Step();float retreat=b.DesiredVelocity.Z;b.SalvoLaunched=0;b.NextThink=0;w.Step();
        Check(retreat>b.DesiredVelocity.Z+50,"Completed missile salvo opens range during the withdrawal window");
        b.Order=ShipOrder.HoldAt(ship.Position);CruiserTrials.Step(w,16);
        expected=def.Sensors.Signature*(1+SensorNet.EngineSignature*ship.EngineOutput+SensorNet.HeatSignature*ship.Power.HeatFraction);
        Check(Math.Abs(SensorNet.Signature(ship)/expected-1)<.0001,"Launch emission expires independently of normal engine and heat signature");
        ship.Ordnance.Reset();Check(double.IsNegativeInfinity(ship.Ordnance.LastMissileLaunch),"Weapon reset clears emission history");
        var empty=new SimWorld();var spent=empty.Add(new ShipBody("EMPTY",ShipDefinitions.ById("mars_missile_cruiser"),Faction.Blue));
        var far=empty.Add(new ShipBody("FAR",CruiserTrials.PassiveTarget(),Faction.Red));far.Place(new(0,0,-85000),Quaternion.Identity);
        while(spent.Ordnance.Missiles>0)spent.Ordnance.ConsumeMissile();
        var emptyBrain=empty.AttachBrain(spent,ShipOrder.AttackOn(far));empty.Sensors.Update(empty.Ships,0,true);empty.Step();
        Check(emptyBrain.DesiredVelocity.Z < -50,"Missile cruiser closes to gun range after exhausting missile stock");
    }
    private static void Measurements()
    {
        var results=CruiserTrials.Run();
        foreach(var faction in new[]{("escort","earth_gun_cruiser"),("mars_escort","mars_gun_cruiser")})
        {
            var old=results.Single(r=>r.Trial.StartsWith("gun")&&r.Hull==faction.Item1);
            var gun=results.Single(r=>r.Hull==faction.Item2);
            Check(gun.RailHits>old.RailHits&&gun.ShieldDamage>old.ShieldDamage,"Gun branch improves real sustained fire and damage at 16 km");
        }
        var baseline=results.Single(r=>r.Trial.StartsWith("missile")&&r.Hull=="mars_escort");var missile=results.Single(r=>r.Hull=="mars_missile_cruiser");
        Check(missile.Missiles==2*baseline.Missiles&&missile.MissileHits>baseline.MissileHits,"Larger salvo produces more actual hits in the same 120-second window");
        var escort=results.Single(r=>r.Trial.StartsWith("screen")&&r.Hull=="escort");var aa=results.Single(r=>r.Hull=="earth_air_defense_cruiser");
        Console.WriteLine($"Defense role: escort {escort.Intercepts}/{escort.Incoming} intercepted, {escort.IncomingHits} hit; AA {aa.Intercepts}/{aa.Incoming} intercepted, counter {aa.CounterKills}, drone {aa.DroneKills}, {aa.IncomingHits} hit");
        Check(aa.CounterKills>0&&aa.Intercepts>escort.Intercepts&&aa.IncomingHits<escort.IncomingHits,"Layered AA defense reduces the real incoming wave reaching its protected ship");
        // Earlier PD kills can leave no targets for the last defense layer.
        // Verify drone capability independently instead of requiring each weapon
        // to receive a kill from the same small deterministic wave.
        var drones=CruiserTrials.Defense("earth_air_defense_cruiser",dronesOnly:true);
        Check(drones.DroneKills>0&&drones.IncomingHits<drones.Incoming,"AA defense drones still finish damaged missiles when PD and counter-missiles are absent");
    }
}
