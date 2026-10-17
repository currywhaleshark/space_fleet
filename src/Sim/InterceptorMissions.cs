using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

public enum InterceptorMission { Recon, AirDefense, Superiority, Assault, Return }
public static class InterceptorMissions
{
    public static string Label(InterceptorMission mission) => mission switch
    { InterceptorMission.Recon => "정찰", InterceptorMission.AirDefense => "방공", InterceptorMission.Superiority => "제공",
        InterceptorMission.Assault => "강습", _ => "복귀" };
}

public sealed partial class SimWorld
{
    private double _nextWingUpdate;
    public void OrderWing(Squadron wing, InterceptorMission? mission, ShipBody? target = null, Vector3? direction = null)
    {
        if (!_squadrons.Contains(wing) || wing.Role != SquadronRole.InterceptorWing) return;
        if (mission is not null && (mission != InterceptorMission.Assault
            || target is not null && target != wing.AssaultCommittedTarget)) wing.AssaultCommittedTarget = null;
        wing.OrderedMission = mission; wing.MissionTarget = target;
        wing.ReconDirection = direction is { } d && d.IsFinite() && d.LengthSquared() > .01f ? d.Normalized() : null;
        wing.MissionSince = double.NegativeInfinity;
        UpdateWing(wing);
    }

    private void StepWingMissions()
    {
        if (Time < _nextWingUpdate) return;
        _nextWingUpdate = Time + .5;
        foreach (var wing in _squadrons)
            if (wing.Role == SquadronRole.InterceptorWing && !wing.ObjectiveOrders && (_commanders.Contains(wing.Faction)
                || wing.PlayerLed && wing.OrderedMission is not null)) UpdateWing(wing);
    }

    private ShipBody WingAnchor(Squadron wing, ShipBody leader)
    {
        if (wing.MissionTarget is { } requested && requested.Faction == wing.Faction
            && Squadron.Active(requested) && requested.Class.Kind != HullKind.Interceptor) return requested;
        return _ships.Where(s => s.Faction == wing.Faction && Squadron.Active(s) && s.Class.Kind != HullKind.Interceptor)
            .OrderBy(s => s.Class.Kind == HullKind.Battleship ? 0 : 1)
            .ThenBy(s => (s.Position - leader.Position).LengthSquared()).FirstOrDefault() ?? leader;
    }

    private void UpdateWing(Squadron wing)
    {
        if (wing.Leader is not { } leader) { wing.Activity = "전멸"; return; }
        var members = wing.ActiveMembers.ToArray();
        ShipBody anchor = WingAnchor(wing, leader);
        wing.ProtectedShip = anchor;
        var known = _ships.Where(s => s.Faction != wing.Faction)
            .Select(s => (Ship: s, Track: Sensors.Track(wing.Faction, s)))
            .Where(k => k.Track.Level >= TrackLevel.Identified && !k.Track.ObservedDestroyed && !k.Track.ObservedDisabled).ToArray();
        var raiders = known.Where(k => k.Ship.Class.Kind == HullKind.Interceptor
            && (k.Track.EstimatedPosition - anchor.Position).Length() < 22_000)
            .OrderBy(k => (k.Track.EstimatedPosition - anchor.Position).LengthSquared()).ToArray();
        var threats = Sensors.SmallContacts(wing.Faction).Where(t => t.Fresh(Time)).ToArray();
        // Do not abandon another mission for a missile this wing cannot intercept.
        // Late/pass-through threats belong to the escort and capital defense layers.
        bool incoming = threats.Any(t => t.Kind == SmallContactKind.Missile && Threatens(t, anchor, out _)
            && members.Any(s => TryDefenseIntercept(s, anchor, t, out _)));
        var capitals = known.Where(k => k.Ship.Class.Kind != HullKind.Interceptor).ToArray();
        bool mars = leader.Definition.Design == DesignFamily.Mars;
        bool scout = leader.Definition.Branch == ShipBranch.Recon;
        // Planning distance only: observed screen openings, readiness and weapon ranges still gate an attack.
        double assaultReach = leader.Definition.Ai.AssaultReachMeters > 0
            ? leader.Definition.Ai.AssaultReachMeters : mars ? 100_000 : 85_000;
        var strike = capitals.Where(k => (k.Track.EstimatedPosition - anchor.Position).Length() < assaultReach)
            .OrderBy(k => k.Track.ObservedShieldFraction)
            .ThenBy(k => (k.Track.EstimatedPosition - leader.Position).LengthSquared()).FirstOrDefault();
        if (wing.Mission == InterceptorMission.Assault && wing.Target is { } committed)
        {
            var previous = capitals.FirstOrDefault(k => k.Ship == committed);
            if (previous.Ship is not null) strike = previous;
        }
        if (wing.MissionTarget is { } assignedEnemy && assignedEnemy.Faction != wing.Faction)
            strike = capitals.FirstOrDefault(k => k.Ship == assignedEnemy);
        int cover = strike.Ship is null ? int.MaxValue : known.Count(k => k.Ship != strike.Ship && k.Ship.Class.Kind == HullKind.Escort
            && (k.Track.EstimatedPosition - strike.Track.EstimatedPosition).Length() < 18_000);
        bool heavyLoss = members.Length * 2 < wing.Members.Count;
        // A brief occlusion retains navigation only; firing still needs a live observation.
        ShipBody? remembered = wing.Target is { } old && Sensors.Memory(wing.Faction,old,Time) is not null ? old : null;
        bool armed = members.Any(s => s.Ordnance.Antimatter.Rounds > 0 && !s.Ordnance.Antimatter.Warning && !WingMemberImpaired(s));
        if (heavyLoss || !armed || wing.AssaultCommittedTarget is { } committedTarget
            && strike.Ship != committedTarget && remembered != committedTarget) wing.AssaultCommittedTarget = null;
        bool committedRun = wing.AssaultCommittedTarget is not null;
        bool stocks = members.Any(s => s.Ordnance.Interceptor.Probes > 0);
        bool deployed = _probes.Any(p => p.Active && p.Owner.Squadron == wing);
        // A dispersed escort screen can leave a usable rear/ventral approach even while its ships survive.
        // Only sampled poses and identified blueprints are used; no enemy ammo or internal health inspection.
        Vec3d entry = strike.Ship is null ? Vec3d.Zero : strike.Track.EstimatedPosition + Vec3d.From(strike.Track.ObservedOrientation * InfiltrationSector) * 18_000;
        bool flankOpen = strike.Ship is not null && strike.Track.Level >= TrackLevel.Identified
            && known.Where(k => k.Ship != strike.Ship && k.Ship.Class.Kind == HullKind.Escort)
                .All(k => (k.Track.EstimatedPosition - entry).Length() > (mars ? 13_000 : 16_000));
        wing.AssaultAvailable = armed && !heavyLoss && strike.Ship is not null && (cover == 0
            || cover == 1 && strike.Track.ObservedShieldFraction < .3f || flankOpen);
        bool intelPoor = capitals.Length == 0 || capitals.All(k => k.Track.Level < TrackLevel.Locked);
        InterceptorMission recommendation = heavyLoss ? InterceptorMission.Return
            : scout ? InterceptorMission.Recon
            : incoming ? InterceptorMission.AirDefense
            : raiders.Length > 0 ? InterceptorMission.Superiority
            : stocks && !deployed && intelPoor ? InterceptorMission.Recon
            : wing.AssaultAvailable ? InterceptorMission.Assault
            : threats.Any(t => t.Kind == SmallContactKind.Probe && (t.Position - anchor.Position).Length() < 20_000)
                ? InterceptorMission.Superiority : InterceptorMission.AirDefense;
        InterceptorMission desired = wing.OrderedMission ?? (committedRun ? InterceptorMission.Assault
            : !scout && remembered is not null && !heavyLoss && !incoming && raiders.Length==0
            && wing.Mission is InterceptorMission.Assault or InterceptorMission.Superiority ? wing.Mission : recommendation);
        // Explicit commands persist. Automatic choices dwell for 12 s; reachable
        // defensive threats can interrupt before an assault commits, and heavy losses always can.
        bool urgent = wing.OrderedMission is null && (incoming || heavyLoss || raiders.Length > 0);
        if (double.IsNegativeInfinity(wing.MissionSince) || desired != wing.Mission && (urgent || Time - wing.MissionSince >= 12))
        { wing.Mission = desired; wing.MissionSince = Time; wing.MissionChanges++; }
        wing.Activity = InterceptorMissions.Label(wing.Mission);
        wing.Target = null;
        Vector3 direction = wing.ReconDirection ?? (capitals.Length > 0
            ? (capitals[0].Track.EstimatedPosition - anchor.Position).ToVector3().Normalized() : anchor.Forward);
        Vector3 lateral = direction.Cross(Mathf.Abs(direction.Y) > .9f ? Vector3.Right : Vector3.Up).Normalized();
        Vec3d rally=anchor.Position+Vec3d.From(direction*6500+lateral*4500);
        if(strike.Ship is not null&&(rally-strike.Track.EstimatedPosition).Length()<22_000)
            rally=anchor.Position+Vec3d.From(-direction*4500+lateral*4500);
        bool gathering=PrepareAssaultRally(wing,leader,members,strike.Ship,rally,anchor.Velocity);
        if(gathering)wing.Activity=$"강습 집결 {wing.RallyReady}/{wing.RallyRequired}";
        var assigned = new Dictionary<ShipBody, int>();
        var usedThreats = new HashSet<object>();
        for (int i = 0; i < members.Length; i++)
        {
            var ship = members[i];
            var brain = BrainOf(ship);
            if (brain is null || !brain.Enabled) continue; // Player body never receives AI controls.
            brain.SmallTarget = null;
            brain.StationVelocity = Vector3.Zero;
            float side = i % 2 == 0 ? 1 : -1;
            Vec3d station = anchor.Position + Vec3d.From(direction * 7500 + lateral * side * (1000 + i * 450) + Vector3.Up * (i % 3 - 1) * 650);
            bool impaired = WingMemberImpaired(ship);
            bool exhausted = ship.Railguns.All(g => !g.HasAmmunition) && ship.Ordnance.Missiles == 0 && ship.Ordnance.Antimatter.Rounds == 0;
            var mission = impaired || exhausted ? InterceptorMission.Return : wing.Mission;
            if (mission == InterceptorMission.Assault && ship.Ordnance.Antimatter.Rounds == 0) mission = InterceptorMission.AirDefense;
            ShipOrder order = ShipOrder.HoldAt(station);
            if(gathering&&mission==InterceptorMission.Assault)
                order=ShipOrder.HoldAt(rally+Vec3d.From(lateral*side*(i/2+1)*250));
            else if (remembered is not null && mission is InterceptorMission.Assault or InterceptorMission.Superiority)
            { order=ShipOrder.AttackOn(remembered); wing.Target=remembered; }
            else if (mission == InterceptorMission.Recon)
            {
                bool reconHull=ship.Definition.Branch==ShipBranch.Recon;
                float forward = reconHull ? 16_000 : ship.Definition.Design == DesignFamily.Mars ? 12_000 : 8500;
                station = anchor.Position + Vec3d.From(direction * forward + lateral * side * (3000 + i * 1000));
                // Do not fly into a known capital's defense bubble while deploying a sensor.
                if (capitals.Any(k => (station - k.Track.EstimatedPosition).Length() < 20_000))
                    station = reconHull ? anchor.Position+Vec3d.From(lateral*side*(6000+i*700)-direction*3000) : ship.Position;
                if(reconHull&&known.Any(k=>(k.Track.EstimatedPosition-ship.Position).Length()<11_000))
                    station=anchor.Position+Vec3d.From(lateral*side*(6000+i*700)-direction*6000);
                order = ShipOrder.HoldAt(station);
                bool covered = _probes.Any(p => p.Active && p.Owner.Squadron == wing
                    && (p.Destination - (ship.Position + Vec3d.From(direction) * p.Definition.DeploymentMeters)).Length() < 12_000);
                if ((ship.Position - station).Length() < 1500) ship.Ordnance.Interceptor.FirstAction ??= Time;
                bool usefulArea = capitals.Length == 0 || capitals.Any(k => (k.Track.EstimatedPosition - ship.Position).Length()
                    < (ship.Definition.ReconProbes?.DeploymentMeters ?? 0) + 36_000);
                if (!covered && usefulArea && ship.Ordnance.Interceptor.Probes > 0 && Time >= ship.Ordnance.Interceptor.NextLaunch)
                {
                    Vector3 throwDirection = (direction + lateral * side * .25f).Normalized();
                    LaunchProbe(ship, throwDirection);
                }
            }
            else if (mission == InterceptorMission.AirDefense)
            {
                foreach (var threat in threats.Where(t => t.Kind == SmallContactKind.Missile && !usedThreats.Contains(t.Target))
                    .OrderBy(t => (t.Predict(Time) - anchor.Position).LengthSquared()))
                {
                    if (!TryDefenseIntercept(ship, anchor, threat, out Vec3d crossing)) continue;
                    usedThreats.Add(threat.Target); brain.SmallTarget = threat.Target;
                    order = ShipOrder.HoldAt(crossing); break;
                }
            }
            else if (mission == InterceptorMission.Superiority)
            {
                if (raiders.Length > 0)
                { var prey = raiders[i % raiders.Length]; order = ShipOrder.AttackOn(prey.Ship); wing.Target = prey.Ship; }
                else
                {
                    var probe = threats.Where(t => t.Kind == SmallContactKind.Probe && (t.Position - anchor.Position).Length() < 20_000)
                        .OrderBy(t => (t.Position - ship.Position).LengthSquared()).FirstOrDefault();
                    if (probe.Target is not null) { brain.SmallTarget = probe.Target; order = ShipOrder.HoldAt(probe.Predict(Time)); }
                }
            }
            else if (mission == InterceptorMission.Assault && strike.Ship is not null && armed)
            {
                // Player may order a risky assault; automatic assault requires the observed screen opening.
                if (wing.OrderedMission is not null || wing.AssaultAvailable || committedRun)
                {
                    order = ShipOrder.AttackOn(strike.Ship); wing.Target = strike.Ship;
                    wing.AssaultCommittedTarget = strike.Ship;
                    ship.Ordnance.Interceptor.FirstAssault ??= Time;
                }
            }
            else if (mission == InterceptorMission.Return)
            {
                brain.FormationOffset = new Vector3(side * (750 + i * 180), (i % 3 - 1) * 250, 2200);
                order = anchor == ship ? ShipOrder.HoldAt(ship.Position) : ShipOrder.EscortOf(anchor);
            }
            if (mission != InterceptorMission.Assault) ship.Ordnance.Antimatter.Cancel();
            Assign(ship, order, assigned);
            // Hold points must move with the protected fleet, unlike ordinary fixed hold commands.
            if (order.Kind == OrderKind.Hold)
            {
                brain.Order = order;
                if (brain.SmallTarget is null)
                {
                    brain.StationVelocity = anchor.Velocity;
                    if (mission is InterceptorMission.Recon or InterceptorMission.AirDefense or InterceptorMission.Superiority
                        && (order.Point - ship.Position).Length() < 1500) ship.Ordnance.Interceptor.FirstAction ??= Time;
                }
            }
        }
    }

    private bool Threatens(SmallContact threat, ShipBody anchor, out double arrival)
    {
        Vec3d r = threat.Predict(Time) - anchor.Position;
        Vec3d velocity = Vec3d.From(threat.Velocity - anchor.Velocity);
        arrival = velocity.LengthSquared() > 1 ? -r.Dot(velocity) / velocity.LengthSquared() : -1;
        return arrival > 0 && arrival < 35 && (r + velocity * arrival).Length() < 6500;
    }

    private static bool WingMemberImpaired(ShipBody ship) => ship.Damage.PropulsionFraction < .25f
        || ship.Damage.WeaponsFraction < .1f
        || ship.Damage.Modules.Sum(m => m.Health) < ship.Damage.Modules.Sum(m => m.Definition.HitPoints) * .35f;

    public bool TryDefenseIntercept(ShipBody ship, ShipBody anchor, SmallContact threat, out Vec3d point)
    {
        point = ship.Position;
        if (threat.Kind != SmallContactKind.Missile || !threat.Fresh(Time) || !Threatens(threat, anchor, out double arrival)) return false;
        Vec3d current = threat.Predict(Time);
        // Already passed the defender on its inward leg: let the escort/PD layer handle it.
        if ((current - ship.Position).Dot(Vec3d.From(threat.Velocity - ship.Velocity)) >= 0) return false;
        double accel = Math.Min(ship.Class.ForwardAccel, ship.Class.StrafeAccel) * ship.Damage.PropulsionFraction;
        double maxSpeed = ship.Class.MaxSpeed;
        float gunRange = ship.Definition.Design == DesignFamily.Mars ? 4000 : 5500;
        for (double t = .5; t <= Math.Min(14, arrival); t += .5)
        {
            Vec3d predicted = current + Vec3d.From(threat.Velocity) * t;
            if ((predicted - anchor.Position).Length() > 22_000) continue;
            double reach = Math.Min(maxSpeed * t, .5 * accel * t * t);
            Vec3d fromCoast = predicted - ship.Position - Vec3d.From(ship.Velocity) * t;
            if (fromCoast.Length() > reach + gunRange) continue;
            point = predicted;
            return true;
        }
        return false;
    }

    private bool EngageSmallTarget(ShipBrain brain)
    {
        var ship = brain.Ship;
        if (brain.SmallTarget is null || Sensors.SmallTrack(ship.Faction, brain.SmallTarget) is not { } track || !track.Fresh(Time)) return false;
        bool fired = false;
        if (track.Target is Missile missile && ship.Ordnance.MissileReady
            && !_missiles.Exists(m => m.Faction == ship.Faction && m.InterceptTarget == missile && !m.InterceptPassed)
            && (track.Predict(Time) - ship.Position).Length() > 2500)
            fired = LaunchCounterMissile(ship, missile).Fired;
        if (SmallTargetAim(ship, track, out var direction, out var aim) && !TerrainBlocked(ship.Railgun!.MuzzlePosition,aim)
            && ship.Forward.AngleTo(direction) <= Mathf.DegToRad(9)
            && !FriendlyInLine(ship, ship.Railgun!.MuzzlePosition, direction, (float)(aim - ship.Position).Length()))
            fired |= FireRailguns(ship, direction).Fired;
        if (fired) ship.Ordnance.Interceptor.FirstAction ??= Time;
        return fired;
    }
}
