using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class Main
{
    private int _flowFrame,_flowChecks;
    private string? _lostShip;
    private void Verify(bool ok,string message)
    {
        if(!ok){GetTree().Quit(1);throw new InvalidOperationException(message);}
        _flowChecks++;
    }
    private void CheckSceneFlow()
    {
        int frame=++_flowFrame;
        if(frame==10){_overlay.ActivateButton(0);Verify(Screen==BattleScreen.Select,"Title button selects roles");}
        if(frame==20){_overlay.ActivateButton(0);Verify(Screen==BattleScreen.Battle&&Battle!.Controlled!.Body.Callsign=="BB-01","BB role starts battle");}
        if(frame==30)
        {
            Battle!._UnhandledInput(new InputEventAction {Action=InputSetup.PowerMenu,Pressed=true});
            Verify(Battle.MenuOpen,"Power radial opens in battle");
            var escape=new InputEventAction {Action=InputSetup.ReleaseMouse,Pressed=true};Battle._UnhandledInput(escape);
            Verify(!Battle.MenuOpen&&Screen==BattleScreen.Battle,"Radial Esc cancels before pause");
            Battle._UnhandledInput(escape);
            Verify(Screen==BattleScreen.Pause&&Battle.Paused,"Helm Esc pauses");
            _overlay.ActivateButton(0);Verify(Screen==BattleScreen.Battle&&!Battle.Paused,"Continue resumes");
        }
        if(frame==40)
        {
            int old=Seed;TogglePause();_overlay.ActivateButton(1);
            Verify(Screen==BattleScreen.Battle&&Battle!.Controlled!.Body.Callsign=="BB-01"&&Seed!=old,"Restart keeps role and changes seed");
        }
        if(frame==50){SelectRole();_overlay.ActivateButton(1);Verify(Battle!.Controlled!.Body.Callsign=="DD-31"&&Battle.Scheme==ControlScheme.Helm,"DD role starts helm");}
        if(frame==60)
        {
            SelectRole();_overlay.ActivateButton(2);Verify(Battle!.Controlled!.Body.Callsign=="IC-21"&&Battle.Scheme==ControlScheme.Pilot,"IC role starts pilot");
            Input.MouseMode=Input.MouseModeEnum.Captured;Battle._UnhandledInput(new InputEventAction{Action=InputSetup.ReleaseMouse,Pressed=true});
            Verify(Screen==BattleScreen.Battle&&Input.MouseMode==Input.MouseModeEnum.Visible,"First pilot Esc releases mouse");
            Battle._UnhandledInput(new InputEventAction{Action=InputSetup.ReleaseMouse,Pressed=true});Verify(Screen==BattleScreen.Pause,"Second Esc pauses");
            _overlay.ActivateButton(0);
            int tick=(int)Battle.World.Tick;Battle._UnhandledInput(new InputEventAction{Action=InputSetup.JumpFar,Pressed=true});
            Verify(Battle.FloatingOrigin&&Battle.Controlled.Body.Position.Length()<20_000,"Normal mode blocks development jump");
            Battle.SetBattleSpeed(16);Verify(Battle.TimeScale==1,"Normal mode excludes x16");
            Verify(Battle.PlayerSquadron!.PlayerLed&&Battle.World.BrainOf(Battle.Controlled.Body) is null,"Selected squad belongs to player");
        }
        if(frame==80){_lostShip=Battle!.Controlled!.Body.Callsign;Battle.Controlled.Body.Damage.Breakup(Battle.World.Time,"FLOW TEST");}
        if(frame==300)
        {
            Verify(Battle!.Controlled!.Body.Callsign!=_lostShip&&Battle.Controlled.Body.Class.Kind==HullKind.Interceptor,"Auto handoff after loss");
            Verify(Battle.ControlSegments.Count>=2&&Battle.ResultRecord.ModulesLost>0,"Ownership segments and received loss recorded");
            foreach(var ship in Battle.World.Ships.Where(s=>s.Faction==Faction.Red))ship.Damage.Breakup(Battle.World.Time,"FLOW TEST");
        }
        if(frame==450)
        {
            Verify(Screen==BattleScreen.Result&&Battle!.TimeScale==1,"Outcome opens result after delay at x1");
            _overlay.ActivateButton(1);Verify(Screen==BattleScreen.Select&&Battle is null,"Result can change role");
        }
        if(frame==480){GD.Print($"PASS: {_flowChecks} scene flow checks");GetTree().Quit();}
    }
}
