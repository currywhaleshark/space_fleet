using System.Diagnostics;
using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Game;
using SpaceFleet.Sim;

static class TacticalChecks
{
    private static int checks;
    private static void Check(bool valid,string reason){if(!valid)throw new Exception(reason);checks++;}
    private static void Reject(Action action,string reason){try{action();}catch(InvalidDataException){checks++;return;}throw new Exception(reason);}
    internal static ShipFit Fit(string hull,params string[] ids)
    {
        var basis=FleetLoadouts.ResolveFit(FleetLoadouts.Empty(hull)).Definition;
        var bays=TacticalModulesCatalog.Bays(basis);
        return FleetLoadouts.Empty(hull) with{Components=new(){TacticalModules=ids.Select((id,i)=>new TacticalMount(bays[i].Id,id)).ToArray()}};
    }
    private static ShipBody Ship(params string[] ids)=>new("TACTICAL",FleetLoadouts.ResolveFit(Fit("battleship",ids)).Definition,Faction.Blue);
    private static void Step(SimWorld world,double seconds){for(int i=0;i<(int)Math.Round(seconds*60);i++)world.Step();}
    public static void Run()
    {
        foreach(var hull in FleetLoadouts.Hulls)
        {
            var basis=FleetLoadouts.ResolveFit(FleetLoadouts.Empty(hull.Id)).Definition;
            var bays=TacticalModulesCatalog.Bays(basis);Check(bays.Length>0,hull.Id+" has physical tactical bays");
            foreach(var device in TacticalModulesCatalog.All.Where(d=>TacticalModulesCatalog.Restriction(basis,d) is null))
            {
                var fit=FleetLoadouts.ResolveFit(Fit(hull.Id,device.Id));
                Check(fit.Definition.Modules.Length==basis.Modules.Length+1,hull.Id+" actual damage compartment");
                var restored=ShipDefinition.Parse(fit.DefinitionJson,true);
                Check(JsonNode.DeepEquals(JsonNode.Parse(restored.ToJson()),JsonNode.Parse(fit.Definition.ToJson())),"Snapshot preserves device once: "+hull.Id+"/"+device.Id);
            }
        }
        Reject(()=>FleetLoadouts.ResolveFit(Fit("battleship","heat-vent","heat-vent")),"Duplicate family rejected");
        Runtime();RepairAndConstraints();Migration();MatchModules();
        Console.WriteLine($"PASS: {checks} tactical module checks");
    }
    private static void MatchModules()
    {
        var profile=HangarProfile.Create("battleship");
        var match=Matchmaking.CreateTiered(31,profile.Squadron(),profile.Owned,profile.Research!.UpgradeSets());
        var ai=match.Participants.Where(p=>!p.IsPlayer).SelectMany(p=>p.Loadout!.Members).Select(m=>m.Definition).ToArray();
        Check(ai.Any(s=>s.TacticalModules is {Length:>0}),"Tiered match retains tactical modules after random weapon matching");
        Check(match.Player.Loadout!.Members.All(m=>m.Definition.TacticalModules is not {Length:>0}),"Random AI fitting never adds a module to the player's preset");
        Check(ai.All(s=>s.TacticalModules is not {Length:>2}),"AI fits at most two role modules");
    }
    private static void Runtime()
    {
        var ship=Ship("generator-overload","heat-vent","repair-stores","repair-controller");var w=new SimWorld();w.Add(ship);
        Step(w,.1);Check(ship.DamageControl.Kits==ship.Definition.DamageControl.Kits+2,"Extra kits granted once");
        float baseOutput=ship.Power.AvailableMw;
        Check(ship.Tactical.Activate(0,w).Fired,"Overload starts");Step(w,.1);
        Check(Math.Abs(ship.Power.AvailableMw/baseOutput-1.25f)<.001f,"Actual generation +25%");
        Check(!ship.Tactical.Activate(0,w).Fired,"No repeated activation");
        Step(w,8);Check(ship.Tactical.OutputBonus==0&&ship.Tactical.Status(ship.Tactical.ActiveSlots[0])==TacticalStatus.Cooldown,"Eight seconds then cooldown");
        ship.Power.AddHeat(ship.Definition.Power.HeatCapacityMj*.8f);float heat=ship.Power.HeatMj;
        Check(ship.Tactical.Activate(1,w).Fired,"Vent starts");
        Check(Math.Abs(ship.Power.HeatMj-Math.Max(0,heat-ship.Definition.Power.HeatCapacityMj*.3f))<1,"Bounded instant heat removal");
        Check(ship.Tactical.SignatureBonus>=.6f,"Vent exposes real sensor signature");
        var overload=ship.Tactical.ActiveSlots[0];double cooldown=overload.CooldownUntil;
        ship.Damage.Hurt(overload.Module,9999,ship.SimTime,0,1);Step(w,.1);Check(ship.Tactical.Status(overload)==TacticalStatus.Destroyed,"Destroyed module unavailable");
        ship.Damage.Reset();Step(w,.1);Check(overload.CooldownUntil==cooldown,"Repair/reset never clears cooldown");
        Check(ship.DamageControl.Kits==ship.Definition.DamageControl.Kits,"Developer reset cannot regenerate extra stores");
        var focused=Ship("focus-search");var fw=new SimWorld();fw.Add(focused);
        var target=new ShipBody("TARGET",ShipDefinitions.ById("escort"),Faction.Red);target.Place(new(0,0,-20000),Quaternion.Identity);fw.Add(target);
        float normal=SensorNet.Measure(focused,target).Track;Check(focused.Tactical.Activate(0,fw).Fired,"Focus starts");
        Check(SensorNet.Measure(focused,target).Track>normal*1.79f,"Forward observation improved through existing sensor calculation");
        target.Place(new(0,0,20000),Quaternion.Identity);Check(SensorNet.Measure(focused,target).Track<normal*.66f,"Off-axis observation weakened");
        var loader=Ship("quick-loader");var lw=new SimWorld();lw.Add(loader);loader.Ordnance.ConsumeMissile();Step(lw,1);
        Check(loader.Ordnance.Missiles==loader.Definition.Missiles!.Rounds-1&&loader.Ordnance.MissileReload<loader.Definition.Missiles.ReloadSeconds-1.1f,"Loader speeds timer without ammunition refill");
        var jumping=Ship("short-jump");var jw=new SimWorld();jw.Add(jumping);jumping.Control=new ShipControl{FlightAssist=false};jumping.Velocity=new(10,0,0);
        jw.Obstacles.Add(new("mid-path",new(0,0,-5000),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,100)));
        var before=jumping.Velocity;Check(jumping.Tactical.Activate(0,jw).Fired&&!jumping.Railgun!.Ready&&!jumping.Ordnance.MissileReady,"Spooling blocks guns and missiles");
        Step(jw,3.1);Check(jumping.Position.Z<-9990&&jumping.Velocity==before,"Jump bypasses path obstacle and preserves inertia");
        Check(jumping.Tactical.ActiveSlots[0].Uses==1&&!jumping.Tactical.WeaponsBlocked,"Success consumes one cooldown, releases weapons");
        var blocked=Ship("short-jump");var bw=new SimWorld();bw.Add(blocked);bw.Obstacles.Add(new("arrival",new(0,0,-10000),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,50)));
        Check(!blocked.Tactical.Activate(0,bw).Fired&&blocked.Tactical.ActiveSlots[0].CooldownUntil==0,"Blocked arrival rejected without cooldown");
        var abort=Ship("short-jump");var aw=new SimWorld();aw.Add(abort);Check(abort.Tactical.Activate(0,aw).Fired,"Abort fixture starts");
        abort.Damage.Hurt(abort.Tactical.ActiveSlots[0].Module,9999,0,0,1);Step(aw,3.2);
        Check(abort.Position.Z==0&&abort.Tactical.ActiveSlots[0].Uses==0,"Destroyed drive cancels without teleport or cooldown");
        var late=Ship("short-jump");var lateWorld=new SimWorld();lateWorld.Add(late);late.Tactical.Activate(0,lateWorld);
        var blocker=new ShipBody("ALLY",ShipDefinitions.ById("escort"),Faction.Blue);blocker.Place(new(0,0,-10000),Quaternion.Identity);lateWorld.Add(blocker);Step(lateWorld,3.2);
        Check(late.Tactical.ActiveSlots[0].Uses==0&&late.Position.Z==0,"Arrival rechecked against ships after spool");
    }
    private static void Migration()
    {
        string dir=Path.Combine("shots","tactical-tests","save-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);string path=Path.Combine(dir,"hangar.json");
        foreach(int version in Enumerable.Range(1,5))
        {
            var profile=HangarProfile.Create("battleship");profile.Version=version;profile.Research!.Points[DesignFamily.Earth]=9876;
            var bay=ShipDefinitions.ById("battleship").Bays.First(b=>b.Accepts.Contains(EquipmentCategory.Shield)&&b.Size>=BaySize.Medium);
            profile.SetEquipment("battleship",0,bay.Id,"shield-capacity");var old=FleetLoadouts.ResolveFit(profile.Fit(new("battleship",0))).Definition;
            HangarStorage.Save(path,profile);var loaded=HangarStorage.Load(path,null);Check(loaded.CanSave&&loaded.Profile.Version==6,"Legacy version "+version+" migrated");
            var fit=loaded.Profile.Fit(new("battleship",0));var def=FleetLoadouts.ResolveFit(fit).Definition;
            Check(fit.Equipment.Count==0&&fit.Components?.AuxiliaryParts?.Single().EquipmentId=="shield-capacity","Physical part has new catalog mounting path");
            Check(ShipDefinition.SerializeData(def.InstalledEquipment)==ShipDefinition.SerializeData(old.InstalledEquipment)&&ShipDefinition.SerializeData(def.Modules)==ShipDefinition.SerializeData(old.Modules)&&def.Flight.MassKg==old.Flight.MassKg,"Migration preserves geometry/cost/effects");
            if(version>1)Check(loaded.Profile.Research!.Points[DesignFamily.Earth]==9876,"RP unchanged");
            HangarStorage.Save(path,loaded.Profile);Check(HangarStorage.Serialize(HangarStorage.Parse(File.ReadAllText(path)))==File.ReadAllText(path),"v6 round-trip");
        }
        var current=HangarProfile.Create("battleship");current.Research!.Points[DesignFamily.Earth]=500;
        var mounts=Fit("battleship","heat-vent").Components!.TacticalModules!;var slot=new HangarSlot("battleship",0);
        current.SetTactical(slot,0,mounts,true);long paid=current.Research.Points[DesignFamily.Earth];
        current.SetTactical(slot,0,mounts,true);Check(current.Research.Points[DesignFamily.Earth]==paid,"Already researched costs no extra RP");
        Reject(()=>current.SetTactical(slot,0,new[]{mounts[0],mounts[0]},true),"Invalid fit rejected");Check(current.Research.Points[DesignFamily.Earth]==paid,"Rejected fit costs nothing");
        Check(HangarStorage.Parse(HangarStorage.Serialize(current)).Fit(slot).Components!.TacticalModules!.SequenceEqual(mounts),"Active order survives save");
        var corrupt=JsonNode.Parse(HangarStorage.Serialize(current))!;
        corrupt["hulls"]!["battleship"]!["squadron"]![0]!["preset"]=HangarSlot.Individual;
        corrupt["hulls"]!["battleship"]!["squadron"]![0]!["own"]=JsonNode.Parse("{\"equipment\":[],\"components\":{\"tacticalModules\":[null]}}");
        string broken=corrupt.ToJsonString();File.WriteAllText(path,broken);
        var rejected=HangarStorage.Load(path,null);
        Check(!rejected.CanSave&&File.ReadAllText(path)==broken,"Corrupt individual tactical mounts preserve the original save and disable overwriting");
    }
    private static void RepairAndConstraints()
    {
        var enhanced=Ship("repair-controller");var basic=Ship();
        var faster=new SimWorld();faster.Add(enhanced);var normal=new SimWorld();normal.Add(basic);
        foreach(var s in new[]{enhanced,basic})
        {var sensor=s.Damage.Modules.First(m=>m.Definition.Kind==ModuleKind.Sensor);s.Damage.Hurt(sensor,99999,0,0,1);}
        double interval=enhanced.Definition.DamageControl.QuietSeconds+enhanced.Definition.DamageControl.RepairSeconds*1.5*.75+.05;
        Step(faster,interval);Step(normal,interval);
        Check(enhanced.DamageControl.Completed==1&&basic.DamageControl.Completed==0,"25% shorter work without shortening quiet time");
        var partial=Ship("repair-controller");var pw=new SimWorld();pw.Add(partial);
        var sensorPart=partial.Damage.Modules.First(m=>m.Definition.Kind==ModuleKind.Sensor);partial.Damage.Hurt(sensorPart,99999,0,0,1);Step(pw,10);
        double progress=partial.DamageControl.Progress;
        partial.Damage.Hurt(partial.Tactical.Slots[0].Module,99999,partial.SimTime,0,1);Step(pw,1);
        Check(partial.Tactical.RepairRate==1&&Math.Abs(partial.DamageControl.Progress-progress-1/(partial.Definition.DamageControl.RepairSeconds*1.5))<.002,"Controller loss preserves progress and restores normal rate");
        var stores=Ship("repair-stores");var sw=new SimWorld();sw.Add(stores);
        stores.Damage.Hurt(stores.Tactical.Slots[0].Module,99999,0,0,1);Step(sw,1);
        Check(stores.DamageControl.Kits==stores.Definition.DamageControl.Kits,"Lost stores remove unused extra kits");
        Step(sw,45);Check(stores.DamageControl.Kits<=stores.Definition.DamageControl.Kits-1,"Repairing stores consumes a kit without regranting extras");
        var vent=Ship("heat-vent");var vw=new SimWorld();vw.Add(vent);vent.Power.AddHeat(vent.Definition.Power.HeatCapacityMj*1.5f);Step(vw,.02);
        Check(vent.Power.Overheated&&vent.Tactical.Activate(0,vw).Fired,"Vent works while overheated");Step(vw,.02);
        Check(vent.Power.Overheated,"Vent does not unconditionally clear overheat hysteresis");
        var def=ShipDefinitions.ById("battleship");var small=TacticalModulesCatalog.Bays(def).Last();
        Reject(()=>FleetLoadouts.ResolveFit(FleetLoadouts.Empty(def.Id) with{Components=new(){TacticalModules=new[]{new TacticalMount(small.Id,"short-jump")}}}),"Small bay rejects medium drive");
        var guard=Ship("air-defense-controller");var gw=new SimWorld();gw.Add(guard);Step(gw,.02);
        Check(guard.Tactical.AirDefense&&ShipDefinition.SerializeData(guard.Definition.PointDefense)==ShipDefinition.SerializeData(basic.Definition.PointDefense),"Defense allocation preserves actual weapon range and rates");
        foreach(var module in guard.Damage.Modules.Where(m=>m.Definition.Kind==ModuleKind.Sensor))guard.Damage.Hurt(module,99999,guard.SimTime,0,1);
        Step(gw,.02);Check(!guard.Tactical.AirDefense,"Lost sensors disable expanded defense allocation");
    }
}
