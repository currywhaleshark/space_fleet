using System.Diagnostics;
using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Sim;

static class MeritChecks
{
    private static int checks; private static uint sequence=100000;
    private static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
    private static ShipBody Add(SimWorld w,string name,Faction side,Vec3d at,string hull="interceptor")
    {var s=w.Add(new ShipBody(name,ShipDefinitions.ById(hull),side));s.Place(at,Quaternion.Identity);s.DamageControl.Priority=RepairPriority.Hold;return s;}
    private static void Observe(SimWorld w,double time=0){w.Sensors.Update(w.Ships,time,true,probes:w.Probes);}
    private static void Blind(ShipBody s){foreach(var m in s.Damage.Modules.Where(m=>m.Definition.Kind==ModuleKind.Sensor))s.Damage.Hurt(m,m.Health,0,0,++sequence);}
    private static ShotResult Impact(ShipBody target,float damage=100,bool kill=false,bool disable=false)
    {return new(target,target.Position,0,false,false,Array.Empty<ModuleHit>(),"fixture"){StructuralDamage=damage,CausedDestruction=kill,CausedDisable=disable};}
    private static void Hit(SimWorld w,ShipBody s,ShipBody t,double time=0,float damage=100,bool kill=false,bool disable=false,ShotCredit? credit=null,BattleWeapon weapon=BattleWeapon.Railgun)
    {w.Merits.Hit(++sequence,s,Impact(t,damage,kill,disable),weapon,time,t.Damage.Shield,credit??w.Merits.Capture(s));}
    public static void Run()
    {
        Attribution();Support();Defense();Capture();ActualWeapons();ActualOrdnance();CausalDestruction();FreezeAndBounds();
        Console.WriteLine($"PASS: {checks} combat merit attribution/provenance/objective checks");
    }
    private static void Attribution()
    {
        var w=new SimWorld();var a=Add(w,"A",Faction.Blue,Vec3d.Zero);var b=Add(w,"B",Faction.Blue,new(200,0,0));var t=Add(w,"T",Faction.Red,new(0,0,-1000));
        w.Merits.Controlled=a;Observe(w);
        Hit(w,a,t,damage:10000);Hit(w,b,t,kill:true,damage:1);
        Check(w.Merits.Ship(a)[MeritKind.Kill]==0&&w.Merits.Ship(b)[MeritKind.Kill]==1,"Final attacker, not top damage, receives the kill");
        Check(w.Merits.Personal()[MeritKind.Kill]==0&&w.Merits.Personal()[MeritKind.Assist]==1,"AI teammate kill remains separate from personal assist");
        Check(w.Merits.Team(Faction.Blue)[MeritKind.Kill]==1,"Team has one kill");
        Hit(w,b,t,kill:true);Check(w.Merits.Ship(b)[MeritKind.Kill]==1,"Repair/re-destruction cannot duplicate a kill");
        var u=Add(w,"U",Faction.Red,new(0,0,-3000));Hit(w,a,u,damage:1);Hit(w,b,u,kill:true);
        Check(w.Merits.Ship(a)[MeritKind.Assist]==1,"A graze is insufficient for assist");
        var v=Add(w,"V",Faction.Red,new(0,0,-5000));Hit(w,a,v,time:0,damage:10000);Hit(w,b,v,time:121,kill:true);
        Check(w.Merits.Ship(a)[MeritKind.Assist]==1,"Expired damage cannot assist a later kill");
        Hit(w,a,v,time:130,disable:true);Hit(w,a,v,time:131,disable:true);
        Check(w.Merits.Ship(a)[MeritKind.Disabled]==1,"Disable is separate and recovery farming does not award repeated ribbons");
        var launch=w.Merits.Capture(a);w.Merits.Controlled=b;var z=Add(w,"Z",Faction.Red,new(0,0,-6000));Hit(w,a,z,kill:true,credit:launch);
        Check(w.Merits.Personal()[MeritKind.Kill]==1,"Delayed impact preserves control at launch");
        Check(w.Merits.PersonalShip(a)[MeritKind.Kill]==1&&w.Merits.PersonalShip(b)[MeritKind.Kill]==0,"Per-ship direct-control credit stays separate across handoff");
        var env=Add(w,"ENV",Faction.Red,new(0,0,-7000));Hit(w,a,env,damage:1000);env.Damage.Breakup(0,"collision");
        for(int i=0;i<15;i++)w.Step();
        Check(w.Merits.UnattributedLosses>=1&&w.Merits.Ship(a)[MeritKind.Kill]==1,"Collision after damage does not become last-attacker kill");
        uint id=++sequence;var hit=Impact(z);w.Merits.Hit(id,a,hit,BattleWeapon.Railgun,3,0,launch);int n=w.Merits.Ship(a)[MeritKind.Hit];
        w.Merits.Hit(id,a,hit,BattleWeapon.Railgun,3,0,launch);Check(w.Merits.Ship(a)[MeritKind.Hit]==n,"Same projectile result is counted once");
    }
    private static void Support()
    {
        var w=new SimWorld();var a=Add(w,"GUN",Faction.Blue,Vec3d.Zero,"battleship");var scout=Add(w,"SCOUT",Faction.Blue,new(2000,0,-9000),"escort");var t=Add(w,"T",Faction.Red,new(0,0,-10000),"battleship");
        Blind(a);w.Merits.Controlled=scout;Observe(w);
        var solution=FireControl.Solve(a,t,0,track:w.Sensors.Track(Faction.Blue,t));
        var credit=w.Merits.Capture(a,t,solution.Valid);
        Check(solution.Valid&&credit.Source?.Owner==scout&&credit.SupportPersonal,"Actual essential link has observer provenance");
        var replacement=Add(w,"SECOND",Faction.Blue,new(0,0,-9990),"escort");Observe(w);
        Check(w.Merits.Capture(a,t,true).Source?.Owner==replacement,"Same-time sensor refresh cannot reuse another observer's cached credit");
        replacement.Damage.Breakup(0,"second source loss");
        scout.Damage.Breakup(0,"test source loss");Observe(w,.25);
        Hit(w,a,t,damage:300,credit:credit);
        Check(w.Merits.Personal()[MeritKind.ScoutHit]==1&&w.Merits.Personal().ScoutDamage==300,"Shot retains support after its observer is destroyed");
        Check(w.Merits.Capture(a,t,true).Source is null,"Lost source cannot support a newly fired round");
        Hit(w,a,t,kill:true,credit:credit);
        Check(w.Merits.Ship(scout)[MeritKind.ScoutKill]==1&&w.Merits.Ship(scout)[MeritKind.Kill]==0,"Launch observer receives causal scout kill, never direct kill");
        Check(w.Merits.Important.Any(e=>e.Kind==MeritKind.ScoutKill&&e.Attacker==a&&e.AttackId==sequence),"Scout kill keeps actual attacker and projectile identity");
        var near=Add(w,"OWN",Faction.Blue,new(0,0,-9500),"battleship");Observe(w,.3);
        Check(w.Merits.Capture(near,t,true).Source is null,"Sufficient independent own sensor yields no artificial support");
        Check(w.Merits.Capture(near,t,false).Source is null,"Manual aim never claims unused datalink");
        var probeWorld=new SimWorld();var owner=Add(probeWorld,"P",Faction.Blue,Vec3d.Zero);var gun=Add(probeWorld,"M",Faction.Blue,new(100,0,0));var enemy=Add(probeWorld,"E",Faction.Red,new(0,0,-10000));
        Blind(owner);Blind(gun);probeWorld.Merits.Controlled=owner;
        var probe=new ReconProbe{Id=99,Owner=owner,Definition=new(),Position=new(0,0,-9800),Direction=Vector3.Forward,Health=4};
        probeWorld.Sensors.Update(probeWorld.Ships,0,true,probes:new[]{probe});
        Check(probeWorld.Merits.Ship(owner)[MeritKind.Detection]==1&&probeWorld.Merits.Ship(owner)[MeritKind.Identification]==1,"Probe first contact and identification belong to owner");
        probeWorld.Sensors.Update(probeWorld.Ships,0,true,probes:new[]{probe});
        Check(probeWorld.Merits.Ship(owner)[MeritKind.Detection]==1,"Repeated observation cannot farm discovery");
        Check(probeWorld.Merits.Capture(gun,enemy,true).Source is null,"Probe does not invent high-quality gun lock");
        Check(probeWorld.Merits.Capture(gun,enemy,true,true).Source?.ObjectId==99,"Contact-based missile guidance retains probe identity");
        var missile=new Missile{Id=123,Shooter=gun,Target=enemy,Definition=gun.Definition.Missiles!,Credit=probeWorld.Merits.Capture(gun,enemy)};
        probeWorld.Merits.MissileGuidance(missile,2);
        missile.SeekerTarget=enemy;probeWorld.Merits.MissileGuidance(missile,2);
        Check(Math.Abs(probeWorld.Merits.MissileCredit(missile).SupportFraction-.5f)<.001,"Autonomous seeker time is excluded from support share");
        probeWorld.Merits.MissileGuidance(missile,20);
        Check(probeWorld.Merits.MissileCredit(missile).Source is null,"Tiny early link cannot claim an otherwise independent flight");
        var linked=missile.SupportCredit with{SupportFraction=1};
        probe.Health=0;probeWorld.Sensors.Update(probeWorld.Ships,0,true,probes:Array.Empty<ReconProbe>());
        Hit(probeWorld,gun,enemy,damage:200,credit:linked);
        Check(probeWorld.Merits.Ship(owner).ScoutDamage==200,"Destroyed probe retains already-used guidance provenance");
        Hit(probeWorld,gun,enemy,damage:200,credit:probeWorld.Merits.Capture(gun,enemy,true,true));
        Check(probeWorld.Merits.Ship(owner).ScoutDamage==200,"New independent attack cannot inherit destroyed probe credit");
    }
    private static void Defense()
    {
        var w=new SimWorld();var a=Add(w,"PD",Faction.Blue,Vec3d.Zero);var ally=Add(w,"FLAG",Faction.Blue,new(0,0,1000),"battleship");var red=Add(w,"ENEMY",Faction.Red,new(0,0,-5000));
        w.Rules=new BattleRules(w,BattleObjective.Flagship,flagships:new[]{ally,red});w.Merits.Controlled=a;
        foreach(var weapon in new[]{BattleWeapon.Railgun,BattleWeapon.PointDefense,BattleWeapon.DefenseDrone,BattleWeapon.Laser,BattleWeapon.Missile})
        {
            var m=new Missile{Id=++sequence,Shooter=red,Target=ally,Definition=red.Definition.Missiles!,Position=Vec3d.Zero,Velocity=new(0,0,1000)};
            w.Merits.Intercept(a,m,weapon,0);
        }
        Check(w.Merits.Personal()[MeritKind.MissileIntercept]==5&&w.Merits.Personal()[MeritKind.FlagshipProtection]==5,"All defense channels preserve actual final defender and threatened flagship");
        var away=new Missile{Id=++sequence,Shooter=red,Target=ally,Definition=red.Definition.Missiles!,Velocity=new(0,0,-1000)};
        w.Merits.Intercept(a,away,BattleWeapon.PointDefense,0);
        Check(w.Merits.Personal()[MeritKind.FlagshipProtection]==5,"Departing missile near flagship does not earn protection");
        var am=new Missile{Id=++sequence,Shooter=red,Target=ally,Definition=red.Definition.Missiles!,Assault=red.Definition.Antimatter,Weapon=BattleWeapon.Antimatter};
        w.Merits.Intercept(a,am,BattleWeapon.DefenseDrone,0);
        Check(w.Merits.Personal()[MeritKind.TorpedoIntercept]==1,"AM intercepted separately from regular missiles");
        w.Merits.Intercept(ally,am,BattleWeapon.PointDefense,0);
        Check(w.Merits.Team(Faction.Blue)[MeritKind.TorpedoIntercept]==1&&w.Merits.Ship(ally)[MeritKind.TorpedoIntercept]==0,"One removal cannot be attributed to two defenders");
    }
    private static void Capture()
    {
        var w=new SimWorld();var a=Add(w,"A",Faction.Blue,Vec3d.Zero);var b=Add(w,"B",Faction.Blue,new(500,0,0),"battleship");Add(w,"R",Faction.Red,new(0,0,100000));w.Merits.Controlled=a;
        var zone=new CaptureZone("B",Vec3d.Zero);w.Rules=new BattleRules(w,BattleObjective.Domination,new[]{zone},10000);
        for(int i=0;i<3000;i++)w.Rules.Evaluate((i+1)*SimWorld.TickDelta);
        Check(zone.Owner==Faction.Blue&&w.Merits.Ship(a)[MeritKind.Capture]==1&&w.Merits.Ship(b)[MeritKind.Capture]==1,"Capture completion shared exactly once by actual contributors");
        Check(w.Merits.Team(Faction.Blue)[MeritKind.Capture]==1,"One team capture event despite two contributor awards");
        Check(Math.Abs(w.Merits.Team(Faction.Blue).CaptureProgress-100)<.001,"Weighted progress sums to actual capture delta");
        int count=w.Merits.Ship(a)[MeritKind.Capture];a.Place(new(0,0,100000),Quaternion.Identity);a.Place(Vec3d.Zero,Quaternion.Identity);
        w.Rules.Evaluate(51);Check(w.Merits.Ship(a)[MeritKind.Capture]==count,"Re-entering owned objective gives no repeat capture");
        var raider=w.Ships.Single(s=>s.Faction==Faction.Red);raider.Place(Vec3d.Zero,Quaternion.Identity);
        for(int i=0;i<360;i++)w.Rules.Evaluate(52+i*SimWorld.TickDelta);
        Check(w.Merits.Team(Faction.Blue).CaptureProgress<100.001&&w.Merits.Ship(a).DefenseSeconds>5,"Contest awards defense time without fake capture progress");
        raider.Place(new(0,0,100000),Quaternion.Identity);w.Rules.Evaluate(59);
        Check(w.Merits.Ship(a)[MeritKind.Defense]==1&&w.Merits.Team(Faction.Blue)[MeritKind.Defense]==1,"Successful defense shared once per contest");
        a.Place(new(0,0,100000),Quaternion.Identity);b.Place(new(0,0,100000),Quaternion.Identity);raider.Place(Vec3d.Zero,Quaternion.Identity);
        for(int i=0;i<6000;i++)w.Rules.Evaluate(60+i*SimWorld.TickDelta);
        Check(zone.Owner==Faction.Red&&w.Merits.Ship(raider)[MeritKind.Capture]==1,"Recapture belongs to actual opposing contributor");
    }
    private static void ActualWeapons()
    {
        foreach(bool laser in new[]{false,true})
        {
            var w=new SimWorld();var def=laser?FleetLoadouts.ResolveFit(FleetLoadouts.Empty("interceptor") with {Components=new(MainGun:"main-laser")}).Definition:ShipDefinitions.ById("interceptor");
            var s=w.Add(new ShipBody("SHOOTER",def,Faction.Blue));s.Place(Vec3d.Zero,Quaternion.Identity);var t=Add(w,"T",Faction.Red,new(0,0,-3000),"battleship");
            w.Log=new BattleLog(w);w.Merits.Controlled=s;
            for(int i=0;i<120;i++){w.FireRailgun(s,Vector3.Forward);w.Step();}
            Check(w.Merits.Ship(s)[MeritKind.Hit]>0,$"Actual {(laser?"laser":"railgun")} collision reaches ledger");
            if(laser)Check(w.Merits.Ship(s)[MeritKind.Hit]<=4&&w.Log.Ship(s).LaserHits>w.Merits.Ship(s)[MeritKind.Hit],"Laser damage pulses form bounded half-second hit units");
            Check(w.Merits.Ship(s).Damage>=w.Log.Ship(s).ShieldDamageInflicted,"Actual damage is not reduced by presentation aggregation");
        }
    }
    private static void ActualOrdnance()
    {
        foreach(bool am in new[]{false,true})
        {
            var (w,s,t)=AntimatterChecks.Fixture();w.Merits.Controlled=s;
            if(am){s.Ordnance.Antimatter.BeginArming();AntimatterChecks.Step(w,2.5);Check(w.LaunchAntimatter(s,t).Fired,"Real AM launch");}
            else Check(w.LaunchMissile(s,t).Fired,"Real missile launch");
            var shot=w.Missiles.Single();w.Merits.Controlled=null;
            for(int i=0;i<1200&&w.Missiles.Count>0;i++)w.Step();
            Check(w.Log!.Ship(s).MissileHits+w.Log.Ship(s).TorpedoHits==1,"Ordnance fixture physically hits");
            Check(w.Merits.Personal()[MeritKind.Hit]==1&&w.Merits.Personal().Damage>0,"Actual missile/AM damage preserves launch control");
            Check(w.Merits.Events.Any(e=>e.Weapon==(am?BattleWeapon.Antimatter:BattleWeapon.Missile)&&e.Kind==MeritKind.Hit),"Actual weapon kind carried through explosion");
        }
    }
    private static void CausalDestruction()
    {
        foreach(var kind in new[]{ModuleKind.Reactor,ModuleKind.Magazine,ModuleKind.AntimatterContainment})
        {
            var w=new SimWorld();var shooter=Add(w,"A",Faction.Blue,new(-10000,0,0));
            var definition=ShipDefinitions.ById("interceptor");var j=JsonNode.Parse(definition.ToJson())!.AsObject();
            var part=definition.Modules.First(m=>m.Kind==kind);
            foreach(var m in j["Modules"]!.AsArray())m!["CriticalChance"]=m["Id"]!.GetValue<string>()==part.Id && kind!=ModuleKind.AntimatterContainment?1:0;
            var target=w.Add(new ShipBody("T",ShipDefinition.Parse(j.ToJsonString()),Faction.Red));target.Place(Vec3d.Zero,Quaternion.Identity);
            target.Damage.AbsorbShield(1e9f,0);
            var ray=DamageRay.Apply(target,Vec3d.From(part.Center-Vector3.Right*5000),Vector3.Right,new(1e8f,1e8f,1e8f,10000),0,++sequence);
            w.Merits.Hit(sequence,shooter,ray,BattleWeapon.Railgun,0,0,new(true));
            Check(ray.CausedDestruction&&w.Merits.Ship(shooter)[MeritKind.Kill]==1,"Actual catastrophic ray attributes one causal kill");
            Check(w.Merits.Important.Any(e=>e.Kind==MeritKind.Kill&&e.Destruction==target.Damage.Destruction),"Catastrophe reason retained separately from credit");
        }
        var envWorld=new SimWorld();var me=Add(envWorld,"ME",Faction.Blue,Vec3d.Zero);var victim=Add(envWorld,"VICTIM",Faction.Red,new(0,0,-1000));
        Hit(envWorld,me,victim,damage:1000);victim.Damage.Catastrophe(0,"external containment failure");
        for(int i=0;i<15;i++)envWorld.Step();
        envWorld.Merits.Freeze(envWorld.Time);Check(envWorld.Merits.Outcome!.Unattributed.Single().Cause==DestructionKind.Antimatter,"Unattributed loss retains explicit cause at outcome");
        Check(envWorld.Merits.Ship(me)[MeritKind.Kill]==0,"Unattributed containment failure never credits unrelated prior attacker");
    }
    private static void FreezeAndBounds()
    {
        var w=new SimWorld();var a=Add(w,"A",Faction.Blue,Vec3d.Zero);var t=Add(w,"T",Faction.Red,new(0,0,-1000));w.Merits.Controlled=a;Observe(w);
        for(int i=0;i<2000;i++)Hit(w,a,t,damage:1);
        Check(w.Merits.StoredEvents==BattleMeritLedger.EventCapacity&&w.Merits.Personal()[MeritKind.Hit]==2000,"Fixed notification ring retains exact totals");
        Check(w.Merits.Events.Select(e=>e.Id).Distinct().Count()==w.Merits.StoredEvents,"Stable unique event IDs");
        Blind(a);Observe(w);t.Damage.Breakup(0,"fixture");Hit(w,a,t,kill:true);
        Check(w.Merits.Personal()[MeritKind.Kill]==1&&w.Merits.Personal(true)[MeritKind.Kill]==0,"Unobserved kill recorded internally without revealing HUD score");
        a.Damage.Reset();Observe(w);for(int i=0;i<15;i++)w.Step();
        Check(w.Merits.Personal(true)[MeritKind.Kill]==1,"Sensor-confirmed destruction releases delayed ribbon once");
        w.Merits.Freeze(w.Time);double damage=w.Merits.Outcome!.Personal.Damage;
        Check(w.Merits.Outcome.PersonalShip(a).Damage==damage,"Frozen snapshot preserves individual controlled ship contributions");
        var u=Add(w,"LATE",Faction.Red,new(0,0,-6000));Hit(w,a,u,kill:true);
        Check(w.Merits.Outcome.Personal.Damage==damage&&w.Merits.Personal()[MeritKind.Kill]==1,"Spectator continuation cannot mutate official totals");
        Check(new SimWorld().Merits.Personal()[MeritKind.Kill]==0,"Next sortie begins with clean merit state");
    }
    public static void Regression(bool smoke=false)
    {
        foreach(bool large in (smoke?new[]{true}:new[]{false,true}))foreach(var field in (smoke?new[]{Battlefield.AsteroidCluster}:Enum.GetValues<Battlefield>()))foreach(var objective in (smoke?new[]{BattleObjective.Domination}:Enum.GetValues<BattleObjective>()))
        {
            var config=new BattleConfig{Seed=12,FieldSeed=12,Battlefield=field,Objective=objective,StartDistance=40000,
                RedDesign=DesignFamily.Mars,Match=large?Matchmaking.Create(12,"interceptor"):null};
            string? baseline=null,merit=null,physical=null;double off=0,on=0;
            foreach(int pass in new[]{0,1,2})
            {
                var w=new SimWorld();w.Merits.Enabled=pass!=0;BattleSetup.Spawn(w,config);
                var timer=Stopwatch.StartNew();long allocated=GC.GetAllocatedBytesForCurrentThread();
                for(int i=0;i<60*(smoke?30:120);i++)w.Step();timer.Stop();
                string summary=w.Log!.Summary();
                string state=string.Join(";",w.Ships.Select(s=>$"{s.Callsign}:{s.Position}:{s.Velocity}:{s.Orientation}:{s.Damage.Shield:R}:{s.Damage.StructureHealth:R}:{s.Damage.Destroyed}:{string.Join(",",s.Damage.Modules.Select(m=>m.Health.ToString("R")))}"));
                string fingerprint=string.Join(";",w.Ships.Select(s=>$"{s.Callsign}:{w.Merits.Ship(s)[MeritKind.Hit]}:{w.Merits.Ship(s)[MeritKind.Kill]}:{w.Merits.Ship(s).Damage:R}:{w.Merits.Ship(s).ScoutDamage:R}"));
                if(pass==0){baseline=summary;physical=state;off=timer.Elapsed.TotalMilliseconds;}
                else {Check(state==physical,"Exact ship pose, shield, structure and module state remain unchanged");Check(summary==baseline,$"Observer preserves exact battle summary: {large}/{field}/{objective}");if(pass==1){merit=fingerprint;on=timer.Elapsed.TotalMilliseconds;}else Check(merit==fingerprint,"Deterministic merit totals on repeated same-seed battle");}
                Console.WriteLine($"merit-load ships={w.Ships.Count} field={field} mode={objective} pass={pass} ms={timer.Elapsed.TotalMilliseconds:0} allocMB={(GC.GetAllocatedBytesForCurrentThread()-allocated)/1048576.0:0.0} events={w.Merits.StoredEvents} pending={w.Merits.PendingCount} shots={w.Log.Side(Faction.Blue).Rails+w.Log.Side(Faction.Red).Rails}");
            }
            Console.WriteLine($"merit-overhead {large}/{field}/{objective}: {(on/off-1)*100:0.0}%");
        }
        Console.WriteLine($"PASS: {checks} merit deterministic battle regression checks");
    }
}
