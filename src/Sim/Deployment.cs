using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

/// <summary>Opening course relation. Auto keeps the canonical fixture head-on and draws one for matches.</summary>
public enum Engagement { Auto, HeadOn, Parallel, Opposite }

/// <summary>Opening height relation, combined with any Engagement. Overhead puts each fleet above the other's head.</summary>
public enum Elevation { Auto, Level, Overhead }

/// <summary>
/// Start geometry and role-based opening positions.
/// The second team's origin stays RedOffset + (0, 0, -StartDistance), so terrain, navigation bounds and capture
/// zones follow it. Each mode rewrites only those two values and the courses; the opening range stays near the
/// requested StartDistance.
/// - Level, head-on: the other fleet is ahead. Parallel: abeam, same course. Opposite: ahead and 0.45 R abeam on
///   the reverse course, so the fleets close and pass.
/// - Overhead keeps the courses but turns the separation that level play puts ahead or abeam straight up
///   (opposite courses keep their along-track offset). The upper fleet flies inverted, so for both crews the other
///   fleet is over their heads.
/// </summary>
public static class Deployment
{
    public static readonly Engagement[] Modes = { Engagement.HeadOn, Engagement.Parallel, Engagement.Opposite };
    public static readonly Elevation[] Heights = { Elevation.Level, Elevation.Overhead };

    /// <summary>Auto values: head-on and level for the canonical fixture; one seeded draw per match (mirror keeps it).</summary>
    public static (Engagement Course, Elevation Height) Resolve(BattleConfig config)
    {
        var random = new Random(unchecked(config.Seed * 7919 + 17));
        Engagement course = Modes[random.Next(Modes.Length)]; Elevation height = Heights[random.Next(Heights.Length)];
        bool match = config.Match is not null;
        return (config.Engagement != Engagement.Auto ? config.Engagement : match ? course : Engagement.HeadOn,
            config.Elevation != Elevation.Auto ? config.Elevation : match ? height : Elevation.Level);
    }

    /// <summary>Concrete config for the resolved opening (idempotent).</summary>
    public static BattleConfig Apply(BattleConfig config)
    {
        if (config.DeploymentApplied) return config;
        var (course, height) = Resolve(config);
        float range = (float)config.StartDistance;
        config = config with { Engagement = course, Elevation = height, DeploymentApplied = true };
        if (height == Elevation.Overhead)
            // Origin = RedOffset + (0, 0, -R): cancel the Z step and lift the separation.
            return course == Engagement.Opposite
                ? config with { RedOffset = config.RedOffset + new Vector3(range * .89f, range * .45f, range) }
                : config with { RedOffset = config.RedOffset + new Vector3(0, range, range) };
        return course == Engagement.Opposite
            ? config with { StartDistance = range * .45, RedOffset = config.RedOffset + new Vector3(range * .89f, 0, 0) }
            : config;
    }

    public static Vec3d Origin(BattleConfig config, int side)
        => side == 0 ? Vec3d.Zero : Vec3d.From(config.RedOffset) + new Vec3d(0, 0, -config.StartDistance);

    /// <summary>Course (ship forward) for one side of an applied config.</summary>
    public static Vector3 Course(BattleConfig config, int side) => config.Engagement switch
    {
        Engagement.Parallel => Vector3.Right,
        Engagement.Opposite => side == 0 ? Vector3.Right : Vector3.Left,
        _ => side == 0 ? Vector3.Forward : Vector3.Back,
    };

    /// <summary>Unit direction toward the other team's origin.</summary>
    public static Vector3 Threat(BattleConfig config, int side)
        => (Origin(config, 1 - side) - Origin(config, side)).ToVector3().Normalized();

    /// <summary>Opening attitude: forward along the course; the upper fleet of an overhead opening flies inverted.</summary>
    public static Quaternion Facing(BattleConfig config, int side, float yawJitter)
    {
        bool inverted = config.Elevation == Elevation.Overhead && Threat(config, side).Y < 0;
        var basis = Basis.LookingAt(Course(config, side), inverted ? Vector3.Down : Vector3.Up);
        return new Quaternion(basis) * new Quaternion(Vector3.Up, yawJitter);
    }

    /// <summary>
    /// World offset of a deployment-frame point. In that frame -Z is the threat axis, +X runs across it level with
    /// world up (across the course when the threat is vertical), and +Z is the rear.
    /// </summary>
    public static Vec3d Place(BattleConfig config, int side, Vec3d local)
    {
        Vector3 back = -Threat(config, side);
        Vector3 across = Vector3.Up.Cross(back);
        // Straight up or down (overhead): spread the ranks across the course instead.
        across = across.LengthSquared() < 1e-6f ? Course(config, side).Cross(Vector3.Up).Normalized() : across.Normalized();
        Vector3 up = back.Cross(across);
        return Vec3d.From(across) * local.X + Vec3d.From(up) * local.Y + Vec3d.From(back) * local.Z;
    }

    /// <summary>
    /// Squad centres by role (deployment frame, metres):
    /// - Recon wings scout the far flanks; interceptor and assault wings lead.
    /// - Gun and escort cruisers hold the centre; missile cruisers stand behind them.
    /// - Battle groups form the rear line at the team origin, and air-defence cruisers screen ahead of their battleship's flank.
    /// </summary>
    public static Vec3d[] RoleSlots(IReadOnlyList<MatchParticipant> team)
    {
        var slots = new Vec3d[team.Count];
        void Rank(IEnumerable<int> members, double z, double spacing, double rise)
        {
            var list = members.ToArray();
            for (int i = 0; i < list.Length; i++)
                slots[list[i]] = new Vec3d((i - (list.Length - 1) * .5) * spacing, (i % 2 == 0 ? 1 : -1) * rise, z);
        }
        int[] Where(Func<MatchParticipant, bool> predicate) => team.Select((p, i) => (p, i)).Where(x => predicate(x.p)).Select(x => x.i).ToArray();
        bool Recon(MatchParticipant p) => p.Role == SquadronRole.InterceptorWing && p.Hull.Branch == ShipBranch.Recon;
        var battleGroups = Where(p => p.Role == SquadronRole.BattleGroup);
        var screens = Where(p => p.Role == SquadronRole.AirDefenseSquadron);
        // Anchored on the battle line: battleships open at the team origin (the same 150 km battleship-to-battleship
        // range as before), so a deeper formation never pushes their first targets out of gun flight time.
        Rank(battleGroups, 0, 20000, 1500);
        Rank(Where(p => p.Role == SquadronRole.InterceptorWing && !Recon(p)), -23000, 12000, 2000);
        Rank(Where(p => p.Role is SquadronRole.GunCruiserSquadron or SquadronRole.EscortSquadron
            || (p.Role == SquadronRole.AirDefenseSquadron && battleGroups.Length == 0)), -14000, 14000, 2500);
        Rank(Where(p => p.Role == SquadronRole.MissileSquadron), -9000, 14000, 3000);
        var recon = Where(Recon);
        for (int i = 0; i < recon.Length; i++)
            slots[recon[i]] = new Vec3d((i % 2 == 0 ? -1 : 1) * (26000 + 10000 * (i / 2)), (i % 2 == 0 ? 4000 : -4000), -29000);
        if (battleGroups.Length > 0)
            for (int i = 0; i < screens.Length; i++)
            {
                // Round-robin over battle groups; alternate flanks so two screens bracket one battleship.
                var bb = slots[battleGroups[i % battleGroups.Length]];
                int flank = (i / battleGroups.Length) % 2 == 0 ? 1 : -1;
                slots[screens[i]] = bb + new Vec3d(flank * 7000, 3000, -4500);
            }
        return slots;
    }
}
