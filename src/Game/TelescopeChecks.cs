using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>Actual scoped mouse, targeting, firing and ship steering integration.</summary>
public partial class TelescopeChecks : Node
{
    private int _checks;
    private void Check(bool ok,string why) { if(!ok) throw new InvalidOperationException(why); _checks++; }
    private static void Mouse(ScaleTest g,MouseButton button,bool pressed=true) => g._UnhandledInput(new InputEventMouseButton {
        ButtonIndex=button,Pressed=pressed,Position=new(25,25) }); // Deliberately away from the crosshair.
    private static void Key(ScaleTest g,Godot.Key key) => g._UnhandledInput(new InputEventKey {PhysicalKeycode=key,Pressed=true});
    private static void Move(ScaleTest g,Vector2 delta)
    { g._UnhandledInput(new InputEventMouseMotion {Relative=delta,Position=new(25,25)}); g._Process(1f/60); }
    private static void Step(ScaleTest g,int frames)
    { for(int i=0;i<frames;i++) { g._PhysicsProcess(SimWorld.TickDelta); g._Process(SimWorld.TickDelta); } }
    private ScaleTest Scene(string callsign)
    {
        var g=new ScaleTest {BattleMode=true,DevMode=false,LaunchControl=callsign}; AddChild(g);
        g.SetProcess(false); g.SetPhysicsProcess(false);
        foreach(var ship in g.World.Ships) g.World.DetachBrain(ship);
        g._Process(1); return g;
    }
    public override async void _Ready()
    {
        try { CheckHullOptics(); CheckHelm(); CheckPilot(); await CaptureOptics(); GD.Print($"PASS: {_checks} telescope interaction checks"); GetTree().Quit(); }
        catch(Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
        finally { Input.ActionRelease(InputSetup.Fire); }
    }
    private async System.Threading.Tasks.Task CaptureOptics()
    {
        if (!BattleArgs.Parse(OS.GetCmdlineUserArgs()).TryGetValue("telescope-shots",out var folder)) return;
        System.IO.Directory.CreateDirectory(folder);
        foreach(string id in new[]{"earth_air_defense_cruiser","mars_missile_cruiser","earth_starter_escort"})
        {
            var lineup=new MatchLineup(new[]{new MatchParticipant(1,Faction.Blue,id,true),new MatchParticipant(1,Faction.Red,"escort",false)});
            var game=new ScaleTest {BattleMode=true,DevMode=false,LaunchControl="B01-1",LaunchConfig=new BattleConfig{Match=lineup}};
            AddChild(game);game.SetProcess(false);game.SetPhysicsProcess(false);
            var me=game.Controlled!.Body;int i=0;
            foreach(var ship in game.World.Ships) {game.World.DetachBrain(ship);ship.Place(new(1e9,++i*10000,0),Quaternion.Identity);ship.Velocity=Vector3.Zero;}
            me.Place(Vec3d.Zero,Quaternion.Identity);game.Gunnery!.Doctrine=FireDoctrine.Hold;
            var target=game.Views.First(v=>v.Body.Faction!=me.Faction);
            var hud=game.GetNode<Hud>("HudLayer/Hud");
            foreach(var size in new[]{new Vector2I(1600,900),new Vector2I(1280,720)})
            foreach(float yaw in new[]{0f,180})
            {
                GetWindow().Size=size;
                game.Camera.ResetTelescope();game.Camera.ResetAim(Quaternion.Identity);game.Camera.SetTelescope(true);
                Vector3 direction=new Quaternion(Vector3.Up,Mathf.DegToRad(yaw))*Vector3.Forward;
                game.Camera.Observe(direction,Quaternion.Identity);game._Process(1);
                Check(game.Camera.AimForward.Dot(direction)>.999f,"Rendered scope uses the requested azimuth");
                game.SelectionHudFixture(target,TrackLevel.Locked,game.Camera.Position+game.Camera.AimForward*6000);
                game._Process(.016);hud.QueueRedraw();
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                hud.QueueRedraw();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                Check(!game.ScopeHullBlocked,$"{id}: rendered scope clear at {yaw}");
                string path=System.IO.Path.Combine(folder,$"{id}-{size.X}-{yaw}.png");
                Check(GetViewport().GetTexture().GetImage().SavePng(path)==Error.Ok,"Scope screenshot saved");
            }
            game.Free();
        }
    }
    private void CheckHullOptics()
    {
        var camera=new ChaseCamera {Mode=CameraMode.MouseAim}; AddChild(camera);
        var rotation=new Quaternion(Vector3.Up,.8f)*new Quaternion(Vector3.Forward,.6f);
        var position=new Vector3(12000,-2000,5000);
        foreach(var definition in FleetLoadouts.Hulls)
        {
            var body=new ShipBody("OPTIC",definition,Faction.Blue);
            var view=ShipView.Create(body,47); AddChild(view);
            var boxes=TelescopeOrbit.ModelBounds(view.GetNode<Node3D>("Model")).ToArray();
            var orbit=view.OpticOrbit;
            camera.ResetTelescope(); camera.ResetAim(rotation); camera.SetTelescope(true);
            foreach(float yaw in new[]{0f,30,60,90,120,150,180,210,240,270,300,330})
            foreach(float pitch in new[]{0f,20,60})
            {
                camera.ResetAim(rotation*new Quaternion(Vector3.Up,Mathf.DegToRad(yaw))*new Quaternion(Vector3.Right,Mathf.DegToRad(pitch)));
                camera.Follow(definition,position,rotation,1,orbit);
                Vector3 local=rotation.Inverse()*(camera.Position-position);
                Vector3 ray=rotation.Inverse()*camera.AimForward;
                Check(!DamageRay.FirstHitAtPose(body,Vec3d.From(local),ray,definition.Flight.Length*2,
                    Vec3d.Zero,Quaternion.Identity,out _),$"{definition.Id}: damage hull clears scope at {yaw}/{pitch}");
                Check(!boxes.Any(box=>box.IntersectsSegment(local,local+ray*definition.Flight.Length*2)),
                    $"{definition.Id}: authored model clears scope at {yaw}/{pitch}");
            }
            GD.Print($"Optic {definition.Id}: center={orbit.Center}, radius={orbit.Radius:0.0}");
            view.Free();
        }
        camera.Free();
    }
    private void CheckHelm()
    {
        var g=Scene("BB-01"); var ship=g.Controlled!.Body;
        g.Gunnery!.Doctrine=FireDoctrine.Hold;
        var orientation=ship.Orientation; var aim=g.Camera.AimForward;
        Mouse(g,MouseButton.Right); g._Process(1);
        Check(g.Camera.TelescopeHeld && Input.MouseMode==Input.MouseModeEnum.Captured && g.Gunnery.Doctrine==FireDoctrine.Manual,
            "Helm scope captures mouse and temporarily takes manual battery control");
        Move(g,new Vector2(350,-100));
        Check(g.Camera.AimForward.AngleTo(aim)>.1f && ship.Orientation==orientation,
            "Mouse alone turns the capital scope without steering the hull or requiring middle click");
        var target=g.Views.Single(v=>v.Body.Callsign=="BB-X1");
        target.Body.Place(g.RenderOrigin+Vec3d.From(g.Camera.Position+g.Camera.AimForward*12000),Quaternion.Identity);
        g.World.Sensors.Update(g.World.Ships,g.World.Time,force:true); Step(g,1);
        aim=g.Camera.AimForward; int rounds=ship.Railguns.Sum(x=>x.Rounds);
        Key(g,Godot.Key.R);
        Check(g.InspectTarget==target && g.Gunnery.Target==target.Body && g.Camera.AimForward.IsEqualApprox(aim),
            "R selects the known center target without snapping aim or firing");
        Check(ship.Railguns.Sum(x=>x.Rounds)==rounds,"Target selection consumes no rounds");
        target.Body.Velocity=g.Camera.Basis.X*700;
        g.World.Sensors.Update(g.World.Ships,g.World.Time,force:true); Step(g,1);
        var expected=FireControl.Solve(ship,target.Body,g.World.Time,track:g.TrackOf(target),localAim:g.AimModule?.Definition.Center);
        Check(g.Gunnery.Solution is null && g.DisplayLeadSolution is {Valid:true} lead && lead.AimPoint==expected.AimPoint,
            "Manual scope displays the same sensor-based intercept solution without an automatic firing order");
        Check(g.DisplayLeadSolution!.FlightTime>0 && ship.Railguns.Sum(x=>x.Rounds)==rounds && !g.CorrectingAim,
            "Predictive display alone does not fire, consume ammunition or engage assisted steering");
        Input.ActionPress(InputSetup.Fire); Step(g,180); Input.ActionRelease(InputSetup.Fire);
        Check(ship.Railguns.Sum(x=>x.Rounds)<rounds && g.World.Log!.Ship(ship).Rails>0,
            $"Scoped held fire launches center-ray rounds despite an off-center OS pointer: {rounds}->{ship.Railguns.Sum(x=>x.Rounds)}, {g.LastFireMessage}");
        Check(ship.Railguns.Any(gun=>gun.Direction.Dot(aim)>.998f),"Physical turret follows the scope firing direction");
        target.Body.Velocity=Vector3.Zero;
        var sideTarget=g.Views.Single(v=>v.Body.Callsign=="DD-X1");
        sideTarget.Body.Place(g.RenderOrigin+Vec3d.From(g.Camera.Position+g.Camera.AimForward*2000+g.Camera.Basis.X*90),
            g.Camera.Quaternion*new Quaternion(Vector3.Up,Mathf.Pi/2));
        g.World.Sensors.Update(g.World.Ships,g.World.Time,force:true); Step(g,1); Key(g,Godot.Key.R);
        Check(g.InspectTarget==sideTarget,"Center ray picks a visible hull section even when its center marker lies outside the 36-pixel aid");
        sideTarget.Body.Teleport(new Vec3d(1e9,10000,0));
        target.Body.Teleport(new Vec3d(1e9,0,0)); g.World.Sensors.Update(g.World.Ships,g.World.Time,force:true); Step(g,1);
        Key(g,Godot.Key.R);
        Check(g.InspectTarget is { } remembered && g.ContactOf(remembered) is not null && g.Camera.AimForward.IsEqualApprox(aim),"R selects only a visible or remembered contact without reading hidden positions or snapping aim");
        Move(g,new Vector2(0,1700));
        Check(g.ScopeHullBlocked,"Looking down from bridge orbit is naturally occluded by own deck");
        rounds=ship.Railguns.Sum(x=>x.Rounds);
        Input.ActionPress(InputSetup.Fire); Step(g,30); Input.ActionRelease(InputSetup.Fire);
        Check(ship.Railguns.Sum(x=>x.Rounds)==rounds && g.LastFireFailed,"Own-hull scope occlusion blocks blind manual shots");
        Mouse(g,MouseButton.Right,false);
        Check(!g.Camera.TelescopeHeld && Input.MouseMode==Input.MouseModeEnum.Visible && g.Gunnery.Doctrine==FireDoctrine.Hold,
            "Release restores helm cursor and pre-scope doctrine");
        Step(g,30); Check(ship.Railguns.Sum(x=>x.Rounds)==rounds,"Restored hold doctrine cannot auto-fire after scope exit");
        Mouse(g,MouseButton.Middle); Move(g,new Vector2(150,0)); Mouse(g,MouseButton.Right);
        Mouse(g,MouseButton.Middle,false); Mouse(g,MouseButton.Right,false);
        Check(!g.Camera.FreeLooking && g.Camera.FreeLookHoldRemaining==ChaseCamera.FreeLookHoldSeconds,
            "Entering scope during a middle drag cannot leave free look latched after both buttons release");
        foreach(var key in new[]{Godot.Key.Escape,Godot.Key.M,Godot.Key.F})
        {
            Mouse(g,MouseButton.Right); Key(g,key);
            Check(!g.Camera.TelescopeHeld && Input.MouseMode==Input.MouseModeEnum.Visible && g.Gunnery.Doctrine==FireDoctrine.Hold,
                $"{key} restores scoped input ownership and doctrine");
            if(g.WorldMapOpen) Key(g,Godot.Key.M);
            if(g.MenuOpen) Key(g,Godot.Key.Escape);
            g._Process(.016);
        }
        Check(!g.Paused,"Escape from scope does not pause the battle");
        Mouse(g,MouseButton.Right); g._Notification((int)NotificationApplicationFocusOut);
        Check(!g.Camera.TelescopeHeld && Input.MouseMode==Input.MouseModeEnum.Visible && g.Gunnery.Doctrine==FireDoctrine.Hold,
            "Focus loss restores the temporary helm camera and gunnery state");
        g.Free();
    }
    private void CheckPilot()
    {
        var g=Scene("IC-21"); var ship=g.Controlled!.Body;
        Mouse(g,MouseButton.Right); g._Process(1);
        Move(g,new Vector2(1400,0));
        Vector3 aim=g.Camera.AimForward; float initial=ship.Forward.AngleTo(aim);
        int rounds=ship.Railgun!.Rounds;
        Input.ActionPress(InputSetup.Fire); Step(g,1);
        Check(initial>.5f && ship.Forward.AngleTo(aim)<initial && ship.Forward.AngleTo(aim)>.4f,
            "Scoped interceptor starts physically turning toward the mouse instead of snapping or orbiting independently");
        Check(ship.Railgun.Rounds==rounds,"Forward fixed gun cannot fire outside its limited traverse during the turn");
        Step(g,180); Input.ActionRelease(InputSetup.Fire);
        Check(ship.Forward.Dot(g.Camera.AimForward)>.99f && ship.Railgun.Rounds<rounds,
            "After nose alignment the scoped interceptor fires while retaining normal flight limits");
        Mouse(g,MouseButton.Right,false);
        Check(Input.MouseMode==Input.MouseModeEnum.Captured,"Pilot release retains the normal captured flight controls");
        g.Free();
    }
}
