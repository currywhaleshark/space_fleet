using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public enum PlayerWeapon { MainGun, Missile, Antimatter }

public partial class ScaleTest
{
    public PlayerWeapon SelectedWeapon { get; private set; } = PlayerWeapon.MainGun;
    private bool _fireReleaseGuard;

    internal void SelectWeapon(PlayerWeapon weapon)
    {
        if(Controlled?.Body is not { } ship) return;
        if(weapon==PlayerWeapon.Antimatter && ship.Definition.Antimatter is null)
        { Notify("반물질 강습어뢰 미탑재",true); return; }
        if(weapon==PlayerWeapon.Missile && ship.Definition.Missiles is null)
        { Notify("미사일 미탑재",true); return; }
        if(weapon!=SelectedWeapon) { ship.Ordnance.Antimatter.Cancel(); SimWorld.StopLasers(ship); }
        SelectedWeapon=weapon;
        // A held click must not become a shot from the newly selected weapon.
        _fireReleaseGuard=Input.IsActionPressed(InputSetup.Fire) || Input.IsMouseButtonPressed(MouseButton.Left);
        if(weapon==PlayerWeapon.Antimatter)
        {
            if(ship.Ordnance.Antimatter.Mode==AntimatterMode.Safe) ship.Ordnance.Antimatter.BeginArming();
            Notify(InputBindings.Text(InputSetup.SelectAntimatter)+" 반물질 강습어뢰 · "+ship.Ordnance.Antimatter.Status,false);
        }
        else Notify(weapon==PlayerWeapon.MainGun ? $"{InputBindings.Text(InputSetup.SelectMainGun)} 주포 선택" : $"{InputBindings.Text(InputSetup.SelectMissile)} 미사일 선택",false);
    }

    private bool HandleWeaponMouse(InputEvent e)
    {
        if(e.IsActionPressed(InputSetup.Telescope))
        {
            if(Scheme==ControlScheme.Helm || Input.MouseMode==Input.MouseModeEnum.Captured) Camera.SetTelescope(true);
            return true;
        }
        if(!e.IsActionPressed(InputSetup.Fire)) return false;
        if(Scheme==ControlScheme.Pilot && Input.MouseMode!=Input.MouseModeEnum.Captured)
        { Input.MouseMode=Input.MouseModeEnum.Captured; _fireReleaseGuard=true; return true; }
        if(_fireReleaseGuard) return true;
        if(Camera.TelescopeHeld && ScopeHullBlocked) { Notify("선체에 시야 가림",true); return true; }
        if(SelectedWeapon!=PlayerWeapon.MainGun) FireSelectedWeapon();
        else if(SelectedSmallContact is null && Scheme==ControlScheme.Helm && Gunnery?.Doctrine!=FireDoctrine.Manual && e is InputEventMouseButton mouse)
        {
            // Selecting a bracket or module owns this entire click, including its held phase.
            // In particular it must never fire using the previous tick's engaged target.
            if(SelectAt(mouse.Position))
            {
                _fireReleaseGuard=true;
                if(Controlled?.Body is { } ship) SimWorld.StopLasers(ship);
            }
            else FireSelectedMainBattery();
        }
        return true;
    }

    private void FireSelectedMainBattery()
    {
        if(Controlled?.Body is not { } ship || FireTarget?.Body is not { } target) return;
        bool fired=World.TryAutoFire(ship,target,AimLocalPoint,SimWorld.GunneryFlightLimit(ship),out var status);
        if(fired) Notify("주포 발사",false);
        else Notify(GunneryLabels.Status(status),true);
    }

    public override void _Notification(int what)
    {
        if(what!=NotificationApplicationFocusOut) return;
        if (ShowHelp) SetHelp(false);
        if(Controlled?.Body is { } ship) SimWorld.StopLasers(ship);
        CloseRoster();
        Camera?.ResetTelescope(); _selectionPointer=null; _fireReleaseGuard=true; JettisonProgress=0;
        if (MenuOpen) { _radial.Cancel(); _radialAction=null; }
    }
}

public partial class Hud
{
    private void DrawWeaponSelection(ShipBody ship,Vector2 screen)
    {
        var layout=CombatHudLayout.For(screen,Game.Scheme==ControlScheme.Helm,ship.Railguns.Length, HudSettings.TextScale);
        DrawSelectedTargetReadout(layout);
        var start=layout.Weapons;
        string[] labels={ ship.Railgun?.IsLaser == true ? $"{InputBindings.Text(InputSetup.SelectMainGun)} 레이저 ∞" : $"{InputBindings.Text(InputSetup.SelectMainGun)} 주포 {ship.Railguns.Sum(g=>g.Rounds)}", $"{InputBindings.Text(InputSetup.SelectMissile)} 미사일 {ship.Ordnance.Missiles}",
            ship.Definition.Antimatter is null ? $"{InputBindings.Text(InputSetup.SelectAntimatter)} 어뢰 —" : $"{InputBindings.Text(InputSetup.SelectAntimatter)} 어뢰 {ship.Ordnance.Antimatter.Rounds}" };
        for(int i=0;i<labels.Length;i++)
        {
            var rect=new Rect2(start+new Vector2(i*132,0),new Vector2(126,31));
            bool selected=(int)Game.SelectedWeapon==i;
            DrawRect(rect,PanelBack); DrawRect(rect,selected ? Lead : Faint,false,selected ? 2 : 1);
            if(selected)DrawRect(new(rect.Position+new Vector2(0,27),new Vector2(126,4)),Lead);
            CenteredLabel(rect.Position+new Vector2(63,18),labels[i],15,selected ? Lead : Text);
        }
    }
}
