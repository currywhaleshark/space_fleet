using System.Text.Json;
using System.Text.Json.Nodes;
using SpaceFleet.Game;
using SpaceFleet.Sim;

static class FleetLoadoutChecks
{
    private static int checks;
    private static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
    private static void Reject(Action action,string why)
    {bool rejected=false;try{action();}catch(Exception e)when(e is InvalidDataException or ArgumentException or JsonException){rejected=true;}Check(rejected,why);}
    public static void Run()
    {
        var original=FleetLoadouts.Hulls.ToDictionary(h=>h.Id,h=>h.ToJson());
        foreach(var hull in FleetLoadouts.Hulls)
        {
            for(int preset=0;preset<3;preset++)
            {
                var squad=FleetLoadouts.Resolve(FleetLoadouts.AiPreset(hull.Id,preset));
                Check(squad.Leader.Id==hull.Id&&squad.Members.Count==hull.Squadron.Members.Count,"Every AI preset resolves with correct composition");
                Check(squad.Members.All(m=>m.Definition.Design==hull.Design),"AI presets preserve design");
                Check(preset==0||squad.Members.All(m=>m.Definition.InstalledEquipment.Length>0),"Nonempty AI presets actually install equipment");
            }
            var fit=FleetLoadouts.Default(hull.Id);var resolved=FleetLoadouts.Resolve(fit);
            Check(resolved.LoadoutId==FleetLoadouts.Resolve(fit).LoadoutId,"Squad identity is deterministic");
            Reject(()=>FleetLoadouts.Resolve(fit,new HashSet<string>()),"Ownership check rejects locked hulls");
            Reject(()=>FleetLoadouts.Resolve(new(fit.Members.Skip(1).ToArray())),"Wrong slot count rejected");
        }
        var profile=HangarProfile.Create("battleship");
        profile.SetEquipment("battleship",0,"utility-port","shield-capacity");
        profile.SetEquipment("earth_gun_cruiser",1,"utility-starboard","sensor-array");
        profile.SetSlot(1,"earth_gun_cruiser",1);profile.SetSlot(2,"earth_air_defense_cruiser",0);
        var selected=FleetLoadouts.Resolve(profile.Squadron());
        Check(selected.Members[1].Definition.Branch==ShipBranch.GunCruiser&&selected.Members[2].Definition.Branch==ShipBranch.AirDefenseCruiser,"Capital escorts accept mixed medium branches");
        Check(selected.Members[0].Definition.InstalledEquipment.Single().Equipment.Id=="shield-capacity","Flagship fit preserved");
        Check(profile.Hulls["earth_gun_cruiser"].Presets[0].Length==0,"Other presets remain empty");
        string before=HangarStorage.Serialize(profile);
        Reject(()=>profile.SetSlot(1,"interceptor",0),"Capital escort slot rejects interceptor");
        Reject(()=>profile.SetSlot(1,"mars_escort",0),"Slot rejects other design");
        Reject(()=>profile.SetSlot(1,"escort",3),"Preset out of range rejected");
        Reject(()=>profile.SetSlot(0,"earth_gun_cruiser",0),"Leader cannot be changed through incompatible squad shape");
        var medium=HangarProfile.Create("escort");
        Reject(()=>medium.SetSlot(0,"earth_gun_cruiser",0),"Same-sized leader also requires explicit hull selection");
        Reject(()=>profile.SetEquipment("battleship",0,"sensor","drone-hangar"),"Invalid equipment rejected");
        Check(HangarStorage.Serialize(profile)==before,"Rejected edits leave whole profile untouched");
        var restored=HangarStorage.Parse(before);
        Check(HangarStorage.Serialize(restored)==before,"All hulls, presets and slot references round trip");
        Check(FleetLoadouts.Resolve(restored.Squadron()).LoadoutId==selected.LoadoutId,"Save reload preserves exact gameplay values");
        foreach(Action<JsonObject> corrupt in new Action<JsonObject>[] {
            n=>n["version"]=99,n=>n["selectedHullId"]="missing",n=>n["selectedHullId"]=null,
            n=>n["hulls"]!["battleship"]!["presets"]=new JsonArray(),
            n=>n["hulls"]!["battleship"]!["presets"]![0]=new JsonArray((JsonNode?)null),
            n=>n["hulls"]!["battleship"]!["squadron"]![1]!["preset"]=-1,
            n=>n["hulls"]!["battleship"]!["squadron"]![1]=null,
            n=>n["hulls"]!["earth_gun_cruiser"]=null,
            n=>n["unknown"]=true})
        {var node=JsonNode.Parse(before)!.AsObject();corrupt(node);Reject(()=>HangarStorage.Parse(node.ToJsonString()),"Malformed save rejected before use");}
        // Inject independently of the current schema version so migration cannot disable this check.
        Reject(()=>HangarStorage.Parse(before.Insert(before.IndexOf('{')+1,"\"version\": 99,")),"Duplicate save fields rejected");
        Storage(profile);
        var seen=new HashSet<string>();bool equipped=false;
        for(int seed=1;seed<=4;seed++)
        {
            var match=Matchmaking.Create(seed,profile.Squadron());var repeat=Matchmaking.Create(seed,profile.Squadron());
            Check(match.Participants.Select(p=>(p.HullId,p.Loadout!.LoadoutId)).SequenceEqual(repeat.Participants.Select(p=>(p.HullId,p.Loadout!.LoadoutId))),"Same seed and player fit reproduce complete matchmaking loadouts");
            var blue=match.Participants.Where(p=>p.Team==Faction.Blue).Select(p=>(p.Hull.Design,p.Hull.MatchClass)).Order();
            var red=match.Participants.Where(p=>p.Team==Faction.Red).Select(p=>(p.Hull.Design,p.Hull.MatchClass)).Order();
            Check(blue.SequenceEqual(red),"Current matching retains equal class/design counts");
            var world=new SimWorld();BattleSetup.Spawn(world,new BattleConfig{Seed=seed,Match=match});
            foreach(var participant in match.Participants)
            {
                seen.Add(participant.HullId);equipped|=!participant.IsPlayer&&participant.Loadout!.Members.Any(m=>m.Definition.InstalledEquipment.Length>0);
                for(int i=0;i<participant.ShipCount;i++)
                {
                    var ship=world.Ships.Single(s=>s.Callsign==participant.Callsign(i));var fit=participant.Loadout!.Members[i];
                    Check(ship.LoadoutId==fit.LoadoutId&&ship.Definition==fit.Definition,"Spawn uses each member's full definition and immutable loadout ID");
                }
            }
            for(int i=0;i<480;i++)world.Step();
            Check(world.Ships.All(s=>s.Velocity.IsFinite()&&double.IsFinite(s.Position.Length())),"Mixed equipped squads simulate finitely");
        }
        Check(seen.Count>=10&&seen.Any(id=>id.Contains("_t"))&&equipped,"Random opponents include varied branches, tier variants and actual equipment");
        foreach(var hull in FleetLoadouts.Hulls)Check(hull.ToJson()==original[hull.Id],"Profiles never mutate the base/variant catalogs");
        Console.WriteLine($"PASS: {checks} squad loadout/preset/storage/matchmaking checks");
    }
    private static void Storage(HangarProfile profile)
    {
        string dir=Path.Combine(AppContext.BaseDirectory,"hangar-"+Guid.NewGuid().ToString("N"));string path=Path.Combine(dir,"hangar.json");
        try
        {
            var missing=HangarStorage.Load(path,"mars_escort");
            Check(missing.CanSave&&missing.Profile.SelectedHullId=="mars_escort","Missing new save imports legacy selection");
            HangarStorage.Save(path,profile);string first=File.ReadAllText(path);
            profile.SetSlot(2,"escort",2);HangarStorage.Save(path,profile);
            Check(File.ReadAllText(path+".bak")==first&&!File.Exists(path+".tmp"),"Atomic save retains previous valid backup");
            var loaded=HangarStorage.Load(path,"interceptor");
            Check(loaded.CanSave&&loaded.Profile.Hulls["battleship"].Squadron[2].Preset==2,"Persisted member selection reloads");
            File.WriteAllText(path,"{broken");var bad=HangarStorage.Load(path,"mars_interceptor");
            Check(!bad.CanSave&&bad.Message is not null&&File.ReadAllText(path)=="{broken"&&File.ReadAllText(path+".bak")==first,"Corrupt save preserved and autosave disabled");
        }
        finally
        {
            foreach(string file in new[]{path,path+".bak",path+".tmp"})if(File.Exists(file))File.Delete(file);
            if(Directory.Exists(dir))Directory.Delete(dir);
        }
    }
    public static void BattleSmoke()
    {
        foreach(var (seed,id,field) in new[]{(1,"earth_air_defense_cruiser",Battlefield.DeepSpace),(2,"mars_missile_cruiser",Battlefield.AsteroidCluster)})
        {
            var world=new SimWorld();var match=Matchmaking.Create(seed,FleetLoadouts.AiPreset(id,1));
            BattleSetup.Spawn(world,new BattleConfig{Seed=seed,FieldSeed=seed,Battlefield=field,Match=match,Objective=BattleObjective.Annihilation});
            while(world.Time<1501&&world.Rules!.Outcome is null)world.Step();
            Check(world.Rules!.Outcome is not null,"Equipped match reaches an outcome");
            Check(world.Ships.All(s=>s.Velocity.IsFinite()&&double.IsFinite(s.Position.Length())),"Equipped full battle remains finite");
            foreach(Faction side in Enum.GetValues<Faction>())Check(world.Log!.Side(side).Rails>0&&world.Log.Side(side).Missiles>0,"Both equipped teams engage");
            Console.WriteLine($"Equipped match: seed={seed}; field={field}; ships={world.Ships.Count}; outcome={world.Rules.Outcome}; destroyed={world.Ships.Count(s=>s.Damage.Destroyed)}; Blue rails/missiles={world.Log!.Side(Faction.Blue).Rails}/{world.Log.Side(Faction.Blue).Missiles}; Red rails/missiles={world.Log.Side(Faction.Red).Rails}/{world.Log.Side(Faction.Red).Missiles}");
        }
        Console.WriteLine($"PASS: {checks} equipped full battle smoke checks");
    }
}
