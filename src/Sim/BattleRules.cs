using System;
using System.Collections.Generic;
using System.Linq;

namespace SpaceFleet.Sim;

public sealed record BattleOutcome(Faction? Winner, string Reason, double Time);

public sealed class BattleRules
{
    // Stage 7 tuning values: keep victory thresholds separate from flight/damage physics.
    public const double BattleshipWeight = 6, EscortWeight = 2, InterceptorWeight = 0.5;
    public const double DefeatFraction = 0.25, FlagshipDefeatFraction = 0.5;
    public const double TimeLimitSeconds = 25 * 60, DrawMargin = 0.1;
    private readonly SimWorld _world;
    private readonly double[] _initial = new double[2];
    private readonly ShipBody?[] _flagships = new ShipBody?[2];
    public BattleOutcome? Outcome { get; private set; }
    public BattleRules(SimWorld world)
    {
        _world = world;
        foreach (Faction f in Enum.GetValues<Faction>())
        {
            _initial[(int)f] = Strength(world.Ships, f);
            _flagships[(int)f] = world.Squadrons.FirstOrDefault(q => q.Faction == f && q.Role == SquadronRole.BattleGroup)?.Members
                .FirstOrDefault(s => s.Class.Kind == HullKind.Battleship);
        }
    }
    public static double Weight(HullKind kind) => kind switch
    { HullKind.Battleship => BattleshipWeight, HullKind.Escort => EscortWeight, _ => InterceptorWeight };
    public static double Strength(IEnumerable<ShipBody> ships, Faction faction) => ships
        .Where(s => s.Faction == faction && !s.Damage.Destroyed && !s.Damage.Disabled).Sum(s => Weight(s.Class.Kind));
    public double Initial(Faction f) => _initial[(int)f];
    public double Fraction(Faction f) => Initial(f) > 0 ? Strength(_world.Ships, f) / Initial(f) : 0;
    public BattleOutcome? Evaluate(double time)
    {
        if (Outcome is not null) return Outcome;
        bool Lost(Faction f) => Fraction(f) <= DefeatFraction ||
            (_flagships[(int)f] is { } flagship && (flagship.Damage.Destroyed || flagship.Damage.Disabled) && Fraction(f) <= FlagshipDefeatFraction);
        bool blue = Lost(Faction.Blue), red = Lost(Faction.Red);
        if (blue || red) Outcome = new(blue && red ? null : blue ? Faction.Red : Faction.Blue,
            blue && red ? "동시 전력 상실" : "전력 상실", time);
        else if (time >= TimeLimitSeconds)
        {
            double difference = Fraction(Faction.Blue) - Fraction(Faction.Red);
            Outcome = new(Math.Abs(difference) <= DrawMargin ? null : difference > 0 ? Faction.Blue : Faction.Red,
                "시간 제한", time);
        }
        return Outcome;
    }
}
