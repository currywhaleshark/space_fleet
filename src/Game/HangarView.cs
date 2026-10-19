using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

/// <summary>
/// Port screen in the WoWS/War Thunder idiom:
/// - "My ships" carousel at the bottom (drag order, favourites first, hide/show).
/// - The selected ship's squad slots on the left (+ to add, x to remove, right click or ... for presets).
/// - Preview in the centre with compact system/bay icons (tooltip on hover, option list on click), stats on the right.
/// </summary>
public partial class HangarView : Control
{
    public Main Host { get; set; }=null!;
    private Font _font=null!;
    private Label _status=null!;
    private HangarPreview _preview=null!;
    private string? _bay;
    private bool _cutaway;
    private ComponentSlot _system=ComponentSlot.MainGun;
    // Main gun popover: catalog systems, or absolute parts per socket (modular weapons §16–17).
    private bool _partsTab;
    private int _socket;
    private enum Popover { None, System, Bay }
    private Popover _popover;
    private int? _picker;
    private HullKind? _kindFilter;
    private DesignFamily? _designFilter;
    private bool _hiddenTray;
    private static readonly Color Ink=new(.81f,.89f,.96f),Blue=new(.35f,.75f,1),Orange=new(1,.7f,.32f),Muted=MenuStyles.Muted,
        Line=new(.13f,.23f,.3f),Panel=new(.025f,.055f,.08f);
    private HangarSlot?[] Squad => Host.Hangar.Hulls[Host.PlayerHullId].Squadron;
    private HangarSlot Current => Squad[Host.HangarSlotIndex]??Squad[0]!;
    internal ResolvedLoadout CurrentFit => FleetLoadouts.ResolveFit(Host.Hangar.Fit(Current));
    internal string? SelectedBay => _bay;
    internal HangarPreview Preview => _preview;
    internal bool PickerOpen => _picker is not null;
    public override void _Ready()
    {
        _font=new SystemFont{FontNames=new[]{"Malgun Gothic","Segoe UI"}};
        if(OS.GetCmdlineUserArgs().Contains("--hangar-exterior"))_cutaway=false;
        var args=BattleArgs.Parse(OS.GetCmdlineUserArgs());
        if(args.ContainsKey("shot")&&args.TryGetValue("hangar-system",out var system)){_system=Enum.Parse<ComponentSlot>(system);_popover=Popover.System;}
        if(args.ContainsKey("shot")&&args.TryGetValue("hangar-parts",out var socket)){_partsTab=true;_socket=int.Parse(socket);}
        if(args.ContainsKey("shot")&&args.TryGetValue("hangar-bay",out var bay)){_bay=bay;_cutaway=true;_popover=Popover.Bay;}
        if(args.ContainsKey("shot")&&args.TryGetValue("hangar-picker",out var picker))_picker=int.Parse(picker);
        Size=new(1600,900);Rebuild();
        if(args.ContainsKey("shot")&&args.ContainsKey("hangar-focus"))MenuStyles.RestoreFocus(this,"system-"+_system);
    }
    private Label Label(string text,Rect2 r,int size=18,Color? color=null,Node? parent=null)
    {
        var label=new Label{AutowrapMode=TextServer.AutowrapMode.WordSmart,Text=text,
            Position=r.Position,Size=r.Size,MouseFilter=MouseFilterEnum.Ignore};
        label.AddThemeFontOverride("font",_font);label.AddThemeFontSizeOverride("font_size",size);
        label.AddThemeColorOverride("font_color",color??Ink);(parent??this).AddChild(label);return label;
    }
    private Button Button(string text,Rect2 r,Action action,bool selected=false,bool primary=false,int size=18,Node? parent=null)
    {
        var b=new Button{Text=text,Position=r.Position,Size=r.Size,ClipText=true,TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis};
        b.AddThemeFontSizeOverride("font_size",size);
        MenuStyles.Apply(b,_font,selected?Blue:Muted,primary,accent:selected);
        b.AddThemeColorOverride("font_color",primary?new(.025f,.055f,.085f):selected?Blue:Ink);
        b.Pressed+=action;(parent??this).AddChild(b);return b;
    }
    private MenuItemButton Item(string name,Texture2D? picture,Rect2 r,Action? action,string details,
        bool selected=false,bool portrait=false,string status="",string marker="",Node? parent=null)
    {
        var b=new MenuItemButton{Caption=name,Picture=picture,Typeface=_font,Portrait=portrait,
            Detail=status,Marker=marker,TooltipText=details,Position=r.Position,Size=r.Size};
        MenuStyles.Apply(b,_font,selected?Blue:Ink,accent:selected);
        b.SetMeta("focus_key",name);
        b.Pressed+=action??b.ShowDetails;(parent??this).AddChild(b);return b;
    }
    private void Action(Action edit)
    {
        try{edit();Host.SaveHangar();Rebuild();}
        catch(Exception e)when(e is InvalidDataException or ArgumentException){Error(e);}
    }
    private void Error(Exception e){_status.Text="변경할 수 없습니다 · "+e.Message;_status.Modulate=Orange;}

    // ---- Slot and fitting actions (also driven directly by the hangar flow checks) ----
    internal void SelectSlot(int slot)
    {
        if(Squad[slot] is null){OpenPicker(slot);return;}
        Host.HangarSlotIndex=slot;_bay=null;_popover=Popover.None;Rebuild();
    }
    internal void SelectBay(string bay){_bay=bay;_cutaway=true;_popover=Popover.Bay;Rebuild();}
    internal void SelectSystem(ComponentSlot slot){_system=slot;_cutaway=false;_popover=Popover.System;Rebuild();}
    internal void OpenPicker(int slot){_picker=slot;_popover=Popover.None;Rebuild();}
    /// <summary>Escape closes the topmost hangar overlay first; false when none is open.</summary>
    internal bool CloseOverlay()
    {
        if(_picker is not null){int slot=_picker.Value;_picker=null;Rebuild();MenuStyles.RestoreFocus(this,"squad-"+slot);return true;}
        if(_popover!=Popover.None)
        {
            var key=_popover==Popover.System?"system-"+_system:"bay-"+_bay;
            _popover=Popover.None;_cutaway=false;Rebuild();MenuStyles.RestoreFocus(this,key);return true;
        }
        return false;
    }
    internal void InstallSystem(string? id,bool research=false)
    {
        if(MenuConfirmation.Pending(this) is not null)return;
        var target=Current;int index=SlotIndex(target);var system=_system;var fit=Host.Hangar.Fit(target);var old=fit.Components?.Get(system);
        if(old==id)return;
        try
        {
            var before=FleetLoadouts.ResolveFit(fit).Definition;
            var after=FleetLoadouts.ResolveFit(fit with{Components=(fit.Components??new()).With(system,id)}).Definition;
            var progress=Host.Hangar.Research!;
            long cost=id is not null&&!progress.HasComponent(target.HullId,id)?progress.ComponentCost(target.HullId,id):0;
            if(cost>0&&!research)throw new InvalidDataException("부품을 먼저 연구해야 합니다.");
            if(progress.Points[before.Design]<cost)throw new InvalidDataException("연구 점수가 부족합니다.");
            string Name(string? part)=>part is null?"기본형":ShipComponents.ById(part).Name;
            string scope=target.UsesOwn?"이 슬롯에만 적용됩니다.":"같은 프리셋을 사용하는 함선에 함께 적용됩니다.";
            MenuConfirmation.Show(this,_font,cost>0?"부품 연구 및 교체 확인":"부품 교체 확인",
                FitScope(target)+$"\n{ShipComponents.Name(system)} · {Name(old)} → {Name(id)}\n\n"+
                ComponentComparison.Fitting(system,before,after),
                MenuConfirmation.Price(before.Design,progress.Points[before.Design],cost),
                cost>0?scope+" 연구 점수는 환불되지 않으며, 연구한 부품의 재장착은 무료입니다.":scope+" 연구 점수 소모 없이 다시 변경할 수 있습니다.",
                cost>0?"연구하고 장착":"교체 적용",()=>Action(()=>
                {
                    if(target.UsesOwn)Host.Hangar.SetSlotComponent(index,system,id,research);
                    else Host.Hangar.SetComponent(target.HullId,target.Preset,system,id,research);
                }));
        }
        catch(Exception e)when(e is InvalidDataException or ArgumentException){Error(e);}
    }
    /// <summary>Shared preset 0..2, or HangarSlot.Individual for a slot-owned fit.</summary>
    internal void SelectPreset(int preset)=>Action(()=>Host.Hangar.SetSlotMode(Host.HangarSlotIndex,preset));
    internal void Install(string? device)
    {
        if(_bay is null||MenuConfirmation.Pending(this) is not null)return;
        var target=Current;int index=SlotIndex(target);string bay=_bay;var fit=Host.Hangar.Fit(target);
        string? old=fit.Equipment.FirstOrDefault(m=>m.BayId==bay)?.EquipmentId;
        if(old==device)return;
        try
        {
            var mounts=fit.Equipment.Where(m=>m.BayId!=bay).ToList();if(device is not null)mounts.Add(new(bay,device));
            var before=FleetLoadouts.ResolveFit(fit).Definition;var after=FleetLoadouts.ResolveFit(fit with{Equipment=mounts}).Definition;
            string Name(string? id)=>id is null?"비어 있음":ShipEquipment.ById(id).Name;
            MenuConfirmation.Show(this,_font,device is null?"장비 해제 확인":"장비 교체 확인",
                FitScope(target)+$"\n{BayName(bay)} · {Name(old)} → {Name(device)}\n\n"+
                ComponentComparison.Equipment(before,after),"비용 0 RP · 연구 점수를 소모하지 않습니다.",
                target.UsesOwn?"이 슬롯에만 적용됩니다. 격납고에서 다시 변경할 수 있습니다.":"같은 프리셋을 사용하는 함선에 함께 적용됩니다. 격납고에서 다시 변경할 수 있습니다.",
                device is null?"장비 해제":"장착 적용",()=>Action(()=>
                {
                    if(target.UsesOwn)Host.Hangar.SetSlotEquipment(index,bay,device);
                    else Host.Hangar.SetEquipment(target.HullId,target.Preset,bay,device);
                }));
        }
        catch(Exception e)when(e is InvalidDataException or ArgumentException){Error(e);}
    }
    private int SlotIndex(HangarSlot target)=>Squad[Host.HangarSlotIndex]==target?Host.HangarSlotIndex:0;
    private string FitScope(HangarSlot target)
    {
        string head=$"{ShipDefinitions.ById(target.HullId).Flight.DisplayName} · {ModeName(target)}";
        if(target.UsesOwn)return head+$"\n적용 대상: {(Host.HangarSlotIndex==0?"기함":$"편대원 {Host.HangarSlotIndex+1}")} (개별 설정)";
        var members=Squad.Select((slot,index)=>(slot,index))
            .Where(m=>m.slot is {UsesOwn:false}&&m.slot.HullId==target.HullId&&m.slot.Preset==target.Preset)
            .Select(m=>m.index==0?"기함":$"편대원 {m.index+1}");
        return head+$"\n적용 대상: {string.Join(", ",members)}";
    }
    private static string ModeName(HangarSlot slot)=>slot.UsesOwn?"개별 설정":$"공유 프리셋 {slot.Preset+1}";
    internal void ChangeMember(string hull)
    {
        if(Host.HangarSlotIndex==0){Host.ChooseHull(hull);return;}
        Action(()=>Host.Hangar.SetSlot(Host.HangarSlotIndex,hull,0));
    }
    internal void RemoveMember(int slot)
    {
        Action(()=>
        {
            Host.Hangar.ClearSlot(slot);
            if(Host.HangarSlotIndex==slot)Host.HangarSlotIndex=0;
        });
    }
    internal void PickHull(string hull)
    {
        if(_picker is not { } slot)return;
        _picker=null;Host.HangarSlotIndex=slot;
        Action(()=>Host.Hangar.SetSlot(slot,hull,0));
        MenuStyles.RestoreFocus(this,"squad-"+slot);
    }
    /// <summary>Why a hull cannot fill a member slot of the current leader, or null when it can.</summary>
    internal string? PickBlocked(int slot,ShipDefinition hull)
    {
        var leader=ShipDefinitions.ById(Host.PlayerHullId);
        if(!Host.Hangar.Owned.Contains(hull.Id))return "미해금";
        if(hull.Design!=leader.Design)return "다른 진영";
        if(!FleetLoadouts.Allows(leader,slot,hull))
            return leader.Kind==HullKind.Battleship?"호위함·순양함만":"같은 함종만";
        if(leader.MatchClass==MatchWeightClass.Large&&ResearchTree.Catalog.ByHull(hull.Id).Tier>ResearchTree.Catalog.ByHull(leader.Id).Tier)
            return "기함보다 높은 티어";
        return null;
    }

    // ---- Layout ----
    private void Rebuild()
    {
        string? focus=MenuStyles.FocusKey(this);
        float yaw=_preview?.Yaw??-.9f,pitch=_preview?.Pitch??.42f,zoom=_preview?.Zoom??1.35f;
        foreach(Node child in GetChildren()){RemoveChild(child);child.QueueFree();}
        Host.HangarSlotIndex=Math.Clamp(Host.HangarSlotIndex,0,Squad.Length-1);
        if(Squad[Host.HangarSlotIndex] is null)Host.HangarSlotIndex=0;
        var slot=Current;var fit=CurrentFit;var def=fit.Definition;
        if(!def.Bays.Any(b=>b.Id==_bay)){_bay=def.Bays.FirstOrDefault()?.Id;if(_popover==Popover.Bay&&_bay is null)_popover=Popover.None;}
        BuildTopBar();
        BuildSquad();
        // Centre: ship name, fit mode selector, preview and compact fitting icons.
        Label(def.Flight.DisplayName,new(364,92,520,42),26);
        string role=FleetLoadouts.RoleName(def.Branch);
        Label($"T{ResearchTree.Catalog.ByHull(def.Id).Tier} · {role} · {(def.Design==DesignFamily.Earth?"지구권":"화성권")} · {(Host.HangarSlotIndex==0?"기함":$"편대원 {Host.HangarSlotIndex+1}")}",
            new(364,134,520,26),16,Blue);
        string[] modes={"프리셋 1","프리셋 2","프리셋 3","개별"};
        for(int i=0;i<modes.Length;i++)
        {
            int mode=i;
            var b=Button(modes[i],new(884+i*74,104,70,34),()=>SelectPreset(mode),slot.Preset==i,size:15);
            b.TooltipText=i<3?$"공유 프리셋 {i+1} · 이 함급의 같은 프리셋을 쓰는 슬롯과 장비를 공유합니다":"개별 설정 · 이 슬롯만 따로 장비를 저장합니다";
        }
        _preview=new HangarPreview{Position=new(364,166),Size=new(816,352),Definition=def,Cutaway=_cutaway,
            ShowBayMarkers=_popover==Popover.Bay||_cutaway,SelectedBay=_bay,Yaw=yaw,Pitch=pitch,Zoom=zoom};
        _preview.BaySelected+=SelectBay;AddChild(_preview);
        Button(_cutaway?"외형으로 보기":"단면으로 보기",new(1036,176,134,32),()=>{_cutaway=!_cutaway;Rebuild();},_cutaway,size:15)
            .SetMeta("focus_key","preview-mode");
        Label("드래그 회전 · 휠 확대",new(378,490,400,24),14,Muted);
        BuildFittingIcons(def);
        var others=Squad.Count(s=>s is {UsesOwn:false}&&s.HullId==slot.HullId&&s.Preset==slot.Preset);
        Label(slot.UsesOwn?"개별 설정 · 아이콘 변경은 이 슬롯에만 적용됩니다.":$"공유 프리셋 {slot.Preset+1} · 이 함급의 같은 프리셋을 쓰는 모든 슬롯에 적용"+(others>1?$" (지금 {others}척)":""),
            new(364,594,816,24),15,slot.UsesOwn?Orange:Muted);
        _status=Label(Host.HangarMessage??"변경 사항은 자동 저장됩니다.",new(364,620,816,26),15,Muted);
        BuildStats(def);
        BuildCarousel();
        if(_popover!=Popover.None)BuildPopover(def);
        if(_picker is { } picker)BuildPicker(picker);
        MenuStyles.RestoreFocus(this,focus);
        QueueRedraw();
    }
    private void BuildTopBar()
    {
        Button("메인으로",new(24,18,110,42),Host.ReturnToPort,size:16);
        Label("격납고",new(150,14,140,48),28);
        Button("기술 연구",new(288,18,128,42),()=>Host.OpenResearch(ResearchTree.Catalog.ByHull(Host.PlayerHullId).Id),size:16);
        Button("전장 설정",new(426,18,128,42),()=>Host.ChangeScreen(BattleScreen.Select),size:16);
        string? problem=Host.LaunchProblem();
        var launch=Button("출격",new(650,10,300,50),Host.LaunchFromHangar,true,primary:true,size:20);
        launch.Disabled=problem is not null;launch.SetMeta("focus_key","launch");
        launch.TooltipText=problem??"이 편대로 매칭을 시작합니다 · 전투 규칙, 전장, 진형은 무작위 · 지정하려면 전장 설정";
        Label(problem??"전투 규칙 · 전장 · 진형 무작위",new(560,62,480,20),13,problem is null?Muted:Orange).HorizontalAlignment=HorizontalAlignment.Center;
        var research=Host.Hangar.Research!;
        Label($"지구권 {research.Points[DesignFamily.Earth]:N0} RP  ·  화성권 {research.Points[DesignFamily.Mars]:N0} RP",new(1076,16,500,28),16,Blue)
            .HorizontalAlignment=HorizontalAlignment.Right;
        string match="";
        if(Host.Hangar.EmptySlots()==0)
        {
            var squad=Host.Hangar.Squadron();var bracket=ResearchTree.Catalog.Bracket(squad);
            match=$"편대 전력 {squad.Members.Sum(ResearchTree.Catalog.Score):0.##} · 매칭 T{bracket.Low}–{bracket.High}";
        }
        Label(match,new(1076,46,500,24),15,Muted).HorizontalAlignment=HorizontalAlignment.Right;
    }
    private void BuildSquad()
    {
        var leader=ShipDefinitions.ById(Host.PlayerHullId);
        Label("내 편대",new(24,92,140,30),19);
        Label(leader.Flight.DisplayName,new(150,96,194,24),14,Muted).HorizontalAlignment=HorizontalAlignment.Right;
        for(int i=0;i<Squad.Length;i++)
        {
            int index=i;var member=Squad[i];var at=new Rect2(24,130+i*94,320,86);
            if(member is null)
            {
                var add=Button("+   함급 추가",at,()=>OpenPicker(index),size:17);
                add.AddThemeColorOverride("font_color",Blue);add.SetMeta("focus_key","squad-"+i);
                add.TooltipText=$"편대원 {i+1} · 빈 슬롯 · 넣을 함급을 고릅니다";continue;
            }
            var hull=ShipDefinitions.ById(member.HullId);
            var button=Item(hull.Flight.DisplayName,MenuArt.Ship(hull),at,()=>SelectSlot(index),
                $"{hull.Flight.DisplayName}\n{(i==0?"기함":$"편대원 {i+1}")} · {ModeName(member)}\n"+HullDetails(hull)+"\n\n우클릭 · 함급 교체, 장비 프리셋, 개별 설정",
                Host.HangarSlotIndex==i,portrait:true,status:ModeName(member),marker:i==0?"기함":$"{i+1}");
            button.SetMeta("focus_key","squad-"+i);
            button.GuiInput+=e=>{if(e is InputEventMouseButton{ButtonIndex:MouseButton.Right,Pressed:true}){OpenSlotMenu(index,null);button.AcceptEvent();}};
            var more=Button("⋯",new(at.End.X-(i==0?34:66),at.Position.Y+4,28,26),()=>OpenSlotMenu(index,null),size:15);
            more.SetMeta("focus_key","slot-menu-"+i);more.TooltipText="슬롯 메뉴 · 함급 교체, 장비 프리셋, 개별 설정";
            if(i>0)
            {
                var remove=Button("×",new(at.End.X-34,at.Position.Y+4,28,26),()=>RemoveMember(index),size:16);
                remove.SetMeta("focus_key","slot-remove-"+i);remove.TooltipText="편대에서 제거 · 빈 슬롯은 + 로 다시 채웁니다";
            }
        }
        int empty=Host.Hangar.EmptySlots();
        float y=130+Squad.Length*94;
        Label($"편대 {Squad.Length-empty} / {Squad.Length}척",new(24,y,160,24),15,Muted);
        if(empty>0)
        {
            Label($"빈 슬롯 {empty}개",new(24,y+26,130,24),15,Orange);
            Button("기본 편성으로 채우기",new(160,y+22,184,32),()=>Action(Host.Hangar.FillEmptySlots),size:14).SetMeta("focus_key","fill-squad");
        }
    }
    private PopupMenu? _menu;
    internal PopupMenu? SlotMenu => _menu;
    internal void OpenSlotMenu(int slot,Control? anchor)
    {
        var member=Squad[slot];if(member is null)return;
        _menu?.QueueFree();
        var menu=_menu=new PopupMenu();
        menu.AddThemeFontOverride("font",_font);menu.AddThemeFontSizeOverride("font_size",16);
        menu.AddThemeFontOverride("font_separator",_font);menu.AddThemeFontSizeOverride("font_separator_size",14);
        var hull=ShipDefinitions.ById(member.HullId);
        menu.AddSeparator((slot==0?"기함":$"편대원 {slot+1}")+" · "+hull.Flight.DisplayName);
        menu.AddItem(slot==0?"기함은 아래 내 함선에서 바꿉니다":"함급 교체…",0);menu.SetItemDisabled(menu.GetItemIndex(0),slot==0);
        menu.AddSeparator("장비 설정");
        for(int i=0;i<=HangarSlot.Individual;i++)
        {
            menu.AddRadioCheckItem(i<3?$"공유 프리셋 {i+1}":"개별 설정",10+i);
            menu.SetItemChecked(menu.GetItemIndex(10+i),member.Preset==i);
        }
        if(slot>0){menu.AddSeparator();menu.AddItem("편대에서 제거",20);}
        menu.IdPressed+=id=>
        {
            switch(id)
            {
                case 0:OpenPicker(slot);break;
                case >=10 and <=13:Host.HangarSlotIndex=slot;SelectPreset((int)id-10);break;
                case 20:RemoveMember(slot);break;
            }
        };
        menu.PopupHide+=()=>{menu.QueueFree();if(_menu==menu)_menu=null;};
        AddChild(menu);
        Vector2 at=anchor is null?GetViewport().GetMousePosition():anchor.GetGlobalTransformWithCanvas()*new Vector2(anchor.Size.X,0);
        menu.Popup(new Rect2I((Vector2I)at,Vector2I.Zero));
    }
    private void BuildFittingIcons(ShipDefinition def)
    {
        Label("계통",new(364,544,40,24),15,Muted);
        var parts=Host.Hangar.Fit(Current).Components;
        foreach(var system in Enum.GetValues<ComponentSlot>())
        {
            int index=(int)system;var id=parts?.Get(system);
            if(system==ComponentSlot.MainGun&&id==def.DefaultMainGun)id=null;
            string name=id is null?(system==ComponentSlot.MainGun&&def.DefaultMainGun=="main-laser"?"기본 레이저":"기본 "+ShipComponents.Name(system)):ShipComponents.ById(id).Name;
            var b=Item("",MenuArt.Component(system,id,def),new(404+index*58,530,52,52),()=>ToggleSystem(system),
                $"{ShipComponents.Name(system)}\n{name}\n{(id is null?"기본 장착 계통":ShipComponents.ById(id).Description)}\n\n클릭하여 변경",
                _popover==Popover.System&&_system==system);
            b.SetMeta("focus_key","system-"+system);b.AccessibilityName=ShipComponents.Name(system)+" · "+name;
            if(id is not null)Dot(b);
        }
        Label("베이",new(770,544,40,24),15,Muted);
        for(int i=0;i<def.Bays.Length;i++)
        {
            var bay=def.Bays[i];string? installed=Host.Hangar.Fit(Current).Equipment.FirstOrDefault(m=>m.BayId==bay.Id)?.EquipmentId;
            string equipmentName=installed is null?"빈 베이":ShipEquipment.ById(installed).Name;
            var b=Item("",installed is null?MenuArt.Icon("empty-bay"):MenuArt.Equipment(ShipEquipment.ById(installed).Category),
                new(810+i*58,530,52,52),()=>ToggleBay(bay.Id),
                $"{BayName(bay.Id)} · {SizeName(bay.Size)}\n{equipmentName}\n{(installed is null?"장비를 넣을 수 있습니다":Benefit(ShipEquipment.ById(installed)))}\n\n클릭하여 변경",
                _popover==Popover.Bay&&_bay==bay.Id);
            b.SetMeta("focus_key","bay-"+bay.Id);b.AccessibilityName=BayName(bay.Id)+" · "+equipmentName;
            if(installed is not null)Dot(b);
        }
    }
    private static void Dot(Control icon)=>icon.AddChild(new ColorRect{Position=new(icon.Size.X-11,5),Size=new(6,6),Color=Blue,MouseFilter=MouseFilterEnum.Ignore});
    private void ToggleSystem(ComponentSlot system)
    {
        if(_popover==Popover.System&&_system==system){_popover=Popover.None;Rebuild();return;}
        SelectSystem(system);
    }
    private void ToggleBay(string bay)
    {
        if(_popover==Popover.Bay&&_bay==bay){_popover=Popover.None;_cutaway=false;Rebuild();return;}
        SelectBay(bay);
    }
    private void BuildPopover(ShipDefinition def)
    {
        // A transparent catcher closes the option list on any outside click.
        var catcher=new Control{Position=Vector2.Zero,Size=new(1600,900),MouseFilter=MouseFilterEnum.Stop};
        catcher.GuiInput+=e=>{if(e is InputEventMouseButton{Pressed:true}){_popover=Popover.None;_cutaway=false;Rebuild();}};
        AddChild(catcher);
        bool system=_popover==Popover.System;
        bool parts=system&&_system==ComponentSlot.MainGun&&_partsTab&&def.Battery is not null;
        int count=parts?1+PartRows(def).Length
            :system?1+ShipComponents.All.Count(p=>p.Slot==_system&&ShipComponents.Available(def,p)&&p.Id!=def.DefaultMainGun)
            :1+ShipEquipment.All.Count();
        float height=48+count*66+30+(system&&_system==ComponentSlot.MainGun?36:0)+(parts?40:0);
        float x=system?404+(int)_system*58:810+Array.FindIndex(def.Bays,b=>b.Id==_bay)*58;
        x=Mathf.Min(x,1180-400);
        var panel=new Panel{Position=new(x,522-height),Size=new(400,height)};
        panel.AddThemeStyleboxOverride("panel",new StyleBoxFlat{BgColor=Panel,BorderColor=new(.25f,.45f,.58f),
            BorderWidthBottom=1,BorderWidthTop=1,BorderWidthLeft=1,BorderWidthRight=1,CornerRadiusBottomLeft=4,CornerRadiusBottomRight=4,
            CornerRadiusTopLeft=4,CornerRadiusTopRight=4,ShadowColor=new(0,0,0,.6f),ShadowSize=12});
        AddChild(panel);
        if(system)BuildSystems(def,panel);else BuildBay(def,panel);
    }
    private void BuildSystems(ShipDefinition def,Control panel)
    {
        var research=Host.Hangar.Research!;var fit=Host.Hangar.Fit(Current);
        float top=46;
        if(_system==ComponentSlot.MainGun&&def.Battery is not null)
        {
            top=82;
            foreach(var (label,tab,x) in new[]{("교체 계통",false,10f),("소켓 부품",true,104f)})
            {
                var b=Button(label,new(x,44,90,30),()=>{_partsTab=tab;Rebuild();},_partsTab==tab,size:15,parent:panel);
                b.TooltipText=tab?"소켓마다 구경·포신 수가 다른 주포 부품을 고릅니다":"주포 전체에 적용하는 교체 계통";
                b.SetMeta("focus_key","maingun-tab-"+(tab?"parts":"systems"));
            }
        }
        Label(ShipComponents.Name(_system)+(_partsTab&&top>46?" 부품":" 교체"),new(14,10,220,28),18,Blue,panel);
        Label(Current.UsesOwn?"이 슬롯만":ModeName(Current),new(230,14,156,22),14,Current.UsesOwn?Orange:Muted,panel).HorizontalAlignment=HorizontalAlignment.Right;
        if(_partsTab&&top>46){BuildParts(def,panel,top);return;}
        string? installed=fit.Components?.Get(_system);
        if(_system==ComponentSlot.MainGun&&installed==def.DefaultMainGun)installed=null;
        var stockProblem=SystemProblem(fit,null);
        var stockHull=ShipDefinitions.ById(def.Id);
        string stockName=_system==ComponentSlot.MainGun&&stockHull.DefaultMainGun=="main-laser"?"기본 레이저":"기본 "+ShipComponents.Name(_system);
        var stock=Item(stockName,MenuArt.Component(_system,hull:stockHull),new(10,top,380,62),()=>InstallSystem(null),
            "연구 비용 없이 기본형으로 복귀",installed is null,status:stockProblem is not null?"장착 불가":installed is null?"장착 중":"",parent:panel);
        stock.SetMeta("focus_key","component-stock");
        stock.TooltipText=stockProblem??"연구 비용 없이 기본형으로 복귀";
        stock.Disabled=stockProblem is not null;
        if(!stock.Disabled){var original=FleetLoadouts.ResolveFit(fit with{Components=(fit.Components??new()).With(_system,null)}).Definition;
            stock.TooltipText+="\n"+ComponentComparison.Summary(_system,def,original)+"\n"+ComponentComparison.Costs(def,original);}
        int row=1;
        foreach(var part in ShipComponents.All.Where(p=>p.Slot==_system&&ShipComponents.Available(def,p)&&p.Id!=def.DefaultMainGun))
        {
            bool unlocked=research.HasComponent(def.Id,part.Id);int cost=research.ComponentCost(def.Id,part.Id);
            string? problem=SystemProblem(fit,part.Id);
            if(problem is null&&!unlocked&&research.Points[def.Design]<cost)problem=$"{cost-research.Points[def.Design]:N0} RP 부족 · 필요 {cost:N0}";
            string action=installed==part.Id?"장착 중":unlocked?"장착 가능":cost+" RP · 연구 및 장착";
            string state=problem is not null?(unlocked?"장착 불가":$"{cost:N0} RP · 연구 불가"):action;
            var button=Item(part.Name,MenuArt.Component(_system,part.Id),new(10,top+row++*66,380,62),()=>InstallSystem(part.Id,true),"",
                installed==part.Id,status:state,parent:panel);
            button.SetMeta("focus_key","component-"+part.Id);
            button.Disabled=problem is not null;
            string comparison=part.Description,costs="";
            try{var next=FleetLoadouts.ResolveFit(fit with{Components=(fit.Components??new()).With(_system,part.Id)}).Definition;
                comparison=ComponentComparison.Summary(_system,def,next);costs=ComponentComparison.Costs(def,next);}
            catch(InvalidDataException){/* A rejected fit has no valid resolved numbers to display. */}
            button.TooltipText=part.Name+"\n"+(problem??action)+"\n현재 → 교체 후\n"+comparison+"\n"+costs+"\n기본형 대비: "+part.Description;
            button.AccessibilityDescription=button.TooltipText;
        }
        Label($"보유 {research.Points[def.Design]:N0} RP · 연구 후 교체 무료",new(14,top+row*66,370,24),14,Muted,panel);
    }
    /// <summary>Parts that could go in the selected socket other than its stock part (fit rules shown per row).</summary>
    private MainGunPart[] PartRows(ShipDefinition def)
    {
        _socket=Math.Clamp(_socket,0,def.Battery!.Hardpoints.Length-1);
        var stock=MainGunParts.StockFor(def);
        return MainGunParts.All.Where(p=>p!=stock&&p.Design==def.Design&&p.Branches.Contains(def.Branch)
            &&p.Size<=MainGunParts.SocketSize(def,def.Battery.Hardpoints[_socket])).ToArray();
    }
    private static string SizeLabel(WeaponSize size)=>size switch{WeaponSize.Small=>"소형",WeaponSize.Medium=>"중형",WeaponSize.Large=>"대형",_=>"특대"};
    private static string PartSummary(MainGunPart p)
    {
        string body=p.Railgun is { } r?$"발당 {r.Energy:0} · 관통 {r.PenetrationMm:0} mm · 장전 {r.ReloadSeconds:0.##}초 · 사거리 {r.MaxRange/1000:0} km"
            :$"초당 {p.Laser!.EnergyPerSecond:0} · 관통 {p.Laser.PenetrationMm:0} mm · 감쇠 {p.Laser.FalloffMeters/1000:0} km";
        return $"{(p.CaliberMm>0?$"{p.CaliberMm:0} mm · ":"")}{p.Barrels}포신 · 용적 {p.Volume:0.#} · 정격 {p.RatedMw:0} MW\n부품 기준 {body}";
    }
    /// <summary>Supply ÷ rated after a change, as the player reads it: slower reload or overcharge.</summary>
    private static string SupplyLine(ShipDefinition d)
    {
        if(d.Battery is not { } b)return "";
        float ratio=b.SupplyRatio;
        string effect=ratio<.999f?$"장전 ×{1/ratio:0.00}":ratio>1.001f?$"과충전 ×{MainBatteries.Overcharge(ratio):0.00} · 발열 ×{MathF.Pow(MainBatteries.Overcharge(ratio),2):0.00}":"정격";
        float used=b.SocketWeapons?.Sum(w=>MainGunParts.Find(w.Id)?.Volume??0)??MainGunParts.StockVolume(d);
        return $"공급 ÷ 정격 {ratio:0.00} · {effect}\n무장 용적 {used:0.#} / {MainGunParts.VolumeBudget(d):0.#}";
    }
    private void BuildParts(ShipDefinition def,Control panel,float top)
    {
        var research=Host.Hangar.Research!;var fit=Host.Hangar.Fit(Current);var b=def.Battery!;
        var rows=PartRows(def);var stock=MainGunParts.StockFor(def);
        for(int i=0;i<b.Hardpoints.Length;i++)
        {
            int socket=i;var h=b.Hardpoints[i];
            var tab=Button($"{i+1} · {SizeLabel(MainGunParts.SocketSize(def,h))}",new(10+i*94,top,90,32),()=>{_socket=socket;Rebuild();},_socket==i,size:15,parent:panel);
            tab.TooltipText=$"소켓 {i+1} · {SizeLabel(MainGunParts.SocketSize(def,h))} · 용적 {MainGunParts.SocketVolume(MainGunParts.SocketSize(def,h)):0}\n장착: {MainGunParts.Find(b.WeaponAt(i).Id)?.Name??stock.Name}";
            tab.SetMeta("focus_key","maingun-socket-"+i);
        }
        top+=40;
        string current=fit.Components?.MainGuns?[_socket]??stock.Id;
        string? Problem(string? id)
        {
            try{FleetLoadouts.ResolveFit(fit with{Components=HangarProfile.WithMainGun(def.Id,fit.Components,_socket,id)});return null;}
            catch(InvalidDataException e){return e.Message;}
        }
        string Tooltip(MainGunPart p,string? problem)
        {
            string after="";
            try{after=SupplyLine(FleetLoadouts.ResolveFit(fit with{Components=HangarProfile.WithMainGun(def.Id,fit.Components,_socket,p==stock?null:p.Id)}).Definition);}
            catch(InvalidDataException){}
            return p.Name+"\n"+(problem??"")+"\n"+PartSummary(p)+(after.Length>0?"\n장착 후 "+after:"");
        }
        var stockButton=Item(stock.Name+" (기본)",MenuArt.Component(ComponentSlot.MainGun,hull:def),new(10,top,380,62),()=>InstallPart(_socket,null),
            "",current==stock.Id,status:current==stock.Id?"장착 중":"",parent:panel);
        stockButton.SetMeta("focus_key","part-stock");stockButton.TooltipText=Tooltip(stock,null);
        int row=1;
        foreach(var part in rows)
        {
            bool unlocked=research.HasPart(def.Design,part.Id);int cost=research.PartCost(part.Id);
            string? problem=Problem(part.Id);
            if(problem is null&&!unlocked&&research.PartSponsor(def.Design,part.Id) is null)problem=$"T{part.MinTier} 이상 연구 가능 함급 필요";
            if(problem is null&&!unlocked&&research.Points[def.Design]<cost)problem=$"{cost-research.Points[def.Design]:N0} RP 부족 · 필요 {cost:N0}";
            string action=current==part.Id?"장착 중":unlocked?"장착 가능":cost+" RP · 진영 공유 연구 및 장착";
            var button=Item(part.Name,MenuArt.Component(ComponentSlot.MainGun,hull:def),new(10,top+row++*66,380,62),()=>InstallPart(_socket,part.Id,true),"",
                current==part.Id,status:problem is not null?(unlocked?"장착 불가":"연구 불가"):action,parent:panel);
            button.SetMeta("focus_key","part-"+part.Id);button.Disabled=problem is not null;
            button.TooltipText=Tooltip(part,problem??action);button.AccessibilityDescription=button.TooltipText;
        }
        Label($"보유 {research.Points[def.Design]:N0} RP · 부품 연구는 진영 전체 공유",new(14,top+row*66,370,24),14,Muted,panel);
    }
    internal void InstallPart(int socket,string? id,bool research=false)
    {
        if(MenuConfirmation.Pending(this) is not null)return;
        var target=Current;int index=SlotIndex(target);var fit=Host.Hangar.Fit(target);
        try
        {
            var before=FleetLoadouts.ResolveFit(fit).Definition;var stock=MainGunParts.StockFor(before);
            string old=fit.Components?.MainGuns?[socket]??stock.Id;
            if(old==(id??stock.Id))return;
            var after=FleetLoadouts.ResolveFit(fit with{Components=HangarProfile.WithMainGun(target.HullId,fit.Components,socket,id)}).Definition;
            var progress=Host.Hangar.Research!;
            long cost=id is not null&&!progress.HasPart(before.Design,id)?progress.PartCost(id):0;
            if(cost>0&&!research)throw new InvalidDataException("부품을 먼저 연구해야 합니다.");
            if(progress.Points[before.Design]<cost)throw new InvalidDataException("연구 점수가 부족합니다.");
            string scope=target.UsesOwn?"이 슬롯에만 적용됩니다.":"같은 프리셋을 사용하는 함선에 함께 적용됩니다.";
            MenuConfirmation.Show(this,_font,cost>0?"주포 부품 연구 및 장착 확인":"주포 부품 교체 확인",
                FitScope(target)+$"\n소켓 {socket+1} · {MainGunParts.ById(old).Name} → {MainGunParts.ById(id??stock.Id).Name}\n\n"+
                SupplyLine(before)+"\n→ "+SupplyLine(after)+"\n\n"+ComponentComparison.Fitting(ComponentSlot.MainGun,before,after),
                MenuConfirmation.Price(before.Design,progress.Points[before.Design],cost),
                cost>0?scope+" 부품 연구는 같은 진영 함급 모두에 열리며 환불되지 않습니다.":scope+" 연구 점수 소모 없이 다시 변경할 수 있습니다.",
                cost>0?"연구하고 장착":"교체 적용",()=>Action(()=>
                {
                    if(target.UsesOwn)Host.Hangar.SetSlotMainGun(index,socket,id,research);
                    else Host.Hangar.SetMainGun(target.HullId,target.Preset,socket,id,research);
                }));
        }
        catch(Exception e)when(e is InvalidDataException or ArgumentException){Error(e);}
    }
    private void BuildBay(ShipDefinition def,Control panel)
    {
        var selected=def.Bays.First(b=>b.Id==_bay);
        Label($"{BayName(selected.Id)} · {SizeName(selected.Size)}",new(14,10,240,28),18,Blue,panel);
        Label(Current.UsesOwn?"이 슬롯만":ModeName(Current),new(230,14,156,22),14,Current.UsesOwn?Orange:Muted,panel).HorizontalAlignment=HorizontalAlignment.Right;
        bool empty=!def.InstalledEquipment.Any(m=>m.BayId==selected.Id);
        var clear=Item("베이 비우기",MenuArt.Icon("empty-bay"),new(10,46,380,62),()=>Install(null),"장착한 장비를 내립니다",empty,
            status:empty?"비어 있음":"",parent:panel);
        clear.SetMeta("focus_key","equipment-none");
        int row=1;
        foreach(var device in ShipEquipment.All)
        {
            string? reason=Compatibility(def,selected,device);
            bool installed=def.InstalledEquipment.Any(m=>m.BayId==selected.Id&&m.Equipment.Id==device.Id);
            var button=Item(device.Name,MenuArt.Equipment(device.Category),new(10,46+row++*66,380,62),()=>Install(device.Id),
                device.Name+"\n"+(reason??(installed?"장착 중":"클릭하여 장착"))+"\n"+Benefit(device)+
                $"\n소비 {device.PowerMw:0.#} MW · 추가 폐열 {device.HeatMw:0.#} MW · 질량 {device.MassKg/1000:0.#} t\n"+Risk(device),installed,
                status:reason is not null?"장착 불가":installed?"장착 중":"",parent:panel);
            button.Disabled=reason is not null;
            button.SetMeta("focus_key","equipment-"+device.Id);
        }
        Label(string.Join(" / ",selected.Accepts.Select(Category))+" 장착 가능",new(14,46+row*66,370,24),14,Muted,panel);
    }
    private void BuildPicker(int slot)
    {
        var dim=new ColorRect{Position=Vector2.Zero,Size=new(1600,900),Color=new(.004f,.015f,.03f,.82f),MouseFilter=MouseFilterEnum.Stop};
        dim.GuiInput+=e=>{if(e is InputEventMouseButton{Pressed:true})CloseOverlay();};
        AddChild(dim);
        var panel=new Panel{Position=new(230,96),Size=new(1140,708)};
        panel.AddThemeStyleboxOverride("panel",new StyleBoxFlat{BgColor=new(.02f,.05f,.075f),BorderColor=new(.25f,.45f,.58f),
            BorderWidthBottom=1,BorderWidthTop=1,BorderWidthLeft=1,BorderWidthRight=1,CornerRadiusBottomLeft=6,CornerRadiusBottomRight=6,
            CornerRadiusTopLeft=6,CornerRadiusTopRight=6});
        AddChild(panel);
        var leader=ShipDefinitions.ById(Host.PlayerHullId);
        Label($"편대원 {slot+1} · 함급 선택",new(24,18,600,34),24,Ink,panel);
        Label(leader.Kind==HullKind.Battleship?"전투단: 같은 진영 호위함·순양함, 기함 이하 티어 · 같은 함급 중복 가능":"같은 진영 같은 함종 · 같은 함급 중복 가능",
            new(24,54,800,24),15,Muted,panel);
        Button("닫기",new(1020,20,96,38),()=>CloseOverlay(),size:16,parent:panel).SetMeta("focus_key","picker-close");
        var hulls=FleetLoadouts.Hulls.Where(h=>h.Design==leader.Design)
            .Select(h=>(hull:h,reason:PickBlocked(slot,h),tier:ResearchTree.Catalog.ByHull(h.Id).Tier))
            .OrderBy(x=>x.reason is not null).ThenByDescending(x=>x.tier).ThenBy(x=>x.hull.Kind).ToArray();
        var scroll=new ScrollContainer{Position=new(20,92),Size=new(1100,596),HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};
        panel.AddChild(scroll);
        var grid=new GridContainer{Columns=6};grid.AddThemeConstantOverride("h_separation",10);grid.AddThemeConstantOverride("v_separation",10);
        scroll.AddChild(grid);
        foreach(var (hull,reason,tier) in hulls)
        {
            var card=new MenuItemButton{Caption=hull.Flight.DisplayName,Picture=MenuArt.Ship(hull),Typeface=_font,Portrait=true,
                Detail=reason??$"T{tier} · {FleetLoadouts.RoleName(hull.Branch)}",Marker=$"T{tier}",
                TooltipText=$"{hull.Flight.DisplayName}\n{(reason is null?"편성 가능 · 공유 프리셋 1로 들어갑니다":"편성 불가 · "+reason+(reason=="미해금"?" · 기술 연구에서 해금합니다":""))}\n"+HullDetails(hull),
                CustomMinimumSize=new(172,150),Size=new(172,150)};
            MenuStyles.Apply(card,_font,reason is null?Ink:Muted);
            card.SetMeta("focus_key","pick-"+hull.Id);card.Disabled=reason is not null;
            if(reason is not null)card.Modulate=new(1,1,1,.45f);
            string id=hull.Id;card.Pressed+=()=>PickHull(id);
            grid.AddChild(card);
        }
        var first=hulls.FirstOrDefault(x=>x.reason is null).hull;
        MenuStyles.RestoreFocus(this,first is not null?"pick-"+first.Id:"picker-close");
    }
    private void BuildCarousel()
    {
        Label("내 함선",new(24,668,90,28),18);
        (string label,HullKind? kind)[] kinds={("전체",null),("전함",HullKind.Battleship),("순양·호위",HullKind.Escort),("요격·정찰",HullKind.Interceptor)};
        float x=118;
        foreach(var (label,kind) in kinds)
        {
            var k=kind;float w=label.Length>2?96:64;
            Button(label,new(x,666,w,32),()=>{_kindFilter=k;_hiddenTray=false;Rebuild();},!_hiddenTray&&_kindFilter==k,size:14).SetMeta("focus_key","filter-"+label);
            x+=w+6;
        }
        x+=10;
        (string label,DesignFamily? design)[] designs={("양 진영",null),("지구권",DesignFamily.Earth),("화성권",DesignFamily.Mars)};
        foreach(var (label,design) in designs)
        {
            var d=design;
            Button(label,new(x,666,78,32),()=>{_designFilter=d;_hiddenTray=false;Rebuild();},!_hiddenTray&&_designFilter==d,size:14).SetMeta("focus_key","filter-"+label);
            x+=84;
        }
        var hidden=Host.Hangar.Hidden;
        Button($"숨긴 함선 {hidden.Length}",new(x+10,666,120,32),()=>{_hiddenTray=!_hiddenTray;Rebuild();},_hiddenTray,size:14).SetMeta("focus_key","hidden-tray");
        Label(_hiddenTray?"숨긴 함선 · 다시 표시하면 원래 자리로 돌아갑니다":"★ 즐겨찾기는 항상 앞 · 드래그로 순서 변경 · ◌ 로 숨기기",
            new(1000,670,576,24),14,Muted).HorizontalAlignment=HorizontalAlignment.Right;
        var scroll=new ScrollContainer{Position=new(24,706),Size=new(1552,180),VerticalScrollMode=ScrollContainer.ScrollMode.Disabled};
        AddChild(scroll);
        var row=new HBoxContainer();row.AddThemeConstantOverride("separation",10);scroll.AddChild(row);
        if(_hiddenTray)
        {
            if(hidden.Length==0)
            {
                var none=Label("숨긴 함선이 없습니다. 카드의 ◌ 버튼으로 숨길 수 있습니다.",new(0,0,800,30),15,Muted,row);
                none.CustomMinimumSize=new(800,30);
            }
            foreach(string id in hidden)
            {
                var hull=ShipDefinitions.ById(id);var cell=new Control{CustomMinimumSize=new(168,160)};row.AddChild(cell);
                var card=Item(hull.Flight.DisplayName,MenuArt.Ship(hull),new(0,0,168,120),null,HullDetails(hull),portrait:true,
                    marker:$"T{ResearchTree.Catalog.ByHull(id).Tier}",parent:cell);
                card.Modulate=new(1,1,1,.6f);card.SetMeta("focus_key","hidden-"+id);
                string hullId=id;
                Button("다시 표시",new(0,126,168,32),()=>Action(()=>Host.Hangar.SetHidden(hullId,false)),size:14,parent:cell).SetMeta("focus_key","show-"+id);
            }
            return;
        }
        foreach(string id in Host.Hangar.ShipList())
        {
            var hull=ShipDefinitions.ById(id);
            if(_kindFilter is { } kind&&hull.Kind!=kind||_designFilter is { } design&&hull.Design!=design)continue;
            bool favorite=Host.Hangar.Favorites.Contains(id);int tier=ResearchTree.Catalog.ByHull(id).Tier;
            var cell=new Control{CustomMinimumSize=new(168,160)};row.AddChild(cell);
            string hullId=id;
            var card=new HangarShipCard{HullId=id,Caption=hull.Flight.DisplayName,Picture=MenuArt.Ship(hull),Typeface=_font,Portrait=true,
                Detail=FleetLoadouts.RoleName(hull.Branch),Marker=$"T{tier}",Position=Vector2.Zero,Size=new(168,160),
                TooltipText=$"{hull.Flight.DisplayName}\nT{tier} · {FleetLoadouts.RoleName(hull.Branch)} · {(hull.Design==DesignFamily.Earth?"지구권":"화성권")}\n클릭: 이 함선을 기함으로 편대 열기\n드래그: 순서 변경"};
            MenuStyles.Apply(card,_font,id==Host.PlayerHullId?Blue:Ink,accent:id==Host.PlayerHullId);
            card.SetMeta("focus_key","ship-"+id);
            card.Pressed+=()=>Host.ChooseHull(hullId);
            card.Dropped+=dragged=>Action(()=>Host.Hangar.MoveShip(dragged,hullId));
            cell.AddChild(card);
            var star=Button(favorite?"★":"☆",new(134,4,30,28),()=>Action(()=>Host.Hangar.SetFavorite(hullId,!favorite)),favorite,size:16,parent:cell);
            star.AddThemeColorOverride("font_color",favorite?new(1,.79f,.3f):Muted);
            star.TooltipText=favorite?"즐겨찾기 해제":"즐겨찾기 · 항상 앞에 정렬";star.SetMeta("focus_key","favorite-"+id);
            var hide=Button("◌",new(100,4,30,28),()=>Action(()=>Host.Hangar.SetHidden(hullId,true)),size:15,parent:cell);
            hide.TooltipText=id==Host.PlayerHullId?"현재 기함은 숨길 수 없습니다":"목록에서 숨기기 · 숨긴 함선에서 다시 표시";
            hide.Disabled=id==Host.PlayerHullId;hide.SetMeta("focus_key","hide-"+id);
        }
    }
    private string? Compatibility(ShipDefinition def,EquipmentBay bay,EquipmentDefinition device)
    {
        if(device.Size>bay.Size)return "베이 크기가 부족합니다.";
        if(!bay.Accepts.Contains(device.Category))return "이 베이에서 지원하지 않는 장비";
        if(device.Designs is not null&&!device.Designs.Contains(def.Design))return "다른 진영 전용 장비입니다.";
        var next=Host.Hangar.Fit(Current).Equipment.Where(m=>m.BayId!=bay.Id).Append(new EquipmentMount(bay.Id,device.Id)).ToArray();
        try{FleetLoadouts.ResolveFit(Host.Hangar.Fit(Current) with{Equipment=next});return null;}
        catch(InvalidDataException){return "전력·열 예산 또는 운용 조건 부족";}
    }
    private string? SystemProblem(ShipFit fit,string? id)
    {
        try{FleetLoadouts.ResolveFit(fit with{Components=(fit.Components??new()).With(_system,id)});return null;}
        catch(InvalidDataException){return "전력 예산 또는 장착 조건 부족";}
    }
    private void BuildStats(ShipDefinition def)
    {
        Label("성능",new(1200,92,200,30),19);
        Label("세로선은 기본형",new(1376,96,200,24),14,Muted).HorizontalAlignment=HorizontalAlignment.Right;
        var baseline=ShipDefinitions.ById(def.Id);var gear=def.InstalledEquipment.Select(m=>m.Equipment).ToArray();
        float budget=def.Power.Engines+def.Power.Shields+def.Power.Weapons+def.Power.Sensors;
        Meter("전력",budget+gear.Sum(d=>d.PowerMw),def.Power.OutputMw,budget,"MW",136);
        Meter("추가 폐열",gear.Sum(d=>d.HeatMw),def.Power.CoolingMw,0,"MW",192);
        Meter("가속",def.Flight.ForwardAccel,Mathf.Max(baseline.Flight.ForwardAccel*1.25f,def.Flight.ForwardAccel),baseline.Flight.ForwardAccel,"m/s²",248);
        Meter("실드",def.Shield.Capacity*(1+gear.Sum(d=>d.Effects.ShieldCapacityFraction)),baseline.Shield.Capacity*2,baseline.Shield.Capacity,"",304);
        Item("함선 상세",null,new(1200,370,376,40),null,HullDetails(def));
    }
    private static string HullDetails(ShipDefinition def)=>$"{def.Flight.DisplayName}\n{FleetLoadouts.RoleName(def.Branch)} · 전장 {def.Flight.Length:0} m\n"+
        $"미사일 {def.Missiles?.Rounds??0} · AM {def.Antimatter?.Rounds??0} · 프로브 {def.ReconProbes?.Count??0}\n"+
        $"센서 {def.Sensors.Strength*(1+def.InstalledEquipment.Sum(m=>m.Equipment.Effects.SensorStrengthFraction)):0} · 열용량 {def.Power.HeatCapacityMj:0} MJ\n드론 {def.DefenseDrones?.Count??0}";
    private void Meter(string name,float value,float maximum,float baseline,string unit,float y)
    {
        Label(name,new(1200,y,130,25),16);
        string valueText=name is "가속" or "실드"?$"{value:0.##} {unit} · 기본 {baseline:0.##}":$"{value:0.#} / {maximum:0.#} {unit}";
        var label=Label(valueText,new(1300,y,276,25),15,Muted);label.HorizontalAlignment=HorizontalAlignment.Right;
        AddChild(new ColorRect{Position=new(1200,y+28),Size=new(376,8),Color=new(.08f,.14f,.19f),MouseFilter=MouseFilterEnum.Ignore});
        AddChild(new ColorRect{Position=new(1200,y+28),Size=new(376*Mathf.Clamp(value/maximum,0,1),8),Color=name=="추가 폐열"?Orange:Blue,MouseFilter=MouseFilterEnum.Ignore});
        if(baseline>0)AddChild(new ColorRect{Position=new(1200+Mathf.Clamp(baseline/maximum,0,1)*376-1,y+25),Size=new(2,14),Color=Ink,MouseFilter=MouseFilterEnum.Ignore});
    }
    internal static string BayName(string id)=>id switch{"utility-port"=>"좌현 다목적","utility-starboard"=>"우현 다목적","sensor"=>"센서 베이","hangar"=>"드론 베이",_=>id};
    private static string SizeName(BaySize size)=>size switch{BaySize.Small=>"소형",BaySize.Medium=>"중형",BaySize.Large=>"대형",_=>"특대형"};
    private static string Category(EquipmentCategory c)=>c switch{EquipmentCategory.Shield=>"실드",EquipmentCategory.Sensor=>"센서",EquipmentCategory.Ecm=>"ECM",_=>"드론"};
    private static string Benefit(EquipmentDefinition d)=>d.Category switch{EquipmentCategory.Shield=>$"용량 +{d.Effects.ShieldCapacityFraction*100:0}%",EquipmentCategory.Sensor=>$"탐지 +{d.Effects.SensorStrengthFraction*100:0}%",EquipmentCategory.Ecm=>$"강도 +{d.Effects.Jammer:0.#}",_=>$"드론 +{d.Effects.Drones}"};
    private static string Risk(EquipmentDefinition d)=>d.Category switch{EquipmentCategory.Shield=>$"재충전 지연 +{d.Effects.ShieldDelaySeconds:0.#}초",EquipmentCategory.Sensor=>$"노출 신호 +{d.Effects.SignatureFraction*100:0}%",EquipmentCategory.Drone=>"손상 시 유폭 위험 · 전투 중 드론 보충 없음",_=>"사용 중 추가 전력·폐열 발생"};
    public override void _Process(double delta)
    {var viewport=GetViewportRect().Size;float scale=Mathf.Min(viewport.X/1600,viewport.Y/900);Scale=Vector2.One*scale;Position=(viewport-new Vector2(1600,900)*scale)*.5f;}
    public override void _Draw()
    {
        DrawRect(new(0,0,1600,900),new(.012f,.025f,.043f));
        DrawRect(new(0,0,1600,84),new(.018f,.035f,.055f));DrawLine(new(0,84),new(1600,84),Line);
        DrawLine(new(354,96),new(354,650),Line);DrawLine(new(1190,96),new(1190,650),Line);
        DrawLine(new(24,658),new(1576,658),Line);
    }
}

/// <summary>"My ships" card that can be dragged onto another card to reorder the carousel.</summary>
public partial class HangarShipCard : MenuItemButton
{
    public string HullId { get; init; }="";
    public event Action<string>? Dropped;
    public override Variant _GetDragData(Vector2 atPosition)
    {
        SetDragPreview(new Label{Text=Caption,Modulate=new(1,1,1,.85f)});
        return HullId;
    }
    public override bool _CanDropData(Vector2 atPosition,Variant data)=>data.VariantType==Variant.Type.String&&data.AsString()!=HullId;
    public override void _DropData(Vector2 atPosition,Variant data)=>Dropped?.Invoke(data.AsString());
}
