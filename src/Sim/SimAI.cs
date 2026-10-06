using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

/// <summary>
/// 함선 AI와 함대 지휘. 매 외부 틱(60Hz) 하위 틱 전에 조종 입력을 쓴다.
/// - 이동: 명령별 목표 속도 + 충돌 회피 → 비행보조(우주식) 입력으로 변환.
/// - 기수: 레일건을 쓸 때는 사격 방향, 아니면 목표 속도 방향.
/// - 사격: 잠금·사격통제 해·비행 시간 기준·사선의 아군 확인 후 레일건. 미사일은 함대 일제 사격 창에서 한 함선당 2발.
/// - 방어: 나를 노리는 미사일이 가까우면 디코이, 근접방어 쪽으로 전력 재배분. 과열이면 ECM을 끈다.
/// - 함대 지휘관(진영별 선택): 주 표적을 정하고 함종별 명령을 내린다.
/// </summary>
public sealed partial class SimWorld
{
    public const double ThinkInterval = 0.25;
    private const double CommandInterval = 2.0;
    private const double SalvoWindow = 8.0;
    /// <summary>자기 미사일 사거리의 이 배수 안에 있는 아군은 "곧 합류"로 보고 일제 사격을 기다린다.</summary>
    private const double SalvoJoinFactor = 1.5;
    private const double SalvoCooldown = 25.0;
    private const int MissilesPerSalvo = 2;
    private const float DecoyThreatMeters = 6000f;
    private const float PointDefenseThreatMeters = 20000f;

    private readonly Dictionary<ShipBody, ShipBrain> _brains = new();
    private readonly HashSet<Faction> _commanders = new();
    private readonly Dictionary<(Faction, ShipBody), (int Id, double Until, double NextAllowed)> _salvos = new();
    private int _salvoSequence;
    private double _nextCommand;

    public IReadOnlyDictionary<ShipBody, ShipBrain> Brains => _brains;

    /// <summary>함선에 AI를 붙인다. 이미 있으면 명령만 바꾼다.</summary>
    public ShipBrain AttachBrain(ShipBody ship, ShipOrder order)
    {
        if (_brains.TryGetValue(ship, out ShipBrain? brain))
        {
            brain.Order = order;
            brain.Enabled = true;
            return brain;
        }
        brain = new ShipBrain(ship, order) { NextThink = Time + (ShipBrain.Hash(ship.Callsign) % 15) / 60.0 };
        _brains[ship] = brain;
        return brain;
    }

    /// <summary>플레이어가 조종을 넘겨받을 때 AI를 뗀다.</summary>
    public void DetachBrain(ShipBody ship) => _brains.Remove(ship);

    public ShipBrain? BrainOf(ShipBody ship) => _brains.TryGetValue(ship, out ShipBrain? brain) ? brain : null;

    /// <summary>이 진영에 함대 지휘관을 둔다(적 AI 함대).</summary>
    public void EnableCommander(Faction faction) => _commanders.Add(faction);

    private void StepAI()
    {
        if (_commanders.Count > 0 && Time >= _nextCommand)
        {
            _nextCommand = Time + CommandInterval;
            foreach (Faction faction in _commanders)
                Command(faction);
        }
        foreach (ShipBrain brain in _brains.Values)
        {
            if (brain.Ship.Damage.Destroyed || brain.Ship.Damage.Disabled)
            {
                brain.Activity = brain.Ship.Damage.Destroyed ? "격침" : "무력화";
                continue;
            }
            if (!brain.Enabled) continue;
            if (Time >= brain.NextThink)
            {
                brain.NextThink = Time + ThinkInterval;
                Think(brain);
            }
            Drive(brain);
            Engage(brain);
        }
    }

    // ── 판단(0.25초) ──────────────────────────────────────────

    private void Think(ShipBrain brain)
    {
        ShipBody ship = brain.Ship;
        float maxSpeed = ship.Class.MaxSpeed;
        Vector3 desired;
        brain.WantBoost = false;

        switch (brain.Order.Kind)
        {
            case OrderKind.Attack when brain.Order.Target is ShipBody target && !target.Damage.Destroyed
                && Sensors.Track(ship.Faction, target) is { Level: >= TrackLevel.Contact } track:
                desired = brain.Profile.AttackRuns ? AttackRun(brain, target, track) : Standoff(brain, target, track);
                break;
            case OrderKind.Escort when brain.Order.Target is ShipBody leader && !leader.Damage.Destroyed:
            {
                Vec3d slot = leader.Position + Vec3d.From(leader.Orientation * brain.FormationOffset);
                Vector3 error = (slot - ship.Position).ToVector3();
                desired = leader.Velocity + (error * 0.05f).LimitLength(maxSpeed);
                brain.WantBoost = error.Length() > 20_000f;
                brain.Activity = "호위";
                break;
            }
            case OrderKind.Hold:
                desired = ((brain.Order.Point - ship.Position).ToVector3() * 0.05f).LimitLength(maxSpeed);
                brain.Activity = "위치 유지";
                break;
            default:
                // 표적을 잃었거나 지휘함이 격침: 그 자리에서 멈춘다.
                desired = Vector3.Zero;
                brain.Activity = "대기";
                break;
        }

        desired += Avoidance(ship, desired);
        float limit = maxSpeed * (brain.WantBoost ? ship.Class.BoostMultiplier : 1f);
        brain.DesiredVelocity = desired.LimitLength(limit);
        if (ship.Power.HeatFraction > 0.85f) brain.WantBoost = false;

        AllocatePower(brain);
    }

    /// <summary>전함·호위함: 대치 거리를 두고 표적 둘레를 천천히 돈다.</summary>
    private Vector3 Standoff(ShipBrain brain, ShipBody target, SensorTrack track)
    {
        ShipBody ship = brain.Ship;
        float maxSpeed = ship.Class.MaxSpeed;
        Vector3 r = (track.EstimatedPosition - ship.Position).ToVector3();
        float d = Mathf.Max(r.Length(), 1f);
        Vector3 rh = r / d;
        Vector3 targetVelocity = track.Level >= TrackLevel.Identified ? target.Velocity : Vector3.Zero;
        // 잠기지 않으면(방해·먼 거리) 대치 거리를 줄여 다가간다.
        float standoff = brain.Profile.StandoffMeters * (track.Level >= TrackLevel.Locked ? 1f : 0.6f);
        float radial = Mathf.Clamp((d - standoff) * 0.01f, -0.6f * maxSpeed, maxSpeed);
        Vector3 desired = targetVelocity + rh * radial;
        if (d < standoff * 1.5f)
        {
            Vector3 up = Mathf.Abs(rh.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up;
            desired += rh.Cross(up).Normalized() * (0.35f * maxSpeed * brain._side);
        }
        brain.WantBoost = d > standoff * 2.5f;
        brain.Activity = d > standoff * 1.2f ? "접근" : "포격";
        return desired;
    }

    /// <summary>요격함: 접근 → 사격 → 이탈을 되풀이한다.</summary>
    private Vector3 AttackRun(ShipBrain brain, ShipBody target, SensorTrack track)
    {
        ShipBody ship = brain.Ship;
        float maxSpeed = ship.Class.MaxSpeed;
        Vector3 r = (track.EstimatedPosition - ship.Position).ToVector3();
        float d = Mathf.Max(r.Length(), 1f);
        Vector3 rh = r / d;
        Vector3 targetVelocity = track.Level >= TrackLevel.Identified ? target.Velocity : Vector3.Zero;
        float breakRange = 1500f + target.Hull.BoundingRadius;

        if (Time < brain.BreakUntil)
        {
            brain.WantBoost = true;
            brain.Activity = "이탈";
            return brain.BreakDirection * maxSpeed * ship.Class.BoostMultiplier;
        }
        if (d < breakRange)
        {
            // 표적 옆으로 빠진다(결정적 방향). 4초 뒤 다시 접근.
            Vector3 up = Mathf.Abs(rh.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up;
            brain.BreakDirection = (rh.Cross(up).Normalized() * brain._side + rh * 0.3f).Normalized();
            brain.BreakUntil = Time + 4.0;
            brain.WantBoost = true;
            brain.Activity = "이탈";
            return brain.BreakDirection * maxSpeed * ship.Class.BoostMultiplier;
        }
        if (d > brain.Profile.StandoffMeters)
        {
            brain.WantBoost = d > 20_000f;
            brain.Activity = "접근";
            return targetVelocity + rh * maxSpeed * (brain.WantBoost ? ship.Class.BoostMultiplier : 1f);
        }
        brain.Activity = "사격 접근";
        return targetVelocity + rh * maxSpeed * 0.6f;
    }

    /// <summary>
    /// 충돌 회피: 10초 안의 최근접 거리가 안전 거리보다 작으면 비켜 가는 속도를 더한다. 이미 너무 가까우면 바로 밀어낸다.
    /// </summary>
    private Vector3 Avoidance(ShipBody ship, Vector3 desired)
    {
        Vector3 push = Vector3.Zero;
        foreach (ShipBody other in _ships)
        {
            if (other == ship) continue;
            Vector3 rp = (other.Position - ship.Position).ToVector3();
            float dist = rp.Length();
            float safe = ship.Hull.BoundingRadius + other.Hull.BoundingRadius;
            safe += Mathf.Max(150f, safe * 0.3f);
            if (dist > safe + 40_000f) continue;
            Vector3 rv = other.Velocity - desired;
            float tca = rv.LengthSquared() > 1e-3f ? Mathf.Clamp(-rp.Dot(rv) / rv.LengthSquared(), 0f, 10f) : 0f;
            Vector3 miss = rp + rv * tca;
            float missDist = miss.Length();
            if (missDist >= safe) continue;
            Vector3 away = missDist > 1f ? -miss / missDist : -(rp.LengthSquared() > 1f ? rp.Normalized() : Vector3.Up);
            float urgency = (safe - missDist) / Mathf.Max(tca, 1f);
            push += away * Mathf.Min(urgency * 2f, ship.Class.MaxSpeed);
        }
        return push;
    }

    private void AllocatePower(ShipBrain brain)
    {
        ShipBody ship = brain.Ship;
        bool threatened = _missiles.Any(m => m.Target == ship && (m.Position - ship.Position).Length() < PointDefenseThreatMeters);
        // ECCM: 공격 중인 표적이 방해로 잠기지 않는데 레일건 거리 근처면 센서를 올린다.
        bool jammedOut = brain.Target is ShipBody target && ship.Railgun is RailgunState gun
            && Sensors.Track(ship.Faction, target) is { Jammed: true, Level: < TrackLevel.Locked and > TrackLevel.None } t
            // t.Range는 가장 잘 보는 아군 관측함 기준이다. 판단은 내 위치에서 한다.
            && (t.EstimatedPosition - ship.Position).Length() < gun.Definition.MuzzleSpeed * brain.Profile.RailFlightSeconds * 1.3;
        int[] pips = threatened ? new[] { 1, 2, 2, 3, 0 }   // 근접방어: 센서·무장
            : jammedOut ? new[] { 2, 1, 1, 4, 0 }           // 방해 뚫기
            : (int[])brain.DefaultPips.Clone();
        brain.Eccm = jammedOut && !threatened;
        if (ship.Power.HeatFraction > 0.85f && pips[4] > 0)
        {
            // 과열 직전: ECM을 끄고 그 핍을 실드로.
            pips[1] = Math.Min(4, pips[1] + pips[4]);
            pips[4] = 0;
            int sum = pips.Sum();
            for (int i = 0; sum < ShipPower.TotalPips && i < 4; i++)
                while (pips[i] < ShipPower.MaxPips && sum < ShipPower.TotalPips) { pips[i]++; sum++; }
        }
        bool same = true;
        for (int i = 0; i < ShipPower.ChannelCount; i++)
            same &= ship.Power.Pips((PowerChannel)i) == pips[i];
        if (!same && pips.Sum() == ShipPower.TotalPips)
            ship.Power.SetPips(pips[0], pips[1], pips[2], pips[3], pips[4]);
    }

    // ── 조종(매 틱) ───────────────────────────────────────────

    private void Drive(ShipBrain brain)
    {
        ShipBody ship = brain.Ship;
        float scale = ship.Class.MaxSpeed * (brain.WantBoost ? ship.Class.BoostMultiplier : 1f);
        Vector3 local = ship.Orientation.Inverse() * brain.DesiredVelocity / Mathf.Max(scale, 1f);
        var thrust = new Vector3(local.X, local.Y, -local.Z);
        if (thrust.LengthSquared() > 1f) thrust = thrust.Normalized();

        Vector3? aim = null;
        if (brain.Target is ShipBody target && ship.Railgun is not null)
        {
            SensorTrack track = Sensors.Track(ship.Faction, target);
            FiringSolution solution = FireControl.Solve(ship, target, Time, track: track);
            // 사격 해가 있으면 포구를 그쪽으로. 요격함은 고정 전방포라 늘 기수를 맞춘다.
            if (solution.Valid && (brain.Profile.AttackRuns || solution.FlightTime <= brain.Profile.RailFlightSeconds * 1.5))
                aim = solution.Direction;
            else if (brain.Profile.AttackRuns && track.Level >= TrackLevel.Contact && Time >= brain.BreakUntil)
                aim = (track.EstimatedPosition - ship.Position).ToVector3().Normalized();
        }
        if (aim is null && brain.DesiredVelocity.LengthSquared() > 25f)
            aim = brain.DesiredVelocity.Normalized();

        ship.Control = new ShipControl
        {
            Thrust = thrust,
            Boost = brain.WantBoost,
            FlightAssist = true,
            Style = AssistStyle.Space,
            AimForward = aim,
        };
    }

    // ── 교전(매 틱) ───────────────────────────────────────────

    private void Engage(ShipBrain brain)
    {
        ShipBody ship = brain.Ship;

        // 디코이: 나를 노리는 미사일이 가까우면.
        if (ship.Ordnance.DecoyReady && _missiles.Any(m => m.Target == ship && (m.Position - ship.Position).Length() < DecoyThreatMeters))
            LaunchDecoys(ship);

        if (brain.Target is not ShipBody target || target.Damage.Destroyed) return;
        SensorTrack track = Sensors.Track(ship.Faction, target);

        // 레일건
        if (ship.Railgun is RailgunState gun && gun.Ready && track.Level >= TrackLevel.Locked)
        {
            FiringSolution solution = FireControl.Solve(ship, target, Time, track: track);
            if (solution.Valid && solution.FlightTime <= brain.Profile.RailFlightSeconds
                && !FriendlyInLine(ship, gun.MuzzlePosition, solution.Direction, (float)solution.Range))
                FireRailgun(ship, solution.Direction);
        }

        // 미사일: 일제 사격 창
        if (ship.Ordnance.MissileDefinition is null || track.Level < TrackLevel.Identified) return;
        double range = (track.EstimatedPosition - ship.Position).Length();
        if (range > brain.Profile.MissileRangeMeters) return;
        if (ship.Power.HeatFraction > 0.85f) return;
        var key = (ship.Faction, target);
        if (!_salvos.TryGetValue(key, out var salvo) || Time > salvo.Until)
        {
            if (salvo.Id != 0 && Time < salvo.NextAllowed) return;
            // 일제 사격: 같은 표적을 공격 중이고 사거리 안에서 장전된 함선 수가, 곧 합류할 아군(사거리 1.5배 안,
            // 미사일 잔량 있음) 수에 이르면 연다. 혼자 남았으면 혼자 쏜다. 근접방어를 한꺼번에 포화시키려는 것이다.
            int ready = _brains.Values.Count(b => b.Enabled && b.Ship.Faction == ship.Faction && b.Target == target
                && !b.Ship.Damage.Destroyed && b.Ship.Ordnance.MissileReady
                && (Sensors.Track(ship.Faction, target).EstimatedPosition - b.Ship.Position).Length() <= b.Profile.MissileRangeMeters);
            int capable = _brains.Values.Count(b => b.Enabled && b.Ship.Faction == ship.Faction && !b.Ship.Damage.Destroyed
                && b.Ship.Ordnance.Missiles > 0 && !b.Profile.AttackRuns
                && (Sensors.Track(ship.Faction, target).EstimatedPosition - b.Ship.Position).Length() <= b.Profile.MissileRangeMeters * SalvoJoinFactor);
            if (ready < Math.Max(1, capable)) return;
            salvo = (++_salvoSequence, Time + SalvoWindow, Time + SalvoCooldown);
            _salvos[key] = salvo;
        }
        if (brain.SalvoId != salvo.Id)
        {
            brain.SalvoId = salvo.Id;
            brain.SalvoLaunched = 0;
        }
        if (brain.SalvoLaunched < MissilesPerSalvo && ship.Ordnance.MissileReady && LaunchMissile(ship, target).Fired)
            brain.SalvoLaunched++;
    }

    /// <summary>사선(포구에서 사거리까지)에 아군 선체가 있는가.</summary>
    private bool FriendlyInLine(ShipBody shooter, Vec3d muzzle, Vector3 direction, float range)
    {
        foreach (ShipBody other in _ships)
            if (other != shooter && other.Faction == shooter.Faction && !other.Damage.Destroyed
                && DamageRay.FirstHit(other, muzzle, direction, range, out _))
                return true;
        return false;
    }

    // ── 함대 지휘(2초) ────────────────────────────────────────

    private void Command(Faction faction)
    {
        var fleet = _brains.Values.Where(b => b.Ship.Faction == faction && b.Enabled && !b.Ship.Damage.Destroyed).ToList();
        if (fleet.Count == 0) return;
        ShipBody? flagship = fleet.Select(b => b.Ship).OrderBy(s => s.Class.Kind).FirstOrDefault();
        Vec3d center = flagship!.Position;

        // 아는 적: 접촉 이상. 표적 배분은 식별된 적을 우선한다.
        var known = _ships.Where(s => s.Faction != faction && !s.Damage.Destroyed)
            .Select(s => (Ship: s, Track: Sensors.Track(faction, s)))
            .Where(k => k.Track.Level >= TrackLevel.Contact)
            .ToList();
        var assigned = new Dictionary<ShipBody, int>();

        // 큰 함선부터 표적을 고른다(전함이 먼저 상대 전함을 잡고, 나머지가 흩어진다).
        foreach (ShipBrain brain in fleet.OrderBy(b => b.Ship.Class.Kind))
        {
            ShipBody ship = brain.Ship;
            ShipBody? target = PickTarget(ship, known, assigned);
            if (target is not null)
                assigned[target] = assigned.GetValueOrDefault(target) + 1;
            double range = target is null ? double.PositiveInfinity
                : (Sensors.Track(faction, target).EstimatedPosition - ship.Position).Length();
            bool lead = flagship == ship || flagship.Damage.Destroyed;

            ShipOrder order = ship.Class.Kind switch
            {
                // 전함: 아는 적이 있으면 공격, 없으면 그 자리.
                HullKind.Battleship => target is null ? ShipOrder.HoldAt(ship.Position) : ShipOrder.AttackOn(target),
                // 호위함: 표적이 자기 미사일 사거리에 들어오기 전까지 기함 호위.
                HullKind.Escort => target is not null && (range < brain.Profile.MissileRangeMeters || lead) ? ShipOrder.AttackOn(target)
                    : lead ? ShipOrder.HoldAt(ship.Position) : ShipOrder.EscortOf(flagship),
                // 요격함: 60 km 안의 표적만 쫓고, 아니면 기함 호위(앞쪽 경계).
                _ => target is not null && range < InterceptorEngageMeters ? ShipOrder.AttackOn(target)
                    : lead ? ShipOrder.HoldAt(ship.Position) : ShipOrder.EscortOf(flagship),
            };
            // 같은 명령이면 바꾸지 않는다(위치 유지 지점이 2초마다 밀리지 않게).
            if (order.Kind != brain.Order.Kind || order.Target != brain.Order.Target)
                brain.Order = order;
        }
    }

    private const double InterceptorEngageMeters = 60_000;

    /// <summary>
    /// 함종별로 상대하기 좋은 표적을 고른다. 같은 함종(관통력이 맞는 상대)을 우선하고,
    /// 이미 다른 아군이 맡은 표적은 피해서 흩어진다. 접촉뿐인 적은 식별된 적이 없을 때만.
    /// 점수 = 함종 선호(0~3) + 거리(100 km당 1) + 이미 배정된 수 × 0.8.
    /// </summary>
    private ShipBody? PickTarget(ShipBody ship, List<(ShipBody Ship, SensorTrack Track)> known, Dictionary<ShipBody, int> assigned)
    {
        bool anyIdentified = known.Any(k => k.Track.Level >= TrackLevel.Identified);
        ShipBody? best = null;
        double bestScore = double.PositiveInfinity;
        foreach (var (enemy, track) in known)
        {
            if (anyIdentified && track.Level < TrackLevel.Identified) continue;
            int preference = !anyIdentified ? 0 : ship.Class.Kind switch
            {
                HullKind.Battleship => enemy.Class.Kind switch { HullKind.Battleship => 0, HullKind.Escort => 1, _ => 3 },
                HullKind.Escort => enemy.Class.Kind switch { HullKind.Escort => 0, HullKind.Battleship => 1, _ => 2 },
                _ => enemy.Class.Kind switch { HullKind.Interceptor => 0, HullKind.Escort => 1, _ => 2 },
            };
            // 무력화된 적은 위협이 아니므로 뒤로 미룬다(식별된 경우에만 알 수 있다).
            double score = preference + (track.EstimatedPosition - ship.Position).Length() / 100_000.0
                + assigned.GetValueOrDefault(enemy) * 0.8
                + (track.Level >= TrackLevel.Identified && enemy.Damage.Disabled ? 3 : 0);
            if (score < bestScore)
            {
                bestScore = score;
                best = enemy;
            }
        }
        return best;
    }

    private void ResetAI()
    {
        _salvos.Clear();
    }
}
