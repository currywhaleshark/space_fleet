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

    internal void RecordImpact(uint id, ShipBody shooter, ShotResult hit, double time,
        Vector3 direction, float energy, float shieldBefore, BattleWeapon weapon)
    {
        if (hit.Target is not { } target) return;
        _impacts.Add(new ProjectileImpact(id, shooter, hit, time) {
            IncomingDirection = direction, Energy = energy, Weapon = weapon,
            TargetVelocity=target.Velocity, TargetOrientation=target.Orientation,
            ShieldBroken = shieldBefore > 0 && target.Damage.Shield <= 0,
            TargetDestroyed = target.Damage.Destroyed });
        if (_impacts.Count > 64) _impacts.RemoveAt(0);
    }

    public FireAttempt FireRailgun(ShipBody shooter, Vector3 direction)
    {
        if (!_ships.Contains(shooter)) return new(false, "월드에 없는 함선", Failure: FireFailure.NotInWorld);
        if (shooter.Railgun is not RailgunState gun) return new(false, "주포 없음", Failure: FireFailure.NoGun);
        return FireRailgun(shooter, gun, direction);
    }

    /// <summary>All ready mounts aim independently at a common point (or a parallel far-field direction).</summary>
    public FireAttempt FireRailguns(ShipBody shooter, Vector3 direction, Vec3d? aimPoint = null)
    {
        if (!_ships.Contains(shooter)) return new(false, "월드에 없는 함선", Failure: FireFailure.NotInWorld);
        FireAttempt first = new(false, "주포 없음", Failure: FireFailure.NoGun);
        int count = 0;
        foreach (RailgunState gun in shooter.Railguns)
        {
            Vector3 aim = aimPoint is Vec3d point ? (point - gun.MuzzlePosition).ToVector3() : direction;
            FireAttempt shot = FireRailgun(shooter, gun, aim);
            if (shot.Fired) { if (count == 0) first = shot; count += shot.Shots; }
            else if (count == 0 && (first.Failure is FireFailure.NoGun or FireFailure.NotReady || shot.Failure == FireFailure.Traversing)) first = shot;
        }
        return count > 0 ? first with { Shots = count, Reason = $"주포 {count}발 발사" } : first;
    }

    public FireAttempt FireRailgun(ShipBody shooter, RailgunState gun, Vector3 direction)
    {
        if (!_ships.Contains(shooter) || !System.Array.Exists(shooter.Railguns, g => g == gun))
            return new(false, "월드에 없는 함선/포탑", Failure: FireFailure.NotInWorld);
        if (!direction.IsFinite() || direction.LengthSquared() < 1e-8f) return new(false, "조준 방향 없음", Failure: FireFailure.NoDirection);
        gun.Aim(direction);
        if (!gun.Ready) return new(false, gun.Status, Failure: FireFailure.NotReady);
        direction = direction.Normalized();
        FireFailure line = RailLineLocal(shooter, gun, shooter.Orientation.Inverse() * direction);
        if (line == FireFailure.Arc)
            return new(false, "주포 사각 밖", Failure: FireFailure.Arc);
        if (line == FireFailure.HullBlocked)
            return new(false, "선체 가림", Failure: FireFailure.HullBlocked);
        if (!gun.Aligned(direction)) return new(false, "포탑 선회 중", Failure: FireFailure.Traversing);
        // Turret rounds leave the actual barrel, never an instant correction toward the requested aim.
        if (gun.Mount is not null) direction = gun.Direction;
        if (BarrelBlocked(shooter, gun, gun.Yaw, gun.Elevation))
            return new(false, "선체 가림", Failure: FireFailure.HullBlocked);
        int rounds = gun.SalvoRounds;
        RailProjectile? first = null;
        for (int barrel = 0; barrel < rounds; barrel++)
        {
            Vec3d muzzle = gun.BarrelPosition(barrel);
            var projectile = new RailProjectile
            {
                Id = ++_shotSequence, Shooter = shooter, Position = muzzle, PrevPosition = muzzle,
                ModuleId = gun.Definition.ModuleId, Barrel = barrel,
                Velocity = shooter.Velocity + direction * gun.Definition.MuzzleSpeed, Packet = gun.Definition.Packet,
                Lifetime = gun.Definition.MaxRange / gun.Definition.MuzzleSpeed,
            };
            _projectiles.Add(projectile);
            first ??= projectile;
            Log?.Fire(shooter, BattleWeapon.Railgun, Time);
        }
        gun.Consume(rounds);
        return new(true, $"레일건 {rounds}발 발사", first, Shots: rounds);
    }

    /// <summary>
    /// 함선 로컬 방향(전방 -Z)으로 지금 주포를 쏠 수 있는지: 포각, 자함 선체 가림. 발사하지 않는다(HUD 사격 방위구용).
    /// </summary>
    public static FireFailure RailLineLocal(ShipBody shooter, Vector3 local)
    {
        FireFailure result = FireFailure.NoGun;
        foreach (RailgunState gun in shooter.Railguns)
        {
            if (gun.Output <= .01f || gun.Rounds <= 0) continue;
            FireFailure line = RailLineLocal(shooter, gun, local);
            if (line == FireFailure.None) return line;
            if (result == FireFailure.NoGun || line == FireFailure.HullBlocked) result = line;
        }
        return result;
    }

    public static FireFailure RailLineLocal(ShipBody shooter, RailgunState gun, Vector3 local)
    {
        if (!local.IsFinite() || local.LengthSquared() < 1e-8f) return FireFailure.NoDirection;
        local = local.Normalized();
        if (gun.Mount is { } mount)
        {
            if (!mount.Contains(local)) return FireFailure.Arc;
            Vector2 a = mount.Angles(local);
            return BarrelBlocked(shooter, gun, a.X, a.Y) ? FireFailure.HullBlocked : FireFailure.None;
        }
        if (Vector3.Forward.AngleTo(local) > Mathf.DegToRad(gun.Definition.TraverseDegrees)) return FireFailure.Arc;
        return DamageRay.FirstHitAtPose(shooter, Vec3d.From(gun.Definition.Muzzle), local, gun.Definition.MaxRange,
            Vec3d.Zero, Quaternion.Identity, out _) ? FireFailure.HullBlocked : FireFailure.None;
    }

    private static bool BarrelBlocked(ShipBody ship, RailgunState gun, float yaw, float pitch)
    {
        for (int barrel = 0; barrel < gun.SalvoRounds; barrel++)
            if (BarrelBlocked(ship, gun, yaw, pitch, barrel)) return true;
        return false;
    }

    private static bool BarrelBlocked(ShipBody ship, RailgunState gun, float yaw, float pitch, int barrel)
    {
        if (gun.Mount is not { } mount) return false; // fixed gun checked by RailLineLocal
        Vector3 direction = mount.AimBasis(yaw, pitch) * Vector3.Forward;
        Vector3 muzzle = mount.Muzzle(yaw, pitch, barrel);
        Vector3 breech = muzzle - direction * Mathf.Abs(mount.Muzzles[barrel].Z);
        if (DamageRay.FirstHitAtPose(ship, Vec3d.From(breech), direction, gun.Definition.MaxRange,
            Vec3d.Zero, Quaternion.Identity, out _)) return true;
        foreach (RailgunState other in ship.Railguns)
        {
            if (other == gun || other.Mount is not { } housing || housing.HousingHalfSize == Vector3.Zero) continue;
            Basis inverse = (housing.MountBasis * new Basis(Vector3.Up, other.Yaw)).Inverse();
            if (DamageRay.IntersectsBox(inverse * (breech - housing.Pivot), inverse * direction,
                housing.HousingCenter, housing.HousingHalfSize, gun.Definition.MaxRange)) return true;
        }
        return false;
    }

    /// <summary>월드 방향 버전.</summary>
    public static FireFailure RailLine(ShipBody shooter, Vector3 direction) =>
        RailLineLocal(shooter, shooter.Orientation.Inverse() * direction);

    public void ResetWeapons()
    {
        foreach (ShipBody ship in _ships) foreach (RailgunState gun in ship.Railguns) gun.Reset();
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
            if (FirstDroneHit(p.Shooter,start,end,travelTime/dt,earliest,out var carrier,out int droneIndex,out double droneFraction))
            {
                DamageDrone(p.Shooter,carrier!,droneIndex,p.Packet.Energy,time+travelTime*droneFraction);
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
            else if (p.Age >= p.Lifetime - 1e-9) _projectiles.RemoveAt(index);
        }
    }
}
