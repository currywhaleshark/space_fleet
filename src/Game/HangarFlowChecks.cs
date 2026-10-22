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
            Hangar.Research.Upgrades["battleship"]=new[]{"fire-control"};
            var view=_overlay.HangarView!;view.SelectSystem(ComponentSlot.MainGun);view.InstallPart(0,"earth-rail-l3",true);
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
        if(frame==8){CheckHangarSquadEditing();CheckTacticalFitting();}
        if(frame==12)
        {
            _overlay.ActivateButton(0);
            HangarVerify(Screen==BattleScreen.Hangar&&Input.MouseMode==Input.MouseModeEnum.Visible,"Hangar opens with free pointer");
            ChooseHull("battleship");Hangar.Research!.Upgrades["battleship"]=new[]{"fire-control"};var view=_overlay.HangarView!;
            HangarVerify(view.CurrentFit.Definition.Id=="battleship","Hull selector loads selected hull");
            Button Find(string key)=>view.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.GetMeta("focus_key","").AsString()==key);
            view.SelectSystem(ComponentSlot.MainGun);
            var unavailable=Find("part-earth-rail-l3");
            HangarVerify(unavailable.Disabled&&unavailable.TooltipText.Contains("RP 부족"),"Unaffordable fit keeps the precise RP shortfall in its tooltip");
            HangarVerify(Find("squad-0") is MenuItemButton {Picture:not null,Portrait:true},"Squad member is an image-led ship button");
            HangarVerify(unavailable is MenuItemButton {Picture:not null}&&unavailable.TooltipText.Contains("장착 후"),"Equipment icons retain actual fit comparisons in details");
            var detail=(MenuItemButton)Find("part-stock");detail.GrabFocus();
            detail._GuiInput(new InputEventKey{Keycode=Key.F1,PhysicalKeycode=Key.F1,Pressed=true});
            var popup=detail.GetChildren().OfType<PopupPanel>().Single();
            HangarVerify(popup.Visible,"F1 opens keyboard-accessible equipment details");
            popup.EmitSignal(Window.SignalName.WindowInput,new InputEventKey{Keycode=Key.Escape,Pressed=true});
            HangarVerify(!popup.Visible&&Screen==BattleScreen.Hangar,"Escape closes details without leaving fitting");
            var preset=Find("프리셋 2");preset.GrabFocus();preset.EmitSignal(BaseButton.SignalName.Pressed);
            HangarVerify(view.CurrentFit.Definition.Components is null&&Hangar.Hulls["battleship"].Squadron[0]!.Preset==1
                &&view.GetViewport().GuiGetFocusOwner()==Find("프리셋 2"),"Preset action rebuild preserves keyboard focus and switches the fit");
            Find("프리셋 1").EmitSignal(BaseButton.SignalName.Pressed);
            Hangar.Research!.Points[DesignFamily.Earth]=100000;
            view.SelectSystem(ComponentSlot.MainGun);
            string unchanged=HangarStorage.Serialize(Hangar);
            Find("part-earth-rail-l3").GrabFocus();view.InstallPart(0,"earth-rail-l3",true);
            var dialog=MenuConfirmation.Pending(view)!;
            HangarVerify(dialog is not null&&dialog.Body.Contains("2연장")&&dialog.Body.Contains("3연장")&&dialog.Body.Contains("기함")
                &&GetViewport().GuiGetFocusOwner()==dialog.CancelButton,"Confirmation previews the actual change and defaults to cancel");
            HangarVerify(HangarStorage.Serialize(Hangar)==unchanged,"Opening confirmation does not spend RP, unlock or equip");
            view.InstallPart(0,null);
            HangarVerify(MenuConfirmation.Pending(view)==dialog,"Repeated item activation cannot replace the pending edit");
            _UnhandledInput(new InputEventKey{PhysicalKeycode=Key.Escape,Pressed=true});
            HangarVerify(MenuConfirmation.Pending(view) is null&&Screen==BattleScreen.Hangar&&HangarStorage.Serialize(Hangar)==unchanged
                &&GetViewport().GuiGetFocusOwner()==Find("part-earth-rail-l3"),"Escape cancels without leaving hangar or modifying the save and restores focus");
            view.InstallPart(0,"earth-rail-l3",true);Hangar.Research.Points[DesignFamily.Earth]=0;
            MenuConfirmation.Pending(view)!.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
            HangarVerify(!Hangar.Research.HasPart(DesignFamily.Earth,"earth-rail-l3")&&view.CurrentFit.Definition.Components is null
                &&Hangar.Research.Points[DesignFamily.Earth]==0,"Confirmation revalidates affordability before unlocking or fitting");
            Hangar.Research.Points[DesignFamily.Earth]=100000;
            CheckHangarPartEditing(view);
            foreach(var part in new[]{"drive-vector","scanner-lowprofile","barrier-recharge"})
            {
                view.SelectSystem(SystemParts.ById(part).Slot);view.InstallSystemPart(part,true);
                long balance=Hangar.Research.Points[DesignFamily.Earth],cost=Hangar.Research.PartCost(part);
                var confirm=MenuConfirmation.Pending(view)!;confirm.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
                confirm.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
                HangarVerify(Hangar.Research.Points[DesignFamily.Earth]==balance-cost,"Repeated confirmation charges exactly once: "+part);
                HangarVerify(view.CurrentFit.Definition.Components!.PartIds.Contains(part),"Hangar researches and fits "+part);
            }
            CheckSystemPartEditing(view);
            view.SelectSystem(ComponentSlot.MainGun);view.InstallPart(0,null);
            HangarVerify(MenuConfirmation.Pending(view) is not null,"Restoring a stock component also asks for confirmation");
            MenuConfirmation.Pending(view)!.CancelButton.EmitSignal(BaseButton.SignalName.Pressed);
            HangarVerify(view.SelectedParts!.MainGuns![0]=="earth-rail-l3","Cancel keeps the installed component");
            view.SelectSystem(ComponentSlot.MainGun);
            HangarVerify(Find("part-stock").TooltipText.Contains("장착 후"),"Stock candidate retains the actual resulting supply and volume");
            view.SelectPreset(1);HangarVerify(view.CurrentFit.Definition.Components is null,"Other system presets stay stock");
            view.SelectPreset(0);HangarVerify(view.SelectedParts is {MainGuns:not null,PointDefensePart:not null,Launcher:not null,Missile:not null,PowerParts:not null},"All four modular categories return with their preset");
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
            HangarVerify(((MenuItemButton)Find("part-stock")).Caption.Contains("레이저")&&((MenuItemButton)Find("part-stock")).Detail=="장착 중","Factory laser has the real stock label and installed state");
            // A previously purchased explicit laser remains a valid save selection.
            Hangar.SetComponent("earth_air_defense_cruiser",0,ComponentSlot.MainGun,"main-laser",true);
            view.SelectSystem(ComponentSlot.MainGun);
            HangarVerify(view.Preview.Fittings!.Parts.First(p=>p.Id=="main:0").Name==ShipComponents.ById("main-laser").Name,"Old explicit laser selection is described truthfully on the ship");
            view.SelectBay("utility-port");view.Install("shield-capacity");
            MenuConfirmation.Pending(view)!.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
            preview=view.Preview;float yaw=preview.Yaw;
            preview._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=new(400,200)});
            preview._GuiInput(new InputEventMouseMotion{Position=new(470,200),Relative=new(70,0)});
            preview._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=new(470,200)});
            HangarVerify(preview.Yaw!=yaw,"Preview drag orbits without steering a ship");
            _UnhandledInput(new InputEventKey{PhysicalKeycode=Key.Escape,Pressed=true});
            HangarVerify(Screen==BattleScreen.Hangar&&view.Preview.PickBay(view.Preview.BayScreen("utility-port")) is null,
                "Escape first closes the open bay option list and stays in the hangar");
            _UnhandledInput(new InputEventKey{PhysicalKeycode=Key.Escape,Pressed=true});
            HangarVerify(Screen==BattleScreen.Title,"A second Escape returns from hangar to main menu");OpenHangar();
            BeginMatch(true);
            _hangarSnapshot=PendingMatch!.Player.Loadout!.Members.Select(m=>m.ContentHash).ToArray();
            Hangar.SetEquipment("battleship",0,"utility-port",null);
            Hangar.SetMainGun("battleship",0,0,null);
            HangarVerify(PendingMatch.Player.Loadout.Members[0].Definition.InstalledEquipment.Length==1,"Queue freezes a snapshot independent of later edits");
            HangarVerify(PendingMatch.Player.Loadout.Leader.Battery!.WeaponAt(0).Id=="earth-rail-l3","Queue freezes the primary selection");
        }
        if(frame==120)
        {
            HangarVerify(Screen==BattleScreen.Battle&&Battle is not null,"Hangar launch reaches battle");
            var game=Battle!;var members=game.OwnedSquadron!.Members;
            HangarVerify(members[0].Railguns[0].BarrelCount==3&&members[0].Definition.Missiles!.CellCount==2&&members[0].Definition.Battery!.PowerParts is ["earth-gen-s"],
                "All four modular categories survive actual battle spawn");
            HangarVerify(members[0].Definition.Components is {EnginePart:"drive-vector",SensorPart:"scanner-lowprofile",ShieldPart:"barrier-recharge",Engine:null,Sensor:null,Shield:null},
                "New engine, sensor and shield assemblies survive matching and battle spawn without legacy stacking");
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

    private void CheckTacticalFitting()
    {
        OpenHangar();ChooseHull("battleship");var view=_overlay.HangarView!;
        var saved=Hangar;Hangar=HangarProfile.Create("battleship");Hangar.Research!.Points[DesignFamily.Earth]=1000;
        view.SelectSlot(0);view.SelectTacticalBay("tactical-0");
        long before=Hangar.Research.Points[DesignFamily.Earth];view.InstallTactical("generator-overload");
        HangarVerify(MenuConfirmation.Pending(view) is not null&&Hangar.Research.Points[DesignFamily.Earth]==before,"Tactical research waits for confirmation");
        MenuConfirmation.Pending(view)!.Cancel();
        HangarVerify(view.CurrentFit.Definition.TacticalModules is null,"Cancelling tactical research leaves fit unchanged");
        view.InstallTactical("generator-overload");MenuConfirmation.Pending(view)!.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
        HangarVerify(view.CurrentFit.Definition.TacticalModules is [{Device.Id:"generator-overload"}]&&Hangar.Research.Points[DesignFamily.Earth]==before-100,"Confirmed tactical fit debits once and adds an actual compartment");
        view.SelectTacticalBay("tactical-1");view.InstallTactical("focus-search");MenuConfirmation.Pending(view)!.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
        view.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text=="앞 순서로").EmitSignal(BaseButton.SignalName.Pressed);
        HangarVerify(view.CurrentFit.Definition.TacticalModules![0].Device.Id=="focus-search","Active skill order is changeable");
        view.CloseOverlay();HangarVerify(GetViewport().GuiGetFocusOwner()?.GetMeta("focus_key","").AsString()=="tactical-modules","Closing tactical picker restores focus");
        Hangar=saved;OpenHangar();ReturnToPort();
    }
    private void CheckHangarPartEditing(HangarView view)
    {
        Button Find(string key)=>view.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.GetMeta("focus_key","").AsString()==key);
        bool Has(string key)=>view.FindChildren("*","Button",true,false).OfType<Button>().Any(b=>b.GetMeta("focus_key","").AsString()==key);
        void Confirm()
        {
            var modal=MenuConfirmation.Pending(view);
            HangarVerify(modal is not null,"Modular fitting requires a concrete confirmation: "+string.Join("; ",view.GetChildren().OfType<Label>().Where(l=>l.Text.StartsWith("변경할 수")).Select(l=>l.Text)));
            modal!.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
        }
        foreach(var category in Enum.GetValues<HangarView.FittingCategory>())
        {
            view.SelectFitting(category);
            HangarVerify(!Has("component-stock")&&!Has("maingun-tab-systems")&&!Has("secondary-tab-systems"),category+": no legacy component picker");
            HangarVerify(category is HangarView.FittingCategory.Generator or HangarView.FittingCategory.Shield
                ?Find("system-"+category) is MenuItemButton{Picture:not null}
                :!Has("system-"+category)&&view.Preview.Fittings!.Parts.Any(p=>p.Category==category),category+": internal icon or exterior surface, without duplicate shortcut");
            var panel=view.GetChildren().OfType<Control>().Single(c=>c.GetMeta("focus_key","").AsString()=="fitting-panel");
            HangarVerify(panel.Position.Y>=90&&panel.Position.Y+panel.Size.Y<=522,category+": fitting list stays within the viewport");
            HangarVerify(panel.GetChildren().OfType<ScrollContainer>().Single().FollowFocus,category+": scroll follows keyboard selection");
        }
        view.SelectSystem(ComponentSlot.MainGun);Find("part-earth-laser-l-line").EmitSignal(BaseButton.SignalName.Pressed);
        HangarVerify(MenuConfirmation.Pending(view)?.Body.Contains("모든 주포 소켓")==true,"Changing weapon family previews a whole-battery replacement");
        Confirm();HangarVerify(view.CurrentFit.Definition.Battery!.SocketWeapons!.All(w=>w.Laser is not null),"New part menu can switch the complete battery to lasers");
        view.InstallPart(0,null);Confirm();
        HangarVerify(view.CurrentFit.Definition.Battery!.SocketWeapons is null&&view.CurrentFit.Definition.Components is null,"Stock choice can return a laser battery to railguns");
        // Legacy selections load unchanged, then are replaced only by a confirmed new part.
        Hangar.SetComponent("battleship",0,ComponentSlot.MainGun,"main-triple",true);
        view.SelectSystem(ComponentSlot.MainGun);
        HangarVerify(((MenuItemButton)Find("part-stock")).Detail!="장착 중"&&view.Preview.Fittings!.Parts.First(p=>p.Id=="main:0").Name==ShipComponents.ById("main-triple").Name,"Legacy primary is not mislabeled as stock");
        string before=HangarStorage.Serialize(Hangar);view.InstallPart(0,null);
        HangarVerify(MenuConfirmation.Pending(view)!.Body.Contains("주포 전체"),"Replacing a legacy whole battery explains every affected socket");
        MenuConfirmation.Pending(view)!.Cancel();HangarVerify(HangarStorage.Serialize(Hangar)==before,"Cancel preserves a legacy fit and research");
        view.InstallPart(0,null);Confirm();
        HangarVerify(view.CurrentFit.Definition.Components?.MainGun is null,"Stock socket choice actually clears legacy primary");
        view.InstallPart(0,"earth-rail-l3",true);long balance=Hangar.Research!.Points[DesignFamily.Earth];Confirm();
        HangarVerify(Hangar.Research.Points[DesignFamily.Earth]==balance-MainGunParts.ById("earth-rail-l3").Cost,"Part research charges exactly once");
        HangarVerify(view.Preview.Fittings!.Parts.First(p=>p.Id=="main:0").Name==MainGunParts.ById("earth-rail-l3").Name,"Main gun surface reports installed socket parts");
        Hangar.SetComponent("battleship",0,ComponentSlot.Secondary,"pd-longrange",true);view.SelectSystem(ComponentSlot.Secondary);
        view.InstallOrdnance(OrdnanceKind.PointDefense,null);Confirm();
        HangarVerify(view.CurrentFit.Definition.Components?.Secondary is null,"Stock PD choice clears legacy secondary");
        view.InstallOrdnance(OrdnanceKind.PointDefense,"pd-gatling-heavy",true);Confirm();
        HangarVerify(view.Preview.Fittings!.Parts.First(p=>p.Id=="pd:0").Name.Contains("개틀링"),"Secondary surface reports the fitted PD part");
        view.SelectFitting(HangarView.FittingCategory.Missiles);
        HangarVerify(Has("missile-tab-Launcher")&&Has("missile-tab-Missile")&&!Has("secondary-tab-PointDefense"),"Missile module has only launcher and ammunition tabs");
        Find("ordnance-launcher-twin").EmitSignal(BaseButton.SignalName.Pressed);Confirm();
        Find("missile-tab-Missile").EmitSignal(BaseButton.SignalName.Pressed);
        Find("ordnance-missile-heavy-ap").EmitSignal(BaseButton.SignalName.Pressed);Confirm();
        HangarVerify(view.SelectedParts is {Launcher:"launcher-twin",Missile:"missile-heavy-ap"},"Both missile choices are independent");
        // Free real weapon volume for the generator rather than bypassing fitting limits.
        view.SelectSystem(ComponentSlot.MainGun);view.InstallPart(1,"earth-rail-s2",true);Confirm();
        HangarVerify(view.SelectedParts!.MainGuns![0]=="earth-rail-l3"&&view.SelectedParts.MainGuns[2] is null,"Same-family replacement changes only the chosen socket");
        Hangar.SetComponent("battleship",0,ComponentSlot.Generator,"generator-output",true);view.SelectSystem(ComponentSlot.Generator);
        view.InstallPower(0,"earth-gen-s",true);Confirm();
        HangarVerify(view.SelectedParts is {Generator:null,PowerParts:["earth-gen-s"]},"New power part replaces legacy generator without stacking");
        HangarVerify(Find("system-Generator").TooltipText.Contains("소형 보조 발전기")&&Has("power-earth-cooler-s"),"Power category exposes fitted generator and cooler alternatives");
        var restored=HangarStorage.Parse(HangarStorage.Serialize(Hangar));
        HangarVerify(restored.Fit(new("battleship",0)).Components==Hangar.Fit(new("battleship",0)).Components
            ||FleetLoadouts.ResolveFit(restored.Fit(new("battleship",0))).ContentHash==view.CurrentFit.ContentHash,"Modular choices preserve save and launch identity");
        // Slot-owned replacement must not leak into a shared preset.
        view.SelectPreset(HangarSlot.Individual);view.SelectSystem(ComponentSlot.Secondary);
        view.InstallOrdnance(OrdnanceKind.PointDefense,null);Confirm();
        HangarVerify(Hangar.Fit(Hangar.Hulls["battleship"].Squadron[0]!).Components?.PointDefensePart is null
            &&Hangar.Fit(new("battleship",0)).Components?.PointDefensePart=="pd-gatling-heavy","Individual PD edit leaves shared fit intact");
        view.SelectPreset(0);
    }

    private void CheckSystemPartEditing(HangarView view)
    {
        Button Find(string key)=>view.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.GetMeta("focus_key","").AsString()==key);
        foreach(string id in new[]{"drive-vector","scanner-lowprofile","barrier-recharge"})
        {
            var part=SystemParts.ById(id);var progress=Hangar.Research!;
            Hangar.SetComponent("battleship",0,part.Slot,part.LegacyId,true);
            view.SelectSystem(part.Slot);
            HangarVerify(((MenuItemButton)Find("system-part-stock")).Detail!="장착 중","Legacy assembly is not mislabeled as stock");
            string untouched=HangarStorage.Serialize(Hangar);long balance=progress.Points[DesignFamily.Earth];
            Find("system-part-"+id).EmitSignal(BaseButton.SignalName.Pressed);
            var dialog=MenuConfirmation.Pending(view)!;
            HangarVerify(dialog is not null&&dialog.Body.Contains("기존 교체 계통")&&dialog.Body.Contains("→"),"Legacy replacement previews actual fit and replacement scope");
            dialog!.Cancel();HangarVerify(HangarStorage.Serialize(Hangar)==untouched,"Cancelling system conversion preserves fit and research");
            Find("system-part-"+id).EmitSignal(BaseButton.SignalName.Pressed);
            MenuConfirmation.Pending(view)!.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
            HangarVerify(progress.Points[DesignFamily.Earth]==balance&&view.SelectedParts!.SystemPart(part.Slot)==id&&view.SelectedParts.Get(part.Slot) is null,
                "Equivalent researched part replaces legacy without charging or stacking");
            string displayed=part.Slot==ComponentSlot.Shield?Find("system-Shield").TooltipText
                :view.Preview.Fittings!.Parts.First(p=>p.Category.ToString()==part.Slot.ToString()).Name;
            HangarVerify(displayed.Contains(part.Name)&&Find("system-part-"+id).TooltipText.Contains("총 전력 수요"),"Installed surface/internal icon and tooltip report actual assembly and fit costs");
            Find("system-part-stock").EmitSignal(BaseButton.SignalName.Pressed);MenuConfirmation.Pending(view)!.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
            HangarVerify(view.SelectedParts!.SystemPart(part.Slot) is null&&view.SelectedParts.Get(part.Slot) is null,"Stock action clears only its assembly");
            Find("system-part-"+id).EmitSignal(BaseButton.SignalName.Pressed);MenuConfirmation.Pending(view)!.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
            HangarVerify(progress.Points[DesignFamily.Earth]==balance,"Researched assembly can be refitted for free");
        }
        view.SelectSystem(ComponentSlot.Sensor);
        long funds=Hangar.Research!.Points[DesignFamily.Earth];
        Find("system-part-scanner-longrange").EmitSignal(BaseButton.SignalName.Pressed);
        HangarVerify(MenuConfirmation.Pending(view)!.Body.Contains("동시 잠금"),"Sensor confirmation exposes the additional lock channel");
        Hangar.Research.Points[DesignFamily.Earth]=0;
        MenuConfirmation.Pending(view)!.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
        HangarVerify(view.SelectedParts!.SensorPart=="scanner-lowprofile"&&!Hangar.Research.HasPart(DesignFamily.Earth,"scanner-longrange"),"RP is revalidated at confirmation without losing installed sensor");
        Hangar.Research.Points[DesignFamily.Earth]=funds;
        view.SelectPreset(HangarSlot.Individual);view.SelectSystem(ComponentSlot.Engine);
        // This slot retains its earlier PD-only individual fit; equip an engine before testing removal.
        Find("system-part-drive-vector").EmitSignal(BaseButton.SignalName.Pressed);MenuConfirmation.Pending(view)!.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
        Find("system-part-stock").EmitSignal(BaseButton.SignalName.Pressed);MenuConfirmation.Pending(view)!.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
        HangarVerify(view.SelectedParts!.EnginePart is null&&Hangar.Fit(new("battleship",0)).Components!.EnginePart=="drive-vector","Individual engine edit leaves the shared preset intact");
        view.SelectPreset(0);
        var roundTrip=HangarStorage.Parse(HangarStorage.Serialize(Hangar));
        HangarVerify(FleetLoadouts.ResolveFit(roundTrip.Fit(new("battleship",0))).ContentHash==view.CurrentFit.ContentHash,"All seven fitting categories survive a save unchanged");
    }

    /// <summary>Port-style squad editing: empty slots, hull cards, individual fits, slot menu, "my ships" list and save v4.</summary>
    private void CheckHangarSquadEditing()
    {
        OpenHangar();ChooseHull("battleship");var view=_overlay.HangarView!;
        Button Find(string key)=>view.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.GetMeta("focus_key","").AsString()==key);
        var squad=Hangar.Hulls["battleship"].Squadron;
        view.RemoveMember(2);squad=Hangar.Hulls["battleship"].Squadron;
        HangarVerify(squad[2] is null&&Find("squad-2").Text.Contains("함급 추가"),"x empties a member slot and shows a + slot");
        HangarVerify(LaunchProblem()?.Contains("빈 슬롯 1개")==true&&Find("launch").Disabled,"An empty slot blocks launch with its count");
        HangarVerify(Throws(()=>Hangar.ClearSlot(0)),"The leader slot cannot be emptied");
        view.SelectSlot(2);
        HangarVerify(view.PickerOpen&&!Find("pick-earth_air_defense_cruiser").Disabled,"Selecting an empty slot opens hull cards with eligible hulls enabled");
        HangarVerify(Find("pick-interceptor").Disabled&&((MenuItemButton)Find("pick-interceptor")).Detail=="호위함·순양함만","Ineligible cards stay visible, disabled, with the reason");
        HangarVerify(view.CloseOverlay()&&!view.PickerOpen,"Escape path closes the hull cards first");
        view.SelectSlot(2);view.PickHull("earth_air_defense_cruiser");
        HangarVerify(Hangar.Hulls["battleship"].Squadron[2]==new HangarSlot("earth_air_defense_cruiser",0)&&LaunchProblem() is null,"Picking a card fills the slot with shared preset 1");
        view.RemoveMember(2);Hangar.FillEmptySlots();
        HangarVerify(Hangar.EmptySlots()==0&&Hangar.Hulls["battleship"].Squadron[2]!.HullId==FleetLoadouts.Default("battleship").Members[2].HullId,"Fill restores default members");
        // Individual fit: copies the shared preset once, then edits stay in the slot.
        view.SelectSlot(1);view.SelectPreset(HangarSlot.Individual);
        var member=Hangar.Hulls["battleship"].Squadron[1]!;
        HangarVerify(member.UsesOwn&&member.Own is not null&&Find("개별") is {} own&&own.HasMeta("focus_key"),"Individual mode stores a slot-owned fit");
        view.SelectBay("utility-port");view.Install("shield-capacity");MenuConfirmation.Pending(view)!.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
        HangarVerify(Hangar.Hulls["battleship"].Squadron[1]!.Own!.Components?.AuxiliaryParts?.Length==1&&Hangar.Hulls[member.HullId].Presets[0].Length==0,
            "Individual equipment changes only the slot, never the shared preset");
        view.SelectPreset(0);
        HangarVerify(view.CurrentFit.Definition.InstalledEquipment.Length==0,"Switching back to a shared preset uses that preset");
        view.SelectPreset(HangarSlot.Individual);
        HangarVerify(view.CurrentFit.Definition.InstalledEquipment.Length==1,"The slot's individual fit is kept while a shared preset is in use");
        view.OpenSlotMenu(1,null);var menu=view.SlotMenu!;
        HangarVerify(menu.Visible&&menu.IsItemChecked(menu.GetItemIndex(10+HangarSlot.Individual))&&menu.GetItemIndex(20)>=0,"Slot menu offers presets, individual fit and removal");
        menu.Hide();
        view.OpenSlotMenu(0,null);menu=view.SlotMenu!;
        HangarVerify(menu.IsItemDisabled(menu.GetItemIndex(0))&&menu.GetItemIndex(20)<0,"Leader slot cannot be replaced or removed from its menu");
        menu.Hide();
        // My ships: favourites lead, drag order persists, hidden ships leave the list but not ownership.
        Hangar.SetFavorite("earth_gun_cruiser",true);
        HangarVerify(Hangar.ShipList()[0]=="earth_gun_cruiser","A favourite is always listed first");
        Hangar.MoveShip("escort","earth_gun_cruiser");
        HangarVerify(Hangar.ShipList().Take(2).SequenceEqual(new[]{"escort","earth_gun_cruiser"}),"Dragging onto a favourite moves the ship into the favourites in that place");
        Hangar.SetHidden("interceptor",true);
        HangarVerify(!Hangar.ShipList().Contains("interceptor")&&Hangar.Owned.Contains("interceptor"),"Hiding removes a hull from the list without losing it");
        HangarVerify(Throws(()=>Hangar.SetHidden("battleship",true)),"The current leader cannot be hidden");
        string json=HangarStorage.Serialize(Hangar);
        HangarVerify(HangarStorage.Serialize(HangarStorage.Parse(json))==json,"Save v4 round trips slots, individual fits and the ship list");
        var legacy=System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        legacy["version"]=3;legacy.Remove("shipOrder");legacy.Remove("favorites");legacy.Remove("hidden");
        foreach(var (_,hull) in legacy["hulls"]!.AsObject())
            foreach(var slot in hull!["squadron"]!.AsArray())if(slot is System.Text.Json.Nodes.JsonObject member3){member3.Remove("own");member3["preset"]=Math.Min(2,(int)member3["preset"]!);}
        HangarVerify(HangarStorage.Parse(legacy.ToJsonString()).ShipList().Length>0,"A v3 save without the new fields still loads");
        Hangar=HangarProfile.Create();ReturnToPort();
    }
    private static bool Throws(Action action){try{action();return false;}catch(System.IO.InvalidDataException){return true;}}
}
