using SpaceFleet.Game;
using SpaceFleet.Sim;

/// <summary>Modular weapons phase 7 (§24): generators and coolers in main gun weapon volume.</summary>
static class PowerPartChecks
{
    private static int checks;
    private static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
    private static void Reject(Action a,string expected,string why)
    {
        try{a();}catch(InvalidDataException e){Check(e.Message.Contains(expected),$"{why}: '{e.Message}' lacks '{expected}'");return;}
        throw new Exception("Expected rejection: "+why);
    }
    private static bool Near(float a,float b)=>MathF.Abs(a-b)<=1e-4f*MathF.Max(1,MathF.Abs(b));
    private static ShipDefinition Fit(string hull,string?[]? guns,params string[] power)=>ResearchTree.Catalog.Resolve(hull,new[]{"fire-control"},
        Array.Empty<EquipmentMount>(),new ShipComponentSelection{MainGuns=guns,PowerParts=power.Length==0?null:power}).Definition;

    public static void Run(){Catalog();Coolers();Generators();Rejections();Research();Scores();
        Console.WriteLine($"PASS: {checks} generator/cooler power part checks");}

    private static void Catalog()
    {
        Check(PowerParts.All.Count==12,"Twelve power parts: two kinds, three sizes, two designs");
        foreach(var design in new[]{DesignFamily.Earth,DesignFamily.Mars})
        foreach(var kind in Enum.GetValues<PowerPartKind>())
            Check(PowerParts.All.Count(p=>p.Design==design&&p.Kind==kind)==3,$"{design} {kind}: small, medium, large");
        float PerVolume(DesignFamily d,PowerPartKind k)=>PowerParts.All.Where(p=>p.Design==d&&p.Kind==k).Average(p=>(k==PowerPartKind.Generator?p.SupplyMw:p.CoolingMw)/p.Volume);
        Check(PerVolume(DesignFamily.Earth,PowerPartKind.Cooler)>PerVolume(DesignFamily.Mars,PowerPartKind.Cooler),"Earth coolers are the more efficient (§9)");
        Check(PerVolume(DesignFamily.Mars,PowerPartKind.Generator)>PerVolume(DesignFamily.Earth,PowerPartKind.Generator),"Mars generators are the more efficient (§9)");
        Check(PowerParts.All.All(p=>MainGunParts.Research(p.Id) is {Stock:false} r&&r.Cost==p.Cost&&r.Design==p.Design),"Power parts are researched like other parts");
    }

    private static void Coolers()
    {
        var stock=Fit("earth_air_defense_cruiser_t5",null);var cooled=Fit("earth_air_defense_cruiser_t5",null,"earth-cooler-m");
        var part=PowerParts.ById("earth-cooler-m");
        Check(Near(cooled.Power.CoolingMw,stock.Power.CoolingMw+part.CoolingMw)&&Near(cooled.Power.HeatCapacityMj,stock.Power.HeatCapacityMj+part.HeatCapacityMj),
            "A cooler adds its cooling and heat capacity");
        Check(cooled.Battery!.PowerParts!.SequenceEqual(new[]{"earth-cooler-m"})&&Near(cooled.Battery.SupplyRatio,stock.Battery!.SupplyRatio),
            "A cooler is recorded and leaves supply alone");
        Check(Near((float)MainGunParts.EffectiveFirepower(cooled),(float)MainGunParts.EffectiveFirepower(stock)),"A cooler does not change sustained firepower");
        var body=new ShipBody("COOL",cooled,Faction.Blue);
        Check(Near(body.Power.CoolingMw,cooled.Power.CoolingMw),"The running ship cools at the fitted rate");
    }

    private static void Generators()
    {
        // A light (stock) fit already runs at or above rated: a generator never overcharges it.
        var stock=Fit("escort",null);var lit=Fit("escort",null,"earth-gen-s");
        Check(Near(lit.Battery!.SupplyMw!.Value,stock.Battery!.SupplyMw??MainGunParts.Supply(stock)),"Generators do not overcharge a fit that is already supplied");
        // An under-supplied mix is lifted toward rated, never past it.
        string?[] heavy={"earth-rail-m3","earth-rail-m1-hp"};
        var slow=Fit("escort",heavy);var fed=Fit("escort",heavy,"earth-gen-s");
        float rated=slow.Battery!.SocketWeapons!.Sum(w=>w.PowerMw),gen=PowerParts.ById("earth-gen-s").SupplyMw;
        Check(slow.Battery.SupplyRatio<1&&Near(fed.Battery!.SupplyMw!.Value,MathF.Min(slow.Battery.SupplyMw!.Value+gen,rated)),
            $"Generator lifts supply toward rated ({slow.Battery.SupplyRatio:0.000} -> {fed.Battery!.SupplyRatio:0.000})");
        Check(fed.Battery.SupplyRatio<=1+1e-5f,"Generators stop at rated");
        Check(MainGunParts.EffectiveFirepower(fed)>MainGunParts.EffectiveFirepower(slow),"A fed mix fires faster");
        Check(Near(PowerParts.Supply(100,115,new[]{PowerParts.ById("mars-gen-l")}),115)&&Near(PowerParts.Supply(100,90,new[]{PowerParts.ById("mars-gen-l")}),100),
            "Supply rule: min(hull + generators, rated), never below the hull's own");
        // Runtime: the turrets reload at the fed ratio.
        var body=new ShipBody("FED",fed,Faction.Blue);
        Check(body.Railguns.All(g=>Near(g.SupplyRatio,fed.Battery.SupplyRatio)),"Turrets run at the fed supply ratio");
    }

    private static void Rejections()
    {
        var escort=ResearchTree.Catalog.Resolve("escort",new[]{"fire-control"},Array.Empty<EquipmentMount>()).Definition;
        Check(MainGunParts.Reject(escort,new string?[]{"earth-rail-m3","earth-rail-m1-hp"},new[]{"earth-gen-s"}) is null,"Generator fits spare volume");
        Check(MainGunParts.Reject(escort,new string?[]{"earth-rail-m3","earth-rail-m1-hp"},new[]{"earth-gen-m"})!.Contains("용적"),"Power parts count toward weapon volume");
        Check(MainGunParts.Reject(escort,new string?[]{null,null},new[]{"mars-gen-s"})!.Contains("다른 진영"),"No cross-faction power parts");
        Check(MainGunParts.Reject(escort,new string?[]{null,null},Enumerable.Repeat("earth-cooler-s",5).ToArray())!.Contains("4개"),"At most four power parts");
        Check(MainGunParts.Reject(escort,new string?[]{null,null},new[]{"missing-part"})!.Contains("알 수 없는"),"Unknown power part");
        Reject(()=>ShipLoadouts.Resolve(new ShipLoadout("escort"){Components=new(MainGun:"main-triple"){PowerParts=new[]{"earth-cooler-s"}}}),
            "socket main guns","Power parts need socket guns, not a catalog primary");
        // An all-stock fit without power parts keeps its content identity.
        Check(ShipLoadouts.Resolve(new ShipLoadout("escort")).LoadoutId==ShipLoadouts.Resolve(new ShipLoadout("escort"){Components=new()}).LoadoutId,"No power parts, no change");
    }

    private static void Research()
    {
        var p=HangarProfile.Campaign(DesignFamily.Earth);var r=p.Research!;r.Points[DesignFamily.Earth]=10000;
        r.GrantPath("escort");r.Upgrades["escort"]=new[]{"fire-control"};
        Reject(()=>p.SetPowerPart("escort",0,0,"earth-cooler-s"),"연구","Unresearched power part needs research");
        long before=r.Points[DesignFamily.Earth];
        p.SetPowerPart("escort",0,0,"earth-cooler-s",research:true);
        Check(r.HasPart(DesignFamily.Earth,"earth-cooler-s")&&r.Points[DesignFamily.Earth]==before-PowerParts.ById("earth-cooler-s").Cost,"Hangar researches and fits in one step");
        Check(p.Fit(new("escort",0)).Components is {PowerParts:["earth-cooler-s"]},"Preset stores power parts");
        Reject(()=>p.SetPowerPart("escort",0,2,"earth-cooler-s"),"용적","Power slots still obey weapon volume");
        p.SetPowerPart("escort",0,0,null);
        Check(p.Fit(new("escort",0)).Components is null,"Emptying the last power slot clears the selection");
        p.SetPowerPart("escort",0,0,"earth-cooler-s");
        p.SetComponent("escort",0,ComponentSlot.MainGun,"main-triple",research:true);
        Check(p.Fit(new("escort",0)).Components is {MainGun:"main-triple",PowerParts:null},"A catalog primary replaces power parts");
        p.SetComponent("escort",0,ComponentSlot.MainGun,null);p.SetPowerPart("escort",0,0,"earth-cooler-s");
        var restored=HangarStorage.Parse(HangarStorage.Serialize(p));
        Check(restored.Fit(new("escort",0)).Components is {PowerParts:["earth-cooler-s"]}&&restored.Research!.HasPart(DesignFamily.Earth,"earth-cooler-s"),
            "Power parts and their research survive a save");
        var fit=p.Fit(new("escort",0));var owned=FleetLoadouts.Hulls.Select(h=>h.Id).ToHashSet();
        var squad=new SquadronLoadout(FleetLoadouts.Default("escort").Members.Select((m,i)=>i==0?fit:m).ToArray());
        Check(Matchmaking.ValidateResearchLoadout(squad,owned,r.UpgradeSets()).Leader.Battery!.PowerParts is {Length:1},"Researched power part can launch");
        var none=r.UpgradeSets().ToDictionary(kv=>kv.Key,kv=>(IReadOnlySet<string>)kv.Value.Where(k=>!k.StartsWith("part:")).ToHashSet());
        Reject(()=>Matchmaking.ValidateResearchLoadout(squad,owned,none),"연구하지 않은","Unresearched power part cannot be smuggled into a match");
    }

    private static void Scores()
    {
        var hull=FleetLoadouts.Empty("escort") with{Upgrades=new[]{"fire-control"}};
        double Score(ShipComponentSelection? parts)=>FitScores.Ratio(hull with{Components=parts});
        string?[] heavy={"earth-rail-m3","earth-rail-m1-hp"};
        Check(Score(new(){MainGuns=heavy,PowerParts=new[]{"earth-gen-s"}})>Score(new(){MainGuns=heavy}),"A generator under a heavy mix raises the fit score");
        Check(Near((float)Score(new(){PowerParts=new[]{"earth-cooler-s"}}),1),"A cooler leaves a rail fit's score at stock");
        // Coolers raise beam duty: an AA cruiser's emitters spend less time cooling.
        var aa=FleetLoadouts.Empty("earth_air_defense_cruiser_t5") with{Upgrades=new[]{"fire-control"}};
        var cooled=FleetLoadouts.ResolveFit(aa with{Components=new(){PowerParts=new[]{"earth-cooler-m"}}}).Definition;
        var plain=FleetLoadouts.ResolveFit(aa).Definition;
        Check(MainGunParts.LaserDuty(plain,plain.Power.CoolingMw)<1&&MainGunParts.LaserDuty(cooled,cooled.Power.CoolingMw)>MainGunParts.LaserDuty(plain,plain.Power.CoolingMw),
            "AA emitters are heat-limited and a cooler raises their duty");
        Check(FitScores.Ratio(aa with{Components=new(){PowerParts=new[]{"earth-cooler-m"}}})>1.05,"A cooler raises a laser fit's score");
        // Forced power trials: stock guns and as many of the part as the volume holds.
        FitScores.ForcedPart="earth-cooler-s";
        try
        {
            var forced=FitScores.RandomParts(hull,new Random(1));
            Check(forced is {MainGuns:null,PowerParts:{Length:>0}}&&forced.PowerParts.All(id=>id=="earth-cooler-s"),"Forced power part fills spare volume");
        }
        finally{FitScores.ForcedPart=null;}
        int withPower=0;var random=new Random(5);
        for(int i=0;i<200;i++)if(FitScores.RandomParts(hull,random,0) is {PowerParts:not null})withPower++;
        Check(withPower is > 40 and < 160,$"AI part mixes sometimes add a power part ({withPower}/200)");
    }
}
