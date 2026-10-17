using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

/// <summary>Local campaign ledger. Grants are recorded with the balance in the same atomic save.</summary>
public sealed class ResearchProgress
{
    public Dictionary<DesignFamily,long> Points { get; set; }=new(){{DesignFamily.Earth,0},{DesignFamily.Mars,0}};
    public string[] Unlocked { get; set; }=Array.Empty<string>();
    public Dictionary<string,string[]> Upgrades { get; set; }=new(StringComparer.Ordinal);
    public Dictionary<string,string[]> Components { get; set; }=new(StringComparer.Ordinal);
    public string[] Receipts { get; set; }=Array.Empty<string>();
    public static ResearchProgress New()=>new(){Unlocked=ResearchTree.Catalog.Trees.Select(t=>t.Nodes.Single(n=>n.ParentId is null).Id).ToArray()};
    public HashSet<string> Owned=>Unlocked.Select(id=>ResearchTree.Catalog.ById(id).HullId!).ToHashSet(StringComparer.Ordinal);
    public string[] ForHull(string id)=>Upgrades.TryGetValue(id,out var upgrades)?upgrades:Array.Empty<string>();
    public bool HasComponent(string hull,string id)=>Components.TryGetValue(hull,out var ids)&&ids.Contains(id);
    public int ComponentCost(string hull,string id)=>checked(ShipComponents.ById(id).Cost*ResearchTree.Catalog.ByHull(hull).Tier);
    public IReadOnlyDictionary<string,IReadOnlySet<string>> UpgradeSets()=>Upgrades.Keys.Union(Components.Keys).ToDictionary(id=>id,
        id=>(IReadOnlySet<string>)ForHull(id).Concat(Components.TryGetValue(id,out var parts)?parts.Select(ShipComponents.UnlockKey):Array.Empty<string>()).ToHashSet());
    public bool BuyComponent(string hull,string id)
    {
        var part=ShipComponents.ById(id);var def=ShipDefinitions.ById(hull);
        if(!Owned.Contains(hull)||!ShipComponents.Available(def,part))throw new InvalidDataException("이 함급에서 연구할 수 없는 부품입니다.");
        if(HasComponent(hull,id))return false;
        Spend(def.Design,ComponentCost(hull,id));
        Components[hull]=(Components.TryGetValue(hull,out var ids)?ids:Array.Empty<string>()).Append(id).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        return true;
    }
    public void GrantPath(string hull)
    {
        var node=ResearchTree.Catalog.ByHull(hull);var set=Unlocked.ToHashSet();
        while(true){set.Add(node.Id);if(node.ParentId is null)break;node=ResearchTree.Catalog.ById(node.ParentId);}
        Unlocked=set.OrderBy(id=>id,StringComparer.Ordinal).ToArray();
    }
    public bool BuyHull(string id)
    {
        var tree=ResearchTree.Catalog;var node=tree.ById(id);
        if(Unlocked.Contains(id))return false;
        if(node.HullId is null)throw new InvalidDataException("아직 구현 준비 중인 함급입니다.");
        if(node.ParentId is { } parent&&!Unlocked.Contains(parent))throw new InvalidDataException("앞 함급을 먼저 연구해야 합니다.");
        var design=tree.Design(node);Spend(design,node.Cost);Unlocked=Unlocked.Append(id).ToArray();return true;
    }
    public bool BuyUpgrade(string hull,string id)
    {
        if(!Owned.Contains(hull))throw new InvalidDataException("함급을 먼저 연구해야 합니다.");
        var tree=ResearchTree.Catalog;var node=tree.ByHull(hull);
        var upgrade=node.Upgrades.SingleOrDefault(u=>u.Id==id)??throw new InvalidDataException("없는 부품 연구입니다.");
        if(ForHull(hull).Contains(id))return false;
        Spend(tree.Design(node),upgrade.Cost);Upgrades[hull]=ForHull(hull).Append(id).ToArray();return true;
    }
    private void Spend(DesignFamily design,int cost)
    {
        if(Points[design]<cost)throw new InvalidDataException("연구 점수가 부족합니다.");
        Points[design]-=cost;
    }
    public int Reward(string receipt,DesignFamily design,double seconds,bool won,double damage,int interceptions)
    {
        if(string.IsNullOrWhiteSpace(receipt)||!Enum.IsDefined(design)||!double.IsFinite(seconds)||seconds<0
            ||!double.IsFinite(damage)||damage<0||interceptions<0)throw new InvalidDataException("잘못된 전투 정산입니다.");
        var r=ResearchTree.Catalog.Settings;
        if(Receipts.Contains(receipt)||seconds<r.MinimumRewardSeconds)return 0;
        int contribution=(int)Math.Min(r.ContributionLimit,damage/r.DamagePerPoint+(double)interceptions*r.InterceptionPoints);
        int points=r.CompletionPoints+(won?r.VictoryPoints:0)+contribution;
        Points[design]=checked(Points[design]+points);
        Receipts=Receipts.Append(receipt).TakeLast(256).ToArray();return points;
    }
    public void Validate()
    {
        var tree=ResearchTree.Catalog;
        if(Points is null||Points.Count!=2||Enum.GetValues<DesignFamily>().Any(d=>!Points.TryGetValue(d,out var p)||p<0||p>1_000_000_000)
            ||Unlocked is null||Unlocked.Length>1024||Unlocked.Distinct().Count()!=Unlocked.Length||Upgrades is null||Upgrades.Count>256
            ||Components is null||Components.Count>128
            ||Receipts is null||Receipts.Length>256||Receipts.Any(string.IsNullOrWhiteSpace)||Receipts.Any(s=>s.Length>128)||Receipts.Distinct().Count()!=Receipts.Length)
            throw new InvalidDataException("손상된 연구 진행입니다.");
        foreach(var design in Enum.GetValues<DesignFamily>())if(!Unlocked.Contains(tree.Root(design).Id))throw new InvalidDataException("기본 호위함 해금이 없습니다.");
        foreach(string id in Unlocked)
        {var n=tree.ById(id);if(n.HullId is null||n.ParentId is { } parent&&!Unlocked.Contains(parent))throw new InvalidDataException("잘못된 함급 연구 경로입니다.");}
        var owned=Owned;
        foreach(var (id,upgrades) in Upgrades)
        {
            var node=tree.ByHull(id);
            if(!owned.Contains(id)||upgrades is null||upgrades.Distinct().Count()!=upgrades.Length||upgrades.Any(u=>!node.Upgrades.Any(n=>n.Id==u)))
                throw new InvalidDataException("잘못된 부품 연구 목록입니다.");
        }
        foreach(var (id,parts) in Components)
        {
            var hull=ShipDefinitions.ById(id);
            if(!owned.Contains(id)||parts is null||parts.Length>32||parts.Distinct().Count()!=parts.Length
                ||parts.Any(p=>!ShipComponents.Available(hull,ShipComponents.ById(p))))
                throw new InvalidDataException("잘못된 교체 부품 연구 목록입니다.");
        }
    }
}
