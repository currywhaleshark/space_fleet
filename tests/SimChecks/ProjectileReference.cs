// Frozen d15d525 projectile traversal and small-target narrow phase.
// Test-only reference: full target scan, original live hull/shield radius reads.
using System;
using Godot;
namespace SpaceFleet.Sim;
public sealed partial class SimWorld
{
    internal void StepProjectilesForCheck(double dt,double time,bool reference)
    {
        if (!reference) StepProjectiles(dt,time);
        else if (Obstacles.Count == 0) ReferenceStepProjectiles<EmptyEnvironment>(dt,time);
        else ReferenceStepProjectiles<FixedEnvironment>(dt,time);
    }
    private void ReferencePrepareProjectileBounds()
    {
        if (_projectileShipBounds.Length < _ships.Count)
        { Array.Resize(ref _projectileShipBounds, _ships.Count); Array.Resize(ref _projectileDroneBounds, _ships.Count); }
        for (int i = 0; i < _ships.Count; i++)
        {
            var ship = _ships[i];
            _projectileShipBounds[i] = SpatialBounds.Segment(ship.PrevPosition, ship.Position);
            if (ship.Damage.Destroyed || ship.Definition.DefenseDrones is not { } def) continue;
            var bounds = _projectileShipBounds[i];
            for (int j = 0; j < def.Count; j++)
            {
                if (!ship.Ordnance.Drones.Alive(j)) continue;
                bounds = bounds.Include(ship.Ordnance.Drones.PreviousWorldPosition(j)).Include(ship.Ordnance.Drones.WorldPosition(j));
            }
            _projectileDroneBounds[i] = bounds.Expanded(def.RadiusMeters);
        }
    }

    private object? ReferenceFirstSmallHit(RailProjectile projectile, Vec3d start, Vec3d end, double dt, double time, ref double nearest,
        out bool proximity)
    {
        var shooter = projectile.Shooter;
        proximity = false;
        object? hit = null;
        foreach (var missile in _missiles)
        {
            if (missile.Faction == shooter.Faction || missile.Health <= 0) continue;
            bool fused = projectile.ProximityTarget == missile && projectile.Age >= .04 && projectile.Age <= 3;
            double radius = fused ? projectile.ProximityMeters : 3;
            if (ReferenceMovingSphereHit(start - missile.Position, end - start - Vec3d.From(missile.Velocity) * dt, radius, out double t) && t < nearest
                && (!fused || !TerrainBlocked(Vec3d.Lerp(start,end,t),missile.Position+Vec3d.From(missile.Velocity)*dt*t)))
            { nearest = t; hit = missile; proximity = fused; }
        }
        foreach (var probe in _probes)
        {
            if (probe.Faction == shooter.Faction || !probe.Active) continue;
            // Probes integrate once at 60 Hz; rail collision runs at 240 Hz. Advance the sampled
            // pose to this substep instead of repeatedly testing against the tick-start position.
            Vec3d position = probe.Position + Vec3d.From(probe.Velocity) * Math.Clamp(time - Time, 0, TickDelta);
            if (ReferenceMovingSphereHit(start - position, end - start - Vec3d.From(probe.Velocity) * dt,
                probe.Definition.Radius, out double t) && t < nearest) { nearest = t; hit = probe; proximity = false; }
        }
        return hit;
    }

    // Continuous double-precision relative trajectory: a fast missile cannot tunnel through a round.
    private static bool ReferenceMovingSphereHit(Vec3d r, Vec3d travel, double radius, out double t)
    {
        t = 0;
        double c = r.LengthSquared() - radius * radius;
        if (c <= 0) return true;
        double a = travel.LengthSquared(), b = r.Dot(travel), discriminant = b * b - a * c;
        if (a < 1e-12 || discriminant < 0) return false;
        t = (-b - Math.Sqrt(discriminant)) / a;
        return t >= 0 && t <= 1;
    }

    internal void ReferenceStepProjectiles<TEnvironment>(double dt,double time) where TEnvironment:struct
    {
        ReferencePrepareProjectileBounds();
        bool environment=typeof(TEnvironment)==typeof(FixedEnvironment);
        ObstacleHit obstacle=default;
        for (int index = _projectiles.Count - 1; index >= 0; index--)
        {
            RailProjectile p = _projectiles[index];
            double remaining = p.Lifetime - p.Age;
            double travelTime = Math.Min(dt, remaining);
            if (travelTime <= 1e-9) { _projectiles.RemoveAt(index); continue; }
            Vec3d start = p.Position, end = start + Vec3d.From(p.Velocity) * travelTime;
            var bounds = SpatialBounds.Segment(start, end).Expanded(0);
            ShipBody? target = null;
            double earliest = double.PositiveInfinity;
            bool blocked=environment && Obstacles.SegmentBlocked(start,end,out obstacle);
            if(blocked) earliest=obstacle.Fraction;
            Vector3 hitDirection = Vector3.Zero;
            Quaternion hitOrientation = Quaternion.Identity;
            Vec3d targetTravel = Vec3d.Zero;
            for (int shipIndex = 0; shipIndex < _ships.Count; shipIndex++)
            {
                ShipBody ship = _ships[shipIndex];
                if (ship == p.Shooter) continue;
                // Positions cannot change in this loop, but an earlier round can destroy a hull
                // or its shield. Read the live radius; never cache a pre-hit shield/wreck state.
                if (!bounds.Overlaps(_projectileShipBounds[shipIndex], DamageRay.DefenseRadius(ship))) continue;
                Vec3d movement = (ship.Position - ship.PrevPosition) * (travelTime / dt);
                Vec3d relativeStart = start - ship.PrevPosition;
                Vec3d relativeTravel = end - start - movement;
                // 연속 상대 이동 검사 + 240Hz 구간 중앙 자세. 절대 좌표를 float로 내리지 않는다.
                double length = relativeTravel.Length();
                if (length < 1e-8) continue;
                double closest = Math.Clamp(-relativeStart.Dot(relativeTravel) / (length * length), 0, 1);
                if ((relativeStart + relativeTravel * closest).Length() > DamageRay.DefenseRadius(ship)) continue;
                Vector3 direction = (relativeTravel * (1 / length)).ToVector3();
                Quaternion orientation = ship.PrevOrientation.Slerp(ship.Orientation, (float)(travelTime / dt * 0.5));
                if (!DamageRay.FirstDefenseHitAtPose(ship, start, direction, (float)length, ship.PrevPosition, orientation, out float distance)) continue;
                double fraction = distance / length;
                if (fraction >= earliest) continue;
                earliest = fraction; target = ship; hitDirection = direction; hitOrientation = orientation; targetTravel = movement;
            }
            p.Age += travelTime;
            p.Position = end;
            double smallFraction = earliest;
            object? small = ReferenceFirstSmallHit(p, start, end, travelTime, time, ref smallFraction, out bool proximity);
            if (FirstDroneHit(p.Shooter,start,end,bounds,travelTime/dt,smallFraction,out var carrier,out int droneIndex,out double droneFraction))
            {
                DamageDrone(p.Shooter,carrier!,droneIndex,p.Packet.Energy,time+travelTime*droneFraction);
                _projectiles.RemoveAt(index);
                continue;
            }
            if (small is not null)
            {
                double hitTime = time + travelTime * smallFraction;
                if (small is Missile missile)
                { missile.Health -= proximity ? 6 : p.Packet.Energy; if (missile.Health <= 0) DestroyMissile(p.Shooter, missile, hitTime); }
                else DamageProbe(p.Shooter, (ReconProbe)small, p.Packet.Energy, hitTime);
                _projectiles.RemoveAt(index);
                continue;
            }
            if (target is not null)
            {
                // 상대 경로를 명중 시각의 선체 위치에 옮겨 같은 교차점에서 내부 관통을 계산한다.
                Vec3d offset = targetTravel * earliest;
                float shieldBefore = target.Damage.Shield;
                ShotResult hit = DamageRay.ApplyAtPose(target, start + offset, hitDirection, p.Packet,
                    time + travelTime * earliest, p.Id, target.PrevPosition + offset, hitOrientation);
                Log?.Hit(p.Shooter, hit, BattleWeapon.Railgun, time + travelTime * earliest, shieldBefore);
                RecordImpact(p.Id, p.Shooter, hit, time + travelTime * earliest,
                    hitDirection, p.Packet.Energy, shieldBefore, BattleWeapon.Railgun);
                _projectiles.RemoveAt(index);
            }
            else if(blocked)
            {
                var wall=obstacle;
                double at=time+travelTime*wall.Fraction;
                Log?.Environment.Weapon(at,p.Shooter.Callsign,wall,BattleWeapon.Railgun);
                _ordnanceEvents.Add(new(OrdnanceEventKind.Detonation,wall.Position,at,p.Shooter.Faction,BattleWeapon.Railgun,p.Velocity.Normalized()));
                _projectiles.RemoveAt(index);
            }
            else if (p.Age >= p.Lifetime - 1e-9) _projectiles.RemoveAt(index);
        }
    }
}
