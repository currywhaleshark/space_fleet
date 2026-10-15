using SpaceFleet.Sim;

static class MatchmakingChecks
{
    private static int _checks;
    private static void Require(bool ok,string message) { if(!ok)throw new Exception(message);_checks++; }
    public static void RunBattles()
    {
        foreach(var (seed,field,objective) in new[]{(1,Battlefield.DeepSpace,BattleObjective.Annihilation),
            (2,Battlefield.AsteroidCluster,BattleObjective.Domination)})
        {
            var world=new SimWorld();
            BattleSetup.Spawn(world,new BattleConfig{Seed=seed,FieldSeed=seed,Battlefield=field,Objective=objective,
                Match=Matchmaking.Create(seed,"interceptor")});
            while(world.Time<1501 && world.Rules!.Outcome is null)world.Step();
            Require(world.Rules!.Outcome is not null,"Match reaches an outcome or timed decision");
            Require(world.Ships.All(s=>s.Velocity.IsFinite()&&double.IsFinite(s.Position.Length())),"Large match remains finite");
            foreach(Faction team in Enum.GetValues<Faction>())
                Require(world.Log!.Side(team).Rails>0 && world.Log.Side(team).Missiles>0,"Both teams reach weapon engagement");
            Console.WriteLine($"Match smoke: seed={seed}; field={field}; objective={objective}; ships={world.Ships.Count}; " +
                $"outcome={world.Rules.Outcome}; destroyed={world.Ships.Count(s=>s.Damage.Destroyed)}; " +
                $"Blue rails/missiles={world.Log!.Side(Faction.Blue).Rails}/{world.Log.Side(Faction.Blue).Missiles}; " +
                $"Red rails/missiles={world.Log.Side(Faction.Red).Rails}/{world.Log.Side(Faction.Red).Missiles}");
        }
        Console.WriteLine($"PASS: {_checks} full random match smoke checks");
    }
    public static void Run()
    {
        var mixes=new HashSet<string>();
        foreach(var hull in ShipDefinitions.All)
        for(int seed=0;seed<100;seed++)
        {
            var match=Matchmaking.Create(seed,hull.Id);
            Require(match.Participants.Count==20 && match.Participants.Count(p=>p.IsPlayer)==1,"Exactly twenty participants, one human");
            Require(match.Player.HullId==hull.Id && match.Player.Team==Faction.Blue && match.Player.Slot==1,"Chosen hull and ownership preserved");
            Require(match.Participants.SequenceEqual(Matchmaking.Create(seed,hull.Id).Participants),"Seed repeats lineup");
            var a=match.Participants.Where(p=>p.Team==Faction.Blue).ToArray();
            var b=match.Participants.Where(p=>p.Team==Faction.Red).ToArray();
            Require(a.Length==10 && b.Length==10 && a.Select(p=>p.HullId).Order().SequenceEqual(b.Select(p=>p.HullId).Order()),"Both teams have matched class/design counts");
            foreach(HullKind kind in Enum.GetValues<HullKind>())Require(a.Count(p=>p.Kind==kind) is >=2 and <=4,"Class counts vary within two to four");
            Require(match.Participants.Sum(p=>p.ShipCount) is >=76 and <=84,"Existing templates fit 76..84 ships");
            mixes.Add(string.Join(',',Enum.GetValues<HullKind>().Select(k=>a.Count(p=>p.Kind==k))));
        }
        Require(mixes.Count>=5,"Composition varies between matches");
        foreach(BattleObjective objective in Enum.GetValues<BattleObjective>())
        {
            var match=Matchmaking.Create(17,"mars_interceptor");
            var cfg=new BattleConfig{Seed=17,Match=match,Objective=objective};
            var world=new SimWorld();var roster=BattleSetup.Spawn(world,cfg);
            var repeat=new SimWorld();BattleSetup.Spawn(repeat,cfg);
            Require(world.Squadrons.Count==20 && world.Ships.Count==match.Participants.Sum(p=>p.ShipCount),"Spawn realizes every squad");
            Require(world.Ships.Select(s=>s.Callsign).Distinct().Count()==world.Ships.Count,"Callsigns unique");
            Require(world.Ships.Zip(repeat.Ships).All(p=>p.First.Position==p.Second.Position&&p.First.Orientation==p.Second.Orientation),"Spawn poses reproducible");
            Require(world.Rules!.Initial(Faction.Blue)==world.Rules.Initial(Faction.Red) && world.Rules.Evaluate(0) is null,"Equal initial strength and no premature result");
            Require(roster.BlueFlagship.Class.Kind==HullKind.Battleship&&roster.RedFlagship.Faction==Faction.Red,"Each team has a flagship");
            foreach(var p in match.Participants)
            {
                var squad=world.Squadrons.Single(s=>s.Name==p.SquadronName);
                Require(squad.Members.Count==p.ShipCount && squad.Role==p.Role && squad.Faction==p.Team,"Preserved squad role and size");
                Require(squad.Members[0].Definition.Id==p.HullId && squad.Members.All(s=>s.Definition.Design==p.Hull.Design),"Selected hull and squad design loaded by ID");
                Require(squad.Members.All(s=>world.BrainOf(s) is not null),"Every AI ship starts with a brain");
                if(squad.Role==SquadronRole.BattleGroup)Require(squad.SupportGroup==squad,"Each battle group escorts its own capital");
                if(squad.Role==SquadronRole.EscortSquadron)Require(squad.SupportGroup is {Role:SquadronRole.BattleGroup} && squad.SupportGroup.Faction==squad.Faction,"Escort support stays within own team");
            }
            for(int i=0;i<world.Ships.Count;i++)for(int j=i+1;j<world.Ships.Count;j++)
                Require((world.Ships[i].Position-world.Ships[j].Position).Length()>60,"Spawn slots do not overlap");
            for(int i=0;i<600;i++)world.Step();
            Require(world.Rules.Outcome is null && world.Ships.All(s=>!s.Damage.Destroyed),"Opening maneuvers do not destroy crowded squads");
            Require(world.Ships.Any(s=>s.Velocity.Length()>1),"AI begins useful movement");
        }
        var legacy=new SimWorld();BattleSetup.Spawn(legacy,new BattleConfig{Seed=1});
        Require(legacy.Ships.Count==24 && legacy.Ships.Any(s=>s.Callsign=="BB-01"),"Canonical balance fixture preserved");
        Console.WriteLine($"PASS: {_checks} random matchmaking checks");
    }
}
