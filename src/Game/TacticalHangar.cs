using System;
using System.IO;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class HangarView
{
    private string? _tacticalBay;
    internal void SelectTacticalBay(string id){_tacticalBay=id;_popover=Popover.Tactical;_cutaway=true;Rebuild();}
    private TacticalMount[] TacticalFit=>SelectedParts?.TacticalModules??Array.Empty<TacticalMount>();
    internal static string TacticalIcon(TacticalEffect effect)=>effect switch
    {TacticalEffect.GeneratorOverload=>"generator",TacticalEffect.HeatVent=>"cooler",TacticalEffect.FocusSearch=>"sensor",
        TacticalEffect.AirDefense=>"point-defense",TacticalEffect.QuickLoader=>"missile",TacticalEffect.ShortJump=>"engine",_=>"generator"};
    private void TacticalButton(ShipDefinition def)
    {
        var bays=TacticalModulesCatalog.Bays(def);
        var button=Button($"전술 모듈 {TacticalFit.Length}/{bays.Length}",new(1010,528,168,44),()=>
        {if(bays.Length>0)SelectTacticalBay(_tacticalBay??bays[0].Id);},_popover==Popover.Tactical,size:16);
        button.Disabled=bays.Length==0;button.SetMeta("focus_key","tactical-modules");
        button.TooltipText="운용 패시브와 활성 능력 · 활성 모듈만 4개 단축키에 할당";
    }
    private void BuildTacticalPicker(ShipDefinition def)
    {
        var bays=TacticalModulesCatalog.Bays(def);if(bays.Length==0)return;
        if(!bays.Any(b=>b.Id==_tacticalBay))_tacticalBay=bays[0].Id;
        var catcher=new HangarPickerBackdrop{Size=new(1600,900),Passage=new(_preview.Position,_preview.Size)};
        catcher.GuiInput+=e=>{if(e is InputEventMouseButton{Pressed:true})CloseOverlay();};AddChild(catcher);
        var panel=new Panel{Position=new(710,140),Size=new(470,448)};
        panel.AddThemeStyleboxOverride("panel",new StyleBoxFlat{BgColor=Panel});AddChild(panel);
        Label("전술 모듈",new(16,8,300,32),21,Blue,panel);
        Button("닫기",new(388,8,66,30),()=>CloseOverlay(),size:14,parent:panel);
        for(int i=0;i<bays.Length;i++)
        {
            var bay=bays[i];var fitted=TacticalFit.FirstOrDefault(m=>m.BayId==bay.Id);
            var tab=Button($"{i+1}"+(fitted is null?" · 빈칸":" · 장착"),new(12+i*74,46,70,32),()=>SelectTacticalBay(bay.Id),_tacticalBay==bay.Id,size:13,parent:panel);
            tab.TooltipText=$"베이 {i+1} · {(bay.Size==BaySize.Medium?"중형":"소형")} · {(bay.Grid==PowerGrid.Port?"좌현":"우현")} 전력망";
        }
        int active=Array.FindIndex(TacticalFit.Where(m=>TacticalModulesCatalog.ById(m.ModuleId).Active).ToArray(),m=>m.BayId==_tacticalBay);
        Label(active<0?"패시브는 단축키를 사용하지 않습니다.":$"활성 키 {InputBindings.Text(InputSetup.TacticalSlots[active])}",new(14,86,290,26),15,Muted,panel);
        if(active>0)Button("앞 순서로",new(334,84,120,30),()=>ReorderTactical(-1),size:14,parent:panel);
        else if(active==0&&TacticalFit.Count(m=>TacticalModulesCatalog.ById(m.ModuleId).Active)>1)
            Button("뒤 순서로",new(334,84,120,30),()=>ReorderTactical(1),size:14,parent:panel);
        var scroll=new ScrollContainer{Position=new(10,124),Size=new(450,314),HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,FollowFocus=true};panel.AddChild(scroll);
        var content=new Control{CustomMinimumSize=new(428,9*68)};scroll.AddChild(content);
        Item("해제",MenuArt.Icon("empty-bay"),new(0,0,428,62),()=>InstallTactical(null),"이 베이에 설치한 전술 모듈만 내립니다.",parent:content);
        int row=1;
        foreach(var device in TacticalModulesCatalog.All)
        {
            var candidate=TacticalFit.Where(m=>m.BayId!=_tacticalBay).Append(new TacticalMount(_tacticalBay!,device.Id)).ToArray();
            string? error=null;
            try{FleetLoadouts.ResolveFit(Host.Hangar.Fit(Current) with{Components=(SelectedParts??new()) with{TacticalModules=candidate}});}
            catch(InvalidDataException e){error=e.Message;}
            bool installed=TacticalFit.Any(m=>m.BayId==_tacticalBay&&m.ModuleId==device.Id),owned=Host.Hangar.HasTactical(def.Design,device.Id);
            if(!owned&&Host.Hangar.Research!.Points[def.Design]<device.ResearchCost)error??=$"RP {device.ResearchCost-Host.Hangar.Research.Points[def.Design]} 부족";
            string type=device.Active?$"활성 · {device.Duration:0}초 / 재사용 {device.Cooldown:0}초":"패시브";
            string details=$"{device.Name} · {type}\n{device.Description}\n소비 {def.Power.OutputMw*device.PowerFraction:0.#} MW · 추가 열 {def.Power.OutputMw*device.HeatFraction:0.#} MW · 질량 +{device.MassFraction:P1}\n{(device.Size==BaySize.Medium?"중형":"소형")} · T{device.MinTier} 이상 · 같은 모듈 중복 불가\n"+(error??(owned?"연구 완료":$"연구 {device.ResearchCost} RP"));
            var b=Item(device.Name,MenuArt.Icon(TacticalIcon(device.Effect)),new(0,row++*68,428,62),()=>InstallTactical(device.Id),details,installed,
                status:error is not null?"장착 불가":installed?"장착 중":owned?type:$"{type} · {device.ResearchCost} RP",parent:content);
            b.Disabled=error is not null;b.SetMeta("focus_key","tactical-"+device.Id);
        }
    }
    internal void InstallTactical(string? id)
    {
        if(_tacticalBay is null||MenuConfirmation.Pending(this) is not null)return;
        var target=Current;int index=SlotIndex(target);var old=TacticalFit.FirstOrDefault(m=>m.BayId==_tacticalBay);
        if(old?.ModuleId==id)return;
        var mounts=TacticalFit.Where(m=>m.BayId!=_tacticalBay).ToList();if(id is not null)mounts.Add(new(_tacticalBay,id));
        var design=CurrentFit.Definition.Design;long cost=id is not null&&!Host.Hangar.HasTactical(design,id)?TacticalModulesCatalog.ById(id).ResearchCost:0;
        string Name(string? value)=>value is null?"비어 있음":TacticalModulesCatalog.ById(value).Name;
        var before=CurrentFit.Definition;
        var after=FleetLoadouts.ResolveFit(Host.Hangar.Fit(target) with{Components=(SelectedParts??new()) with{TacticalModules=mounts.ToArray()}}).Definition;
        string Costs(ShipDefinition d)=>$"질량 {d.Flight.MassKg/1000:0.#} t · 전술 전력 {d.TacticalModules?.Sum(m=>m.PowerMw)??0:0.#} MW · 폐열 {d.TacticalModules?.Sum(m=>m.HeatMw)??0:0.#} MW";
        string detail=FitScope(target)+$"\n{Name(old?.ModuleId)} → {Name(id)}\n{Costs(before)}\n→ {Costs(after)}\n\n"+(id is null?"활성 단축키 순서가 앞당겨집니다.":TacticalModulesCatalog.ById(id).Description);
        MenuConfirmation.Show(this,_font,cost>0?"전술 모듈 연구 및 장착":"전술 모듈 변경",detail,
            MenuConfirmation.Price(design,Host.Hangar.Research!.Points[design],cost),(target.UsesOwn?"이 편대원에게만 적용됩니다.":"같은 프리셋을 사용하는 슬롯에 함께 적용됩니다.")+" 연구는 환불되지 않습니다.",cost>0?"연구하고 장착":"변경 적용",
            ()=>Action(()=>Host.Hangar.SetTactical(target,index,mounts.ToArray(),research:true)));
    }
    private void ReorderTactical(int direction)
    {
        var mounts=TacticalFit.ToArray();int index=Array.FindIndex(mounts,m=>m.BayId==_tacticalBay);
        int other=index+direction;while(other>=0&&other<mounts.Length&&!TacticalModulesCatalog.ById(mounts[other].ModuleId).Active)other+=direction;
        if(other<0||other>=mounts.Length)return;
        (mounts[index],mounts[other])=(mounts[other],mounts[index]);
        Action(()=>Host.Hangar.SetTactical(Current,SlotIndex(Current),mounts));
    }
}
