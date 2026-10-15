using Godot;

namespace SpaceFleet.Game;

public partial class ScaleTest
{
    public bool RosterOpen { get; private set; }
    public bool RosterEnabled { get; set; } = true;
    private Input.MouseModeEnum _rosterPreviousMouse;

    // Tab must be seen before GUI focus traversal, including while the map is open.
    public override void _Input(InputEvent e) { HandleRosterInput(e); }
    private bool HandleRosterInput(InputEvent e)
    {
        if(e.IsActionReleased(InputSetup.TeamRoster))
        {CloseRoster();GetViewport().SetInputAsHandled();return true;}
        if(!e.IsActionPressed(InputSetup.TeamRoster))return false;
        if(RosterEnabled&&!Paused&&!RosterOpen)
        {
            if(MenuOpen){_radial.Cancel();_radialAction=null;}
            _worldMap.CancelInteraction();
            _rosterPreviousMouse=Input.MouseMode;
            RosterOpen=true;Camera.SetFreeLook(false);Camera.ResetTelescope();JettisonProgress=0;
            _fireReleaseGuard=true;Input.MouseMode=Input.MouseModeEnum.Visible;
        }
        GetViewport().SetInputAsHandled();return true;
    }
    public void CloseRoster()
    {
        if(!RosterOpen)return;
        RosterOpen=false;_fireReleaseGuard=true;
        Input.MouseMode=!Paused&&!Spectating&&!WorldMapOpen&&Scheme==ControlScheme.Pilot
            ? _rosterPreviousMouse : Input.MouseModeEnum.Visible;
    }
    private void HandleSpectatorInput(InputEvent e)
    {
        if(e.IsActionPressed(InputSetup.ReleaseMouse))PauseRequested?.Invoke();
        else if(e.IsActionPressed(InputSetup.SpectatePrevious))CycleSpectator(-1);
        else if(e.IsActionPressed(InputSetup.SpectateNext))CycleSpectator(1);
        else if(e is InputEventMouseButton {ButtonIndex:MouseButton.Middle} button)Camera.SetFreeLook(button.Pressed);
        else if(e is InputEventMouseMotion motion && Camera.FreeLooking)Camera.AddMouse(motion.Relative);
        else if(e is InputEventMouseButton {Pressed:true,ButtonIndex:MouseButton.WheelUp})Camera.Zoom(.9f);
        else if(e is InputEventMouseButton {Pressed:true,ButtonIndex:MouseButton.WheelDown})Camera.Zoom(1.1f);
    }
}
