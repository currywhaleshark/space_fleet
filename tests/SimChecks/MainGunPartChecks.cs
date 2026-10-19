using Godot;
using SpaceFleet.Sim;

/// <summary>Modular weapons phase 1: absolute main gun parts, per-socket calibers, socket/volume/power fit checks.</summary>
static class MainGunPartChecks
{
    private static int checks;
    private static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
    private static void Reject(Action a,string expected,string why)
    {
        try{a();}catch(InvalidDataException e){Check(e.Message.Contains(expected),$"{why}: '{e.Message}' lacks '{expected}'");return;}
        throw new Exception("Expected rejection: "+why);
    }
    private static bool Near(float a,float b)=>MathF.Abs(a-b)<=1e-4f*MathF.Max(1,MathF.Abs(b));
    private static ResolvedLoadout Resolve(string id,IReadOnlyList<string?>? guns,string? patch=null)
        =>ShipLoadouts.Resolve((patch is null?ShipVariants.Loadout(id):new ShipLoadout(id,patch)) with{MainGuns=guns});
    private static string?[] Stock(ShipDefinition d)=>new string?[d.Battery!.Hardpoints.Length];

    public static void Run(){Catalog();StockIdentity();Replacements();Rejections();MixedCalibers();CrossSystem();Supply();
        Ceiling();Console.WriteLine($"PASS: {checks} main gun part checks");}
    public static void RunCeiling(){Ceiling();Console.WriteLine($"PASS: {checks} main gun ceiling checks");}

    private static void Catalog()
    {
        Check(MainGunParts.All.Count>=14,"Catalog loads");
        foreach(var p in MainGunParts.All)
        {
            Check(p.Volume<=MainGunParts.SocketVolume(p.Size),"Part fits its own socket class");
            Check(p.Cost==0==(p.StockKinds is {Length:>0}),"Stock parts are free, extra parts cost RP");
        }
        // Reference hulls are the tier where each stock part was measured: part values are the current numbers.
        foreach(var (hull,part) in new[]{("interceptor","earth-rail-s1-axial"),("mars_interceptor","mars-rail-s1-axial"),
            ("escort","earth-rail-m2"),("mars_escort","mars-rail-m2-long"),("battleship","earth-rail-l2"),
            ("mars_battleship","mars-rail-l2-long"),("earth_air_defense_cruiser","earth-laser-m")})
        {
            var d=Resolve(hull,null).Definition;var p=MainGunParts.ById(part);var w=d.Battery!.Weapon;
            Check(MainGunParts.StockFor(d)==p&&w.Size==p.Size&&w.BarrelCount==p.Barrels&&w.System==p.System,$"Stock part identity: {hull}");
            Check(d.Battery.Installations.All(m=>m.Rounds==p.Rounds),$"Stock magazine: {hull}");
            if(p.Railgun is { } r)
            {
                var a=w.Railgun!;
                Check(Near(a.MuzzleSpeed,r.MuzzleSpeed)&&Near(a.ReloadSeconds,r.ReloadSeconds)&&Near(a.Energy,r.Energy)
                    &&Near(a.PenetrationMm,r.PenetrationMm)&&Near(a.ModuleDamage,r.ModuleDamage)&&Near(a.MaxRange,r.MaxRange)
                    &&Near(a.SensorErrorMeters,r.SensorErrorMeters)&&Near(a.SensorErrorPerKm,r.SensorErrorPerKm)
                    &&Near(a.VelocityError,r.VelocityError)&&Near(a.ShotHeatMj,r.ShotHeatMj)&&Near(a.AimErrorScale,r.AimErrorScale),
                    $"Stock part reproduces current ballistics: {hull}");
            }
            else
            {
                var a=w.Laser!;var l=p.Laser!;
                Check(Near(a.EnergyPerSecond,l.EnergyPerSecond)&&Near(a.ModuleDamagePerSecond,l.ModuleDamagePerSecond)
                    &&Near(a.PenetrationMm,l.PenetrationMm)&&Near(a.HeatMw,l.HeatMw)&&Near(a.FalloffMeters,l.FalloffMeters)
                    &&Near(a.SensorErrorMeters,l.SensorErrorMeters)&&Near(a.SensorErrorPerKm,l.SensorErrorPerKm)
                    &&a.RampSeconds==l.RampSeconds&&a.MaxArmorReduction==l.MaxArmorReduction&&a.RayRange==l.RayRange,
                    $"Stock part reproduces current emitter: {hull}");
            }
        }
    }

    private static void StockIdentity()
    {
        int hulls=0;
        foreach(var source in FleetLoadouts.Hulls)
        {
            var stock=FleetLoadouts.ResolveFit(FleetLoadouts.Empty(source.Id));var d=stock.Definition;
            Check(Resolve(source.Id,null).ContentHash==stock.ContentHash,$"Loadout helper is the stock fit: {source.Id}");
            if(d.Battery is null)continue;
            hulls++;
            var part=MainGunParts.StockFor(d);
            Check(MainGunParts.Reject(d,Stock(d)) is null,$"Stock fit passes size/volume/power: {source.Id}");
            Check(d.Battery.Hardpoints.Length*part.RatedMw<=d.Power.Weapons,$"Stock rated power within weapons channel: {source.Id}");
            Check(Resolve(source.Id,Stock(d)).ContentHash==stock.ContentHash,$"Null parts keep content ID: {source.Id}");
            var named=Enumerable.Repeat<string?>(part.Id,d.Battery.Hardpoints.Length).ToArray();
            var explicitStock=Resolve(source.Id,named);
            Check(explicitStock.ContentHash==stock.ContentHash&&explicitStock.DefinitionJson==stock.DefinitionJson,
                $"Explicit stock parts keep definition and content ID: {source.Id}");
            Check(d.Battery.SocketWeapons is null&&!d.ToJson().Contains("socketWeapons",StringComparison.OrdinalIgnoreCase),
                $"Stock definition JSON gains no socket part field: {source.Id}");
        }
        Check(hulls>=34,$"Every playable hull checked: {hulls}");
    }

    private static void Replacements()
    {
        int fits=0;
        foreach(var source in FleetLoadouts.Hulls.Where(h=>h.Railgun is not null))
        {
            var d=FleetLoadouts.ResolveFit(FleetLoadouts.Empty(source.Id)).Definition;var stock=MainGunParts.StockFor(d);
            var stockBody=new ShipBody("STOCK",d,Faction.Blue);
            foreach(var p in MainGunParts.All.Where(p=>p!=stock&&MainGunParts.Fits(d,p)))
            {
                var choice=Stock(d);choice[0]=p.Id;
                if(MainGunParts.Reject(d,choice) is not null)continue;
                fits++;
                var r=Resolve(source.Id,choice).Definition;
                Check(ShipDefinition.Parse(r.ToJson(),strict:true).ToJson()==r.ToJson(),$"Part fit round-trips: {source.Id}/{p.Id}");
                Check(ShipDefinition.SerializeData(r.Modules)==ShipDefinition.SerializeData(d.Modules)&&r.ModelId==d.ModelId
                    &&ShipDefinition.SerializeData(r.Flight)==ShipDefinition.SerializeData(d.Flight)
                    &&ShipDefinition.SerializeData(r.Power)==ShipDefinition.SerializeData(d.Power),$"Part keeps hull, modules and budgets: {source.Id}/{p.Id}");
                var body=new ShipBody("PART",r,Faction.Blue);var g=body.Railguns[0];
                Check(g.Weapon.Id==p.Id&&g.BarrelCount==p.Barrels&&g.Installation.Muzzles.Length==p.Barrels,$"Socket carries the part: {source.Id}/{p.Id}");
                for(int i=1;i<body.Railguns.Length;i++)
                    Check(ShipDefinition.SerializeData(body.Railguns[i].Definition with{Mounts=null})
                        ==ShipDefinition.SerializeData((body.Railguns[i].IsLaser?stockBody.Railguns[i].Definition:MainBatteries.Supplied(stockBody.Railguns[i].Definition,body.SupplyRatio())) with{Mounts=null})
                        &&ShipDefinition.SerializeData(body.Railguns[i].Mount)==ShipDefinition.SerializeData(stockBody.Railguns[i].Mount),
                        $"Untouched sockets keep stock values at the fit's supply: {source.Id}/{i}");
                Check(body.Railguns.All(x=>x.SupplyRatio==r.Battery!.SupplyRatio)&&Near(r.Battery!.SupplyRatio,
                    MainGunParts.Supply(d)/(p.RatedMw+stock.RatedMw*(d.Battery!.Hardpoints.Length-1))),"Supply ratio = stock rated / installed rated");
                // Hull carry-over: (actual ÷ stock part) is the same factor for the new part.
                if(p.Railgun is { } pr&&stock.Railgun is { } sr)
                {
                    var a=d.Battery!.Weapon.Railgun!;var rated=g.Weapon.Railgun!;
                    Check(Near(rated.Energy,pr.Energy*a.Energy/sr.Energy)&&Near(rated.ReloadSeconds,pr.ReloadSeconds*a.ReloadSeconds/sr.ReloadSeconds)
                        &&Near(rated.MaxRange,pr.MaxRange*a.MaxRange/sr.MaxRange),$"Tier/research ratios carry to the part: {source.Id}/{p.Id}");
                    Check(Near(g.Definition.Energy,rated.Energy*g.Overcharge)&&Near(g.Definition.MaxRange,rated.MaxRange)
                        &&Near(g.Definition.ReloadSeconds,rated.ReloadSeconds/MathF.Min(1,g.SupplyRatio)),$"Runtime = rated at supply: {source.Id}/{p.Id}");
                    Check(g.Definition.Rounds==Math.Max(1,(int)MathF.Round(d.Battery.Installations[0].Rounds*(float)p.Rounds/stock.Rounds)),
                        $"Magazine scales with the part: {source.Id}/{p.Id}");
                }
            }
        }
        Console.WriteLine($"  replacement fits {fits}");Check(fits>=15,$"Replacement fits exercised: {fits}");
    }

    private static void Rejections()
    {
        var escort=Resolve("escort",null).Definition;
        Reject(()=>Resolve("earth_air_defense_cruiser",new[]{"earth-laser-l",null}),"소켓보다 큰","Large part in a medium socket");
        Reject(()=>Resolve("mars_battleship",new[]{"mars-rail-xl1",null}),"소켓보다 큰","Extra-large part in a large socket");
        // Raise the weapons channel so only volume binds: 4 + 4 > 3.5 × 2.
        const string power="{\"power\":{\"weapons\":200}}";
        Check(Resolve("escort",null,power).Definition.Power.Weapons==200,"Test hull patch applies");
        // The T3 gun cruiser holds 7: two triples (4 + 4) exceed it while their 140 MW stays within 1.5x supply.
        Reject(()=>Resolve("earth_gun_cruiser",new[]{"earth-rail-m3","earth-rail-m3"}),"용적 초과","Hull weapon volume");
        // Volume 4 + 3 = 7 fits; rated 70 + 45 = 115 MW over the 100 MW supply is allowed and reloads slower.
        Check(MainGunParts.Reject(escort,new[]{"earth-rail-m3","earth-rail-m1-hp"}) is null
            &&Near(Resolve("escort",new[]{"earth-rail-m3","earth-rail-m1-hp"}).Definition.Battery!.SupplyRatio,100/115f),"Under-supplied fit installs");
        // Rated draw is capped at 1.5x supply.
        var m3=MainGunParts.ById("earth-rail-m3");
        Check(MainGunParts.Totals(new[]{m3,m3},100,100) is null&&MainGunParts.Totals(new[]{m3,m3},100,90)!.Contains("전력 과다"),"Rated draw limited to 1.5x supply");
        Reject(()=>Resolve("mars_escort",new[]{"earth-rail-m2",null}),"다른 진영","No cross-faction parts");
        Reject(()=>Resolve("escort",new[]{"earth-laser-m",null}),"연구할 수 없는","Branch-locked part");
        Reject(()=>Resolve("earth_air_defense_cruiser",new[]{null,"earth-rail-s2"}),"계통","One weapon system per ship");
        Reject(()=>Resolve("escort",new[]{"earth-rail-m2"}),"소켓 2개","One part per socket");
        Reject(()=>Resolve("escort",new[]{"missing-gun",null}),"알 수 없는","Unknown part");
        var fixedSocket=Resolve("interceptor",null).Definition.Battery!.Hardpoints[0];
        Check(fixedSocket.Fixed&&MainGunParts.Socket(fixedSocket,MainGunParts.ById("earth-rail-s2"))!.Contains("단장"),"Fixed axis takes single barrels only");
        Reject(()=>ShipLoadouts.Resolve(new ShipLoadout("escort"){MainGuns=new[]{"earth-rail-m1-hp",null},Components=new(MainGun:"main-single")}),
            "not both","Parts and catalog primary are exclusive");
        // Socket parts survive strict schema validation only with one system and per-socket barrels.
        var fit=Resolve("battleship",new[]{"earth-rail-l3",null,"earth-rail-s2"}).Definition;
        var json=System.Text.Json.Nodes.JsonNode.Parse(fit.ToJson())!.AsObject();
        json["MainBattery"]!["SocketWeapons"]![2]!["BarrelCount"]=1;
        Reject(()=>ShipDefinition.Parse(json.ToJsonString(),strict:true),"invalid barrels","Socket barrels must match installed muzzles");
        Check(escort.Battery!.SocketWeapons is null,"Catalog hull untouched");
    }

    private static void MixedCalibers()
    {
        // Earth T4 battleship: 240 mm triple, stock 240 mm twin, 100 mm twin on three large sockets.
        var stock=Resolve("battleship",null).Definition;
        var d=Resolve("battleship",new[]{"earth-rail-l3",null,"earth-rail-s2"}).Definition;
        var b=d.Battery!;
        Check(b.SocketWeapons!.Select(w=>w.Id).SequenceEqual(new[]{"earth-rail-l3","earth-rail-l2","earth-rail-s2"}),"Per-socket part IDs");
        Check(b.Weapon.Id=="earth-rail-l3"&&d.Railgun!.MaxRange==250000,"Longest-reaching socket is the ship-level view");
        Check(d.Railgun!.Rounds==300+240+300&&b.Installations.Select(m=>m.Muzzles.Length).SequenceEqual(new[]{3,2,2}),"Barrels and magazines per socket");
        var world=new SimWorld();var body=world.Add(new ShipBody("MIX",d,Faction.Blue));body.Control=new ShipControl{FlightAssist=false};
        var s=body.Railguns;var st=new ShipBody("REF",stock,Faction.Blue).Railguns[1];
        float ratio=MainGunParts.Supply(stock)/new[]{"earth-rail-l3","earth-rail-l2","earth-rail-s2"}.Sum(id=>MainGunParts.ById(id).RatedMw);
        Check(Near(b.SupplyRatio,ratio)&&s.All(g=>Near(g.Overcharge,ratio)),"Lighter fit overcharges every socket");
        Check(ShipDefinition.SerializeData(s[1].Definition with{Mounts=null})==ShipDefinition.SerializeData(MainBatteries.Supplied(st.Definition,b.SupplyRatio) with{Mounts=null})
            &&ShipDefinition.SerializeData(s[1].Mount)==ShipDefinition.SerializeData(st.Mount),"Stock socket keeps stock values at the fit's supply");
        Check(s[0].Weapon.Railgun!.Energy==1250&&s[0].Weapon.Railgun.ReloadSeconds==3.4f&&s[2].Weapon.Railgun!.Energy==300
            &&s[2].Weapon.Railgun.MuzzleSpeed==7000&&s[2].Definition.MaxRange==100000,"Each socket compiles its own ballistics");
        Check(s[0].Mount!.YawRate==st.Mount!.YawRate*.8f&&s[2].Mount!.YawRate==st.Mount.YawRate,"Traverse follows the socket's part");
        // Broadside aim clears the forward housings, as in the component firing checks.
        var aim=new Vector3(1,.035f,-.4f).Normalized();
        int ticks=(int)Math.Ceiling((90/s.Min(g=>Math.Min(g.Mount!.YawRate,g.Mount.ElevationRate))+1)*SimWorld.TickRate);
        for(int t=0;t<ticks;t++){foreach(var g in s)g.Aim(aim);world.Step();}
        float heat=body.Power.HeatMj;
        var fire=world.FireRailguns(body,aim);
        Check(fire.Shots==7,$"Every barrel fires: {fire}");
        foreach(var g in s)
        {
            var rounds=world.Projectiles.Where(p=>p.ModuleId==g.Definition.ModuleId).ToArray();
            Check(rounds.Length==g.BarrelCount,"Rounds leave their own socket");
            Check(rounds.All(p=>p.Packet.Energy==g.Definition.Energy&&p.Packet.PenetrationMm==g.Definition.PenetrationMm
                &&Near((float)(p.Velocity-body.Velocity).Length(),g.Definition.MuzzleSpeed)),"Rounds carry their own caliber's packet and speed");
            Check(Near(g.ReloadRemaining,g.Definition.ReloadSeconds),"Reload follows the socket's part");
        }
        Check(MathF.Abs(body.Power.HeatMj-heat-s.Sum(g=>g.Definition.ShotHeatMj*g.BarrelCount))<1&&s[0].Definition.ShotHeatMj>260*ratio*ratio-.01f,
            "Shot heat sums each part's own heat at overcharge squared");
    }

    private static void CrossSystem()
    {
        // The AA emitter hull can carry small twin rails; the laser factors carry the hull's own tier.
        foreach(var id in new[]{"earth_air_defense_cruiser","earth_air_defense_cruiser_t5"}.Where(i=>FleetLoadouts.Hulls.Any(h=>h.Id==i)))
        {
            var d=Resolve(id,null).Definition;var r=Resolve(id,new[]{"earth-rail-s2","earth-rail-s2"}).Definition;
            var a=d.Battery!.Weapon.Laser!;var s=MainGunParts.ById("earth-laser-m").Laser!;var p=MainGunParts.ById("earth-rail-s2").Railgun!;
            Check(r.Battery!.Weapon.System==PrimaryWeaponSystem.Railgun&&r.Railgun!.Rounds==600,"Emitter hull switches system wholesale with part magazines");
            Check(Near(r.Railgun!.Energy,p.Energy*a.EnergyPerSecond/s.EnergyPerSecond)&&Near(r.Railgun.ReloadSeconds,p.ReloadSeconds),
                "Laser hull factor carries to rail energy, not cadence");
            var world=new SimWorld();var body=world.Add(new ShipBody("RAIL",r,Faction.Blue));
            Check(body.Railguns.All(g=>!g.IsLaser&&g.BarrelCount==2&&g.Rounds==300&&g.Overcharge==MainBatteries.MaxOvercharge),"Rail sockets armed, light fit overcharged to the cap");
        }
    }

    private static float SupplyRatio(this ShipBody ship)=>ship.Definition.Battery?.SupplyRatio??1;

    /// <summary>Controlled fire at under-rated, rated and overcharged supply: per-shot energy and speed, reload, heat.</summary>
    private static void Supply()
    {
        var rows=new List<string>{"case\tsupply\tenergy\tspeed\treload_s\theat_per_salvo"};
        void Trial(string name,string hull,string?[]? guns,float ratio)
        {
            var d=Resolve(hull,guns).Definition;
            Check(Near(d.Battery!.SupplyRatio,ratio),$"{name}: supply ratio {d.Battery.SupplyRatio}");
            var world=new SimWorld();var body=world.Add(new ShipBody(name,d,Faction.Blue));body.Control=new ShipControl{FlightAssist=false};
            var g=body.Railguns[0];var rated=g.Weapon.Railgun!;
            var aim=new Vector3(1,.035f,-.4f).Normalized();
            int ticks=(int)Math.Ceiling((90/Math.Min(g.Mount!.YawRate,g.Mount.ElevationRate)+1)*SimWorld.TickRate);
            for(int t=0;t<ticks;t++){g.Aim(aim);world.Step();}
            float heat=body.Power.HeatMj;
            var shot=world.FireRailgun(body,g,aim);
            Check(shot.Fired&&shot.Shots==g.BarrelCount,$"{name}: salvo fires {shot}");
            float over=Math.Clamp(ratio,1,1.3f),under=Math.Min(ratio,1);
            var rounds=world.Projectiles.Where(p=>p.ModuleId==g.Definition.ModuleId).ToArray();
            float speed=(float)(rounds[0].Velocity-body.Velocity).Length();
            Check(rounds.All(p=>Near(p.Packet.Energy,rated.Energy*over)&&p.Packet.PenetrationMm==rated.PenetrationMm&&p.Packet.ModuleDamage==rated.ModuleDamage)
                &&Near(speed,rated.MuzzleSpeed*over),$"{name}: energy and speed x overcharge only");
            float salvoHeat=body.Power.HeatMj-heat;
            Check(MathF.Abs(salvoHeat-rated.ShotHeatMj*over*over*g.BarrelCount)<.5f,$"{name}: heat x overcharge^2 ({salvoHeat})");
            int reload=0;while(!g.Ready&&reload<60*SimWorld.TickRate){foreach(var x in body.Railguns)x.Aim(aim);world.Step();reload++;}
            float seconds=reload*(float)SimWorld.TickDelta;
            Check(MathF.Abs(seconds-rated.ReloadSeconds/under)<=2*(float)SimWorld.TickDelta,$"{name}: reload {seconds}s vs {rated.ReloadSeconds/under}s");
            rows.Add($"{name}\t{ratio:0.###}\t{rounds[0].Packet.Energy:0.#}\t{speed:0}\t{seconds:0.###}\t{salvoHeat:0.#}");
        }
        Trial("earth-escort-stock","escort",null,1);
        Trial("earth-escort-under","escort",new[]{"earth-rail-m3","earth-rail-m1-hp"},100/115f);
        Trial("mars-escort-rated","mars_escort",new[]{"mars-rail-m1-long",null},1);
        Trial("earth-escort-over","escort",new[]{"earth-rail-m1-hp","earth-rail-s2"},100/70f);
        Trial("earth-bb-over","battleship",new[]{"earth-rail-l3",null,"earth-rail-s2"},168f/(76+56+25));
        // Lasers: damage rate follows supply to 130%, heat follows the square above rated.
        var laserHull=Resolve("earth_air_defense_cruiser",null).Definition;var stockLaser=laserHull.Battery!.Weapon.Laser!;
        var dim=MainBatteries.Supplied(stockLaser,.8f);var hot=MainBatteries.Supplied(stockLaser,1.5f);
        Check(Near(dim.EnergyPerSecond,stockLaser.EnergyPerSecond*.8f)&&Near(dim.HeatMw,stockLaser.HeatMw*.8f)
            &&Near(hot.ModuleDamagePerSecond,stockLaser.ModuleDamagePerSecond*1.3f)&&Near(hot.HeatMw,stockLaser.HeatMw*1.69f)
            &&MainBatteries.Supplied(stockLaser,1)==stockLaser,"Laser supply scaling");
        var stockBody=new ShipBody("AA",laserHull,Faction.Blue);
        Check(stockBody.Railguns.All(g=>g.SupplyRatio==1&&g.Laser!.Definition==g.Weapon.Laser),"Stock emitters run at rated");
        string dir=Path.Combine("docs","validation","modular_weapons_phase2_20261019");
        if(Directory.Exists(dir))File.WriteAllLines(Path.Combine(dir,"supply_trials.tsv"),rows);
        foreach(var row in rows)Console.WriteLine("  "+row);
    }

    /// <summary>The hull a player moves to next: same design and branch one tier up; escorts move to the gun cruiser.</summary>
    private static ShipDefinition? NextTier(ResearchNode node)
    {
        var tree=ResearchTree.Catalog;var design=tree.Design(node);
        var next=tree.Playable.FirstOrDefault(n=>tree.Design(n)==design&&n.Branch==node.Branch&&n.Tier==node.Tier+1)
            ??tree.Playable.FirstOrDefault(n=>tree.Design(n)==design&&n.ParentId==node.Id&&n.Branch==ShipBranch.GunCruiser);
        return next is null?null:FleetLoadouts.ResolveFit(FleetLoadouts.Empty(next.HullId!)).Definition;
    }

    /// <summary>
    /// Phase 3 ceiling (§7): a hull with every container upgrade and its strongest legal main gun fit does not
    /// out-shoot the next tier's stock hull in sustained energy per second.
    /// </summary>
    private static void Ceiling()
    {
        var rows=new List<string>{"hull\ttier\tstock\tupgraded_stock\tbest\tbest_fit\tnext\tnext_stock\tbest/next"};
        foreach(var node in ResearchTree.Catalog.Playable.OrderBy(n=>n.HullId,StringComparer.Ordinal))
        {
            string id=node.HullId!;
            var stock=FleetLoadouts.ResolveFit(FleetLoadouts.Empty(id)).Definition;
            if(stock.Battery is null)continue;
            var all=node.Upgrades.Select(u=>u.Id).ToArray();
            var upgraded=ResearchTree.Catalog.Resolve(id,all,Array.Empty<EquipmentMount>()).Definition;
            // Lasers and rails do not share a firepower unit; a hull is held to its own system's ceiling.
            var system=MainGunParts.StockFor(stock).System;
            float best=0;string bestFit="stock";int combos=0;
            foreach(var combo in MainGunParts.Combos(upgraded,sameSystem:false))
            {
                combos++;
                if(combo.Any(c=>c is not null&&MainGunParts.ById(c).System!=system))continue;
                var d=ResearchTree.Catalog.Resolve(id,all,Array.Empty<EquipmentMount>(),new ShipComponentSelection{MainGuns=combo}).Definition;
                float e=MainGunParts.SustainedEnergy(d);
                if(e>best){best=e;bestFit=string.Join("+",combo.Select(c=>c??"stock"));}
            }
            Check(combos>0,$"{id}: upgraded stock fit is legal");
            var next=NextTier(node);float nextStock=next is null?0:MainGunParts.SustainedEnergy(next);
            float ratio=next is null?0:best/nextStock;
            rows.Add($"{id}\t{node.Tier}\t{MainGunParts.SustainedEnergy(stock):0}\t{MainGunParts.SustainedEnergy(upgraded):0}\t{best:0}\t{bestFit}\t{next?.Id??"-"}\t{nextStock:0}\t{ratio:0.000}");
            CeilingRatios[id]=ratio;
        }
        string dir=Path.Combine("docs","validation","modular_weapons_phase3_20261020");
        if(Directory.Exists(dir))File.WriteAllLines(Path.Combine(dir,"ceiling.tsv"),rows);
        foreach(var row in rows)Console.WriteLine("  "+row);
        foreach(var (id,ratio) in CeilingRatios)
            Check(ratio<=1.0001f,$"{id}: fully upgraded best fit {ratio:0.000}x the next tier's stock firepower");
    }
    private static readonly Dictionary<string,float> CeilingRatios=new();
}
