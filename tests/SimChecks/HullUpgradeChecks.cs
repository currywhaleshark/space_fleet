using System.Text.Json.Nodes;
using SpaceFleet.Game;
using SpaceFleet.Sim;

/// <summary>Modular weapons phase 3: tier volume, container upgrades, design-shared part research, save v4 → v5.</summary>
static class HullUpgradeChecks
{
    private static int checks;
    private static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
    private static void Reject(Action a,string why){try{a();}catch(InvalidDataException){checks++;return;}throw new Exception("Expected rejection: "+why);}
    private static bool Near(float a,float b)=>MathF.Abs(a-b)<=1e-4f*MathF.Max(1,MathF.Abs(b));
    private static ShipDefinition Stock(string id)=>FleetLoadouts.ResolveFit(FleetLoadouts.Empty(id)).Definition;
    private static ShipDefinition Upgraded(string id,params string[] upgrades)=>ResearchTree.Catalog.Resolve(id,upgrades,Array.Empty<EquipmentMount>()).Definition;

    public static void Run(){TierVolume();Upgrades();Research();Saves();Console.WriteLine($"PASS: {checks} hull upgrade/part research/save v5 checks");}

    private static void TierVolume()
    {
        // 15% of the stock volume per tier above the branch's entry tier, rounded to 0.5.
        foreach(var (id,volume) in new[]{("earth_gun_cruiser",7f),("earth_gun_cruiser_t4",8f),("earth_gun_cruiser_t5",9f),
            ("battleship",21f),("battleship_t5",24f),("mars_battleship",15f),("mars_battleship_t5",17.5f),
            ("earth_starter_escort",3.5f),("escort",8f),("interceptor",2f),("interceptor_t5",3f)})
            Check(Near(MainGunParts.VolumeBudget(Stock(id)),volume),$"{id}: tier weapon volume {MainGunParts.VolumeBudget(Stock(id))} = {volume}");
        foreach(var hull in FleetLoadouts.Hulls.Where(h=>h.Battery is not null))
        {
            var d=Stock(hull.Id);
            Check(MainGunParts.VolumeBudget(d)>=MainGunParts.StockVolume(d)&&MainGunParts.Supply(d)==MainGunParts.StockFor(d).RatedMw*d.Battery!.Hardpoints.Length,
                $"{hull.Id}: tiers widen volume only; stock supply stays at rated");
            Check(d.Battery.SupplyRatio==1&&d.WeaponBudget is null,$"{hull.Id}: stock hull carries no upgrade state");
        }
        // The extra T5 volume lets a heavier mix fit that the T3 hull rejects.
        Check(MainGunParts.Reject(Stock("earth_gun_cruiser"),new[]{"earth-rail-m3","earth-rail-m3"})!.Contains("용적")
            &&MainGunParts.Reject(Stock("earth_gun_cruiser_t5"),new[]{"earth-rail-m3","earth-rail-m3"}) is null,"T5 volume admits two triples");
    }

    private static void Upgrades()
    {
        foreach(var node in ResearchTree.Catalog.Playable)
        {
            string id=node.HullId!;var stock=Stock(id);
            Check(node.Upgrades.Select(u=>u.Id).SequenceEqual(new[]{"hull","fire-control","reactor","drive","repair"}),$"{id}: upgrade IDs kept for saves");
            var mount=Upgraded(id,"fire-control");var first=stock.Battery!.Hardpoints[0];
            Check(mount.WeaponBudget is {VolumeBonus:2} b&&b.EnlargedSockets!.Single()==first.Id,$"{id}: 포좌 확장 = first socket + volume 2");
            Check(Near(MainGunParts.VolumeBudget(mount),MainGunParts.VolumeBudget(stock)+2)
                &&MainGunParts.SocketSize(mount,mount.Battery!.Hardpoints[0])==(first.Size<WeaponSize.ExtraLarge?first.Size+1:first.Size),$"{id}: first socket one size larger");
            Check(ShipDefinition.SerializeData(mount.Railgun)==ShipDefinition.SerializeData(stock.Railgun)
                &&mount.Battery.SocketWeapons is null,$"{id}: socket upgrade alone does not change the stock guns");
            var reactor=Upgraded(id,"reactor");
            Check(Near(reactor.Battery!.SupplyRatio,1.06f)&&Near(reactor.Power.CoolingMw,stock.Power.CoolingMw*1.15f)
                &&reactor.Power.OutputMw==stock.Power.OutputMw,$"{id}: 주포 전원 = supply 1.06, cooling 1.15");
            var body=new ShipBody("R",reactor,Faction.Blue);
            Check(body.Railguns.All(g=>Near(g.Overcharge,1.06f)),$"{id}: stock guns overcharge with the reactor upgrade");
            var all=Upgraded(id,node.Upgrades.Select(u=>u.Id).ToArray());
            Check(all.Battery!.Hardpoints[0].Size==MainGunParts.SocketSize(mount,mount.Battery.Hardpoints[0])
                &&MainGunParts.SocketSize(all,all.Battery.Hardpoints[0])==all.Battery.Hardpoints[0].Size,$"{id}: a fitted battery is not enlarged twice");
        }
        // Enlarged sockets open bigger parts: Mars T5 battleship takes the 320 mm gun only after 포좌 확장.
        var nergal=Stock("mars_battleship_t5");var enlarged=Upgraded("mars_battleship_t5","fire-control");
        Check(MainGunParts.Reject(nergal,new[]{"mars-rail-xl1",null})!.Contains("소켓보다 큰")&&MainGunParts.Reject(enlarged,new[]{"mars-rail-xl1",null}) is null,
            "XL decisive gun needs the enlarged socket");
        var fit=ResearchTree.Catalog.Resolve("mars_battleship_t5",new[]{"fire-control"},Array.Empty<EquipmentMount>(),new ShipComponentSelection{MainGuns=new[]{"mars-rail-xl1",null}}).Definition;
        Check(fit.Battery!.Hardpoints[0].Size==WeaponSize.ExtraLarge&&fit.Battery.SocketWeapons![0].Id=="mars-rail-xl1","Enlarged size reaches the fitted battery");
        Check(new ShipBody("XL",fit,Faction.Red).Railguns[0].Definition.Energy>new ShipBody("ST",nergal,Faction.Red).Railguns[0].Definition.Energy*1.5f,"Decisive gun fires its own heavy shot");
        // Interceptors: the enlarged fixed axis takes the medium axial gun, slower at its rated draw.
        var falke=ResearchTree.Catalog.Resolve("interceptor_t3",new[]{"fire-control"},Array.Empty<EquipmentMount>(),new ShipComponentSelection{MainGuns=new[]{"earth-rail-m1-axial"}}).Definition;
        Check(falke.Battery!.Hardpoints[0].Fixed&&falke.Battery.Hardpoints[0].Size==WeaponSize.Medium&&falke.Battery.SupplyRatio<1,"Fixed axis enlarged to medium with an under-supplied axial gun");
        Reject(()=>ResearchTree.Catalog.Resolve("interceptor_t3",null,Array.Empty<EquipmentMount>(),new ShipComponentSelection{MainGuns=new[]{"earth-rail-m1-axial"}}),
            "Medium axial gun needs 포좌 확장");
    }

    private static void Research()
    {
        var p=HangarProfile.Campaign(DesignFamily.Earth);var r=p.Research!;r.Points[DesignFamily.Earth]=10000;r.Points[DesignFamily.Mars]=10000;
        Check(r.HasPart(DesignFamily.Earth,"earth-rail-m2")&&!r.HasPart(DesignFamily.Earth,"earth-rail-m3"),"Stock parts are free; others need research");
        Check(r.PartSponsor(DesignFamily.Earth,"earth-rail-m3") is null,"No owned hull can research a T3 part yet");
        Reject(()=>r.BuyPart(DesignFamily.Earth,"earth-rail-m3"),"Research needs a qualifying hull");
        r.GrantPath("earth_gun_cruiser");
        Check(r.PartSponsor(DesignFamily.Earth,"earth-rail-m3")=="earth_gun_cruiser","T3 gun cruiser qualifies for the triple");
        long before=r.Points[DesignFamily.Earth];
        Check(r.BuyPart(DesignFamily.Earth,"earth-rail-m3")&&r.Points[DesignFamily.Earth]==before-MainGunParts.ById("earth-rail-m3").Cost,"Part research charges its cost once");
        Check(!r.BuyPart(DesignFamily.Earth,"earth-rail-m3")&&r.Points[DesignFamily.Earth]==before-MainGunParts.ById("earth-rail-m3").Cost,"Second purchase is free and does nothing");
        Check(r.Points[DesignFamily.Mars]==10000&&!r.HasPart(DesignFamily.Mars,"earth-rail-m3"),"Earth research is not shared with Mars");
        Reject(()=>r.BuyPart(DesignFamily.Mars,"earth-rail-m3"),"No cross-faction part research");
        // Shared within the design: the T2 escort, which could not research it, can now fit it.
        p.SetMainGun("escort",0,0,"earth-rail-m3");
        Check(p.Fit(new("escort",0)).Components!.MainGuns![0]=="earth-rail-m3","Design-shared part fits another hull without new research");
        Reject(()=>p.SetMainGun("escort",0,1,"earth-rail-m1-hp"),"Unresearched part needs research");
        before=r.Points[DesignFamily.Earth];
        p.SetMainGun("escort",0,1,"earth-rail-m1-hp",research:true);
        Check(r.Points[DesignFamily.Earth]==before-MainGunParts.ById("earth-rail-m1-hp").Cost&&r.HasPart(DesignFamily.Earth,"earth-rail-m1-hp"),"Hangar researches and fits in one step");
        var resolved=FleetLoadouts.ResolveFit(p.Fit(new("escort",0))).Definition;
        Check(resolved.Battery!.SocketWeapons!.Select(w=>w.Id).SequenceEqual(new[]{"earth-rail-m3","earth-rail-m1-hp"}),"Preset fit resolves both parts");
        Check(Near(resolved.Battery.SupplyRatio,100f/115),"Preset fit runs at its supply ratio");
        p.SetComponent("escort",0,ComponentSlot.MainGun,"main-triple",research:true);
        Check(p.Fit(new("escort",0)).Components is {MainGun:"main-triple",MainGuns:null},"A catalog primary replaces socket parts");
        p.SetMainGun("escort",0,0,"earth-rail-m3");
        Check(p.Fit(new("escort",0)).Components is {MainGun:null,MainGuns:not null},"Socket parts replace a catalog primary");
        p.SetMainGun("escort",0,0,null);p.SetMainGun("escort",0,1,"earth-rail-m2");
        Check(p.Fit(new("escort",0)).Components is null,"All stock sockets clear the selection");
        // Matchmaking accepts researched parts and rejects smuggled ones.
        p.SetMainGun("escort",0,0,"earth-rail-m3");
        var squad=new SquadronLoadout(FleetLoadouts.Default("escort").Members.Select((m,i)=>i==0?p.Fit(new("escort",0)):m).ToArray());
        var owned=FleetLoadouts.Hulls.Select(h=>h.Id).ToHashSet();
        Check(Matchmaking.ValidateResearchLoadout(squad,owned,r.UpgradeSets()).Leader.Battery!.SocketWeapons is not null,"Researched part can launch");
        var empty=r.UpgradeSets().ToDictionary(kv=>kv.Key,kv=>(IReadOnlySet<string>)kv.Value.Where(k=>!k.StartsWith("part:")).ToHashSet());
        Reject(()=>Matchmaking.ValidateResearchLoadout(squad,owned,empty),"Unresearched part cannot be smuggled into a match");
    }

    private static void Saves()
    {
        // A v4 save: per-hull component unlocks, old research IDs, no part fields.
        var p=HangarProfile.Create("escort");var r=p.Research!;r.Points[DesignFamily.Earth]=777;r.Points[DesignFamily.Mars]=55;
        r.Upgrades["escort"]=new[]{"fire-control","reactor"};
        var json=JsonNode.Parse(HangarStorage.Serialize(p))!.AsObject();json["version"]=4;
        var research=json["research"]!.AsObject();research.Remove("sharedComponents");research.Remove("parts");
        research["components"]=new JsonObject{["escort"]=new JsonArray("main-triple"),["mars_escort"]=new JsonArray("shield-recharge")};
        json["hulls"]!["escort"]!["systemPresets"]![0]=new JsonObject{["mainGun"]="main-triple"};
        string folder=Path.Combine(AppContext.BaseDirectory,"v5-save-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            string path=Path.Combine(folder,"hangar.json");File.WriteAllText(path,json.ToJsonString());
            var loaded=HangarStorage.Load(path,null);var m=loaded.Profile;
            Check(loaded.CanSave&&m.Version==5&&loaded.Message is not null,$"v4 save migrates to v5 with a notice: {loaded.CanSave} {loaded.Message}");
            Check(m.Research!.Points[DesignFamily.Earth]==777&&m.Research.Points[DesignFamily.Mars]==55,"Migration keeps RP");
            Check(m.Research.ForHull("escort").SequenceEqual(new[]{"fire-control","reactor"}),"Old research IDs carry over as container upgrades");
            Check(m.Research.Components.Count==0&&m.Research.SharedComponents[DesignFamily.Earth].SequenceEqual(new[]{"main-triple"})
                &&m.Research.SharedComponents[DesignFamily.Mars].SequenceEqual(new[]{"shield-recharge"}),"Per-hull unlocks become design-shared");
            Check(m.Research.HasComponent("earth_gun_cruiser","main-triple")&&!m.Research.HasComponent("mars_gun_cruiser","main-triple"),"Shared unlock reaches other hulls of the design only");
            var escort=FleetLoadouts.ResolveFit(m.Fit(new("escort",0))).Definition;
            Check(escort.WeaponBudget is {VolumeBonus:2,SupplyScale:1.06f}&&Near(escort.Battery!.SupplyRatio,1.06f),"Migrated research resolves as container upgrades");
            string saved=HangarStorage.Serialize(m);var again=HangarStorage.Parse(saved);
            Check(again.Version==5&&HangarStorage.Serialize(again)==saved,"v5 save round-trips exactly");
            m.Research.Points[DesignFamily.Earth]=5000;m.Research.GrantPath("earth_gun_cruiser");
            m.SetMainGun("escort",1,0,"earth-rail-m3",research:true);
            var restored=HangarStorage.Parse(HangarStorage.Serialize(m));
            Check(restored.Fit(new("escort",1)).Components!.MainGuns![0]=="earth-rail-m3"&&restored.Research!.HasPart(DesignFamily.Earth,"earth-rail-m3"),
                "Part fits and part research survive a save");
            var tampered=JsonNode.Parse(HangarStorage.Serialize(m))!.AsObject();
            tampered["research"]!["parts"]!["Earth"]=new JsonArray();
            Reject(()=>HangarStorage.Parse(tampered.ToJsonString()),"A save cannot fit parts it has not researched");
        }
        finally{Directory.Delete(folder,true);}
    }
}
