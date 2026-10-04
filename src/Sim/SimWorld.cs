using System.Collections.Generic;

namespace SpaceFleet.Sim;

/// <summary>
/// 시뮬레이션 전체 상태. 고정 틱으로 진행하며, 나중에 서버 권한형 멀티플레이로
/// 옮길 때 이 층만 서버에서 돌리는 것을 전제로 한다.
/// </summary>
public sealed class SimWorld
{
    public const double TickRate = 60.0;
    public const double TickDelta = 1.0 / TickRate;

    private readonly List<ShipBody> _ships = new();

    public IReadOnlyList<ShipBody> Ships => _ships;
    public long Tick { get; private set; }
    public double Time => Tick * TickDelta;

    public ShipBody Add(ShipBody ship)
    {
        _ships.Add(ship);
        return ship;
    }

    public void Step()
    {
        foreach (ShipBody ship in _ships)
            ship.Step(TickDelta);
        Tick++;
    }
}
