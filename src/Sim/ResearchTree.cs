using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json.Nodes;

namespace SpaceFleet.Sim;

public sealed record ResearchUpgrade(string Id,string Name,string Description,int Cost,double MatchBonus,JsonObject Patch);
public sealed record ResearchNode(string Id,string Name,int Tier,ShipBranch Branch,string? ParentId,int Lane,
    string? HullId,int Cost,ResearchUpgrade[] Upgrades);
public sealed record DesignTree(DesignFamily Design,ResearchNode[] Nodes);
public sealed record ResearchSettings(double TierMultiplier,int CompletionPoints,int VictoryPoints,int ContributionLimit,
    double DamagePerPoint,int InterceptionPoints,double MinimumRewardSeconds);

/// <summary>Data-only tree and upgrade contracts; no player persistence or rendering.</summary>
public sealed class ResearchCatalog
{
    public IReadOnlyList<DesignTree> Trees { get; }
    public ResearchSettings Settings { get; }
    public IEnumerable<ResearchNode> Nodes=>Trees.SelectMany(t=>t.Nodes);
    public IEnumerable<ResearchNode> Playable=>Nodes.Where(n=>n.HullId is not null);
    public int MaxTier=>Nodes.Max(n=>n.Tier);
    public ResearchNode ById(string id)=>Nodes.FirstOrDefault(n=>n.Id==id)??throw new InvalidDataException("알 수 없는 연구 항목입니다.");
    public ResearchNode ByHull(string id)=>Playable.FirstOrDefault(n=>n.HullId==id)??throw new InvalidDataException("연구 트리에 없는 함급입니다.");
    public DesignFamily Design(ResearchNode node)=>Trees.Single(t=>t.Nodes.Contains(node)).Design;
    public ResearchNode Root(DesignFamily design)=>Trees.Single(t=>t.Design==design).Nodes.Single(n=>n.ParentId is null);
    public ResearchCatalog(IEnumerable<DesignTree> trees,ResearchSettings settings)
    {
        Trees=Array.AsReadOnly(trees.ToArray());Settings=settings;Validate();
    }
    public static ResearchCatalog Parse(IEnumerable<string> trees,string settings)=>new(
        trees.Select(ShipDefinition.ParseData<DesignTree>),ShipDefinition.ParseData<ResearchSettings>(settings));
    private void Validate()
    {
        void Require([DoesNotReturnIf(false)] bool ok,string why){if(!ok)throw new InvalidDataException("Research: "+why);}
        Require(Trees.All(t=>t is not null&&t.Nodes is not null&&t.Nodes.All(n=>n is not null)),"null tree/node");
        Require(Trees.Count==Enum.GetValues<DesignFamily>().Length&&Trees.Select(t=>t.Design).Distinct().Count()==Trees.Count,"one tree per design");
        var ids=new HashSet<string>(StringComparer.Ordinal);var hulls=new HashSet<string>(StringComparer.Ordinal);
        foreach(var tree in Trees)
        {
            Require(Enum.IsDefined(tree.Design)&&tree.Nodes is {Length:>0},"empty/invalid tree");
            foreach(var n in tree.Nodes)
            {
                Require(n is not null&&!string.IsNullOrWhiteSpace(n.Id)&&ids.Add(n.Id)&&!string.IsNullOrWhiteSpace(n.Name)
                    &&n.Tier>=1&&Enum.IsDefined(n.Branch)&&n.Lane>=0&&n.Cost>=0&&n.Upgrades is not null,"invalid/duplicate node");
                Require(n!.ParentId is null?n.Tier==1&&n.Cost==0:n.Cost>0,"root/cost mismatch");
                if(n.HullId is not null)
                {
                    var hull=ShipDefinitions.ById(n.HullId);
                    Require(hulls.Add(n.HullId)&&hull.Design==tree.Design&&hull.Branch==n.Branch&&hull.Flight.DisplayName==n.Name,"hull identity/design/name mismatch");
                    var upgrades=new HashSet<string>();
                    foreach(var u in n.Upgrades)
                    {
                        Require(u is not null&&!string.IsNullOrWhiteSpace(u.Id)&&!string.IsNullOrWhiteSpace(u.Name)&&!string.IsNullOrWhiteSpace(u.Description)&&upgrades.Add(u.Id)&&u.Cost>0
                            &&double.IsFinite(u.MatchBonus)&&u.MatchBonus>=0&&u.MatchBonus<=.15&&u.Patch is not null,"invalid upgrade");
                        Require(u!.Patch.Count>0&&u.Patch.All(p=>p.Key is "structureHitPoints" or "railgun" or "power" or "flight" or "damageControl"),"upgrade schema");
                    }
                    var resolved=Resolve(n.HullId,n.Upgrades.Select(u=>u.Id).ToArray(),Array.Empty<EquipmentMount>());
                    Require(resolved.Definition.Id==hull.Id&&resolved.Definition.ModelId==hull.ModelId,"upgrades cannot change identity/model");
                }
                else Require(n.Upgrades.Length==0,"unimplemented hull has upgrades");
            }
            Require(tree.Nodes.Count(n=>n.ParentId is null)==1,"exactly one root");
            foreach(var n in tree.Nodes.Where(n=>n.ParentId is not null))
            {var parent=tree.Nodes.FirstOrDefault(p=>p.Id==n.ParentId);Require(parent is not null&&parent.Tier<n.Tier,"missing parent/cycle/tier inversion");}
        }
        var r=Settings;Require(r is not null,"missing scoring rules");
        Require(double.IsFinite(r.TierMultiplier)&&r.TierMultiplier>1&&r.TierMultiplier<=2&&r.CompletionPoints>0&&r.VictoryPoints>=0
            &&r.ContributionLimit>=0&&double.IsFinite(r.DamagePerPoint)&&r.DamagePerPoint>0&&r.InterceptionPoints>=0
            &&double.IsFinite(r.MinimumRewardSeconds)&&r.MinimumRewardSeconds>0,"invalid scoring rules");
    }
    public ResolvedLoadout Resolve(string hull,IReadOnlyList<string>? upgrades,IReadOnlyList<EquipmentMount> equipment)
        =>Resolve(hull,upgrades,equipment,null);
    public ResolvedLoadout Resolve(string hull,IReadOnlyList<string>? upgrades,IReadOnlyList<EquipmentMount> equipment,ShipComponentSelection? components)
    {
        var node=ByHull(hull);var selected=upgrades??Array.Empty<string>();
        if(selected.Distinct().Count()!=selected.Count||selected.Any(id=>!node.Upgrades.Any(u=>u.Id==id)))throw new InvalidDataException("잘못된 부품 연구 세팅입니다.");
        var patches=node.Upgrades.Where(u=>selected.Contains(u.Id)).Select(u=>u.Patch.ToJsonString()).ToArray();
        if(ShipVariants.All.FirstOrDefault(v=>v.Node.Id==hull) is { } variant)
            return ShipLoadouts.Resolve(new(variant.Node.BaseHullId,variant.Node.Patch.ToJsonString(),patches,equipment){Components=components});
        return ShipLoadouts.Resolve(new(hull,UpgradePatches:patches,Equipment:equipment){Components=components});
    }
    public double Score(ShipFit fit)
    {
        var node=ByHull(fit.HullId);double bonus=node.Upgrades.Where(u=>fit.Upgrades?.Contains(u.Id)==true).Sum(u=>u.MatchBonus);
        return ShipDefinitions.ById(fit.HullId).BattleValue.Weight*Math.Pow(Settings.TierMultiplier,node.Tier-1)*(1+bonus);
    }
    public (int Center,int Low,int High) Bracket(SquadronLoadout squad)
    {
        if(squad?.Members is not {Count:>0})throw new InvalidDataException("빈 편대입니다.");
        double weight=squad.Members.Sum(m=>ShipDefinitions.ById(m.HullId).BattleValue.Weight);
        int center=(int)Math.Round(squad.Members.Sum(m=>ByHull(m.HullId).Tier*ShipDefinitions.ById(m.HullId).BattleValue.Weight)/weight,MidpointRounding.AwayFromZero);
        int low=center==1?1:Math.Max(1,center-1),high=center==1?1:Math.Min(Playable.Max(n=>n.Tier),center+1);
        if(squad.Members.Any(m=>ByHull(m.HullId).Tier<low||ByHull(m.HullId).Tier>high))throw new InvalidDataException("편대원의 티어 차이가 큽니다. 출격 범위 ±1 안으로 맞춰 주세요.");
        return(center,low,high);
    }
}

public static class ResearchTree
{
    public static ResearchCatalog Catalog { get; }=Load();
    private static ResearchCatalog Load()
    {
        string Read(string name){using var stream=typeof(ResearchTree).Assembly.GetManifestResourceStream("SpaceFleet.data.tree."+name+".json")!;using var reader=new StreamReader(stream);return reader.ReadToEnd();}
        return ResearchCatalog.Parse(new[]{Read("earth"),Read("mars")},Read("rules"));
    }
}
