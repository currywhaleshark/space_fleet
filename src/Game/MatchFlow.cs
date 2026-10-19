using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

/// <summary>Battle setup chosen on the battle settings screen. Null or Auto means "random" for that item.</summary>
public sealed record MatchSettings(BattleObjective? Objective=null,Battlefield? Battlefield=null,
    Engagement Course=Engagement.Auto,Elevation Height=Elevation.Auto)
{
    public static readonly MatchSettings Random=new();
}

public partial class Main
{
    public HullKind SelectedKind { get; private set; } = HullKind.Interceptor;
    public HangarProfile Hangar { get; private set; } = HangarProfile.Campaign();
    public string PlayerHullId => Hangar.SelectedHullId;
    public string? HangarMessage { get; private set; }
    private bool _canSaveHangar=true;
    internal int HangarSlotIndex;
    public MatchLineup? PendingMatch { get; private set; }
    public double MatchingRemaining { get; private set; }
    private int _matchingSeed;
    private string? _matchReceipt;
    private DesignFamily _rewardDesign;
    private bool _rewardClaimed;
    public int? LastResearchReward { get; private set; }
    internal string? ResearchNodeId;
    private bool IsFlowCheck => _args.ContainsKey("flow-test") || _args.ContainsKey("match-flow-test") || _args.ContainsKey("hangar-test") || _args.ContainsKey("progression-test") || _args.ContainsKey("shot");

    private void LoadFleetChoice()
    {
        if (IsFlowCheck)
        {
            if(!_args.ContainsKey("progression-test")&&_args.GetValueOrDefault("menu")!="research")Hangar=HangarProfile.Create();
            SelectedKind=ShipDefinitions.ById(PlayerHullId).Kind;PlayerDesign=ShipDefinitions.ById(PlayerHullId).Design;
            if(_args.ContainsKey("shot")&&_args.GetValueOrDefault("menu")=="research")Hangar.Research!.Points[DesignFamily.Earth]=450;
            return;
        }
        var cfg = new ConfigFile();
        string? legacy=cfg.Load("user://fleet-choice.cfg")==Error.Ok?cfg.GetValue("fleet","hull","interceptor").AsString():null;
        var loaded=HangarStorage.Load(ProjectSettings.GlobalizePath("user://hangar.json"),legacy);
        Hangar=loaded.Profile;_canSaveHangar=loaded.CanSave;HangarMessage=loaded.Message;
        var hull=ShipDefinitions.ById(PlayerHullId);SelectedKind=hull.Kind;PlayerDesign=hull.Design;
    }
    private void SaveFleetChoice()
    {
        if (IsFlowCheck) return;
        if(!_canSaveHangar)return;
        try{HangarStorage.Save(ProjectSettings.GlobalizePath("user://hangar.json"),Hangar);HangarMessage="세팅 저장됨";}
        catch(Exception e)when(e is System.IO.IOException or UnauthorizedAccessException)
        {HangarMessage="저장하지 못했습니다. 이번 출격에는 현재 세팅을 사용합니다.";GD.PushWarning(e.Message);}
    }
    public void ChooseHull(HullKind kind)
        => ChooseHull(ShipDefinitions.For(kind,PlayerDesign).Id);
    public void ChooseHull(string id)
    {
        var hull=ShipDefinitions.ById(id);if(!Hangar.Owned.Contains(id)){OpenResearch(ResearchTree.Catalog.ByHull(id).Id);return;}
        Hangar.SelectedHullId=id;SelectedKind=hull.Kind;PlayerDesign=hull.Design;HangarSlotIndex=0;
        SaveFleetChoice();_overlay?.Rebuild();
    }
    public void SaveHangar(){SaveFleetChoice();}
    public void OpenHangar(){HangarSlotIndex=0;ChangeScreen(BattleScreen.Hangar);}
    public void ReturnToHangar()
    {
        PendingMatch=null;_fixtureOutcome=null;
        if(Battle is not null){RemoveChild(Battle);Battle.QueueFree();Battle=null;}
        OpenHangar();
    }
    public void OpenResearch(string? node=null){ResearchNodeId=node??ResearchTree.Catalog.ByHull(PlayerHullId).Id;ChangeScreen(BattleScreen.Research);}
    public void BuyResearch(string node,string? upgrade=null)
    {
        string? focus=_overlay.ResearchView is {} view?MenuStyles.FocusKey(view):null;
        if(upgrade is null)Hangar.Research!.BuyHull(node);
        else Hangar.Research!.BuyUpgrade(ResearchTree.Catalog.ById(node).HullId!,upgrade);
        SaveHangar();_overlay.Rebuild();
        if(_overlay.ResearchView is {} next)MenuStyles.RestoreFocus(next,focus);
    }
    public string? LaunchProblem()
    {
        try{Matchmaking.ValidateResearchLoadout(Hangar.Squadron(),Hangar.Owned,Hangar.Research!.UpgradeSets());return null;}
        catch(System.IO.InvalidDataException e){return e.Message;}
    }
    /// <summary>Settings screen choices; the hangar's launch button ignores them and randomises everything.</summary>
    public MatchSettings Settings { get; private set; }=MatchSettings.Random;
    // The last launch's settings, reused by "다시 매칭". Null keeps the fixed Selected* values (flow checks).
    internal MatchSettings? LastLaunchSettings { get; private set; }
    private (BattleObjective Objective,Battlefield Field,int FieldSeed,Engagement Course,Elevation Height)? _matchSetup;
    public void SetSettings(MatchSettings settings){Settings=settings;_overlay.Rebuild();}
    public void LaunchFromHangar()=>BeginMatch(settings:MatchSettings.Random);
    public void LaunchFromSettings()=>BeginMatch(settings:Settings);
    /// <summary>Concrete objective, battlefield and terrain seed for a launch; random items follow the match seed.
    /// Course and height stay Auto when random: the deployment draws them from the same seed.</summary>
    internal static (BattleObjective Objective,Battlefield Field,int FieldSeed,Engagement Course,Elevation Height) Resolve(MatchSettings settings,int seed)
    {
        var random=new Random(unchecked(seed*31+7));
        var objectives=Enum.GetValues<BattleObjective>();var fields=Enum.GetValues<Battlefield>();
        var objective=objectives[random.Next(objectives.Length)];var field=fields[random.Next(fields.Length)];
        return (settings.Objective??objective,settings.Battlefield??field,1+random.Next(1000),settings.Course,settings.Height);
    }
    public void BeginMatch(bool useRequestedSeed = false, MatchSettings? settings = null)
    {
        if(LaunchProblem() is { } problem){HangarMessage=problem;OpenHangar();return;}
        settings??=LastLaunchSettings;LastLaunchSettings=settings;
        if (Battle is not null) { RemoveChild(Battle); Battle.QueueFree(); Battle = null; }
        _fixtureOutcome = null;
        _matchingSeed = useRequestedSeed && _args.TryGetValue("seed", out var seed) ? int.Parse(seed)
            : System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, int.MaxValue);
        _matchSetup=settings is null?(SelectedObjective,SelectedBattlefield,FieldSeed,Engagement.Auto,Elevation.Auto):Resolve(settings,_matchingSeed);
        PendingMatch = Matchmaking.CreateTiered(_matchingSeed, Hangar.Squadron(),Hangar.Owned,Hangar.Research!.UpgradeSets());
        _matchReceipt=Guid.NewGuid().ToString("N");_rewardDesign=PlayerDesign;_rewardClaimed=false;LastResearchReward=null;
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
    private void ClaimResearchReward()
    {
        if(_rewardClaimed||_matchReceipt is null||_fixtureOutcome is not null||Battle?.LaunchConfig?.Match?.Tier is null||Battle.World.Rules?.Outcome is not { } outcome)return;
        _rewardClaimed=true;var stats=Battle.OwnedSquadron!.Members.Select(Battle.World.Log!.Ship).ToArray();
        LastResearchReward=Hangar.Research!.Reward(_matchReceipt,_rewardDesign,outcome.Time,outcome.Winner==Faction.Blue,
            Battle.World.Merits.Outcome?.Reward(Battle.OwnedSquadron.Members).Damage??stats.Sum(s=>s.ModuleDamageInflicted+s.ShieldDamageInflicted*.2),
            Battle.World.Merits.Outcome?.Reward(Battle.OwnedSquadron.Members).Intercepts??stats.Sum(s=>s.MissilesIntercepted+s.TorpedoesIntercepted));
        SaveHangar();
    }
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
