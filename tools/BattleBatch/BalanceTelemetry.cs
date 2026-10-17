using System.Globalization;
using SpaceFleet.Sim;

// Opt-in, read-only snapshots for balance diagnosis. Never attempt a shot or change an AI order.
// Sampled eligibility describes this instant; it is not a count of failed firing attempts.
sealed class BalanceTelemetry(int seed, bool mirror)
{
    private double next;
    public List<string> Rows { get; } = new();
    public const string Header = "seed,mirror,time,faction,design,kind,callsign,active,activity,posture,order,target,targetDisabled,track,rangeKm,minFlightSeconds,flightEligibleGuns,arcEligibleGuns,clearLineGuns,armorEligibleGuns,alignedGuns,readyGuns,rails,railHits,railRounds,missiles,torpedoes,amRounds,amMode,containment,amFailures,amJettisons,shieldFraction,moduleFraction,propulsion,weapons,sensors,heat,supply,pips,speed,nearestEscortKm,nearestEnemyKm,shieldDamageInflicted,moduleDamageInflicted,missilesIntercepted,torpedoesIntercepted,dronesDestroyed,repairs,repairKits,repairModule,repairProgress,wingMission,wingChanges,probesLaunched,probesActive,probeContacts,probeIdentifications,probeKills,smallTarget,trackSnr,observer,observerRangeKm,eccm,boost,desiredSpeed";
    public void Capture(SimWorld world)
    {
        if (world.Time < next) return;
        next += 5;
        foreach (var ship in world.Ships)
        {
            var brain = world.BrainOf(ship);
            var target = brain?.Target;
            var track = target is null ? SensorTrack.Unknown : world.Sensors.Track(ship.Faction, target);
            var stats = world.Log!.Ship(ship);
            var am = ship.Ordnance.Antimatter;
            var solved = target is null ? Array.Empty<(RailgunState Gun, FiringSolution Solution)>() : ship.Railguns
                .Select(g => (Gun: g, Solution: FireControl.Solve(ship, target, world.Time, track: track,
                    localAim: brain?.AimModule?.Definition.Center, weapon: g))).ToArray();
            var eligible = solved.Where(s => s.Solution.Valid && s.Solution.FlightTime <= brain!.Profile.RailFlightSeconds).ToArray();
            double? Nearest(Func<ShipBody, bool> predicate) => world.Ships.Where(s => s != ship && Squadron.Active(s) && predicate(s))
                .Select(s => (double?)((s.Position - ship.Position).Length() / 1000)).Min();
            string F(double? value) => value?.ToString("0.000", CultureInfo.InvariantCulture) ?? "";
            string B(bool value) => value ? "1" : "0";
            Rows.Add(string.Join(',', new[] {
                seed.ToString(), B(mirror), F(world.Time), ship.Faction.ToString(), ship.Definition.Design.ToString(), ship.Class.Kind.ToString(), ship.Callsign,
                B(Squadron.Active(ship)), brain?.Activity ?? "", world.Fleet(ship.Faction).Posture.ToString(), brain?.Order.Kind.ToString() ?? "",
                target?.Callsign ?? "", B(target?.Damage.Disabled == true), track.Level.ToString(),
                target is null || track.Level == TrackLevel.None ? "" : F((track.EstimatedPosition - ship.Position).Length() / 1000),
                F(solved.Where(s => s.Solution.Valid).Select(s => (double?)s.Solution.FlightTime).Min()), eligible.Length.ToString(),
                eligible.Count(s => s.Gun.Mount?.Contains(ship.Orientation.Inverse() * s.Solution.Direction) ?? true).ToString(),
                eligible.Count(s => SimWorld.RailLineLocal(ship, s.Gun, ship.Orientation.Inverse() * s.Solution.Direction) == FireFailure.None).ToString(),
                eligible.Count(s => target!.Damage.Shield > 1 || DamageRay.PreviewArmor(target, s.Gun.MuzzlePosition,
                    s.Solution.Direction, s.Gun.Definition.PenetrationMm, out _)).ToString(),
                eligible.Count(s => s.Gun.Aligned(s.Solution.Direction)).ToString(), ship.Railguns.Count(g => g.Ready).ToString(),
                stats.Rails.ToString(), stats.RailHits.ToString(), ship.Railguns.Sum(g => g.Rounds).ToString(), stats.Missiles.ToString(), stats.Torpedoes.ToString(),
                am.Rounds.ToString(), am.Mode.ToString(), F(am.Containment), am.Failures.ToString(), am.Jettisons.ToString(),
                F(ship.Damage.Shield / ship.Definition.Shield.Capacity),
                F(ship.Damage.Modules.Sum(m => m.Health) / ship.Damage.Modules.Sum(m => m.Definition.HitPoints)),
                F(ship.Damage.PropulsionFraction), F(ship.Damage.WeaponsFraction), F(ship.Damage.SensorFraction), F(ship.Power.HeatFraction), F(ship.Power.Supply),
                string.Join('/', Enum.GetValues<PowerChannel>().Select(ship.Power.Pips)), F(ship.Velocity.Length()),
                F(Nearest(s => s.Faction == ship.Faction && s.Class.Kind == HullKind.Escort)), F(Nearest(s => s.Faction != ship.Faction)),
                F(stats.ShieldDamageInflicted), F(stats.ModuleDamageInflicted), stats.MissilesIntercepted.ToString(), stats.TorpedoesIntercepted.ToString(), stats.DronesDestroyed.ToString(),
                ship.DamageControl.Completed.ToString(), ship.DamageControl.Kits.ToString(), ship.DamageControl.ActiveModule?.Definition.Id ?? "", F(ship.DamageControl.Progress),
                ship.Squadron?.Mission.ToString() ?? "", ship.Squadron?.MissionChanges.ToString() ?? "",
                ship.Ordnance.Interceptor.Launched.ToString(), world.Probes.Count(p => p.Owner == ship).ToString(),
                ship.Ordnance.Interceptor.NewContacts.ToString(), ship.Ordnance.Interceptor.NewIdentifications.ToString(),
                ship.Ordnance.Interceptor.ProbeKills.ToString(), brain?.SmallTarget?.GetType().Name ?? "", F(track.TrackSnr), track.Observation?.Observer.Callsign ?? "", F(track.Range/1000), B(brain?.Eccm == true), B(brain?.WantBoost == true), F(brain?.DesiredVelocity.Length())
            }));
        }
    }
}
