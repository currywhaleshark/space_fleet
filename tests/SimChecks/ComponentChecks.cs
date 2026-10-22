using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Game;
using SpaceFleet.Sim;

static class ComponentChecks
{
    private static int checks;
    private static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
    private static void Reject(Action a,string why){try{a();}catch(InvalidDataException){checks++;return;}throw new Exception("Expected rejection: "+why);}
    public static void Run(){Catalog();Systems();Storage();PdShipHits();PdMissileHits();Console.WriteLine($"PASS: {checks} replacement system/catalog/research/preset checks");}
    private static ShipFit Fit(string hull,ShipComponentSelection selection)=>FleetLoadouts.Empty(hull) with{Components=selection};
    private static void Catalog()
    {
        foreach(var source in FleetLoadouts.Hulls)
        {
            string before=source.ToJson();var empty=FleetLoadouts.ResolveFit(FleetLoadouts.Empty(source.Id));
            Check(empty.ContentHash==FleetLoadouts.ResolveFit(Fit(source.Id,new())).ContentHash,"Empty replacement selection preserves stock identity");
            foreach(var part in ShipComponents.All.Where(p=>ShipComponents.Available(source,p)))
            {
                var selection=new ShipComponentSelection().With(part.Slot,part.Id);
                var resolved=FleetLoadouts.ResolveFit(Fit(source.Id,selection));var d=resolved.Definition;
                Check(d.Components==selection&&d.ModelId==source.ModelId,"Selection and original hull identity survive resolution");
                Check(ShipDefinition.SerializeData(d.Modules)==ShipDefinition.SerializeData(source.Modules)
                    &&d.Hull.Boxes.SequenceEqual(source.Hull.Boxes),"Replacement keeps original damage geometry and module health");
                Check(ShipDefinition.Parse(d.ToJson(),strict:true).ToJson()==d.ToJson(),"Resolved components round-trip without charging twice");
                if(part.Slot!=ComponentSlot.MainGun)Check(d.Railgun==source.Railgun||ShipDefinition.SerializeData(d.Railgun)==ShipDefinition.SerializeData(source.Railgun),"Other systems do not change primary geometry/stats");
                if(part.Slot==ComponentSlot.MainGun)
                {
                    var world=new SimWorld();var body=world.Add(new ShipBody("GUN",d,Faction.Blue));
                    body.Control=new ShipControl{FlightAssist=false};
                    // A battleship's aft mount is correctly occluded by its forward housing at rest.
                    // Traverse to a clear broadside, rather than requiring it to fire through that housing.
                    var aim=d.Railgun!.Mounts is null?Vector3.Forward:new Vector3(1,.035f,-.4f).Normalized();
                    int ticks=d.Railgun.Mounts is null?0:(int)Math.Ceiling((90/body.Railguns.Min(g=>Math.Min(g.Mount!.YawRate,g.Mount.ElevationRate))+1)*SimWorld.TickRate);
                    for(int t=0;t<ticks;t++){foreach(var g in body.Railguns)g.Aim(aim);world.Step();}
                    var fire=world.FireRailguns(body,aim);
                    Check(fire.Shots==body.Railguns.Sum(g=>g.BarrelCount),$"Every replacement barrel fires a physical round: {source.Id}/{part.Id}: {fire}");
                    Check(body.Railguns.All(g=>g.IsLaser ? g.Rounds==0 && world.Projectiles.Count==0 : g.Rounds==g.Definition.Rounds-g.BarrelCount),"Replacement magazine accounts for actual barrels");
                    var all=ResearchTree.Catalog.ByHull(source.Id).Upgrades.Select(u=>u.Id).ToArray();
                    var upgraded=FleetLoadouts.ResolveFit(Fit(source.Id,selection) with{Upgrades=all}).Definition;
                    var permanent=ResearchTree.Catalog.Resolve(source.Id,all,Array.Empty<EquipmentMount>()).Definition;
                    // The AA factory baseline is now an emitter. Rail replacements still
                    // derive from the researched ballistic template retained by the variant.
                    if(source.DefaultMainGun is not null)
                        permanent=CruiserTrials.Resolve(source.Id,ResearchTree.Catalog.ByHull(source.Id).Upgrades.Select(u=>u.Patch.ToJsonString())
                            .Append("{\"defaultMainGun\":null}").ToArray()).Definition;
                    // Permanent research now grows the container (supply 1.06 → overcharge), not the reload.
                    var researched=new ShipBody("RES",upgraded,Faction.Blue).Railguns[0];var fitted=new ShipBody("FIT",d,Faction.Blue).Railguns[0];
                    Check(upgraded.Battery!.Weapon.Laser is not null ? researched.Laser!.Definition.EnergyPerSecond > fitted.Laser!.Definition.EnergyPerSecond
                        : Math.Abs(upgraded.Railgun!.ReloadSeconds-permanent.Railgun!.ReloadSeconds*part.Effects.Reload)<.0001&&researched.Definition.Energy>fitted.Definition.Energy,
                        "Permanent container research keeps the selected reload tradeoff and adds supply");
                }
            }
            Check(source.ToJson()==before,"Catalog hull is never modified");
        }
        Reject(()=>FleetLoadouts.ResolveFit(Fit("escort",new(MainGun:"main-rapid"))),"Fixed receiver cannot fit a rotating turret");
        Reject(()=>FleetLoadouts.ResolveFit(Fit("interceptor",new(MainGun:"main-triple"))),"Fixed gun cannot gain a rotating triple mount");
        Reject(()=>FleetLoadouts.ResolveFit(Fit("escort",new(Engine:"main-single"))),"Cross-slot part rejected");
        Reject(()=>FleetLoadouts.ResolveFit(Fit("escort",new(Sensor:"missing"))),"Unknown component rejected");
    }
    private static void Systems()
    {
        var source=ShipDefinitions.ById("escort");
        var fitted=FleetLoadouts.ResolveFit(Fit("escort",new(MainGun:"main-triple",Secondary:"pd-barrage",Engine:"engine-vector",
            Sensor:"sensor-longrange",Generator:"generator-output",Shield:"shield-recharge"))).Definition;
        Check(fitted.Power.OutputMw>source.Power.OutputMw&&fitted.Power.CoolingMw<source.Power.CoolingMw,"Generator output trades cooling");
        Check(fitted.Shield.RechargePerSecond>source.Shield.RechargePerSecond&&fitted.Shield.Capacity<source.Shield.Capacity,"Shield regeneration trades capacity");
        Check(fitted.Flight.StrafeAccel>source.Flight.StrafeAccel&&fitted.Flight.ForwardAccel<source.Flight.ForwardAccel,"Vector engines retain directional tradeoff after mass penalty");
        Check(fitted.Sensors.Strength>source.Sensors.Strength&&fitted.Sensors.Signature>source.Sensors.Signature,"Long-range sensors increase own signature");
        Check(fitted.PointDefense!.ShotsPerSecond>source.PointDefense!.ShotsPerSecond&&fitted.PointDefense.RangeMeters<source.PointDefense.RangeMeters,"PD trades range for fire rate");
        foreach(var kind in new[]{ModuleKind.Sensor,ModuleKind.Thruster,ModuleKind.Generator,ModuleKind.Reactor,ModuleKind.ShieldEmitter,ModuleKind.Gun})
        {
            var body=new ShipBody("DAMAGE",fitted,Faction.Blue);body.DamageControl.Priority=RepairPriority.Hold;
            var killed=body.Damage.Modules.Where(m=>m.Definition.Kind==kind).ToArray();
            foreach(var m in killed)body.Damage.Hurt(m,m.Health,0,0,1);
            Check(killed.All(m=>m.Destroyed),"Replacement uses original destructible modules");
            if(kind==ModuleKind.Sensor)Check(body.Damage.SensorFraction==0,"Replaced sensor can be disabled");
            if(kind==ModuleKind.Thruster)Check(body.Damage.PropulsionFraction==0,"Replaced engine can be disabled");
            if(kind==ModuleKind.ShieldEmitter)Check(body.Damage.ShieldCapacity==0,"Replaced shield fails with emitters");
            if(kind==ModuleKind.Gun)Check(body.Railguns.All(g=>!g.Ready),"Replaced primary fails with gun modules");
            body.Damage.Reset();Check(body.Damage.Modules.All(m=>!m.Destroyed)&&body.Damage.ShieldCapacity==fitted.Shield.Capacity,"Recovery restores selected system capability");
        }
        var basePower=source.Power;
        string tight=new JsonObject{["power"]=new JsonObject{["outputMw"]=basePower.Engines+basePower.Shields+basePower.Weapons+basePower.Sensors}}.ToJsonString();
        Reject(()=>ShipLoadouts.Resolve(new("escort",tight){Components=new(Engine:"engine-cruise")}),"Combined power budget rejects excessive demand");
        var assisted=ShipLoadouts.Resolve(new("escort",tight){Components=new(Engine:"engine-cruise",Generator:"generator-output")});
        Check(assisted.Definition.Components!.Engine=="engine-cruise","Generator replacement is composed before final budget validation");
    }
    private static void PdShipHits()
    {
        var targetJson=JsonNode.Parse(ShipDefinitions.ById("interceptor").ToJson())!;
        targetJson["PointDefense"]=null;targetJson["DefenseDrones"]=null;targetJson["Shield"]!["Capacity"]=1000000;
        var target=ShipDefinition.Parse(targetJson.ToJsonString(),strict:true);
        foreach(string part in new[]{"pd-barrage","pd-longrange"})
        foreach(float range in new[]{500f,5100f})
        {
            var selected=FleetLoadouts.ResolveFit(Fit("escort",new(Secondary:part))).Definition;
            var json=JsonNode.Parse(selected.ToJson())!;json["DefenseDrones"]=null;
            var def=ShipDefinition.Parse(json.ToJsonString(),strict:true);var pd=def.PointDefense!;
            Check(pd.ShipPacket!.Value.Range==pd.RangeMeters,"Selected anti-ship packet reaches the selected PD range");
            var w=new SimWorld();var defender=w.Add(new ShipBody("PD",def,Faction.Blue));
            var raider=w.Add(new ShipBody("RAIDER",target,Faction.Red));
            raider.Place(Vec3d.From(pd.Mounts[0]+pd.Normals![0].Normalized()*range),Quaternion.Identity);
            defender.Control=raider.Control=new ShipControl{FlightAssist=false};
            for(int i=0;i<360;i++)w.Step();
            var hits=w.Impacts.Where(x=>x.Shooter==defender&&x.Hit.ShieldStopped).ToArray();
            if(part=="pd-barrage"&&range>pd.RangeMeters)
                Check(hits.Length==0,"Short-range barrage cannot damage targets outside its firing range");
            else
            {
                Check(hits.Length>0,"PD actually damages the interceptor at "+range+"m");
                float energy=20*ShipComponents.ById(part).Effects.Damage;
                Check(hits.All(x=>Math.Abs(x.Hit.ShieldAbsorbed-energy)<.001&&Math.Abs(x.Energy-energy)<.001),
                    "Real shield absorption and visual impact energy use the replacement damage multiplier");
            }
        }
        var malformed=JsonNode.Parse(ShipDefinitions.ById("escort").ToJson())!;
        malformed["PointDefense"]!["ShipPacket"]=JsonNode.Parse(ShipDefinition.SerializeData(new DamagePacket(20,25,10,100)));
        Reject(()=>ShipDefinition.Parse(malformed.ToJsonString(),strict:true),"PD ship packet cannot silently stop short of its engagement range");
        malformed["PointDefense"]!["ShipPacket"]!["Energy"]=-1;
        Reject(()=>ShipDefinition.Parse(malformed.ToJsonString(),strict:true),"Invalid PD ship energy rejected");
    }
    private static void PdMissileHits()
    {
        foreach(var hull in FleetLoadouts.Hulls.Where(h=>h.PointDefense is not null))
        {
            var stock=hull.PointDefense!;
            var pd=FleetLoadouts.ResolveFit(Fit(hull.Id,new(Secondary:"pd-barrage"))).Definition.PointDefense!;
            Check(pd.EffectiveMissileDamage==stock.EffectiveMissileDamage,"Barrage keeps missile breakpoints on every hull");
            Check(Math.Abs(pd.DamagePerHit-stock.DamagePerHit*.9f)<.0001
                &&Math.Abs(pd.EffectiveShipPacket.Energy-stock.EffectiveShipPacket.Energy*.9f)<.0001,
                "Barrage retains drone/probe and ship damage tradeoffs");
            Check(pd.YawRate==stock.YawRate&&pd.ElevationRate==stock.ElevationRate&&pd.ArcDegrees==stock.ArcDegrees,
                "Barrage cannot bypass existing mechanical limits");
        }
        foreach(string? part in new string?[]{null,"pd-barrage","pd-longrange"})
        foreach(int health in new[]{4,8,12})
        {
            var resolved=part is null?ShipDefinitions.ById("escort"):
                FleetLoadouts.ResolveFit(Fit("escort",new(Secondary:part))).Definition;
            var data=JsonNode.Parse(resolved.ToJson())!;data["DefenseDrones"]=null;
            var d=ShipDefinition.Parse(data.ToJsonString(),strict:true);var pd=d.PointDefense!;
            var w=new SimWorld();var defender=w.Add(new ShipBody("DEFENDER",d,Faction.Blue));
            var launcher=w.Add(new ShipBody("LAUNCHER",ShipDefinitions.ById("interceptor"),Faction.Red));
            launcher.Place(new(0,0,-20000),Quaternion.Identity);
            defender.Control=launcher.Control=new ShipControl{FlightAssist=false};
            var flight=launcher.Definition.Missiles!;
            var m=new Missile{Id=101,Shooter=launcher,Definition=flight,Health=health,
                Position=Vec3d.From(pd.Mounts[0]+pd.Normals![0].Normalized()*500),Age=flight.BurnSeconds+1,
                NextSeekerCheck=double.MaxValue};
            ((List<Missile>)w.Missiles).Add(m);w.Log=new BattleLog(w);
            int hits=0;var observed=new HashSet<PointDefenseShot>();
            for(int i=0;i<600&&m.Health>0;i++)
            {
                w.Step();int added=0;
                foreach(var shot in w.PointDefenseShots)if(observed.Add(shot)&&shot.Hit)added++;
                if(added>0)
                {
                    hits+=added;
                    Check(Math.Abs(m.Health-Math.Max(0,health-hits*pd.EffectiveMissileDamage))<.001,
                        "Actual missile damage uses the dedicated packet, including final overkill");
                }
            }
            Check(m.Health<=0&&w.Log.Ship(defender).MissilesIntercepted==1,"Actual PD interception removes and records one missile");
            Check(hits==(int)Math.Ceiling(health/pd.EffectiveMissileDamage),"Actual hits-to-kill match light/torpedo/heavy durability");
        }
        foreach(float invalid in new[]{0f,-1f})
        {
            var data=JsonNode.Parse(ShipDefinitions.ById("escort").ToJson())!;
            data["PointDefense"]!["MissileDamagePerHit"]=invalid;
            Reject(()=>ShipDefinition.Parse(data.ToJsonString(),strict:true),"Missile-specific PD damage must be positive");
        }
    }
    private static void Storage()
    {
        var p=HangarProfile.Create("escort");p.Research!.Points[DesignFamily.Earth]=100000;
        long initial=p.Research.Points[DesignFamily.Earth];
        Reject(()=>p.SetComponent("escort",0,ComponentSlot.MainGun,"main-triple"),"Research required before fitting");
        p.SetComponent("escort",0,ComponentSlot.MainGun,"main-triple",true);
        long spent=initial-p.Research.Points[DesignFamily.Earth];Check(spent==p.Research.ComponentCost("escort","main-triple"),"Unlock charges exactly once");
        p.SetComponent("escort",1,ComponentSlot.MainGun,"main-triple",true);
        Check(p.Research.Points[DesignFamily.Earth]==initial-spent,"Unlocked components can be shared across presets without a second charge");
        p.SetComponent("escort",1,ComponentSlot.Sensor,"sensor-lowprofile",true);
        Check(p.Hulls["escort"].SystemPresets[0]!.Sensor is null,"Preset selections stay independent");
        var snapshot=FleetLoadouts.Resolve(p.Squadron());string saved=HangarStorage.Serialize(p);
        var restored=HangarStorage.Parse(saved);
        Check(restored.Version==6&&FleetLoadouts.Resolve(restored.Squadron()).LoadoutId==snapshot.LoadoutId,"Current save (v6) stores exact fit and research across reload");
        p.SetComponent("escort",0,ComponentSlot.MainGun,null);
        Check(snapshot.Leader.Railgun!.Mounts!.All(m=>m.Muzzles.Length==3),"Resolved queued snapshot cannot be edited by later preset changes");
        Check(FleetLoadouts.Resolve(p.Squadron()).Leader.MainBattery is null,"Returning to stock restores stock primary schema");
        Check(Matchmaking.ValidateResearchLoadout(restored.Squadron(),restored.Owned,restored.Research!.UpgradeSets()).Leader.Components?.MainGun=="main-triple","Researched components can launch");
        Reject(()=>Matchmaking.ValidateResearchLoadout(restored.Squadron(),restored.Owned,new Dictionary<string,IReadOnlySet<string>>()),"Unresearched component cannot be smuggled into match");
        var old=JsonNode.Parse(HangarStorage.Serialize(HangarProfile.Create("escort")))!.AsObject();old["version"]=2;
        old["research"]!.AsObject().Remove("components");
        foreach(var hull in old["hulls"]!.AsObject())hull.Value!.AsObject().Remove("systemPresets");
        string folder=Path.Combine(AppContext.BaseDirectory,"component-save-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            string path=Path.Combine(folder,"hangar.json");File.WriteAllText(path,old.ToJsonString());
            var migrated=HangarStorage.Load(path,null);Check(migrated.CanSave&&migrated.Profile.Version==6,"Old v2 save migrates to the current v6");
            Check(migrated.Profile.Research!.Components.Count==0&&migrated.Profile.Research.SharedComponents.Values.All(v=>v.Length==0)&&migrated.Profile.Hulls.Values.All(h=>h.SystemPresets.All(s=>s is null)),"Migration grants no parts and keeps stock choices");
            HangarStorage.Save(path,migrated.Profile);Check(File.ReadAllText(path+".bak")==old.ToJsonString(),"Migration backup preserves old bytes");
        }
        finally{if(!Path.GetFullPath(folder).StartsWith(Path.GetFullPath(AppContext.BaseDirectory),StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid test folder");Directory.Delete(folder,true);}
        long balance=p.Research.Points[DesignFamily.Earth];var prior=p.Hulls["escort"].SystemPresets[0];
        Reject(()=>p.SetComponent("escort",0,ComponentSlot.MainGun,"main-rapid",true),"Incompatible research-and-fit is atomic");
        Check(p.Research.Points[DesignFamily.Earth]==balance&&p.Hulls["escort"].SystemPresets[0]==prior,"Failed replacement spends no points and changes no preset");
        var poor=HangarProfile.Campaign();string untouched=HangarStorage.Serialize(poor);
        Reject(()=>poor.SetComponent(poor.SelectedHullId,0,ComponentSlot.Shield,"shield-capacity",true),"Insufficient research points");
        Check(HangarStorage.Serialize(poor)==untouched,"Insufficient RP leaves ownership, balance and every preset untouched");
        var malformed=JsonNode.Parse(saved)!.AsObject();malformed["hulls"]!["escort"]!["systemPresets"]=new JsonArray();
        Reject(()=>HangarStorage.Parse(malformed.ToJsonString()),"Bad replacement preset count rejected");
    }
}
