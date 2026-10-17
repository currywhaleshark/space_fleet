using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class Main
{
    private int _hangarFrame,_hangarChecks;
    private string[] _hangarSnapshot=Array.Empty<string>();
    private string _confirmationUnchanged="";
    private void HangarVerify(bool ok,string why)
    {if(!ok){GetTree().Quit(1);throw new InvalidOperationException(why);}_hangarChecks++;}
    private void CheckHangarFlow()
    {
        int frame=++_hangarFrame;
        if(frame==1)
        {
            OpenHangar();ChooseHull("battleship");Hangar.Research!.Points[DesignFamily.Earth]=100000;
            var view=_overlay.HangarView!;view.SelectSystem(ComponentSlot.MainGun);view.InstallSystem("main-triple",true);
            _confirmationUnchanged=HangarStorage.Serialize(Hangar);
        }
        if(frame==2)
        {
            var view=_overlay.HangarView!;
            var behind=view.GetChildren().OfType<Button>().Single(b=>b.Text=="메인으로").GetGlobalRect().GetCenter();
            Input.ParseInputEvent(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=behind,GlobalPosition=behind});
            Input.ParseInputEvent(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=behind,GlobalPosition=behind});
        }
        if(frame==3)
        {
            HangarVerify(Screen==BattleScreen.Hangar&&MenuConfirmation.Pending(_overlay.HangarView!) is not null,
                "Modal blocks real pointer clicks on underlying navigation");
            Input.ParseInputEvent(new InputEventKey{Keycode=Key.Tab,PhysicalKeycode=Key.Tab,Pressed=true});
            Input.ParseInputEvent(new InputEventKey{Keycode=Key.Tab,PhysicalKeycode=Key.Tab,Pressed=false});
        }
        if(frame==4)
        {
            var dialog=MenuConfirmation.Pending(_overlay.HangarView!)!;
            HangarVerify(GetViewport().GuiGetFocusOwner()==dialog.ConfirmButton,"Tab moves from cancel to the confirm action");
            Input.ParseInputEvent(new InputEventKey{Keycode=Key.Tab,PhysicalKeycode=Key.Tab,Pressed=true});
            Input.ParseInputEvent(new InputEventKey{Keycode=Key.Tab,PhysicalKeycode=Key.Tab,Pressed=false});
        }
        if(frame==5)
        {
            HangarVerify(GetViewport().GuiGetFocusOwner()==MenuConfirmation.Pending(_overlay.HangarView!)!.CancelButton,
                "Keyboard focus stays inside the modal");
            Input.ParseInputEvent(new InputEventKey{Keycode=Key.Escape,PhysicalKeycode=Key.Escape,Pressed=true});
            Input.ParseInputEvent(new InputEventKey{Keycode=Key.Escape,PhysicalKeycode=Key.Escape,Pressed=false});
        }
        if(frame==6)
        {
            HangarVerify(Screen==BattleScreen.Hangar&&MenuConfirmation.Pending(_overlay.HangarView!) is null
                &&HangarStorage.Serialize(Hangar)==_confirmationUnchanged,"Real Escape input cancels without passing through to menu navigation");
            Hangar.Research!.Points[DesignFamily.Earth]=0;ReturnToPort();
        }
        if(frame==12)
        {
            _overlay.ActivateButton(0);
            HangarVerify(Screen==BattleScreen.Hangar&&Input.MouseMode==Input.MouseModeEnum.Visible,"Hangar opens with free pointer");
            ChooseHull("battleship");var view=_overlay.HangarView!;
            HangarVerify(view.CurrentFit.Definition.Id=="battleship","Hull selector loads selected hull");
            Button Find(string key)=>view.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.GetMeta("focus_key","").AsString()==key);
            var unavailable=Find("component-main-triple");
            HangarVerify(unavailable.Disabled&&unavailable.TooltipText.Contains("RP 부족"),"Unaffordable fit keeps the precise RP shortfall in its tooltip");
            HangarVerify(Find("squad-0") is MenuItemButton {Picture:not null,Portrait:true},"Squad member is an image-led ship button");
            HangarVerify(unavailable is MenuItemButton {Picture:not null}&&unavailable.TooltipText.Contains("현재 → 교체 후"),"Equipment icons retain actual fit comparisons in details");
            var detail=(MenuItemButton)Find("system-MainGun");detail.GrabFocus();
            detail._GuiInput(new InputEventKey{Keycode=Key.F1,Pressed=true});
            var popup=detail.GetChildren().OfType<PopupPanel>().Single();
            HangarVerify(popup.Visible,"F1 opens keyboard-accessible equipment details");
            popup.EmitSignal(Window.SignalName.WindowInput,new InputEventKey{Keycode=Key.Escape,Pressed=true});
            HangarVerify(!popup.Visible&&Screen==BattleScreen.Hangar,"Escape closes details without leaving fitting");
            var preset=Find("프리셋 2");preset.GrabFocus();preset.EmitSignal(BaseButton.SignalName.Pressed);
            HangarVerify(view.CurrentFit.Definition.Components is null&&Hangar.Hulls["battleship"].Squadron[0].Preset==1
                &&view.GetViewport().GuiGetFocusOwner()==Find("프리셋 2"),"Preset action rebuild preserves keyboard focus and switches the fit");
            Find("프리셋 1").EmitSignal(BaseButton.SignalName.Pressed);
            Hangar.Research!.Points[DesignFamily.Earth]=100000;
            view.SelectSystem(ComponentSlot.MainGun);
            string unchanged=HangarStorage.Serialize(Hangar);
            Find("component-main-triple").GrabFocus();view.InstallSystem("main-triple",true);
            var dialog=MenuConfirmation.Pending(view)!;
            HangarVerify(dialog is not null&&dialog.Body.Contains("2 → 3연장")&&dialog.Body.Contains("기함")
                &&GetViewport().GuiGetFocusOwner()==dialog.CancelButton,"Confirmation previews the actual change and defaults to cancel");
            HangarVerify(HangarStorage.Serialize(Hangar)==unchanged,"Opening confirmation does not spend RP, unlock or equip");
            view.InstallSystem("main-single",true);
            HangarVerify(MenuConfirmation.Pending(view)==dialog,"Repeated item activation cannot replace the pending edit");
            _UnhandledInput(new InputEventKey{PhysicalKeycode=Key.Escape,Pressed=true});
            HangarVerify(MenuConfirmation.Pending(view) is null&&Screen==BattleScreen.Hangar&&HangarStorage.Serialize(Hangar)==unchanged
                &&GetViewport().GuiGetFocusOwner()==Find("component-main-triple"),"Escape cancels without leaving hangar or modifying the save and restores focus");
            view.InstallSystem("main-triple",true);Hangar.Research.Points[DesignFamily.Earth]=0;
            MenuConfirmation.Pending(view)!.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
            HangarVerify(!Hangar.Research.HasComponent("battleship","main-triple")&&view.CurrentFit.Definition.Components is null
                &&Hangar.Research.Points[DesignFamily.Earth]==0,"Confirmation revalidates affordability before unlocking or fitting");
            Hangar.Research.Points[DesignFamily.Earth]=100000;
            foreach(var part in new[]{"main-triple","pd-longrange","engine-vector","sensor-lowprofile","generator-output","shield-recharge"})
            {
                view.SelectSystem(ShipComponents.ById(part).Slot);view.InstallSystem(part,true);
                long balance=Hangar.Research.Points[DesignFamily.Earth],cost=Hangar.Research.ComponentCost("battleship",part);
                var confirm=MenuConfirmation.Pending(view)!;confirm.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
                confirm.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
                HangarVerify(Hangar.Research.Points[DesignFamily.Earth]==balance-cost,"Repeated confirmation charges exactly once: "+part);
                HangarVerify(view.CurrentFit.Definition.Components!.Ids.Contains(part),"Hangar researches and fits "+part);
            }
            view.SelectSystem(ComponentSlot.MainGun);view.InstallSystem(null);
            HangarVerify(MenuConfirmation.Pending(view) is not null,"Restoring a stock component also asks for confirmation");
            MenuConfirmation.Pending(view)!.CancelButton.EmitSignal(BaseButton.SignalName.Pressed);
            HangarVerify(view.CurrentFit.Definition.Components!.MainGun=="main-triple","Cancel keeps the installed component");
            view.SelectSystem(ComponentSlot.MainGun);
            HangarVerify(Find("component-main-single").TooltipText.Contains("3 → 1연장"),"Candidate comparison uses the installed triple gun rather than the stock twin gun");
            view.SelectPreset(1);HangarVerify(view.CurrentFit.Definition.Components is null,"Other system presets stay stock");
            view.SelectPreset(0);HangarVerify(view.CurrentFit.Definition.Components!.Ids.Count()==6,"All six systems return with their preset");
            HangarVerify(view.Preview.PickBay(view.Preview.BayScreen("utility-port")) is null,"Exterior preview has no invisible bay click targets");
            view.SelectBay("utility-port");
            var preview=view.Preview;var point=preview.BayScreen("utility-port");
            HangarVerify(preview.PickBay(point)=="utility-port","Bay marker projects the physical damage position");
            preview._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=point});
            preview._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=point});
            HangarVerify(view.SelectedBay=="utility-port","Clicking the projected bay selects fitting target");
            view.Install("shield-capacity");
            unchanged=HangarStorage.Serialize(Hangar);
            MenuConfirmation.Pending(view)!.CancelButton.EmitSignal(BaseButton.SignalName.Pressed);
            HangarVerify(HangarStorage.Serialize(Hangar)==unchanged,"Cancelling bay equipment preserves every preset and RP");
            view.Install("shield-capacity");MenuConfirmation.Pending(view)!.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
            HangarVerify(view.CurrentFit.Definition.InstalledEquipment.Length==1,"Equipment choice resolves preview");
            view.Install(null);MenuConfirmation.Pending(view)!.Cancel();
            HangarVerify(view.CurrentFit.Definition.InstalledEquipment.Length==1,"Cancelling bay removal preserves its equipment");
            view.SelectPreset(1);HangarVerify(view.CurrentFit.Definition.InstalledEquipment.Length==0,"Other presets remain independent");
            view.SelectPreset(0);HangarVerify(view.CurrentFit.Definition.InstalledEquipment.Length==1,"Returning to preset restores fit");
            view.SelectSlot(1);view.ChangeMember("earth_gun_cruiser");view.SelectPreset(1);
            view.SelectBay("utility-starboard");view.Install("sensor-array");
            MenuConfirmation.Pending(view)!.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
            HangarVerify(view.CurrentFit.Definition.Id=="earth_gun_cruiser"&&view.CurrentFit.Definition.InstalledEquipment.Length==1,"Escort slot keeps variant and independent fit");
            view.SelectSlot(2);view.ChangeMember("earth_air_defense_cruiser");
            view.SelectSystem(ComponentSlot.MainGun);
            HangarVerify(view.CurrentFit.Definition.Battery!.Weapon.Laser is not null&&view.CurrentFit.Definition.Components?.MainGun is null,"AA factory laser is equipped without buying a part");
            HangarVerify(((MenuItemButton)Find("component-stock")).Caption=="기본 레이저"&&((MenuItemButton)Find("component-stock")).Detail=="장착 중","Factory laser has the real stock label and installed state");
            // A previously purchased explicit laser remains a valid save selection.
            Hangar.SetComponent("earth_air_defense_cruiser",0,ComponentSlot.MainGun,"main-laser",true);
            view.SelectSystem(ComponentSlot.MainGun);
            HangarVerify(((MenuItemButton)Find("component-stock")).Detail=="장착 중","Old explicit laser selection still displays the equipped factory weapon");
            view.SelectBay("utility-port");view.Install("shield-capacity");
            MenuConfirmation.Pending(view)!.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
            preview=view.Preview;float yaw=preview.Yaw;
            preview._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=new(400,200)});
            preview._GuiInput(new InputEventMouseMotion{Position=new(470,200),Relative=new(70,0)});
            preview._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=new(470,200)});
            HangarVerify(preview.Yaw!=yaw,"Preview drag orbits without steering a ship");
            _UnhandledInput(new InputEventKey{PhysicalKeycode=Key.Escape,Pressed=true});
            HangarVerify(Screen==BattleScreen.Title,"Escape returns from hangar to main menu");OpenHangar();
            BeginMatch(true);
            _hangarSnapshot=PendingMatch!.Player.Loadout!.Members.Select(m=>m.ContentHash).ToArray();
            Hangar.SetEquipment("battleship",0,"utility-port",null);
            Hangar.SetComponent("battleship",0,ComponentSlot.MainGun,null);
            HangarVerify(PendingMatch.Player.Loadout.Members[0].Definition.InstalledEquipment.Length==1,"Queue freezes a snapshot independent of later edits");
            HangarVerify(PendingMatch.Player.Loadout.Leader.Components?.MainGun=="main-triple","Queue freezes the primary selection");
        }
        if(frame==120)
        {
            HangarVerify(Screen==BattleScreen.Battle&&Battle is not null,"Hangar launch reaches battle");
            var game=Battle!;var members=game.OwnedSquadron!.Members;
            HangarVerify(members[0].Railguns.All(g=>g.BarrelCount==3)&&members[0].Definition.Components!.Ids.Count()==6,
                "All six replacement systems survive actual battle spawn");
            HangarVerify(members.Count==3&&members[1].Definition.Id=="earth_gun_cruiser"&&members[2].Definition.Id=="earth_air_defense_cruiser","Mixed escort slots spawn in owned squad");
            for(int i=0;i<members.Count;i++)
            {
                HangarVerify(members[i].LoadoutId!.EndsWith(_hangarSnapshot[i]),"Spawn preserves exact frozen loadout identity");
                HangarVerify(members[i].Damage!.Modules.Any(m=>m.Definition.Id.StartsWith("fit-")),"Installed equipment has a live damage module");
            }
            var next=members[1];var module=next.Damage.Modules.First(m=>m.Definition.Id.StartsWith("fit-"));
            next.DamageControl.Priority=RepairPriority.Hold;next.Damage.Hurt(module,10,game.World.Time,0,1);float health=module.Health;
            members[0].Damage.Breakup(game.World.Time,"hangar check");MatchTicks(game,190);
            HangarVerify(game.Controlled!.Body==next&&next.LoadoutId!.EndsWith(_hangarSnapshot[1]),"Handoff selects configured escort with its own equipment");
            HangarVerify(module.Health==health,"Handoff does not restore damaged equipment");
            ReturnToPort();_overlay.ActivateButton(0);
            HangarVerify(Hangar.Hulls["battleship"].Squadron[1]==new HangarSlot("earth_gun_cruiser",1),"Returning to hangar retains squad and preset choices");
            GD.Print($"PASS: {_hangarChecks} hangar/preview/launch/handoff flow checks");GetTree().Quit();
        }
    }
}
