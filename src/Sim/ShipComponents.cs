using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;

namespace SpaceFleet.Sim;

public enum ComponentSlot { MainGun, Secondary, Engine, Sensor, Generator, Shield }
public sealed record ShipComponentSelection(string? MainGun = null, string? Secondary = null, string? Engine = null,
    string? Sensor = null, string? Generator = null, string? Shield = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public EquipmentMount[]? AuxiliaryParts { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public TacticalMount[]? TacticalModules { get; init; }
    public string? Get(ComponentSlot slot) => slot switch { ComponentSlot.MainGun=>MainGun, ComponentSlot.Secondary=>Secondary,
        ComponentSlot.Engine=>Engine, ComponentSlot.Sensor=>Sensor, ComponentSlot.Generator=>Generator, ComponentSlot.Shield=>Shield,
        _=>throw new InvalidDataException("없는 교체 계통입니다.") };
    public ShipComponentSelection With(ComponentSlot slot,string? id) => slot switch {
        ComponentSlot.MainGun=>this with{MainGun=id},ComponentSlot.Secondary=>this with{Secondary=id},
        ComponentSlot.Engine=>this with{Engine=id,EnginePart=null},ComponentSlot.Sensor=>this with{Sensor=id,SensorPart=null},
        ComponentSlot.Generator=>this with{Generator=id},ComponentSlot.Shield=>this with{Shield=id,ShieldPart=null},
        _=>throw new InvalidDataException("없는 교체 계통입니다.") };
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> Ids => Enum.GetValues<ComponentSlot>().Select(Get).OfType<string>();
    /// <summary>Absolute main gun part per socket (null entry = stock); see MainGunParts. Not a catalog component.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string?[]? MainGuns { get; init; }
    /// <summary>Missile launcher, missile and point-defence parts (§21); null = stock.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Launcher { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Missile { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? PointDefensePart { get; init; }
    /// <summary>Internal generators and coolers in main gun weapon volume (§24); null = none.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? PowerParts { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? EnginePart { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? SensorPart { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ShieldPart { get; init; }
    public string? SystemPart(ComponentSlot slot)=>slot switch
    {ComponentSlot.Engine=>EnginePart,ComponentSlot.Sensor=>SensorPart,ComponentSlot.Shield=>ShieldPart,_=>null};
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> PartIds => (MainGuns ?? Array.Empty<string?>()).Append(Launcher).Append(Missile).Append(PointDefensePart)
        .Concat(PowerParts ?? Array.Empty<string>()).Append(EnginePart).Append(SensorPart).Append(ShieldPart).OfType<string>();
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => !Ids.Any() && MainGuns is null && Launcher is null && Missile is null && PointDefensePart is null && PowerParts is null
        && EnginePart is null && SensorPart is null && ShieldPart is null && AuxiliaryParts is null && TacticalModules is null;
}

// Multipliers apply after permanent research, once, before physical bay equipment.
// MassFraction is an additional fraction of that researched hull's total mass.
public sealed record ComponentEffects(float MassFraction=0,float Power=1,float Output=1,float Cooling=1,float HeatCapacity=1,
    float Forward=1,float Strafe=1,float Brake=1,float Strength=1,float Signature=1,
    float Capacity=1,float Recharge=1,float Delay=1,float Rate=1,float Damage=1,float Range=1,float Tracking=1,
    int Barrels=0,float SalvoEnergy=1,float Reload=1,float Traverse=1,float Accuracy=1,float ShotHeat=1,
    float? MissileDamage=null);
public sealed record ShipComponentDefinition(string Id,ComponentSlot Slot,string Name,string Description,int Cost,
    ComponentEffects Effects,bool FixedOnly=false,bool TurretOnly=false);

/// <summary>Six researched replacement systems; stock is null and remains bit-identical.</summary>
public static class ShipComponents
{
    public static IReadOnlyList<ShipComponentDefinition> All { get; }=Load();
    public static ShipComponentDefinition ById(string id)=>All.FirstOrDefault(c=>c.Id==id)
        ??throw new InvalidDataException("알 수 없는 교체 부품입니다: "+id);
    public static string Name(ComponentSlot slot)=>slot switch { ComponentSlot.MainGun=>"주포",ComponentSlot.Secondary=>"부포 / PD",
        ComponentSlot.Engine=>"엔진",ComponentSlot.Sensor=>"센서",ComponentSlot.Generator=>"발전기",_=>"실드" };
    public static string UnlockKey(string id)=>"component:"+id;
    public static bool Available(ShipDefinition hull,ShipComponentDefinition c)
    {
        bool exists=c.Slot switch {
            ComponentSlot.MainGun=>hull.Railgun is not null,ComponentSlot.Secondary=>hull.PointDefense is not null,
            ComponentSlot.Engine=>hull.Modules.Any(m=>m.Kind==ModuleKind.Thruster),
            ComponentSlot.Sensor=>hull.Modules.Any(m=>m.Kind==ModuleKind.Sensor),
            ComponentSlot.Generator=>hull.Modules.Any(m=>m.Kind is ModuleKind.Generator or ModuleKind.Reactor),
            ComponentSlot.Shield=>hull.Modules.Any(m=>m.Kind==ModuleKind.ShieldEmitter),_=>false};
        return exists&&(!c.FixedOnly||hull.Railgun?.Mounts is null)&&(!c.TurretOnly||hull.Railgun?.Mounts is {Length:>0});
    }
    public static void ValidateSelection(ShipDefinition hull,ShipComponentSelection? selection)
    {
        if(selection is null)return;
        foreach(var slot in Enum.GetValues<ComponentSlot>())if(selection.Get(slot) is { } id)
        {var c=ById(id);if(c.Slot!=slot||!Available(hull,c))throw new InvalidDataException("이 함선의 "+Name(slot)+"에 맞지 않는 부품입니다.");}
        SystemParts.ValidateSelection(hull,selection);
    }
    private static IReadOnlyList<ShipComponentDefinition> Load()
    {
        using var stream=typeof(ShipComponents).Assembly.GetManifestResourceStream("SpaceFleet.data.components.catalog.json")
            ??throw new InvalidDataException("Missing replacement component catalog");
        using var reader=new StreamReader(stream);
        var rows=ShipDefinition.ParseData<ShipComponentDefinition[]>(reader.ReadToEnd());
        if(rows.Length==0||rows.Any(c=>c is null)||rows.Select(c=>c.Id).Distinct().Count()!=rows.Length)
            throw new InvalidDataException("Invalid replacement catalog");
        foreach(var c in rows)
        {
            if(string.IsNullOrWhiteSpace(c.Id)||!c.Id.All(x=>char.IsAsciiLetterOrDigit(x)||x=='-')||!Enum.IsDefined(c.Slot)
                ||string.IsNullOrWhiteSpace(c.Name)||string.IsNullOrWhiteSpace(c.Description)||c.Cost<=0||c.Effects is null
                ||c.FixedOnly&&c.TurretOnly||c.Slot!=ComponentSlot.MainGun&&(c.FixedOnly||c.TurretOnly))
                throw new InvalidDataException("Invalid component identity");
            var e=c.Effects;
            float[] factors={e.Power,e.Output,e.Cooling,e.HeatCapacity,e.Forward,e.Strafe,e.Brake,e.Strength,e.Signature,
                e.Capacity,e.Recharge,e.Delay,e.Rate,e.Damage,e.Range,e.Tracking,e.SalvoEnergy,e.Reload,e.Traverse,e.Accuracy,e.ShotHeat};
            if(factors.Any(v=>!float.IsFinite(v)||v<.5f||v>2)||!float.IsFinite(e.MassFraction)||e.MassFraction<0||e.MassFraction>.1f
                ||e.Barrels is <0 or >3||e.Barrels>0&&!c.TurretOnly||e.Traverse>1
                ||e.MissileDamage is float md&&(c.Slot!=ComponentSlot.Secondary||!float.IsFinite(md)||md<.5f||md>2))
                throw new InvalidDataException("Invalid component effect");
        }
        return Array.AsReadOnly(rows);
    }
    internal static ShipDefinition Apply(ShipDefinition hull,ShipComponentSelection? selection,bool useFactoryPrimary=true)
    {
        if(selection is not null)SystemParts.ValidateSelection(hull,selection);
        selection=SystemParts.Normalize(selection);
        if(selection is not null&&!selection.Ids.Any()&&selection.EnginePart is null&&selection.SensorPart is null&&selection.ShieldPart is null)selection=null;
        string? primary=selection?.MainGun??(useFactoryPrimary&&hull.Battery is not null?hull.DefaultMainGun:null);
        if(selection is null&&primary is null)return hull;
        ValidateSelection(hull,selection);
        if(hull.Components is not null)throw new InvalidDataException("교체 부품을 중복 합성할 수 없습니다.");
        var effective=(selection??new()).With(ComponentSlot.MainGun,primary);
        ComponentEffects E(ComponentSlot slot)=>effective.SystemPart(slot) is { } part?SystemParts.Effects(SystemParts.ById(part)):
            effective.Get(slot) is { } id?ById(id).Effects:new();
        var gun=E(ComponentSlot.MainGun);var pd=E(ComponentSlot.Secondary);var engine=E(ComponentSlot.Engine);
        var sensor=E(ComponentSlot.Sensor);var generator=E(ComponentSlot.Generator);var shield=E(ComponentSlot.Shield);
        float massScale=1+effective.Ids.Sum(id=>ById(id).Effects.MassFraction)
            +SystemParts.Slots.Sum(slot=>effective.SystemPart(slot) is { } id?SystemParts.Effects(SystemParts.ById(id)).MassFraction:0);
        var f=hull.Flight;var p=hull.Power;
        var json=JsonNode.Parse(hull.ToJson())!.AsObject();
        void Set<T>(string name,T value)=>json[name]=JsonNode.Parse(ShipDefinition.SerializeData(value));
        Set(nameof(ShipDefinition.Components),selection is null?null:selection with{MainGuns=null,Launcher=null,Missile=null,PointDefensePart=null,AuxiliaryParts=null,TacticalModules=null});
        Set(nameof(ShipDefinition.Flight),f with{MassKg=f.MassKg*massScale,ForwardAccel=f.ForwardAccel*engine.Forward/massScale,
            StrafeAccel=f.StrafeAccel*engine.Strafe/massScale,BrakeAccel=f.BrakeAccel*engine.Brake/massScale});
        Set(nameof(ShipDefinition.Power),p with{OutputMw=p.OutputMw*generator.Output,CoolingMw=p.CoolingMw*generator.Cooling,
            HeatCapacityMj=p.HeatCapacityMj*generator.HeatCapacity,Engines=p.Engines*engine.Power,Sensors=p.Sensors*sensor.Power,
            Shields=p.Shields*shield.Power,Weapons=p.Weapons*gun.Power*pd.Power});
        Set(nameof(ShipDefinition.Sensors),hull.Sensors with{Strength=hull.Sensors.Strength*sensor.Strength,Signature=hull.Sensors.Signature*sensor.Signature});
        Set(nameof(ShipDefinition.Shield),hull.Shield with{Capacity=hull.Shield.Capacity*shield.Capacity,
            RechargePerSecond=hull.Shield.RechargePerSecond*shield.Recharge,RechargeDelay=hull.Shield.RechargeDelay*shield.Delay});
        if(selection?.Secondary is not null&&hull.PointDefense is { } d)
            Set(nameof(ShipDefinition.PointDefense),d with{ShotsPerSecond=d.ShotsPerSecond*pd.Rate,DamagePerHit=d.DamagePerHit*pd.Damage,
                MissileDamagePerHit=pd.MissileDamage is float missileDamage?d.EffectiveMissileDamage*missileDamage:d.MissileDamagePerHit*pd.Damage,
                RangeMeters=d.RangeMeters*pd.Range,YawRate=d.YawRate*pd.Tracking,ElevationRate=d.ElevationRate*pd.Tracking,
                ShipPacket=d.EffectiveShipPacket with{Energy=d.EffectiveShipPacket.Energy*pd.Damage,
                    ModuleDamage=d.EffectiveShipPacket.ModuleDamage*pd.Damage,Range=d.RangeMeters*pd.Range}});
        if(primary is { } gunId)
        {
            var b=hull.Battery!;bool laser=gunId=="main-laser";int count=laser?1:gun.Barrels==0?b.Weapon.BarrelCount:gun.Barrels;
            float energy=gun.SalvoEnergy*b.Weapon.BarrelCount/count;var ballistics=b.Weapon.Railgun!;
            var w=b.Weapon with{Id="component-"+gunId,BarrelCount=count,TraverseSpeedScale=b.Weapon.TraverseSpeedScale*gun.Traverse,
                Railgun=ballistics with{Energy=ballistics.Energy*energy,PenetrationMm=ballistics.PenetrationMm*Mathf.Pow(energy,.6f),
                    ModuleDamage=ballistics.ModuleDamage*energy,ReloadSeconds=ballistics.ReloadSeconds*gun.Reload,
                    AimErrorScale=ballistics.AimErrorScale*gun.Accuracy,ShotHeatMj=ballistics.ShotHeatMj*gun.ShotHeat}};
            if(laser)
            {
                float barrels=b.Weapon.BarrelCount, cadence=ballistics.ReloadSeconds;
                float mounts=b.Hardpoints.Length;
                w=w with{System=PrimaryWeaponSystem.Laser,Railgun=null,BarrelCount=1,
                    Laser=new LaserDefinition(ballistics.Energy*barrels/cadence*.9f,ballistics.ModuleDamage*barrels/cadence*.9f,
                        ballistics.PenetrationMm*.32f,
                        (hull.Power.HeatCapacityMj/16+hull.Power.CoolingMw*.4f)/mounts,
                        hull.Kind==HullKind.Battleship?40000:hull.Kind==HullKind.Escort?20000:5000,
                        ballistics.SensorErrorMeters,ballistics.SensorErrorPerKm*3,ballistics.AimErrorScale)};
            }
            var installs=b.Installations.Select(m=>m with{Muzzles=MainBatteries.Spread(m,count),Rounds=laser?0:m.Rounds}).ToArray();
            json.Remove(nameof(ShipDefinition.Railgun));
            Set(nameof(ShipDefinition.MainBattery),b with{Weapon=w,Installations=installs});
        }
        return ShipDefinition.Parse(json.ToJsonString(),strict:true);
    }
}
