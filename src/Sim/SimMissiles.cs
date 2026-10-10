using System;
using System.Collections.Generic;
using Godot;

namespace SpaceFleet.Sim;

/// <summary>
/// 미사일·디코이·근접방어. 하위 틱(240Hz)마다 진행한다.
/// - 중간 유도: 발사 진영 센서망의 추정 위치(접촉 이상). 놓치면 마지막 목표점으로 계속 난다.
/// - 종말 유도: 탐색기가 시야각·사거리 안의 적 함선과 디코이 중 신호(÷ ECM)에 비례한 확률로 고른다. 0.5초마다 다시 고른다.
/// - 조종: 비례항법(N=4)의 요구 방향으로 유한 속도로 기수를 돌리고 기수 주변 추력만 사용. 연료가 떨어지면 관성.
/// - 신관: 상대 경로가 선체를 지나거나 선체 상자에 신관 거리 안으로 들어오면 폭발. 피해는 실드·장갑·모듈 관통 계산을 그대로 쓴다.
/// - 근접방어: 포대마다 사거리 안의 가장 가까운 적 미사일을 쏜다. 명중률은 거리·가로지르는 속도·센서·무장 채널에 따른다.
/// </summary>
public sealed partial class SimWorld
{
    public const double SeekerInterval = 0.5;
    /// <summary>탐색기는 원래 표적에 이 가중을 더 준다(다른 함선으로 쉽게 갈아타지 않게).</summary>
    private const float IntendedTargetWeight = 1.5f;
    private const float ProportionalGain = 4f;
    /// <summary>근접방어 명중률: 가로지르는 속도가 이보다 빠르면 비례해서 떨어진다(m/s).</summary>
    private const float PointDefenseCrossingSpeed = 1500f;
    // Start slewing before an approaching threat reaches gun range; this adds no firing range.
    private const float PointDefenseTrackingRangeMultiplier = 1.5f;

    private readonly List<Missile> _missiles = new();
    private readonly List<Decoy> _decoys = new();
    private readonly List<OrdnanceEvent> _ordnanceEvents = new();
    private readonly List<PointDefenseShot> _pointDefenseShots = new();
    private uint _pointDefenseSequence;
    private readonly Dictionary<ShipBody, (uint Failures, uint Jettisons)> _amObserved = new();

    public IReadOnlyList<Missile> Missiles => _missiles;
    public IReadOnlyList<Decoy> Decoys => _decoys;
    public IReadOnlyList<OrdnanceEvent> OrdnanceEvents => _ordnanceEvents;
    public IReadOnlyList<PointDefenseShot> PointDefenseShots => _pointDefenseShots;

    public FireAttempt LaunchMissile(ShipBody shooter, ShipBody target)
    {
        if (!_ships.Contains(shooter) || !_ships.Contains(target)) return new(false, "월드에 없는 함선");
        if (shooter.Definition.Missiles is not MissileDefinition def) return new(false, "발사관 없음");
        if (target.Faction == shooter.Faction || target.Damage.Destroyed) return new(false, "유효 표적 없음");
        if (!shooter.Ordnance.MissileReady) return new(false, shooter.Ordnance.MissileStatus);
        SensorTrack track = Sensors.Track(shooter.Faction, target);
        if (track.Level < TrackLevel.Contact) return new(false, "표적 미탐지");

        _shotSequence++;
        Vec3d start = shooter.Position + Vec3d.From(shooter.Orientation * def.LaunchPoint);
        var missile = new Missile
        {
            Id = _shotSequence, Shooter = shooter, Target = target, Definition = def,
            Position = start, PrevPosition = start, Health = def.HitPoints, AimPoint = track.EstimatedPosition,
            // 수직 발사관: 함선 위쪽으로 사출한 뒤 스스로 표적을 향해 꺾는다.
            Velocity = shooter.Velocity + shooter.Up * def.EjectSpeed,
            NoseDirection = shooter.Up, PreviousNoseDirection = shooter.Up,
            NextSeekerCheck = Time + SeekerInterval,
        };
        _missiles.Add(missile);
        shooter.Ordnance.ConsumeMissile();
        Log?.Fire(shooter, BattleWeapon.Missile, Time);
        return new(true, "미사일 발사");
    }

    public int LaunchDecoys(ShipBody ship)
    {
        if (!_ships.Contains(ship) || !ship.Ordnance.DecoyReady) return 0;
        DecoyDefinition def = ship.Definition.Decoys!;
        int count = ship.Ordnance.ConsumeDecoys();
        for (int i = 0; i < count; i++)
        {
            // 좌우로 번갈아, 약간 뒤·위로 퍼뜨린다(결정적).
            float side = i % 2 == 0 ? 1f : -1f;
            Vector3 local = new Vector3(side, 0.35f + 0.15f * (i / 2), 0.5f).Normalized();
            _shotSequence++;
            _decoys.Add(new Decoy
            {
                Id = _shotSequence, Owner = ship, Definition = def,
                Position = ship.Position, PrevPosition = ship.Position,
                Velocity = ship.Velocity + ship.Orientation * local * def.EjectSpeed,
            });
        }
        return count;
    }

    private void ResetOrdnance()
    {
        foreach (ShipBody ship in _ships) ship.Ordnance.Reset();
        _missiles.Clear(); _decoys.Clear(); _ordnanceEvents.Clear(); _pointDefenseShots.Clear();
        foreach (var ship in _ships) _amObserved[ship] = (ship.Ordnance.Antimatter.Failures, ship.Ordnance.Antimatter.Jettisons);
    }

    private void PruneOrdnanceEvents()
    {
        _ordnanceEvents.RemoveAll(e => Time - e.Time > 3);
        _pointDefenseShots.RemoveAll(s => Time - s.Time > 0.15);
    }

    private void StepOrdnance(double dt, double time)
    {
        long timing = SimProfiler.Begin();
        foreach (var ship in _ships)
        {
            var am = ship.Ordnance.Antimatter;
            var old = _amObserved.GetValueOrDefault(ship);
            if (am.Failures > old.Failures)
                _ordnanceEvents.Add(new(OrdnanceEventKind.ContainmentFailure, ship.Position, time, ship.Faction, BattleWeapon.Antimatter));
            if (am.Jettisons > old.Jettisons)
                _ordnanceEvents.Add(new(OrdnanceEventKind.Jettisoned, ship.Position, time, ship.Faction, BattleWeapon.Antimatter, -ship.Up));
            _amObserved[ship] = (am.Failures, am.Jettisons);
        }
        foreach (Decoy decoy in _decoys)
        {
            decoy.PrevPosition = decoy.Position;
            decoy.Position += decoy.Velocity * (float)dt;
            decoy.Age += dt;
        }
        _decoys.RemoveAll(d => d.Expired);

        for (int i = _missiles.Count - 1; i >= 0; i--)
        {
            Missile m = _missiles[i];
            m.Age += dt;
            if (m.Age > m.Definition.MaxFlightSeconds)
            {
                Log?.EndAntimatter(m, AntimatterOutcome.Expired, time);
                _ordnanceEvents.Add(new(OrdnanceEventKind.Expired, m.Position, time, m.Faction, m.Weapon));
                _missiles.RemoveAt(i);
                continue;
            }
            if (m.SeekerTarget is Decoy lost && (lost.Expired || !_decoys.Contains(lost)))
            {
                m.SeekerTarget = null;
                m.NextSeekerCheck = time;
            }
            if (time >= m.NextSeekerCheck)
            {
                UpdateSeeker(m, time);
                m.NextSeekerCheck = time + SeekerInterval;
            }
            StepMissile(m, dt);
            if (m.Assault is not null && Log is not null)
            {
                m.ClosestTargetHull = Math.Min(m.ClosestTargetHull, HullDistance(m.Target, m.Position));
                if (m.SeekerTarget == m.Target) m.SeekerSeconds += dt;
            }
            if (Fuze(m, dt, time))
                _missiles.RemoveAt(i);
            else if (m.Assault is { } am && m.TravelMeters >= am.MaxTravelMeters - .001)
            {
                Log?.EndAntimatter(m, AntimatterOutcome.Expired, time);
                _ordnanceEvents.Add(new(OrdnanceEventKind.Expired, m.Position, time, m.Faction, m.Weapon));
                _missiles.RemoveAt(i);
            }
        }

        SimProfiler.End(SimSection.Missiles, timing);
        timing = SimProfiler.Begin();
        StepPointDefense(dt, time);
        SimProfiler.End(SimSection.PointDefense, timing);
        timing = SimProfiler.Begin();
        StepDefenseDrones(dt, time);
        SimProfiler.End(SimSection.Drones, timing);
    }

    private void StepMissile(Missile m, double dt)
    {
        Vec3d aim;
        Vector3 aimVelocity;
        switch (m.SeekerTarget)
        {
            case ShipBody ship:
                aim = ship.Position + Vec3d.From(ship.Orientation * (ship == m.Target ? m.LocalAim ?? Vector3.Zero : Vector3.Zero));
                aimVelocity = ship.Velocity;
                if (m.Assault is not null && ship==m.Target && m.LocalAim is Vector3 module)
                    aimVelocity += ship.Orientation*ship.AngularVelocity.Cross(module);
                break;
            case Decoy decoy:
                aim = decoy.Position; aimVelocity = decoy.Velocity;
                break;
            default:
                if (m.Assault is not null) { aim = m.AimPoint; aimVelocity = Vector3.Zero; break; }
                SensorTrack track = Sensors.Track(m.Faction, m.Target);
                if (track.Level >= TrackLevel.Contact)
                {
                    aim = track.EstimatedPosition;
                    // 식별 이상이면 표적 속도도 안다. 접촉뿐이면 위치만.
                    aimVelocity = track.Level >= TrackLevel.Identified ? m.Target.Velocity : Vector3.Zero;
                }
                else
                {
                    aim = m.AimPoint;
                    aimVelocity = Vector3.Zero;
                }
                break;
        }
        m.AimPoint = aim;

        Vector3 accel = Vector3.Zero;
        if (m.Assault is null && m.Burning)
        {
            Vector3 requested = Guidance(m.Position, m.Velocity, aim, aimVelocity, m.Definition.Accel);
            if (m.NoseDirection.LengthSquared() < .5f)
                m.NoseDirection = m.Velocity.LengthSquared() > 1 ? m.Velocity.Normalized() : (aim - m.Position).ToVector3().Normalized();
            if (requested.LengthSquared() > .001f)
            {
                // Powered missiles turn forward through an arc; they cannot use the
                // main motor as an instant retro-thruster after passing the target.
                Vector3 desired = m.Velocity.LengthSquared() > 1
                    ? TurnMissileNose(m.Velocity, requested, Mathf.DegToRad(m.Definition.MaxSteeringAngleDegrees)) : requested.Normalized();
                m.NoseDirection = TurnMissileNose(m.NoseDirection, desired, Mathf.DegToRad(m.Definition.TurnRateDegrees) * (float)dt);
                Vector3 thrust = TurnMissileNose(m.NoseDirection, desired, Mathf.DegToRad(m.Definition.ThrustGimbalDegrees));
                accel = thrust * m.Definition.Accel;
            }
        }
        if (m.Assault is { } assault && m.Burning)
        {
            // The motor thrusts along the launch attitude, not inherited transverse drift.
            // Terminal corrections use the same accelerating flight model and a limited gimbal.
            Vector3 forward = m.LaunchDirection;
            Vector3 lateral = Vector3.Zero;
            if (m.SeekerLocked && AssaultGuidance.Intercept((aim-m.Position).ToVector3(),aimVelocity-m.Velocity,
                0,m.Definition.Accel,Math.Max(.01,m.Definition.MaxFlightSeconds-m.Age),out var correction,out _))
                lateral=(correction*m.Definition.Accel-forward*correction.Dot(forward)*m.Definition.Accel)
                    .LimitLength(assault.TerminalAccelG*ShipBody.StandardGravity);
            accel = forward * Mathf.Sqrt(Mathf.Max(0, m.Definition.Accel * m.Definition.Accel - lateral.LengthSquared())) + lateral;
        }
        m.Velocity += accel * (float)dt;
        m.PrevPosition = m.Position;
        Vector3 travel = m.Velocity * (float)dt;
        if (m.Assault is { } limited) travel = travel.LimitLength((float)Math.Max(0, limited.MaxTravelMeters - m.TravelMeters));
        m.Position += travel;
        m.TravelMeters += travel.Length();
    }

    /// <summary>Bounded attitude rotation, including a deterministic axis for an exact 180-degree demand.</summary>
    internal static Vector3 TurnMissileNose(Vector3 current, Vector3 desired, float maxAngle)
    {
        current = current.Normalized(); desired = desired.Normalized();
        float angle = Mathf.Atan2(current.Cross(desired).Length(), Mathf.Clamp(current.Dot(desired), -1, 1));
        if (angle <= maxAngle) return desired;
        Vector3 axis = current.Cross(desired);
        if (axis.LengthSquared() < 1e-10f)
            axis = current.Cross(Mathf.Abs(current.Dot(Vector3.Up)) < .9f ? Vector3.Up : Vector3.Right);
        return current.Rotated(axis.Normalized(), maxAngle).Normalized();
    }

    /// <summary>비례항법 + 시선 방향 가속. 최대 가속을 넘지 않는다.</summary>
    internal static Vector3 Guidance(Vec3d position, Vector3 velocity, Vec3d aim, Vector3 aimVelocity, float maxAccel)
    {
        Vector3 r = (aim - position).ToVector3();
        float dist = r.Length();
        if (dist < 1f) return Vector3.Zero;
        Vector3 rh = r / dist;
        Vector3 vRel = aimVelocity - velocity;
        float closing = -rh.Dot(vRel);
        Vector3 omega = r.Cross(vRel) / (dist * dist);
        // 발사 직후처럼 접근 속도가 작으면 최소값으로 돌려 시선 회전을 계속 잡는다.
        Vector3 pn = ProportionalGain * Mathf.Max(closing, 200f) * omega.Cross(rh);
        if (pn.LengthSquared() > maxAccel * maxAccel)
            pn = pn.Normalized() * maxAccel;
        float axial = Mathf.Sqrt(Mathf.Max(0f, maxAccel * maxAccel - pn.LengthSquared()));
        return pn + rh * axial;
    }

    private void UpdateSeeker(Missile m, double time)
    {
        MissileDefinition def = m.Definition;
        // Keep the stabilized seeker's search axis along the flight path. Motor
        // attitude may slew sideways for a correction without moving its sensor window.
        Vector3 forward = m.Assault is not null ? m.LaunchDirection : m.Velocity.LengthSquared() > 1f ? m.Velocity.Normalized()
            : (m.AimPoint - m.Position).ToVector3().Normalized();
        float halfFov = Mathf.DegToRad(def.SeekerFovDegrees * 0.5f);
        var candidates = new List<(object Target, float Weight)>();
        float total = 0f;

        void Consider(object target, Vec3d position, float signature, float jam, float bias)
        {
            Vector3 los = (position - m.Position).ToVector3();
            float d = los.Length();
            if (d > def.SeekerRangeMeters || d < 1f || forward.AngleTo(los) > halfFov) return;
            float km = d / 1000f;
            float snr = def.SeekerStrength * signature / (km * km) / (1f + jam);
            if (snr < 1f) return;
            candidates.Add((target, snr * bias));
            total += snr * bias;
        }

        foreach (ShipBody ship in _ships)
        {
            if (ship.Faction == m.Faction || ship.Damage.Destroyed) continue;
            float jam = ship.Power.EcmActive ? ship.Definition.Sensors.Jammer * ship.Power.EcmEffect : 0f;
            Consider(ship, ship.Position, SensorNet.Signature(ship), jam, ship == m.Target ? IntendedTargetWeight : 1f);
        }
        foreach (Decoy decoy in _decoys)
            if (decoy.Faction != m.Faction)
                Consider(decoy, decoy.Position, decoy.Signature, 0f, 1f);

        if (candidates.Count == 0)
        {
            m.SeekerTarget = null;
            return;
        }
        // 신호 비례 확률로 고른다. 디코이가 밝으면 끌려간다. 결정적 난수(미사일·판정 순번).
        float roll = Roll(m.Id * 2654435761u ^ (uint)(time / SeekerInterval)) * total;
        foreach (var (target, weight) in candidates)
        {
            if ((roll -= weight) <= 0f)
            {
                m.SeekerTarget = target;
                return;
            }
        }
        m.SeekerTarget = candidates[^1].Target;
    }

    /// <summary>신관. 탐색기가 디코이를 쫓으면 디코이 근처에서 헛되이 터진다.</summary>
    private bool Fuze(Missile m, double dt, double time)
    {
        if (m.Assault is not null && AssaultCollision(m, time)) return true;
        if (m.SeekerTarget is Decoy decoy)
        {
            if ((decoy.Position - m.Position).Length() < m.Definition.FuzeMeters * 2f)
            {
                Log?.EndAntimatter(m, AntimatterOutcome.Decoy, time);
                _ordnanceEvents.Add(new(OrdnanceEventKind.Detonation, m.Position, time, m.Faction, m.Weapon));
                return true;
            }
            return false;
        }
        if (m.Assault is not null) return false; // AM transfers energy only on physical hull contact
        ShipBody victim = m.SeekerTarget as ShipBody ?? m.Target;
        Vec3d relStart = m.PrevPosition - victim.PrevPosition;
        Vec3d relEnd = m.Position - victim.Position;
        Vec3d travel = relEnd - relStart;
        double reach = DamageRay.DefenseRadius(victim) + m.Definition.FuzeMeters;
        double tt = travel.LengthSquared() > 1e-9 ? Math.Clamp(-relStart.Dot(travel) / travel.LengthSquared(), 0, 1) : 0;
        if ((relStart + travel * tt).Length() > reach) return false;

        Quaternion orientation = victim.PrevOrientation.Slerp(victim.Orientation, 0.5f);
        double length = travel.Length();
        // 1) 상대 경로가 선체를 관통: 맞은 지점에서 폭발.
        if (length > 1e-6)
        {
            Vector3 dir = (travel * (1 / length)).ToVector3();
            if (DamageRay.FirstDefenseHitAtPose(victim, m.PrevPosition, dir, (float)length, victim.PrevPosition, orientation, out float hit))
            {
                Vec3d offset = (victim.Position - victim.PrevPosition) * (hit / length);
                Detonate(m, victim, m.PrevPosition + offset + Vec3d.From(dir) * Math.Max(0, hit - 1), dir,
                    victim.PrevPosition + offset, orientation, time);
                return true;
            }
        }
        // 2) 근접: 끝 위치가 선체 상자에서 신관 거리 안.
        if (HullDistance(victim, m.Position) <= m.Definition.FuzeMeters)
        {
            Vector3 toCenter = (victim.Position - m.Position).ToVector3().Normalized();
            Detonate(m, victim, m.Position, toCenter, victim.Position, victim.Orientation, time);
            return true;
        }
        return false;
    }

    private void Detonate(Missile m, ShipBody victim, Vec3d origin, Vector3 direction, Vec3d pose, Quaternion orientation, double time)
    {
        float shieldBefore = victim.Damage.Shield;
        DamagePacket packet = m.Assault is { } am ? m.Definition.Packet with { Range = am.DamageDepthMeters } : m.Definition.Packet;
        // Preserve AM's authored penetration depth measured from the hull, even when the field intercepts it first.
        if(m.Assault is not null && DamageRay.FirstHitAtPose(victim,origin,direction,1e6f,pose,orientation,out float hullDistance))
            packet=packet with { Range=packet.Range+Mathf.Max(0,hullDistance-1) };
        ShotResult hit = DamageRay.ApplyAtPose(victim, origin, direction, packet, time, m.Id, pose, orientation);
        Log?.EndAntimatter(m, AntimatterOutcome.Hit, time, victim);
        Log?.Hit(m.Shooter, hit, m.Weapon, time, shieldBefore);
        RecordImpact(m.Id, m.Shooter, hit, time, direction, m.Definition.Energy, shieldBefore, m.Weapon);
        _ordnanceEvents.Add(new(OrdnanceEventKind.Detonation, hit.ShieldPoint??hit.Point, time, m.Faction, m.Weapon, direction));
    }

    private bool AssaultCollision(Missile missile, double time)
    {
        ShipBody? hitShip = null; double earliest = double.PositiveInfinity;
        Vector3 hitDirection = Vector3.Zero; Quaternion hitRotation = Quaternion.Identity;
        Vec3d hitPoint = default, hitPose = default;
        foreach (ShipBody ship in _ships)
        {
            if (ship == missile.Shooter) continue;
            Vec3d relative = missile.PrevPosition - ship.PrevPosition;
            Vec3d travel = missile.Position - missile.PrevPosition - (ship.Position - ship.PrevPosition);
            double length = travel.Length();
            if (length < 1e-8) continue;
            double closest = Math.Clamp(-relative.Dot(travel) / (length * length), 0, 1);
            if ((relative + travel * closest).Length() > DamageRay.DefenseRadius(ship)) continue;
            Vector3 direction = (travel * (1 / length)).ToVector3();
            Quaternion rotation = ship.PrevOrientation.Slerp(ship.Orientation, .5f);
            if (!DamageRay.FirstDefenseHitAtPose(ship, missile.PrevPosition, direction, (float)length, ship.PrevPosition, rotation, out float hit)) continue;
            double fraction = hit / length;
            if (fraction >= earliest) continue;
            earliest = fraction; hitShip = ship; hitDirection = direction; hitRotation = rotation;
            Vec3d offset = (ship.Position - ship.PrevPosition) * fraction;
            hitPose = ship.PrevPosition + offset;
            hitPoint = missile.PrevPosition + offset + Vec3d.From(direction) * Math.Max(0, hit - .01f);
        }
        if (hitShip is null) return false;
        Detonate(missile, hitShip, hitPoint, hitDirection, hitPose, hitRotation, time); return true;
    }

    /// <summary>점에서 선체 상자들까지의 최단 거리(m). 안쪽이면 0.</summary>
    internal static float HullDistance(ShipBody ship, Vec3d point)
    {
        Vector3 p = ship.Orientation.Inverse() * (point - ship.Position).ToVector3();
        float best = float.PositiveInfinity;
        foreach (HullSection s in ship.Definition.HullSections)
        {
            Vector3 q = (p - s.Center).Clamp(-s.HalfSize, s.HalfSize);
            best = Mathf.Min(best, (p - s.Center - q).Length());
        }
        return best;
    }

    /// <summary>근접방어가 함선(요격함)을 맞혔을 때의 피해. 실드부터 깎고 얇은 장갑만 뚫는다.</summary>
    private static readonly DamagePacket PointDefenseShipPacket = new(20f, 25f, 10f, 5000f);
    /// <summary>요격함은 미사일보다 커서 맞히기 쉽다.</summary>
    private const float PointDefenseShipSizeFactor = 2f;

    // Reused per defending ship/substep. Preserve source order and recheck live health per mount:
    // an earlier mount can kill/remove a candidate without invalidating these scratch lists.
    private readonly List<Missile> _pdMissileCandidates = new();
    private readonly List<ShipBody> _pdRaiderCandidates = new(), _pdCarrierCandidates = new();
    private Vec3d[] _pdMountPositions = Array.Empty<Vec3d>();

    /// <summary>
    /// 사거리 안 표적을 우선한다. 미사일 우선, 요격함 보조포는 드론 다음 요격함,
    /// 대형함 PD는 요격함 다음 드론. 사거리 밖 표적은 조기 추적만 한다.
    /// 제한된 속도로 선회·고각을 맞춘 뒤에만 발사하므로 근접 고속 횡단 표적에 사격 공백이 생긴다.
    /// </summary>
    private void StepPointDefense(double dt, double time)
    {
        bool anyInterceptors = false, anyDrones = false;
        foreach (ShipBody s in _ships)
        {
            anyInterceptors |= s.Class.Kind == HullKind.Interceptor && !s.Damage.Destroyed;
            anyDrones |= !s.Damage.Destroyed && s.Ordnance.Drones.SurvivingCount>0;
        }
        foreach (ShipBody ship in _ships)
        {
            if (ship.Definition.PointDefense is not PointDefenseDefinition pd) continue;
            float quality = Mathf.Clamp(ship.Damage.SensorFraction * ship.Power.SensorEffect, 0f, 1.5f) * ship.Power.WeaponEffect;
            if (ship.Damage.Destroyed || quality <= 0.001f || _missiles.Count == 0 && !anyInterceptors && !anyDrones)
            {
                foreach (var state in ship.Ordnance.PointDefense) state.ClearTracking();
                continue;
            }

            if (pd.Mounts.Length == 0) continue;
            if (_pdMountPositions.Length < pd.Mounts.Length) Array.Resize(ref _pdMountPositions, pd.Mounts.Length);
            var bounds = SpatialBounds.Segment(ship.Position, ship.Position);
            for (int i = 0; i < pd.Mounts.Length; i++)
            {
                _pdMountPositions[i] = ship.Position + Vec3d.From(ship.Orientation * pd.Mounts[i]);
                bounds = bounds.Include(_pdMountPositions[i]);
            }
            bounds = bounds.Expanded(pd.RangeMeters * PointDefenseTrackingRangeMultiplier);
            _pdMissileCandidates.Clear(); _pdRaiderCandidates.Clear(); _pdCarrierCandidates.Clear();
            foreach (var missile in _missiles)
                if (missile.Faction != ship.Faction && missile.Health > 0 && bounds.Contains(missile.Position))
                    _pdMissileCandidates.Add(missile);
            foreach (var other in _ships)
            {
                if (other.Faction == ship.Faction || other.Damage.Destroyed) continue;
                if (other.Class.Kind == HullKind.Interceptor && bounds.Contains(other.Position)) _pdRaiderCandidates.Add(other);
                if (other.Definition.DefenseDrones is { } drones && bounds.Expanded(drones.OrbitMeters).Contains(other.Position))
                    _pdCarrierCandidates.Add(other);
            }
            if (_pdMissileCandidates.Count == 0 && _pdRaiderCandidates.Count == 0 && _pdCarrierCandidates.Count == 0)
            {
                foreach (var state in ship.Ordnance.PointDefense) state.ClearTracking();
                continue;
            }
            Quaternion inverse = ship.Orientation.Inverse();
            for (int i = 0; i < pd.Mounts.Length; i++)
            {
                Vec3d mount = _pdMountPositions[i];
                var tracking = ship.Ordnance.PointDefense[i];
                Missile? threat = null;
                ShipBody? raider = null, droneCarrier = null;
                int droneIndex=-1, bestPriority=int.MaxValue;
                double nearest = pd.RangeMeters * PointDefenseTrackingRangeMultiplier;
                bool Candidate(Vec3d point,int priority, out double distance, out int rank)
                {
                    distance=(point-mount).Length(); rank=(distance<=pd.RangeMeters ? 0 : 10)+priority;
                    if(distance>pd.RangeMeters*PointDefenseTrackingRangeMultiplier) return false;
                    if(rank>bestPriority || rank==bestPriority && distance>=nearest) return false;
                    return tracking.Contains(inverse * (point-mount).ToVector3());
                }
                foreach (Missile m in _pdMissileCandidates)
                {
                    if (m.Faction == ship.Faction || m.Health<=0) continue;
                    if (Candidate(m.Position,0,out double distance,out int rank))
                    { bestPriority=rank; nearest=distance; threat=m; }
                }

                if (anyInterceptors)
                    foreach (ShipBody other in _pdRaiderCandidates)
                    {
                        if (other.Faction == ship.Faction || other.Class.Kind != HullKind.Interceptor || other.Damage.Destroyed) continue;
                        if (Candidate(other.Position,ship.Class.Kind==HullKind.Interceptor ? 2 : 1,out double distance,out int rank))
                        { bestPriority=rank; nearest=distance; raider=other; threat=null; }
                    }
                if (anyDrones)
                    foreach (var carrier in _pdCarrierCandidates)
                    {
                        if (carrier.Faction==ship.Faction || carrier.Damage.Destroyed || carrier.Definition.DefenseDrones is not { } droneDef) continue;
                        double range=pd.RangeMeters*PointDefenseTrackingRangeMultiplier+droneDef.OrbitMeters;
                        if ((carrier.Position-mount).LengthSquared()>range*range) continue;
                        var drones=carrier.Ordnance.Drones;
                        for (int j=0;j<droneDef.Count;j++)
                        {
                            if (!drones.Alive(j)) continue;
                            Vec3d point=drones.WorldPosition(j);
                            if (!Candidate(point,ship.Class.Kind==HullKind.Interceptor ? 1 : 2,out double distance,out int rank)) continue;
                            // Only a candidate that can replace the current target needs an occlusion ray.
                            // The pivot lies on the armor surface; start just outside the local deck.
                            Vec3d clearOrigin=mount+Vec3d.From(ship.Orientation*(tracking.MountBasis*Vector3.Up)*.05f);
                            if (!ClearDefenseLine(clearOrigin,point)) continue;
                            bestPriority=rank; nearest=distance;
                            droneCarrier=carrier; droneIndex=j; threat=null; raider=null;
                        }
                    }
                if (threat is null && raider is null && droneCarrier is null) { tracking.ClearTracking(); continue; }

                Vec3d aim = threat?.Position ?? raider?.Position ?? droneCarrier!.Ordnance.Drones.WorldPosition(droneIndex);
                tracking.Track(inverse * (aim-mount).ToVector3(), dt, ship.Power.WeaponEffect);
                tracking.TargetKind=threat is not null ? PointDefenseTarget.Missile : raider is not null ? PointDefenseTarget.Interceptor : PointDefenseTarget.Drone;
                if (!tracking.Aligned || nearest > pd.RangeMeters) { tracking.FireAccumulator = 0; continue; }
                tracking.FireAccumulator += pd.ShotsPerSecond * (float)dt;
                Vector3 velocity = threat?.Velocity ?? raider?.Velocity ?? droneCarrier!.Ordnance.Drones.WorldVelocity(droneIndex);
                uint salt = threat?.Id ?? (raider is not null ? ShipBrain.Hash(raider.Callsign) : ShipBrain.Hash(droneCarrier!.Callsign)^(uint)(droneIndex+1)*2654435761u);
                while (tracking.FireAccumulator >= 1f && (threat is null || threat.Health > 0)
                    && raider?.Damage.Destroyed!=true && (droneCarrier is null || droneCarrier.Ordnance.Drones.Alive(droneIndex)))
                {
                    tracking.FireAccumulator -= 1f;
                    Vector3 los = (aim - mount).ToVector3();
                    float d = Mathf.Max(los.Length(), 1f);
                    Vector3 rh = los / d;
                    Vector3 rel = velocity - ship.Velocity;
                    float crossing = (rel - rh * rel.Dot(rh)).Length();
                    float p = pd.HitChance * (1f - 0.7f * d / pd.RangeMeters)
                        * Mathf.Min(1f, PointDefenseCrossingSpeed / Mathf.Max(crossing, 1f)) * quality
                        * (raider is not null ? PointDefenseShipSizeFactor : droneCarrier is not null ? 1.5f : threat!.Assault is null ? 1f : .8f);
                    bool hit = Roll(++_pointDefenseSequence * 2246822519u ^ salt) < p;
                    _pointDefenseShots.Add(new PointDefenseShot(mount, aim, time, hit, ship.Faction));
                    tracking.LastFiredAt = time;
                    tracking.ShotCount++;
                    if (!hit) continue;
                    if (threat is not null)
                        threat.Health -= pd.DamagePerHit;
                    else if (droneCarrier is not null)
                        DamageDrone(ship,droneCarrier,droneIndex,pd.DamagePerHit,time);
                    else
                    {
                        float shieldBefore = raider!.Damage.Shield;
                        ShotResult result = DamageRay.Apply(raider, mount, rh, PointDefenseShipPacket, time, ++_shotSequence);
                        if (result.Target is not null)
                            _pointDefenseShots[^1] = _pointDefenseShots[^1] with { To = result.ShieldPoint ?? result.Point };
                        Log?.Hit(ship, result, BattleWeapon.PointDefense, time, shieldBefore);
                        RecordImpact(_shotSequence, ship, result, time, rh, PointDefenseShipPacket.Energy,
                            shieldBefore, BattleWeapon.PointDefense);
                    }
                }
                if (threat is not null && threat.Health <= 0 && _missiles.Remove(threat))
                {
                    Log?.Intercept(ship, threat);
                    Log?.EndAntimatter(threat, AntimatterOutcome.PointDefense, time);
                    _ordnanceEvents.Add(new(OrdnanceEventKind.Intercepted, threat.Position, time, threat.Faction, threat.Weapon));
                }
            }
        }
    }

    private static float Roll(uint value)
    {
        value ^= value >> 16; value *= 0x7feb352d; value ^= value >> 15;
        value *= 0x846ca68b; value ^= value >> 16;
        return (value & 0xffffff) / 16777216f;
    }
}
