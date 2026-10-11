using Godot;
using SpaceFleet.Sim;

static class NavigationChecks
{
    public static void Trace()
    {
        foreach(bool mirror in new[]{false,true})
        {
            var world=new SimWorld();BattleSetup.Spawn(world,new(){Seed=5,Mirror=mirror,Objective=BattleObjective.Domination});
            int count=0;
            while(world.Time<1000 && world.Rules!.Outcome is null)
            {
                world.Step();
                if(world.Log!.Environment.StuckDetections==count)continue;count=world.Log.Environment.StuckDetections;
                var e=world.Log.Environment.Events.Last(x=>x.Kind=="회피 갇힘");var ship=world.Ships.First(s=>s.Callsign==e.Source);var brain=world.BrainOf(ship)!;
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{mirror,time=world.Time,ship=ship.Callsign,position=ship.Position.ToString(),propulsion=ship.Damage.PropulsionFraction,
                    velocity=ship.Velocity.ToString(),desired=brain.DesiredVelocity.ToString(),order=brain.Order.Kind.ToString(),goal=brain.NavigationGoal?.ToString(),waypoint=brain.NavigationWaypoint?.ToString(),
                    active=brain.NavigationActive,obstacle=e.Obstacle,pathPlans=world.Log.Environment.PathPlans,failures=world.Log.Environment.PathFailures,
                    modules=ship.Damage.Modules.Where(m=>m.Definition.Kind is ModuleKind.Thruster or ModuleKind.ManeuverThruster).Select(m=>new{id=m.Definition.Id,hp=m.Health})}));
            }
        }
    }
    public static void Run()
    {
        int checks=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
        foreach(double shift in new[]{0.0,1e12})
        {
            Vec3d center=new(shift,-shift,shift);var walls=new ObstacleSet();
            // A U-shaped volume: direct entry is blocked; the exit is behind the goal.
            for(int level=-2;level<=2;level++)
            {
                walls.Add(new("Front"+level,center,Quaternion.Identity,ObstaclePart.Capsule(new(-6000,level*2500,0),new(6000,level*2500,0),1500)));
                foreach(int side in new[]{-1,1})walls.Add(new("Side"+side+level,center,Quaternion.Identity,ObstaclePart.Capsule(new(side*6000,level*2500,0),new(side*6000,level*2500,-6000),1500)));
            }
            var nav=new TerrainNavigation(walls,center,new(16000,16000,16000));
            foreach(double radius in new[]{90.0,350,850})
            {
                Vec3d start=center+new Vec3d(0,0,9000),goal=center+new Vec3d(0,0,-3500);
                Check(nav.FindPath(start,goal,radius,out var route),"Concave rock group has an escape route");
                Check(route.Points.Count>1 && route.Expanded>0,"Blocked route invokes A* and retains a corner");
                Vec3d previous=start;
                foreach(var p in route.Points){Check(!walls.BlocksSegment(previous,p,radius),"Every smoothed waypoint segment clears the inflated hull");previous=p;}
                Check((previous-goal).Length()<.01,"Path ends at the requested free destination");
                Check(nav.FindPath(start,goal,radius,out var again) && route.Points.SequenceEqual(again.Points),"A* and smoothing are deterministic");
                int searches=nav.Searches;
                Check(nav.FindPath(center+new Vec3d(12000,0,9000),center+new Vec3d(12000,0,-9000),radius,out var direct)
                    && direct.Expanded==0 && searches==nav.Searches,"Open direct route never invokes A*");
                var free=nav.NearestFree(center,radius);Check(!walls.ClosestApproach(free,radius,out _),"Embedded formation destination is projected into free space");
            }
            // Closed shell; no diagonal corner cutting or wall tunnelling.
            var closed=new ObstacleSet();
            for(int axis=0;axis<3;axis++)foreach(int sign in new[]{-1,1})
            for(int u=-2;u<=2;u++)for(int v=-2;v<=2;v++)
            {
                Vec3d p=axis==0?new(sign*4000,u*2000,v*2000):axis==1?new(u*2000,sign*4000,v*2000):new(u*2000,v*2000,sign*4000);
                closed.Add(new("Shell",center+p,Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,1500)));
            }
            var sealedNav=new TerrainNavigation(closed,center,new(12000,12000,12000));
            Check(!sealedNav.FindPath(center+new Vec3d(10000,0,0),center,100,out _),"Sealed goal reports no path");
        }
        var empty=new SimWorld();BattleSetup.Spawn(empty,new());
        Check(empty.Navigation is null,"Empty deep space allocates no navigation grid");
        foreach(int field in new[]{1,2,3})
        {
            var world=new SimWorld();BattleSetup.Spawn(world,new(){Battlefield=Battlefield.AsteroidCluster,FieldSeed=field,Objective=BattleObjective.Domination});
            Check(world.Navigation is not null,"Asteroid field owns a prepared navigation grid");
            foreach(Vec3d start in new[]{Vec3d.Zero,new Vec3d(0,0,-150000)})
            {
                Check(world.Navigation!.FindPath(start,world.Asteroids!.Center,world.NavigationRadius(HullKind.Battleship),out _),"Both battleship spawns connect to field centre");
                foreach(var zone in world.Rules!.CaptureZones)
                {
                    Vec3d offset=start-zone.Position;var goal=zone.Position+offset*(SimWorld.HoldClearance/offset.Length());
                    Check(world.Navigation.FindPath(start,goal,world.NavigationRadius(HullKind.Battleship),out _),"Both spawns connect to every capture holding shell");
                }
            }
            Console.WriteLine($"Field {field}: {world.Asteroids!.Rocks.Count} rocks, {world.Asteroids.Regenerations} deterministic regeneration(s)");
        }
        foreach(var kind in new[]{HullKind.Battleship,HullKind.Escort,HullKind.Interceptor})
        {
            var world=new SimWorld();var ship=world.Add(new ShipBody("NAV",ShipDefinitions.For(kind),Faction.Blue));
            ship.Place(new(-9000,0,-75000),Quaternion.Identity);
            world.Obstacles.Add(new("Crossing rock",new(0,0,-75000),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,2200)));
            world.PrepareNavigation(new());world.Log=new BattleLog(world);
            Vec3d goal=new(9000,0,-75000);var brain=world.AttachBrain(ship,ShipOrder.HoldAt(goal));
            double closest=double.PositiveInfinity;
            for(int i=0;i<240*60;i++){world.Step();closest=Math.Min(closest,(ship.Position-goal).Length());}
            Console.WriteLine($"Navigation {kind}: nearest {closest:F1}m, plans {world.Log.Environment.PathPlans}, collisions {world.Log.Environment.ShipCollisions}");
            Check(closest<750 && world.Log.Environment.PathPlans>0,"AI actually follows the path to its hold destination");
            Check(!ship.Damage.Destroyed && world.Log.Environment.ShipCollisions==0,"AI uses physical acceleration without hitting the rock");
        }
        var formation=new SimWorld();
        var lead=formation.Add(new ShipBody("LEADER",ShipClass.Battleship,Faction.Blue));
        var wing=formation.Add(new ShipBody("FOLLOWER",ShipClass.Escort,Faction.Blue));
        lead.Place(new(0,0,-71000),Quaternion.Identity);wing.Place(new(-3000,0,-71000),Quaternion.Identity);
        formation.AddSquadron("Test",Faction.Blue,SquadronRole.BattleGroup,new[]{lead,wing});
        formation.Obstacles.Add(new("NARROW",new(1500,0,-73500),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,1500)));
        formation.PrepareNavigation(new());var follower=formation.AttachBrain(wing,ShipOrder.EscortOf(lead));follower.FormationOffset=new(2000,0,-1000);
        for(int i=0;i<12;i++)formation.ThinkTerrainCheck(follower);
        Check(follower.FormationCompression<.2f && follower.NavigationGoal is not null,"Formation contracts near terrain");
        Check(!formation.Obstacles.ClosestApproach(follower.NavigationGoal!.Value,formation.NavigationRadius(HullKind.Escort),out _),"Compressed formation slot is outside terrain");
        lead.Place(new(25000,0,-71000),Quaternion.Identity);
        for(int i=0;i<12;i++)formation.ThinkTerrainCheck(follower);
        Check(follower.FormationCompression==1,"Formation spacing restores in open space");
        var intelligence=new SimWorld();var observer=intelligence.Add(new ShipBody("NAV-OBSERVER",ShipClass.Escort,Faction.Blue));
        var enemy=intelligence.Add(new ShipBody("NAV-TARGET",ShipClass.Escort,Faction.Red));
        observer.Place(new(0,0,-65000),Quaternion.Identity);enemy.Place(new(0,0,-90000),Quaternion.Identity);
        intelligence.Obstacles.Add(new("OFFSET",new(10000,0,-75000),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,3000)));
        intelligence.PrepareNavigation(new());var attacker=intelligence.AttachBrain(observer,ShipOrder.AttackOn(enemy));
        intelligence.ThinkTerrainCheck(attacker);
        Check(attacker.NavigationGoal is null,"Unobserved enemy supplies no navigation destination");
        intelligence.Sensors.Update(intelligence.Ships,0,force:true);intelligence.ThinkTerrainCheck(attacker);var observedGoal=attacker.NavigationGoal;
        enemy.Place(new(500000,700000,-300000),Quaternion.Identity);intelligence.ThinkTerrainCheck(attacker);
        Check(observedGoal is not null && attacker.NavigationGoal==observedGoal,"Hidden actual enemy movement cannot change the observed navigation goal");
        Console.WriteLine($"PASS: {checks} terrain navigation checks");
    }
}
