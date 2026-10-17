using System.Diagnostics;
using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Sim;

static class LaserChecks
{
    private static int checks;
    private static void Check(bool ok,string why) { if(!ok)throw new Exception(why);checks++; }
    public static void Run()
    {
        Definitions(); CadenceAndShield(); FocusAndDamage(); LinesAndSensors(); HeatAndDamage(); SmallTargets(); AutoFire();
        Console.WriteLine($"PASS: {checks} laser channel/damage/heat/sensor/fit checks");
    }
    private static ShipDefinition Edit(ShipDefinition d,Action<JsonObject> edit)
    { var j=JsonNode.Parse(d.ToJson())!.AsObject();edit(j);return ShipDefinition.Parse(j.ToJsonString(),strict:true); }
    private static ShipDefinition Laser(string id="interceptor",bool cool=true)
    {
        var d=FleetLoadouts.ResolveFit(FleetLoadouts.Empty(id) with{Components=new(MainGun:"main-laser")}).Definition;
        return Edit(d,j=>{
            j["PointDefense"]=null;j["DefenseDrones"]=null;
            if(cool) j["Power"]!["HeatCapacityMj"]=1e9;
        });
    }
    private static ShipDefinition Target()
    {
        var d=ShipDefinitions.ById("escort");
        // Controlled plate fixture enclosing every module: no weak spot in the entry armor.
        var h=new HullSection("fixture","test plate",Vector3.Zero,new(80,80,160),new ArmorPlate(100));
        return Edit(d,j=>{
            j["HullSections"]=JsonNode.Parse(ShipDefinition.SerializeData(new[]{h}));
            j["PointDefense"]=null;j["DefenseDrones"]=null;j["StructureHitPoints"]=10000;
            j["Shield"]!["Capacity"]=1e7;
            j["Modules"]=JsonNode.Parse(ShipDefinition.SerializeData(d.Modules.Select(m=>m with{HitPoints=1e7f,CriticalChance=0}).ToArray()));
        });
    }
    private static (SimWorld W,ShipBody S,ShipBody T) Fixture(bool shield=true,double offset=0,bool cool=true)
    {
        var w=new SimWorld();var s=w.Add(new ShipBody("LASER",Laser(cool:cool),Faction.Blue));
        var t=w.Add(new ShipBody("PLATE",Target(),Faction.Red));
        var origin=new Vec3d(offset,-offset,offset);s.Place(origin,Quaternion.Identity);t.Place(origin+new Vec3d(0,0,-3000),Quaternion.Identity);
        s.Control=t.Control=new ShipControl{FlightAssist=false};s.DamageControl.Priority=t.DamageControl.Priority=RepairPriority.Hold;
        if(!shield)foreach(var m in t.Damage.Modules.Where(m=>m.Definition.Kind==ModuleKind.ShieldEmitter))t.Damage.Hurt(m,m.Health,0,0,1);
        w.Log=new BattleLog(w);return(w,s,t);
    }
    private static void Burn(SimWorld w,ShipBody s,int ticks,Vector3? direction=null)
    { for(int i=0;i<ticks;i++){w.FireRailguns(s,direction??Vector3.Forward);w.Step();} }
    private static void Definitions()
    {
        foreach(var d in FleetLoadouts.Hulls)
        {
            string before=d.ToJson();var chosen=Laser(d.Id);var body=new ShipBody("FIT",chosen,Faction.Blue);
            Check(chosen.Battery!.Weapon.Railgun is null&&chosen.Battery.Weapon.Laser is not null,"Exclusive laser schema");
            Check(body.Railguns.All(g=>g.IsLaser&&g.Rounds==0&&g.HasAmmunition),"Every hull mount has an energy emitter, no rounds");
            Check(chosen.Railgun!.Mounts?.Length==d.Railgun!.Mounts?.Length,"Original fixed/turret layout retained");
            Check(ShipDefinition.Parse(chosen.ToJson(),true).ToJson()==chosen.ToJson(),"Laser fit round trips");
            Check(d.ToJson()==before,"Catalog hull preserved");
        }
        var d0=Laser();
        foreach(float bad in new[]{0,-1,float.NaN,float.PositiveInfinity})
        {
            bool rejected=false;
            try{Edit(d0,j=>j["MainBattery"]!["Weapon"]!["Laser"]!["HeatMw"]=bad);}
            catch(Exception e) when(e is System.IO.InvalidDataException or ArgumentException){rejected=true;}
            Check(rejected,"Invalid heat rejected");
        }
    }
    private static void CadenceAndShield()
    {
        foreach(double offset in new[]{0.0,1e12})
        {
            var(w,s,t)=Fixture(offset:offset);float before=t.Damage.Shield;
            Burn(w,s,60);
            var laser=s.Railgun!.Laser!;
            Check(laser.Pulses==15,$"15 Hz continuous pulses: {laser.Pulses}");
            Check(w.Projectiles.Count==0&&s.Railgun.Rounds==0,"No projectile or ammunition consumption");
            Check(t.Damage.Shield<before&&t.Damage.Modules.All(m=>m.Health==m.Definition.HitPoints),"Shield absorbs without module penetration");
            Check(laser.FocusFraction==0&&laser.ShieldHit,"No focus amplification on shield");
            Check(laser.HitShip==t&&((laser.HitLocal-t.Definition.ShieldEnvelope.Center)/t.Definition.ShieldEnvelope.Radii).Length()> .999,"Beam terminates on shield shell");
            Check(w.Log!.Ship(s).Rails==0&&w.Log.Ship(s).LaserPulses==15,"Laser telemetry does not masquerade as rails");
            SimWorld.StopLasers(s);ulong pulses=laser.Pulses;for(int i=0;i<60;i++)w.Step();
            Check(!laser.Active&&laser.Pulses==pulses,"Explicit release stops without a tail damage pulse");
            w.FireRailgun(s,Vector3.Forward);for(int i=0;i<60;i++)w.Step();
            Check(laser.Pulses==pulses,"Single stale input cannot emit a full pulse");
            for(int i=0;i<30;i++){w.FireRailgun(s,Vector3.Forward);w.Step();SimWorld.StopLasers(s);w.Step();}
            Check(laser.Pulses==pulses,"Click spam cannot obtain free full-energy pulses");
        }
    }
    private static void FocusAndDamage()
    {
        var(w,s,t)=Fixture(false);
        Burn(w,s,60);Check(t.Damage.StructureHealth==t.Definition.StructureHitPoints,"Unfocused low penetration cannot breach plate");
        Burn(w,s,480);var laser=s.Railgun!.Laser!;
        Check(laser.FocusFraction>.99&&t.Damage.StructureHealth<t.Definition.StructureHitPoints,"Sustained same patch ablates armor and reaches structure");
        string geometry=ShipDefinition.SerializeData(t.Definition.HullSections);
        Burn(w,s,4,new Vector3(.02f,0,-1).Normalized());
        Check(laser.FocusFraction<.03,"Moving to another patch cannot transfer focus");
        Check(ShipDefinition.SerializeData(t.Definition.HullSections)==geometry,"Ablation never changes permanent armor data");
        Burn(w,s,180);Check(laser.FocusFraction>.3,"Same patch rebuilds focus");
        t.Orientation=new Quaternion(Vector3.Up,.3f);Burn(w,s,4);
        Check(laser.FocusFraction<.03,"Hull rotation moves the struck local patch and resets focus");
        t.Orientation=Quaternion.Identity;
        SimWorld.StopLasers(s);for(int i=0;i<61;i++)w.Step();Check(laser.FocusFraction==0,"Interruption decays within one second");
        Burn(w,s,180);t.Damage.Reset();Burn(w,s,4);Check(laser.FocusFraction<.4&&laser.ShieldHit,"Restored shield prevents applying stored ablation");
        w.ResetWeapons();Check(laser.Pulses==0&&laser.FocusFraction==0&&!laser.Active&&laser.HitShip is null,"Reset clears channel, focus and presentation state");
    }

    private static void AutoFire()
    {
        var(w,s,t)=Fixture(cool:false);w.Sensors.Update(w.Ships,0,true);
        w.AttachBrain(s,ShipOrder.HoldAt(s.Position));
        Check(w.TryAutoFire(s,t,null,10,out _),"AI can start a laser using observed lock");
        s.Power.AddHeat(s.Definition.Power.HeatCapacityMj*.86f);
        Check(!w.TryAutoFire(s,t,null,10,out _)&&s.Railgun!.Laser!.AutoCooling,"AI interrupts before overheat");
        s.Power.Reset();
        Check(w.TryAutoFire(s,t,null,10,out _)&&!s.Railgun!.Laser!.AutoCooling,"AI resumes only after cooling hysteresis");
        foreach(var m in s.Damage.Modules.Where(m=>m.Definition.Kind==ModuleKind.Sensor))s.Damage.Hurt(m,m.Health,0,0,1);
        var scout=w.Add(new ShipBody("SCOUT",ShipDefinitions.ById("escort"),Faction.Blue));scout.Place(new(4000,0,0),Quaternion.Identity);
        w.Sensors.Update(w.Ships,w.Time,true);
        var linked=FireControl.Solve(s,t,w.Time,track:w.Sensors.Track(s.Faction,t));
        Check(linked.Valid&&linked.ViaDatalink&&linked.FlightTime==0,"Laser fire control reuses allied lock after own sensor destruction");
        var defense=new SimWorld{Doctrine=new FleetDoctrine()};
        var anchor=defense.Add(new ShipBody("ANCHOR",ShipDefinitions.ById("battleship"),Faction.Blue));
        defense.AddSquadron("BG",Faction.Blue,SquadronRole.BattleGroup,new[]{anchor});
        var light=defense.Add(new ShipBody("ENERGY-WING",Edit(Laser(),j=>{j["Missiles"]=null;j["Antimatter"]=null;}),Faction.Blue));
        light.Place(new(0,0,-7000),Quaternion.Identity);defense.AttachBrain(light,ShipOrder.HoldAt(light.Position));
        var wing=defense.AddSquadron("W",Faction.Blue,SquadronRole.InterceptorWing,new[]{light});
        defense.EnableCommander(Faction.Blue);defense.OrderWing(wing,InterceptorMission.AirDefense,anchor);
        for(int i=0;i<60;i++)defense.Step();
        Check(defense.BrainOf(light)!.Order.Kind==OrderKind.Hold,"Ammo-free laser wing keeps its defense station after expendable weapons are exhausted");
    }
    private static void LinesAndSensors()
    {
        var(w,s,t)=Fixture();float shield=t.Damage.Shield;
        w.Obstacles.Add(new("BLOCK",new(0,0,-1500),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,200)));
        Burn(w,s,60);Check(t.Damage.Shield==shield&&s.Railgun!.Laser!.HitShip is null,"Obstacle stops beam before target");
        Check(Math.Abs(s.Railgun!.Laser!.End.Z+1300)<.1,"Beam ends at physical obstacle surface");
        var(fw,fs,ft)=Fixture();var ally=fw.Add(new ShipBody("FRIEND",Target(),Faction.Blue));ally.Place(new(0,0,-1500),Quaternion.Identity);
        fw.Log=new BattleLog(fw);float friendShield=ally.Damage.Shield;Burn(fw,fs,30);
        Check(ally.Damage.Shield<friendShield&&ft.Damage.Shield==shield,"First allied hull blocks and receives manual ray");
        var(sw,ss,st)=Fixture();sw.Sensors.Update(sw.Ships,0,true);
        var track=sw.Sensors.Track(ss.Faction,st);
        var a=FireControl.Solve(ss,st,0,track:track);
        st.Position+=new Vec3d(50,40,20);st.Velocity=new(400,500,600);
        var b=FireControl.Solve(ss,st,0,track:track);
        Check(a.Valid&&a.Direction==b.Direction&&a.FlightTime==0,"Instantaneous aim uses saved observation, not hidden live pose");
        st.Place(new(0,0,-3000),Quaternion.Identity);
        float signature=SensorNet.Signature(ss);Burn(sw,ss,30);
        Check(SensorNet.Signature(ss)>signature*1.9,"Active emitter increases signature");
        var bearing=sw.Sensors.LaserBearings(Faction.Red,sw.Time).Single();
        Check(bearing.Direction.Dot(Vector3.Back)>.999&&bearing.ReceiverPosition==st.Position,"Recipient faction gets incoming direction only");
        SimWorld.StopLasers(ss);for(int i=0;i<200;i++)sw.Step();
        Check(!sw.Sensors.LaserBearings(Faction.Red,sw.Time).Any(),"Bearing contacts expire");
        Check(ss.Railgun!.Laser!.SignatureBoost==0,"Emission signature decays after ceasefire");
        Check(ss.Railgun.Laser.Definition.Attenuation(100000)<.01,"Long-range energy disperses");
    }
    private static void HeatAndDamage()
    {
        var(w,s,t)=Fixture(cool:false);bool overheated=false;
        for(int i=0;i<3600;i++) {w.FireRailgun(s,Vector3.Forward);w.Step();if(s.Power.Overheated){overheated=true;break;}}
        Check(overheated,"Continuous emission eventually overheats");
        ulong count=s.Railgun!.Laser!.Pulses;w.Step();Check(!s.Railgun.Ready&&!s.Railgun.Laser.Active&&s.Railgun.Laser.Pulses==count,"Overheat blocks channel");
        SimWorld.StopLasers(s);for(int i=0;i<3600&&s.Power.Overheated;i++)w.Step();
        Check(!s.Power.Overheated&&s.Railgun.Ready,"Cooling hysteresis allows later restart");
        var(mw,ms,mt)=Fixture();foreach(var m in ms.Damage.Modules.Where(m=>m.Definition.Kind==ModuleKind.Magazine))ms.Damage.Hurt(m,m.Health,0,0,1);
        Burn(mw,ms,30);Check(ms.Railgun!.Laser!.Pulses>0&&ms.Damage.WeaponsFraction>0,"Destroyed ammunition magazines do not disable energy emitter or primary system status");
        ms.Damage.Hurt(ms.Damage.Module(ms.Railgun.Definition.ModuleId),1e9f,mw.Time,0,1);mw.Step();
        Check(!ms.Railgun.Ready&&!ms.Railgun.Laser.Active,"Destroyed emitter stops firing");
        ms.Damage.Reset();ms.Railgun.Reset();Burn(mw,ms,8);Check(ms.Railgun.Laser.Pulses==2,"Repaired emitter restarts normally");
        ms.Damage.Breakup(mw.Time,"test");mw.Step();Check(!ms.Railgun.Laser.Active,"Destroyed shooter cannot keep emitting");
    }
    private static void SmallTargets()
    {
        var(w,s,t)=Fixture();var def=t.Definition.Missiles!;
        var missile=new Missile{Id=1,Shooter=t,Target=s,Definition=def with{AccelG=0},Health=.01f,Position=new(0,0,-500),
            PrevPosition=new(0,0,-500),Velocity=Vector3.Zero,LaunchDirection=Vector3.Back,NextSeekerCheck=100,AimPoint=s.Position};
        ((List<Missile>)w.Missiles).Add(missile);Burn(w,s,4);
        Check(w.Missiles.Count==0&&s.Ordnance.Interceptor.MissileKills==1,"Ray physically intercepts a missile with no proximity fuze");
        Check(w.Impacts.All(i=>i.Hit.Target!=t),"Small target absorbs the pulse before ship behind it");
        var(pw,ps,pt)=Fixture();
        var probe=new ReconProbe{Id=7,Owner=pt,Definition=ps.Definition.ReconProbes!,Health=.01f,
            Position=new(0,0,-500),PreviousPosition=new(0,0,-500),Destination=new(0,0,-500),Direction=Vector3.Forward};
        ((List<ReconProbe>)pw.Probes).Add(probe);Burn(pw,ps,4);
        Check(!probe.Active&&ps.Ordnance.Interceptor.ProbeKills==1,"Laser ray destroys an actual recon probe");
        var dw=new SimWorld();var ds=dw.Add(new ShipBody("LASER",Laser(),Faction.Blue));
        var carrier=dw.Add(new ShipBody("DRONES",Edit(ShipDefinitions.ById("battleship"),j=>{
            j["PointDefense"]=null;j["DefenseDrones"]!["HitPoints"]=.001;j["DefenseDrones"]!["RangeMeters"]=1;
        }),Faction.Red));
        carrier.Place(new(0,0,-3000),Quaternion.Identity);
        int index=Enumerable.Range(0,carrier.Definition.DefenseDrones!.Count).MaxBy(i=>carrier.Ordnance.Drones.WorldPosition(i).Z);
        var drone=carrier.Ordnance.Drones.WorldPosition(index);
        ds.Place(drone+new Vec3d(0,0,500)-Vec3d.From(ds.Railgun!.Definition.Muzzle),Quaternion.Identity);
        for(int i=0;i<8;i++)
        {
            dw.FireRailgun(ds,(carrier.Ordnance.Drones.WorldPosition(index)-ds.Railgun.MuzzlePosition).ToVector3().Normalized());dw.Step();
        }
        Check(!carrier.Ordnance.Drones.Alive(index),$"Laser ray physically destroys a defense drone before its carrier: end={ds.Railgun.Laser!.End}, drone={carrier.Ordnance.Drones.WorldPosition(index)}, pulses={ds.Railgun.Laser.Pulses}, hit={ds.Railgun.Laser.HitShip?.Callsign}");
    }

    // Repeatable active-channel cost fixture, no renderer or AI; deliberately synchronized pulse ticks.
    public static void Profile()
    {
        foreach(int count in new[]{24,240})
        foreach(bool firing in new[]{false,true})
        {
            var w=new SimWorld();var d=Laser("escort");
            for(int i=0;i<count;i++)
            {
                var s=w.Add(new ShipBody("P"+i,d,i<count/2?Faction.Blue:Faction.Red));
                s.Place(new((i%(count/2))*2000,0,i<count/2?0:-10000),new Quaternion(Vector3.Up,i<count/2?0:Mathf.Pi));
                s.Control=new ShipControl{FlightAssist=false};s.DamageControl.Priority=RepairPriority.Hold;
            }
            void Step(){if(firing)foreach(var s in w.Ships)w.FireRailguns(s,s.Forward);w.Step();}
            for(int t=0;t<90;t++)Step();
            var samples=new double[480];long allocated=GC.GetAllocatedBytesForCurrentThread();
            for(int t=0;t<samples.Length;t++){long start=Stopwatch.GetTimestamp();Step();samples[t]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;}
            allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;Array.Sort(samples);
            Console.WriteLine($"LASER PERF ships={count} emitters={w.Ships.Sum(s=>s.Railguns.Length)} firing={firing} p50_ms={samples[240]:F3} p95_ms={samples[456]:F3} p99_ms={samples[475]:F3} max_ms={samples[^1]:F3} bytes_tick={allocated/480} pulses={w.Ships.Sum(s=>s.Railguns.Sum(g=>(long)g.Laser!.Pulses))}");
        }
    }
}
