using System;
using System.Collections.Generic;
using Godot;

namespace SpaceFleet.Sim;

public sealed partial class SimWorld
{
    private readonly List<Missile> _counterMissileScratch = new();

    public bool CounterMissileSolution(ShipBody ship, Missile target, out Vector3 direction, out string reason, bool requireLaunchAlignment = true)
    {
        direction = Vector3.Zero;
        reason = "요격 미사일 미탑재";
        if (ship.Definition.Missiles is not { CanInterceptMissiles: true } def) return false;
        reason = "유효 미사일 표적 없음";
        if (!_ships.Contains(ship) || target.Faction == ship.Faction || !_missiles.Contains(target) || target.Health <= 0) return false;
        reason = "미사일 신호 소실";
        if (Sensors.SmallTrack(ship.Faction, target) is not { } track || !track.Fresh(Time)) return false;
        Vec3d start = ship.Position + Vec3d.From(ship.Orientation * def.LaunchPoint);
        reason="지형 가림";
        if(TerrainBlocked(start,track.Predict(Time))) return false;
        Vector3 r = (track.Predict(Time) - start).ToVector3(), v = track.Velocity - ship.Velocity;
        reason = "요격 미사일 사거리 밖";
        if (r.Length() > def.InterceptRangeMeters) return false;
        reason = "통과/이탈 중인 미사일";
        if (r.Dot(v) >= 0) return false;
        reason = "요격 방향으로 기수를 돌리세요";
        if (requireLaunchAlignment && ship.Forward.AngleTo(r) > Mathf.DegToRad(80)) return false;
        reason = "요격 경로 없음";
        double closest = -r.Dot(v) / Math.Max(1, v.LengthSquared());
        if (!AssaultGuidance.Intercept(r, v, def.EjectSpeed, def.Accel, Math.Min(8, closest), out direction, out _)) return false;
        // Account conservatively for the forward launcher and finite motor/body turn, not instant launch vector snapping.
        if (requireLaunchAlignment && ship.Forward.AngleTo(direction) > Mathf.DegToRad(85)) return false;
        reason = "좌클릭 · 요격 미사일 발사";
        return true;
    }

    public FireAttempt LaunchCounterMissile(ShipBody ship, Missile target)
    {
        if (!CounterMissileSolution(ship, target, out _, out var reason)) return new(false, reason);
        if (!ship.Ordnance.MissileReady) return new(false, ship.Ordnance.MissileStatus);
        var def = ship.Definition.Missiles!;
        var track = Sensors.SmallTrack(ship.Faction, target)!.Value;
        Vec3d start = ship.Position + Vec3d.From(ship.Orientation * def.LaunchPoint);
        _missiles.Add(new Missile {
            Id = ++_shotSequence, Shooter = ship, InterceptTarget = target, Definition = def,
            Position = start, PrevPosition = start, Health = def.HitPoints,
            Velocity = ship.Velocity + ship.Forward * def.EjectSpeed,
            NoseDirection = ship.Forward, PreviousNoseDirection = ship.Forward,
            AimPoint = track.Predict(Time), LastAimVelocity = track.Velocity, NextSeekerCheck = Time,
        });
        ship.Ordnance.ConsumeMissile();
        ship.Ordnance.Interceptor.FirstAction ??= Time;
        Log?.Fire(ship, BattleWeapon.Missile, Time);
        return new(true, "요격 미사일 발사");
    }

    private void CounterMissileAim(Missile m, double time, double dt, out Vec3d aim, out Vector3 velocity)
    {
        aim = m.AimPoint + Vec3d.From(m.LastAimVelocity) * dt;
        velocity = m.LastAimVelocity;
        bool observed = false;
        if (m.SeekerTarget is Decoy decoy)
        { aim = decoy.Position; velocity = decoy.Velocity; observed = true; }
        else if (m.SeekerTarget is Missile target && _missiles.Contains(target) && target.Health > 0)
        {
            // Onboard seeker, with actual range/FOV/signal gating every substep.
            Vector3 r = (target.Position - m.Position).ToVector3();
            float km = Mathf.Max(.001f, r.Length() / 1000);
            Vector3 forward = m.Velocity.LengthSquared() > 1 ? m.Velocity.Normalized() : m.NoseDirection;
            if (r.Length() <= m.Definition.SeekerRangeMeters && forward.AngleTo(r) <= Mathf.DegToRad(m.Definition.SeekerFovDegrees * .5f)
                && !TerrainBlocked(m.Position,target.Position)
                && m.Definition.SeekerStrength * (target.Burning ? 2 : .65f) / (km * km) >= 1)
            { aim = target.Position; velocity = target.Velocity; observed = true; }
        }
        if (!observed && m.InterceptTarget is { } intended && Sensors.SmallTrack(m.Faction, intended) is { } track && track.Fresh(time))
        { aim = track.Predict(time); velocity = track.Velocity; observed = true; }
        m.LastAimVelocity = velocity;
        if (!observed) return;
        double closing = (aim - m.Position).Dot(Vec3d.From(velocity - m.Velocity));
        if (closing < 0) m.InterceptClosing = true;
        else if (m.InterceptClosing && m.Age > .15) m.InterceptPassed = true;
    }

    private void StepCounterMissileFuzes(double time)
    {
        _counterMissileScratch.Clear();
        foreach (var m in _missiles) if (m.InterceptTarget is not null) _counterMissileScratch.Add(m);
        foreach (var m in _counterMissileScratch)
        {
            if (!_missiles.Contains(m)) continue;
            var target = m.InterceptTarget!;
            if (!_missiles.Contains(target) || target.Health <= 0 || m.Age > 8)
            { _missiles.Remove(m); _ordnanceEvents.Add(new(OrdnanceEventKind.Expired, m.Position, time, m.Faction)); continue; }
            if (m.Age < .08) continue;
            if (m.SeekerTarget is Decoy decoy)
            {
                if ((m.Position - decoy.Position).Length() < m.Definition.InterceptFuzeMeters)
                { _missiles.Remove(m); _ordnanceEvents.Add(new(OrdnanceEventKind.Detonation, m.Position, time, m.Faction)); }
                continue;
            }
            Vec3d r = m.PrevPosition - target.PrevPosition;
            Vec3d travel = m.Position - m.PrevPosition - (target.Position - target.PrevPosition);
            if (!MovingSphereHit(r, travel, m.Definition.InterceptFuzeMeters, out double fraction))
            {
                if (m.InterceptPassed)
                { _missiles.Remove(m); _ordnanceEvents.Add(new(OrdnanceEventKind.Expired, m.Position, time, m.Faction)); }
                continue;
            }
            if(TerrainBlocked(Vec3d.Lerp(m.PrevPosition,m.Position,fraction),Vec3d.Lerp(target.PrevPosition,target.Position,fraction))) continue;
            target.Health -= m.Definition.InterceptDamage;
            if (target.Health <= 0)
            {
                DestroyMissile(m.Shooter, target, time);
                if (Log is { } log) log.Ship(m.Shooter).CounterMissileKills++;
            }
            _missiles.Remove(m); m.Health = 0;
            _ordnanceEvents.Add(new(OrdnanceEventKind.Detonation, Vec3d.Lerp(m.PrevPosition, m.Position, fraction), time, m.Faction));
        }
    }
}
