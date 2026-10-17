using System;
using System.Collections.Generic;
using System.Linq;

namespace SpaceFleet.Sim;

/// <summary>Battle victory condition, independent from Earth/Mars hull designs and Blue/Red teams.</summary>
public enum BattleObjective { Annihilation, Flagship, Domination }

public sealed record BattleOutcome(Faction? Winner, string Reason, double Time);

/// <summary>
/// A spherical, world-space control area. Signed progress: +100 Blue, -100 Red.
/// Ownership persists after ships leave; an attacker interrupts scoring immediately.
/// A contested area cannot change hands or award points.
/// </summary>
public sealed class CaptureZone
{
    public const double CaptureRatePerSecond = 2.5;
    public string Name { get; }
    public Vec3d Position { get; }
    public double RadiusMeters { get; }
    public double Progress { get; private set; }
    public Faction? Owner { get; private set; }
    public bool Contested { get; private set; }
    public double BluePresence { get; private set; }
    public double RedPresence { get; private set; }
    public Faction? ScoringOwner => Contested || (Owner == Faction.Blue && RedPresence > 0)
        || (Owner == Faction.Red && BluePresence > 0) ? null : Owner;

    public CaptureZone(string name, Vec3d position, double radiusMeters = 12_000)
    {
        if (string.IsNullOrWhiteSpace(name) || !double.IsFinite(radiusMeters) || radiusMeters <= 0
            || !double.IsFinite(position.X) || !double.IsFinite(position.Y) || !double.IsFinite(position.Z))
            throw new ArgumentException("Invalid capture zone");
        Name = name;
        Position = position;
        RadiusMeters = radiusMeters;
    }

    public void Update(IReadOnlyList<ShipBody> ships, double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        BluePresence = RedPresence = 0;
        double radiusSquared = RadiusMeters * RadiusMeters;
        foreach (ShipBody ship in ships)
        {
            if (!Squadron.Active(ship) || (ship.Position - Position).LengthSquared() > radiusSquared) continue;
            // Caps prevent a cluster of tiny craft from capturing instantaneously.
            double strength = ship.Definition.BattleValue.CaptureStrength;
            if (ship.Faction == Faction.Blue) BluePresence += strength;
            else RedPresence += strength;
        }
        Contested = BluePresence > 0 && RedPresence > 0;
        if (Contested || (BluePresence <= 0 && RedPresence <= 0)) return;

        double direction = BluePresence > 0 ? 1 : -1;
        double amount = Math.Min(3, Math.Max(BluePresence, RedPresence)) * CaptureRatePerSecond * seconds;
        Progress = Math.Clamp(Progress + direction * amount, -100, 100);
        if (direction > 0)
        {
            if (Progress >= 100) Owner = Faction.Blue;
            else if (Progress <= 0) Owner = null;
        }
        else
        {
            if (Progress <= -100) Owner = Faction.Red;
            else if (Progress >= 0) Owner = null;
        }
    }
}

public sealed class BattleRules
{
    // Legacy accessors resolve the default Earth hull; runtime values belong to each definition.
    public static double BattleshipWeight => Weight(HullKind.Battleship);
    public static double EscortWeight => Weight(HullKind.Escort);
    public static double InterceptorWeight => Weight(HullKind.Interceptor);
    public const double DefeatFraction = 0.25, FlagshipDefeatFraction = 0.5;
    public const double TimeLimitSeconds = 25 * 60, DrawMargin = 0.1;
    public const int DefaultDominationScore = 500;
    public const double ZoneSpacing = 60_000;
    private readonly SimWorld _world;
    private readonly double[] _initial = new double[2];
    private readonly ShipBody?[] _flagships = new ShipBody?[2];
    private readonly double[] _scores = new double[2];
    private double _lastEvaluatedTime;

    public BattleObjective Objective { get; }
    public IReadOnlyList<CaptureZone> CaptureZones { get; }
    public int ScoreLimit { get; }
    public BattleOutcome? Outcome { get; private set; }
    public ShipBody? Flagship(Faction faction) => _flagships[(int)faction];
    public double Score(Faction faction) => _scores[(int)faction];

    public static string Name(BattleObjective objective) => objective switch
    {
        BattleObjective.Flagship => "기함격침전",
        BattleObjective.Domination => "점령전",
        _ => "섬멸전",
    };

    public static CaptureZone[] DefaultZones(double startDistance, Vec3d redOffset)
    {
        if (!double.IsFinite(startDistance) || startDistance <= 0)
            throw new ArgumentOutOfRangeException(nameof(startDistance));
        // 두 함대 접근축(Z)에 수직으로 나란히 놓아 양측 거리를 같게 한다. 거점 간격 60 km, 높이는 서로 다르게
        // (A 위, B 아래, C 약간 위) 둬서 함대가 평면 위에서만 움직이지 않게 한다. 나중에 거점 위치에 구조물을 놓는다.
        Vec3d center = new(redOffset.X * .5, redOffset.Y * .5, (redOffset.Z - startDistance) * .5);
        return new[]
        {
            new CaptureZone("A", center + new Vec3d(-ZoneSpacing, 14_000, 0)),
            new CaptureZone("B", center + new Vec3d(0, -9_000, 0)),
            new CaptureZone("C", center + new Vec3d(ZoneSpacing, 5_000, 0)),
        };
    }

    public BattleRules(SimWorld world, BattleObjective objective = BattleObjective.Annihilation,
        IEnumerable<CaptureZone>? zones = null, int scoreLimit = DefaultDominationScore,IReadOnlyList<ShipBody>? flagships=null)
    {
        if (!Enum.IsDefined(objective) || scoreLimit <= 0)
            throw new ArgumentException("Invalid battle objective configuration");
        _world = world;
        Objective = objective;
        ScoreLimit = scoreLimit;
        CaptureZones = objective == BattleObjective.Domination
            ? Array.AsReadOnly((zones ?? DefaultZones(150_000, Vec3d.Zero)).ToArray())
            : Array.Empty<CaptureZone>();
        if (objective == BattleObjective.Domination && CaptureZones.Count == 0)
            throw new ArgumentException("Domination requires at least one zone");
        foreach (Faction f in Enum.GetValues<Faction>())
        {
            _initial[(int)f] = Strength(world.Ships, f);
            _flagships[(int)f] = world.Squadrons.FirstOrDefault(q => q.Faction == f && q.Role == SquadronRole.BattleGroup)?.Members
                .FirstOrDefault(s => s.Class.Kind == HullKind.Battleship)
                ??flagships?.FirstOrDefault(s=>s.Faction==f);
        }
        _lastEvaluatedTime = world.Time;
    }

    public static double Weight(HullKind kind) => Weight(ShipDefinitions.For(kind));
    public static double Weight(ShipDefinition definition) => definition.BattleValue.Weight;
    public static double Strength(IEnumerable<ShipBody> ships, Faction faction) => ships
        .Where(s => s.Faction == faction && !s.Damage.Destroyed && !s.Damage.Disabled).Sum(s => Weight(s.Definition));
    public double Initial(Faction f) => _initial[(int)f];
    public static ShipBody? Replacement(IEnumerable<ShipBody> ships, Faction faction, HullKind preferred) => ships
        .Where(s => s.Faction == faction && Squadron.Active(s)).OrderBy(s => s.Class.Kind == preferred ? 0 : 1)
        .ThenBy(s => s.Class.Kind).FirstOrDefault();
    public double Fraction(Faction f) => Initial(f) > 0 ? Strength(_world.Ships, f) / Initial(f) : 0;

    public BattleOutcome? Evaluate(double time)
    {
        if (Outcome is not null) return Outcome;
        // 응급복구로 무력화를 풀고 있는 함선은 아직 잃은 전력으로 치지 않는다.
        double Recoverable(Faction f) => Strength(_world.Ships, f)
            + _world.Ships.Where(s => s.Faction == f && s.DamageControl.RecoveringDisable).Sum(s => Weight(s.Definition));
        double RecoverableFraction(Faction f) => Initial(f) <= 0 ? 0 : Recoverable(f) / Initial(f);
        bool FlagshipDown(Faction f) => _flagships[(int)f] is { } flagship
            && (flagship.Damage.Destroyed || flagship.Damage.Disabled && !flagship.DamageControl.RecoveringDisable);

        if (Objective == BattleObjective.Domination)
        {
            // Capped step ensures external Evaluate(timeLimit) checks cannot grant 25 minutes of free score.
            double dt = Math.Clamp(time - _lastEvaluatedTime, 0, SimWorld.TickDelta);
            _lastEvaluatedTime = time;
            foreach (CaptureZone zone in CaptureZones)
            {
                zone.Update(_world.Ships, dt);
                if (zone.ScoringOwner is Faction owner) _scores[(int)owner] += dt;
            }
            bool blueEmpty = Recoverable(Faction.Blue) <= 0;
            bool redEmpty = Recoverable(Faction.Red) <= 0;
            if (blueEmpty || redEmpty)
                Outcome = new(blueEmpty && redEmpty ? null : blueEmpty ? Faction.Red : Faction.Blue,
                    blueEmpty && redEmpty ? "양측 전력 상실" : "전력 전멸", time);
            else if (_scores[0] >= ScoreLimit || _scores[1] >= ScoreLimit)
                Outcome = new(Math.Abs(_scores[0] - _scores[1]) < 1e-6 ? null : _scores[0] > _scores[1] ? Faction.Blue : Faction.Red,
                    "거점 점수 달성", time);
            else if (time >= TimeLimitSeconds)
            {
                double difference = _scores[0] - _scores[1];
                if (Math.Abs(difference) < 1e-6)
                {
                    int blueZones = CaptureZones.Count(z => z.Owner == Faction.Blue);
                    int redZones = CaptureZones.Count(z => z.Owner == Faction.Red);
                    difference = blueZones - redZones;
                    if (difference == 0) difference = Fraction(Faction.Blue) - Fraction(Faction.Red);
                }
                Outcome = new(Math.Abs(difference) < 1e-6 ? null : difference > 0 ? Faction.Blue : Faction.Red,
                    "점령전 시간 제한", time);
            }
            return Outcome;
        }

        bool blue, red;
        if (Objective == BattleObjective.Flagship)
        {
            // 기함전: 기함 격침, 또는 응급복구 없이 무력화된 기함은 즉시 패배.
            bool Lost(Faction f) => Recoverable(f) <= 0 || FlagshipDown(f);
            blue = Lost(Faction.Blue);
            red = Lost(Faction.Red);
        }
        else
        {
            bool Lost(Faction f) => RecoverableFraction(f) <= DefeatFraction
                || FlagshipDown(f) && RecoverableFraction(f) <= FlagshipDefeatFraction;
            blue = Lost(Faction.Blue);
            red = Lost(Faction.Red);
        }
        if (blue || red)
            Outcome = new(blue && red ? null : blue ? Faction.Red : Faction.Blue,
                blue && red ? "동시 전력 상실" : Objective == BattleObjective.Flagship ? "기함 상실" : "전력 상실", time);
        else if (time >= TimeLimitSeconds)
        {
            double difference = Fraction(Faction.Blue) - Fraction(Faction.Red);
            Outcome = new(Math.Abs(difference) <= DrawMargin ? null : difference > 0 ? Faction.Blue : Faction.Red,
                "시간 제한", time);
        }
        return Outcome;
    }
}
