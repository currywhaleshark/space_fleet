using System;
using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

public sealed partial class SimWorld
{
    /// <summary>
    /// Capture-mode orders remain inside the ordinary command/AI pipeline.
    /// Squadrons manoeuvre to world-space points rather than chasing unobserved enemies.
    /// The closest *known* enemy is a defensive fire target, never a navigation oracle.
    /// Player-led squads retain their own commands.
    /// </summary>
    /// <summary>거점 중심 구조물(반지름 약 2 km)과 겹치지 않게, 중심에서 이만큼 자기 쪽으로 물러나 머문다. 점령 반경(12 km) 안이다.</summary>
    public const double HoldClearance = 4_000, WingExtraClearance = 3_000;

    /// <summary>거점 중심에서 함선 쪽 방향으로 <see cref="HoldClearance"/>만큼 떨어진 대기점. 중심에 겹쳐 있으면 자기 진영 출발 쪽.</summary>
    private static Vec3d HoldPoint(CaptureZone zone, Vec3d from, Faction faction)
    {
        Vec3d away = from - zone.Position;
        double length = away.Length();
        Vec3d dir = length > 1 ? away * (1 / length) : new Vec3d(0, 0, faction == Faction.Blue ? 1 : -1);
        return zone.Position + dir * HoldClearance;
    }

    private void CommandCaptureZones(Faction faction)
    {
        if (Rules is not { Objective: BattleObjective.Domination } rules) return;
        foreach (Squadron squad in _squadrons.Where(s => s.Faction == faction && !s.PlayerLed && s.Leader is not null))
        {
            int index = squad.Role switch
            {
                SquadronRole.BattleGroup => 1,
                var role when Squadron.IsSupportRole(role) => faction == Faction.Blue ? 0 : 2,
                _ => 1,
            };
            CaptureZone zone = rules.CaptureZones[Math.Min(index, rules.CaptureZones.Count - 1)];
            ShipBody leader = squad.Leader!;
            // Prefer the assigned sector; reinforce another if it becomes friendly and uncontested.
            if (zone.Owner == faction && zone.ScoringOwner == faction)
            {
                var needed = rules.CaptureZones.Where(z => z.Owner != faction)
                    .OrderBy(z => (z.Position - leader.Position).LengthSquared()).FirstOrDefault();
                if (needed is not null && (zone.Position - leader.Position).Length() < zone.RadiusMeters)
                    zone = needed;
            }
            var known = _ships.Where(s => s.Faction != faction && Squadron.Active(s))
                .Select(s => (Ship: s, Track: Sensors.Track(faction, s)))
                .Where(x => x.Track.Level >= TrackLevel.Contact)
                .OrderBy(x => (x.Track.EstimatedPosition - zone.Position).LengthSquared()).ToList();
            ShipBody? threat = squad.Target is { } remembered && Sensors.Memory(faction,remembered,Time) is not null
                ? remembered : known.FirstOrDefault().Ship;
            // Use stand-off anchor offsets so friendly ships do not all aim at the identical point.
            int slot = 0;
            // 같은 거점에 모인 편대끼리 겹치지 않게 요격 편대는 전투단보다 바깥(+3 km)에 머문다.
            Vec3d anchor = HoldPoint(zone, leader.Position, faction);
            if (squad.Role == SquadronRole.InterceptorWing) anchor += (anchor - zone.Position) * (WingExtraClearance / HoldClearance);
            Vec3d outward = (anchor - zone.Position) * (1 / (anchor - zone.Position).Length());
            foreach (ShipBody ship in squad.ActiveMembers)
            {
                Vector3 offset = squad.Role switch
                {
                    // 간격은 선체 길이(전함 1.2 km)보다 넉넉히: 경계 상자가 겹친 채 정지하면 매 틱 정밀 충돌 검사가 돈다.
                    SquadronRole.BattleGroup => new Vector3((slot % 2 == 0 ? 1 : -1) * (slot / 2 + 1) * 2_000, 0, 0),
                    var role when Squadron.IsSupportRole(role) => new Vector3((slot % 2 == 0 ? 1 : -1) * (slot / 2 + 1) * 1_400, 400, 0),
                    _ => new Vector3((slot % 2 == 0 ? 1 : -1) * (slot / 2 + 1) * 500, -300, 0),
                };
                if (ship == leader) offset = Vector3.Zero;
                // 대형 간격은 대기점의 접평면으로만: 구조물 쪽으로 파고들지 않는다.
                Vec3d spread = Vec3d.From(offset);
                spread -= outward * spread.Dot(outward);
                Assign(ship, ShipOrder.HoldAt(anchor + spread, threat), new System.Collections.Generic.Dictionary<ShipBody, int>(), false);
                slot++;
            }
            squad.Target = threat;
            squad.Activity = "거점 " + zone.Name;
            squad.ObjectiveOrders = true;
        }
        // Ships outside a squadron also contribute to the closest objective.
        foreach (ShipBrain brain in _brains.Values.Where(b => b.Ship.Faction == faction && b.Ship.Squadron is null && b.Enabled))
        {
            CaptureZone zone = rules.CaptureZones.OrderBy(z => (z.Position - brain.Ship.Position).LengthSquared()).First();
            brain.Order = ShipOrder.HoldAt(HoldPoint(zone, brain.Ship.Position, faction));
            brain.CommandStandoff = null;
        }
    }
    private void CommandFlagshipObjective(Faction faction)
    {
        if (Rules is not { Objective: BattleObjective.Flagship } rules) return;
        Faction other = faction == Faction.Blue ? Faction.Red : Faction.Blue;
        ShipBody? enemy = rules.Flagship(other);
        if (enemy is null || enemy.Damage.Destroyed || enemy.Damage.Disabled) return;
        SensorTrack track = Sensors.Track(faction, enemy);
        if (track.Level < TrackLevel.Identified) return; // Never reveal unobserved flagship identities.
        foreach (Squadron squad in _squadrons.Where(s => s.Faction == faction && !s.PlayerLed && s.Leader is not null))
        {
            if (squad.Role == SquadronRole.InterceptorWing &&
                (track.EstimatedPosition - squad.Leader!.Position).Length() > 40_000) continue;
            var assigned = new System.Collections.Generic.Dictionary<ShipBody, int>();
            ShipBody? friendlyFlagship = rules.Flagship(faction);
            foreach (ShipBody ship in squad.ActiveMembers)
            {
                ShipOrder order = squad.Role == SquadronRole.BattleGroup && ship != friendlyFlagship
                    && friendlyFlagship is not null && Squadron.Active(friendlyFlagship)
                    ? ShipOrder.EscortOf(friendlyFlagship, enemy) : ShipOrder.AttackOn(enemy);
                Assign(ship, order, assigned);
            }
            squad.Target = enemy;
            squad.Activity = "기함 공격";
            squad.ObjectiveOrders = true;
        }
    }

}
