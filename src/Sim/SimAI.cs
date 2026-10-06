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
    private readonly List<Squadron> _squadrons = new();
    public IReadOnlyList<Squadron> Squadrons => _squadrons;

    /// <summary>편대를 만든다. 구성원 순서가 선두 순서다(전투단은 전함을 먼저).</summary>
    public Squadron AddSquadron(string name, Faction faction, SquadronRole role, IEnumerable<ShipBody> members)
    {
        var squadron = new Squadron(name, faction, role, members);
        _squadrons.Add(squadron);
        return squadron;
    }

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
        if (brain.Order.Kind != OrderKind.Attack || !brain.Profile.AttackRuns)
        {
            brain.AimModule = null;
            brain.InRun = false;
        }

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

    /// <summary>침투 진입 구역: 표적 후미 아래쪽(로컬 방향), 이 각도 안이면 돌진한다.</summary>
    private static readonly Vector3 InfiltrationSector = new Vector3(0, -0.5f, 1f).Normalized();
    private const float InfiltrationConeDegrees = 35f;
    private const float InfiltrationEntryMeters = 3500f;

    /// <summary>
    /// 요격함: 접근 → 사격 → 이탈을 되풀이한다. 표적이 식별된 주력함이면 먼저 후미 아래쪽 진입 구역으로 돌아가
    /// (근접방어 사각·얇은 후미 장갑) 엔진, 엔진이 없으면 방열판을 노리고 돌진한다.
    /// </summary>
    private Vector3 AttackRun(ShipBrain brain, ShipBody target, SensorTrack track)
    {
        ShipBody ship = brain.Ship;
        float maxSpeed = ship.Class.MaxSpeed;
        Vector3 r = (track.EstimatedPosition - ship.Position).ToVector3();
        float d = Mathf.Max(r.Length(), 1f);
        Vector3 rh = r / d;
        Vector3 targetVelocity = track.Level >= TrackLevel.Identified ? target.Velocity : Vector3.Zero;
        float breakRange = 1500f + target.Hull.BoundingRadius;
        bool capital = target.Class.Kind != HullKind.Interceptor && track.Level >= TrackLevel.Identified;
        Vector3 sector = target.Orientation * InfiltrationSector;
        brain.AimModule = capital
            ? Subsystems.Pick(target, AimSubsystem.Engines, ship.Position) ?? Subsystems.Pick(target, AimSubsystem.Radiators, ship.Position)
            : null;

        if (Time < brain.BreakUntil)
        {
            brain.WantBoost = true;
            brain.Activity = "이탈";
            return brain.BreakDirection * maxSpeed * ship.Class.BoostMultiplier;
        }

        if (capital && !brain.InRun && d < 40_000f)
        {
            Vector3 fromTarget = -r;
            if (fromTarget.AngleTo(sector) > Mathf.DegToRad(InfiltrationConeDegrees))
            {
                // 진입점으로 우회. 곧장 가는 길이 표적 곁(진입 거리의 80% 안)을 스치면 바깥쪽 경유점을 먼저 거친다.
                float entry = target.Hull.BoundingRadius + InfiltrationEntryMeters;
                Vec3d entryPoint = track.EstimatedPosition + Vec3d.From(sector * entry);
                Vector3 toEntry = (entryPoint - ship.Position).ToVector3();
                float t = Mathf.Clamp(r.Dot(toEntry) / Mathf.Max(toEntry.LengthSquared(), 1f), 0f, 1f);
                if ((toEntry * t - r).Length() < InfiltrationEntryMeters * 0.8f)
                {
                    Vector3 outward = fromTarget - sector * fromTarget.Dot(sector);
                    outward = outward.LengthSquared() > 1f ? outward.Normalized() : sector.Cross(Vector3.Up).Normalized();
                    Vec3d wide = track.EstimatedPosition + Vec3d.From(outward * 8000f + sector * 4000f);
                    toEntry = (wide - ship.Position).ToVector3();
                }
                // 멀면 부스트, 가까우면 감속해서 경유점을 지나치지 않는다.
                float remaining = toEntry.Length();
                brain.WantBoost = remaining > 6000f;
                brain.Activity = "침투 우회";
                float speed = Mathf.Min(maxSpeed * (brain.WantBoost ? ship.Class.BoostMultiplier : 1f), Mathf.Max(80f, remaining * 0.25f));
                return targetVelocity + toEntry.Normalized() * speed;
            }
            brain.InRun = true;
        }

        if (d < breakRange)
        {
            // 주력함 침투 중이면 진입 구역 쪽으로, 아니면 표적 옆으로 빠진다(결정적 방향). 4초 뒤 다시 접근.
            Vector3 up = Mathf.Abs(rh.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up;
            Vector3 side = rh.Cross(up).Normalized() * brain._side;
            brain.BreakDirection = capital ? (sector + side * 0.6f).Normalized() : (side + rh * 0.3f).Normalized();
            brain.BreakUntil = Time + 4.0;
            brain.InRun = false;
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
        brain.Activity = capital ? "침투 돌진" : "사격 접근";
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
            FiringSolution solution = FireControl.Solve(ship, target, Time, track: track, localAim: brain.AimModule?.Definition.Center);
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
            HelmForward = aim,
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
            TryAutoFire(ship, target, brain.AimModule?.Definition.Center, brain.Profile.RailFlightSeconds, out _);
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

    private const double InterceptorEngageMeters = 60_000;
    /// <summary>요격 편대: 적 함대가 기함에서 이 거리 안이면 집결 후 돌격.</summary>
    private const double WingStrikeMeters = 50_000;
    private const double WingGatherMeters = 2_500;
    private const double WingGatherTimeout = 20;
    /// <summary>호위 전대: 적 요격함이 기함에서 이 거리 안이면 방공으로 돌아온다.</summary>
    private const double ScreenThreatMeters = 30_000;
    /// <summary>호위 전대 측면 기동점: 표적에서 우리 쪽으로 50 km, 옆으로 40 km.</summary>
    private const double FlankStandBack = 50_000, FlankSide = 40_000;
    /// <summary>호위 전대: 표적이 이 거리 안이면 측면 기동을 멈추고 공격.</summary>
    private const double FlankAttackMeters = 70_000;

    private void Command(Faction faction)
    {
        var known = _ships.Where(s => s.Faction != faction && !s.Damage.Destroyed)
            .Select(s => (Ship: s, Track: Sensors.Track(faction, s)))
            .Where(k => k.Track.Level >= TrackLevel.Contact)
            .ToList();
        var assigned = new Dictionary<ShipBody, int>();
        foreach (Squadron gone in _squadrons.Where(q => q.Faction == faction && q.Leader is null))
        {
            gone.Activity = "전멸";
            gone.Target = null;
        }
        var squadrons = _squadrons.Where(q => q.Faction == faction && q.Leader is not null).ToList();
        // 기함: 전투단 선두(없으면 아무 편대 선두). 다른 편대가 지키고 따르는 기준.
        ShipBody? flagship = squadrons.FirstOrDefault(q => q.Role == SquadronRole.BattleGroup)?.Leader
            ?? squadrons.FirstOrDefault()?.Leader;
        if (flagship is null) return;

        // 전투단부터 표적을 정해야 다른 편대가 흩어진다.
        foreach (Squadron squadron in squadrons.OrderBy(q => q.Role))
        {
            if (squadron.PlayerLed) continue;
            switch (squadron.Role)
            {
                case SquadronRole.BattleGroup: CommandBattleGroup(squadron, known, assigned); break;
                case SquadronRole.EscortSquadron: CommandEscortSquadron(squadron, flagship, known, assigned); break;
                default: CommandWing(squadron, flagship, known, assigned); break;
            }
        }

        // 편대에 속하지 않은 AI 함선: 함종 기본 행동.
        foreach (ShipBrain brain in _brains.Values.Where(b => b.Ship.Faction == faction && b.Ship.Squadron is null && b.Enabled))
        {
            ShipBody? target = PickTarget(brain.Ship, known, assigned);
            Assign(brain.Ship, target is null ? ShipOrder.HoldAt(brain.Ship.Position) : ShipOrder.AttackOn(target), assigned);
        }
    }

    /// <summary>전투단: 전함은 주 표적을 대치 포격, 나머지는 전함 옆 근접 대형에서 같은 표적을 쏜다.</summary>
    private void CommandBattleGroup(Squadron squadron, List<(ShipBody Ship, SensorTrack Track)> known, Dictionary<ShipBody, int> assigned)
    {
        ShipBody leader = squadron.Leader!;
        ShipBody? target = PickTarget(leader, known, assigned);
        squadron.Target = target;
        squadron.Activity = target is null ? "전진 대기" : "포격";
        Assign(leader, target is null ? ShipOrder.HoldAt(leader.Position) : ShipOrder.AttackOn(target), assigned);
        int slot = 0;
        foreach (ShipBody ship in squadron.ActiveMembers.Where(s => s != leader))
        {
            float side = slot % 2 == 0 ? 1f : -1f;
            float rank = 1 + slot / 2;
            SetFormation(ship, new Vector3(side * (leader.Class.Length * 0.6f + 900f) * rank, 0, -600f * rank));
            Assign(ship, ShipOrder.EscortOf(leader, target), assigned, count: false);
            slot++;
        }
    }

    /// <summary>호위 전대: 적 요격함이 기함에 붙으면 방공, 아니면 측면으로 돌아 적 호위함을 친다.</summary>
    private void CommandEscortSquadron(Squadron squadron, ShipBody flagship, List<(ShipBody Ship, SensorTrack Track)> known,
        Dictionary<ShipBody, int> assigned)
    {
        ShipBody leader = squadron.Leader!;
        var raiders = known.Where(k => k.Ship.Class.Kind == HullKind.Interceptor && k.Track.Level >= TrackLevel.Identified
                && !k.Ship.Damage.Disabled && (k.Track.EstimatedPosition - flagship.Position).Length() < ScreenThreatMeters)
            .OrderBy(k => (k.Track.EstimatedPosition - flagship.Position).Length()).ToList();
        int slot = 0;

        if (raiders.Count > 0 && flagship.Squadron != squadron)
        {
            // 방공: 기함 둘레 3 km에 펼쳐 서서 가까운 적 요격함부터 나눠 쏜다.
            squadron.Activity = "방공";
            squadron.Target = raiders[0].Ship;
            foreach (ShipBody ship in squadron.ActiveMembers)
            {
                float angle = Mathf.Tau * slot / Mathf.Max(1, squadron.ActiveMembers.Count());
                SetFormation(ship, new Vector3(Mathf.Cos(angle) * 3000f, 400f, Mathf.Sin(angle) * 3000f));
                Assign(ship, ShipOrder.EscortOf(flagship, raiders[slot % raiders.Count].Ship), assigned);
                slot++;
            }
            return;
        }

        ShipBody? target = PickTarget(leader, known, assigned);
        squadron.Target = target;
        if (target is null)
        {
            squadron.Activity = "기함 호위";
            foreach (ShipBody ship in squadron.ActiveMembers)
            {
                SetFormation(ship, Wedge(slot++, 1500f) + new Vector3(-6000f, 0, -2000f));
                Assign(ship, flagship.Squadron == squadron && ship == leader ? ShipOrder.HoldAt(ship.Position) : ShipOrder.EscortOf(flagship), assigned);
            }
            return;
        }

        Vec3d targetPos = Sensors.Track(squadron.Faction, target).EstimatedPosition;
        double range = (targetPos - leader.Position).Length();
        if (range < FlankAttackMeters)
        {
            // 공격: 전원 같은 표적(일제 사격·집중 포격).
            squadron.Activity = "측면 공격";
            foreach (ShipBody ship in squadron.ActiveMembers)
                Assign(ship, ShipOrder.AttackOn(target), assigned, count: ship == leader);
            return;
        }

        // 측면 기동: 기함-표적 선의 옆으로 돌아 들어가며, 사거리에 들면 미사일을 쏜다.
        squadron.Activity = "측면 기동";
        Vector3 line = (targetPos - flagship.Position).ToVector3();
        Vector3 forward = line.LengthSquared() > 1f ? line.Normalized() : Vector3.Forward;
        Vector3 up = Mathf.Abs(forward.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up;
        float side = (ShipBrain.Hash(squadron.Name) & 1) == 0 ? 1f : -1f;
        Vec3d flank = targetPos - Vec3d.From(forward * (float)FlankStandBack) + Vec3d.From(forward.Cross(up).Normalized() * (float)FlankSide * side);
        Assign(leader, ShipOrder.HoldAt(flank, target), assigned);
        foreach (ShipBody ship in squadron.ActiveMembers.Where(s => s != leader))
        {
            SetFormation(ship, Wedge(slot++, 1200f));
            Assign(ship, ShipOrder.EscortOf(leader, target), assigned, count: false);
        }
    }

    /// <summary>요격 편대: 적 요격함 요격 → (적 함대 근접 시) 집결 → 한 표적에 동시 돌격 → 경계.</summary>
    private void CommandWing(Squadron squadron, ShipBody flagship, List<(ShipBody Ship, SensorTrack Track)> known,
        Dictionary<ShipBody, int> assigned)
    {
        ShipBody leader = squadron.Leader!;
        var members = squadron.ActiveMembers.ToList();

        // 1) 요격: 60 km 안의 식별된 적 요격함을 나눠 맡는다.
        var enemyWing = known.Where(k => k.Ship.Class.Kind == HullKind.Interceptor && k.Track.Level >= TrackLevel.Identified
                && !k.Ship.Damage.Disabled && (k.Track.EstimatedPosition - leader.Position).Length() < InterceptorEngageMeters)
            .Select(k => k.Ship).ToList();
        if (enemyWing.Count > 0)
        {
            squadron.Activity = "요격";
            squadron.Target = enemyWing[0];
            squadron.Striking = false;
            var local = new Dictionary<ShipBody, int>();
            foreach (ShipBody ship in members)
            {
                ShipBody prey = enemyWing.OrderBy(e => local.GetValueOrDefault(e))
                    .ThenBy(e => (e.Position - ship.Position).Length()).First();
                local[prey] = local.GetValueOrDefault(prey) + 1;
                Assign(ship, ShipOrder.AttackOn(prey), assigned);
            }
            return;
        }

        // 2) 돌격 표적: 기함 50 km 안의 식별된 적 중 실드가 가장 약한 것(호위함 우선, 전함은 마지막).
        ShipBody? strike = known
            .Where(k => k.Track.Level >= TrackLevel.Identified && !k.Ship.Damage.Disabled
                && (k.Track.EstimatedPosition - flagship.Position).Length() < WingStrikeMeters)
            .OrderBy(k => k.Ship.Class.Kind == HullKind.Battleship ? 1 : 0)
            .ThenBy(k => k.Ship.Damage.ShieldCapacity > 0 ? k.Ship.Damage.Shield / k.Ship.Damage.ShieldCapacity : 0)
            .Select(k => k.Ship).FirstOrDefault();
        if (squadron.Striking && squadron.Target is ShipBody current && !current.Damage.Destroyed && !current.Damage.Disabled
            && Sensors.Track(squadron.Faction, current).Level >= TrackLevel.Identified)
            strike = current; // 돌격 중엔 표적을 바꾸지 않는다.

        if (strike is null)
        {
            // 3) 경계: 기함 앞 8 km에서 쐐기 대형.
            squadron.Activity = "전방 경계";
            squadron.Striking = false;
            squadron.GatherStarted = double.NegativeInfinity;
            squadron.Target = null;
            int slot = 0;
            foreach (ShipBody ship in members)
            {
                SetFormation(ship, new Vector3(0, 300f, -8000f) + Wedge(slot++, 150f));
                Assign(ship, flagship == ship ? ShipOrder.HoldAt(ship.Position) : ShipOrder.EscortOf(flagship), assigned);
            }
            return;
        }

        squadron.Target = strike;
        if (!squadron.Striking)
        {
            // 집결: 선두 곁으로 모인다. 다 모였거나 20초가 지나면 돌격.
            if (double.IsNegativeInfinity(squadron.GatherStarted)) squadron.GatherStarted = Time;
            bool gathered = members.All(s => (s.Position - leader.Position).Length() < WingGatherMeters);
            if (!gathered && Time - squadron.GatherStarted < WingGatherTimeout)
            {
                squadron.Activity = "집결";
                int slot = 0;
                Assign(leader, ShipOrder.HoldAt(leader.Position + Vec3d.From(leader.Velocity * 2f)), assigned);
                foreach (ShipBody ship in members.Where(s => s != leader))
                {
                    SetFormation(ship, Wedge(slot++, 150f));
                    Assign(ship, ShipOrder.EscortOf(leader), assigned);
                }
                return;
            }
            squadron.Striking = true;
        }
        squadron.Activity = "돌격";
        foreach (ShipBody ship in members)
            Assign(ship, ShipOrder.AttackOn(strike), assigned, count: ship == leader);
    }

    /// <summary>쐐기 대형 자리: 선두 뒤로 좌우 번갈아(로컬 +Z가 뒤).</summary>
    private static Vector3 Wedge(int slot, float spacing)
    {
        int rank = 1 + slot / 2;
        float side = slot % 2 == 0 ? 1f : -1f;
        return new Vector3(side * spacing * rank, 0, spacing * 0.7f * rank);
    }

    private void SetFormation(ShipBody ship, Vector3 offset)
    {
        if (_brains.TryGetValue(ship, out ShipBrain? brain))
            brain.FormationOffset = offset;
    }

    /// <summary>명령을 바꾼다. 같은 명령이면 그대로 둔다(위치 유지 지점이 2초마다 밀리지 않게).</summary>
    private void Assign(ShipBody ship, ShipOrder order, Dictionary<ShipBody, int> assigned, bool count = true)
    {
        if (count && order.Kind == OrderKind.Attack && order.Target is ShipBody target)
            assigned[target] = assigned.GetValueOrDefault(target) + 1;
        if (!_brains.TryGetValue(ship, out ShipBrain? brain) || !brain.Enabled) return;
        bool moved = order.Kind == OrderKind.Hold && (order.Point - brain.Order.Point).Length() > 2000;
        if (order.Kind != brain.Order.Kind || order.Target != brain.Order.Target || order.FireAt != brain.Order.FireAt || moved)
            brain.Order = order;
    }
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
