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
        if(weapon!=SelectedWeapon) ship.Ordnance.Antimatter.Cancel();
        SelectedWeapon=weapon;
        // A held click must not become a shot from the newly selected weapon.
        _fireReleaseGuard=Input.IsActionPressed(InputSetup.Fire) || Input.IsMouseButtonPressed(MouseButton.Left);
        if(weapon==PlayerWeapon.Antimatter)
        {
            if(ship.Ordnance.Antimatter.Mode==AntimatterMode.Safe) ship.Ordnance.Antimatter.BeginArming();
            Notify("3 반물질 강습어뢰 · "+ship.Ordnance.Antimatter.Status,false);
        }
        else Notify(weapon==PlayerWeapon.MainGun ? "1 주포 선택" : "2 미사일 선택",false);
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
        if(SelectedWeapon!=PlayerWeapon.MainGun) FireSelectedWeapon();
        else if(Scheme==ControlScheme.Helm && Gunnery?.Doctrine!=FireDoctrine.Manual && e is InputEventMouseButton mouse)
        {
            SelectAt(mouse.Position);
            FireSelectedMainBattery();
        }
        return true;
    }

    private void FireSelectedMainBattery()
    {
        if(Controlled?.Body is not { } ship || FireTarget?.Body is not { } target) return;
        bool fired=World.TryAutoFire(ship,target,AimModule?.Definition.Center,double.PositiveInfinity,out var status);
        if(fired) Notify("주포 발사",false);
        else Notify(GunneryLabels.Status(status),true);
    }

    public override void _Notification(int what)
    {
        if(what!=NotificationApplicationFocusOut) return;
        Camera?.ResetTelescope(); _fireReleaseGuard=true; JettisonProgress=0;
    }
}

public partial class Hud
{
    private void DrawWeaponSelection(ShipBody ship,Vector2 screen)
    {
        var start=new Vector2(screen.X*.5f-195,screen.Y-185);
        string[] labels={ $"1 주포 {ship.Railguns.Sum(g=>g.Rounds)}", $"2 미사일 {ship.Ordnance.Missiles}",
            ship.Definition.Antimatter is null ? "3 어뢰 —" : $"3 어뢰 {ship.Ordnance.Antimatter.Rounds}" };
        for(int i=0;i<labels.Length;i++)
        {
            var rect=new Rect2(start+new Vector2(i*132,0),new Vector2(126,25));
            bool selected=(int)Game.SelectedWeapon==i;
            DrawRect(rect,PanelBack); DrawRect(rect,selected ? Lead : Faint,false,selected ? 2 : 1);
            CenteredLabel(rect.Position+new Vector2(63,17),labels[i],12,selected ? Lead : Dim);
        }
        if(Game.Camera.TelescopeHeld)
            CenteredLabel(new Vector2(screen.X*.5f,28),"망원 ×4 · 우클릭을 놓으면 복귀",12,Text);
    }
}
