using System;
using System.IO;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class ResearchView : Control
{
    public Main Host { get; set; }=null!;
    private Font _font=null!;
    private Label? _status;
    private static readonly Color Ink=new(.82f,.9f,1),Blue=new(.36f,.78f,1),Orange=new(1,.67f,.3f),Muted=MenuStyles.Muted;
    private ResearchCatalog Tree=>ResearchTree.Catalog;
    private ResearchProgress Progress=>Host.Hangar.Research!;
    private ResearchNode Selected=>Tree.ById(Host.ResearchNodeId!);
    private DesignFamily Design=>Tree.Design(Selected);
    public override void _Ready()
    {_font=new SystemFont{FontNames=new[]{"Malgun Gothic","Segoe UI"}};Size=new(1600,900);Build();}
    private Label Text(string value,Rect2 box,int size=18,Color? color=null)
    {
        var l=new Label{Text=value,Position=box.Position,Size=box.Size,MouseFilter=MouseFilterEnum.Ignore,AutowrapMode=TextServer.AutowrapMode.WordSmart};
        l.AddThemeFontOverride("font",_font);l.AddThemeFontSizeOverride("font_size",size);l.AddThemeColorOverride("font_color",color??Ink);AddChild(l);return l;
    }
    private Button Button(Node parent,string text,Rect2 box,Action action,Color color)
    {
        var b=new Button{Text=text,Position=box.Position,Size=box.Size,ClipText=true,TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis};
        b.AddThemeFontSizeOverride("font_size",16);MenuStyles.Apply(b,_font,color,accent:color==Blue||color==Orange);
        b.Pressed+=action;parent.AddChild(b);return b;
    }
    internal void SelectNode(string id){Host.ResearchNodeId=id;Build();}
    internal void Purchase(string? upgrade=null)
    {
        if(MenuConfirmation.Pending(this) is not null)return;
        try
        {
            var node=Selected;var design=Design;var progress=Progress;
            if(node.HullId is not {} hull)throw new InvalidDataException("아직 연구할 수 없는 함급입니다.");
            string title,body,confirm;long cost;
            if(upgrade is null)
            {
                if(progress.Unlocked.Contains(node.Id))return;
                if(node.ParentId is {} parent&&!progress.Unlocked.Contains(parent))throw new InvalidDataException("선행 함급을 먼저 연구하세요.");
                cost=node.Cost;title="함급 연구 확인";confirm="함급 연구";
                body=$"T{node.Tier} {node.Name}\n미보유 → 보유\n\n격납고에서 기함 또는 편대원으로 편성할 수 있습니다.\n"+
                    "함급만 해금하며 현재 편대 구성은 유지됩니다.\n\n"+
                    "다음 연구: "+string.Join(", ",Tree.Nodes.Where(n=>n.ParentId==node.Id).Select(n=>n.Name).DefaultIfEmpty("후속 함급 없음"));
            }
            else
            {
                if(!progress.Owned.Contains(hull))throw new InvalidDataException("함급을 먼저 연구하세요.");
                var item=node.Upgrades.Single(u=>u.Id==upgrade);if(progress.ForHull(hull).Contains(upgrade))return;
                cost=item.Cost;title="영구 개량 확인";confirm="개량 연구";
                body=node.Name+" · "+item.Name+"\n이 함급의 모든 프리셋과 편대원에 적용됩니다.\n";
                for(int preset=0;preset<3;preset++)
                {
                    var fit=Host.Hangar.Fit(new(hull,preset));var before=FleetLoadouts.ResolveFit(fit).Definition;
                    var after=FleetLoadouts.ResolveFit(fit with{Upgrades=(fit.Upgrades??Array.Empty<string>()).Append(upgrade).ToArray()}).Definition;
                    body+=$"\n프리셋 {preset+1} · {ComponentComparison.Upgrade(upgrade,before,after)}\n";
                }
            }
            if(progress.Points[design]<cost)throw new InvalidDataException("연구 점수가 부족합니다.");
            MenuConfirmation.Show(this,_font,title,body,MenuConfirmation.Price(design,progress.Points[design],cost),
                "확인하면 연구 점수가 차감됩니다. 완료한 연구는 취소하거나 환불할 수 없습니다.",confirm,()=>
                {
                    try{Host.BuyResearch(node.Id,upgrade);}
                    catch(InvalidDataException e){ShowError(e.Message);}
                });
        }
        catch(InvalidDataException e){if(_status is not null){_status.Text=e.Message;_status.AddThemeColorOverride("font_color",Orange);}}
    }
    private void ShowError(string message)
    {if(_status is not null){_status.Text=message;_status.AddThemeColorOverride("font_color",Orange);}}
    private void Build()
    {
        string? focus=MenuStyles.FocusKey(this);
        foreach(Node child in GetChildren()){RemoveChild(child);child.QueueFree();}
        var selected=Selected;var design=Design;
        Text("기술 연구",new(40,25,300,60),34);
        Text("함급 해금 · 함급별 영구 개량",new(40,87,600,32),18,Muted);
        Button(this,"지구권",new(420,36,180,44),()=>SelectNode(Tree.Root(DesignFamily.Earth).Id),design==DesignFamily.Earth?Blue:Muted);
        Button(this,"화성권",new(620,36,180,44),()=>SelectNode(Tree.Root(DesignFamily.Mars).Id),design==DesignFamily.Mars?Orange:Muted);
        Text($"{(design==DesignFamily.Earth?"지구권":"화성권")} 연구 점수  {Progress.Points[design]:N0}",new(1080,37,470,45),25,design==DesignFamily.Earth?Blue:Orange);
        var reward=Tree.Settings;
        Text($"전투 완료 +{reward.CompletionPoints} · 승리 +{reward.VictoryPoints} · 편대 기여 최대 +{reward.ContributionLimit}",new(1080,88,470,35),15,Muted);
        var scroll=new ScrollContainer{Position=new(40,140),Size=new(1000,630),VerticalScrollMode=ScrollContainer.ScrollMode.Disabled};AddChild(scroll);
        var canvas=new Control{CustomMinimumSize=new(Tree.MaxTier*195+12,605),MouseFilter=MouseFilterEnum.Pass};scroll.AddChild(canvas);
        var nodes=Tree.Trees.Single(t=>t.Design==design).Nodes;
        Rect2 Bounds(ResearchNode n)=>new(8+(n.Tier-1)*195,40+n.Lane*112,180,104);
        canvas.Draw+=()=>
        {
            foreach(var n in nodes.Where(n=>n.ParentId is not null))
            {
                var a=Bounds(Tree.ById(n.ParentId!));var b=Bounds(n);
                canvas.DrawLine(new(a.End.X,a.GetCenter().Y),new(b.Position.X,b.GetCenter().Y),new(.18f,.3f,.4f),2,true);
            }
        };
        for(int tier=1;tier<=Tree.MaxTier;tier++)
        {
            var label=new Label{Text="T"+tier,Position=new(8+(tier-1)*195,0),Size=new(180,30),HorizontalAlignment=HorizontalAlignment.Center};
            label.AddThemeFontOverride("font",_font);label.AddThemeFontSizeOverride("font_size",20);label.AddThemeColorOverride("font_color",Muted);canvas.AddChild(label);
        }
        foreach(var n in nodes)
        {
            bool owned=Progress.Unlocked.Contains(n.Id);bool ready=n.HullId is not null&&(n.ParentId is null||Progress.Unlocked.Contains(n.ParentId));
            string status=owned?"보유":n.HullId is null?"준비 중":ready?$"연구 {n.Cost} RP":"선행 연구 필요";
            var bounds=Bounds(n);
            var b=new MenuItemButton{Caption=n.Name,Detail=owned?"보유":$"{n.Cost:N0} RP",Typeface=_font,Portrait=true,
                Picture=n.HullId is {} id?MenuArt.Ship(ShipDefinitions.ById(id)):null,
                Position=bounds.Position,Size=bounds.Size,
                TooltipText=$"T{n.Tier} · {n.Name}\n{status}\n연구 비용 {n.Cost:N0} RP"+
                    (n.ParentId is {} parent?$"\n선행 함급: {Tree.ById(parent).Name}":"\n기본 제공 함급")};
            MenuStyles.Apply(b,_font,n.Id==selected.Id?Orange:owned?Blue:Muted,accent:n.Id==selected.Id||owned);
            b.Pressed+=()=>SelectNode(n.Id);canvas.AddChild(b);
            b.SetMeta("focus_key","research-"+n.Id);
        }
        scroll.SetDeferred("scroll_horizontal",Math.Max(0,(selected.Tier-4)*195));
        Text($"T{selected.Tier}  {selected.Name}",new(1080,152,470,46),24,Blue);
        string role=FleetLoadouts.RoleName(selected.Branch);
        if(selected.HullId is { } hullId){var hull=ShipDefinitions.ById(hullId);role+=$" · 미사일 {hull.Missiles?.Rounds??0} · AM {hull.Antimatter?.Rounds??0} · 프로브 {hull.ReconProbes?.Count??0}";}
        Text(role,new(1080,209,470,32),16,Muted);
        bool unlocked=Progress.Unlocked.Contains(selected.Id);
        bool parentReady=selected.ParentId is null||Progress.Unlocked.Contains(selected.ParentId);
        Text(selected.ParentId is null?"양 진영의 기본 호위함은 무료로 제공됩니다.":$"선행 함급 · {Tree.ById(selected.ParentId).Name}\n"+(parentReady?"선행 연구 완료":"이 함급을 먼저 연구하세요."),new(1080,247,470,52),16,parentReady?Muted:Orange);
        if(selected.HullId is null)
        {
            Text("후속 단계에서 추가될 함급입니다.\n연구 점수는 소비되지 않습니다.",new(1080,313,470,80),20,Muted);
        }
        else
        {
            var action=Button(this,unlocked?"격납고에서 편성":$"함급 연구 · {selected.Cost} RP",new(1080,308,470,48),
                ()=>{if(unlocked){Host.ChooseHull(selected.HullId);Host.OpenHangar();}else Purchase();},Blue);
            action.SetMeta("focus_key","research-action");
            action.Disabled=!unlocked&&(Progress.Points[design]<selected.Cost||!parentReady);
            string condition=unlocked?"보유 함급 · 격납고에서 기함으로 선택합니다.":$"필요 {selected.Cost:N0} RP · 보유 {Progress.Points[design]:N0} RP"+
                (Progress.Points[design]<selected.Cost?$" · {selected.Cost-Progress.Points[design]:N0} RP 부족":" · 점수 충족");
            Text(condition,new(1080,362,470,27),16,action.Disabled?Orange:Muted);
            Text("영구 개량 · 이 함급의 모든 프리셋에 적용",new(1080,399,470,32),18,Blue);
            for(int i=0;i<selected.Upgrades.Length;i++)
            {
                var u=selected.Upgrades[i];bool done=Progress.ForHull(selected.HullId).Contains(u.Id);
                string state=done?"연구 완료":!unlocked?"함급 해금 필요":Progress.Points[design]<u.Cost?$"{u.Cost-Progress.Points[design]:N0} RP 부족":$"연구 · {u.Cost} RP";
                var b=new MenuItemButton{Caption=u.Name,Detail=state,Typeface=_font,Position=new(1080,443+i*62),Size=new(470,54),
                    Picture=MenuArt.Icon(u.Id switch{"hull"=>"hull","fire-control"=>"point-defense","reactor"=>"generator","drive"=>"engine",_=>"sensor"}),
                    TooltipText=u.Name+$"\n{state}\n필요 {u.Cost} RP · 보유 {Progress.Points[design]:N0} RP\n"+u.Description};
                MenuStyles.Apply(b,_font,done?Blue:Ink,accent:done);b.Pressed+=()=>Purchase(u.Id);AddChild(b);
                b.SetMeta("focus_key","upgrade-"+u.Id);
                b.Disabled=done||!unlocked||Progress.Points[design]<u.Cost;
            }
        }
        Text("보유는 수량 제한이 없습니다. 해금한 함급을 여러 편대 슬롯에 사용할 수 있습니다.",new(40,784,990,30),16,Muted);
        Button(this,"격납고로",new(40,833,280,44),Host.OpenHangar,Blue);
        _status=Text(Host.HangarMessage??"진영별 점수와 연구는 자동 저장됩니다.",new(350,835,1120,40),16,Muted);
        MenuStyles.RestoreFocus(this,focus);
        QueueRedraw();
    }
    public override void _Process(double delta)
    {var size=GetViewportRect().Size;float scale=Mathf.Min(size.X/1600,size.Y/900);Scale=Vector2.One*scale;Position=(size-new Vector2(1600,900)*scale)*.5f;}
    public override void _Draw()
    {DrawRect(new(0,0,1600,900),new(.012f,.025f,.043f));DrawLine(new(1055,140),new(1055,790),new(.15f,.25f,.33f));}
}
