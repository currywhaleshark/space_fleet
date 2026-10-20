using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Godot;
using SpaceFleet.Sim;

// Opt-in measurement fixtures, never invoked by gameplay. Controlled benches and free AI
// engagements are recorded separately: no resets or privileged aiming in the AI battles.
internal static class ComponentTrials
{
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,IncludeFields=true,
        Converters={new JsonStringEnumConverter()}};
    private static readonly string[] Hulls={"battleship","mars_battleship","escort","mars_escort","interceptor","mars_interceptor"};
    private static IEnumerable<string?> Choices(string hull,ComponentSlot slot)=>new string?[]{null}.Concat(
        ShipComponents.All.Where(c=>c.Slot==slot&&ShipComponents.Available(ShipDefinitions.ById(hull),c)).Select(c=>(string?)c.Id));
    private static ShipDefinition Fit(string hull,string? part,ComponentSlot slot=ComponentSlot.MainGun)=>
        FleetLoadouts.ResolveFit(FleetLoadouts.Empty(hull) with{Components=part is null?null:new ShipComponentSelection().With(slot,part)}).Definition;
    private static ShipDefinition Edit(ShipDefinition d,Action<JsonObject> edit)
    {var j=JsonNode.Parse(d.ToJson())!.AsObject();edit(j);return ShipDefinition.Parse(j.ToJsonString(),strict:true);}
    private static ShipDefinition NoDefense(ShipDefinition d)=>Edit(d,j=>{j["PointDefense"]=null;j["DefenseDrones"]=null;});
    private static ShipBody Add(SimWorld w,string name,ShipDefinition d,Faction side,Vec3d position,Quaternion rotation=default)
    {
        var s=w.Add(new ShipBody(name,d,side));s.Place(position,rotation==default?Quaternion.Identity:rotation);
        s.Control=new ShipControl{FlightAssist=false};return s;
    }
    private static object Stats(SimWorld w,ShipBody s)=>new {s.Callsign,Hull=s.Definition.Id,Components=s.Definition.Components,
        s.Damage.Destroyed,s.Damage.Disabled,s.Damage.StructureFraction,s.Damage.Shield,
        Log=w.Log!.Ship(s),Ammo=s.Railguns.Sum(g=>g.Rounds),PdShots=s.Ordnance.PointDefense.Sum(p=>(long)p.ShotCount)};

    public static void Save(string output,string phase,int[] seeds,int jobs,double seconds,bool legacyPd=false)
    {
        if(phase is not ("all" or "bench" or "pd" or "pd-salvos" or "ordnance-salvos" or "laser-duels" or "duels" or "fleet")||jobs<1||seconds<=0||!double.IsFinite(seconds)
            ||seeds.Length==0||seeds.Distinct().Count()!=seeds.Length)throw new ArgumentException("Invalid component trial options");
        var tasks=new List<(string Id,Func<object> Run)>();
        if(phase is "all" or "bench")foreach(string id in Hulls)foreach(string? part in Choices(id,ComponentSlot.MainGun))
        {
            foreach(bool hot in new[]{false,true})tasks.Add(($"cadence-{id}-{part??"stock"}-{hot}",()=>Cadence(id,part,hot,seconds)));
            tasks.Add(($"armor-{id}-{part??"stock"}",()=>Armor(id,part)));
            tasks.Add(($"shield-{id}-{part??"stock"}",()=>Shield(id,part)));
        }
        if(phase is "all" or "pd")foreach(string id in Hulls)foreach(string? part in Choices(id,ComponentSlot.Secondary))
        foreach(int seed in seeds)foreach(bool missile in new[]{false,true})
        foreach(var exposure in new[]{(Name:"near",Range:500f,Speed:0f),(Name:"fast",Range:500f,Speed:1200f),
            (Name:"outer",Range:ShipDefinitions.ById(id).PointDefense!.RangeMeters*1.12f,Speed:0f)})
            tasks.Add(($"pd-{id}-{part??"stock"}-{seed}-{missile}-{exposure.Name}",()=>Pd(id,part,seed,missile,exposure.Name,exposure.Range,exposure.Speed,legacyPd)));
        if(phase is "all" or "duels")foreach(string id in Hulls)foreach(string? part in Choices(id,ComponentSlot.MainGun))
        foreach(int seed in seeds)foreach(bool mirror in new[]{false,true})
        foreach(double range in ShipDefinitions.ById(id).Kind switch{HullKind.Battleship=>new[]{20000d,100000d},HullKind.Escort=>new[]{6000d,40000d},_=>new[]{3000d,15000d}})
            tasks.Add(($"duel-{id}-{part??"stock"}-{seed}-{mirror}-{range}",()=>Duel(id,part,seed,mirror,range,seconds)));
        if(phase is "all" or "fleet")foreach(string id in new[]{"battleship","mars_interceptor"})foreach(string? part in Choices(id,ComponentSlot.MainGun))
        foreach(int seed in seeds)tasks.Add(($"fleet-{id}-{part??"stock"}-{seed}",()=>Fleet(id,part,seed,seconds)));
        if(phase is "pd-salvos")foreach(string id in Hulls)
        foreach(string part in new[]{"stock","barrage-before","pd-barrage","pd-longrange"})
        foreach(int seed in seeds)foreach(string threat in new[]{"light","heavy"})foreach(float speed in new[]{650f,1800f})
            tasks.Add(($"salvo-{id}-{part}-{seed}-{threat}-{speed}",()=>PdSalvo(id,part,seed,threat,speed)));
        // Modular weapons phase 5: the same controlled stream against every PD option, and every missile part against stock PD.
        if(phase is "ordnance-salvos")foreach(string id in Hulls)
        {
            var hull=ShipDefinitions.ById(id);
            var options=new[]{"stock","pd-barrage","pd-longrange"}.Concat(OrdnanceParts.Candidates(hull,OrdnanceKind.PointDefense).Where(p=>!p.Stock).Select(p=>p.Id));
            var threats=new[]{"light","heavy"}.Concat(OrdnanceParts.All.Where(p=>p.Kind==OrdnanceKind.Missile&&!p.Stock).Select(p=>p.Id));
            // Speed 0 = the threat's own motor burnout speed; "own" range starts the stream at 90% of this PD's own range.
            foreach(string option in options)foreach(string threat in threats)foreach(int seed in seeds)foreach(float speed in new[]{650f,1800f,0f})
            foreach(bool own in new[]{false,true})
                if((option=="stock"||threat is "light" or "heavy")&&!(own&&option=="stock"&&speed!=0))
                    tasks.Add(($"ordnance-{id}-{option}-{threat}-{seed}-{speed}-{(own?"own":"common")}",()=>OrdnanceSalvo(id,option,threat,seed,speed,own)));
        }
        // Laser balance: a fitted hull against the same hull on stock weapons (both with 포좌 확장 so large parts fit).
        if(phase is "laser-duels")
            foreach(var (hull,fit,ranges) in new (string,string,double[])[]{
                ("earth_air_defense_cruiser_t5","stock",new[]{8000d,20000d,40000d}),
                ("earth_air_defense_cruiser_t5","earth-laser-l,-",new[]{8000d,20000d,40000d}),
                ("earth_air_defense_cruiser_t5","earth-rail-s2,earth-rail-s2",new[]{8000d,20000d,40000d}),
                ("battleship","stock",new[]{20000d,60000d,100000d}),
                ("battleship","earth-laser-l,earth-laser-l,earth-laser-l",new[]{20000d,60000d,100000d})})
            foreach(double range in ranges)foreach(int seed in seeds)foreach(bool mirror in new[]{false,true})
                tasks.Add(($"laser-{hull}-{fit}-{range}-{seed}-{mirror}",()=>LaserDuel(hull,fit,seed,mirror,range,seconds)));
        // Laser balance: the same AA fits screening a passive anchor from four 12-missile waves (beam AA and heat).
        if(phase is "laser-duels")
            foreach(string fit in LaserAaFits)foreach(int seed in seeds)
                tasks.Add(($"laser-aa-{fit}-{seed}",()=>LaserRaid("earth_air_defense_cruiser_t5",fit,seed)));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var results=new ConcurrentBag<(string Id,object Data)>();int done=0;
        Parallel.ForEach(tasks,new ParallelOptions{MaxDegreeOfParallelism=jobs},task=>{
            results.Add((task.Id,task.Run()));int count=Interlocked.Increment(ref done);
            if(count%12==0||count==tasks.Count)Console.WriteLine($"Component trials {count}/{tasks.Count}");});
        File.WriteAllText(output,JsonSerializer.Serialize(results.OrderBy(x=>x.Id).Select(x=>new{x.Id,Result=x.Data}).ToArray(),Json));
        Console.WriteLine($"Saved {results.Count} {phase} trials: {output}");
    }

    private static object Cadence(string hull,string? part,bool priority,double seconds)
    {
        var d=Fit(hull,part);var w=new SimWorld();var s=Add(w,"GUN",NoDefense(d),Faction.Blue,Vec3d.Zero);
        if(priority)s.Power.SetPips(1,1,4,2,0);
        Vector3 aim=s.Railgun!.Mount is null?Vector3.Forward:new Vector3(1,.035f,-.4f).Normalized();
        int settle=s.Railgun.Mount is null?0:(int)Math.Ceiling((90/s.Railguns.Min(g=>Math.Min(g.Mount!.YawRate,g.Mount.ElevationRate))+1)*SimWorld.TickRate);
        for(int i=0;i<settle;i++){foreach(var g in s.Railguns)g.Aim(aim);w.Step();}
        double start=w.Time,hot=0;float peak=0;double? firstHot=null,empty=null;int shots=0;var gates=new Dictionary<FireFailure,int>();
        for(int i=0;i<(int)(seconds*SimWorld.TickRate);i++)
        {
            var fire=w.FireRailguns(s,aim);shots+=fire.Shots;
            if(!fire.Fired)gates[fire.Failure]=gates.GetValueOrDefault(fire.Failure)+1;
            peak=Math.Max(peak,s.Power.HeatFraction);
            if(s.Power.Overheated){hot+=SimWorld.TickDelta;firstHot??=w.Time-start;}
            if(s.Railguns.All(g=>g.Rounds==0))empty??=w.Time-start;
            w.Step();
        }
        return new{Kind="cadence",Hull=hull,Part=part??"stock",Priority=priority,Seconds=seconds,Shots=shots,
            FiredEnergy=shots*d.Railgun!.Energy,Ammo=s.Railguns.Sum(g=>g.Rounds),EmptyAt=empty,HotSeconds=hot,FirstHot=firstHot,PeakHeat=peak,
            Guns=s.Railguns.Length,Barrels=s.Railgun.BarrelCount,d.Railgun.Energy,d.Railgun.PenetrationMm,d.Railgun.ReloadSeconds,
            d.Railgun.ShotHeatMj,d.Railgun.AimErrorScale,Gates=gates};
    }

    private static object Armor(string hull,string? part)
    {
        var d=Fit(hull,part);var target=ShipDefinitions.For(d.Kind==HullKind.Interceptor?HullKind.Escort:HullKind.Battleship,
            d.Design==DesignFamily.Earth?DesignFamily.Mars:DesignFamily.Earth);
        var s=new ShipBody("PLATE",target,Faction.Red);int rays=0,penetrated=0,criticals=0;double modules=0,structure=0;
        foreach(var m in target.Modules)foreach(var direction in new[]{Vector3.Left,Vector3.Down,Vector3.Back,
            new Vector3(-1,0,.7f).Normalized(),new Vector3(-.3f,0,1).Normalized()})
        {
            s.Damage.Reset();var origin=Vec3d.From(m.Center-direction*target.Flight.Length*2);
            var hit=DamageRay.ApplyAtPose(s,origin,direction,d.Railgun!.Packet,0,(uint)++rays,s.Position,s.Orientation,shieldApplies:false);
            if(hit.StructuralDamage>0||hit.Modules.Count>0)penetrated++;
            modules+=hit.Modules.Sum(x=>x.Damage);structure+=hit.StructuralDamage;if(s.Damage.Destroyed)criticals++;
        }
        return new{Kind="armor",Hull=hull,Part=part??"stock",Target=target.Id,Rays=rays,Penetrated=penetrated,
            ModuleDamagePerRound=modules/rays,StructurePerRound=structure/rays,Criticals=criticals,
            ModuleDamagePerMountSalvo=modules/rays*d.Battery!.Weapon.BarrelCount};
    }

    private static object Shield(string hull,string? part)
    {
        var d=Fit(hull,part);var target=ShipDefinitions.For(d.Kind==HullKind.Interceptor?HullKind.Escort:HullKind.Battleship,
            d.Design==DesignFamily.Earth?DesignFamily.Mars:DesignFamily.Earth);
        var w=new SimWorld();var shooter=Add(w,"GUN",NoDefense(d),Faction.Blue,Vec3d.Zero);
        // One actual mount is placed abreast of a stationary target. All physical muzzles
        // aim at it independently; the target keeps its real shield/armor and regeneration.
        var gun=shooter.Railgun!;var direction=gun.Mount is null?Vector3.Forward:new Vector3(1,.1f,-.4f).Normalized();
        var victim=Add(w,"TARGET",NoDefense(target),Faction.Red,Vec3d.From(direction*5000));
        victim.DamageControl.Priority=RepairPriority.Hold;w.Log=new BattleLog(w);
        double? first=null,down=null;
        for(int tick=0;tick<180*SimWorld.TickRate;tick++)
        {
            var aim=(victim.Position-gun.MuzzlePosition).ToVector3().Normalized();
            var fire=w.FireRailgun(shooter,gun,aim);if(fire.Fired)first??=w.Time;
            w.Step();
            if(victim.Damage.Shield<=.001){down=w.Time;break;}
        }
        // Salvo impacts need not arrive in projectile-ID order. BattleLog records
        // every hit at resolution, unlike a maximum-ID cursor over visual events.
        var log=w.Log.Ship(shooter);
        return new{Kind="shield",Hull=hull,Part=part??"stock",Target=target.Id,FirstFire=first,ShieldDown=down,
            TimeFromFirst=down-first,Shots=gun.ShotCount,Hits=log.RailHits,Absorbed=log.ShieldDamageInflicted,Remaining=victim.Damage.Shield};
    }

    private static object Pd(string hull,string? part,int seed,bool missile,string exposure,float range,float speed,bool legacy)
    {
        var d=Edit(Fit(hull,part,ComponentSlot.Secondary),j=>j["DefenseDrones"]=null);
        if(legacy)d=Edit(d,j=>j["PointDefense"]!.AsObject().Remove("ShipPacket"));
        var w=new SimWorld();var defender=Add(w,"DEFENDER",d,Faction.Blue,Vec3d.Zero);
        var raider=Add(w,"RAIDER-"+seed,NoDefense(ShipDefinitions.ById("interceptor")),Faction.Red,new(0,0,-20000));
        var pd=d.PointDefense!;var normal=pd.Normals![0].Normalized();var tangent=normal.Cross(Vector3.Up);
        if(tangent.LengthSquared()<.01f)tangent=Vector3.Right;else tangent=tangent.Normalized();
        Missile? threat=null;
        if(missile)
        {
            // Acquire at close range before stepping, then move the launcher outside PD coverage.
            raider.Place(new(0,0,-500),new Quaternion(Vector3.Up,Mathf.Pi));w.Sensors.Update(w.Ships,w.Time,true);
            for(int i=0;i<seed;i++)
            {
                raider.Ordnance.Reset();var launch=w.LaunchMissile(raider,defender);
                if(!launch.Fired)throw new Exception("PD fixture launch: "+hull+" "+launch.Reason);
                if(threat is not null){threat.Health=0;threat.Age=threat.Definition.MaxFlightSeconds+1;}
                threat=w.Missiles.Last();
            }
            raider.Place(new(0,0,-20000),Quaternion.Identity);
            threat!.Age=threat.Definition.BurnSeconds+1;threat.NextSeekerCheck=double.MaxValue;
        }
        w.Log=new BattleLog(w);double? killed=null;double start=w.Time;int hits=0;uint last=0;
        for(int tick=0;tick<360;tick++)
        {
            var position=Vec3d.From(pd.Mounts[0]+normal*range+tangent*((tick/60f-3)*speed));
            if(threat is not null){threat.Position=threat.PrevPosition=position;threat.Velocity=tangent*speed;}
            else{raider.Place(position,Quaternion.Identity);raider.Velocity=tangent*speed;raider.Damage.Reset();}
            w.Step();
            foreach(var impact in w.Impacts.Where(x=>x.Id>last)){hits++;last=Math.Max(last,impact.Id);}
            if(threat is not null&&!w.Missiles.Contains(threat)){killed=w.Time-start;break;}
        }
        return new{Kind="pd",Hull=hull,Part=part??"stock",Legacy=legacy,Seed=seed,Missile=missile,Exposure=exposure,Range=range,Speed=speed,
            Shots=defender.Ordnance.PointDefense.Sum(p=>(long)p.ShotCount),Hits=hits,ShieldDamage=w.Log.Ship(defender).ShieldDamageInflicted,
            ModuleDamage=w.Log.Ship(defender).ModuleDamageInflicted,Intercepted=w.Log.Ship(defender).MissilesIntercepted,KilledAt=killed};
    }

    /// <summary>Reference hull that carries each missile part (any tier: research tiers are not enforced in resolution).</summary>
    private static MissileDefinition Threat(string threat)=>threat switch
    {
        "light"=>ShipDefinitions.ById("interceptor").Missiles!,
        "heavy"=>ShipDefinitions.ById("escort").Missiles!,
        _=>OrdnanceParts.ById(threat) is var part?FleetLoadouts.ResolveFit(FleetLoadouts.Empty(part.Family switch
            {MissileFamily.Light=>"interceptor_t3",MissileFamily.Heavy=>"battleship",_=>part.Design==DesignFamily.Mars?"mars_missile_cruiser":"escort"})
            with{Components=new ShipComponentSelection{Missile=threat}}).Definition.Missiles!:throw new ArgumentException(threat),
    };
    private static object OrdnanceSalvo(string hull,string option,string threat,int seed,float speed,bool ownRange)
    {
        var parts=option=="stock"?null:OrdnanceParts.Find(option) is not null?new ShipComponentSelection{PointDefensePart=option}
            :new ShipComponentSelection().With(ComponentSlot.Secondary,option);
        var d=Edit(FleetLoadouts.ResolveFit(FleetLoadouts.Empty(hull) with{Components=parts}).Definition,j=>j["DefenseDrones"]=null);
        var flight=Threat(threat);
        if(speed<=0)speed=MathF.Min(flight.Accel*flight.BurnSeconds,4000);
        return Stream(d,hull,option,seed,threat,flight,speed,ownRange?d.PointDefense!.RangeMeters*.9:null);
    }
    private static object PdSalvo(string hull,string part,int seed,string type,float speed)
    {
        var d=Edit(Fit(hull,part=="stock"?null:part=="barrage-before"?"pd-barrage":part,ComponentSlot.Secondary),j=>{
            j["DefenseDrones"]=null;
            if(part=="barrage-before")j["PointDefense"]!.AsObject().Remove("MissileDamagePerHit");});
        return Stream(d,hull,part,seed,type,ShipDefinitions.ById(type=="light"?"interceptor":"escort").Missiles!,speed);
    }
    private static object Stream(ShipDefinition d,string hull,string part,int seed,string type,MissileDefinition flight,float speed,double? start=null)
    {
        var w=new SimWorld();var defender=Add(w,"DEFENDER",d,Faction.Blue,Vec3d.Zero);
        var launcher=Add(w,"LAUNCHER",NoDefense(ShipDefinitions.ById("interceptor")),Faction.Red,new(0,0,-20000));
        var pd=d.PointDefense!;var first=defender.Ordnance.PointDefense[0];
        Vector3 approach=first.MountBasis*new Basis(Vector3.Up,first.CenterYaw)
            *new Basis(Vector3.Right,Mathf.DegToRad(45))*Vector3.Forward;
        Vector3 across=approach.Cross(Vector3.Up).Normalized();
        double distance=start??ShipDefinitions.ById(hull).PointDefense!.RangeMeters*.7;
        var random=new Random(seed);var offsets=Enumerable.Range(0,12).Select(_=>(float)(random.NextDouble()-.5)*80).ToArray();
        w.Log=new BattleLog(w);int launched=0;double? firstKill=null;
        while(w.Time<20)
        {
            if(launched<12&&w.Time+1e-8>=launched*.3)
            {
                // Controlled mid-flight stream: stock missile body, HP, fuze and
                // damage; exhausted motor and constant approach, no position resets.
                var position=Vec3d.From(pd.Mounts[0])+Vec3d.From(approach)*distance+Vec3d.From(across*offsets[launched]);
                Vector3 nose=(-position).ToVector3().Normalized();
                ((List<Missile>)w.Missiles).Add(new Missile{Id=(uint)(10000+seed*100+launched),Shooter=launcher,
                    Target=defender,AimPoint=defender.Position,
                    Definition=flight,Health=flight.HitPoints,Position=position,PrevPosition=position,Velocity=nose*speed,
                    NoseDirection=nose,PreviousNoseDirection=nose,Age=flight.BurnSeconds+1,NextSeekerCheck=double.MaxValue});
                launched++;
            }
            w.Step();if(w.Log.Ship(defender).MissilesIntercepted>0)firstKill??=w.Time;
            if(launched==12&&w.Missiles.Count==0)break;
        }
        var log=w.Log.Ship(defender);
        return new{Kind="pd-salvo",Hull=hull,Part=part,Seed=seed,Threat=type,Speed=speed,StartRange=distance,Launched=launched,
            Intercepted=log.MissilesIntercepted,Hits=w.Log.Ship(launcher).MissileHits,FirstKill=firstKill,Seconds=w.Time,
            Shots=defender.Ordnance.PointDefense.Sum(p=>(long)p.ShotCount),Remaining=w.Missiles.Count,
            defender.Damage.Destroyed,defender.Damage.StructureFraction,Received=log.ModuleDamage};
    }

    internal static readonly string[] LaserAaFits={"stock","earth-laser-l,-","earth-rail-s2,earth-rail-s2"};
    private static object LaserRaid(string hull,string fit,int seed)
    {
        var w=new SimWorld();var rng=new Random(seed);
        var guard=w.Add(new ShipBody("GUARD",Edit(Researched(hull,fit),j=>{j["Decoys"]=null;j["PointDefense"]=null;j["DefenseDrones"]=null;j["Missiles"]=null;}),Faction.Blue));
        var anchor=w.Add(new ShipBody("ANCHOR",CruiserTrials.PassiveTarget(),Faction.Blue));anchor.Place(new(0,0,5000),Quaternion.Identity);
        var source=w.Add(new ShipBody("SOURCE",CruiserTrials.Resolve("escort","""{"railgun":null,"pointDefense":null,"defenseDrones":null,"sensors":{"jammer":0}}""").Definition,Faction.Red));
        source.Place(new(0,0,-22000),new Quaternion(Vector3.Up,Mathf.Pi));
        foreach(var ship in w.Ships)ship.Control=new(){FlightAssist=false};
        w.AttachBrain(guard,ShipOrder.HoldAt(guard.Position));w.Log=new BattleLog(w);
        int incoming=0,cooling=0,ticks=0;float heat=0;
        while(w.Time<75)
        {
            if(incoming<48&&w.Time+1e-8>=incoming/12*15)
            {
                for(int i=0;i<12;i++,incoming++)
                {
                    var p=new Vec3d((rng.NextDouble()-.5)*3200,(i%2==0?1:-1)*(150+rng.NextDouble()*550),-10000-i/4*1000);
                    CruiserTrials.AddThreat(w,source,anchor,p,(anchor.Position-p).ToVector3().Normalized()*2500);
                }
                w.Sensors.Update(w.Ships,w.Time,true,w.Probes,w.Missiles);
            }
            w.Step();ticks++;heat=Math.Max(heat,guard.Power.HeatFraction);
            if(guard.Railguns.Any(g=>g.Laser?.Cooling==true))cooling++;
        }
        var log=w.Log.Ship(guard);
        return new{Kind="laser-aa",Hull=hull,Fit=fit,Seed=seed,Incoming=incoming,Intercepts=log.MissilesIntercepted,
            Hits=w.Log.Ship(source).MissileHits,Pulses=log.LaserPulses,log.Rails,MaxHeat=heat,CoolingShare=cooling/(double)ticks};
    }
    private static ShipDefinition Researched(string hull,string fit)=>ResearchTree.Catalog.Resolve(hull,new[]{"fire-control"},Array.Empty<EquipmentMount>(),
        fit=="stock"?null:new ShipComponentSelection{MainGuns=fit.Split(',').Select(g=>g=="-"?null:g).ToArray()}).Definition;
    private static object LaserDuel(string hull,string fit,int seed,bool mirror,double range,double seconds)
    {
        var w=new SimWorld();var random=new Random(seed);var selected=Researched(hull,fit);var stock=Researched(hull,"stock");
        var blue=Add(w,"DUEL-B",mirror?stock:selected,Faction.Blue,new(0,(random.NextDouble()-.5)*2000,0));
        var red=Add(w,"DUEL-R",mirror?selected:stock,Faction.Red,new(0,(random.NextDouble()-.5)*2000,-range),new Quaternion(Vector3.Up,Mathf.Pi));
        var a=mirror?red:blue;var b=mirror?blue:red;
        w.AttachBrain(blue,ShipOrder.AttackOn(red));w.AttachBrain(red,ShipOrder.AttackOn(blue));w.Doctrine=new FleetDoctrine();
        w.Rules=new BattleRules(w,BattleObjective.Annihilation);w.Sensors.Update(w.Ships,0,true);w.Log=new BattleLog(w);
        // Selected ship: share of ticks with a beam on, in automatic cooling, and its mean heat fraction.
        int ticks=0,beam=0,cooling=0;double heat=0;
        while(w.Time<seconds&&w.Rules.Outcome is null)
        {
            w.Step();ticks++;heat+=a.Power.HeatFraction;
            if(a.Railguns.Any(g=>g.Laser?.Active==true))beam++;
            if(a.Railguns.Any(g=>g.Laser?.Cooling==true))cooling++;
        }
        w.Log.Finish();var la=w.Log.Ship(a);var lb=w.Log.Ship(b);
        return new{Kind="laser-duel",Hull=hull,Fit=fit,Seed=seed,Mirror=mirror,Range=range,Seconds=w.Time,
            Winner=w.Rules.Outcome?.Winner==a.Faction?"selected":w.Rules.Outcome?.Winner==b.Faction?"stock":"none",
            SelectedDealt=la.ShieldDamageInflicted+la.ModuleDamageInflicted,StockDealt=lb.ShieldDamageInflicted+lb.ModuleDamageInflicted,
            SelectedModules=la.ModuleDamageInflicted,StockModules=lb.ModuleDamageInflicted,SelectedPulses=la.LaserPulses,SelectedRails=la.Rails,
            Predicted=MainGunParts.EffectiveFirepower(selected)/MainGunParts.EffectiveFirepower(stock),
            BeamShare=beam/(double)Math.Max(1,ticks),CoolingShare=cooling/(double)Math.Max(1,ticks),MeanHeat=heat/Math.Max(1,ticks),
            Emitters=a.Railguns.Select(g=>g.Laser is { } l?$"laser {l.Definition.EnergyPerSecond:0}/s pen {l.Definition.PenetrationMm:0} falloff {l.Definition.FalloffMeters:0} heat {l.Definition.HeatMw:0}"
                :$"rail {g.Definition.Energy:0} x{g.BarrelCount} v {g.Definition.MuzzleSpeed:0} reload {g.Definition.ReloadSeconds:0.00} pen {g.Definition.PenetrationMm:0}").ToArray()};
    }
    private static object Duel(string hull,string? part,int seed,bool mirror,double range,double seconds)
    {
        var w=new SimWorld();var random=new Random(seed);var selected=Fit(hull,part);var stock=ShipDefinitions.ById(hull);
        // Swap the equipped side, retaining each side's callsign, starting pose,
        // update order and deterministic hit salt. Stock/stock pairs stay identical.
        var blue=Add(w,"DUEL-B",mirror?stock:selected,Faction.Blue,new(0,(random.NextDouble()-.5)*2000,0));
        var red=Add(w,"DUEL-R",mirror?selected:stock,Faction.Red,new(0,(random.NextDouble()-.5)*2000,-range),new Quaternion(Vector3.Up,Mathf.Pi));
        var a=mirror?red:blue;var b=mirror?blue:red;
        w.AttachBrain(blue,ShipOrder.AttackOn(red));w.AttachBrain(red,ShipOrder.AttackOn(blue));w.Doctrine=new FleetDoctrine();
        w.Rules=new BattleRules(w,BattleObjective.Annihilation);w.Sensors.Update(w.Ships,0,true);w.Log=new BattleLog(w);
        double hotA=0,hotB=0;double? fireA=null,fireB=null;
        while(w.Time<seconds&&w.Rules.Outcome is null)
        {
            w.Step();if(a.Power.Overheated)hotA+=SimWorld.TickDelta;if(b.Power.Overheated)hotB+=SimWorld.TickDelta;
            if(w.Log.Ship(a).Rails>0)fireA??=w.Time;if(w.Log.Ship(b).Rails>0)fireB??=w.Time;
            if(!a.Velocity.IsFinite()||!b.Velocity.IsFinite())throw new Exception("Non-finite trial motion");
        }
        w.Log.Finish();return new{Kind="duel",Hull=hull,Part=part??"stock",Seed=seed,Mirror=mirror,Range=range,
            Seconds=w.Time,SelectedSide=a.Faction,Winner=w.Rules.Outcome?.Winner,Reason=w.Rules.Outcome?.Reason??"time-cap",
            Selected=Stats(w,a),Stock=Stats(w,b),SelectedHot=hotA,StockHot=hotB,SelectedFirstFire=fireA,StockFirstFire=fireB};
    }

    private static object Fleet(string hull,string? part,int seed,double seconds)
    {
        var baseFit=FleetLoadouts.Default(hull);
        var fit=baseFit with{Members=baseFit.Members.Select(m=>m.HullId==hull?m with{Components=part is null?null:new(MainGun:part)}:m).ToArray()};
        var unlocks=new Dictionary<string,IReadOnlySet<string>>{{hull,part is null?new HashSet<string>():new HashSet<string>{ShipComponents.UnlockKey(part)}}};
        var match=Matchmaking.CreateTiered(seed,fit,FleetLoadouts.Hulls.Select(d=>d.Id).ToHashSet(),unlocks);
        var w=new SimWorld();BattleSetup.Spawn(w,new BattleConfig{Match=match,Seed=seed});
        var calls=Enumerable.Range(0,match.Player.ShipCount).Select(match.Player.Callsign).ToHashSet();
        var owned=w.Ships.Where(s=>calls.Contains(s.Callsign)).ToArray();
        while(w.Time<seconds&&w.Rules!.Outcome is null)w.Step();w.Log!.Finish();
        if(w.Ships.Any(s=>!s.Velocity.IsFinite()||!double.IsFinite(s.Position.Length())))throw new Exception("Non-finite fleet motion");
        return new{Kind="fleet",Hull=hull,Part=part??"stock",Seed=seed,Ships=w.Ships.Count,Seconds=w.Time,
            Winner=w.Rules!.Outcome?.Winner,Selected=owned.Select(s=>Stats(w,s)).ToArray(),
            Blue=w.Log.Side(Faction.Blue),Red=w.Log.Side(Faction.Red),w.Log.FriendlyCollisions};
    }
}
