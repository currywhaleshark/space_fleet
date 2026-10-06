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
    private static readonly int[] BattleTimeScales = { 1, 2, 4 };
    private int[] AvailableTimeScales => BattleMode && !DevMode ? BattleTimeScales : TimeScales;
    private int _timeScaleIndex;

    public int TimeScale => AvailableTimeScales[_timeScaleIndex];
    public SquadCommand SquadOrder { get; private set; } = SquadCommand.Escort;
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
            SquadOrder = SquadCommand.Escort;
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
            squadron.PlayerLed = !AutoPlay && controlled is not null && squadron == controlled.Squadron;
    }

    /// <summary>플레이어 편대(내가 탄 함선의 편대).</summary>
    public Squadron? PlayerSquadron => Controlled?.Body.Squadron;

    /// <summary>편대 명령을 내 편대의 다른 함선에게 내린다. 호위면 조종 함선 둘레에 고르게 자리를 잡는다.</summary>
    private void ApplySquadOrder()
    {
        if (AutoPlay || Controlled?.Body is not ShipBody leader) return;
        if (SquadOrder == SquadCommand.Focus && (SquadTarget is null || SquadTarget.Damage.Destroyed))
            SquadOrder = SquadCommand.Escort;
        ShipBody? fireAt = Gunnery?.Engaged ?? (HasSelectedEnemy ? InspectTarget!.Body : null);
        SquadCommands.Apply(World, leader, SquadOrder, SquadTarget, fireAt);
    }

    private double _nextSquadUpdate;
    private void StepSquadCommand()
    {
        if (SquadOrder is not (SquadCommand.Intercept or SquadCommand.Escort) || World.Time < _nextSquadUpdate) return;
        _nextSquadUpdate = World.Time + 2;
        ApplySquadOrder();
    }
    private void IssueOrder(OrderKind kind) => IssueSquadCommand(kind switch
    { OrderKind.Attack => SquadCommand.Focus, OrderKind.Hold => SquadCommand.Hold, _ => SquadCommand.Escort });

    private void IssueSquadCommand(SquadCommand command)
    {
        if (command == SquadCommand.Focus)
        {
            if (!HasSelectedEnemy) { Notify("공격할 적을 먼저 고르세요 (R)", true); return; }
            SquadTarget = InspectTarget!.Body;
        }
        else SquadTarget = null;
        SquadOrder = command;
        _nextSquadUpdate = World.Time + 2;
        ApplySquadOrder();
        Notify($"편대 · {SquadCommands.Label(command)}", false);
    }
    private void CycleTimeScale(int step)
    {
        _timeScaleIndex = Mathf.Clamp(_timeScaleIndex + step, 0, AvailableTimeScales.Length - 1);
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
