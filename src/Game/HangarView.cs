using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

/// <summary>Three presets per hull, squad member references and physical bay fitting.</summary>
public partial class HangarView : Control
{
    public Main Host { get; set; }=null!;
    private Font _font=null!;
    private Label _status=null!;
    private HangarPreview _preview=null!;
    private string? _bay;
    private bool _cutaway;
    private bool _systems=true;
    private ComponentSlot _system=ComponentSlot.MainGun;
    private static readonly Color Ink=new(.81f,.89f,.96f),Blue=new(.35f,.75f,1),Orange=new(1,.7f,.32f),Muted=MenuStyles.Muted;
    private HangarSlot Current => Host.Hangar.Hulls[Host.PlayerHullId].Squadron[Host.HangarSlotIndex];
    internal ResolvedLoadout CurrentFit => FleetLoadouts.ResolveFit(Host.Hangar.Fit(Current));
    internal string? SelectedBay => _bay;
    internal HangarPreview Preview => _preview;
    public override void _Ready()
    {
        _font=new SystemFont{FontNames=new[]{"Malgun Gothic","Segoe UI"}};
        if(OS.GetCmdlineUserArgs().Contains("--hangar-exterior"))_cutaway=false;
        var args=BattleArgs.Parse(OS.GetCmdlineUserArgs());
        if(args.ContainsKey("shot")&&args.TryGetValue("hangar-system",out var system))_system=Enum.Parse<ComponentSlot>(system);
        if(args.ContainsKey("shot")&&args.TryGetValue("hangar-bay",out var bay)){_bay=bay;_systems=false;_cutaway=true;}
        Size=new(1600,900);Rebuild();
        if(args.ContainsKey("shot")&&args.ContainsKey("hangar-focus"))MenuStyles.RestoreFocus(this,"system-"+_system);
    }
    private Label Label(string text,Rect2 r,int size=18,Color? color=null)
    {
        var label=new Label{AutowrapMode=TextServer.AutowrapMode.WordSmart,Text=text,
            Position=r.Position,Size=r.Size,MouseFilter=MouseFilterEnum.Ignore};
        label.AddThemeFontOverride("font",_font);label.AddThemeFontSizeOverride("font_size",size);
        label.AddThemeColorOverride("font_color",color??Ink);AddChild(label);return label;
    }
    private Button Button(string text,Rect2 r,Action action,bool selected=false,bool primary=false)
    {
        var b=new Button{Text=text,Position=r.Position,Size=r.Size,ClipText=true,TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis};
        b.AddThemeFontSizeOverride("font_size",18);
        MenuStyles.Apply(b,_font,selected?Blue:Muted,primary,accent:selected);
        b.AddThemeColorOverride("font_color",primary?new(.025f,.055f,.085f):selected?Blue:Ink);
        b.Pressed+=action;AddChild(b);return b;
    }
    private MenuItemButton Item(string name,Texture2D? picture,Rect2 r,Action? action,string details,
        bool selected=false,bool portrait=false,string status="",string marker="")
    {
        var b=new MenuItemButton{Caption=name,Picture=picture,Typeface=_font,Portrait=portrait,
            Detail=status,Marker=marker,TooltipText=details,Position=r.Position,Size=r.Size};
        MenuStyles.Apply(b,_font,selected?Blue:Ink,accent:selected);
        b.SetMeta("focus_key",name);
        b.Pressed+=action??b.ShowDetails;AddChild(b);return b;
    }
    private void Action(Action edit)
    {
        try{edit();Host.SaveHangar();Rebuild();}
        catch(Exception e)when(e is InvalidDataException or ArgumentException)
        {_status.Text="변경할 수 없습니다 · "+e.Message;_status.Modulate=Orange;}
    }
    internal void SelectSlot(int slot)
    {Host.HangarSlotIndex=slot;_bay=null;Rebuild();}
    internal void SelectBay(string bay){_bay=bay;_systems=false;_cutaway=true;Rebuild();}
    internal void SelectSystem(ComponentSlot slot){_system=slot;_systems=true;_cutaway=false;Rebuild();}
    internal void InstallSystem(string? id,bool research=false)
    {
        if(MenuConfirmation.Pending(this) is not null)return;
        var target=Current;var system=_system;var fit=Host.Hangar.Fit(target);var old=fit.Components?.Get(system);
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
            MenuConfirmation.Show(this,_font,cost>0?"부품 연구 및 교체 확인":"부품 교체 확인",
                FitScope(target)+$"\n{ShipComponents.Name(system)} · {Name(old)} → {Name(id)}\n\n"+
                ComponentComparison.Fitting(system,before,after),
                MenuConfirmation.Price(before.Design,progress.Points[before.Design],cost),
                cost>0?"공유 프리셋에 함께 적용됩니다. 연구 점수는 환불되지 않으며, 연구한 부품의 재장착은 무료입니다.":"같은 프리셋을 사용하는 함선에 함께 적용됩니다. 연구 점수 소모 없이 다시 변경할 수 있습니다.",
                cost>0?"연구하고 장착":"교체 적용",()=>Action(()=>Host.Hangar.SetComponent(target.HullId,target.Preset,system,id,research)));
        }
        catch(Exception e)when(e is InvalidDataException or ArgumentException)
        {_status.Text="변경할 수 없습니다 · "+e.Message;_status.Modulate=Orange;}
    }
    internal void SelectPreset(int preset)=>Action(()=>Host.Hangar.SetSlot(Host.HangarSlotIndex,Current.HullId,preset));
    internal void Install(string? device)
    {
        if(_bay is null||MenuConfirmation.Pending(this) is not null)return;
        var target=Current;string bay=_bay;var fit=Host.Hangar.Fit(target);
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
                "같은 프리셋을 사용하는 함선에 함께 적용됩니다. 격납고에서 다시 변경할 수 있습니다.",
                device is null?"장비 해제":"장착 적용",()=>Action(()=>Host.Hangar.SetEquipment(target.HullId,target.Preset,bay,device)));
        }
        catch(Exception e)when(e is InvalidDataException or ArgumentException)
        {_status.Text="변경할 수 없습니다 · "+e.Message;_status.Modulate=Orange;}
    }
    private string FitScope(HangarSlot target)
    {
        var members=Host.Hangar.Hulls[Host.PlayerHullId].Squadron.Select((slot,index)=>(slot,index))
            .Where(m=>m.slot==target).Select(m=>m.index==0?"기함":$"편대원 {m.index}");
        return $"{ShipDefinitions.ById(target.HullId).Flight.DisplayName} · 프리셋 {target.Preset+1}\n적용 대상: {string.Join(", ",members)}";
    }
    internal void ChangeMember(string hull)
    {
        if(Host.HangarSlotIndex==0){Host.ChooseHull(hull);return;}
        Action(()=>Host.Hangar.SetSlot(Host.HangarSlotIndex,hull,0));
    }
    private void Rebuild()
    {
        string? focus=MenuStyles.FocusKey(this);
        float yaw=_preview?.Yaw??-.9f,pitch=_preview?.Pitch??.42f,zoom=_preview?.Zoom??1.35f;
        foreach(Node child in GetChildren()){RemoveChild(child);child.QueueFree();}
        var state=Host.Hangar.Hulls[Host.PlayerHullId];Host.HangarSlotIndex=Math.Clamp(Host.HangarSlotIndex,0,state.Squadron.Length-1);
        var slot=Current;var fit=CurrentFit;var def=fit.Definition;
        if(!def.Bays.Any(b=>b.Id==_bay))_bay=def.Bays.FirstOrDefault()?.Id;
        Label("격납고",new(40,28,260,55),34);
        Label("내 편대",new(40,88,280,30),17,Muted);
        Label(def.Flight.DisplayName,new(340,27,800,50),30);
        Label($"{(def.Design==DesignFamily.Earth?"지구권":"화성권")}  ·  {(Host.HangarSlotIndex==0?"기함":$"편대원 {Host.HangarSlotIndex}")}  ·  프리셋 {slot.Preset+1}",new(340,79,550,30),18,Blue);
        Label($"T{ResearchTree.Catalog.ByHull(def.Id).Tier} · 보유 함급 {Host.Hangar.Owned.Count} / {FleetLoadouts.Hulls.Count()}",new(1170,40,385,35),18,Muted);
        Button("기술 연구",new(1170,108,385,44),()=>Host.OpenResearch(ResearchTree.Catalog.ByHull(def.Id).Id));
        var leader=ShipDefinitions.ById(Host.PlayerHullId);var owned=Host.Hangar.Owned;
        var hulls=FleetLoadouts.Hulls.Where(h=>owned.Contains(h.Id)&&(Host.HangarSlotIndex==0
            ||FleetLoadouts.Allows(leader,Host.HangarSlotIndex,h)&&(leader.MatchClass!=MatchWeightClass.Large||ResearchTree.Catalog.ByHull(h.Id).Tier<=ResearchTree.Catalog.ByHull(leader.Id).Tier))).ToArray();
        var select=new OptionButton{Position=new(40,136),Size=new(270,48)};
        select.AddThemeFontOverride("font",_font);select.AddThemeFontSizeOverride("font_size",17);
        for(int i=0;i<hulls.Length;i++){select.AddItem(hulls[i].Flight.DisplayName);if(hulls[i].Id==slot.HullId)select.Select(i);}
        select.ItemSelected+=i=>ChangeMember(hulls[(int)i].Id);AddChild(select);
        Label(Host.HangarSlotIndex==0?"함급 선택 · 편대 세팅 불러오기":"같은 진영 · 슬롯에 맞는 함급",new(40,187,280,24),14,Muted);
        for(int i=0;i<state.Squadron.Length;i++)
        {
            int index=i;var member=state.Squadron[i];var hull=ShipDefinitions.ById(member.HullId);
            Item(hull.Flight.DisplayName,MenuArt.Ship(hull),new(40,220+i*94,270,88),()=>SelectSlot(index),
                $"{hull.Flight.DisplayName}\n{(i==0?"기함":$"편대원 {i}")} · 프리셋 {member.Preset+1}\n"+HullDetails(hull),
                Host.HangarSlotIndex==i,portrait:true,marker:i==0?"기함":$"{i+1}").SetMeta("focus_key","squad-"+i);
        }
        var squadInfo=Label($"편대 {state.Squadron.Length}척",new(40,708,270,30),16,Muted);
        squadInfo.MouseFilter=MouseFilterEnum.Pass;squadInfo.TooltipText="기함 격침 시 생존 함선으로 인계합니다. 장비와 피해 상태는 유지됩니다.";
        for(int i=0;i<3;i++){int preset=i;Button($"프리셋 {i+1}",new(340+i*265,125,250,42),()=>SelectPreset(preset),slot.Preset==i);}
        _preview=new HangarPreview{Position=new(340,190),Size=new(780,414),Definition=def,Cutaway=_cutaway,ShowBayMarkers=!_systems||_cutaway,SelectedBay=_bay,Yaw=yaw,Pitch=pitch,Zoom=zoom};
        _preview.BaySelected+=SelectBay;AddChild(_preview);
        Button(_cutaway?"외형으로 보기":"단면으로 보기",new(925,70,195,38),()=>{_cutaway=!_cutaway;Rebuild();},_cutaway).SetMeta("focus_key","preview-mode");
        Label(_systems?"드래그 회전 · 휠 확대":"드래그 회전 · 휠 확대 · 베이 클릭",new(355,571,600,30),15,Muted);
        Button("계통 교체",new(340,615,235,36),()=>{_systems=true;_cutaway=false;Rebuild();},_systems);
        Button("베이 장비",new(590,615,235,36),()=>{_systems=false;_cutaway=true;Rebuild();},!_systems);
        if(_systems)
        {
            var parts=Host.Hangar.Fit(Current).Components;
            foreach(var system in Enum.GetValues<ComponentSlot>())
            {
                int index=(int)system;var id=parts?.Get(system);
                string name=id is null?"기본 "+ShipComponents.Name(system):ShipComponents.ById(id).Name;
                Item(name,MenuArt.Component(system,id,def),new(340+index%3*265,658+index/3*58,250,52),()=>SelectSystem(system),
                    name+"\n"+ShipComponents.Name(system)+" · 교체 부품 선택\n"+(id is null?"기본 장착 계통":ShipComponents.ById(id).Description),
                    _system==system).SetMeta("focus_key","system-"+system);
            }
        }
        else for(int i=0;i<def.Bays.Length;i++)
        {
            var bay=def.Bays[i];string? installed=slot is not null?Host.Hangar.Fit(slot).Equipment.FirstOrDefault(m=>m.BayId==bay.Id)?.EquipmentId:null;
            string equipmentName=installed is null?"빈 베이":ShipEquipment.ById(installed).Name;
            Item(equipmentName,installed is null?MenuArt.Icon("empty-bay"):MenuArt.Equipment(ShipEquipment.ById(installed).Category),
                new(340+i*197,660,185,106),()=>SelectBay(bay.Id),BayName(bay.Id)+"\n"+equipmentName+"\n"+SizeName(bay.Size),
                _bay==bay.Id,portrait:true,status:BayName(bay.Id)).SetMeta("focus_key","bay-"+bay.Id);
        }
        var help=Label("장비에 마우스를 올리면 상세 · 키보드 선택 후 F1",new(340,775,780,24),15,Muted);
        help.MouseFilter=MouseFilterEnum.Pass;help.TooltipText="같은 함급·프리셋의 편대원에게 함께 적용됩니다. 외형 교체는 주포만 반영합니다.";
        BuildStats(def);
        var selected=def.Bays.FirstOrDefault(b=>b.Id==_bay);
        if(_systems)BuildSystems(def);
        else if(selected is not null)
        {
            Label($"{BayName(selected.Id)} · {SizeName(selected.Size)}",new(1170,425,390,32),21,Blue);
            Label(string.Join(" / ",selected.Accepts.Select(Category)),new(1170,460,390,30),15,Muted);
            Button("베이 비우기",new(1170,487,385,36),()=>Install(null),!def.InstalledEquipment.Any(m=>m.BayId==selected.Id));
            int i=0;
            foreach(var device in ShipEquipment.All)
            {
                string? reason=Compatibility(def,selected,device);
                float y=535+i++*64;
                bool installed=def.InstalledEquipment.Any(m=>m.BayId==selected.Id&&m.Equipment.Id==device.Id);
                var button=Item(device.Name,MenuArt.Equipment(device.Category),new(1170,y,385,58),()=>Install(device.Id),
                    device.Name+"\n"+(reason??(installed?"장착 중":"클릭하여 장착"))+"\n"+Benefit(device)+
                    $"\n소비 {device.PowerMw:0.#} MW · 추가 폐열 {device.HeatMw:0.#} MW · 질량 {device.MassKg/1000:0.#} t\n"+Risk(device),installed,
                    status:reason is not null?"장착 불가":installed?"장착 중":"");
                button.Disabled=reason is not null;
                button.SetMeta("focus_key","equipment-"+device.Id);
            }
        }
        string? problem=Host.LaunchProblem();
        string match=problem is null?$"편대 전력 {Host.Hangar.Squadron().Members.Sum(ResearchTree.Catalog.Score):0.##} · 매칭 T{ResearchTree.Catalog.Bracket(Host.Hangar.Squadron()).Low}–{ResearchTree.Catalog.Bracket(Host.Hangar.Squadron()).High}":problem;
        _status=Label(match+"\n"+(Host.HangarMessage??"변경 사항은 자동 저장됩니다."),new(340,813,780,65),16,problem is null?Muted:Orange);
        Button("메인으로",new(40,821,128,50),Host.ReturnToPort);
        Button("전장 설정",new(180,821,130,50),()=>Host.ChangeScreen(BattleScreen.Select));
        Button("이 편대로 출격",new(1170,817,385,54),()=>Host.BeginMatch(),true,primary:true).Disabled=problem is not null;
        MenuStyles.RestoreFocus(this,focus);
        QueueRedraw();
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
    private void BuildSystems(ShipDefinition def)
    {
        var research=Host.Hangar.Research!;var fit=Host.Hangar.Fit(Current);
        string? installed=fit.Components?.Get(_system);
        Label(ShipComponents.Name(_system)+" 교체",new(1170,425,385,32),19,Blue);
        var stockProblem=SystemProblem(fit,null);
        var stock=Item("기본 "+ShipComponents.Name(_system),MenuArt.Component(_system,hull:def),new(1170,465,385,68),()=>InstallSystem(null),
            "연구 비용 없이 기본형으로 복귀",installed is null,status:stockProblem is not null?"장착 불가":installed is null?"장착 중":"");
        stock.SetMeta("focus_key","component-stock");
        stock.TooltipText=SystemProblem(fit,null)??"연구 비용 없이 기본형으로 복귀";
        stock.Disabled=SystemProblem(fit,null) is not null;
        if(!stock.Disabled){var original=FleetLoadouts.ResolveFit(fit with{Components=(fit.Components??new()).With(_system,null)}).Definition;
            stock.TooltipText+="\n"+ComponentComparison.Summary(_system,def,original)+"\n"+ComponentComparison.Costs(def,original);}
        int row=0;
        foreach(var part in ShipComponents.All.Where(p=>p.Slot==_system&&ShipComponents.Available(def,p)))
        {
            bool unlocked=research.HasComponent(def.Id,part.Id);int cost=research.ComponentCost(def.Id,part.Id);
            string? problem=SystemProblem(fit,part.Id);
            if(problem is null&&!unlocked&&research.Points[def.Design]<cost)problem=$"{cost-research.Points[def.Design]:N0} RP 부족 · 필요 {cost:N0}";
            float y=545+row++*84;
            string action=installed==part.Id?"장착 중":unlocked?"장착 가능":cost+" RP · 연구 및 장착";
            string state=problem is not null?(unlocked?"장착 불가":$"{cost:N0} RP · 연구 불가"):action;
            var button=Item(part.Name,MenuArt.Component(_system,part.Id),new(1170,y,385,76),()=>InstallSystem(part.Id,true),"",installed==part.Id,status:state);
            button.AddThemeFontSizeOverride("font_size",17);button.SetMeta("focus_key","component-"+part.Id);
            button.Disabled=problem is not null;
            string comparison=part.Description,costs="";
            try{var next=FleetLoadouts.ResolveFit(fit with{Components=(fit.Components??new()).With(_system,part.Id)}).Definition;
                comparison=ComponentComparison.Summary(_system,def,next);costs=ComponentComparison.Costs(def,next);}
            catch(InvalidDataException){/* A rejected fit has no valid resolved numbers to display. */}
            button.TooltipText=part.Name+"\n"+(problem??action)+"\n현재 → 교체 후\n"+comparison+"\n"+costs+"\n기본형 대비: "+part.Description;
            button.AccessibilityDescription=button.TooltipText;
        }
        Label($"보유 {research.Points[def.Design]:N0} RP · 연구 후 교체 무료",new(1170,782,385,22),15,Muted);
    }
    private string? SystemProblem(ShipFit fit,string? id)
    {
        try{FleetLoadouts.ResolveFit(fit with{Components=(fit.Components??new()).With(_system,id)});return null;}
        catch(InvalidDataException){return "전력 예산 또는 장착 조건 부족";}
    }
    private void BuildStats(ShipDefinition def)
    {
        var baseline=ShipDefinitions.ById(def.Id);var gear=def.InstalledEquipment.Select(m=>m.Equipment).ToArray();
        float budget=def.Power.Engines+def.Power.Shields+def.Power.Weapons+def.Power.Sensors;
        Meter("전력",budget+gear.Sum(d=>d.PowerMw),def.Power.OutputMw,budget,"MW",190);
        Meter("추가 폐열",gear.Sum(d=>d.HeatMw),def.Power.CoolingMw,0,"MW",241);
        Meter("가속",def.Flight.ForwardAccel,Mathf.Max(baseline.Flight.ForwardAccel*1.25f,def.Flight.ForwardAccel),baseline.Flight.ForwardAccel,"m/s²",292);
        Meter("실드",def.Shield.Capacity*(1+gear.Sum(d=>d.Effects.ShieldCapacityFraction)),baseline.Shield.Capacity*2,baseline.Shield.Capacity,"",343);
        Item("함선 상세",null,new(1170,386,385,32),null,HullDetails(def));
    }
    private static string HullDetails(ShipDefinition def)=>$"{def.Flight.DisplayName}\n{FleetLoadouts.RoleName(def.Branch)} · 전장 {def.Flight.Length:0} m\n"+
        $"미사일 {def.Missiles?.Rounds??0} · AM {def.Antimatter?.Rounds??0} · 프로브 {def.ReconProbes?.Count??0}\n"+
        $"센서 {def.Sensors.Strength*(1+def.InstalledEquipment.Sum(m=>m.Equipment.Effects.SensorStrengthFraction)):0} · 열용량 {def.Power.HeatCapacityMj:0} MJ\n드론 {def.DefenseDrones?.Count??0}";
    private void Meter(string name,float value,float maximum,float baseline,string unit,float y)
    {
        Label(name,new(1170,y,130,25),16);
        string valueText=name is "가속" or "실드"?$"{value:0.##} {unit} · 기본 {baseline:0.##}":$"{value:0.#} / {maximum:0.#} {unit}";
        var label=Label(valueText,new(1280,y,275,25),15,Muted);label.HorizontalAlignment=HorizontalAlignment.Right;
        AddChild(new ColorRect{Position=new(1170,y+28),Size=new(385,8),Color=new(.08f,.14f,.19f),MouseFilter=MouseFilterEnum.Ignore});
        AddChild(new ColorRect{Position=new(1170,y+28),Size=new(385*Mathf.Clamp(value/maximum,0,1),8),Color=name=="추가 폐열"?Orange:Blue,MouseFilter=MouseFilterEnum.Ignore});
        if(baseline>0)AddChild(new ColorRect{Position=new(1170+Mathf.Clamp(baseline/maximum,0,1)*385-1,y+25),Size=new(2,14),Color=Ink,MouseFilter=MouseFilterEnum.Ignore});
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
        DrawLine(new(322,190),new(322,790),new(.13f,.23f,.3f));DrawLine(new(1145,190),new(1145,790),new(.13f,.23f,.3f));
        DrawLine(new(40,804),new(1555,804),new(.13f,.23f,.3f));
    }
}
