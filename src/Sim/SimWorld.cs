using System.Collections.Generic;
using System;
using Godot;

namespace SpaceFleet.Sim;

/// <summary>
/// 시뮬레이션 전체 상태. 고정 틱으로 진행하며, 나중에 서버 권한형 멀티플레이로
/// 옮길 때 이 층만 서버에서 돌리는 것을 전제로 한다.
/// </summary>
public sealed partial class SimWorld
{
    public const double TickRate = 60.0;
    public const double TickDelta = 1.0 / TickRate;
    // 작은 날개·방열판의 회전 접촉을 위해 240Hz로 나눈다. 외부 시뮬레이션 틱은 여전히 60Hz.
    private const int CollisionSubsteps = 4;

    private readonly List<ShipBody> _ships = new();
    private readonly List<(Vec3d Position, Quaternion Orientation)> _previous = new();
    private uint _shotSequence;

    public IReadOnlyList<ShipBody> Ships => _ships;
    /// <summary>진영별 센서망(탐지·식별·잠금). 0.25초마다 갱신한다.</summary>
    public SensorNet Sensors { get; } = new();
    public long Tick { get; private set; }
    public double Time => Tick * TickDelta;

    public ShipBody Add(ShipBody ship)
    {
        _ships.Add(ship);
        return ship;
    }

    public ShotResult FireTestShot(ShipBody shooter, Vec3d origin, Vector3 direction, DamagePacket packet)
    {
        packet.Validate();
        if (!double.IsFinite(origin.X) || !double.IsFinite(origin.Y) || !double.IsFinite(origin.Z)
            || !direction.IsFinite() || direction.LengthSquared() < 1e-8f)
            throw new ArgumentException("Invalid shot ray");
        direction = direction.Normalized();
        ShipBody? target = null;
        float nearest = float.PositiveInfinity;
        foreach (ShipBody ship in _ships)
        {
            if (ship == shooter) continue;
            if (DamageRay.FirstHit(ship, origin, direction, packet.Range, out float distance) && distance < nearest)
            { target = ship; nearest = distance; }
        }
        _shotSequence++;
        return target is null ? new ShotResult(null, origin + Vec3d.From(direction) * packet.Range, packet.Range,
            false, false, Array.Empty<ModuleHit>(), "빗나감") : DamageRay.Apply(target, origin, direction, packet, Time, _shotSequence);
    }

    public void Step()
    {
        StepAI();
        StepGunnery();
        _previous.Clear();
        foreach (RailProjectile projectile in _projectiles) projectile.PrevPosition = projectile.Position;
        foreach (ShipBody ship in _ships)
            _previous.Add((ship.Position, ship.Orientation));
        double dt = TickDelta / CollisionSubsteps;
        for (int substep = 0; substep < CollisionSubsteps; substep++)
        {
            foreach (ShipBody ship in _ships)
                ship.Step(dt);
            ShipCollision.Resolve(_ships, Time + (substep + 1) * dt);
            StepProjectiles(dt, Time + substep * dt);
            StepOrdnance(dt, Time + substep * dt);
        }
        // 렌더 보간은 하위 틱이 아니라 전체 60Hz 틱의 양 끝 상태를 사용한다.
        for (int i = 0; i < _ships.Count; i++)
        {
            _ships[i].PrevPosition = _previous[i].Position;
            _ships[i].PrevOrientation = _previous[i].Orientation;
        }
        Tick++;
        Sensors.Update(_ships, Time);
        _impacts.RemoveAll(i => Time - i.Time > 3);
        PruneOrdnanceEvents();
    }
}
