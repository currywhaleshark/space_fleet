using Godot;
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
        Deployments();
        Console.WriteLine($"PASS: {_checks} random matchmaking checks");
    }
    // Opening geometry (Deployment): seeded course x height for matches, role ranks, screens beside battleships.
    private static void Deployments()
    {
        Require(Deployment.Resolve(new BattleConfig{Seed=3})==(Engagement.HeadOn,Elevation.Level),"Canonical fixture stays head-on and level");
        var drawn=Enumerable.Range(1,60).Select(seed=>Deployment.Resolve(new BattleConfig{Seed=seed,Match=Matchmaking.Create(seed,"escort")})).ToArray();
        Require(Deployment.Modes.All(m=>Deployment.Heights.All(h=>drawn.Count(d=>d==(m,h))>=4)),"Matches draw all six course/height openings");
        Require(Deployment.Resolve(new BattleConfig{Seed=9,Mirror=true,Match=Matchmaking.Create(9,"escort")})==drawn[8],"Mirror keeps the seed's opening");
        var owned=FleetLoadouts.Hulls.Select(h=>h.Id).ToHashSet();var research=new Dictionary<string,IReadOnlySet<string>>();
        const double R=150_000;
        foreach(string anchor in new[]{"earth_gun_cruiser","battleship_t5","earth_starter_escort"})
        foreach(Engagement course in Deployment.Modes)
        foreach(Elevation height in Deployment.Heights)
        foreach(bool mirror in new[]{false,true})
        {
            var match=Matchmaking.CreateTiered(5,FleetLoadouts.Default(anchor),owned,research);
            var world=new SimWorld();var roster=BattleSetup.Spawn(world,new BattleConfig{Seed=5,Mirror=mirror,Match=match,Engagement=course,Elevation=height});
            string label=$"{anchor}/{course}/{height}/{(mirror?"mirror":"normal")}";
            Require(roster.Engagement==course&&roster.Elevation==height,label+": roster reports its opening");
            Vec3d Mean(IEnumerable<ShipBody> ships)=>ships.Aggregate(Vec3d.Zero,(a,s)=>a+s.Position)*(1.0/ships.Count());
            Vec3d blue=Mean(world.Ships.Where(s=>s.Faction==Faction.Blue)),red=Mean(world.Ships.Where(s=>s.Faction==Faction.Red));
            bool lines=world.Ships.Any(s=>s.Class.Kind==HullKind.Battleship);
            Vec3d Line(Faction f)=>Mean(world.Ships.Where(s=>s.Faction==f&&s.Class.Kind==HullKind.Battleship));
            Vec3d separation=lines?Line(Faction.Red)-Line(Faction.Blue):red-blue;double slack=lines?.1:.25;
            Require(separation.Length()>R*(1-slack)&&separation.Length()<R*(1+slack),label+": battle lines open at the requested distance");
            foreach(Faction team in Enum.GetValues<Faction>())
            {
                Vec3d enemy=team==Faction.Blue?red:blue;
                double Depth(SquadronRole role)=>world.Squadrons.Where(q=>q.Faction==team&&q.Role==role).SelectMany(q=>q.Members)
                    .Select(s=>(s.Position-enemy).Length()).DefaultIfEmpty(double.NaN).Average();
                double bb=Depth(SquadronRole.BattleGroup),wing=Depth(SquadronRole.InterceptorWing),gun=Depth(SquadronRole.GunCruiserSquadron);
                if(!double.IsNaN(bb)&&!double.IsNaN(wing))Require(bb>wing+15_000,label+": battleships open behind the interceptor wings");
                if(!double.IsNaN(bb)&&!double.IsNaN(gun))Require(bb>gun+8_000,label+": battleships open behind the gun cruisers");
                foreach(var screen in world.Squadrons.Where(q=>q.Faction==team&&q.Role==SquadronRole.AirDefenseSquadron))
                    Require(world.Squadrons.Where(q=>q.Faction==team&&q.Role==SquadronRole.BattleGroup)
                        .Any(g=>(g.Leader!.Position-screen.Leader!.Position).Length()<12_000),label+": air-defence screen opens beside a battleship");
                // Height: level fleets stay upright; overhead fleets each have the other above their own heads.
                Vector3 up=world.Ships.First(s=>s.Faction==team).Orientation*Vector3.Up;
                if(height==Elevation.Level)Require(up.Y>.98f,label+": level fleets fly upright");
                else Require(Math.Abs(up.Y)>.98f&&(enemy-(team==Faction.Blue?blue:red)).ToVector3().Dot(up)>R*.25,label+": the other fleet is over this fleet's head");
            }
            Vector3 Forward(Faction f)=>world.Ships.First(s=>s.Faction==f).Orientation*Vector3.Forward;
            float alignment=Forward(Faction.Blue).Dot(Forward(Faction.Red));
            Vector3 toRed=separation.ToVector3().Normalized();
            switch(course)
            {
                case Engagement.HeadOn:Require(alignment<-.99f,label+": head-on courses are reversed");break;
                case Engagement.Parallel:Require(alignment>.99f,label+": parallel courses match");break;
                case Engagement.Opposite:Require(alignment<-.99f&&Forward(Faction.Blue).Dot(toRed)>.7f,label+": opposite courses close to pass");break;
            }
            if(height==Elevation.Level)
            {
                Require(Math.Abs(separation.Y)<10_000,label+": level opening");
                if(course==Engagement.HeadOn)Require(Forward(Faction.Blue).Dot(toRed)>.98f,label+": head-on fleets face each other");
                if(course==Engagement.Parallel)Require(Math.Abs(Forward(Faction.Blue).Dot(toRed))<.15f,label+": parallel fleets sail abeam");
            }
            else
            {
                Require(Math.Abs(separation.Y)>R*(lines?.4:.25),label+": one fleet opens above the other");
                if(course!=Engagement.Opposite)Require(Math.Abs(separation.Y)>R*(lines?.9:.75),label+": head-on and parallel overhead fleets open straight above each other");
            }
            for(int i=0;i<world.Ships.Count;i++)for(int j=i+1;j<world.Ships.Count;j++)
                Require((world.Ships[i].Position-world.Ships[j].Position).Length()>60,label+": spawn slots do not overlap");
            for(int i=0;i<240;i++)world.Step();
            Require(world.Ships.All(s=>!s.Damage.Destroyed&&s.Velocity.IsFinite()),label+": opening seconds stay stable");
        }
        var fixture=new SimWorld();BattleSetup.Spawn(fixture,new BattleConfig{Seed=1,Engagement=Engagement.Opposite,Elevation=Elevation.Overhead});
        Require(fixture.Ships.Count==24&&Vector3.Right.Dot(fixture.Ships.First(s=>s.Faction==Faction.Blue).Orientation*Vector3.Forward)>.98f
            &&(fixture.Ships.First(s=>s.Faction==Faction.Red).Orientation*Vector3.Up).Y<-.98f,
            "Explicit opening also applies to the canonical fixture");
    }
}
