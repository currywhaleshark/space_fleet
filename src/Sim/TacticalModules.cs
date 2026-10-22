using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;

namespace SpaceFleet.Sim;

public enum TacticalEffect { RepairStores, RepairController, GeneratorOverload, HeatVent, AirDefense, QuickLoader, FocusSearch, ShortJump }
public enum TacticalCategory { Support, Power, Defense, Sensors, Propulsion }
public sealed record TacticalDefinition(string Id,string Name,TacticalEffect Effect,bool Active,float Duration,float Cooldown,
    float PowerFraction,float HeatFraction,float MassFraction,int ResearchCost,int MinTier,string Description)
{
    public BaySize Size=>Effect is TacticalEffect.GeneratorOverload or TacticalEffect.ShortJump?BaySize.Medium:BaySize.Small;
    public TacticalCategory Category=>Effect switch {
        TacticalEffect.GeneratorOverload or TacticalEffect.HeatVent=>TacticalCategory.Power,
        TacticalEffect.AirDefense or TacticalEffect.QuickLoader=>TacticalCategory.Defense,
        TacticalEffect.FocusSearch=>TacticalCategory.Sensors,TacticalEffect.ShortJump=>TacticalCategory.Propulsion,_=>TacticalCategory.Support};
}
/// <summary>Array order is the active-key order. Passive mounts never consume a key.</summary>
public sealed record TacticalMount(string BayId,string ModuleId);
public sealed record TacticalBay(string Id,Vector3 Center,Vector3 HalfSize,PowerGrid Grid,BaySize Size,TacticalCategory[] Accepts);
public sealed record InstalledTactical(TacticalMount Mount,TacticalDefinition Device,ModuleDefinition DamageModule,float PowerMw,float HeatMw);

public static class TacticalModulesCatalog
{
    public static readonly IReadOnlyList<TacticalDefinition> All=Array.AsReadOnly(new[]{
        new TacticalDefinition("repair-stores","예비 복구 자재",TacticalEffect.RepairStores,false,0,0,.005f,.001f,.004f,80,1,"전투 시작 시 응급복구 자재 +2. 파괴 시 남은 예비 자재 손실. 전투 중 보충 없음."),
        new TacticalDefinition("repair-controller","복구 통제기",TacticalEffect.RepairController,false,0,0,.015f,.005f,.003f,90,1,"복구 작업 시간 25% 단축. 피격 후 대기시간과 복구 상한은 유지."),
        new TacticalDefinition("generator-overload","발전기 과부하",TacticalEffect.GeneratorOverload,true,8,90,.02f,.02f,.004f,100,2,"8초간 실제 발전량 +25%. 추가 폐열. 과열·발전기·전력망 손상 시 중단."),
        new TacticalDefinition("heat-vent","긴급 열 배출",TacticalEffect.HeatVent,true,6,100,.02f,.001f,.004f,100,2,"열용량의 최대 30% 즉시 배출. 6초간 적외선 노출 +60%."),
        new TacticalDefinition("air-defense-controller","확장 방공 통제기",TacticalEffect.AirDefense,false,0,0,.02f,.007f,.004f,120,2,"공유 센서에 잡힌 아군 위협을 우선 배정. PD·드론 중복 요격 감소. 무장 사거리 불변."),
        new TacticalDefinition("quick-loader","미사일 신속 장전기",TacticalEffect.QuickLoader,false,0,0,.02f,.01f,.006f,100,2,"미사일 재장전 시간 15% 단축. 탄수·유도·탄두 성능은 유지."),
        new TacticalDefinition("focus-search","집중 탐색",TacticalEffect.FocusSearch,true,10,60,.02f,.008f,.003f,120,2,"10초간 기수 ±35° 센서 관측 강화. 그 밖의 방향은 관측 약화. 신호 노출 +20%."),
        new TacticalDefinition("short-jump","단거리 초공간 도약",TacticalEffect.ShortJump,true,3,120,.03f,.015f,.008f,180,3,"3초 준비 후 최대 10 km 기수 방향 도약. 관성 유지. 준비 중 사격 불가. 도착 공간이 막히면 취소.")
    });
    public static TacticalDefinition ById(string id)=>All.FirstOrDefault(d=>d.Id==id)??throw new InvalidDataException("없는 전술 모듈: "+id);
    public static ShipFit EquipAI(ShipFit fit,int preset)
    {
        if(preset==0)return fit;
        var hull=FleetLoadouts.ResolveFit(fit).Definition;
        string[] choices=hull.Branch switch{
            ShipBranch.AirDefenseCruiser=>new[]{"air-defense-controller","heat-vent","repair-controller"},
            ShipBranch.MissileCruiser=>new[]{"quick-loader","focus-search","repair-stores"},
            _ when hull.Kind==HullKind.Interceptor=>new[]{"focus-search","short-jump","repair-controller"},
            _=>new[]{"heat-vent","generator-overload","repair-stores","repair-controller"}};
        var bays=Bays(hull);var mounts=new List<TacticalMount>();
        foreach(var id in choices)
        {
            if(mounts.Count>=Math.Min(2,bays.Length))break;
            var next=fit with{Components=(fit.Components??new()) with{TacticalModules=mounts.Append(new TacticalMount(bays[mounts.Count].Id,id)).ToArray()}};
            try{FleetLoadouts.ResolveFit(next);fit=next;mounts=next.Components!.TacticalModules!.ToList();}catch(InvalidDataException){ }
        }
        return fit;
    }
    internal static bool Overlap(Vector3 a,Vector3 ah,Vector3 b,Vector3 bh)=>Mathf.Abs(a.X-b.X)<ah.X+bh.X+.01f&&Mathf.Abs(a.Y-b.Y)<ah.Y+bh.Y+.01f&&Mathf.Abs(a.Z-b.Z)<ah.Z+bh.Z+.01f;
    /// <summary>Reserve empty interior compartments, independent of auxiliary-part sockets. No fixed module is moved.</summary>
    public static TacticalBay[] Bays(ShipDefinition hull)
    {
        var bays=new List<TacticalBay>();int count=hull.Kind==HullKind.Battleship?6:hull.Kind==HullKind.Escort?5:4;
        float edge=Math.Max(.12f,hull.Flight.Length*.004f);var half=new Vector3(edge,edge,edge);
        foreach(var section in hull.HullSections.OrderByDescending(s=>s.HalfSize.X*s.HalfSize.Y*s.HalfSize.Z))
        for(int z=1;z<10&&bays.Count<count;z++)
        for(int y=1;y<6&&bays.Count<count;y++)
        for(int x=1;x<6&&bays.Count<count;x++)
        {
            var center=section.Center+section.HalfSize*new Vector3(x/3f-1,y/3f-1,z/5f-1);
            if(Mathf.Abs(center.X-section.Center.X)+edge>section.HalfSize.X||Mathf.Abs(center.Y-section.Center.Y)+edge>section.HalfSize.Y||Mathf.Abs(center.Z-section.Center.Z)+edge>section.HalfSize.Z)continue;
            if(hull.Modules.Any(m=>m.Kind!=ModuleKind.TacticalModule&&Overlap(center,half,m.Center,m.HalfSize))
                ||hull.Bays.Any(b=>Overlap(center,half,b.Center,b.HalfSize))||bays.Any(b=>Overlap(center,half,b.Center,b.HalfSize)))continue;
            bays.Add(new("tactical-"+bays.Count,center,half,center.X<0?PowerGrid.Port:PowerGrid.Starboard,
                bays.Count<count-2?BaySize.Medium:BaySize.Small,Enum.GetValues<TacticalCategory>()));
        }
        return bays.ToArray();
    }
    public static string? Restriction(ShipDefinition hull,TacticalDefinition d)
    {
        if(ResearchTree.Catalog.ByHull(hull.Id).Tier<d.MinTier)return $"T{d.MinTier} 이상 필요";
        if(d.Effect==TacticalEffect.QuickLoader&&hull.Missiles is null)return "미사일 발사기 필요";
        if(d.Effect==TacticalEffect.AirDefense&&hull.PointDefense is null&&hull.DefenseDrones is null)return "PD 또는 방어 드론 필요";
        return null;
    }
    internal static ShipDefinition Install(ShipDefinition hull,TacticalMount[]? mounts)
    {
        if(mounts is null||mounts.Length==0)return hull;
        if(mounts.Length>6||mounts.Any(m=>m is null)||mounts.Select(m=>m.BayId).Distinct().Count()!=mounts.Length
            ||mounts.Select(m=>m.ModuleId).Distinct().Count()!=mounts.Length||mounts.Count(m=>ById(m.ModuleId).Active)>4)
            throw new InvalidDataException("중복 전술 모듈 / 베이 또는 활성 슬롯 4개 초과");
        var bays=Bays(hull);var installed=new List<InstalledTactical>();float mass=0;
        foreach(var mount in mounts)
        {
            var device=ById(mount.ModuleId);if(Restriction(hull,device) is { } error)throw new InvalidDataException(error);
            var bay=bays.FirstOrDefault(b=>b.Id==mount.BayId)??throw new InvalidDataException("없는 전술 베이");
            if(bay.Size<device.Size||!bay.Accepts.Contains(device.Category))throw new InvalidDataException("전술 베이 규격 / 분류 불일치");
            float hp=hull.Kind==HullKind.Interceptor?35:hull.Kind==HullKind.Escort?90:160;
            var damage=new ModuleDefinition(bay.Id,device.Name,ModuleKind.TacticalModule,bay.Center,bay.HalfSize,hp,15,bay.Grid);
            installed.Add(new(mount,device,damage,hull.Power.OutputMw*device.PowerFraction,hull.Power.OutputMw*device.HeatFraction));
            mass+=hull.Flight.MassKg*device.MassFraction;
        }
        var json=JsonNode.Parse(hull.ToJson())!.AsObject();
        json[nameof(ShipDefinition.Modules)]=JsonNode.Parse(ShipDefinition.SerializeData(hull.Modules.Concat(installed.Select(i=>i.DamageModule)).ToArray()));
        json[nameof(ShipDefinition.TacticalModules)]=JsonNode.Parse(ShipDefinition.SerializeData(installed));
        var f=hull.Flight;float ratio=f.MassKg/(f.MassKg+mass);
        json[nameof(ShipDefinition.Flight)]=JsonNode.Parse(ShipDefinition.SerializeData(f with{MassKg=f.MassKg+mass,ForwardAccel=f.ForwardAccel*ratio,StrafeAccel=f.StrafeAccel*ratio,BrakeAccel=f.BrakeAccel*ratio}));
        return ShipDefinition.Parse(json.ToJsonString(),strict:true);
    }
    internal static void Validate(ShipDefinition hull)
    {
        var installed=hull.TacticalModules;
        if(installed is null){if(hull.Modules.Any(m=>m.Kind==ModuleKind.TacticalModule))throw new InvalidDataException("전술 모듈 장착 정보 누락");return;}
        if(installed.Length>6||installed.Any(i=>i is null||i.Mount is null||i.Device is null||i.DamageModule is null)||installed.Select(i=>i.Mount.BayId).Distinct().Count()!=installed.Length
            ||installed.Select(i=>i.Device.Id).Distinct().Count()!=installed.Length||installed.Count(i=>i.Device.Active)>4)throw new InvalidDataException("전술 모듈 중복");
        foreach(var item in installed)
        {
            var d=item.Device;
            if(!Enum.IsDefined(d.Effect)||!float.IsFinite(d.Duration)||!float.IsFinite(d.Cooldown)||d.Duration<0||d.Cooldown<0
                ||(d.Active&&(d.Duration<=0||d.Cooldown<d.Duration))||!float.IsFinite(d.MassFraction)||d.MassFraction<=0
                ||!float.IsFinite(d.PowerFraction)||d.PowerFraction<=0||!float.IsFinite(d.HeatFraction)||d.HeatFraction<0)
                throw new InvalidDataException("전술 모듈 성능 데이터 오류");
            if(item.Mount.ModuleId!=item.Device.Id||item.DamageModule.Id!=item.Mount.BayId||item.DamageModule.Kind!=ModuleKind.TacticalModule
                ||!hull.Modules.Contains(item.DamageModule)||item.PowerMw<=0||!float.IsFinite(item.PowerMw)||item.HeatMw<0||!float.IsFinite(item.HeatMw))throw new InvalidDataException("전술 모듈 데이터 불일치");
            var m=item.DamageModule;
            if(hull.Modules.Any(o=>o.Id!=m.Id&&Overlap(m.Center,m.HalfSize,o.Center,o.HalfSize))||hull.Bays.Any(b=>Overlap(m.Center,m.HalfSize,b.Center,b.HalfSize)))throw new InvalidDataException("전술 베이 겹침");
        }
        if(hull.Modules.Count(m=>m.Kind==ModuleKind.TacticalModule)!=installed.Length)throw new InvalidDataException("전술 모듈 장착 수 불일치");
        if(hull.Power.Engines+hull.Power.Shields+hull.Power.Weapons+hull.Power.Sensors+hull.InstalledEquipment.Sum(i=>i.Equipment.PowerMw)+installed.Sum(i=>i.PowerMw)>hull.Power.OutputMw
            ||hull.InstalledEquipment.Sum(i=>i.Equipment.HeatMw)+installed.Sum(i=>i.HeatMw)>hull.Power.CoolingMw)throw new InvalidDataException("전술 모듈 전력 / 냉각 예산 초과");
    }
}
