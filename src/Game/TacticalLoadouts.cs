using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public sealed partial class HangarProfile
{
    public Dictionary<DesignFamily,string[]> TacticalResearch { get; set; }=new(){{DesignFamily.Earth,Array.Empty<string>()},{DesignFamily.Mars,Array.Empty<string>()}};
    internal void MigrateEquipmentParts()
    {
        // The former bay device is now a physical part, with its original socket ID and damage geometry.
        // Reject an ambiguous old+new double installation rather than silently discarding a fit.
        ShipComponentSelection? Merge(ShipComponentSelection? components,EquipmentMount[] old)
        {
            if(old is null)throw new InvalidDataException("부품 배열 누락");
            if(old.Length==0)return components;
            if(old.Any(m=>m is null))throw new InvalidDataException("부품 장착 정보 누락");
            var all=(components?.AuxiliaryParts??Array.Empty<EquipmentMount>()).Concat(old).ToArray();
            if(all.Any(m=>m is null)||all.Select(m=>m.BayId).Distinct().Count()!=all.Length)throw new InvalidDataException("중복 부품 / 누락된 장착 정보 이관");
            return (components??new()) with{AuxiliaryParts=all};
        }
        if(Hulls is null)return;
        foreach(var state in Hulls.Values)
        {
            if(state?.Presets is not {Length:3}||state.SystemPresets is not {Length:3})continue;
            for(int i=0;i<3;i++)if(state.Presets[i] is { } old)
            {state.SystemPresets[i]=Merge(state.SystemPresets[i],old);state.Presets[i]=Array.Empty<EquipmentMount>();}
            if(state.Squadron is null)throw new InvalidDataException("편대 정보 누락");
            for(int i=0;i<state.Squadron.Length;i++)if(state.Squadron[i] is {Own:{ } own} member)
                state.Squadron[i]=member with{Own=own with{Components=Merge(own.Components,own.Equipment),Equipment=Array.Empty<EquipmentMount>()}};
        }
    }
    internal static EquipmentMount[] PhysicalParts(ShipFit fit)=>(fit.Components?.AuxiliaryParts??Array.Empty<EquipmentMount>()).Concat(fit.Equipment).ToArray();
    internal static ShipFit WithPhysicalPart(ShipFit fit,string bay,string? id)
    {
        var parts=PhysicalParts(fit).Where(p=>p.BayId!=bay).ToList();if(id is not null)parts.Add(new(bay,id));
        return fit with{Equipment=Array.Empty<EquipmentMount>(),Components=(fit.Components??new()) with{AuxiliaryParts=parts.Count==0?null:parts.OrderBy(p=>p.BayId,StringComparer.Ordinal).ToArray()}};
    }
    private void CommitFit(HangarSlot target,ShipFit fit,int index)
    {
        if(target.UsesOwn)
        {
            var next=EditableSquad(index,leaderAllowed:true);
            next[index]=target with{Own=new(fit.Equipment.ToArray(),fit.Components)};Commit(next);
        }
        else{Hulls[target.HullId].Presets[target.Preset]=fit.Equipment.ToArray();Hulls[target.HullId].SystemPresets[target.Preset]=fit.Components;}
    }
    public void SetPhysicalPart(HangarSlot target,int index,string bay,string? id)
    {var fit=WithPhysicalPart(Fit(target),bay,id);FleetLoadouts.ResolveFit(fit);CommitFit(target,fit,index);}
    public bool HasTactical(DesignFamily design,string id)=>TacticalResearch[design].Contains(id);
    public void SetTactical(HangarSlot target,int index,TacticalMount[] mounts,bool research=false)
    {
        if(!Owned.Contains(target.HullId)||Research is null)throw new InvalidDataException("보유하지 않은 함선");
        var before=Fit(target);var fit=before with{Components=(before.Components??new()) with{TacticalModules=mounts.Length==0?null:mounts}};
        var def=FleetLoadouts.ResolveFit(fit).Definition;var pending=mounts.Select(m=>m.ModuleId).Distinct().Where(id=>!HasTactical(def.Design,id)).ToArray();
        long price=pending.Sum(id=>TacticalModulesCatalog.ById(id).ResearchCost);
        if(price>0&&!research)throw new InvalidDataException("전술 모듈 연구 필요");
        if(Research.Points[def.Design]<price)throw new InvalidDataException("연구 점수 부족");
        CommitFit(target,fit,index);
        Research.Points[def.Design]-=price;TacticalResearch[def.Design]=TacticalResearch[def.Design].Concat(pending).ToArray();
    }
    private void ValidateTacticalResearch(string hull,ShipComponentSelection? parts)
    {
        if(TacticalResearch is null||TacticalResearch.Count!=2||Enum.GetValues<DesignFamily>().Any(d=>!TacticalResearch.TryGetValue(d,out var ids)||ids is null||ids.Length>8||ids.Distinct().Count()!=ids.Length))throw new InvalidDataException("전술 연구 저장 오류");
        foreach(var ids in TacticalResearch.Values)foreach(string id in ids)TacticalModulesCatalog.ById(id);
        var design=ShipDefinitions.ById(hull).Design;
        if(parts?.TacticalModules?.Any(m=>m is null||!HasTactical(design,m.ModuleId))==true)throw new InvalidDataException("미연구 또는 손상된 전술 모듈 장착");
    }
}
