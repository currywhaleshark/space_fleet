using System;
using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

public enum FireDoctrine { Free, Focus, Disable, Hold, Manual }
public enum GunneryStatus { NoTarget, Firing, Reload, WaitLock, Range, Arc, HullBlocked, FriendlyLine, Armor, Hold, Manual, Traversing, Obstructed, Attenuated, AimQuality, DefenseReserve }

public sealed class GunneryOrder
{
    public FireDoctrine Doctrine { get; set; } = FireDoctrine.Free;
    // Player-selected ordnance is aimed by the input controller; suppress autonomous ship fire.
    public bool PlayerSmallTarget { get; set; }
    public ShipBody? Target { get; set; }
    public string? PriorityModuleId { get; set; }
    public string? PriorityExternalId { get; set; }
    public Vector3? EngagedLocalAim { get; internal set; }
    public AimSubsystem AimPart { get; set; }
    public ShipBody? Engaged { get; internal set; }
    public ModuleState? EngagedModule { get; internal set; }
    public GunneryStatus Status { get; internal set; }
    public FiringSolution? Solution { get; internal set; }
}

public static class GunneryLabels
{
    public static string Doctrine(FireDoctrine doctrine) => doctrine switch
    { FireDoctrine.Focus => "집중", FireDoctrine.Disable => "무력화", FireDoctrine.Hold => "정지", FireDoctrine.Manual => "수동", _ => "자율" };
    public static string Status(GunneryStatus status) => status switch
    {
        GunneryStatus.Firing => "발사", GunneryStatus.Reload => "재장전", GunneryStatus.WaitLock => "잠금 대기",
        GunneryStatus.Traversing => "포탑 선회 중",
        GunneryStatus.Range => "사거리 밖", GunneryStatus.Arc => "포각 밖",
        GunneryStatus.Attenuated => "거리 감쇠 · 자동사격 대기", GunneryStatus.AimQuality => "조준 품질 부족",
        GunneryStatus.DefenseReserve => "방공 대비 · 대함 조사 대기",
        GunneryStatus.Obstructed => "지형 가림",
        GunneryStatus.HullBlocked => "선체 가림", GunneryStatus.FriendlyLine => "아군 사선",
        GunneryStatus.Armor => "장갑 관통 불가", GunneryStatus.Hold => "사격 정지", GunneryStatus.Manual => "수동 사격",
        _ => "표적 없음",
    };
}

public sealed partial class SimWorld
{
    public static double GunneryFlightLimit(ShipBody ship) => AiProfile.For(ship.Definition).RailFlightSeconds * 1.5;

    public bool TryAutoFire(ShipBody ship, ShipBody target, Vector3? localAim, double maxFlightSeconds, out GunneryStatus status) =>
        TryAutoFire(ship, target, localAim, maxFlightSeconds, out status, out _);

    private bool TryAutoFire(ShipBody ship, ShipBody target, Vector3? localAim, double maxFlightSeconds,
        out GunneryStatus status, out FiringSolution? solved)
    {
        solved = null; status = GunneryStatus.NoTarget;
        bool fired = false;
        int priority = -1;
        foreach (RailgunState gun in ship.Railguns)
        {
            bool shot = TryAutoFireGun(ship, gun, target, localAim, maxFlightSeconds, out var state, out var solution);
            fired |= shot;
            int rank = state switch { GunneryStatus.Firing => 10, GunneryStatus.Traversing => 9,
                GunneryStatus.Reload => 8, GunneryStatus.FriendlyLine => 7, GunneryStatus.HullBlocked or GunneryStatus.Obstructed => 6,
                GunneryStatus.Arc => 5, _ => 4 };
            if (gun.Output <= .01f || !gun.HasAmmunition) rank = 0;
            if (rank > priority) { priority = rank; status = state; solved = solution; }
        }
        return fired;
    }

    private bool TryAutoFireGun(ShipBody ship, RailgunState gun, ShipBody target, Vector3? localAim, double maxFlightSeconds,
        out GunneryStatus status, out FiringSolution? solved)
    {
        solved = null;
        status = GunneryStatus.NoTarget;
        if (ship.Damage.Destroyed || target.Damage.Destroyed || target.Faction == ship.Faction) return false;
        if(gun.DefenseTick==Tick){status=gun.DefenseStatus;return false;}
        if(AutoLaserCooling(ship,gun)||AutoOverchargeHold(ship,gun))
        {status=GunneryStatus.Reload;return false;}
        if (gun.Output <= .01f || !gun.HasAmmunition) { status = GunneryStatus.Reload; return false; }
        SensorTrack track = Sensors.Track(ship.Faction, target);
        if (track.Level < TrackLevel.Locked) { status = GunneryStatus.WaitLock; return false; }
        FiringSolution solution = FireControl.Solve(ship, target, Time, track: track, localAim: localAim, weapon: gun);
        solved = solution;
        if (solution.Valid) gun.Aim(solution.Direction); // track while reloading, too
        if (!solution.Valid || solution.FlightTime > maxFlightSeconds) { status = GunneryStatus.Range; return false; }
        if(gun.Laser is { } laser)
        {
            if(laser.AutoShipTarget!=target){laser.AutoShipTarget=target;laser.AutoShipEngaged=false;}
            laser.AutoTransmission=laser.Definition.Attenuation((float)solution.Range);
            bool effective=laser.AutoTransmission>=(laser.AutoShipEngaged?.20f:.25f);
            laser.AutoShipEngaged=effective;
            if(!effective){laser.Stop();status=GunneryStatus.Attenuated;return false;}
            // Only measured aim error and identified hull size; no hidden armor/health veto.
            if(solution.ErrorMeters>Math.Max(5,target.Class.Length*.25f))
            {laser.Stop();status=GunneryStatus.AimQuality;return false;}
            if(ship.Definition.Branch==ShipBranch.AirDefenseCruiser)
            {
                if(ship.Power.HeatFraction>=.55f)laser.DefenseReserve=true;
                else if(ship.Power.HeatFraction<=.35f)laser.DefenseReserve=false;
                if(laser.DefenseReserve){laser.Stop();status=GunneryStatus.DefenseReserve;return false;}
            }
        }
        if(Obstacles.Count>0)
            for(int barrel=0;barrel<gun.SalvoRounds;barrel++)
                if(Obstacles.BlocksSegment(gun.BarrelPosition(barrel),solution.AimPoint))
                { status=GunneryStatus.Obstructed; return false; }
        FireFailure line = RailLineLocal(ship, gun, ship.Orientation.Inverse() * solution.Direction);
        if (line != FireFailure.None) { status = line == FireFailure.Arc ? GunneryStatus.Arc : GunneryStatus.HullBlocked; return false; }
        if (!gun.Aligned(solution.Direction)) { status = GunneryStatus.Traversing; return false; }
        if (!gun.Ready) { status = GunneryStatus.Reload; return false; }
        // Armor is resolved at impact, never used to veto an otherwise valid shot.
        Vector3 bore = gun.Mount is null ? solution.Direction : gun.Direction;
        for (int barrel = 0; barrel < gun.SalvoRounds; barrel++)
            if (FriendlyInLine(ship, gun.BarrelPosition(barrel), bore, (float)solution.Range, includeShields:gun.IsLaser))
            { gun.Laser?.Stop(); status = GunneryStatus.FriendlyLine; return false; }
        FireAttempt attempt = FireRailgun(ship, gun, solution.Direction, Merits.Capture(ship,target,true), automatic:true);
        status = attempt.Fired ? GunneryStatus.Firing : attempt.Failure switch
        {
            FireFailure.HullBlocked => GunneryStatus.HullBlocked,
            FireFailure.Arc => GunneryStatus.Arc,
            FireFailure.Traversing => GunneryStatus.Traversing,
            _ => GunneryStatus.Reload,
        };
        return attempt.Fired;
    }

    /// <summary>자율 사격 표적. 거리는 센서 추정 위치로 잰다(진짜 위치는 사수가 모른다).</summary>
    public ShipBody? PickGunneryTarget(ShipBody ship) => _ships
        .Where(s => s.Faction != ship.Faction && !s.Damage.Destroyed)
        .Select(s => (Ship: s, Track: Sensors.Track(ship.Faction, s)))
        .Where(s => s.Track.Level >= TrackLevel.Locked)
        .Select(s => (s.Ship, Range: (s.Track.EstimatedPosition - ship.Position).Length()))
        .OrderByDescending(s => (s.Ship.Class.Kind == HullKind.Interceptor && s.Range < 20_000 ? 3
            : s.Ship.Class.Kind == HullKind.Escort ? 2 : s.Ship.Class.Kind == HullKind.Battleship ? 1 : 0)
            * 1_000_000.0 - s.Range + (ship.Class.Kind == HullKind.Escort && s.Range < 30_000
                && s.Ship.Ordnance.Antimatter.Rounds > 0 ? 5_000_000 : 0))
        .ThenBy(s => s.Ship.Callsign, StringComparer.Ordinal).Select(s => s.Ship).FirstOrDefault();

    private static readonly AimSubsystem[] DisableParts =
        { AimSubsystem.Engines, AimSubsystem.Guns, AimSubsystem.Sensors, AimSubsystem.Radiators };

    private ModuleState? PickGunneryModule(ShipBody ship, ShipBody target, GunneryOrder order)
    {
        var track=Sensors.Track(ship.Faction,target);
        ModuleState? priority = order.PriorityModuleId is {} id?ExternalTargets.Pick(ship,target,track,Time,AimSubsystem.Center,id,Obstacles):null;
        if (priority is not null) return priority;
        ModuleState? selected = ExternalTargets.Pick(ship,target,track,Time,order.AimPart,obstacles:Obstacles);
        if (selected is not null || order.Doctrine != FireDoctrine.Disable) return selected;
        // 무력화 자동 선택: 가까운 기동·무장·센서·방열판. 관통 여부는 착탄 때 판정한다.
        foreach(var part in DisableParts)
            if(ExternalTargets.Pick(ship,target,track,Time,part,obstacles:Obstacles) is {} module)return module;
        return null;
    }

    private void StepGunnery()
    {
        foreach (ShipBody ship in _ships)
        {
            if (ship.Gunnery is not GunneryOrder order || BrainOf(ship) is { Enabled: true }) continue;
            order.Engaged = null; order.EngagedModule = null; order.EngagedLocalAim=null; order.Solution = null;
            if (order.PlayerSmallTarget) { order.Status = GunneryStatus.Manual; continue; }
            if (order.Doctrine is FireDoctrine.Hold or FireDoctrine.Manual)
            { order.Status = order.Doctrine == FireDoctrine.Hold ? GunneryStatus.Hold : GunneryStatus.Manual; continue; }
            GunneryStatus defenseStatus=GunneryStatus.NoTarget;
            bool defending=order.Doctrine==FireDoctrine.Free&&EngageDefenseBattery(ship,out defenseStatus);
            if (order.Target is { } remembered && Sensors.Memory(ship.Faction,remembered,Time) is not null
                && order.Doctrine is FireDoctrine.Focus or FireDoctrine.Disable)
            { order.Engaged=remembered; order.Status=GunneryStatus.WaitLock; continue; }
            ShipBody? target = order.Target is { Damage.Destroyed: false } chosen && chosen.Faction != ship.Faction
                && (order.Doctrine is FireDoctrine.Focus or FireDoctrine.Disable || Sensors.Track(ship.Faction, chosen).Level >= TrackLevel.Locked)
                ? chosen : order.Doctrine == FireDoctrine.Focus ? null : PickGunneryTarget(ship);
            if (target is null) { order.Status = defending?defenseStatus:GunneryStatus.NoTarget; continue; }
            order.Engaged = target;
            order.EngagedModule = order.PriorityExternalId is null?PickGunneryModule(ship, target, order):null;
            order.EngagedLocalAim=order.PriorityExternalId is {} externalId&&ExternalTargets.TryAim(ship,target,Sensors.Track(ship.Faction,target),Time,externalId,out var externalAim,obstacles:Obstacles)
                ?externalAim.Point:order.EngagedModule?.Definition.Center;
            TryAutoFire(ship, target, order.EngagedLocalAim, GunneryFlightLimit(ship),
                out var status, out var solution);
            order.Status = status; order.Solution = solution;
            if (solution is null && Sensors.Track(ship.Faction, target).Level >= TrackLevel.Locked)
                order.Solution = FireControl.Solve(ship, target, Time, track: Sensors.Track(ship.Faction, target), localAim: order.EngagedLocalAim);
        }
    }
}
