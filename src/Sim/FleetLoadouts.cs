using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SpaceFleet.Sim;

public sealed record ShipFit(string HullId, IReadOnlyList<EquipmentMount> Equipment,IReadOnlyList<string>? Upgrades=null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ShipComponentSelection? Components { get; init; }
}
public sealed record SquadronLoadout(IReadOnlyList<ShipFit> Members);
public sealed record ResolvedSquadron(string LoadoutId, IReadOnlyList<ResolvedLoadout> Members)
{
    public ShipDefinition Leader => Members[0].Definition;
    public string Composition => string.Join(" · ", Members.GroupBy(m=>FleetLoadouts.RoleName(m.Definition.Branch))
        .Select(g=>$"{g.Key} {g.Count()}"));
}

/// <summary>Playable hull IDs and squad-slot validation; no ownership persistence or GUI dependencies.</summary>
public static class FleetLoadouts
{
    public static IEnumerable<ShipDefinition> Hulls => ShipDefinitions.All.Concat(ShipVariants.All.Select(v=>v.Definition));
    public static ShipFit Empty(string id) => new(id,Array.Empty<EquipmentMount>());
    public static ResolvedLoadout ResolveFit(ShipFit fit)
    {
        if(fit is null||fit.Equipment is null)throw new InvalidDataException("잘못된 함선 세팅입니다.");
        if(fit.Upgrades is {Count:>0}||fit.Components is {IsEmpty:false})
            return ResearchTree.Catalog.Resolve(fit.HullId,fit.Upgrades,fit.Equipment,fit.Components);
        return ShipVariants.All.Any(v=>v.Node.Id==fit.HullId)
            ? ShipVariants.Resolve(fit.HullId,equipment:fit.Equipment)
            : ShipLoadouts.Resolve(new(fit.HullId,Equipment:fit.Equipment));
    }
    public static SquadronLoadout Default(string leader) => new(ShipDefinitions.ById(leader).Squadron.Members
        .Select(s=>Empty(s.HullId??leader)).ToArray());
    public static bool Allows(ShipDefinition leader,int slot,ShipDefinition member)
    {
        if(slot<0||slot>=leader.Squadron.Members.Count||member.Design!=leader.Design)return false;
        if(slot==0)return member.Id==leader.Id;
        var kind=leader.Squadron.Members[slot].AllowedKind??leader.Kind;
        return member.Kind==kind && (kind!=HullKind.Escort||member.Branch is ShipBranch.Escort
            or ShipBranch.GunCruiser or ShipBranch.AirDefenseCruiser or ShipBranch.MissileCruiser);
    }
    public static ResolvedSquadron Resolve(SquadronLoadout loadout,IReadOnlySet<string>? owned=null)
    {
        if(loadout?.Members is not {Count:>0 and <=16})throw new InvalidDataException("편대원 수가 올바르지 않습니다.");
        var fits=loadout.Members.Select(ResolveFit).ToArray();var leader=fits[0].Definition;
        if(fits.Length!=leader.Squadron.Members.Count)throw new InvalidDataException("함급의 편대 슬롯 수와 다릅니다.");
        for(int i=0;i<fits.Length;i++)
        {
            var def=fits[i].Definition;
            if(owned is not null&&!owned.Contains(def.Id))throw new InvalidDataException("보유하지 않은 함급입니다.");
            if(!Allows(leader,i,def))throw new InvalidDataException("이 슬롯에는 같은 진영의 허용 함종만 배치할 수 있습니다.");
        }
        string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",fits.Select(f=>f.ContentHash))))).ToLowerInvariant();
        return new("squad-"+hash,Array.AsReadOnly(fits));
    }
    public static string RoleName(ShipBranch branch)=>branch switch
    {
        ShipBranch.Escort=>"호위함",ShipBranch.LineBattleship=>"전함",ShipBranch.Interceptor=>"요격함",
        ShipBranch.GunCruiser=>"포격순양함",ShipBranch.AirDefenseCruiser=>"방공순양함",ShipBranch.MissileCruiser=>"미사일순양함",
        ShipBranch.Recon=>"정찰함",ShipBranch.Assault=>"강습함",
        _=>branch.ToString(),
    };
    // Equipment presets; tier gating and ownership are checked by CreateTiered.
    public static SquadronLoadout AiPreset(string id,int preset)
    {
        ShipFit Fit(string hull)
        {
            var def=ShipDefinitions.ById(hull);var mounts=new List<EquipmentMount>();
            if(preset==0)return Empty(hull);
            foreach(var bay in def.Bays)
            {
                var preference=preset==1?new[]{"sensor-array","shield-capacity","drone-hangar","ecm-amplifier"}
                    :new[]{"ecm-amplifier","drone-hangar","shield-capacity","sensor-array"};
                foreach(string device in preference)
                {
                    var next=mounts.Append(new EquipmentMount(bay.Id,device)).ToArray();
                    try{ResolveFit(new(hull,next));mounts.Add(next[^1]);break;}
                    catch(InvalidDataException){/* Incompatible bay or budget: try the next device. */}
                }
            }
            return new(hull,mounts.ToArray());
        }
        return new(Default(id).Members.Select(s=>Fit(s.HullId)).ToArray());
    }
}
