using System;
using Godot;

namespace SpaceFleet.Sim;

/// <summary>Game balance units. The payload is a short-lived, directed penetrator, not an area blast.</summary>
public sealed record AntimatterDefinition(string ModuleId, int Rounds, float ArmingSeconds,
    float RecommendedMinMeters, float RecommendedMaxMeters, float MaxTravelMeters,
    float TerminalAccelG, float LaunchConeDegrees, float DamageDepthMeters,
    float WarningHealth, float SafeFailureHealth, float ArmedFailureHealth, MissileDefinition Flight);

public enum AntimatterMode { Safe, Arming, Armed, Failed }
public enum AntimatterOutcome { Hit, PointDefense, Drone, Decoy, Expired }
public sealed record AntimatterFlight(Faction Faction, string Shooter, string Target, HullKind TargetKind,
    double LaunchTime, double Range, double FlightSeconds, double TravelMeters, double SeekerSeconds,
    float ClosestHull, AntimatterOutcome Outcome, string? HitShip);

public sealed class AntimatterState
{
    private readonly ShipBody _ship;
    public AntimatterState(ShipBody ship)
    {
        _ship = ship;
        Rounds = Definition?.Rounds ?? 0;
        ship.Damage.ModuleDamaged += OnModuleDamaged;
    }
    public AntimatterDefinition? Definition => _ship.Definition.Antimatter;
    public int Rounds { get; private set; }
    public AntimatterMode Mode { get; private set; }
    public float ArmingElapsed { get; private set; }
    public uint Launches { get; private set; }
    public uint Jettisons { get; private set; }
    public uint Failures { get; private set; }
    public double FailedAt { get; private set; } = double.NegativeInfinity;
    public bool Jettisoned { get; private set; }
    public float Containment => Definition is { } d ? _ship.Damage.Module(d.ModuleId).HealthFraction : 0;
    public bool Warning => Rounds > 0 && Definition is { } d && Containment < d.WarningHealth;
    public bool Powered => !_ship.Damage.Destroyed && _ship.Damage.GenerationFraction > .01f
        && Definition is { } d && _ship.Damage.GridPower(_ship.Damage.Module(d.ModuleId).Definition.Grid) > .01f
        && _ship.Power.WeaponEffect > .01f && !_ship.Power.Overheated;
    public bool Ready => Definition is not null && Rounds > 0 && Mode == AntimatterMode.Armed && Powered;
    public string Status => _ship.Damage.Destroyed ? "격침" : Mode == AntimatterMode.Failed ? "격리 실패"
        : Jettisoned ? "전량 투기" : Rounds == 0 ? "탄두 소진" : !Powered ? "격리 전력 대기"
        : Mode switch { AntimatterMode.Arming => $"ARMING {Definition!.ArmingSeconds-ArmingElapsed:0.0}s",
            AntimatterMode.Armed => "ARMED", _ => "SAFE" };
    public bool BeginArming()
    {
        if (Definition is not { } d || !Powered || Rounds <= 0 || Mode != AntimatterMode.Safe
            || Containment <= d.ArmedFailureHealth) return false;
        Mode = AntimatterMode.Arming; ArmingElapsed = 0; return true;
    }
    public bool Cancel()
    {
        if (Mode is not (AntimatterMode.Arming or AntimatterMode.Armed)) return false;
        Mode = AntimatterMode.Safe; ArmingElapsed = 0; return true;
    }
    public bool Jettison()
    {
        if (Rounds <= 0 || _ship.Damage.Destroyed || Mode == AntimatterMode.Failed) return false;
        Rounds = 0; Jettisoned = true; Jettisons++; Cancel();
        _ship.Damage.Report(_ship.SimTime, "반물질탄 전량 투기 · 격리 위험 해소");
        return true;
    }
    internal void Step(double dt)
    {
        if (_ship.Damage.Destroyed) { Rounds = 0; Cancel(); return; }
        if (!Powered) { Cancel(); return; } // local reserve safely aborts preparation; unrelated hits never roll for detonation
        if (Mode != AntimatterMode.Arming) return;
        ArmingElapsed = Math.Min(Definition!.ArmingSeconds, ArmingElapsed + (float)dt);
        if (ArmingElapsed >= Definition.ArmingSeconds - 1e-5f) Mode = AntimatterMode.Armed;
    }
    internal void Consume()
    {
        Rounds--; Launches++; Cancel();
        _ship.Power.AddHeat(Definition!.Flight.LaunchHeatMj);
    }
    private void OnModuleDamaged(ModuleState module, double time)
    {
        if (Definition is not { } d || module.Definition.Id != d.ModuleId || Rounds <= 0 || Mode == AntimatterMode.Failed) return;
        float threshold = Mode is AntimatterMode.Arming or AntimatterMode.Armed ? d.ArmedFailureHealth : d.SafeFailureHealth;
        if (module.HealthFraction > threshold)
        { _ship.Damage.Report(time, "반물질 격리 손상 · 비상 투기 가능"); return; }
        Rounds = 0; Mode = AntimatterMode.Failed; ArmingElapsed = 0; FailedAt = time; Failures++;
        _ship.Damage.Catastrophe(time, "반물질 격리 실패 · 유폭 · 함선 격침");
    }
}

public sealed partial class SimWorld
{
    public FireAttempt LaunchAntimatter(ShipBody shooter, ShipBody target, Vector3? localAim = null)
    {
        if (!_ships.Contains(shooter) || !_ships.Contains(target)) return new(false, "월드에 없는 함선");
        AntimatterState state = shooter.Ordnance.Antimatter;
        if (state.Definition is not { } am) return new(false, "반물질 발사관 없음");
        if (!state.Ready) return new(false, state.Status);
        if (target.Faction == shooter.Faction || target.Damage.Destroyed) return new(false, "유효 표적 없음");
        if (localAim is Vector3 invalid && !invalid.IsFinite()) return new(false, "조준점 오류");
        SensorTrack track = Sensors.Track(shooter.Faction, target);
        if (track.Level < TrackLevel.Contact) return new(false, "표적 미탐지");
        if (localAim is not null && track.Level < TrackLevel.Identified) return new(false, "취약부 식별 필요");
        MissileDefinition def = am.Flight;
        Vec3d start = shooter.Position + Vec3d.From(shooter.Orientation * def.LaunchPoint);
        Vec3d aim = track.EstimatedPosition + Vec3d.From(target.Orientation * (localAim ?? Vector3.Zero));
        double range = (aim - start).Length();
        // One initial lead solution. There is no fleet-network homing after separation.
        Vector3 direction = AntimatterLaunchDirection(shooter,target,track,localAim,out double t);
        aim = start + Vec3d.From(direction) * (def.EjectSpeed*t+.5*def.Accel*t*t);
        if (shooter.Forward.AngleTo(direction) > Mathf.DegToRad(am.LaunchConeDegrees)) return new(false, "AM 전방 발사각 밖");
        _shotSequence++;
        _missiles.Add(new Missile { Id = _shotSequence, Shooter = shooter, Target = target, Definition = def,
            Weapon = BattleWeapon.Antimatter, Assault = am, LocalAim = localAim, LaunchRange = range,
            Position = start, PrevPosition = start, Health = def.HitPoints, AimPoint = aim,
            Velocity = shooter.Velocity + direction * def.EjectSpeed, LaunchDirection = direction,
            NextSeekerCheck = Time + SeekerInterval });
        state.Consume(); Log?.Fire(shooter, BattleWeapon.Antimatter, Time);
        return new(true, "반물질 강습어뢰 발사", Shots: 1);
    }
}
