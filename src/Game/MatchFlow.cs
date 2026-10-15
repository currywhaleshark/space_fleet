using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class Main
{
    public HullKind SelectedKind { get; private set; } = HullKind.Interceptor;
    public string PlayerHullId => ShipDefinitions.For(SelectedKind, PlayerDesign).Id;
    public MatchLineup? PendingMatch { get; private set; }
    public double MatchingRemaining { get; private set; }
    private int _matchingSeed;
    private bool IsFlowCheck => _args.ContainsKey("flow-test") || _args.ContainsKey("match-flow-test") || _args.ContainsKey("shot");

    private void LoadFleetChoice()
    {
        if (IsFlowCheck) return;
        var cfg = new ConfigFile();
        if (cfg.Load("user://fleet-choice.cfg") != Error.Ok) return;
        string id = cfg.GetValue("fleet", "hull", "interceptor").AsString();
        var hull = ShipDefinitions.All.FirstOrDefault(d => d.Id == id);
        if (hull is null) return;
        SelectedKind = hull.Kind; PlayerDesign = hull.Design;
    }
    private void SaveFleetChoice()
    {
        if (IsFlowCheck) return;
        var cfg = new ConfigFile(); cfg.SetValue("fleet", "hull", PlayerHullId);
        if (cfg.Save("user://fleet-choice.cfg") != Error.Ok) GD.PushWarning("함선 선택을 저장하지 못했습니다.");
    }
    public void ChooseHull(HullKind kind)
    { SelectedKind = kind; SaveFleetChoice(); _overlay.Rebuild(); }
    public void BeginMatch(bool useRequestedSeed = false)
    {
        if (Battle is not null) { RemoveChild(Battle); Battle.QueueFree(); Battle = null; }
        _fixtureOutcome = null;
        _matchingSeed = useRequestedSeed && _args.TryGetValue("seed", out var seed) ? int.Parse(seed)
            : System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, int.MaxValue);
        PendingMatch = Matchmaking.Create(_matchingSeed, PlayerHullId);
        MatchingRemaining = 1.2; SaveFleetChoice();
        ChangeScreen(BattleScreen.Matching);
    }
    private void StepMatching(double delta)
    {
        if (Screen != BattleScreen.Matching || PendingMatch is null) return;
        MatchingRemaining -= delta;
        if (MatchingRemaining > 0) return;
        var match = PendingMatch; PendingMatch = null;
        StartBattle(match.Player.Callsign(0), match: match, matchSeed: _matchingSeed);
    }
    public void ReturnToPort()
    {
        PendingMatch = null; _fixtureOutcome = null;
        if (Battle is not null) { RemoveChild(Battle); Battle.QueueFree(); Battle = null; }
        ChangeScreen(BattleScreen.Title);
    }
    public void ContinueSpectating() => ChangeScreen(BattleScreen.Battle);
    private bool _matchPreviewApplied;
    private void PreviewMatch()
    {
        if(_matchPreviewApplied||!_args.ContainsKey("shot")||Battle?.LaunchConfig?.Match is null)return;
        if(_args.ContainsKey("roster-preview"))
        {Battle._UnhandledInput(new InputEventAction{Action=InputSetup.TeamRoster,Pressed=true});_matchPreviewApplied=true;}
        if(_args.ContainsKey("eliminated-preview"))
        {
            foreach(var ship in Battle.OwnedSquadron!.Members)ship.Damage.Breakup(Battle.World.Time,"preview");
            _matchPreviewApplied=true;
        }
    }
}
