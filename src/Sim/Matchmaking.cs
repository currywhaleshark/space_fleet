using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

/// <summary>A participant owns one immutable squad template, independently of team/design.</summary>
public sealed record MatchParticipant(int Slot, Faction Team, string HullId, bool IsPlayer, ResolvedSquadron? Loadout = null,
    double MatchPoints=0,int Tier=0,IReadOnlyList<int>? MemberTiers=null)
{
    public ShipDefinition Hull => ShipDefinitions.ById(HullId);
    public HullKind Kind => Hull.Kind;
    public string Id => $"{(Team == Faction.Blue ? "B" : "R")}{Slot:00}";
    public string Name => IsPlayer ? "나" : $"AI {Id}";
    public string SquadronName => $"{Id} 편대";
    public string Callsign(int member) => $"{Id}-{member + 1}";
    public int ShipCount => Hull.Squadron.Members.Count;
    public SquadronRole Role => Hull.Squadron.Role;
    public string Composition => Loadout?.Composition ?? Hull.Squadron.Composition;
}

public sealed class MatchLineup
{
    public int? Tier { get; internal init; }
    public int? LowTier { get; internal init; }
    public int? HighTier { get; internal init; }
    public IReadOnlyList<MatchParticipant> Participants { get; }
    public MatchParticipant Player => Participants.Single(p => p.IsPlayer);
    public const int TeamSize = 10;
    internal MatchLineup(IEnumerable<MatchParticipant> participants) => Participants = Array.AsReadOnly(participants.ToArray());
}

public static partial class Matchmaking
{
    /// <summary>Hangar matches freeze every member's validated loadout before the countdown.</summary>
    public static MatchLineup Create(int seed,SquadronLoadout playerLoadout,IReadOnlySet<string>? owned=null)
    {
        var resolved=FleetLoadouts.Resolve(playerLoadout,owned);
        var legacy=Create(seed,resolved.Leader.Id);
        var random=new Random(seed^0x521A4);
        var presets=new Dictionary<(string,int),ResolvedSquadron>();
        var participants=legacy.Participants.Select(p=>
        {
            if(p.IsPlayer)return p with{Loadout=resolved};
            var candidates=FleetLoadouts.Hulls.Where(h=>h.Design==p.Hull.Design&&h.MatchClass==p.Hull.MatchClass).ToArray();
            var hull=candidates[random.Next(candidates.Length)];int preset=random.Next(3);
            if(!presets.TryGetValue((hull.Id,preset),out var fit))
                presets[(hull.Id,preset)]=fit=FleetLoadouts.Resolve(FleetLoadouts.AiPreset(hull.Id,preset));
            return p with{HullId=hull.Id,Loadout=fit};
        }).ToArray();
        return new(participants);
    }

    public static MatchLineup Create(int seed, string playerHullId)
    {
        var player = ShipDefinitions.ById(playerHullId);
        var random = new Random(seed);
        // Each class has 2..4 participants. Vary the mix between matches, while
        // matching class/design counts across teams so random fills cannot stack BBs.
        int large, medium, small;
        do { large = random.Next(2, 5); medium = random.Next(2, 5); small = MatchLineup.TeamSize - large - medium; }
        while (small is < 2 or > 4);
        var hulls = new List<string>();
        foreach (var (size, count) in new[] { (MatchWeightClass.Large, large), (MatchWeightClass.Medium, medium), (MatchWeightClass.Small, small) })
            for (int i = 0; i < count; i++)
            {
                var design = random.Next(2) == 0 ? DesignFamily.Earth : DesignFamily.Mars;
                hulls.Add(ShipDefinitions.All.Single(h => h.MatchClass == size && h.Design == design).Id);
            }
        int chosen = hulls.FindIndex(id => ShipDefinitions.ById(id).MatchClass == player.MatchClass);
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
                var member = entry.Hull.Squadron.Members[i];
                var resolved=entry.Loadout?.Members[i];
                var hull = resolved?.Definition ?? (member.HullId is { } id ? ShipDefinitions.ById(id) : entry.Hull);
                var p = center + Vec3d.From(member.Offset);
                var ship = world.Add(new ShipBody(entry.Callsign(i), hull, entry.Team) { LoadoutId=resolved?.LoadoutId });
                ship.Place(origin + new Vec3d(p.X * dir, p.Y, p.Z * dir), pose);
                world.AttachBrain(ship, ShipOrder.HoldAt(ship.Position));
                members.Add(ship); ships.Add(ship);
                if (i == 0 && entry.Role == SquadronRole.BattleGroup && flagships[(int)entry.Team] is null) flagships[(int)entry.Team] = ship;
            }
            world.AddSquadron(entry.SquadronName, entry.Team, entry.Role, members);
        }
        foreach (var squad in world.Squadrons)
        {
            if (squad.Role == SquadronRole.InterceptorWing) continue;
            squad.SupportGroup = squad.Role == SquadronRole.BattleGroup ? squad : world.Squadrons
                .Where(q => q.Faction == squad.Faction && q.Role == SquadronRole.BattleGroup)
                .OrderBy(q => (q.Leader!.Position - squad.Leader!.Position).LengthSquared()).FirstOrDefault() ?? squad;
            if (Squadron.IsSupportRole(squad.Role))
            {
                int lane = world.Squadrons.TakeWhile(q => q != squad)
                    .Count(q => Squadron.IsSupportRole(q.Role) && q.SupportGroup == squad.SupportGroup);
                squad.SupportOffset = new Vector3(0, lane * 5000, 0);
            }
        }
        world.Doctrine = new FleetDoctrine();
        foreach(Faction team in Enum.GetValues<Faction>())
            flagships[(int)team]??=ships.Where(s=>s.Faction==team).OrderByDescending(s=>s.Definition.BattleValue.Weight).First();
        world.EnableCommander(Faction.Red); world.EnableCommander(Faction.Blue);
        world.Rules = new BattleRules(world, config.Objective,
            config.Objective == BattleObjective.Domination ? BattleRules.DefaultZones(config.StartDistance, Vec3d.From(config.RedOffset)) : null,flagships:flagships);
        world.PopulateBattlefield(config);
        world.Sensors.Update(world.Ships, world.Time, force: true);
        world.Log = new BattleLog(world);
        world.Log.Environment.NavigationBuildMs = world.NavigationBuildMilliseconds;
        return new BattleRoster(ships.AsReadOnly(), flagships[0], flagships[1]);
    }
}
