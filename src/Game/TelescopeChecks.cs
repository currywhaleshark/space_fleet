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
    public override void _Ready()
    {
        try { CheckHelm(); CheckPilot(); GD.Print($"PASS: {_checks} telescope interaction checks"); GetTree().Quit(); }
        catch(Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
        finally { Input.ActionRelease(InputSetup.Fire); }
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
        Input.ActionPress(InputSetup.Fire); Step(g,180); Input.ActionRelease(InputSetup.Fire);
        Check(ship.Railguns.Sum(x=>x.Rounds)<rounds && g.World.Log!.Ship(ship).Rails>0,
            $"Scoped held fire launches center-ray rounds despite an off-center OS pointer: {rounds}->{ship.Railguns.Sum(x=>x.Rounds)}, {g.LastFireMessage}");
        Check(ship.Railguns.Any(gun=>gun.Direction.Dot(aim)>.998f),"Physical turret follows the scope firing direction");
        var sideTarget=g.Views.Single(v=>v.Body.Callsign=="DD-X1");
        sideTarget.Body.Place(g.RenderOrigin+Vec3d.From(g.Camera.Position+g.Camera.AimForward*2000+g.Camera.Basis.X*90),
            g.Camera.Quaternion*new Quaternion(Vector3.Up,Mathf.Pi/2));
        g.World.Sensors.Update(g.World.Ships,g.World.Time,force:true); Step(g,1); Key(g,Godot.Key.R);
        Check(g.InspectTarget==sideTarget,"Center ray picks a visible hull section even when its center marker lies outside the 36-pixel aid");
        sideTarget.Body.Teleport(new Vec3d(1e9,10000,0));
        var selected=g.InspectTarget;
        target.Body.Teleport(new Vec3d(1e9,0,0)); g.World.Sensors.Update(g.World.Ships,g.World.Time,force:true); Step(g,1);
        Key(g,Godot.Key.R);
        Check(g.InspectTarget==selected && g.LastFireFailed,"Scope cannot silently switch to an undetected or off-reticle target");
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
