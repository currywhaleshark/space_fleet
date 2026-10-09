using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Sim;

static class DefenseDroneChecks
{
    private static int _checks;
    private static void Check(bool ok,string why) { if(!ok) throw new Exception(why); _checks++; }
    private static void Step(SimWorld world,double seconds) => AntimatterChecks.Step(world,seconds);
    private static (SimWorld World,ShipBody Attacker,ShipBody Carrier) Fixture() => AntimatterChecks.Fixture(4000,drones:true);
    private static Missile Threat(SimWorld world,ShipBody attacker,ShipBody carrier,Vector3 localPosition)
    {
        world.Sensors.Update(world.Ships,world.Time,force:true);
        Check(world.LaunchMissile(attacker,carrier).Fired,"Test threat launches through normal ordnance pipeline");
        var missile=world.Missiles.Last();
        missile.Position=missile.PrevPosition=carrier.Position+Vec3d.From(carrier.Orientation*localPosition);
        missile.Velocity=Vector3.Zero; missile.Age=missile.Definition.BurnSeconds+1; missile.Health=100000;
        return missile;
    }
    public static void Run()
    {
        CheckDeployment(); CheckEngagement(); CheckFailures(); CheckSafety();
        Console.WriteLine($"PASS: {_checks} defense drone checks");
    }
    private static void CheckDeployment()
    {
        var (world,attacker,carrier)=Fixture(); var drones=carrier.Ordnance.Drones;
        var def=carrier.Definition.DefenseDrones!;
        Check(drones.Active && drones.ArmedCount==8 && drones.Rounds.Sum()==640 && drones.Sector==DroneSector.AllAround,"Eight armed drones begin in all-around patrol");
        Check(!attacker.Ordnance.Drones.Active && !attacker.Ordnance.Drones.Assign(DroneSector.Fore),"Ships without hardware cannot issue a drone command");
        Check(!drones.Assign((DroneSector)999),"Invalid sector rejected");
        foreach(var sector in new[]{DroneSector.Fore,DroneSector.Aft,DroneSector.Port,DroneSector.Starboard,DroneSector.AllAround})
        {
            Vector3[] before=Enumerable.Range(0,def.Count).Select(i=>drones.LocalPosition(i,world.Time)).ToArray();
            Check(drones.Assign(sector),"Valid defense command accepted");
            Check(before.SequenceEqual(Enumerable.Range(0,def.Count).Select(i=>drones.LocalPosition(i,world.Time))),"Commands never teleport drones");
            bool safe=true,limited=true,render=true;
            for(int tick=0;tick<1200;tick++)
            {
                world.Step();
                for(int i=0;i<def.Count;i++)
                {
                    Vector3 p=drones.LocalPosition(i,world.Time);
                    safe &= Math.Abs(p.Length()-def.OrbitMeters)<.01f;
                    limited &= p.DistanceTo(before[i])<=def.RepositionSpeed*SimWorld.TickDelta+.02f;
                    render &= drones.InterpolatedPosition(i,.5f).DistanceTo(before[i].Lerp(p,.5f))<.001f;
                    before[i]=p;
                }
            }
            Check(safe && limited,"Reassignment follows an external shell at a bounded speed, including opposite sectors");
            Check(render,"Drones interpolate between whole physics ticks with their carrier");
            Check(drones.RepositioningCount==0 && drones.Rounds.Sum()==640,"Movement settles with no missiles present and consumes no ammunition");
            if(sector==DroneSector.AllAround) continue;
            Vector3 axis=DefenseDroneState.Direction(sector),side=axis.Cross(Vector3.Up);
            Check(before.All(p=>drones.Covers(p) && p.Normalized().Dot(axis)>.9f),"All drones arrive in the selected defense sector");
            Check(drones.Covers(axis*2000+Vector3.Up*10000) && drones.Covers(axis*2000+Vector3.Down*10000),"Sector covers dorsal and ventral approaches");
            Check(!drones.Covers(-axis*1000) && !drones.Covers(axis*900+side*1000) && drones.Covers(axis*1000+side*900),"Sector edges divide fore/aft/port/starboard without rear fire");
        }
        var position=drones.LocalPosition(0,world.Time); var offset=new Vec3d(1e9,-1e9,1e9);
        carrier.Teleport(offset);
        Check(drones.LocalPosition(0,world.Time)==position,"Large world translation leaves local drone state precise");
        drones.Assign(DroneSector.Fore); Step(world,20);
        carrier.Place(carrier.Position,new Quaternion(Vector3.Up,Mathf.Pi/2));
        Vector3 worldBearing=carrier.Orientation*Vector3.Forward;
        Check(drones.Covers(carrier.Orientation.Inverse()*worldBearing*2000),"Defense sector rotates with the hull");
        Check(drones.InterpolatedPosition(0,0).IsFinite() && drones.LocalDirection(0).LengthSquared()>.99f,"Render direction remains finite while not firing");
    }
    private static void CheckEngagement()
    {
        int Shots(DroneSector sector,Vector3 point,bool rotated=false)
        {
            var (world,attacker,carrier)=Fixture(); var drones=carrier.Ordnance.Drones;
            drones.Assign(sector); Step(world,20);
            if(rotated) carrier.Place(carrier.Position,new Quaternion(Vector3.Up,Mathf.Pi/2));
            Threat(world,attacker,carrier,point); Step(world,3);
            return 640-drones.Rounds.Sum();
        }
        int patrol=Shots(DroneSector.AllAround,Vector3.Forward*2500);
        int forward=Shots(DroneSector.Fore,Vector3.Forward*2500);
        int rear=Shots(DroneSector.Aft,Vector3.Forward*2500);
        Check(forward>patrol && patrol>0 && rear==0,$"Concentration increases actual shots while leaving the opposite sector exposed: {patrol}/{forward}/{rear}");
        foreach(var sector in new[]{DroneSector.Fore,DroneSector.Starboard,DroneSector.Aft,DroneSector.Port})
            Check(Shots(sector,DefenseDroneState.Direction(sector)*2200+Vector3.Up*500,true)>0,"Every sector intercepts in carrier coordinates after a world rotation");
        Check(Shots(DroneSector.Fore,Vector3.Forward*3000)==0,"Concentration does not extend each drone's 1.8 km weapon range");
        var (w,a,c)=Fixture(); var d=c.Ordnance.Drones;
        d.Assign(DroneSector.Fore); Step(w,20);
        var round=Threat(w,a,c,Vector3.Forward*1800); round.Health=.001f;
        Step(w,3);
        Check(!w.Missiles.Contains(round) && d.Interceptions==1,"Actual drone hit destroys a hostile missile and counts the interception once");
        Check(w.OrdnanceEvents.Any(e=>e.Kind==OrdnanceEventKind.Intercepted),"Drone kills produce the shared interception event");
        Check(d.LastFiredAt.Any(t=>double.IsFinite(t)),"Firing exposes actual timestamp for HUD and visuals");
        Console.WriteLine($"Drone fore approach / 3 s: all-around {patrol}, fore {forward}, aft {rear} shots");
    }
    private static void CheckFailures()
    {
        var (w,a,c)=Fixture(); var d=c.Ordnance.Drones;
        d.Assign(DroneSector.Fore); Step(w,20); Threat(w,a,c,Vector3.Forward*1800);
        foreach(var m in c.Damage.Modules.Where(m=>m.Definition.Kind==ModuleKind.Sensor)) c.Damage.Hurt(m,m.Health,w.Time,0,1);
        Step(w,1);
        Check(!d.Active && d.Rounds.Sum()==640 && d.Accum.All(x=>x==0),"Sensor failure stops fire and clears firing credit");
        Check(!d.Assign(DroneSector.Aft),"Failed command link rejects new assignment");
        c.Damage.Reset(); Step(w,.2);
        Check(d.Active && d.Sector==DroneSector.Fore && d.Rounds.Sum()==640,"Repair resumes previous assignment without an accumulated instant burst");
        Step(w,1); Check(d.Rounds.Sum()<640,"Recovered drones resume fire");
        Array.Fill(d.Rounds,1); Step(w,2);
        Check(d.ArmedCount==0 && d.Rounds.Sum()==0,"Each drone stops at zero rounds");
        d.Assign(DroneSector.Aft); Step(w,20); d.Assign(DroneSector.Fore); Step(w,20);
        Check(d.Rounds.Sum()==0,"Reassigning or waiting does not refill ammunition");
        c.Damage.Catastrophe(w.Time,"test"); Step(w,1);
        Check(!d.Active && !d.Assign(DroneSector.Port),"Destroyed carrier cannot command or fire drones");
        c.Damage.Reset(); w.ResetWeapons();
        Check(d.Sector==DroneSector.AllAround && d.Rounds.Sum()==640 && d.Interceptions==0 && d.LastFiredAt.All(double.IsNegativeInfinity)
            && Enumerable.Range(0,8).All(i=>d.InterpolatedPosition(i,0)==d.InterpolatedPosition(i,1)),"Explicit development reset clears all command, ammo, telemetry and interpolation state");
    }
    private static void CheckSafety()
    {
        var (w,a,c)=Fixture(); var d=c.Ordnance.Drones;
        d.Assign(DroneSector.Fore); Step(w,20);
        var friend=w.Add(new ShipBody("FRIEND",ShipClass.Interceptor,c.Faction)); friend.Place(c.Position+new Vec3d(8000,0,0),Quaternion.Identity);
        w.Log=new BattleLog(w);
        Threat(w,friend,a,Vector3.Forward*1000).Position=c.Position+Vec3d.From(Vector3.Forward*1800);
        Step(w,1); Check(d.Rounds.Sum()==640,"Friendly ordnance is never targeted");
        foreach(var m in c.Damage.Modules.Where(m=>m.Definition.Kind is ModuleKind.Generator or ModuleKind.Reactor)) c.Damage.Hurt(m,m.Health,w.Time,0,1);
        Step(w,1); Check(!d.Active,"Loss of carrier power disables drone control");
        using var stream=typeof(ShipDefinitions).Assembly.GetManifestResourceStream("SpaceFleet.data.ships.battleship.json")!;
        using var reader=new StreamReader(stream);
        var data=JsonNode.Parse(reader.ReadToEnd())!.AsObject();
        data.Remove("pointDefense"); data["defenseDrones"]!["count"]=1;
        var def=ShipDefinition.Parse(data.ToJsonString());
        w=new SimWorld(); c=w.Add(new ShipBody("BLOCK",def.Flight,Faction.Red,def)); c.Place(Vec3d.Zero,Quaternion.Identity);
        a=w.Add(new ShipBody("ATTACK",ShipClass.Interceptor,Faction.Blue)); a.Place(new Vec3d(0,0,4000),Quaternion.Identity);
        Threat(w,a,c,new Vector3(-800,0,0)); Step(w,1);
        Check(c.Ordnance.Drones.Rounds[0]==80,"Carrier hull blocks a drone firing through to the opposite side");
        data["defenseDrones"]!["repositionSpeed"]=0;
        bool rejected=false;
        try { ShipDefinition.Parse(data.ToJsonString()); } catch(InvalidDataException) { rejected=true; }
        Check(rejected,"Invalid drone flight speed is rejected at data load");
    }
}
