using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public sealed record HangarSlot(string HullId,int Preset);
public sealed class HangarHull
{
    public EquipmentMount[][] Presets { get; set; } = {Array.Empty<EquipmentMount>(),Array.Empty<EquipmentMount>(),Array.Empty<EquipmentMount>()};
    public ShipComponentSelection?[] SystemPresets { get; set; } = new ShipComponentSelection?[3];
    public HangarSlot[] Squadron { get; set; } = Array.Empty<HangarSlot>();
}

/// <summary>Local editable choices. Combat receives a resolved snapshot, never this mutable save model.</summary>
public sealed class HangarProfile
{
    public int Version { get; set; } = 3;
    public ResearchProgress? Research { get; set; }
    public string SelectedHullId { get; set; } = "interceptor";
    public Dictionary<string,HangarHull> Hulls { get; set; } = new(StringComparer.Ordinal);
    public static HangarProfile Create(string selected="interceptor")
    {
        var profile=new HangarProfile {SelectedHullId=selected};
        foreach(var def in FleetLoadouts.Hulls)
            profile.Hulls.Add(def.Id,new HangarHull {Squadron=FleetLoadouts.Default(def.Id).Members.Select(m=>new HangarSlot(m.HullId,0)).ToArray()});
        profile.Research=ResearchProgress.New();
        foreach(string id in profile.Hulls.Keys)profile.Research.GrantPath(id);
        profile.Validate();return profile;
    }
    public static HangarProfile Campaign(DesignFamily design=DesignFamily.Earth)
    {var p=Create(ResearchTree.Catalog.Root(design).HullId!);p.Research=ResearchProgress.New();p.Validate();return p;}
    public HashSet<string> Owned=>Research?.Owned??Hulls.Keys.ToHashSet();
    public ShipFit Fit(HangarSlot slot) => new(slot.HullId,Hulls[slot.HullId].Presets[slot.Preset].ToArray(),Research?.ForHull(slot.HullId).ToArray())
        {Components=Hulls[slot.HullId].SystemPresets[slot.Preset]};
    public SquadronLoadout Squadron(string? leader=null) => new(Hulls[leader??SelectedHullId].Squadron.Select(Fit).ToArray());
    public void SetSlot(int index,string hull,int preset)
    {
        if(preset<0||preset>=3||!Hulls.ContainsKey(hull)||!Owned.Contains(hull))throw new InvalidDataException("사용할 수 없는 함선 세팅입니다.");
        var state=Hulls[SelectedHullId];
        if(index<0||index>=state.Squadron.Length)throw new InvalidDataException("잘못된 편대 슬롯입니다.");
        if(index==0&&hull!=SelectedHullId)throw new InvalidDataException("기함 변경은 함급 선택을 사용해야 합니다.");
        var next=(HangarSlot[])state.Squadron.Clone();next[index]=new(hull,preset);
        FleetLoadouts.Resolve(new(next.Select(Fit).ToArray()),Hulls.Keys.ToHashSet());
        state.Squadron=next;
    }
    public void SetEquipment(string hull,int preset,string bay,string? device)
    {
        if(!Hulls.TryGetValue(hull,out var state)||preset<0||preset>=3||!Owned.Contains(hull))throw new InvalidDataException("잘못된 프리셋입니다.");
        if(!ShipDefinitions.ById(hull).Bays.Any(b=>b.Id==bay))throw new InvalidDataException("없는 베이입니다.");
        var next=state.Presets[preset].Where(m=>m.BayId!=bay).ToList();
        if(device is not null)next.Add(new(bay,device));
        FleetLoadouts.ResolveFit(Fit(new(hull,preset)) with{Equipment=next});
        state.Presets[preset]=next.OrderBy(m=>m.BayId,StringComparer.Ordinal).ToArray();
    }
    public void SetComponent(string hull,int preset,ComponentSlot slot,string? id,bool research=false)
    {
        if(!Hulls.TryGetValue(hull,out var state)||preset<0||preset>=3||!Owned.Contains(hull)||Research is null)
            throw new InvalidDataException("잘못된 프리셋입니다.");
        var next=(state.SystemPresets[preset]??new()).With(slot,id);
        var fit=Fit(new(hull,preset)) with{Components=next.Ids.Any()?next:null};
        FleetLoadouts.ResolveFit(fit); // Validate the complete budget before charging RP or changing the preset.
        if(id is not null&&!Research.HasComponent(hull,id))
        {
            if(!research)throw new InvalidDataException("부품을 먼저 연구해야 합니다.");
            Research.BuyComponent(hull,id);
        }
        state.SystemPresets[preset]=fit.Components;
    }
    public void Validate()
    {
        if(Version is not (1 or 2 or 3)||Hulls is null||Hulls.Count==0||Hulls.Count>128||!Hulls.ContainsKey(SelectedHullId))
            throw new InvalidDataException("지원하지 않거나 손상된 격납고 저장입니다.");
        if(Version>=2){if(Research is null)throw new InvalidDataException("연구 진행이 없습니다.");Research.Validate();if(!Owned.Contains(SelectedHullId))throw new InvalidDataException("선택한 함급이 잠겨 있습니다.");}
        foreach(var (id,hull) in Hulls)
        {
            ShipDefinitions.ById(id);
            if(hull is null||hull.Presets is not {Length:3}||hull.SystemPresets is not {Length:3}||hull.Squadron is not {Length:>0 and <=16}
                ||hull.Squadron[0]?.HullId!=id)throw new InvalidDataException("잘못된 격납고 구성입니다.");
            for(int index=0;index<hull.Presets.Length;index++)
            {
                var mounts=hull.Presets[index];
                if(mounts is null||mounts.Length>16)throw new InvalidDataException("잘못된 장착 목록입니다.");
                if(hull.SystemPresets[index]?.Ids.Any(part=>Research?.HasComponent(id,part)!=true)==true)
                    throw new InvalidDataException("연구하지 않은 교체 부품이 장착되어 있습니다.");
                FleetLoadouts.ResolveFit(Fit(new(id,index)));
            }
            foreach(var slot in hull.Squadron)
                if(slot is null||slot.Preset<0||slot.Preset>=3||!Hulls.ContainsKey(slot.HullId))throw new InvalidDataException("잘못된 편대원 프리셋 참조입니다.");
        }
        foreach(string id in Hulls.Keys)FleetLoadouts.Resolve(Squadron(id),Hulls.Keys.ToHashSet());
    }
}

public sealed record HangarLoadResult(HangarProfile Profile,bool CanSave,string? Message);
public static class HangarStorage
{
    private static readonly JsonSerializerOptions Options=new()
    {PropertyNamingPolicy=JsonNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,WriteIndented=true};
    public static string Serialize(HangarProfile profile){profile.Validate();return JsonSerializer.Serialize(profile,Options);}
    public static HangarProfile Parse(string json)
    {
        // Reject ambiguous duplicate keys before the serializer can silently choose a value.
        using var doc=JsonDocument.Parse(json);
        void Unique(JsonElement element)
        {
            if(element.ValueKind==JsonValueKind.Object)
            {var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var p in element.EnumerateObject())
                {if(!keys.Add(p.Name))throw new InvalidDataException("중복 저장 필드입니다.");Unique(p.Value);}}
            else if(element.ValueKind==JsonValueKind.Array)foreach(var child in element.EnumerateArray())Unique(child);
        }
        Unique(doc.RootElement);
        var profile=JsonSerializer.Deserialize<HangarProfile>(json,Options)??throw new InvalidDataException("빈 저장입니다.");
        profile.Validate();return profile;
    }
    public static HangarLoadResult Load(string path,string? legacyHull)
    {
        HangarProfile Legacy()
        {
            var old=FleetLoadouts.Hulls.FirstOrDefault(h=>h.Id==legacyHull);
            var p=HangarProfile.Campaign(old?.Design??DesignFamily.Earth);
            if(old is not null){p.Research!.GrantPath(old.Id);foreach(var m in FleetLoadouts.Default(old.Id).Members)p.Research.GrantPath(m.HullId);p.SelectedHullId=old.Id;}
            return p;
        }
        try
        {
            if(!File.Exists(path))return new(Legacy(),true,legacyHull is null?null:"이전 함선 선택을 가져왔습니다.");
            if(new FileInfo(path).Length>1024*1024)throw new InvalidDataException("Save exceeds size limit");
            var profile=Parse(File.ReadAllText(path));
            if(profile.Version==1)
            {
                var oldHulls=profile.Hulls.Keys.ToArray();profile.Research=ResearchProgress.New();
                foreach(string id in oldHulls)profile.Research.GrantPath(id);
                foreach(var hull in FleetLoadouts.Hulls)if(!profile.Hulls.ContainsKey(hull.Id))
                    profile.Hulls.Add(hull.Id,new HangarHull{Squadron=FleetLoadouts.Default(hull.Id).Members.Select(m=>new HangarSlot(m.HullId,0)).ToArray()});
                profile.Version=3;profile.Validate();return new(profile,true,"이전 격납고를 이관했습니다. 기존 함급과 세팅은 유지됩니다.");
            }
            // Catalog expansion adds empty presets, never ownership or research points.
            foreach(var hull in FleetLoadouts.Hulls)if(!profile.Hulls.ContainsKey(hull.Id))
                profile.Hulls.Add(hull.Id,new HangarHull{Squadron=FleetLoadouts.Default(hull.Id).Members.Select(m=>new HangarSlot(m.HullId,0)).ToArray()});
            profile.Version=3;profile.Validate();
            return new(profile,true,null);
        }
        catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {return new(Legacy(),false,"저장 파일을 읽지 못했습니다. 원본을 보존하며 이번 세팅은 저장하지 않습니다.");}
    }
    public static void Save(string path,HangarProfile profile)
    {
        string json=Serialize(profile);string full=Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temporary=full+".tmp";
        using(var stream=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None))
        {using var writer=new StreamWriter(stream,new System.Text.UTF8Encoding(false),leaveOpen:true);writer.Write(json);writer.Flush();stream.Flush(true);}
        if(File.Exists(full))File.Copy(full,full+".bak",true);
        File.Move(temporary,full,true);
    }
}
