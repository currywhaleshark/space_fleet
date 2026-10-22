using System;
using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

public enum TacticalStatus { Ready, Preparing, Active, Cooldown, Unavailable, Destroyed }
public sealed class TacticalSlot
{
    internal TacticalSlot(InstalledTactical installed,ModuleState module){Installed=installed;Module=module;}
    public InstalledTactical Installed { get; }
    public TacticalDefinition Definition=>Installed.Device;
    public ModuleState Module { get; }
    public double CooldownUntil { get; internal set; }
    public double ActiveUntil { get; internal set; }
    public Vec3d? JumpDestination { get; internal set; }
    internal Vec3d? JumpOffset;
    public string LastResult { get; internal set; }="";
    public int Uses { get; internal set; }
}

/// <summary>Battle lifetime belongs to the ship. Damage repair never reconstructs timers or grants charges.</summary>
public sealed class TacticalState
{
    private readonly ShipBody _ship;
    public TacticalSlot[] Slots { get; }
    public TacticalSlot[] ActiveSlots { get; }
    internal TacticalState(ShipBody ship)
    {
        _ship=ship;
        Slots=(ship.Definition.TacticalModules??Array.Empty<InstalledTactical>()).Select(i=>new TacticalSlot(i,ship.Damage.Module(i.DamageModule.Id))).ToArray();
        ActiveSlots=Slots.Where(s=>s.Definition.Active).ToArray();
    }
    public bool WeaponsBlocked { get; private set; }
    public float OutputBonus { get; private set; }
    public float SignatureBonus { get; private set; }
    public float ExtraPower { get; private set; }
    public float ExtraHeat { get; private set; }
    public float RepairRate { get; private set; }=1;
    public float MissileReloadRate { get; private set; }=1;
    public bool AirDefense { get; private set; }
    public bool FocusSearch { get; private set; }
    public bool HasStores=>Array.Exists(Slots,s=>s.Definition.Effect==TacticalEffect.RepairStores);
    public bool StoresDestroyed=>Array.Exists(Slots,s=>s.Definition.Effect==TacticalEffect.RepairStores&&s.Module.Destroyed);
    private bool Available(TacticalSlot slot)=>!_ship.Damage.Destroyed&&!_ship.Damage.Disabled&&(!slot.Definition.Active||slot.Definition.Effect==TacticalEffect.HeatVent||!_ship.Power.Overheated)
        &&_ship.Damage.GenerationFraction>.01f&&_ship.Power.Supply>.1f
        &&slot.Module.HealthFraction>.01f&&_ship.Damage.GridPower(slot.Module.Definition.Grid)>.01f;
    public TacticalStatus Status(TacticalSlot slot)
    {
        if(slot.Module.Destroyed)return TacticalStatus.Destroyed;
        if(!Available(slot))return TacticalStatus.Unavailable;
        if(slot.ActiveUntil>_ship.SimTime)return slot.Definition.Effect==TacticalEffect.ShortJump?TacticalStatus.Preparing:TacticalStatus.Active;
        return slot.CooldownUntil>_ship.SimTime?TacticalStatus.Cooldown:TacticalStatus.Ready;
    }
    public FireAttempt Activate(int index,SimWorld world,Vec3d? destination=null)
    {
        if(index<0||index>=ActiveSlots.Length)return new(false,"전술 모듈 미장착");
        var slot=ActiveSlots[index];var d=slot.Definition;
        if(Status(slot)!=TacticalStatus.Ready)return new(false,d.Name+" · "+Label(Status(slot)));
        if(d.Effect==TacticalEffect.FocusSearch&&_ship.Damage.SensorFraction<=.01f)return new(false,"센서 손상");
        if(d.Effect==TacticalEffect.HeatVent&&(_ship.Power.HeatMj<=0||_ship.Damage.CoolingFraction<=.01f))return new(false,"배출할 열 없음 / 방열계 손상");
        if(d.Effect==TacticalEffect.ShortJump)
        {
            var wanted=destination??_ship.Position+Vec3d.From(_ship.Forward)*10000;
            if((wanted-_ship.Position).LengthSquared()>10000.01*10000.01||!world.JumpClear(_ship,wanted))return new(false,"도약 불가 · 거리 / 도착 공간");
            slot.JumpDestination=wanted;slot.JumpOffset=destination is null?wanted-_ship.Position:null;SimWorld.StopLasers(_ship);
        }
        else
        {
            slot.CooldownUntil=_ship.SimTime+d.Cooldown;slot.Uses++;
            if(d.Effect==TacticalEffect.HeatVent)_ship.Power.VentHeat(_ship.Definition.Power.HeatCapacityMj*.3f);
        }
        slot.ActiveUntil=_ship.SimTime+d.Duration;slot.LastResult=d.Name+" · "+(d.Effect==TacticalEffect.ShortJump?"도약 준비":"가동");
        _ship.Damage.Report(_ship.SimTime,slot.LastResult);Refresh();return new(true,slot.LastResult);
    }
    internal void Refresh()
    {
        OutputBonus=SignatureBonus=ExtraPower=ExtraHeat=0;RepairRate=MissileReloadRate=1;AirDefense=FocusSearch=WeaponsBlocked=false;
        foreach(var slot in Slots)
        {
            if(!Available(slot))
            {
                if(slot.JumpDestination is not null){slot.JumpDestination=null;slot.LastResult="도약 취소 · 계통 사용 불가";_ship.Damage.Report(_ship.SimTime,slot.LastResult);}
                slot.ActiveUntil=0;continue;
            }
            float strength=slot.Module.HealthFraction;
            bool active=slot.ActiveUntil>_ship.SimTime||slot.JumpDestination is not null;
            ExtraPower+=slot.Installed.PowerMw*(slot.Definition.Active&&!active?.15f:1);
            ExtraHeat+=slot.Installed.HeatMw*(slot.Definition.Active&&!active?.15f:1);
            switch(slot.Definition.Effect)
            {
                case TacticalEffect.RepairController:RepairRate=1+strength/3;break;
                case TacticalEffect.QuickLoader:MissileReloadRate=1+strength*(1/.85f-1);break;
                case TacticalEffect.AirDefense:AirDefense=_ship.Damage.SensorFraction>.01f;break;
                case TacticalEffect.GeneratorOverload when active:OutputBonus=.25f*strength;ExtraHeat+=_ship.Definition.Power.OutputMw*.25f*strength;break;
                case TacticalEffect.HeatVent when active:SignatureBonus+=.6f;break;
                case TacticalEffect.FocusSearch when active:FocusSearch=_ship.Damage.SensorFraction>.01f;SignatureBonus+=.2f;break;
                case TacticalEffect.ShortJump when active:WeaponsBlocked=true;SignatureBonus+=1;break;
            }
        }
    }
    internal void CompleteJumps(SimWorld world)
    {
        Refresh();
        foreach(var slot in ActiveSlots)
        {
            if(slot.JumpDestination is not { } end||_ship.SimTime+1e-8<slot.ActiveUntil)continue;
            if(slot.JumpOffset is { } offset)end=_ship.Position+offset;
            bool success=Available(slot)&&(end-_ship.Position).LengthSquared()<=10000.01*10000.01&&world.JumpClear(_ship,end);
            if(success){world.RelocateByJump(_ship,end);slot.CooldownUntil=_ship.SimTime+slot.Definition.Cooldown;slot.Uses++;_ship.Power.AddHeat(_ship.Definition.Power.HeatCapacityMj*.12f);}
            slot.JumpDestination=null;slot.ActiveUntil=0;slot.LastResult=success?"도약 완료":"도약 취소 · 도착 공간 / 거리";
            _ship.Damage.Report(_ship.SimTime,slot.LastResult);
        }
        Refresh();
    }
    public float ObservationFactor(Vector3 direction)=>!FocusSearch?1:direction.Normalized().Dot(_ship.Forward)>=Mathf.Cos(Mathf.DegToRad(35))?1.8f:.65f;
    public static string Label(TacticalStatus status)=>status switch{TacticalStatus.Ready=>"준비",TacticalStatus.Preparing=>"준비 중",TacticalStatus.Active=>"가동",TacticalStatus.Cooldown=>"재사용 대기",TacticalStatus.Destroyed=>"파괴",_=>"사용 불가"};
}

public sealed partial class SimWorld
{
    public bool JumpClear(ShipBody ship,Vec3d destination)
    {
        if(!ObstacleMath.Finite(destination))return false;
        double radius=ship.Hull.BoundingRadius+10;
        if(Obstacles.BlocksSegment(destination,destination,radius))return false;
        foreach(var other in _ships)
            if(other!=ship&&(destination-other.Position).LengthSquared()<Math.Pow(radius+other.Hull.BoundingRadius,2))return false;
        return true;
    }
    internal void RelocateByJump(ShipBody ship,Vec3d destination)
    {
        ship.Teleport(destination-ship.Position);
        if(_brains.TryGetValue(ship,out var brain)){CancelRoute(brain);brain.NextThink=Time;brain.NavigationGoal=null;brain.NavigationWaypoint=null;brain.NavigationActive=false;}
        // Re-observe from real positions; no synthetic lock or missile target teleport is introduced.
        Sensors.Update(_ships,Time,force:true,probes:_probes,missiles:_missiles);
    }
    private void TacticalAI(ShipBrain brain)
    {
        var ship=brain.Ship;
        for(int i=0;i<ship.Tactical.ActiveSlots.Length;i++)
        {
            var slot=ship.Tactical.ActiveSlots[i];if(ship.Tactical.Status(slot)!=TacticalStatus.Ready)continue;
            bool threat=IncomingMissileThreat(ship,15000);
            bool engage=brain.Order.Target is { } target&&target.Faction!=ship.Faction&&Sensors.Track(ship.Faction,target).Level>=TrackLevel.Contact;
            bool use=slot.Definition.Effect switch{
                TacticalEffect.HeatVent=>ship.Power.HeatFraction>.65f,
                TacticalEffect.GeneratorOverload=>ship.Power.Supply<.9f&&ship.Power.HeatFraction<.5f,
                TacticalEffect.FocusSearch=>engage&&ship.Power.HeatFraction<.7f,
                TacticalEffect.ShortJump=>threat&&ship.Damage.StructureFraction<.35f,
                _=>false};
            Vec3d? destination=null;
            if(use&&slot.Definition.Effect==TacticalEffect.ShortJump)
            {
                var known=Sensors.SmallContacts(ship.Faction).Where(c=>c.Kind==SmallContactKind.Missile&&c.Fresh(Time))
                    .OrderBy(c=>(c.Predict(Time)-ship.Position).LengthSquared()).FirstOrDefault();
                if(known.Target is null)continue;
                var away=(ship.Position-known.Predict(Time)).ToVector3().Normalized();
                destination=ship.Position+Vec3d.From(away)*9500;
            }
            if(use)ship.Tactical.Activate(i,this,destination);
        }
    }
}
