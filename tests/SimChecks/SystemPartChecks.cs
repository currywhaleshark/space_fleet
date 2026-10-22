using Godot;
using SpaceFleet.Game;
using SpaceFleet.Sim;

static class SystemPartChecks
{
    private static int checks;
    private static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
    private static void Reject(Action action,string why)
    {try{action();}catch(InvalidDataException){checks++;return;}throw new Exception("Expected rejection: "+why);}
    private static bool Near(float a,float b)=>Math.Abs(a-b)<=Math.Max(1,Math.Abs(b))*2e-6f;
    private static void SameStats(ShipDefinition a,ShipDefinition b,string why)
    {
        Check(Near(a.Flight.MassKg,b.Flight.MassKg)&&Near(a.Flight.ForwardAccel,b.Flight.ForwardAccel)
            &&Near(a.Flight.StrafeAccel,b.Flight.StrafeAccel)&&Near(a.Flight.BrakeAccel,b.Flight.BrakeAccel),why+" flight");
        Check(Near(a.Power.Engines,b.Power.Engines)&&Near(a.Power.Sensors,b.Power.Sensors)&&Near(a.Power.Shields,b.Power.Shields)
            &&Near(a.Power.OutputMw,b.Power.OutputMw)&&Near(a.Power.CoolingMw,b.Power.CoolingMw),why+" power");
        Check(Near(a.Sensors.Strength,b.Sensors.Strength)&&Near(a.Sensors.Signature,b.Sensors.Signature)
            &&SensorLocks.Channels(a)==SensorLocks.Channels(b),why+" sensors and lock channels");
        Check(Near(a.Shield.Capacity,b.Shield.Capacity)&&Near(a.Shield.RechargePerSecond,b.Shield.RechargePerSecond)
            &&Near(a.Shield.RechargeDelay,b.Shield.RechargeDelay),why+" shields");
    }
    public static void Run()
    {
        CatalogAndFits();ResearchAndStorage();Runtime();
        Console.WriteLine($"PASS: {checks} engine/sensor/shield part checks");
    }
    private static void CatalogAndFits()
    {
        var ids=MainGunParts.All.Select(p=>p.Id).Concat(OrdnanceParts.All.Select(p=>p.Id)).Concat(PowerParts.All.Select(p=>p.Id))
            .Concat(ShipComponents.All.Select(p=>p.Id)).Concat(SystemParts.All.Select(p=>p.Id)).ToArray();
        Check(ids.Distinct().Count()==ids.Length,"Part and legacy IDs never collide");
        foreach(var hull in FleetLoadouts.Hulls)
        foreach(bool upgraded in new[]{false,true})
        {
            var fit=FleetLoadouts.Empty(hull.Id) with{Upgrades=upgraded?ResearchTree.Catalog.ByHull(hull.Id).Upgrades.Select(u=>u.Id).ToArray():null};
            var baseline=FleetLoadouts.ResolveFit(fit);
            foreach(var slot in SystemParts.Slots)
            {
                var stock=SystemParts.StockFor(slot);
                var explicitStock=slot switch{ComponentSlot.Engine=>new ShipComponentSelection{EnginePart=stock.Id},
                    ComponentSlot.Sensor=>new ShipComponentSelection{SensorPart=stock.Id},_=>new ShipComponentSelection{ShieldPart=stock.Id}};
                Check(FleetLoadouts.ResolveFit(fit with{Components=explicitStock}).ContentHash==baseline.ContentHash,"Stock part preserves exact loadout hash");
                foreach(var p in SystemParts.Candidates(hull,slot).Where(p=>!p.Stock))
                {
                    var legacy=FleetLoadouts.ResolveFit(fit with{Components=new ShipComponentSelection().With(slot,p.LegacyId)}).Definition;
                    var next=SystemParts.With(null,slot,p.Id);
                    var updated=FleetLoadouts.ResolveFit(fit with{Components=next}).Definition;
                    SameStats(updated,legacy,hull.Id+"/"+p.Id);
                    Check(updated.Components?.SystemPart(slot)==p.Id&&updated.Components.Get(slot) is null,"Only the new part is materialized");
                    Check(ShipDefinition.SerializeData(updated.Modules)==ShipDefinition.SerializeData(legacy.Modules)
                        &&ShipDefinition.SerializeData(updated.Battery)==ShipDefinition.SerializeData(legacy.Battery),"Damage modules and weapon geometry are preserved");
                    Check(ShipDefinition.Parse(updated.ToJson(),strict:true).ToJson()==updated.ToJson(),"Resolved part round-trip does not reapply ratings");
                }
            }
            var old=new ShipComponentSelection(Engine:"engine-vector",Sensor:"sensor-longrange",Shield:"shield-capacity",Generator:"generator-cooled");
            var parts=SystemParts.With(SystemParts.With(SystemParts.With(old,ComponentSlot.Engine,"drive-vector"),ComponentSlot.Sensor,"scanner-longrange"),ComponentSlot.Shield,"barrier-capacity");
            SameStats(FleetLoadouts.ResolveFit(fit with{Components=old}).Definition,FleetLoadouts.ResolveFit(fit with{Components=parts}).Definition,"Combined mass/research/legacy generator");
        }
        Reject(()=>ShipLoadouts.Resolve(new ShipLoadout("escort"){Components=new(Engine:"engine-cruise"){EnginePart="drive-vector"}}),"No stacking old engine with new assembly");
        Reject(()=>ShipLoadouts.Resolve(new ShipLoadout("escort"){Components=new(){EnginePart="scanner-stock"}}),"Wrong stock category rejected");
        Reject(()=>SystemParts.With(null,ComponentSlot.Engine,"scanner-longrange"),"Wrong replacement category rejected");
        Reject(()=>SystemParts.With(null,ComponentSlot.MainGun,"drive-vector"),"No engine in weapon sockets");
        Reject(()=>SystemParts.With(null,ComponentSlot.Engine,"missing"),"Unknown part rejected");
        Reject(()=>ShipLoadouts.Resolve(new ShipLoadout("escort","{\"power\":{\"outputMw\":400}}")
            {Components=new(){EnginePart="drive-thrust"}}),"New engine respects the combined power budget");
    }
    private static void ResearchAndStorage()
    {
        var p=HangarProfile.Create("escort");var r=p.Research!;r.Points[DesignFamily.Earth]=10000;
        Reject(()=>p.SetSystemPart("escort",0,ComponentSlot.Engine,"drive-thrust"),"Unresearched part cannot equip");
        long before=r.Points[DesignFamily.Earth];
        p.SetSystemPart("escort",0,ComponentSlot.Engine,"drive-thrust",true);
        Check(r.Points[DesignFamily.Earth]==before-90&&r.HasPart(DesignFamily.Earth,"drive-thrust")&&!r.HasPart(DesignFamily.Mars,"drive-thrust"),"Fixed part cost and faction isolation");
        p.SetSystemPart("battleship",1,ComponentSlot.Engine,"drive-thrust");
        Check(r.Points[DesignFamily.Earth]==before-90,"Research shared across hulls and presets without another charge");
        foreach(var part in SystemParts.All.Where(x=>x.LegacyId is not null))
        {
            p.SetComponent("escort",0,part.Slot,part.LegacyId,true);
            long paid=r.Points[DesignFamily.Earth];
            string saved=HangarStorage.Serialize(p);var restored=HangarStorage.Parse(saved);
            Check(restored.Hulls["escort"].SystemPresets[0]!.Get(part.Slot)==part.LegacyId,"Loading old equipment preserves the original selection");
            restored.SetSystemPart("escort",0,part.Slot,part.Id);
            Check(restored.Research!.Points[DesignFamily.Earth]==paid&&restored.Hulls["escort"].SystemPresets[0]!.Get(part.Slot) is null,"Legacy research covers its equivalent part without double effects or cost");
            var launch=Matchmaking.ValidateResearchLoadout(restored.Squadron(),restored.Owned,restored.Research.UpgradeSets());
            Check(launch.Leader.Components!.SystemPart(part.Slot)==part.Id,"Inherited research is accepted by matching");
            var frozen=launch.Members[0].ContentHash;
            restored.SetSystemPart("escort",0,part.Slot,null);
            Check(launch.Members[0].ContentHash==frozen&&launch.Leader.Components.SystemPart(part.Slot)==part.Id,"Queued fit is frozen against later edits");
        }
        p.SetSlotMode(1,HangarSlot.Individual);var member=p.Hulls["escort"].Squadron[1]!;
        string shared=ShipDefinition.SerializeData(p.Hulls[member.HullId].SystemPresets);
        p.SetSlotSystemPart(1,ComponentSlot.Sensor,"scanner-longrange");
        Check(ShipDefinition.SerializeData(p.Hulls[member.HullId].SystemPresets)==shared,"Individual part cannot change shared presets");
        var reloaded=HangarStorage.Parse(HangarStorage.Serialize(p));
        Check(reloaded.Fit(reloaded.Hulls["escort"].Squadron[1]!).Components!.SensorPart=="scanner-longrange","Individual part survives save and reload");
        // A v4 per-hull unlock also authorizes the corresponding new design-wide part.
        var oldSave=HangarProfile.Create("escort");oldSave.Version=4;
        oldSave.Research!.Components["escort"]=new[]{"sensor-longrange"};
        oldSave.SetSystemPart("escort",0,ComponentSlot.Sensor,"scanner-longrange");
        Check(oldSave.Research.Points[DesignFamily.Earth]==0&&HangarStorage.Parse(HangarStorage.Serialize(oldSave)).Research!.HasPart(DesignFamily.Earth,"scanner-longrange"),"v4 unlock survives without RP reset");
        var poor=HangarProfile.Campaign();string untouched=HangarStorage.Serialize(poor);
        Reject(()=>poor.SetSystemPart(poor.SelectedHullId,0,ComponentSlot.Shield,"barrier-capacity",true),"Insufficient RP rejected atomically");
        Check(HangarStorage.Serialize(poor)==untouched,"Failed edit does not change research, fit or RP");
        var forged=p.Squadron() with{Members=p.Squadron().Members.Select((m,i)=>i==0?m with{Components=new(){ShieldPart="barrier-recharge"}}:m).ToArray()};
        var none=r.UpgradeSets().ToDictionary(kv=>kv.Key,kv=>(IReadOnlySet<string>)kv.Value.Where(k=>!k.StartsWith("part:")).ToHashSet());
        Reject(()=>Matchmaking.ValidateResearchLoadout(forged,p.Owned,none),"Unresearched parts cannot be smuggled into matching");
    }
    private static void Runtime()
    {
        var legacy=new ShipComponentSelection(Engine:"engine-vector",Sensor:"sensor-longrange",Shield:"shield-capacity");
        var modern=new ShipComponentSelection{EnginePart="drive-vector",SensorPart="scanner-longrange",ShieldPart="barrier-capacity"};
        ShipBody Make(ShipComponentSelection c)=>new("TEST",FleetLoadouts.ResolveFit(FleetLoadouts.Empty("escort") with{Components=c}).Definition,Faction.Blue);
        var a=Make(legacy);var b=Make(modern);var wa=new SimWorld();var wb=new SimWorld();wa.Add(a);wb.Add(b);
        a.Control=b.Control=new ShipControl{FlightAssist=false,Thrust=new Vector3(.25f,.1f,1)};
        for(int tick=0;tick<120;tick++){wa.Step();wb.Step();}
        Check(a.Position==b.Position&&a.Velocity==b.Velocity&&a.Velocity.Length()>0&&Near(a.Power.HeatMj,b.Power.HeatMj),"Actual engine thrust and power evolve identically to legacy ratings");
        foreach(var kind in new[]{ModuleKind.Thruster,ModuleKind.Sensor,ModuleKind.ShieldEmitter})
        {
            var body=Make(modern);body.DamageControl.Priority=RepairPriority.Hold;
            foreach(var module in body.Damage.Modules.Where(m=>m.Definition.Kind==kind).ToArray())body.Damage.Hurt(module,module.Health,0,0,1);
            Check(kind switch{ModuleKind.Thruster=>body.Damage.PropulsionFraction==0,ModuleKind.Sensor=>body.Damage.SensorFraction==0,
                _=>body.Damage.ShieldCapacity==0},"Assembly still fails when its physical modules are destroyed");
            body.Damage.Reset();
            Check(body.Damage.Modules.All(m=>!m.Destroyed)&&Near(body.Damage.ShieldCapacity,body.Definition.Shield.Capacity),"Recovery retains fitted assembly values");
        }
    }
}
