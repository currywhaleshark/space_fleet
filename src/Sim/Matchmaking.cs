using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

/// <summary>A participant owns one immutable squad template, independently of team/design.</summary>
public sealed record MatchParticipant(int Slot, Faction Team, string HullId, bool IsPlayer)
{
    public ShipDefinition Hull => ShipDefinitions.ById(HullId);
    public HullKind Kind => Hull.Kind;
    public string Id => $"{(Team == Faction.Blue ? "B" : "R")}{Slot:00}";
    public string Name => IsPlayer ? "나" : $"AI {Id}";
    public string SquadronName => $"{Id} 편대";
    public string Callsign(int member) => $"{Id}-{member + 1}";
    public int ShipCount => Kind switch { HullKind.Battleship => 3, HullKind.Escort => 4, _ => 5 };
    public SquadronRole Role => Kind switch
    { HullKind.Battleship => SquadronRole.BattleGroup, HullKind.Escort => SquadronRole.EscortSquadron, _ => SquadronRole.InterceptorWing };
    public string Composition => Kind switch
    { HullKind.Battleship => "전함 1 + 호위함 2", HullKind.Escort => "호위함 4", _ => "요격함 5" };
}

public sealed class MatchLineup
{
    public IReadOnlyList<MatchParticipant> Participants { get; }
    public MatchParticipant Player => Participants.Single(p => p.IsPlayer);
    public const int TeamSize = 10;
    internal MatchLineup(IEnumerable<MatchParticipant> participants) => Participants = Array.AsReadOnly(participants.ToArray());
}

public static class Matchmaking
{
    public static MatchLineup Create(int seed, string playerHullId)
    {
        var player = ShipDefinitions.ById(playerHullId);
        var random = new Random(seed);
        // Each class has 2..4 participants. Vary the mix between matches, while
        // matching class/design counts across teams so random fills cannot stack BBs.
        int battleships, escorts, interceptors;
        do { battleships = random.Next(2, 5); escorts = random.Next(2, 5); interceptors = 10 - battleships - escorts; }
        while (interceptors is < 2 or > 4);
        var hulls = new List<string>();
        foreach (var (kind, count) in new[] { (HullKind.Battleship, battleships), (HullKind.Escort, escorts), (HullKind.Interceptor, interceptors) })
            for (int i = 0; i < count; i++)
                hulls.Add(ShipDefinitions.For(kind, random.Next(2) == 0 ? DesignFamily.Earth : DesignFamily.Mars).Id);
        int chosen = hulls.FindIndex(id => ShipDefinitions.ById(id).Kind == player.Kind);
        hulls[chosen] = player.Id;
        var blue = hulls.ToArray();
        (blue[0], blue[chosen]) = (blue[chosen], blue[0]);
        Shuffle(blue, 1, random);
        var red = hulls.ToArray(); Shuffle(red, 0, random);
        return new MatchLineup(blue.Select((id, i) => new MatchParticipant(i + 1, Faction.Blue, id, i == 0))
            .Concat(red.Select((id, i) => new MatchParticipant(i + 1, Faction.Red, id, false))));
    }

    private static void Shuffle<T>(T[] items, int first, Random random)
    {
        for (int i = items.Length - 1; i > first; i--) { int j = random.Next(first, i + 1); (items[i], items[j]) = (items[j], items[i]); }
    }

    internal static BattleRoster Spawn(SimWorld world, BattleConfig config)
    {
        var match = config.Match!;
        var random = new Random(config.Seed);
        var ships = new List<ShipBody>();
        var flagships = new ShipBody[2];
        foreach (var entry in match.Participants)
        {
            int side = (entry.Team == Faction.Blue ? 0 : 1) ^ (config.Mirror ? 1 : 0);
            double dir = side == 0 ? 1 : -1;
            Vec3d origin = side == 0 ? Vec3d.Zero : Vec3d.From(config.RedOffset) + new Vec3d(0, 0, -config.StartDistance);
            int slot = entry.Slot - 1;
            Vec3d center = new((slot % 5 - 2) * 12000, slot / 5 * 6000 - 3000, slot / 5 * 8000);
            center += new Vec3d(random.NextDouble() - .5, random.NextDouble() - .5, random.NextDouble() - .5) * config.PositionJitter;
            var pose = new Quaternion(Vector3.Up, (side == 0 ? 0 : Mathf.Pi) + Mathf.DegToRad((float)(random.NextDouble() - .5) * config.HeadingJitterDegrees));
            var members = new List<ShipBody>();
            for (int i = 0; i < entry.ShipCount; i++)
            {
                HullKind kind = entry.Kind == HullKind.Battleship && i > 0 ? HullKind.Escort : entry.Kind;
                var hull = i == 0 ? entry.Hull : ShipDefinitions.For(kind, entry.Hull.Design);
                var offset = entry.Kind switch
                {
                    HullKind.Battleship => i == 0 ? Vec3d.Zero : new Vec3d(i == 1 ? -1500 : 1500, i == 1 ? 150 : -150, -600),
                    HullKind.Escort => new Vec3d((i % 2 == 0 ? -1 : 1) * 1200 * (i / 2 + 1), 0, 900 * (i / 2)),
                    _ => new Vec3d(60 * i, -25 * i, 50 * i),
                };
                var p = center + offset;
                var ship = world.Add(new ShipBody(entry.Callsign(i), hull, entry.Team));
                ship.Place(origin + new Vec3d(p.X * dir, p.Y, p.Z * dir), pose);
                world.AttachBrain(ship, ShipOrder.HoldAt(ship.Position));
                members.Add(ship); ships.Add(ship);
                if (kind == HullKind.Battleship && flagships[(int)entry.Team] is null) flagships[(int)entry.Team] = ship;
            }
            world.AddSquadron(entry.SquadronName, entry.Team, entry.Role, members);
        }
        foreach (var squad in world.Squadrons)
        {
            if (squad.Role == SquadronRole.InterceptorWing) continue;
            squad.SupportGroup = squad.Role == SquadronRole.BattleGroup ? squad : world.Squadrons
                .Where(q => q.Faction == squad.Faction && q.Role == SquadronRole.BattleGroup)
                .OrderBy(q => (q.Leader!.Position - squad.Leader!.Position).LengthSquared()).First();
            if (squad.Role == SquadronRole.EscortSquadron)
            {
                int lane = world.Squadrons.TakeWhile(q => q != squad)
                    .Count(q => q.Role == SquadronRole.EscortSquadron && q.SupportGroup == squad.SupportGroup);
                squad.SupportOffset = new Vector3(0, lane * 5000, 0);
            }
        }
        world.Doctrine = new FleetDoctrine();
        world.EnableCommander(Faction.Red); world.EnableCommander(Faction.Blue);
        world.Rules = new BattleRules(world, config.Objective,
            config.Objective == BattleObjective.Domination ? BattleRules.DefaultZones(config.StartDistance, Vec3d.From(config.RedOffset)) : null);
        world.PopulateBattlefield(config);
        world.Sensors.Update(world.Ships, world.Time, force: true);
        world.Log = new BattleLog(world);
        world.Log.Environment.NavigationBuildMs = world.NavigationBuildMilliseconds;
        return new BattleRoster(ships.AsReadOnly(), flagships[0], flagships[1]);
    }
}
