using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SpaceFleet.Sim;

/// <summary>Engine, sensor or shield assembly. Absolute ratings at the escort reference hull;
/// per-field stock ratios retain each hull's tier, design and permanent research. One assembly per system.
/// Existing module locations and damage behaviour are retained; these are not weapon-volume additions.</summary>
public sealed record SystemPart(string Id,string Name,ComponentSlot Slot,int Cost,bool Stock,
    float PowerMw,float ReferenceMassKg,float AddedMassKg=0,string? LegacyId=null,
    float ForwardThrustN=0,float StrafeThrustN=0,float BrakeThrustN=0,
    float Strength=0,float Signature=0,int LockChannels=0,
    float Capacity=0,float RechargePerSecond=0,float RechargeDelay=0);

public static class SystemParts
{
    public static readonly ComponentSlot[] Slots={ComponentSlot.Engine,ComponentSlot.Sensor,ComponentSlot.Shield};
    public static IReadOnlyList<SystemPart> All { get; }=Load();
    public static SystemPart? Find(string id)=>All.FirstOrDefault(p=>p.Id==id);
    public static SystemPart ById(string id)=>Find(id)??throw new InvalidDataException("알 수 없는 계통 부품입니다: "+id);
    public static SystemPart StockFor(ComponentSlot slot)=>All.Single(p=>p.Stock&&p.Slot==slot);
    public static bool Available(ShipDefinition hull,ComponentSlot slot)=>Slots.Contains(slot)&&hull.Modules.Any(m=>m.Kind==(slot switch
    {ComponentSlot.Engine=>ModuleKind.Thruster,ComponentSlot.Sensor=>ModuleKind.Sensor,_=>ModuleKind.ShieldEmitter}));
    public static IEnumerable<SystemPart> Candidates(ShipDefinition hull,ComponentSlot slot)=>All.Where(p=>p.Slot==slot&&Available(hull,slot));
    public static void ValidateSelection(ShipDefinition hull,ShipComponentSelection selection)
    {
        foreach(var slot in Slots)if(selection.SystemPart(slot) is { } id)
        {
            var part=ById(id);
            if(part.Slot!=slot||!Available(hull,slot))throw new InvalidDataException("이 함선의 "+ShipComponents.Name(slot)+"에 맞지 않는 부품입니다.");
            if(selection.Get(slot) is not null)throw new InvalidDataException("같은 계통의 구형 교체와 부품을 함께 장착할 수 없습니다.");
        }
    }
    public static ShipComponentSelection? With(ShipComponentSelection? current,ComponentSlot slot,string? id)
    {
        if(!Slots.Contains(slot))throw new InvalidDataException("엔진·센서·실드 부품만 선택할 수 있습니다.");
        if(id is not null)
        {
            var part=ById(id);
            if(part.Slot!=slot)throw new InvalidDataException("부품 종류가 맞지 않습니다.");
            if(part.Stock)id=null;
        }
        var next=(current??new()).With(slot,null);
        next=slot switch{ComponentSlot.Engine=>next with{EnginePart=id},ComponentSlot.Sensor=>next with{SensorPart=id},_=>next with{ShieldPart=id}};
        return next.IsEmpty?null:next;
    }
    // Explicit stock IDs have the same definition/hash as null. Do not silently erase a conflicting legacy fit.
    internal static ShipComponentSelection? Normalize(ShipComponentSelection? current)
    {
        foreach(var slot in Slots)if(current?.SystemPart(slot) is { } id)
        {
            var p=ById(id);
            if(p.Slot!=slot||current.Get(slot) is not null)throw new InvalidDataException("중복되거나 잘못된 계통 부품입니다.");
            if(p.Stock)current=With(current,slot,null);
        }
        return current;
    }
    /// <summary>Composition adapter: replacement ratings divided by the stock assembly's ratings.
    /// Shared mass scaling is applied once alongside legacy parts, before physical bay equipment.</summary>
    internal static ComponentEffects Effects(SystemPart p)
    {
        var s=StockFor(p.Slot);
        return p.Slot switch
        {
            ComponentSlot.Engine=>new(MassFraction:p.AddedMassKg/p.ReferenceMassKg,Power:p.PowerMw/s.PowerMw,
                Forward:p.ForwardThrustN/s.ForwardThrustN,Strafe:p.StrafeThrustN/s.StrafeThrustN,Brake:p.BrakeThrustN/s.BrakeThrustN),
            ComponentSlot.Sensor=>new(Power:p.PowerMw/s.PowerMw,Strength:p.Strength/s.Strength,Signature:p.Signature/s.Signature),
            _=>new(MassFraction:p.AddedMassKg/p.ReferenceMassKg,Power:p.PowerMw/s.PowerMw,
                Capacity:p.Capacity/s.Capacity,Recharge:p.RechargePerSecond/s.RechargePerSecond,Delay:p.RechargeDelay/s.RechargeDelay)
        };
    }
    private static IReadOnlyList<SystemPart> Load()
    {
        using var stream=typeof(SystemParts).Assembly.GetManifestResourceStream("SpaceFleet.data.components.system_parts.json")
            ??throw new InvalidDataException("Missing system part catalog");
        using var reader=new StreamReader(stream);
        var rows=ShipDefinition.ParseData<SystemPart[]>(reader.ReadToEnd());
        static bool Pos(float v)=>float.IsFinite(v)&&v>0;
        if(rows.Length==0||rows.Any(p=>p is null)||rows.Select(p=>p.Id).Distinct().Count()!=rows.Length
            ||Slots.Any(slot=>rows.Count(p=>p.Slot==slot&&p.Stock)!=1))throw new InvalidDataException("Invalid system part catalog");
        foreach(var p in rows)
        {
            if(string.IsNullOrWhiteSpace(p.Id)||!p.Id.All(c=>char.IsAsciiLetterOrDigit(c)||c=='-')||string.IsNullOrWhiteSpace(p.Name)
                ||!Slots.Contains(p.Slot)||p.Cost<0||(p.Cost==0)!=p.Stock||!Pos(p.PowerMw)||!Pos(p.ReferenceMassKg)
                ||!float.IsFinite(p.AddedMassKg)||p.AddedMassKg<0||p.AddedMassKg>p.ReferenceMassKg*.1f
                ||p.LegacyId is { } old&&ShipComponents.ById(old).Slot!=p.Slot
                ||(p.Slot switch
                {ComponentSlot.Engine=>!Pos(p.ForwardThrustN)||!Pos(p.StrafeThrustN)||!Pos(p.BrakeThrustN),
                 ComponentSlot.Sensor=>!Pos(p.Strength)||!Pos(p.Signature)||p.LockChannels is <0 or >1,
                 _=>!Pos(p.Capacity)||!Pos(p.RechargePerSecond)||!Pos(p.RechargeDelay)}))
                throw new InvalidDataException("Invalid system part: "+p.Id);
        }
        return Array.AsReadOnly(rows);
    }
}
