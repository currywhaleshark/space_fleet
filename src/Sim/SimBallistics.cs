using System;
using System.Collections.Generic;
using Godot;

namespace SpaceFleet.Sim;

public sealed partial class SimWorld
{
    private readonly List<RailProjectile> _projectiles = new();
    private readonly List<ProjectileImpact> _impacts = new();
    public IReadOnlyList<RailProjectile> Projectiles => _projectiles;
    public IReadOnlyList<ProjectileImpact> Impacts => _impacts;

    public FireAttempt FireRailgun(ShipBody shooter, Vector3 direction)
    {
        if (!_ships.Contains(shooter)) return new(false, "월드에 없는 함선", Failure: FireFailure.NotInWorld);
        if (shooter.Railgun is not RailgunState gun) return new(false, "주포 없음", Failure: FireFailure.NoGun);
        if (!direction.IsFinite() || direction.LengthSquared() < 1e-8f) return new(false, "조준 방향 없음", Failure: FireFailure.NoDirection);
        if (!gun.Ready) return new(false, gun.Status, Failure: FireFailure.NotReady);
        direction = direction.Normalized();
        if (shooter.Forward.AngleTo(direction) > Mathf.DegToRad(gun.Definition.TraverseDegrees))
            return new(false, "주포 사각 밖", Failure: FireFailure.Arc);
        if (DamageRay.FirstHit(shooter, gun.MuzzlePosition, direction, gun.Definition.MaxRange, out _))
            return new(false, "선체 가림", Failure: FireFailure.HullBlocked);
        _shotSequence++;
        var projectile = new RailProjectile
        {
            Id = _shotSequence, Shooter = shooter, Position = gun.MuzzlePosition, PrevPosition = gun.MuzzlePosition,
            Velocity = shooter.Velocity + direction * gun.Definition.MuzzleSpeed, Packet = gun.Definition.Packet,
            Lifetime = gun.Definition.MaxRange / gun.Definition.MuzzleSpeed,
        };
        _projectiles.Add(projectile);
        gun.Consume();
        return new(true, "레일건 발사", projectile);
    }

    /// <summary>
    /// 함선 로컬 방향(전방 -Z)으로 지금 주포를 쏠 수 있는지: 포각, 자함 선체 가림. 발사하지 않는다(HUD 사격 방위구용).
    /// </summary>
    public static FireFailure RailLineLocal(ShipBody shooter, Vector3 local)
    {
        if (shooter.Railgun is not RailgunState gun) return FireFailure.NoGun;
        if (!local.IsFinite() || local.LengthSquared() < 1e-8f) return FireFailure.NoDirection;
        local = local.Normalized();
        if (Vector3.Forward.AngleTo(local) > Mathf.DegToRad(gun.Definition.TraverseDegrees)) return FireFailure.Arc;
        return DamageRay.FirstHitAtPose(shooter, Vec3d.From(gun.Definition.Muzzle), local, gun.Definition.MaxRange,
            Vec3d.Zero, Quaternion.Identity, out _) ? FireFailure.HullBlocked : FireFailure.None;
    }

    /// <summary>월드 방향 버전.</summary>
    public static FireFailure RailLine(ShipBody shooter, Vector3 direction) =>
        RailLineLocal(shooter, shooter.Orientation.Inverse() * direction);

    public void ResetWeapons()
    {
        foreach (ShipBody ship in _ships) ship.Railgun?.Reset();
        _projectiles.Clear(); _impacts.Clear();
        ResetOrdnance();
        ResetAI();
    }

    private void StepProjectiles(double dt, double time)
    {
        for (int index = _projectiles.Count - 1; index >= 0; index--)
        {
            RailProjectile p = _projectiles[index];
            double remaining = p.Lifetime - p.Age;
            double travelTime = Math.Min(dt, remaining);
            if (travelTime <= 1e-9) { _projectiles.RemoveAt(index); continue; }
            Vec3d start = p.Position, end = start + Vec3d.From(p.Velocity) * travelTime;
            ShipBody? target = null;
            double earliest = double.PositiveInfinity;
            Vector3 hitDirection = Vector3.Zero;
            Quaternion hitOrientation = Quaternion.Identity;
            Vec3d targetTravel = Vec3d.Zero;
            foreach (ShipBody ship in _ships)
            {
                if (ship == p.Shooter) continue;
                Vec3d movement = (ship.Position - ship.PrevPosition) * (travelTime / dt);
                Vec3d relativeStart = start - ship.PrevPosition;
                Vec3d relativeTravel = end - start - movement;
                // 연속 상대 이동 검사 + 240Hz 구간 중앙 자세. 절대 좌표를 float로 내리지 않는다.
                double length = relativeTravel.Length();
                if (length < 1e-8) continue;
                double closest = Math.Clamp(-relativeStart.Dot(relativeTravel) / (length * length), 0, 1);
                if ((relativeStart + relativeTravel * closest).Length() > ship.Hull.BoundingRadius) continue;
                Vector3 direction = (relativeTravel * (1 / length)).ToVector3();
                Quaternion orientation = ship.PrevOrientation.Slerp(ship.Orientation, (float)(travelTime / dt * 0.5));
                if (!DamageRay.FirstHitAtPose(ship, start, direction, (float)length, ship.PrevPosition, orientation, out float distance)) continue;
                double fraction = distance / length;
                if (fraction >= earliest) continue;
                earliest = fraction; target = ship; hitDirection = direction; hitOrientation = orientation; targetTravel = movement;
            }
            p.Age += travelTime;
            p.Position = end;
            if (target is not null)
            {
                // 상대 경로를 명중 시각의 선체 위치에 옮겨 같은 교차점에서 내부 관통을 계산한다.
                Vec3d offset = targetTravel * earliest;
                ShotResult hit = DamageRay.ApplyAtPose(target, start + offset, hitDirection, p.Packet,
                    time + travelTime * earliest, p.Id, target.PrevPosition + offset, hitOrientation);
                _impacts.Add(new ProjectileImpact(p.Id, p.Shooter, hit, time + travelTime * earliest));
                if (_impacts.Count > 64) _impacts.RemoveAt(0);
                _projectiles.RemoveAt(index);
            }
            else if (p.Age >= p.Lifetime - 1e-9) _projectiles.RemoveAt(index);
        }
    }
}
