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
        if (e.IsActionPressed(InputSetup.GunneryMenu) && Scheme == ControlScheme.Helm)
        {
            OpenGunneryMenu();
            GetViewport().SetInputAsHandled();
            return true;
        }
        if (e.IsActionPressed(InputSetup.SquadMenu))
        {
            OpenSquadMenu();
            GetViewport().SetInputAsHandled();
            return true;
        }
        if (e.IsActionPressed(InputSetup.DroneMenu))
        {
            OpenDroneMenu();
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
        Camera.SetFreeLook(false);
        Camera.ResetTelescope();
        _fireReleaseGuard=true;
        _radial.Open(title, items, Scheme == ControlScheme.Pilot || _shot is not null
            ? GetViewport().GetVisibleRect().Size * 0.5f : GetViewport().GetMousePosition(), Scheme == ControlScheme.Pilot);
    }

    private bool HasSelectedEnemy => InspectTarget is { } target && Controlled is { } me
        && target.Body.Faction != me.Body.Faction && !target.Body.Damage.Destroyed && Known(target);

    private void OpenGunneryMenu() => OpenMenu(InputSetup.GunneryMenu, "사격", new[]
    {
        new RadialItem("무력화", 0, () => SetDoctrine(FireDoctrine.Disable)),
        new RadialItem("집중", 90, () => { SelectEnemy(InspectTarget!); SetDoctrine(FireDoctrine.Focus); }, () => HasSelectedEnemy),
        new RadialItem("자유", 180, () => SetDoctrine(FireDoctrine.Free)),
        new RadialItem("중지", 270, () => SetDoctrine(FireDoctrine.Hold)),
        new RadialItem("수동", 225, () => SetDoctrine(FireDoctrine.Manual)),
        new RadialItem("일제사격", 45, Volley, () => HasSelectedEnemy),
    });

    private void Volley()
    {
        if (!HasSelectedEnemy || Controlled is not { } me || InspectTarget is not { } target) return;
        SelectEnemy(target);
        FireAttempt missile = World.LaunchMissile(me.Body, target.Body);
        ModuleState? module = target.Body.Damage.Modules.FirstOrDefault(m => !m.Destroyed && m.Definition.Id == Gunnery?.PriorityModuleId)
            ?? Subsystems.Pick(target.Body, AimPart, me.Body.Position);
        bool rail = World.TryAutoFire(me.Body, target.Body, module?.Definition.Center, double.PositiveInfinity, out GunneryStatus status);
        Notify(rail || missile.Fired ? "일제사격" : GunneryLabels.Status(status), !rail && !missile.Fired);
    }

    private void OpenSquadMenu() => OpenMenu(InputSetup.SquadMenu, "편대", new[]
    {
        new RadialItem("호위", 270, () => IssueSquadCommand(SquadCommand.Escort)),
        new RadialItem("요격", 0, () => IssueSquadCommand(SquadCommand.Intercept)),
        new RadialItem("집중공격", 90, () => IssueSquadCommand(SquadCommand.Focus), () => HasSelectedEnemy),
        new RadialItem("복귀", 180, () => IssueSquadCommand(SquadCommand.Return)),
        new RadialItem("위치유지", 135, () => IssueSquadCommand(SquadCommand.Hold)),
    });

    private void OpenDroneMenu()
    {
        if (Controlled?.Body is not { } ship) return;
        if (ship.Definition.DefenseDrones is null) { Notify("방어 드론 미탑재",true); return; }
        var drones=ship.Ordnance.Drones;
        RadialItem Item(DroneSector sector,float angle) => new(DefenseDroneState.Label(sector),angle,() =>
        {
            if (drones.Assign(sector)) Notify($"드론 · {DefenseDroneState.Label(sector)} 방어",false);
        },() => drones.Active);
        OpenMenu(InputSetup.DroneMenu,"드론",new[]
        {
            Item(DroneSector.Fore,0), Item(DroneSector.Starboard,90),
            Item(DroneSector.Aft,180), Item(DroneSector.Port,270), Item(DroneSector.AllAround,225),
        });
    }

    private void SetupShotRadial()
    {
        if (_shot is null) return;
        string? action = _shot.Radial switch
        { "power" => InputSetup.PowerMenu, "gunnery" or "fire" => InputSetup.GunneryMenu, "squad" => InputSetup.SquadMenu,
            "drone" => InputSetup.DroneMenu, _ => null };
        if (action is null) return;
        _UnhandledInput(new InputEventAction { Action = action, Pressed = true });
        if (!MenuOpen) return;
        if (_shot.RadialDirection is float direction)
        {
            Vector2 offset = new Vector2(Mathf.Sin(Mathf.DegToRad(direction)), -Mathf.Cos(Mathf.DegToRad(direction))) * 100;
            if (Scheme == ControlScheme.Pilot) _radial.FeedMotion(offset);
            else _radial.SetPointer(_radial.Center + offset);
        }
    }

    private void StepShotRadial()
    {
        if (_shot is not { RadialRelease: true } || _shotRadialReleased || _frame + 1 < _shot.RadialHoldFrames) return;
        _shotRadialReleased = true;
        if (_radialAction is { } action) _UnhandledInput(new InputEventAction { Action = action, Pressed = false });
        if (Controlled?.Body.Power is { } power)
            GD.Print($"radial released: pips={string.Join('/', Enum.GetValues<PowerChannel>().Select(power.Pips))}");
        GD.Print($"radial result: doctrine={Gunnery?.Doctrine}, squad={SquadOrder}, target={SquadTarget?.Callsign}, drones={Controlled?.Body.Ordnance.Drones.Sector}");
    }
}
