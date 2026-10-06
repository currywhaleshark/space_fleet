using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class ScaleTest
{
    private RadialMenu _radial = null!;
    private string? _radialAction;
    private bool _shotRadialReleased;
    public bool MenuOpen => _radial is { IsOpen: true };

    private bool HandleRadialInput(InputEvent e)
    {
        if (MenuOpen)
        {
            if (e.IsActionPressed(InputSetup.ReleaseMouse)) _radial.Cancel();
            else if (_radialAction is not null && e.IsActionReleased(_radialAction)) _radial.Release();
            else if (e is InputEventMouseMotion motion)
            {
                if (Scheme == ControlScheme.Pilot) _radial.FeedMotion(motion.Relative);
                else _radial.SetPointer(motion.Position);
            }
            if (!MenuOpen) _radialAction = null;
            GetViewport().SetInputAsHandled();
            return true;
        }
        if (e.IsActionPressed(InputSetup.PowerMenu))
        {
            OpenPowerMenu();
            GetViewport().SetInputAsHandled();
            return true;
        }
        return false;
    }

    private void OpenPowerMenu()
    {
        if (Controlled?.Body is not { } ship) return;
        OpenMenu(InputSetup.PowerMenu, "전력", PowerPresets.All.Select(p => new RadialItem(p.Label, p.AngleDeg, () =>
        {
            p.Apply(ship.Power);
            Notify($"전력 · {p.Label}", false);
        })).ToArray());
    }

    private void OpenMenu(string action, string title, RadialItem[] items)
    {
        if (MenuOpen) return;
        _radialAction = action;
        Camera.FreeLooking = false;
        _radial.Open(title, items, Scheme == ControlScheme.Pilot || _shot is not null
            ? GetViewport().GetVisibleRect().Size * 0.5f : GetViewport().GetMousePosition(), Scheme == ControlScheme.Pilot);
    }

    private void SetupShotRadial()
    {
        if (_shot?.Radial != "power") return;
        _UnhandledInput(new InputEventAction { Action = InputSetup.PowerMenu, Pressed = true });
        if (_shot.RadialDirection is float direction)
        {
            Vector2 offset = new Vector2(Mathf.Sin(Mathf.DegToRad(direction)), -Mathf.Cos(Mathf.DegToRad(direction))) * 100;
            if (Scheme == ControlScheme.Pilot) _radial.FeedMotion(offset);
            else _radial.SetPointer(_radial.Center + offset);
        }
    }

    private void StepShotRadial()
    {
        if (_shot is not { RadialRelease: true } || _shotRadialReleased || _frame < _shot.RadialHoldFrames) return;
        _shotRadialReleased = true;
        if (_radialAction is { } action) _UnhandledInput(new InputEventAction { Action = action, Pressed = false });
        if (Controlled?.Body.Power is { } power)
            GD.Print($"radial released: pips={string.Join('/', Enum.GetValues<PowerChannel>().Select(power.Pips))}");
    }
}
