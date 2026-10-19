using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

/// <summary>Equipment and systems kept by one squad slot instead of a shared hull preset.</summary>
public sealed record HangarFit(EquipmentMount[] Equipment,ShipComponentSelection? Components=null);
/// <summary>Squad member: a hull and its fit. Preset 0..2 shares the hull's preset; Individual uses Own.
/// Own survives switching back to a shared preset, so the slot's custom fit can be restored.</summary>
public sealed record HangarSlot(string HullId,int Preset)
{
    public const int Individual=3;
    // An init property rather than a constructor parameter: the save reader sets it after construction.
    public HangarFit? Own { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public bool UsesOwn=>Preset==Individual;
}
public sealed class HangarHull
{
    public EquipmentMount[][] Presets { get; set; } = {Array.Empty<EquipmentMount>(),Array.Empty<EquipmentMount>(),Array.Empty<EquipmentMount>()};
    public ShipComponentSelection?[] SystemPresets { get; set; } = new ShipComponentSelection?[3];
    // Null entries are empty member slots (never the leader). Launch requires every slot filled.
    public HangarSlot?[] Squadron { get; set; } = Array.Empty<HangarSlot?>();
}

/// <summary>Local editable choices. Combat receives a resolved snapshot, never this mutable save model.</summary>
public sealed class HangarProfile
{
    public int Version { get; set; } = 5;
    public ResearchProgress? Research { get; set; }
    public string SelectedHullId { get; set; } = "interceptor";
    public Dictionary<string,HangarHull> Hulls { get; set; } = new(StringComparer.Ordinal);
    // "My ships" carousel: the player's own order, favourites (always first) and hidden hulls.
    public string[] ShipOrder { get; set; } = Array.Empty<string>();
    public string[] Favorites { get; set; } = Array.Empty<string>();
    public string[] Hidden { get; set; } = Array.Empty<string>();
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
    public ShipFit Fit(HangarSlot slot) => slot.UsesOwn&&slot.Own is { } own
        ? new(slot.HullId,own.Equipment.ToArray(),Research?.ForHull(slot.HullId).ToArray()){Components=own.Components}
        : new(slot.HullId,Hulls[slot.HullId].Presets[slot.Preset].ToArray(),Research?.ForHull(slot.HullId).ToArray())
            {Components=Hulls[slot.HullId].SystemPresets[slot.Preset]};
    public int EmptySlots(string? leader=null)=>Hulls[leader??SelectedHullId].Squadron.Count(s=>s is null);
    public SquadronLoadout Squadron(string? leader=null)
    {
        int empty=EmptySlots(leader);
        if(empty>0)throw new InvalidDataException($"빈 슬롯 {empty}개 · 채워야 출격할 수 있습니다.");
        return new(Hulls[leader??SelectedHullId].Squadron.Select(s=>Fit(s!)).ToArray());
    }
    /// <summary>Validation view of a squad with empty slots: each empty slot takes its default member.</summary>
    private SquadronLoadout Filled(string leader,HangarSlot?[] slots)
    {
        var defaults=FleetLoadouts.Default(leader).Members;
        return new(slots.Select((s,i)=>s is null?defaults[i] with{Upgrades=Research?.ForHull(defaults[i].HullId).ToArray()}:Fit(s)).ToArray());
    }
    private HangarSlot?[] EditableSquad(int index,bool leaderAllowed=false)
    {
        var state=Hulls[SelectedHullId];
        if(index<0||index>=state.Squadron.Length||index==0&&!leaderAllowed)throw new InvalidDataException("잘못된 편대 슬롯입니다.");
        return (HangarSlot?[])state.Squadron.Clone();
    }
    private void Commit(HangarSlot?[] next)
    {FleetLoadouts.Resolve(Filled(SelectedHullId,next),Hulls.Keys.ToHashSet());Hulls[SelectedHullId].Squadron=next;}
    public void SetSlot(int index,string hull,int preset)
    {
        if(preset<0||preset>=3||!Hulls.ContainsKey(hull)||!Owned.Contains(hull))throw new InvalidDataException("사용할 수 없는 함선 세팅입니다.");
        if(index==0&&hull!=SelectedHullId)throw new InvalidDataException("기함 변경은 함급 선택을 사용해야 합니다.");
        var next=EditableSquad(index,leaderAllowed:true);
        // Same hull keeps its individual fit; a different hull starts from the chosen shared preset.
        next[index]=new(hull,preset){Own=next[index]?.HullId==hull?next[index]!.Own:null};
        Commit(next);
    }
    public void ClearSlot(int index){var next=EditableSquad(index);next[index]=null;Commit(next);}
    /// <summary>Preset 0..2 or Individual. The first switch to Individual copies the current shared preset.</summary>
    public void SetSlotMode(int index,int mode)
    {
        if(mode<0||mode>HangarSlot.Individual)throw new InvalidDataException("잘못된 장비 설정입니다.");
        var next=EditableSquad(index,leaderAllowed:true);
        var slot=next[index]??throw new InvalidDataException("빈 슬롯입니다.");
        var own=slot.Own;
        if(mode==HangarSlot.Individual&&own is null)
        {var shared=Fit(slot);own=new(shared.Equipment.ToArray(),shared.Components);}
        next[index]=slot with{Preset=mode,Own=own};Commit(next);
    }
    public void FillEmptySlots()
    {
        var next=(HangarSlot?[])Hulls[SelectedHullId].Squadron.Clone();var defaults=FleetLoadouts.Default(SelectedHullId).Members;
        for(int i=0;i<next.Length;i++)next[i]??=new(defaults[i].HullId,0);
        Commit(next);
    }
    public void SetSlotEquipment(int index,string bay,string? device)
    {
        var next=EditableSquad(index,leaderAllowed:true);var slot=next[index];
        if(slot is not {UsesOwn:true,Own:{ } own})throw new InvalidDataException("개별 설정 슬롯이 아닙니다.");
        if(!ShipDefinitions.ById(slot.HullId).Bays.Any(b=>b.Id==bay))throw new InvalidDataException("없는 베이입니다.");
        var mounts=own.Equipment.Where(m=>m.BayId!=bay).ToList();if(device is not null)mounts.Add(new(bay,device));
        var fit=own with{Equipment=mounts.OrderBy(m=>m.BayId,StringComparer.Ordinal).ToArray()};
        FleetLoadouts.ResolveFit(Fit(slot with{Own=fit}));
        next[index]=slot with{Own=fit};Commit(next);
    }
    public void SetSlotComponent(int index,ComponentSlot system,string? id,bool research=false)
    {
        var next=EditableSquad(index,leaderAllowed:true);var slot=next[index];
        if(slot is not {UsesOwn:true,Own:{ } own}||Research is null)throw new InvalidDataException("개별 설정 슬롯이 아닙니다.");
        var parts=(own.Components??new()).With(system,id);
        if(system==ComponentSlot.MainGun&&id is not null)parts=parts with{MainGuns=null}; // catalog primary replaces socket parts
        var fit=own with{Components=parts.IsEmpty?null:parts};
        FleetLoadouts.ResolveFit(Fit(slot with{Own=fit})); // Validate the complete budget before charging RP.
        if(id is not null&&!Research.HasComponent(slot.HullId,id))
        {
            if(!research)throw new InvalidDataException("부품을 먼저 연구해야 합니다.");
            Research.BuyComponent(slot.HullId,id);
        }
        next[index]=slot with{Own=fit};Commit(next);
    }
    /// <summary>Carousel order: favourites first, then the saved order, then the remaining hulls by tier.</summary>
    public string[] ShipList(bool includeHidden=false)
    {
        var owned=Owned;
        var known=ShipOrder.Where(owned.Contains).Concat(FleetLoadouts.Hulls.Where(h=>owned.Contains(h.Id))
            .OrderByDescending(h=>ResearchTree.Catalog.ByHull(h.Id).Tier).ThenBy(h=>h.Design).Select(h=>h.Id)).Distinct()
            .Where(id=>includeHidden||!Hidden.Contains(id)).ToArray();
        return known.Where(Favorites.Contains).Concat(known.Where(id=>!Favorites.Contains(id))).ToArray();
    }
    public void SetFavorite(string hull,bool favorite)
    {
        if(!Owned.Contains(hull))throw new InvalidDataException("해금하지 않은 함급입니다.");
        Favorites=favorite?Favorites.Append(hull).Distinct().ToArray():Favorites.Where(h=>h!=hull).ToArray();
    }
    public void SetHidden(string hull,bool hidden)
    {
        if(!Owned.Contains(hull))throw new InvalidDataException("해금하지 않은 함급입니다.");
        if(hidden&&hull==SelectedHullId)throw new InvalidDataException("현재 기함은 숨길 수 없습니다.");
        Hidden=hidden?Hidden.Append(hull).Distinct().ToArray():Hidden.Where(h=>h!=hull).ToArray();
        if(hidden)Favorites=Favorites.Where(h=>h!=hull).ToArray();
    }
    /// <summary>Drag order: moves a hull in front of another; crossing into or out of favourites follows the target.</summary>
    public void MoveShip(string hull,string before)
    {
        if(hull==before)return;
        var order=ShipList(includeHidden:true).ToList();
        if(!order.Contains(hull)||!order.Contains(before))throw new InvalidDataException("목록에 없는 함급입니다.");
        order.Remove(hull);order.Insert(order.IndexOf(before),hull);ShipOrder=order.ToArray();
        bool favorite=Favorites.Contains(before);
        if(favorite!=Favorites.Contains(hull))SetFavorite(hull,favorite);
        if(favorite)Favorites=ShipOrder.Where(Favorites.Contains).ToArray();
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
        if(slot==ComponentSlot.MainGun&&id is not null)next=next with{MainGuns=null}; // catalog primary replaces socket parts
        var fit=Fit(new(hull,preset)) with{Components=next.IsEmpty?null:next};
        FleetLoadouts.ResolveFit(fit); // Validate the complete budget before charging RP or changing the preset.
        if(id is not null&&!Research.HasComponent(hull,id))
        {
            if(!research)throw new InvalidDataException("부품을 먼저 연구해야 합니다.");
            Research.BuyComponent(hull,id);
        }
        state.SystemPresets[preset]=fit.Components;
    }
    /// <summary>Main gun part at one socket of a shared preset (null = stock). Researches it first when asked.</summary>
    public void SetMainGun(string hull,int preset,int socket,string? part,bool research=false)
    {
        if(!Hulls.TryGetValue(hull,out var state)||preset<0||preset>=3||!Owned.Contains(hull)||Research is null)
            throw new InvalidDataException("잘못된 프리셋입니다.");
        var next=WithMainGun(hull,state.SystemPresets[preset],socket,part);
        var fit=Fit(new(hull,preset)) with{Components=next};
        FleetLoadouts.ResolveFit(fit); // Validate sockets, volume and supply before charging RP.
        BuyIfNeeded(hull,part,research);
        state.SystemPresets[preset]=next;
    }
    /// <summary>Main gun part at one socket of an individually fitted squad slot.</summary>
    public void SetSlotMainGun(int index,int socket,string? part,bool research=false)
    {
        var next=EditableSquad(index,leaderAllowed:true);var slot=next[index];
        if(slot is not {UsesOwn:true,Own:{ } own}||Research is null)throw new InvalidDataException("개별 설정 슬롯이 아닙니다.");
        var fit=own with{Components=WithMainGun(slot.HullId,own.Components,socket,part)};
        FleetLoadouts.ResolveFit(Fit(slot with{Own=fit}));
        BuyIfNeeded(slot.HullId,part,research);
        next[index]=slot with{Own=fit};Commit(next);
    }
    internal static ShipComponentSelection? WithMainGun(string hull,ShipComponentSelection? current,int socket,string? part)
    {
        int sockets=ShipDefinitions.ById(hull).Battery?.Hardpoints.Length??0;
        if(socket<0||socket>=sockets)throw new InvalidDataException("없는 주포 소켓입니다.");
        var guns=(current?.MainGuns?.ToArray()??new string?[sockets]);
        if(guns.Length!=sockets)throw new InvalidDataException("잘못된 주포 소켓 수입니다.");
        if(part is not null&&MainGunParts.IsStock(MainGunParts.ById(part)))part=null;
        guns[socket]=part;
        // Socket parts replace a catalog primary; all-stock sockets drop back to no selection.
        var next=(current??new()).With(ComponentSlot.MainGun,null) with{MainGuns=guns.All(g=>g is null)?null:guns};
        return next.IsEmpty?null:next;
    }
    private void BuyIfNeeded(string hull,string? part,bool research)
    {
        var design=ShipDefinitions.ById(hull).Design;
        if(part is null||Research!.HasPart(design,part))return;
        if(!research)throw new InvalidDataException("부품을 먼저 연구해야 합니다.");
        Research.BuyPart(design,part);
    }
    private bool Unresearched(string hull,ShipComponentSelection? parts)=>parts?.Ids.Any(part=>Research?.HasComponent(hull,part)!=true)==true
        ||parts?.MainGuns?.OfType<string>().Any(part=>Research?.HasPart(ShipDefinitions.ById(hull).Design,part)!=true)==true;
    public void Validate()
    {
        if(Version is not (1 or 2 or 3 or 4 or 5)||Hulls is null||Hulls.Count==0||Hulls.Count>128||!Hulls.ContainsKey(SelectedHullId))
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
                if(Unresearched(id,hull.SystemPresets[index]))
                    throw new InvalidDataException("연구하지 않은 교체 부품이 장착되어 있습니다.");
                FleetLoadouts.ResolveFit(Fit(new(id,index)));
            }
            foreach(var slot in hull.Squadron)
            {
                if(slot is null)continue; // An empty member slot (the leader slot is checked above).
                if(slot.Preset<0||slot.Preset>HangarSlot.Individual||!Hulls.ContainsKey(slot.HullId)||slot.UsesOwn&&slot.Own is null
                    ||slot.Own is {Equipment:null or {Length:>16}})throw new InvalidDataException("잘못된 편대원 프리셋 참조입니다.");
                if(Unresearched(slot.HullId,slot.Own?.Components))
                    throw new InvalidDataException("연구하지 않은 교체 부품이 장착되어 있습니다.");
            }
        }
        foreach(string id in Hulls.Keys)FleetLoadouts.Resolve(Filled(id,Hulls[id].Squadron),Hulls.Keys.ToHashSet());
        foreach(var list in new[]{ShipOrder,Favorites,Hidden})
            if(list is null||list.Length>128||list.Distinct().Count()!=list.Length||list.Any(h=>!Hulls.ContainsKey(h)))
                throw new InvalidDataException("잘못된 함선 목록 설정입니다.");
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
                profile.Version=5;profile.Validate();return new(profile,true,"이전 격납고를 이관했습니다. 기존 함급과 세팅은 유지됩니다.");
            }
            // Catalog expansion adds empty presets, never ownership or research points.
            foreach(var hull in FleetLoadouts.Hulls)if(!profile.Hulls.ContainsKey(hull.Id))
                profile.Hulls.Add(hull.Id,new HangarHull{Squadron=FleetLoadouts.Default(hull.Id).Members.Select(m=>new HangarSlot(m.HullId,0)).ToArray()});
            // v5: per-hull component unlocks become design-wide; research IDs and spent RP carry over unchanged.
            string? note=null;
            if(profile.Version<5&&profile.Research is { } research)
            {
                if(research.Components.Count>0)note="교체 부품 해금을 진영 공유로 옮겼습니다. 사격통제·반응로 연구는 포좌 확장·주포 전원 개량으로 바뀌었습니다.";
                research.ShareLegacyUnlocks();
            }
            profile.Version=5;profile.Validate();
            return new(profile,true,note);
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
