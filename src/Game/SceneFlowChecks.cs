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
        if(frame==14)
        {
            _overlay.ActivateButton(3);
            Verify(PlayerDesign==DesignFamily.Mars && EnemyDesign==DesignFamily.Mars,"Player design selector is independent of the opponent");
            _overlay.ActivateButton(4);
            Verify(PlayerDesign==DesignFamily.Mars && EnemyDesign==DesignFamily.Earth,"Opponent design selector supports Mars vs Earth");
        }
        if(frame==20){_overlay.ActivateButton(0);Verify(Screen==BattleScreen.Battle&&Battle!.Controlled!.Body.Callsign=="BB-01","BB role starts battle");}
        if(frame==30)
        {
            Verify(Battle!.Controlled!.Body.Definition.Id=="mars_battleship" && Battle.Controlled.Body.Faction==Faction.Blue,
                "Mars battleship starts on Blue via ID-based model/data loading");
            Verify(Battle.World.Ships.Where(s=>s.Faction==Faction.Red).All(s=>s.Definition.Design==DesignFamily.Earth),"Red can use Earth hulls");
            float range = Battle!.Radar.Range;
            Battle._UnhandledInput(new InputEventAction {Action=InputSetup.RadarNear,Pressed=true});
            Verify(Battle.Radar.Range<range,"Radar near shortcut changes tactical range");
            Battle._UnhandledInput(new InputEventMouseButton {ButtonIndex=MouseButton.WheelDown,Pressed=true,Position=new Vector2(80,80)});
            Verify(Battle.Radar.Range==range,"Wheel over radar changes map range");
            Battle._UnhandledInput(new InputEventMouseButton {ButtonIndex=MouseButton.Middle,Pressed=true});
            Battle._UnhandledInput(new InputEventMouseButton {ButtonIndex=MouseButton.Middle,Pressed=false});
            Verify(!Battle.Camera.FreeLooking&&Battle.Camera.FreeLookHoldRemaining==3,"Middle release starts observation delay");
            Battle!._UnhandledInput(new InputEventAction {Action=InputSetup.PowerMenu,Pressed=true});
            Verify(Battle.MenuOpen,"Power radial opens in battle");
            var escape=new InputEventAction {Action=InputSetup.ReleaseMouse,Pressed=true};Battle._UnhandledInput(escape);
            Verify(!Battle.MenuOpen&&Screen==BattleScreen.Battle,"Radial Esc cancels before pause");
            Battle._UnhandledInput(escape);
            Verify(Screen==BattleScreen.Pause&&Battle.Paused,"Helm Esc pauses");
            _overlay.ActivateButton(0);Verify(Screen==BattleScreen.Battle&&!Battle.Paused,"Continue resumes");
        }
        if(frame==34) Battle!.CheckContactSelection(Verify);
        if(frame==40)
        {
            int old=Seed;TogglePause();_overlay.ActivateButton(1);
            Verify(Screen==BattleScreen.Battle&&Battle!.Controlled!.Body.Callsign=="BB-01"&&Seed!=old,"Restart keeps role and changes seed");
        }
        if(frame==32)
        {
            Battle!._UnhandledInput(new InputEventMouseButton { ButtonIndex=MouseButton.Left,Pressed=true,Position=new(80,80) });
            Verify(Battle.WorldMapOpen && Input.MouseMode==Input.MouseModeEnum.Visible,"Clicking the radar globe opens the full map");
            var before=Battle.WorldMap.Center;
            double span=Battle.WorldMap.HalfSpan;
            float yaw=Battle.WorldMap.Yaw,pitch=Battle.WorldMap.Pitch;
            Battle._UnhandledInput(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true,Position=new(350,300)});
            Battle._UnhandledInput(new InputEventMouseMotion {Position=new(420,330),Relative=new(70,30)});
            Battle._UnhandledInput(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=false,Position=new(420,330)});
            Verify(Battle.WorldMap.Center==before && Battle.WorldMap.HalfSpan==span
                && Battle.WorldMap.Yaw!=yaw && Battle.WorldMap.Pitch!=pitch,"Left drag rotates the full map without moving its world center or scale");
            yaw=Battle.WorldMap.Yaw; pitch=Battle.WorldMap.Pitch;
            Battle._UnhandledInput(new InputEventMouseMotion {Position=new(440,340),Relative=new(20,10)});
            Verify(Battle.WorldMap.Yaw==yaw && Battle.WorldMap.Pitch==pitch,"Releasing left drag stops map rotation");
            Battle._UnhandledInput(new InputEventMouseButton {ButtonIndex=MouseButton.Middle,Pressed=true,Position=new(350,300)});
            Battle._UnhandledInput(new InputEventMouseMotion {Position=new(420,330),Relative=new(70,30)});
            Battle._UnhandledInput(new InputEventMouseButton {ButtonIndex=MouseButton.Middle,Pressed=false,Position=new(420,330)});
            Verify(Battle.WorldMap.Center!=before && Battle.WorldMap.Yaw==yaw && Battle.WorldMap.Pitch==pitch,
                "Middle drag pans without rotating the map or starting ship freelook");
            Battle._UnhandledInput(new InputEventMouseButton {ButtonIndex=MouseButton.WheelUp,Pressed=true,Position=new(420,330)});
            Verify(Battle.WorldMap.HalfSpan<span,"Full map wheel zoom works");
            Battle._UnhandledInput(new InputEventAction {Action=InputSetup.ReleaseMouse,Pressed=true});
            Verify(!Battle.WorldMapOpen && !Battle.Paused && Screen==BattleScreen.Battle,"Esc closes the map before pausing battle");
            before=Battle.WorldMap.Center; span=Battle.WorldMap.HalfSpan;
            Battle._UnhandledInput(new InputEventKey {PhysicalKeycode=Key.M,Pressed=true});
            Verify(Battle.WorldMapOpen && Battle.WorldMap.Center==before && Battle.WorldMap.HalfSpan==span
                && Battle.WorldMap.Yaw==yaw && Battle.WorldMap.Pitch==pitch,"M reopens at the remembered world position, zoom and rotation");
            Battle._UnhandledInput(new InputEventKey {PhysicalKeycode=Key.Home,Pressed=true});
            Verify(Battle.WorldMap.HalfSpan>span,"Home refits the whole battlefield");
            Battle._UnhandledInput(new InputEventKey {PhysicalKeycode=Key.M,Pressed=true});
            Verify(!Battle.WorldMapOpen && Input.MouseMode==Input.MouseModeEnum.Visible,"M closes the helm map with a visible cursor");
        }
        if(frame==50){SelectRole();_overlay.ActivateButton(1);Verify(Battle!.Controlled!.Body.Callsign=="DD-31"&&Battle.Scheme==ControlScheme.Helm,"DD role starts helm");}
        if(frame==60)
        {
            SelectRole();_overlay.ActivateButton(2);Verify(Battle!.Controlled!.Body.Callsign=="IC-21"&&Battle.Scheme==ControlScheme.Pilot,"IC role starts pilot");
            Battle._UnhandledInput(new InputEventKey {PhysicalKeycode=Key.M,Pressed=true});
            Verify(Battle.WorldMapOpen && Input.MouseMode==Input.MouseModeEnum.Visible,"Pilot map releases the captured mouse");
            Battle._UnhandledInput(new InputEventAction {Action=InputSetup.PowerMenu,Pressed=true});
            Verify(!Battle.MenuOpen,"Map blocks ship command menus");
            Input.ActionPress(InputSetup.Fire); Input.ActionPress(InputSetup.ThrottleUp); Input.ActionPress(InputSetup.RollRight);
            int rounds=Battle.Controlled.Body.Railgun!.Rounds; float throttle=Battle.Throttle;
            Battle._PhysicsProcess(1.0/60);
            Verify(Battle.Controlled.Body.Railgun.Rounds==rounds && Battle.Throttle==throttle && Battle.Controlled.Body.Control.Roll==0,
                "Map blocks held fire, throttle and roll while simulation runs");
            Battle._UnhandledInput(new InputEventKey {PhysicalKeycode=Key.M,Pressed=true});
            Battle._PhysicsProcess(1.0/60);
            Verify(!Battle.WorldMapOpen && Input.MouseMode==Input.MouseModeEnum.Captured && Battle.Controlled.Body.Railgun.Rounds==rounds,
                "Closing restores pilot capture without firing a held map click");
            Input.ActionRelease(InputSetup.Fire); Input.ActionRelease(InputSetup.ThrottleUp); Input.ActionRelease(InputSetup.RollRight);
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
            _overlay.ActivateButton(3);
            Verify(PlayerDesign==DesignFamily.Earth && EnemyDesign==DesignFamily.Earth,"UI supports Earth vs Earth");
            _overlay.ActivateButton(3); _overlay.ActivateButton(4);
            Verify(PlayerDesign==DesignFamily.Mars && EnemyDesign==DesignFamily.Mars,"UI supports Mars vs Mars");
            _overlay.ActivateButton(3);
            Verify(PlayerDesign==DesignFamily.Earth && EnemyDesign==DesignFamily.Mars,"UI supports Earth vs Mars");
        }
        if(frame==480){GD.Print($"PASS: {_flowChecks} scene flow checks");GetTree().Quit();}
    }
}
