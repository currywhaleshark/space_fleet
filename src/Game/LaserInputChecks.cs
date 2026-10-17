using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class WeaponInputChecks
{
    private void CheckLaserInput()
    {
        InputSetup.Register();
        var fit=FleetLoadouts.Default("interceptor");
        fit=new(fit.Members.Select(m=>m with{Components=new(MainGun:"main-laser")}).ToArray());
        var lineup=new MatchLineup(new[]{
            new MatchParticipant(1,Faction.Blue,"interceptor",true,FleetLoadouts.Resolve(fit)),
            new MatchParticipant(1,Faction.Red,"escort",false)});
        var game=new ScaleTest{LaunchControl="B01-1",LaunchConfig=new BattleConfig{Match=lineup}};
        AddChild(game);game.SetProcess(false);game.SetPhysicsProcess(false);
        foreach(var body in game.World.Ships)game.World.DetachBrain(body);
        var ship=game.Controlled!.Body;var laser=ship.Railgun!.Laser??throw new System.InvalidOperationException("Laser fit not loaded");
        Check(ship.Railgun.IsLaser,"Saved laser fit reaches the actual player ship");
        Input.MouseMode=Input.MouseModeEnum.Captured;
        void Release(){Input.ActionRelease(InputSetup.Fire);Mouse(game,MouseButton.Left,false);}
        void Start(){Release();Input.ActionPress(InputSetup.Fire);Tick(game,12);Check(laser.Active,"Fresh held click starts emission");}
        Start();ulong pulses=laser.Pulses;Release();
        Check(!laser.Active,"Release event immediately hides the beam");
        Tick(game,8);Check(laser.Pulses==pulses,"Release event prevents subsequent damage pulses");
        Start();KeyEvent(game,Key.Key2);Tick(game,8);
        Check(!laser.Active,"Selecting missiles stops continuous primary fire");
        KeyEvent(game,Key.Key1);pulses=laser.Pulses;Tick(game,8);
        Check(laser.Pulses==pulses,"Held input across weapon switch cannot restart laser");
        Start();KeyEvent(game,Key.F);Tick(game,8);
        Check(game.MenuOpen&&!laser.Active,"Radial capture interrupts laser emission");
        KeyEvent(game,Key.Escape);Release();
        Start();KeyEvent(game,Key.M);Tick(game,8);
        Check(game.WorldMapOpen&&!laser.Active,"Map input cannot sustain a laser");
        KeyEvent(game,Key.M);Release();game._Process(.016);
        Start();game.Paused=true;Check(!laser.Active,"Pause clears held emission without advancing simulation");
        game.Paused=false;Tick(game,8);Check(!laser.Active,"Unpausing requires a fresh fire press");
        Start();game._Notification((int)NotificationApplicationFocusOut);Tick(game,8);
        Check(!laser.Active,"Losing application focus cannot leave a live beam");
        Start();KeyEvent(game,Key.F9);Check(!laser.Active,"Control handoff stops previous manual channel");
        Release();game.Free();
    }
}
