using System;
using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

public enum FireDoctrine { Free, Focus, Disable, Hold, Manual }
public enum GunneryStatus { NoTarget, Firing, Reload, WaitLock, Range, Arc, HullBlocked, FriendlyLine, Armor, Hold, Manual }

public sealed class GunneryOrder
{
    public FireDoctrine Doctrine { get; set; } = FireDoctrine.Free;
    public ShipBody? Target { get; set; }
    public string? PriorityModuleId { get; set; }
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
        GunneryStatus.Range => "사거리 밖", GunneryStatus.Arc => "포각 밖",
        GunneryStatus.HullBlocked => "선체 가림", GunneryStatus.FriendlyLine => "아군 사선",
        GunneryStatus.Armor => "장갑 관통 불가", GunneryStatus.Hold => "사격 정지", GunneryStatus.Manual => "수동 사격",
        _ => "표적 없음",
    };
}

public sealed partial class SimWorld
{
    public bool TryAutoFire(ShipBody ship, ShipBody target, Vector3? localAim, double maxFlightSeconds, out GunneryStatus status) =>
        TryAutoFire(ship, target, localAim, maxFlightSeconds, out status, out _);

    private bool TryAutoFire(ShipBody ship, ShipBody target, Vector3? localAim, double maxFlightSeconds,
        out GunneryStatus status, out FiringSolution? solved)
    {
        solved = null;
        status = GunneryStatus.NoTarget;
        if (ship.Damage.Destroyed || target.Damage.Destroyed || target.Faction == ship.Faction) return false;
        if (ship.Railgun is not RailgunState gun || !gun.Ready) { status = GunneryStatus.Reload; return false; }
        SensorTrack track = Sensors.Track(ship.Faction, target);
        if (track.Level < TrackLevel.Locked) { status = GunneryStatus.WaitLock; return false; }
        FiringSolution solution = FireControl.Solve(ship, target, Time, track: track, localAim: localAim);
        solved = solution;
        // Keep the existing AI's solution/armor evaluation and firing conditions.
        bool worthIt = target.Damage.Shield > 1f
            || DamageRay.PreviewArmor(target, gun.MuzzlePosition, solution.Direction, gun.Definition.PenetrationMm, out _);
        if (!solution.Valid || solution.FlightTime > maxFlightSeconds) { status = GunneryStatus.Range; return false; }
        if (!worthIt) { status = GunneryStatus.Armor; return false; }
        if (FriendlyInLine(ship, gun.MuzzlePosition, solution.Direction, (float)solution.Range))
        { status = GunneryStatus.FriendlyLine; return false; }
        FireAttempt attempt = FireRailgun(ship, solution.Direction);
        status = attempt.Fired ? GunneryStatus.Firing : attempt.Failure switch
        {
            FireFailure.HullBlocked => GunneryStatus.HullBlocked,
            FireFailure.Arc => GunneryStatus.Arc,
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
            * 1_000_000.0 - s.Range)
        .ThenBy(s => s.Ship.Callsign, StringComparer.Ordinal).Select(s => s.Ship).FirstOrDefault();

    private static readonly AimSubsystem[] DisableParts =
        { AimSubsystem.Engines, AimSubsystem.Guns, AimSubsystem.Sensors, AimSubsystem.Radiators };

    private ModuleState? PickGunneryModule(ShipBody ship, ShipBody target, GunneryOrder order)
    {
        ModuleState? priority = target.Damage.Modules.FirstOrDefault(m => m.Definition.Id == order.PriorityModuleId && !m.Destroyed);
        if (priority is not null) return priority;
        ModuleState? selected = Subsystems.Pick(target, order.AimPart, ship.Position);
        if (selected is not null || order.Doctrine != FireDoctrine.Disable) return selected;
        if (ship.Railgun is not RailgunState gun) return null;
        // 무력화 자동 선택: 겉으로 드러난 기동·무장·센서·방열판 중 지금 뚫리는 가장 가까운 것. 내부 냉각기는 노리지 않는다.
        return target.Damage.Modules.Where(m => !m.Destroyed && DisableParts.Any(part => Subsystems.Matches(m.Definition, part)))
            .OrderBy(m => (Subsystems.WorldPosition(target, m.Definition) - ship.Position).LengthSquared())
            .FirstOrDefault(m => DamageRay.PreviewArmor(target, gun.MuzzlePosition,
                (Subsystems.WorldPosition(target, m.Definition) - gun.MuzzlePosition).ToVector3(), gun.Definition.PenetrationMm, out _))
            ?? Subsystems.Pick(target, AimSubsystem.Engines, ship.Position);
    }

    private void StepGunnery()
    {
        foreach (ShipBody ship in _ships)
        {
            if (ship.Gunnery is not GunneryOrder order || BrainOf(ship) is { Enabled: true }) continue;
            order.Engaged = null; order.EngagedModule = null; order.Solution = null;
            if (order.Doctrine is FireDoctrine.Hold or FireDoctrine.Manual)
            { order.Status = order.Doctrine == FireDoctrine.Hold ? GunneryStatus.Hold : GunneryStatus.Manual; continue; }
            ShipBody? target = order.Target is { Damage.Destroyed: false } chosen && chosen.Faction != ship.Faction
                && (order.Doctrine is FireDoctrine.Focus or FireDoctrine.Disable || Sensors.Track(ship.Faction, chosen).Level >= TrackLevel.Locked)
                ? chosen : order.Doctrine == FireDoctrine.Focus ? null : PickGunneryTarget(ship);
            if (target is null) { order.Status = GunneryStatus.NoTarget; continue; }
            order.Engaged = target;
            order.EngagedModule = PickGunneryModule(ship, target, order);
            TryAutoFire(ship, target, order.EngagedModule?.Definition.Center, AiProfile.For(ship.Class.Kind).RailFlightSeconds * 1.5,
                out var status, out var solution);
            order.Status = status; order.Solution = solution;
            if (solution is null && Sensors.Track(ship.Faction, target).Level >= TrackLevel.Locked)
                order.Solution = FireControl.Solve(ship, target, Time, track: Sensors.Track(ship.Faction, target), localAim: order.EngagedModule?.Definition.Center);
        }
    }
}
