using System.Linq;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

/// <summary>Nominal fully powered comparison from resolved fits, including permanent upgrades and bay mass.</summary>
internal static class ComponentComparison
{
    private static string Change(float current,float next,string format="0.##")=>current.ToString(format)+" → "+next.ToString(format);
    private static float Shield(ShipDefinition d)=>d.Shield.Capacity*(1+d.InstalledEquipment.Sum(m=>m.Equipment.Effects.ShieldCapacityFraction));
    private static float Sensor(ShipDefinition d)=>d.Sensors.Strength*(1+d.InstalledEquipment.Sum(m=>m.Equipment.Effects.SensorStrengthFraction));
    private static float Delay(ShipDefinition d)=>d.Shield.RechargeDelay+d.InstalledEquipment.Sum(m=>m.Equipment.Effects.ShieldDelaySeconds);
    private static float Signature(ShipDefinition d)=>d.Sensors.Signature*(1+d.InstalledEquipment.Sum(m=>m.Equipment.Effects.SignatureFraction));
    internal static string Summary(ComponentSlot slot,ShipDefinition a,ShipDefinition b)=>slot switch
    {
        ComponentSlot.MainGun when a.Battery?.Weapon.Laser is not null || b.Battery?.Weapon.Laser is not null => LaserSummary(a,b),
        ComponentSlot.MainGun when a.Railgun is {} x&&b.Railgun is {} y =>
            $"발당 에너지 {Change(x.Energy,y.Energy)} · {a.Battery!.Weapon.BarrelCount} → {b.Battery!.Weapon.BarrelCount}연장\n"+
            $"관통 {Change(x.PenetrationMm,y.PenetrationMm,"0")} mm\n장전 {Change(x.ReloadSeconds,y.ReloadSeconds)}초",
        ComponentSlot.Secondary when a.PointDefense is {} x&&b.PointDefense is {} y =>
            $"연사 {Change(x.ShotsPerSecond,y.ShotsPerSecond)}발/초 · 사거리 {Change(x.RangeMeters/1000,y.RangeMeters/1000)} km\n"+
            $"탄두 피해 {Change(x.EffectiveMissileDamage,y.EffectiveMissileDamage)}\n드론 피해 {Change(x.DamagePerHit,y.DamagePerHit)}",
        ComponentSlot.Engine => $"전진 {Change(a.Flight.ForwardAccel,b.Flight.ForwardAccel)} m/s²\n횡이동 {Change(a.Flight.StrafeAccel,b.Flight.StrafeAccel)} m/s²\n제동 {Change(a.Flight.BrakeAccel,b.Flight.BrakeAccel)} m/s²",
        ComponentSlot.Sensor => $"센서 강도 {Change(Sensor(a),Sensor(b))}\n노출 신호 {Change(Signature(a),Signature(b))}\n센서 전력 {Change(a.Power.Sensors,b.Power.Sensors)} MW",
        ComponentSlot.Generator => $"발전 {Change(a.Power.OutputMw,b.Power.OutputMw)} MW\n냉각 {Change(a.Power.CoolingMw,b.Power.CoolingMw)} MW\n열용량 {Change(a.Power.HeatCapacityMj,b.Power.HeatCapacityMj)} MJ",
        ComponentSlot.Shield => $"실드 용량 {Change(Shield(a),Shield(b))}\n재충전 {Change(a.Shield.RechargePerSecond,b.Shield.RechargePerSecond)}/초\n재충전 지연 {Change(Delay(a),Delay(b))}초",
        _=>"이 계통을 탑재하지 않은 함선입니다."
    };
    private static string LaserSummary(ShipDefinition a,ShipDefinition b)
    {
        string Describe(ShipDefinition d)=>d.Battery?.Weapon.Laser is { } l
            ? $"레이저 · 포당 에너지 {l.EnergyPerSecond:0.#}/초\n기본 관통 {l.PenetrationMm:0} mm · {l.RampSeconds:0}초 유지 시 입구 장갑 -{l.MaxArmorReduction:P0}\n기본 폐열 {l.HeatMw:0.#} MW/포 · 반감 거리 {l.FalloffMeters/1000:0} km · 탄약 없음"
            : $"레일건 · 발당 에너지 {d.Railgun!.Energy:0.#} · {d.Battery!.Weapon.BarrelCount}연장\n관통 {d.Railgun.PenetrationMm:0} mm · 장전 {d.Railgun.ReloadSeconds:0.##}초";
        return Describe(a)+"\n↓\n"+Describe(b);
    }
    internal static string Costs(ShipDefinition a,ShipDefinition b)
    {
        float Demand(ShipDefinition d)=>d.Power.Engines+d.Power.Shields+d.Power.Weapons+d.Power.Sensors+d.InstalledEquipment.Sum(m=>m.Equipment.PowerMw);
        return $"총 전력 수요 {Change(Demand(a),Demand(b))} MW\n질량 {Change(a.Flight.MassKg/1000,b.Flight.MassKg/1000)} t";
    }
    internal static string Fitting(ComponentSlot slot,ShipDefinition a,ShipDefinition b)
    {
        string result=Summary(slot,a,b);
        if(slot==ComponentSlot.MainGun&&a.Battery?.Weapon.Laser is null&&b.Battery?.Weapon.Laser is null&&a.Railgun is {} x&&b.Railgun is {} y)
        {
            result+=$"\n{(x.Mounts is {Length:>0}?"포탑 일제":"발사")} 발열 {Change(x.ShotHeatMj*a.Battery!.Weapon.BarrelCount,y.ShotHeatMj*b.Battery!.Weapon.BarrelCount)} MJ";
            if(x.AimErrorScale!=y.AimErrorScale)result+=$"\n조준 오차 배율 {Change(x.AimErrorScale,y.AimErrorScale)}";
            if(x.Mounts is {Length:>0}&&y.Mounts is {Length:>0})
                result+=$"\n수평 / 수직 선회 (°/초) {Change(x.Mounts[0].YawRate,y.Mounts[0].YawRate)} / {Change(x.Mounts[0].ElevationRate,y.Mounts[0].ElevationRate)}";
        }
        if(slot==ComponentSlot.Secondary&&a.PointDefense is {} p&&b.PointDefense is {} q)
            result+=$"\n수평 / 수직 선회 (°/초) {Change(p.YawRate,q.YawRate)} / {Change(p.ElevationRate,q.ElevationRate)}";
        if(slot!=ComponentSlot.Engine&&a.Flight.ForwardAccel!=b.Flight.ForwardAccel)
            result+=$"\n전진 가속 {Change(a.Flight.ForwardAccel,b.Flight.ForwardAccel)} m/s²";
        return result+"\n"+Costs(a,b);
    }
    internal static string Equipment(ShipDefinition a,ShipDefinition b)
    {
        var rows=new System.Collections.Generic.List<string>();
        void Row(string name,float x,float y,string unit=""){if(x!=y)rows.Add($"{name} {Change(x,y)}{unit}");}
        Row("실드 용량",Shield(a),Shield(b));Row("재충전 지연",Delay(a),Delay(b),"초");
        Row("센서 강도",Sensor(a),Sensor(b));Row("노출 신호",Signature(a),Signature(b));
        float Jammer(ShipDefinition d)=>d.Sensors.Jammer+d.InstalledEquipment.Sum(m=>m.Equipment.Effects.Jammer);
        Row("ECM 강도",Jammer(a),Jammer(b));Row("드론",a.DefenseDrones?.Count??0,b.DefenseDrones?.Count??0,"기");
        Row("추가 폐열",a.InstalledEquipment.Sum(m=>m.Equipment.HeatMw),b.InstalledEquipment.Sum(m=>m.Equipment.HeatMw)," MW");
        rows.Add(Costs(a,b));return string.Join("\n",rows);
    }
    internal static string Upgrade(string id,ShipDefinition a,ShipDefinition b)=>id switch
    {
        "hull"=>$"구조 내구도 {Change(a.StructureHitPoints,b.StructureHitPoints)}",
        "fire-control"=>$"주포 장전 {Change(a.Railgun?.ReloadSeconds??0,b.Railgun?.ReloadSeconds??0)}초",
        "reactor"=>$"발전 {Change(a.Power.OutputMw,b.Power.OutputMw)} MW · 냉각 {Change(a.Power.CoolingMw,b.Power.CoolingMw)} MW",
        "drive"=>$"전진 / 횡이동 / 제동 (m/s²)\n{Change(a.Flight.ForwardAccel,b.Flight.ForwardAccel)} / {Change(a.Flight.StrafeAccel,b.Flight.StrafeAccel)} / {Change(a.Flight.BrakeAccel,b.Flight.BrakeAccel)}",
        "repair"=>$"응급복구 {Change(a.DamageControl.RepairSeconds,b.DamageControl.RepairSeconds)}초",
        _=>""
    };
}
