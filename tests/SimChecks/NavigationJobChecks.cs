using Godot;
using SpaceFleet.Sim;

static class NavigationJobChecks
{
    public static void Run()
    {
        int checks=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
        foreach(double shift in new[]{0.0,1e12})
        {
            var center=new Vec3d(shift,-shift,shift);var obstacles=new ObstacleSet();
            obstacles.Add(new("SCREEN",center,Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,3500)));
            var nav=new TerrainNavigation(obstacles,center,new(22000,12000,22000));nav.Prepare(100);
            var start=center+new Vec3d(-14000,0,0);var goal=center+new Vec3d(14000,0,0);
            var observers=Enumerable.Range(0,24).Select(i=>center+new Vec3d(i*400-4000,0,-16000)).ToArray();
            foreach(bool cover in new[]{false,true})foreach(int limit in new[]{1,20,1024,30000})
            {
                bool expected=nav.FindPath(start,goal,100,out var original,limit,cover?new(obstacles,observers):null);
                // Interleave independent searches, including a synchronous call
                // sharing edge caches. Slice sizes must never change the answer.
                var jobs=new[]{1,37,1500,12000}.Select(budget=>(Budget:budget,Job:nav.BeginPath(start,goal,100,limit,cover?new(obstacles,observers):null))).ToArray();
                int turns=0;
                while(jobs.Any(x=>!x.Job.Complete))
                {
                    if(++turns>2000000)throw new Exception("Search did not make bounded progress");
                    foreach(var (budget,job) in jobs)
                    { int used=job.Step(budget);if(used<0 || used>budget)throw new Exception("Exceeded granted work"); }
                    if(turns==10)nav.FindPath(start+new Vec3d(0,6000,0),goal,100,out _);
                }
                foreach(var (_,job) in jobs)
                {
                    Check(job.Found==expected && job.Path.Expanded==original.Expanded && job.Path.Length==original.Length
                        && job.Path.Points.SequenceEqual(original.Points),"Interleaved sliced path equals synchronous result, including failure and smoothing");
                    Check(job.Step(1)==0,"Finished job consumes no more work");job.Dispose();
                }
            }
            using var cancelled=nav.BeginPath(start,goal,100,exposure:new(obstacles,observers));cancelled.Step(37);cancelled.Dispose();
            Check(cancelled.Complete && !cancelled.Found && cancelled.Step(12000)==0,"Cancelled search cannot publish a path");
        }
        var world=new SimWorld();
        world.Obstacles.Add(new("ROCK",new(0,0,-75000),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,3500)));
        for(int i=0;i<24;i++)
        {
            var ship=world.Add(new ShipBody("QUEUE"+i,ShipClass.Interceptor,Faction.Blue));
            ship.Place(new(-14000,i*70,-75000),Quaternion.Identity);
            world.AttachBrain(ship,ShipOrder.HoldAt(new(14000,i*70,-75000)));
        }
        world.PrepareNavigation(new());world.Log=new BattleLog(world);
        foreach(var brain in world.Brains.Values)world.ThinkTerrainCheck(brain);
        Check(world.PendingNavigation==24,"Every blocked ship queues once without synchronous planning");
        Check(world.Log.Environment.PathPlans==0,"Thinking only requests work");
        var first=world.Ships[0];var stale=world.BrainOf(world.Ships[1])!;var disabled=world.BrainOf(world.Ships[2])!;
        world.DetachBrain(first);stale.Order=ShipOrder.HoldAt(new(-50000,0,-75000));disabled.Enabled=false;
        for(int i=0;i<180;i++)
        { world.Step();Check(world.NavigationWorkLastTick<=SimWorld.NavigationWorkBudget,"Fleet planning stays within one shared tick budget"); }
        Check(world.Log.Environment.PathCancelled>=3,"Detached, changed-order and disabled requests are cancelled");
        Check(world.Log.Environment.PathPlans>=21 && world.PendingNavigation==0,"Queued ships all receive routes without starvation");
        Check(world.Log.Environment.ShipCollisions==0,"Waiting ships brake safely before the obstruction");
        Check(stale.NavigationWaypoint is null || (stale.NavigationWaypoint.Value-new Vec3d(14000,70,-75000)).Length()>1000,"Old destination is not applied after command replacement");
        foreach(var ship in world.Ships.Skip(3))
        { ship.Place(new(-14000,0,-75000),Quaternion.Identity);var b=world.BrainOf(ship)!;b.Order=ShipOrder.HoldAt(new(14000,0,-75000)); }
        world.PrepareNavigation(new());foreach(var brain in world.Brains.Values.Where(b=>b.Enabled))world.ThinkTerrainCheck(brain);
        Check(world.PendingNavigation>0,"Rebuilt grid accepts new jobs");world.ResetWeapons();
        Check(world.PendingNavigation==0,"Reset releases searches and route snapshots");
        var fallback=new SimWorld();var scout=fallback.Add(new ShipBody("COVER",ShipClass.Interceptor,Faction.Blue));
        scout.Place(new(-50000,0,-75000),Quaternion.Identity);
        fallback.AddSquadron("SCOUT",Faction.Blue,SquadronRole.InterceptorWing,new[]{scout}).Mission=InterceptorMission.Recon;
        var enemy=fallback.Add(new ShipBody("OBSERVED",ShipClass.Battleship,Faction.Red));
        enemy.Place(new(-40000,10000,-75000),Quaternion.Identity);
        fallback.Obstacles.Add(new("SCREEN",new(0,0,-75000),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,3500)));
        fallback.PrepareNavigation(new());fallback.Sensors.Update(fallback.Ships,0,force:true);fallback.Log=new BattleLog(fallback);
        var planner=fallback.AttachBrain(scout,ShipOrder.HoldAt(new(50000,0,-75000)));fallback.ThinkTerrainCheck(planner);
        Check(fallback.PendingNavigation==1,"Cover search is queued");
        for(int i=0;i<1000 && fallback.PendingNavigation>0;i++)fallback.NavigationJobsCheck();
        Check(fallback.PendingNavigation==0 && fallback.Log.Environment.CoverBudgetFallbacks==1
            && fallback.Log.Environment.PathFailures==0,"Exhausted cover budget falls back to a sliced ordinary path");
        fallback.ThinkTerrainCheck(planner);
        Check(planner.NavigationActive && planner.NavigationWaypoint is { } corner
            && !fallback.Obstacles.BlocksSegment(scout.Position,corner,fallback.NavigationRadius(HullKind.Interceptor)),"Completed fallback is applied through safe normal steering");
        fallback.PrepareNavigation(new());fallback.ThinkTerrainCheck(planner);scout.Damage.Breakup(0,"test");fallback.NavigationJobsCheck();
        Check(fallback.PendingNavigation==0 && fallback.Log.Environment.PathCancelled>0,"Destroyed ship cannot receive a completed path");
        Console.WriteLine($"PASS: {checks} incremental navigation checks");
    }
}
