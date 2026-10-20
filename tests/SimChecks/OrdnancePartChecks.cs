using Godot;
using SpaceFleet.Game;
using SpaceFleet.Sim;

/// <summary>Modular weapons phase 5: missile launchers (cells), missiles and point-defence parts.</summary>
static class OrdnancePartChecks
{
    private static int checks;
    private static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
    private static void Reject(Action a,string expected,string why)
    {
        try{a();}catch(InvalidDataException e){Check(e.Message.Contains(expected),$"{why}: '{e.Message}' lacks '{expected}'");return;}
        throw new Exception("Expected rejection: "+why);
    }
    private static bool Near(float a,float b)=>MathF.Abs(a-b)<=1e-4f*MathF.Max(1,MathF.Abs(b));
    private static ResolvedLoadout Resolve(string id,ShipComponentSelection? parts)=>ShipLoadouts.Resolve(ShipVariants.Loadout(id) with{Components=parts});
    private static ShipDefinition Stock(string id)=>FleetLoadouts.ResolveFit(FleetLoadouts.Empty(id)).Definition;

    public static void Run(){Catalog();Identity();Replacements();Rejections();Cells();Research();
        Console.WriteLine($"PASS: {checks} missile launcher/missile/PD part checks");}

    private static void Catalog()
    {
        foreach(var (hull,missile,pd) in new[]{("interceptor","missile-light","pd-small"),("earth_air_defense_cruiser","missile-counter","pd-medium"),
            ("escort","missile-medium","pd-medium"),("battleship","missile-heavy","pd-large")})
        {
            var d=Stock(hull);var m=OrdnanceParts.ById(missile);var p=OrdnanceParts.ById(pd);
            Check(OrdnanceParts.StockMissile(d)==m&&OrdnanceParts.StockPointDefense(d)==p,$"{hull}: stock ordnance parts");
            var md=d.Missiles!;
            Check(Near(md.Energy,m.Energy)&&Near(md.PenetrationMm,m.PenetrationMm)&&Near(md.ModuleDamage,m.ModuleDamage)&&Near(md.HitPoints,m.HitPoints)
                &&Near(md.AccelG,m.AccelG)&&Near(md.SeekerRangeMeters,m.SeekerRangeMeters)&&md.CellCount==1,$"{hull}: stock missile reproduces current values");
            if(hull!="earth_air_defense_cruiser")
            {
                var pdd=d.PointDefense!;
                Check(Near(pdd.RangeMeters,p.RangeMeters)&&Near(pdd.ShotsPerSecond,p.ShotsPerSecond)&&Near(pdd.DamagePerHit,p.DamagePerHit)
                    &&Near(pdd.MuzzleSpeed,p.MuzzleSpeed),$"{hull}: stock PD reproduces current values");
            }
        }
    }

    private static void Identity()
    {
        foreach(var source in FleetLoadouts.Hulls)
        {
            var stock=FleetLoadouts.ResolveFit(FleetLoadouts.Empty(source.Id));var d=stock.Definition;
            Check(!d.ToJson().Contains("\"Cells\""),$"{source.Id}: stock missile JSON has no cell field");
            var explicitStock=new ShipComponentSelection{Launcher=OrdnanceParts.StockLauncher.Id,
                Missile=OrdnanceParts.StockMissile(d)?.Id,PointDefensePart=OrdnanceParts.StockPointDefense(d)?.Id};
            Check(Resolve(source.Id,explicitStock).ContentHash==stock.ContentHash,$"{source.Id}: explicit stock ordnance keeps the content ID");
        }
    }

    private static void Replacements()
    {
        int fits=0;
        foreach(var source in FleetLoadouts.Hulls)
        {
            var d=Stock(source.Id);
            foreach(var part in OrdnanceParts.All.Where(p=>!p.Stock&&OrdnanceParts.Socket(d,p) is null))
            {
                fits++;
                var sel=part.Kind switch{OrdnanceKind.Launcher=>new ShipComponentSelection{Launcher=part.Id},
                    OrdnanceKind.Missile=>new ShipComponentSelection{Missile=part.Id},_=>new ShipComponentSelection{PointDefensePart=part.Id}};
                var r=Resolve(source.Id,sel).Definition;
                Check(ShipDefinition.Parse(r.ToJson(),strict:true).ToJson()==r.ToJson(),$"{source.Id}/{part.Id}: round-trips");
                Check(ShipDefinition.SerializeData(r.Modules)==ShipDefinition.SerializeData(d.Modules)&&ShipDefinition.SerializeData(r.Railgun)==ShipDefinition.SerializeData(d.Railgun),
                    $"{source.Id}/{part.Id}: hull and main guns untouched");
                if(part.Kind==OrdnanceKind.Launcher)
                    Check(r.Missiles!.CellCount==part.Cells&&r.Missiles.Rounds==Math.Max(part.Cells,(int)MathF.Round(d.Missiles!.Rounds*part.MagazineScale))
                        &&Near(r.Missiles.ReloadSeconds,d.Missiles.ReloadSeconds*part.ReloadScale)&&Near(r.Missiles.Energy,d.Missiles.Energy),$"{source.Id}/{part.Id}: cells, magazine, reload");
                else if(part.Kind==OrdnanceKind.Missile)
                {
                    var s=OrdnanceParts.StockMissile(d)!;
                    Check(Near(r.Missiles!.Energy,part.Energy*d.Missiles!.Energy/s.Energy)&&Near(r.Missiles.HitPoints,part.HitPoints*d.Missiles.HitPoints/s.HitPoints)
                        &&r.Missiles.Rounds==d.Missiles.Rounds,$"{source.Id}/{part.Id}: tier ratio carries to the missile");
                }
                else
                {
                    var s=OrdnanceParts.StockPointDefense(d)!;var pd=r.PointDefense!;
                    Check(Near(pd.ShotsPerSecond,part.ShotsPerSecond*d.PointDefense!.ShotsPerSecond/s.ShotsPerSecond)
                        &&Near(pd.RangeMeters,part.RangeMeters*d.PointDefense.RangeMeters/s.RangeMeters)&&pd.Mounts.Length==d.PointDefense.Mounts.Length
                        &&Near(pd.EffectiveShipPacket.Range,pd.RangeMeters),$"{source.Id}/{part.Id}: PD values carry ratios to every mount");
                }
                var fit=FleetLoadouts.Empty(source.Id) with{Components=sel};
                Check(FitScores.Ratio(fit) is var ratio&&double.IsFinite(ratio)&&ratio>0,$"{source.Id}/{part.Id}: scored");
            }
        }
        Console.WriteLine($"  ordnance replacement fits {fits}");Check(fits>=60,$"Ordnance fits exercised: {fits}");
    }

    private static void Rejections()
    {
        // Family and size rules apply even where the branch is allowed.
        var heavy=OrdnanceParts.ById("missile-heavy-ap") with{Branches=new[]{ShipBranch.Escort}};
        Check(OrdnanceParts.Socket(Stock("escort"),heavy)!.Contains("계열"),"Heavy missile in a medium tube");
        var quad=OrdnanceParts.ById("launcher-quad") with{Branches=new[]{ShipBranch.Interceptor},Size=WeaponSize.Medium};
        Check(OrdnanceParts.Socket(Stock("interceptor"),quad)!.Contains("큰 발사기"),"Medium launcher on a small craft");
        var flak=OrdnanceParts.ById("pd-flak-heavy") with{Branches=new[]{ShipBranch.Escort}};
        Check(OrdnanceParts.Socket(Stock("escort"),flak)!.Contains("포대 등급"),"Large PD class on an escort");
        Reject(()=>Resolve("mars_escort",new(){Missile="missile-light"}),"기본 부품","Another family's stock missile");
        Reject(()=>Resolve("earth_gun_cruiser",new(){Launcher="launcher-vls8"}),"진영","Mars-only launcher on an Earth hull");
        Reject(()=>Resolve("escort",new(){Launcher="launcher-quad"}),"연구할 수 없는","Branch-locked launcher");
        Reject(()=>Resolve("escort",new(){PointDefensePart="pd-flak-heavy"}),"연구할 수 없는","Battleship PD on an escort");
        Reject(()=>Resolve("escort",new(){PointDefensePart="pd-gatling",Secondary="pd-barrage"}),"not both","PD part and catalog secondary");
        Reject(()=>Resolve("escort",new(){Missile="pd-gatling"}),"종류","Wrong part kind in a slot");
    }

    private static void Cells()
    {
        // Stock tube: exactly the old timing (one ready, refilled after the reload).
        foreach(var (hull,parts,cells) in new[]{("mars_missile_cruiser_t4",(ShipComponentSelection?)null,1),
            ("mars_missile_cruiser_t4",new ShipComponentSelection{Launcher="launcher-vls8"},8),("battleship",new ShipComponentSelection{Launcher="launcher-quad"},4)})
        {
            var d=Resolve(hull,parts).Definition;
            var world=new SimWorld();var shooter=world.Add(new ShipBody("LAUNCH",d,Faction.Red));
            var target=world.Add(new ShipBody("TARGET",ShipDefinitions.ById("escort"),Faction.Blue));
            target.Place(new Vec3d(0,0,-60000),Quaternion.Identity);
            shooter.Control=target.Control=new ShipControl{FlightAssist=false};
            world.Sensors.Update(world.Ships,world.Time,force:true);
            Check(shooter.Ordnance.LoadedCells==cells,$"{hull}: {cells} cells loaded at start");
            int fired=0;var starts=new List<Vector3>();
            while(world.LaunchMissile(shooter,target).Fired){fired++;starts.Add((world.Missiles[^1].Position-shooter.Position).ToVector3());}
            Check(fired==cells&&shooter.Ordnance.Missiles==d.Missiles!.Rounds-cells,$"{hull}: salvo of {cells} ({fired})");
            Check(starts.Distinct().Count()==cells,$"{hull}: each cell launches from its own spot");
            int ticks=(int)Math.Ceiling(d.Missiles.ReloadSeconds*SimWorld.TickRate)+1;
            for(int t=0;t<ticks;t++)world.Step();
            Check(shooter.Ordnance.LoadedCells==1,$"{hull}: one cell refills per reload ({shooter.Ordnance.LoadedCells})");
            for(int t=0;t<ticks*(cells-1);t++)world.Step();
            Check(shooter.Ordnance.LoadedCells==cells,$"{hull}: every cell refills in turn ({shooter.Ordnance.LoadedCells})");
        }
        // AI salvos grow with the cells.
        var vls=Resolve("mars_missile_cruiser_t4",new(){Launcher="launcher-vls8"}).Definition;
        Check(Math.Max(vls.Ai.MissilesPerSalvo,vls.Missiles!.CellCount)==8,"AI salvo size follows the cells");
    }

    private static void Research()
    {
        var p=HangarProfile.Campaign(DesignFamily.Mars);var r=p.Research!;r.Points[DesignFamily.Mars]=10000;r.Points[DesignFamily.Earth]=10000;
        Reject(()=>r.BuyPart(DesignFamily.Mars,"launcher-vls8"),"함급","Needs a qualifying hull");
        r.GrantPath("mars_missile_cruiser_t4");
        long before=r.Points[DesignFamily.Mars];
        Check(r.BuyPart(DesignFamily.Mars,"launcher-vls8")&&r.Points[DesignFamily.Mars]==before-320,"Launcher research charges once");
        Check(r.PartSponsor(DesignFamily.Earth,"launcher-vls8") is null,"Mars-only launcher has no Earth sponsor");
        Check(r.HasPart(DesignFamily.Mars,"missile-medium")&&r.HasPart(DesignFamily.Mars,"pd-medium"),"Stock ordnance needs no research");
        p.SetOrdnance("mars_missile_cruiser_t4",0,OrdnanceKind.Launcher,"launcher-vls8");
        p.SetOrdnance("mars_missile_cruiser_t4",0,OrdnanceKind.PointDefense,"pd-gatling",research:true);
        Check(r.HasPart(DesignFamily.Mars,"pd-gatling")&&!r.HasPart(DesignFamily.Earth,"pd-gatling"),"PD research is per design");
        var fit=p.Fit(new("mars_missile_cruiser_t4",0));
        Check(fit.Components is {Launcher:"launcher-vls8",PointDefensePart:"pd-gatling"},"Preset stores ordnance parts");
        p.SetComponent("mars_missile_cruiser_t4",0,ComponentSlot.Secondary,"pd-barrage",research:true);
        Check(p.Fit(new("mars_missile_cruiser_t4",0)).Components is {PointDefensePart:null,Secondary:"pd-barrage",Launcher:"launcher-vls8"},"Catalog secondary replaces the PD part");
        p.SetOrdnance("mars_missile_cruiser_t4",0,OrdnanceKind.PointDefense,"pd-gatling");
        Check(p.Fit(new("mars_missile_cruiser_t4",0)).Components is {PointDefensePart:"pd-gatling",Secondary:null},"PD part replaces the catalog secondary");
        var restored=HangarStorage.Parse(HangarStorage.Serialize(p));
        Check(restored.Fit(new("mars_missile_cruiser_t4",0)).Components is {Launcher:"launcher-vls8",PointDefensePart:"pd-gatling"},"Ordnance parts survive a save");
        var squad=new SquadronLoadout(FleetLoadouts.Default("mars_missile_cruiser_t4").Members.Select((m,i)=>i==0?fit:m).ToArray());
        var owned=FleetLoadouts.Hulls.Select(h=>h.Id).ToHashSet();
        Check(Matchmaking.ValidateResearchLoadout(squad,owned,r.UpgradeSets()).Leader.Missiles!.CellCount==8,"Researched launcher can launch");
        var none=r.UpgradeSets().ToDictionary(kv=>kv.Key,kv=>(IReadOnlySet<string>)kv.Value.Where(k=>!k.StartsWith("part:")).ToHashSet());
        Reject(()=>Matchmaking.ValidateResearchLoadout(squad,owned,none),"연구하지 않은","Unresearched launcher cannot be smuggled into a match");
    }
}
