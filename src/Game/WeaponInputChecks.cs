using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>--weapon-input-test: actual input routing, independent ammo and optical camera transitions.</summary>
public partial class WeaponInputChecks : Node
{
    private int _checks;
    private void Check(bool ok,string why) { if(!ok) throw new InvalidOperationException(why); _checks++; }
    private static void KeyEvent(ScaleTest game,Key key,bool pressed=true)
        => game._UnhandledInput(new InputEventKey { PhysicalKeycode=key,Pressed=pressed });
    private static void Mouse(ScaleTest game,MouseButton button,bool pressed=true)
        => game._UnhandledInput(new InputEventMouseButton { ButtonIndex=button,Pressed=pressed,Position=new(800,450) });
    private static void Follow(ScaleTest game,int frames=120)
    {
        var ship=game.Controlled!.Body;
        for(int i=0;i<frames;i++) game.Camera.Follow(ship.Definition,Vector3.Zero,ship.Orientation,1f/60);
    }
    private static void Tick(ScaleTest game,int frames=1)
    { for(int i=0;i<frames;i++) game._PhysicsProcess(SimWorld.TickDelta); }
    public override void _Ready()
    {
        try { Run(); GD.Print($"PASS: {_checks} weapon selection/telescope checks"); GetTree().Quit(); }
        catch(Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
        finally { Input.ActionRelease(InputSetup.Fire); }
    }
    private void Run()
    {
        InputSetup.Register(); InputSetup.Register();
        Check(InputMap.ActionGetEvents(InputSetup.Fire).Count==1 && InputMap.ActionGetEvents(InputSetup.Telescope).Count==1,
            "Registering scenes twice does not duplicate mouse bindings");
        Check(!InputMap.HasAction("launch_missile") && !InputMap.HasAction("power_weapons"),"Legacy input actions removed");
        var game=new ScaleTest { BattleMode=true,DevMode=false,LaunchControl="IC-21" }; AddChild(game);
        game.SetProcess(false); game.SetPhysicsProcess(false);
        foreach(var body in game.World.Ships) game.World.DetachBrain(body);
        game.SetupAntimatterPractice();
        var ship=game.Controlled!.Body; var am=ship.Ordnance.Antimatter;
        int[] pips=Enum.GetValues<PowerChannel>().Select(ship.Power.Pips).ToArray();
        Check(game.SelectedWeapon==PlayerWeapon.MainGun,"Initial weapon is main gun in normal battle");
        Input.MouseMode=Input.MouseModeEnum.Captured;
        int rail=ship.Railgun!.Rounds;
        Input.ActionPress(InputSetup.Fire); Tick(game); Input.ActionRelease(InputSetup.Fire);
        Check(ship.Railgun.Rounds==rail-1 && ship.Ordnance.Missiles==4 && am.Rounds==2,"Main gun click consumes only main ammo");
        rail=ship.Railgun.Rounds;
        KeyEvent(game,Key.Key2);
        Check(game.SelectedWeapon==PlayerWeapon.Missile,"Key 2 selects missile in non-dev battle");
        Mouse(game,MouseButton.Right); Follow(game);
        Check(game.Camera.TelescopeHeld && Math.Abs(game.Camera.Fov-ChaseCamera.TelescopeFov)<.01,"Right hold reaches fourfold optical magnification");
        Check(ship.Ordnance.Missiles==4 && am.Rounds==2,"Right click never fires secondary weapons");
        Mouse(game,MouseButton.Left); Input.ActionPress(InputSetup.Fire); Tick(game,155); Input.ActionRelease(InputSetup.Fire);
        Mouse(game,MouseButton.Left,false);
        Check(ship.Ordnance.Missiles==3 && ship.Railgun.Rounds==rail,"Scoped missile click fires once, holding never fires railgun or extra missiles");
        Mouse(game,MouseButton.Right,false); Follow(game);
        Check(!game.Camera.TelescopeHeld && Math.Abs(game.Camera.Fov-70)<.01,"Right release restores normal field of view");
        KeyEvent(game,Key.Key3); Tick(game,30); float elapsed=am.ArmingElapsed;
        KeyEvent(game,Key.Key3);
        Check(game.AntimatterSelected && am.Mode==AntimatterMode.Arming && am.ArmingElapsed==elapsed,"Reselecting torpedo keeps preparation progress");
        KeyEvent(game,Key.Key1);
        Check(game.SelectedWeapon==PlayerWeapon.MainGun && am.Mode==AntimatterMode.Safe,"Switch to main cancels preparation");
        KeyEvent(game,Key.Key3); Tick(game,155);
        Check(am.Ready,"Selected torpedo becomes armed");
        Mouse(game,MouseButton.Right); Mouse(game,MouseButton.Left); Mouse(game,MouseButton.Left,false);
        Check(am.Rounds==1 && ship.Ordnance.Missiles==3 && ship.Railgun.Rounds==rail,"Scoped left click fires only selected torpedo");
        Check(am.Mode==AntimatterMode.Safe,"Next torpedo requires a new preparation");
        KeyEvent(game,Key.Key3); KeyEvent(game,Key.Key2);
        Check(am.Mode==AntimatterMode.Safe,"Switch to missile also cancels preparation");
        foreach(var key in new[]{Key.Key4,Key.Key5,Key.Key0,Key.Key6}) KeyEvent(game,key);
        Check(game.SelectedWeapon==PlayerWeapon.Missile && pips.SequenceEqual(Enum.GetValues<PowerChannel>().Select(ship.Power.Pips)),
            "Number keys never change power allocation; a ship without drones cannot open their radial");
        Check(!game.MenuOpen,"Key 4 on an interceptor reports unavailable hardware without trapping input");

        Mouse(game,MouseButton.Right); KeyEvent(game,Key.F);
        Check(game.MenuOpen && !game.Camera.TelescopeHeld && game.Camera.Fov==70,"Power radial still opens and exits scope");
        int missiles=ship.Ordnance.Missiles;
        Mouse(game,MouseButton.Left); KeyEvent(game,Key.Key3); Mouse(game,MouseButton.Right);
        Check(ship.Ordnance.Missiles==missiles && !game.AntimatterSelected && !game.Camera.TelescopeHeld,"Radial blocks weapons and telescope");
        KeyEvent(game,Key.Escape); Mouse(game,MouseButton.Left,false);
        Mouse(game,MouseButton.Right); KeyEvent(game,Key.M);
        Check(game.WorldMapOpen && !game.Camera.TelescopeHeld,"Full map exits telescope");
        Mouse(game,MouseButton.Left); KeyEvent(game,Key.Key3);
        Check(ship.Ordnance.Missiles==missiles && !game.AntimatterSelected,"Map clicks cannot fire or switch weapons");
        KeyEvent(game,Key.M); game._Process(.016);
        Mouse(game,MouseButton.Right); game.Paused=true;
        Check(!game.Camera.TelescopeHeld && game.Camera.Fov==70,"Pause immediately resets optics even without camera ticks");
        Mouse(game,MouseButton.Left); Check(ship.Ordnance.Missiles==missiles,"Pause blocks selected weapon");
        game.Paused=false;
        Mouse(game,MouseButton.Right); KeyEvent(game,Key.Escape);
        Check(Input.MouseMode==Input.MouseModeEnum.Visible && !game.Camera.TelescopeHeld,"Escape releases cursor and telescope");
        Mouse(game,MouseButton.Left);
        Check(Input.MouseMode==Input.MouseModeEnum.Captured && ship.Ordnance.Missiles==missiles,"Recapture click does not fire selected missile");
        Mouse(game,MouseButton.Left,false); Mouse(game,MouseButton.Left); Mouse(game,MouseButton.Left,false);
        Check(ship.Ordnance.Missiles==missiles-1,"Fresh click after recapture fires normally");
        Mouse(game,MouseButton.Right); game._Notification((int)NotificationApplicationFocusOut);
        Check(!game.Camera.TelescopeHeld && game.Camera.Fov==70,"Application focus loss exits telescope");
        Input.ActionPress(InputSetup.Fire); KeyEvent(game,Key.Key1); Tick(game,60);
        Check(ship.Railgun.Rounds==rail,"Switching while holding fire cannot discharge the new weapon");
        Input.ActionRelease(InputSetup.Fire); Mouse(game,MouseButton.Left,false);
        Input.ActionPress(InputSetup.Fire); Tick(game); Input.ActionRelease(InputSetup.Fire);
        Check(ship.Railgun.Rounds==rail-1,"New click after weapon switch fires main gun");
        CheckOptics(game);
        game.Free();

        game=new ScaleTest { LaunchControl="BB-01" }; AddChild(game);
        game.SetProcess(false); game.SetPhysicsProcess(false);
        foreach(var body in game.World.Ships) game.World.DetachBrain(body);
        Check(game.SelectedWeapon==PlayerWeapon.MainGun,"New ship begins with main weapon");
        KeyEvent(game,Key.F7); game.Gunnery!.Doctrine=FireDoctrine.Hold;
        int battery=game.Controlled!.Body.Railguns.Sum(g=>g.Rounds);
        int payload=game.Controlled.Body.Ordnance.Missiles;
        Input.ActionPress(InputSetup.Fire); Tick(game,180); Input.ActionRelease(InputSetup.Fire);
        Check(game.Controlled.Body.Railguns.Sum(g=>g.Rounds)<battery && game.Controlled.Body.Ordnance.Missiles==payload,
            "Capital main selection fires turrets under player control without launching missiles");
        KeyEvent(game,Key.Key2); KeyEvent(game,Key.Key3);
        Check(game.SelectedWeapon==PlayerWeapon.Missile && !game.AntimatterSelected,"Ship without torpedoes retains previous valid weapon");
        battery=game.Controlled.Body.Railguns.Sum(g=>g.Rounds);
        Mouse(game,MouseButton.Left); Input.ActionPress(InputSetup.Fire); Tick(game,180); Input.ActionRelease(InputSetup.Fire);
        Mouse(game,MouseButton.Left,false);
        Check(game.Controlled.Body.Ordnance.Missiles==payload-1 && game.Controlled.Body.Railguns.Sum(g=>g.Rounds)==battery,
            "Capital missile selection fires one missile without manual main battery fire");
        Mouse(game,MouseButton.Right); Follow(game);
        Check(game.Camera.TelescopeHeld && game.Camera.Fov<21,"Capital ship has the same telescope control");
        KeyEvent(game,Key.Tab);
        Check(!game.Camera.TelescopeHeld && game.SelectedWeapon==PlayerWeapon.MainGun,"Ship handover resets scope and weapon selection");
        game.Free();
        CheckDroneInput();
    }
    private void CheckDroneInput()
    {
        var game=new ScaleTest { BattleMode=true,DevMode=false,LaunchControl="BB-01" }; AddChild(game);
        game.SetProcess(false); game.SetPhysicsProcess(false);
        foreach(var body in game.World.Ships) game.World.DetachBrain(body);
        var ship=game.Controlled!.Body; var drones=ship.Ordnance.Drones;
        var radial=game.GetNode<RadialMenu>("HudLayer/RadialMenu");
        int battery=ship.Railguns.Sum(g=>g.Rounds),missiles=ship.Ordnance.Missiles;
        var pips=Enum.GetValues<PowerChannel>().Select(ship.Power.Pips).ToArray();
        void Point(float degrees) => game._UnhandledInput(new InputEventMouseMotion {
            Position=radial.Center+new Vector2(Mathf.Sin(Mathf.DegToRad(degrees)),-Mathf.Cos(Mathf.DegToRad(degrees)))*100 });
        Check(InputMap.ActionGetEvents(InputSetup.DroneMenu).Count==1,"Drone key registered once across scene restarts");
        foreach(var (angle,sector) in new[]{(0f,DroneSector.Fore),(90f,DroneSector.Starboard),(180f,DroneSector.Aft),(270f,DroneSector.Port),(225f,DroneSector.AllAround)})
        {
            Mouse(game,MouseButton.Right); KeyEvent(game,Key.Key4);
            Check(game.MenuOpen && !game.Camera.TelescopeHeld,"4 opens drone radial in a normal battle and clears telescope");
            var before=drones.Sector; Point(angle); KeyEvent(game,Key.Key2); Mouse(game,MouseButton.Left);
            Check(drones.Sector==before && game.SelectedWeapon==PlayerWeapon.MainGun,"Highlighting a sector waits for release and blocks weapon changes");
            KeyEvent(game,Key.Key4,false); Mouse(game,MouseButton.Left,false);
            Check(!game.MenuOpen && drones.Sector==sector,"Release applies exactly the highlighted defense direction");
        }
        Check(ship.Railguns.Sum(g=>g.Rounds)==battery && ship.Ordnance.Missiles==missiles && drones.Rounds.Sum()==640
            && pips.SequenceEqual(Enum.GetValues<PowerChannel>().Select(ship.Power.Pips)),"Drone commands do not fire, consume ammo or alter power allocation");
        KeyEvent(game,Key.Key4); KeyEvent(game,Key.Key4,false);
        Check(drones.Sector==DroneSector.AllAround,"Center release cancels without changing command");
        KeyEvent(game,Key.Key4); Point(0); KeyEvent(game,Key.Escape); KeyEvent(game,Key.Key4,false);
        Check(!game.MenuOpen && !game.Paused && drones.Sector==DroneSector.AllAround,"Escape cancels the drone radial before pausing");
        KeyEvent(game,Key.Key4); Point(90); game.Paused=true; game.Paused=false; KeyEvent(game,Key.Key4,false);
        Check(!game.MenuOpen && drones.Sector==DroneSector.AllAround,"Pause cancels a pending drone command");
        KeyEvent(game,Key.Key4); Point(180); game._Notification((int)NotificationApplicationFocusOut); KeyEvent(game,Key.Key4,false);
        Check(!game.MenuOpen && drones.Sector==DroneSector.AllAround,"Focus loss cannot leave an armed radial gesture");
        KeyEvent(game,Key.Key4); Point(270); KeyEvent(game,Key.M); KeyEvent(game,Key.M); KeyEvent(game,Key.Key4,false);
        Check(!game.MenuOpen && drones.Sector==DroneSector.AllAround,"Full map cancels a pending drone command");
        drones.Assign(DroneSector.Fore); Tick(game,720);
        game.Controlled.Sync(game.RenderOrigin,.5,1f/60);
        var swarm=game.Controlled.Drones!;
        Check(swarm.Multimesh.InstanceCount==8 && Enumerable.Range(0,8).All(i=>swarm.Multimesh.GetInstanceTransform(i).Origin
            .IsEqualApprox(drones.InterpolatedPosition(i,.5f))),"Rendered drones match physical positions and interpolation after reassignment");
        foreach(var module in ship.Damage.Modules.Where(m=>m.Definition.Kind==ModuleKind.Sensor)) ship.Damage.Hurt(module,module.Health,game.World.Time,0,1);
        KeyEvent(game,Key.Key4); Point(180); KeyEvent(game,Key.Key4,false); game.Controlled.Sync(game.RenderOrigin,1,1f/60);
        Check(drones.Sector==DroneSector.Fore && swarm.Visible,"Disabled controls cannot change sectors or make deployed drones disappear");
        game.Free();
    }
    private void CheckOptics(ScaleTest game)
    {
        var cam=game.Camera; cam.ResetAim(Quaternion.Identity); cam.ResetTelescope(); Follow(game);
        var aim=cam.AimForward; var position=cam.Position;
        Mouse(game,MouseButton.Right); Follow(game);
        float nose=game.Controlled!.Body.Definition.HullSections.Min(s=>s.Center.Z-s.HalfSize.Z);
        Check(cam.AimForward.IsEqualApprox(aim) && (game.Controlled.Body.Orientation.Inverse()*cam.Position).Z<nose
            && !cam.Position.IsEqualApprox(position),
            "Optical zoom preserves aim and moves a bridgeless ship's viewpoint forward");
        cam.AddMouse(new Vector2(100,0)); Follow(game,1);
        float scoped=cam.AimForward.AngleTo(aim);
        cam.ResetAim(Quaternion.Identity); cam.ResetTelescope(); cam.AddMouse(new Vector2(100,0)); Follow(game,1);
        float normal=cam.AimForward.AngleTo(aim);
        Check(Math.Abs(normal/scoped-4)<.02,"Scope mouse sensitivity scales by optical magnification");
        cam.ResetAim(Quaternion.Identity); cam.Zoom(.9f); Follow(game); position=cam.Position;
        Mouse(game,MouseButton.Right); Follow(game); Mouse(game,MouseButton.Right,false); Follow(game);
        Check(cam.Position.IsEqualApprox(position),"Telescope preserves wheel camera distance on release");
        CheckBridgeOptics();
    }

    private void CheckBridgeOptics()
    {
        var cam=new ChaseCamera { Mode=CameraMode.MouseAim }; AddChild(cam);
        foreach(var kind in new[]{HullKind.Battleship,HullKind.Escort})
        {
            var def=ShipDefinitions.For(kind);
            var bridge=def.HullSections.Single(s=>s.Id=="bridge");
            var position=new Vector3(12000,-2000,5000);
            var rotation=new Quaternion(Vector3.Up,.8f)*new Quaternion(Vector3.Forward,.6f);
            cam.ResetAim(rotation); cam.ResetTelescope(); cam.Follow(def,position,rotation,1f/60);
            var chase=cam.Position; var aim=cam.AimForward;
            cam.SetTelescope(true); cam.Follow(def,position,rotation,1f/60);
            var local=rotation.Inverse()*(cam.Position-position);
            Check(local.Z<bridge.Center.Z-bridge.HalfSize.Z && local.Y>bridge.Center.Y
                && local.Y<bridge.Center.Y+bridge.HalfSize.Y,$"{kind}: first scoped frame is outside the bridge front at viewing height");
            Check(cam.AimForward.IsEqualApprox(aim),$"{kind}: scope movement does not steer the ship or firing direction");
            Check(!DamageRay.FirstHitAtPose(new ShipBody("OPTIC",def.Flight,Faction.Blue,def),
                Vec3d.From(cam.Position),aim,def.Flight.Length,Vec3d.From(position),rotation,out _),
                $"{kind}: forward scope sightline is clear of own hull and bridge");
            cam.AddMouse(new Vector2(90,30)); cam.Follow(def,position,rotation,1f/60);
            Vector3 orbited=rotation.Inverse()*(cam.Position-position);
            Check(Math.Abs(orbited.Y-local.Y)<.003f && orbited.DistanceTo(local)>.1f,
                $"{kind}: scope orbits at constant bridge height while aim changes");
            cam.ResetAim(rotation); cam.SetTelescope(false); cam.Follow(def,position,rotation,1f/60);
            Check(cam.Position.DistanceTo(chase)<.003f,$"{kind}: release restores the saved chase distance");
            cam.SetTelescope(true);
            foreach(float yaw in new[]{0f,45,90,135,179,-179,-135,-90,-45})
            {
                cam.ResetAim(rotation*new Quaternion(Vector3.Up,Mathf.DegToRad(yaw)));
                cam.Follow(def,position,rotation,1);
                Check(!DamageRay.FirstHitAtPose(new ShipBody("ORBIT",def.Flight,Faction.Blue,def),
                    Vec3d.From(cam.Position),cam.AimForward,def.Flight.Length,Vec3d.From(position),rotation,out _),
                    $"{kind}: bridge orbit remains unobstructed at {yaw} degrees, including astern and corners");
            }
            cam.ResetTelescope();
        }
        cam.Free();
    }
}
