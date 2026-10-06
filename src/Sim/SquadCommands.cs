using System;
using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

public enum SquadCommand { Escort, Intercept, Focus, Return, Hold }

/// <summary>플레이어 편대 명령을 편대원 두뇌의 명령으로 풀어 준다. 시뮬레이션 층이라 검사에서도 그대로 돌린다.</summary>
public static class SquadCommands
{
    public static string Label(SquadCommand command) => command switch
    { SquadCommand.Intercept => "요격", SquadCommand.Focus => "집중공격", SquadCommand.Return => "복귀", SquadCommand.Hold => "위치 유지", _ => "호위" };

    public static void Apply(SimWorld world, ShipBody leader, SquadCommand command, ShipBody? target = null, ShipBody? fireAt = null)
    {
        if (leader.Squadron is null) return;
        var members = world.Brains.Values.Where(b => b.Ship.Squadron == leader.Squadron && b.Ship != leader && !b.Ship.Damage.Destroyed)
            .OrderBy(b => b.Ship.Callsign, StringComparer.Ordinal).ToArray();
        var threats = command == SquadCommand.Intercept ? world.Ships
            .Where(s => s.Faction != leader.Faction && !s.Damage.Destroyed && s.Class.Kind == HullKind.Interceptor)
            .Select(s => (Ship: s, Track: world.Sensors.Track(leader.Faction, s)))
            .Where(s => s.Track.Level >= TrackLevel.Identified && (s.Track.EstimatedPosition - leader.Position).Length() <= 20_000)
            .OrderBy(s => (s.Track.EstimatedPosition - leader.Position).LengthSquared())
            .ThenBy(s => s.Ship.Callsign, StringComparer.Ordinal).Select(s => s.Ship).ToArray() : Array.Empty<ShipBody>();
        for (int i = 0; i < members.Length; i++)
        {
            ShipBrain brain = members[i];
            ShipBody ship = brain.Ship;
            float angle = Mathf.Tau * i / members.Length;
            float radius = (leader.Class.Length + ship.Class.Length) * 0.6f + 500;
            brain.FormationOffset = new Vector3(Mathf.Cos(angle) * radius, (i % 2 == 0 ? 1 : -1) * radius * 0.15f, Mathf.Sin(angle) * radius);
            brain.Order = command switch
            {
                SquadCommand.Focus when target is { Damage.Destroyed: false } && target.Faction != leader.Faction => ShipOrder.AttackOn(target),
                SquadCommand.Intercept when threats.Length > 0 => ShipOrder.AttackOn(threats[i % threats.Length]),
                SquadCommand.Hold => ShipOrder.HoldAt(ship.Position),
                SquadCommand.Return => ShipOrder.EscortOf(leader),
                _ => ShipOrder.EscortOf(leader, fireAt),
            };
        }
    }
}
