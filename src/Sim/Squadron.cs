using System.Collections.Generic;
using System.Linq;

namespace SpaceFleet.Sim;

public enum SquadronRole
{
    /// <summary>전함 1 + 호위함 2. 전함이 대치 포격, 호위함은 근접 대형으로 방공하며 같은 표적을 쏜다.</summary>
    BattleGroup,
    /// <summary>호위함 4. 측면으로 돌아 적 호위함에 미사일·포격. 적 요격함이 전투단에 붙으면 방공으로 돌아온다.</summary>
    EscortSquadron,
    /// <summary>요격함 5. 전투단 앞 경계 → 적 요격함 요격 → 집결 후 한 표적에 동시 돌격.</summary>
    InterceptorWing,
}

/// <summary>
/// 편대. 함대 지휘관이 역할에 따라 구성원에게 명령을 내린다. PlayerLed면 지휘관이 건드리지 않고 플레이어 명령을 따른다.
/// </summary>
public sealed class Squadron
{
    public Squadron(string name, Faction faction, SquadronRole role, IEnumerable<ShipBody> members)
    {
        Name = name;
        Faction = faction;
        Role = role;
        Members = members.ToList();
        foreach (ShipBody ship in Members)
            ship.Squadron = this;
    }

    public string Name { get; }
    public Faction Faction { get; }
    public SquadronRole Role { get; }
    public IReadOnlyList<ShipBody> Members { get; }
    public bool PlayerLed { get; set; }

    /// <summary>살아서 움직일 수 있는 첫 구성원. 전투단은 전함이 살아 있으면 전함.</summary>
    public ShipBody? Leader => Members.FirstOrDefault(Active);
    public IEnumerable<ShipBody> ActiveMembers => Members.Where(Active);

    /// <summary>편대의 현재 표적과 행동(HUD·검증용).</summary>
    public ShipBody? Target { get; internal set; }
    public string Activity { get; internal set; } = "";

    internal double GatherStarted = double.NegativeInfinity;
    internal bool Striking;

    public static bool Active(ShipBody ship) => !ship.Damage.Destroyed && !ship.Damage.Disabled;
}
