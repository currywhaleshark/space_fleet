using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

/// <summary>Opening geometry. Auto keeps the canonical fixture head-on and draws one of the four for matches.</summary>
public enum Engagement { Auto, HeadOn, Parallel, Opposite, Vertical }

/// <summary>
/// Start geometry and role-based opening positions.
/// The second team's origin stays RedOffset + (0, 0, -StartDistance), so terrain, navigation bounds and capture
/// zones follow it. Each mode rewrites only those two values and the course, and keeps the opening range near
/// the requested StartDistance.
/// </summary>
public static class Deployment
{
    public static readonly Engagement[] Modes = { Engagement.HeadOn, Engagement.Parallel, Engagement.Opposite, Engagement.Vertical };

    public static Engagement Resolve(BattleConfig config) => config.Engagement != Engagement.Auto ? config.Engagement
        : config.Match is null ? Engagement.HeadOn : Modes[new Random(unchecked(config.Seed * 7919 + 17)).Next(Modes.Length)];

    /// <summary>Concrete config for the resolved mode (idempotent). Opposite courses pass 0.45 R apart; the high team sits 0.45 R up.</summary>
    public static BattleConfig Apply(BattleConfig config)
    {
        if (config.DeploymentApplied) return config;
        var mode = Resolve(config);
        float range = (float)config.StartDistance;
        return mode switch
        {
            Engagement.Opposite => config with { Engagement = mode, DeploymentApplied = true, StartDistance = range * .45, RedOffset = config.RedOffset + new Vector3(range * .89f, 0, 0) },
            Engagement.Vertical => config with { Engagement = mode, DeploymentApplied = true, StartDistance = range * .89, RedOffset = config.RedOffset + new Vector3(0, range * .45f, 0) },
            _ => config with { Engagement = mode, DeploymentApplied = true },
        };
    }

    public static Vec3d Origin(BattleConfig config, int side)
        => side == 0 ? Vec3d.Zero : Vec3d.From(config.RedOffset) + new Vec3d(0, 0, -config.StartDistance);

    /// <summary>Course (ship forward) for one side of an applied config.</summary>
    public static Vector3 Course(BattleConfig config, int side) => config.Engagement switch
    {
        Engagement.Parallel => Vector3.Right,
        Engagement.Opposite => side == 0 ? Vector3.Right : Vector3.Left,
        _ => Threat(config, side),
    };

    /// <summary>Unit direction toward the other team's origin.</summary>
    public static Vector3 Threat(BattleConfig config, int side)
        => (Origin(config, 1 - side) - Origin(config, side)).ToVector3().Normalized();

    /// <summary>Opening attitude: forward along the course, roll level with world up.</summary>
    public static Quaternion Facing(BattleConfig config, int side, float yawJitter)
    {
        var basis = Basis.LookingAt(Course(config, side), Vector3.Up);
        return new Quaternion(basis) * new Quaternion(Vector3.Up, yawJitter);
    }

    /// <summary>
    /// World offset of a deployment-frame point. In that frame -Z is the threat axis, +X runs across it level with
    /// world up, and +Z is the rear.
    /// </summary>
    public static Vec3d Place(BattleConfig config, int side, Vec3d local)
    {
        Vector3 back = -Threat(config, side);
        Vector3 across = Vector3.Up.Cross(back);
        across = across.LengthSquared() < 1e-6f ? Vector3.Right : across.Normalized();
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
