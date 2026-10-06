using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>
/// 편대 지휘와 AI 연결. 플레이어가 탄 함선의 편대는 G/H/J 명령을 따르고(PlayerLed),
/// 나머지 아군 편대와 적 함대는 시뮬레이션의 함대 지휘관이 역할대로 움직인다. [ ]로 시간 배속(×1·×4·×16).
/// </summary>
public partial class ScaleTest
{
    private static readonly int[] TimeScales = { 1, 4, 16 };
    private int _timeScaleIndex;

    public int TimeScale => TimeScales[_timeScaleIndex];
    public OrderKind SquadOrder { get; private set; } = OrderKind.Escort;
    public ShipBody? SquadTarget { get; private set; }
    /// <summary>스크린샷 검증 등에서 AI를 끈 상태.</summary>
    public bool AiDisabled { get; private set; }

    /// <summary>시작할 때 한 번: 아군 편대와 적 함대에 AI를 붙인다.</summary>
    private void SetupAI(bool disabled)
    {
        AiDisabled = disabled;
        foreach (ShipView view in Views)
        {
            ShipBody ship = view.Body;
            if (ship == Controlled?.Body) continue;
            ShipBrain brain = World.AttachBrain(ship, ShipOrder.HoldAt(ship.Position));
            brain.Enabled = !disabled;
        }
        if (!disabled)
        {
            World.EnableCommander(Faction.Red);
            World.EnableCommander(Faction.Blue);
        }
        MarkPlayerSquadron();
        ApplySquadOrder();
    }

    /// <summary>조종 함선을 바꿀 때: 새 함선은 AI를 떼고, 이전 함선은 편대에 넣는다.</summary>
    private void HandOverControl(ShipBody? previous, ShipBody next)
    {
        World.DetachBrain(next);
        if (previous is not null && previous != next && World.Brains.Count > 0)
            World.AttachBrain(previous, ShipOrder.HoldAt(previous.Position)).Enabled = !AiDisabled;
        // 다른 편대로 옮겨 타면 그 편대가 내 명령을 따르고, 이전 편대는 지휘관에게 돌아간다.
        if (previous?.Squadron != next.Squadron)
        {
            SquadOrder = OrderKind.Escort;
            SquadTarget = null;
        }
        MarkPlayerSquadron(next);
        ApplySquadOrder();
    }

    /// <summary>플레이어가 탄 함선의 편대만 PlayerLed로 둔다.</summary>
    private void MarkPlayerSquadron(ShipBody? controlled = null)
    {
        controlled ??= Controlled?.Body;
        foreach (Squadron squadron in World.Squadrons)
            squadron.PlayerLed = controlled is not null && squadron == controlled.Squadron;
    }

    /// <summary>플레이어 편대(내가 탄 함선의 편대).</summary>
    public Squadron? PlayerSquadron => Controlled?.Body.Squadron;

    /// <summary>편대 명령을 내 편대의 다른 함선에게 내린다. 호위면 조종 함선 둘레에 고르게 자리를 잡는다.</summary>
    private void ApplySquadOrder()
    {
        if (Controlled?.Body is not ShipBody leader) return;
        var squad = World.Brains.Values
            .Where(b => b.Ship.Squadron is not null && b.Ship.Squadron == leader.Squadron && b.Ship != leader && !b.Ship.Damage.Destroyed)
            .OrderBy(b => b.Ship.Callsign)
            .ToList();
        if (SquadOrder == OrderKind.Attack && (SquadTarget is null || SquadTarget.Damage.Destroyed))
            SquadOrder = OrderKind.Escort;

        for (int i = 0; i < squad.Count; i++)
        {
            ShipBrain brain = squad[i];
            ShipBody ship = brain.Ship;
            float angle = Mathf.Tau * i / squad.Count;
            float radius = leader.Class.Length * 0.6f + ship.Class.Length * 0.6f + 500f;
            brain.FormationOffset = new Vector3(Mathf.Cos(angle) * radius, (i % 2 == 0 ? 1 : -1) * radius * 0.15f, Mathf.Sin(angle) * radius);
            brain.Order = SquadOrder switch
            {
                OrderKind.Attack => ShipOrder.AttackOn(SquadTarget!),
                OrderKind.Hold => ShipOrder.HoldAt(ship.Position),
                _ => ShipOrder.EscortOf(leader),
            };
        }
    }

    private void IssueOrder(OrderKind kind)
    {
        if (kind == OrderKind.Attack)
        {
            if (InspectTarget is not ShipView target || target.Body.Faction == Controlled?.Body.Faction || target.Body.Damage.Destroyed)
            {
                Notify("공격할 적을 먼저 고르세요 (R)", failed: true);
                return;
            }
            SquadTarget = target.Body;
        }
        SquadOrder = kind;
        ApplySquadOrder();
        Notify(kind switch
        {
            OrderKind.Attack => $"편대: {SquadTarget!.Callsign} 공격",
            OrderKind.Hold => "편대: 위치 유지",
            _ => "편대: 호위",
        }, failed: false);
    }

    private void CycleTimeScale(int step)
    {
        _timeScaleIndex = Mathf.Clamp(_timeScaleIndex + step, 0, TimeScales.Length - 1);
        Notify($"시간 ×{TimeScale}", failed: false);
    }

    /// <summary>연습 표적·훈련 함선처럼 스크립트가 움직이는 함선은 AI를 끈다. F6에서 다시 켠다.</summary>
    private void SuspendBrain(ShipBody ship)
    {
        if (World.BrainOf(ship) is ShipBrain brain)
            brain.Enabled = false;
    }

    private void ResumeBrains()
    {
        if (AiDisabled) return;
        foreach (ShipBrain brain in World.Brains.Values)
            brain.Enabled = true;
        _practiceTarget = null;
        _drillShooter = null;
        ApplySquadOrder();
    }
}
