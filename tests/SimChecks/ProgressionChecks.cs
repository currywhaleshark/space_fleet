using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Game;
using SpaceFleet.Sim;

static class ProgressionChecks
{
    private static int checks;
    private static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
    private static void Reject(Action action,string why)
    {bool rejected=false;try{action();}catch(Exception e)when(e is InvalidDataException or ArgumentException or JsonException){rejected=true;}Check(rejected,why);}
    public static void Run()
    {
        var tree=ResearchTree.Catalog;
        Check(tree.MaxTier==5&&tree.Playable.Count()==34&&tree.Nodes.Count()==34,"Two data-driven trees through T5, thirty-four implemented hulls");
        foreach(var family in Enum.GetValues<DesignFamily>())
        {
            var node=tree.Root(family);var starter=ShipDefinitions.ById(node.HullId!);var old=ShipDefinitions.For(HullKind.Escort,family);
            Check(node.Tier==1&&node.Cost==0&&starter.Railgun!.Mounts!.Length==1&&starter.Bays.Length==1,"Starter has one gun mount and one bay");
            Check(Math.Abs(starter.Flight.Length-old.Flight.Length*.8)<.001&&starter.ModelId==starter.Id,"Independent starter keeps its nominal scale");
            Check(Math.Abs(tree.Score(FleetLoadouts.Empty(old.Id))/tree.Score(FleetLoadouts.Empty(starter.Id))-tree.Settings.TierMultiplier)<.00001,"A tier step applies its power multiplier only once");
            foreach(var module in starter.Modules)
            {
                var previous=old.Modules.Single(m=>m.Id==module.Id);
                Check(module.HalfSize.DistanceTo(previous.HalfSize*.8f)<.001,"Authored starter preserves module hit-volume size");
                bool relocated=module.Kind==ModuleKind.Sensor||module.Id.StartsWith("radiator-");
                Check(relocated?module.Center.DistanceTo(previous.Center*.8f)>.01:module.Center.DistanceTo(previous.Center*.8f)<.001,
                    "Only the exposed sensor and radiators move with the new starter silhouette");
            }
        }
        foreach(var node in tree.Playable)
        {
            var original=ShipDefinitions.ById(node.HullId!);string before=original.ToJson();
            var fitted=tree.Resolve(node.HullId!,node.Upgrades.Select(u=>u.Id).ToArray(),Array.Empty<EquipmentMount>()).Definition;
            Check(fitted.StructureHitPoints>original.StructureHitPoints&&fitted.Flight.ForwardAccel>original.Flight.ForwardAccel,"Permanent upgrades alter full ship definition");
            Check((original.Battery!.Weapon.Laser is { } laser ? fitted.Battery!.Weapon.Laser!.EnergyPerSecond>laser.EnergyPerSecond : fitted.Railgun!.ReloadSeconds<original.Railgun!.ReloadSeconds)&&fitted.Power.OutputMw>original.Power.OutputMw,"Weapon/reactor upgrades improve the selected weapon system");
            Check(original.ToJson()==before&&fitted.ModelId==original.ModelId,"Research does not mutate base data/model");
            Reject(()=>tree.Resolve(node.HullId!,new[]{"free-power"},Array.Empty<EquipmentMount>()),"Unknown upgrade rejects");
        }
        var earth=tree.Trees.Single(t=>t.Design==DesignFamily.Earth);var parent=earth.Nodes.First(n=>n.Tier==5);
        var future=parent with{Id="test-tier6",Name="Test tier six",Tier=6,ParentId=parent.Id,HullId=null,Cost=5000,Upgrades=Array.Empty<ResearchUpgrade>()};
        var extended=new ResearchCatalog(tree.Trees.Select(t=>t.Design==DesignFamily.Earth?t with{Nodes=t.Nodes.Append(future).ToArray()}:t),tree.Settings);
        Check(extended.MaxTier==6,"Adding a data node extends maximum tier without code changes");
        Reject(()=>new ResearchCatalog(tree.Trees.Select(t=>t.Design==DesignFamily.Earth?t with{Nodes=t.Nodes.Append(future with{ParentId=future.Id}).ToArray()}:t),tree.Settings),"Cyclic research rejects");
        var profile=HangarProfile.Campaign();var progress=profile.Research!;
        Check(profile.Owned.Count==2&&tree.ByHull(profile.SelectedHullId).Tier==1,"New profile starts with both free T1 escorts");
        Reject(()=>progress.BuyHull("battleship"),"Cannot skip research prerequisites");
        Reject(()=>progress.BuyHull("escort"),"Cannot buy without points");
        Check(progress.Reward("short",DesignFamily.Earth,1,true,90000,50)==0,"No reward for an instant fixture");
        int reward=progress.Reward("completed-1",DesignFamily.Earth,300,true,10000,0);
        Check(reward==260&&progress.Points[DesignFamily.Mars]==0,"Reward goes to launch design with bounded contribution");
        Check(progress.Reward("completed-1",DesignFamily.Earth,300,true,10000,0)==0&&progress.Points[DesignFamily.Earth]==260,"Same battle cannot pay twice");
        Check(progress.BuyHull("escort")&&progress.Points[DesignFamily.Earth]==60,"Research deducts cost and unlocks the hull");
        Check(!progress.BuyHull("escort")&&progress.Points[DesignFamily.Earth]==60,"Repeated purchase is free and idempotent");
        progress.Reward("completed-2",DesignFamily.Earth,300,false,0,0);
        Check(progress.BuyUpgrade("escort","drive")&&progress.Points[DesignFamily.Earth]==40,"Permanent part research consumes faction points");
        Check(!progress.BuyUpgrade("escort","drive"),"Upgrade cannot be charged twice");
        Reject(()=>progress.BuyUpgrade("mars_escort","drive"),"Cannot research parts of a locked hull");
        profile.SelectedHullId="escort";
        Check(FleetLoadouts.Resolve(profile.Squadron()).Leader.Flight.ForwardAccel>ShipDefinitions.ById("escort").Flight.ForwardAccel,"Preset references include owned permanent upgrades");
        string saved=HangarStorage.Serialize(profile);var roundtrip=HangarStorage.Parse(saved);
        Check(HangarStorage.Serialize(roundtrip)==saved,"Progress, receipts, fits and squad slots round trip together");
        Check(roundtrip.Research!.Reward("completed-2",DesignFamily.Earth,300,true,90000,50)==0,"Reloading does not duplicate a recorded reward");
        foreach(Action<JsonObject> mutate in new Action<JsonObject>[] {
            n=>n["version"]=99,n=>n["research"]!["points"]!["Earth"]=-1,n=>n["research"]!["unlocked"]=null,
            n=>n["research"]!["upgrades"]!["escort"]=new JsonArray("unknown"),n=>n["selectedHullId"]="battleship"})
        {var bad=JsonNode.Parse(saved)!.AsObject();mutate(bad);Reject(()=>HangarStorage.Parse(bad.ToJsonString()),"Malformed campaign save rejects");}
        Storage();Matching();StarterGeometry();
        Console.WriteLine($"PASS: {checks} research/tree/reward/migration/tier matching checks");
    }
    private static void StarterGeometry()
    {
        foreach(string id in new[]{"earth_starter_escort","mars_starter_escort"})
        {
            var d=ShipDefinitions.ById(id);var orientation=new Quaternion(new Vector3(.3f,1,.2f).Normalized(),.67f);
            var position=new Vec3d(1e9,-2e9,3e9);
            foreach(var module in d.Modules)
            {
                var ship=new ShipBody("T1-RAY",d,Faction.Blue);ship.Place(position,orientation);
                var outward=module.Kind==ModuleKind.Thruster?Vector3.Back:
                    module.Id.StartsWith("radiator-")&&d.Design==DesignFamily.Mars?Vector3.Right*Mathf.Sign(module.Center.X):Vector3.Up;
                var direction=orientation*outward;
                var hit=DamageRay.ApplyAtPose(ship,position+Vec3d.From(orientation*(module.Center+outward*1000)),-direction,
                    new DamagePacket(1000000,1000000,1000),0,1,ship.Position,ship.Orientation,shieldApplies:false);
                Check(hit.HullHit&&hit.Modules.Any(m=>m.Id==module.Id),$"{id}: authored hull exposes actual {module.Id} at rotated double coordinates");
            }
            var world=new SimWorld();var shooter=world.Add(new ShipBody("T1-FIRE",d,Faction.Blue));
            shooter.Place(position,orientation);shooter.Control=new ShipControl{FlightAssist=false};
            var shot=world.FireRailguns(shooter,shooter.Forward);
            Check(shot.Fired&&shot.Shots==2&&world.Projectiles.Count==2&&shooter.Railguns.Single().Rounds==478,"Starter twin barrel fires two rounds from its one real turret");
            var mount=d.Railgun!.Mounts!.Single();
            foreach(var projectile in world.Projectiles)
            {
                var expected=position+Vec3d.From(orientation*(mount.Pivot+mount.Trunnion+mount.Muzzles[projectile.Barrel]));
                Check((projectile.Position-expected).Length()<.001,"Actual round starts at the authored rotating muzzle");
            }
            var target=world.Add(new ShipBody("T1-TARGET",CruiserTrials.PassiveTarget(),Faction.Red));
            target.Place(position+Vec3d.From(shooter.Forward*3000),Quaternion.Identity);world.Sensors.Update(world.Ships,0,true);
            Check(world.LaunchMissile(shooter,target).Fired&&shooter.Ordnance.Missiles==11,"Starter keeps its independent twelve-round missile supply");
            Check((world.Missiles.Last().Position-position-Vec3d.From(orientation*d.Missiles!.LaunchPoint)).Length()<.001,
                "Missile leaves the authored launcher on a rotated starter");
        }
    }
    private static void Storage()
    {
        string dir=Path.Combine(AppContext.BaseDirectory,"progress-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);string path=Path.Combine(dir,"hangar.json");
        try
        {
            var old=HangarProfile.Create("battleship");old.Version=1;old.Research=null;
            foreach(string id in old.Hulls.Keys.Where(id=>id.Contains("starter")||id.Contains("_t")).ToArray())old.Hulls.Remove(id);
            old.SetEquipment("battleship",1,"utility-port","shield-capacity");old.SetSlot(0,"battleship",1);
            string original=HangarStorage.Serialize(old);File.WriteAllText(path,original);
            var loaded=HangarStorage.Load(path,null);
            Check(loaded.CanSave&&loaded.Profile.Version==3&&loaded.Profile.Owned.Count==12,"Old trial hangar migrates its owned hulls plus starters");
            Check(loaded.Profile.Hulls["battleship"].Presets[1].Single().EquipmentId=="shield-capacity"&&loaded.Profile.Hulls["battleship"].Squadron[0].Preset==1,"Migration keeps equipment/preset references");
            HangarStorage.Save(path,loaded.Profile);
            Check(File.ReadAllText(path+".bak")==original,"First migrated save retains exact v1 backup");
            Check(HangarStorage.Load(path,null).Profile.Research!.Unlocked.Length==12,"Migration is not repeated on v2");
            File.Delete(path);var legacy=HangarStorage.Load(path,"mars_battleship").Profile;
            Check(legacy.SelectedHullId=="mars_battleship"&&legacy.Owned.Contains("mars_escort")&&legacy.Owned.Count<12,"Legacy selection retains chosen hull and prerequisites without unlocking everything");
            Check(HangarStorage.Load(path,null).Profile.Owned.Count==2,"No legacy selection creates a fresh campaign");
            File.WriteAllText(path,"broken");var bad=HangarStorage.Load(path,null);
            Check(!bad.CanSave&&File.ReadAllText(path)=="broken","Corrupt campaign stays untouched with autosave disabled");
        }
        finally{foreach(string file in new[]{path,path+".bak",path+".tmp"})if(File.Exists(file))File.Delete(file);Directory.Delete(dir);}
    }
    private static void Matching()
    {
        var tree=ResearchTree.Catalog;var profile=HangarProfile.Create();var upgrades=tree.Playable.ToDictionary(n=>n.HullId!,n=>(IReadOnlySet<string>)n.Upgrades.Select(u=>u.Id).ToHashSet());
        foreach(string hull in new[]{"earth_starter_escort","mars_starter_escort","interceptor","escort","earth_gun_cruiser","battleship"})
        for(int seed=1;seed<=8;seed++)
        {
            var fit=FleetLoadouts.Default(hull);fit=new(fit.Members.Select(m=>m with{Upgrades=new[]{"drive"}}).ToArray());
            var match=Matchmaking.CreateTiered(seed,fit,profile.Owned,upgrades);
            Check(match.Participants.Count==20&&match.Player.Loadout!.Leader.Id==hull,"Twenty participants retain the selected player hull");
            var a=match.Participants.Where(p=>p.Team==Faction.Blue).ToArray();var b=match.Participants.Where(p=>p.Team==Faction.Red).ToArray();
            Check(a.Select(p=>p.Kind).Order().SequenceEqual(b.Select(p=>p.Kind).Order()),"Participant weight classes match");
            IEnumerable<(MatchWeightClass,int)> Tiers(MatchParticipant[] team)=>team.SelectMany(p=>p.Loadout!.Members.Select((m,i)=>(m.Definition.MatchClass,p.MemberTiers![i]))).Order();
            Check(Tiers(a).SequenceEqual(Tiers(b)),"All member class/tier counts match, including mixed escort slots");
            Check(Math.Abs(a.Sum(p=>p.MatchPoints)-b.Sum(p=>p.MatchPoints))<.00001,"Full squad research-weighted scores match");
            Check(match.Participants.SelectMany(p=>p.MemberTiers!).All(t=>t>=match.LowTier&&t<=match.HighTier),"Every actual ship lies inside the global bracket");
            if(tree.ByHull(hull).Tier==1)Check(match.Participants.All(p=>p.Tier==1&&p.Kind==HullKind.Escort),"T1 introductory match contains only starter escorts");
            if(seed==1)
            {
                var world=new SimWorld();var roster=BattleSetup.Spawn(world,new BattleConfig{Seed=seed,Match=match,Objective=BattleObjective.Flagship});
                Check(roster.BlueFlagship is not null&&roster.RedFlagship is not null&&world.Rules!.Flagship(Faction.Blue) is not null,"Low-tier matches have valid designated flagships without a battleship");
                for(int i=0;i<240;i++)world.Step();
                Check(world.Ships.All(s=>s.Velocity.IsFinite()),"All tiers and mixed roles simulate");
            }
        }
        var low=FleetLoadouts.Default("battleship");var high=new SquadronLoadout(new[]{FleetLoadouts.Empty("battleship"),FleetLoadouts.Empty("earth_gun_cruiser"),FleetLoadouts.Empty("earth_air_defense_cruiser")});
        Check(high.Members.Sum(tree.Score)>low.Members.Sum(tree.Score),"Higher-tier escorts raise full squad score with unchanged flagship");
        var invalid=new SquadronLoadout(new[]{FleetLoadouts.Empty("battleship"),FleetLoadouts.Empty("earth_starter_escort"),FleetLoadouts.Empty("earth_starter_escort")});
        Reject(()=>Matchmaking.CreateTiered(1,invalid,profile.Owned,upgrades),"Overwide player squad rejects instead of ignoring low-tier escorts");
        var fresh=HangarProfile.Campaign();Reject(()=>Matchmaking.CreateTiered(1,low,fresh.Owned,fresh.Research!.UpgradeSets()),"Locked ships cannot launch");
        Reject(()=>Matchmaking.CreateTiered(1,new(fresh.Squadron().Members.Select(m=>m with{Upgrades=new[]{"drive"}}).ToArray()),fresh.Owned,fresh.Research!.UpgradeSets()),"Unearned upgrades cannot launch");
    }
    public static void Battles()
    {
        var profile=HangarProfile.Campaign();var world=new SimWorld();
        var match=Matchmaking.CreateTiered(1,profile.Squadron(),profile.Owned,profile.Research!.UpgradeSets());
        BattleSetup.Spawn(world,new BattleConfig{Seed=1,Match=match,Objective=BattleObjective.Annihilation});
        while(world.Rules!.Outcome is null&&world.Time<1501)world.Step();
        Check(world.Rules!.Outcome is not null,"T1 fleet reaches a result");
        foreach(Faction f in Enum.GetValues<Faction>())Check(world.Log!.Side(f).Rails>0&&world.Log.Side(f).Missiles>0,"T1 fleets reach both weapon engagements");
        Check(world.Ships.All(s=>s.Velocity.IsFinite()&&double.IsFinite(s.Position.Length())),"Starter battle stays finite");
        Console.WriteLine($"T1 match: ships={world.Ships.Count}; outcome={world.Rules.Outcome}; destroyed={world.Ships.Count(s=>s.Damage.Destroyed)}");
        Console.WriteLine($"PASS: {checks} T1 full battle checks");
    }
}
