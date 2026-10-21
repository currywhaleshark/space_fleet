using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public enum BattleScreen { Title, Select, Matching, Battle, Pause, Eliminated, Result, Hangar, Research }
public partial class Main : Node3D
{
    public BattleScreen Screen { get; private set; } = BattleScreen.Title;
    public ScaleTest? Battle { get; private set; }
    public int Seed { get; private set; }
    public string Role { get; private set; } = "IC-21";
    public BattleObjective SelectedObjective { get; private set; } = BattleObjective.Annihilation;
    public Battlefield SelectedBattlefield { get; private set; } = Battlefield.DeepSpace;
    public int FieldSeed { get; private set; } = 1;
    public void CycleBattlefield() { SelectedBattlefield=SelectedBattlefield==Battlefield.DeepSpace?Battlefield.AsteroidCluster:Battlefield.DeepSpace;_overlay.Rebuild(); }
    public void CycleObjective()
    {
        SelectedObjective = SelectedObjective switch
        {
            BattleObjective.Annihilation => BattleObjective.Flagship,
            BattleObjective.Flagship => BattleObjective.Domination,
            _ => BattleObjective.Annihilation,
        };
        _overlay.Rebuild();
    }
    public DesignFamily PlayerDesign { get; private set; } = DesignFamily.Earth;
    public DesignFamily EnemyDesign { get; private set; } = DesignFamily.Mars;
    public void ToggleDesign(bool enemy)
    {
        if (enemy) EnemyDesign = EnemyDesign == DesignFamily.Earth ? DesignFamily.Mars : DesignFamily.Earth;
        else PlayerDesign = PlayerDesign == DesignFamily.Earth ? DesignFamily.Mars : DesignFamily.Earth;
        if (!enemy) { var wanted=ShipDefinitions.For(SelectedKind,PlayerDesign).Id;ChooseHull(Hangar.Owned.Contains(wanted)?wanted:ResearchTree.Catalog.Root(PlayerDesign).HullId!); return; }
        _overlay.Rebuild();
    }
    public BattleOutcome? Outcome => _fixtureOutcome ?? Battle?.World.Rules?.Outcome;
    private BattleOutcome? _fixtureOutcome;
    private BattleOverlay _overlay = null!;
    private Dictionary<string,string> _args = null!;
    private int _frame;
    private double _resultDelay;
    public override void _Ready()
    {
        _args = BattleArgs.Parse(OS.GetCmdlineUserArgs());
        if(_args.ContainsKey("merit-test")){AddChild(new MeritGameChecks());return;}
        if(_args.ContainsKey("hud-details-test")){AddChild(new HudDetailsChecks());return;}
        if(_args.ContainsKey("selection-hud-test")){AddChild(new TargetSelectionChecks());return;}
        if(_args.ContainsKey("hud-readability-test")){AddChild(new HudReadabilityChecks());return;}
        if(_args.ContainsKey("interception-hud-test")){AddChild(new InterceptionHudChecks());return;}
        if(_args.ContainsKey("laser-test")||_args.ContainsKey("laser-preview"))
        { AddChild(new LaserGameChecks{Shot=_args.ContainsKey("laser-preview")?_args.GetValueOrDefault("shot"):null});return; }
        if(_args.ContainsKey("export-menu-art")){AddChild(new MenuArtExport());return;}
        LoadFleetChoice();
        if(_args.TryGetValue("battlefield",out var battlefield))SelectedBattlefield=Enum.Parse<Battlefield>(battlefield,true);
        if(_args.TryGetValue("field-seed",out var fieldSeed))FieldSeed=int.Parse(fieldSeed);
        if (_args.TryGetValue("blue-design", out var blue))
        { PlayerDesign = Enum.Parse<DesignFamily>(blue, true); Hangar.SelectedHullId=ShipDefinitions.For(SelectedKind,PlayerDesign).Id; }
        if (_args.TryGetValue("hangar-hull",out var hangarHull))
        { var hull=ShipDefinitions.ById(hangarHull);Hangar.SelectedHullId=hull.Id;SelectedKind=hull.Kind;PlayerDesign=hull.Design; }
        // Visual fixtures use an isolated profile and cannot overwrite the player's saved fits.
        if(_args.ContainsKey("shot")&&_args.ContainsKey("hangar-fit"))
            foreach(var fit in FleetLoadouts.AiPreset(PlayerHullId,1).Members)
                Hangar.Hulls[fit.HullId].Presets[0]=fit.Equipment.ToArray();
        if(_args.ContainsKey("shot")&&_args.TryGetValue("hangar-component",out var component))
        {
            Hangar.Research!.Points[PlayerDesign]=100000;
            foreach(string id in component.Split(','))
                Hangar.SetComponent(PlayerHullId,0,ShipComponents.ById(id).Slot,id,true);
        }
        if (_args.TryGetValue("red-design", out var red)) EnemyDesign = Enum.Parse<DesignFamily>(red, true);
        if (_args.TryGetValue("objective", out var objective)
            && Enum.TryParse<BattleObjective>(objective, true, out var parsed) && Enum.IsDefined(parsed))
            SelectedObjective = parsed;
        InputSetup.Register();
        SoundSettings.Initialize();
        FeedbackSettings.Load();
        HudSettings.Load();
        if(_args.ContainsKey("teaser") || _args.ContainsKey("teaser-preview")) {
            AddChild(new CinematicTeaser { Options=_args }); return; }
        if (_args.ContainsKey("gallery")) { AddChild(ModelGallery.FromArgs(_args)); return; }
        if (_args.ContainsKey("cine") || _args.ContainsKey("cine-still")) { AddChild(new Cine.CineDirector()); return; }
        if(_args.ContainsKey("combat-visual-test") || _args.ContainsKey("fx-preview")) {
            AddChild(new CombatVisualChecks { Preview=_args.GetValueOrDefault("fx-preview"),Output=_args.GetValueOrDefault("shot"),
                ShipId=_args.GetValueOrDefault("fx-ship","battleship"),PreviewView=_args.GetValueOrDefault("fx-view","hero"),
                PreviewAge=float.TryParse(_args.GetValueOrDefault("fx-age"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float age)?age:.12f,
                PreviewDistance=float.TryParse(_args.GetValueOrDefault("fx-distance"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float fxDistance)?fxDistance:3_000_000 }); return; }
        if (_args.ContainsKey("environment-test")) { AddChild(new EnvironmentChecks()); return; }
        if (_args.ContainsKey("model-test")) { AddChild(new ShipModelChecks()); return; }
        if (_args.ContainsKey("am-game-test")) { AddChild(new AntimatterGameChecks()); return; }
        if (_args.ContainsKey("tracking-assist-test")) { AddChild(new WeaponInputChecks { TrackingOnly=true }); return; }
        if (_args.ContainsKey("counter-missile-input-test")) { AddChild(new WeaponInputChecks { CounterMissileOnly=true }); return; }
        if (_args.ContainsKey("sensor-coverage-test")) { AddChild(new SensorCoverageChecks()); return; }
        if (_args.ContainsKey("weapon-input-test")) { AddChild(new WeaponInputChecks()); return; }
        if (_args.ContainsKey("damage-control-test")) { AddChild(new WeaponInputChecks { DamageControlOnly=true }); return; }
        if (_args.ContainsKey("telescope-test")) { AddChild(new TelescopeChecks()); return; }
        if (_args.ContainsKey("navigation-test")) { AddChild(new NavigationChecks()); return; }
        if (_args.ContainsKey("contact-test")) { AddChild(new ContactMemoryChecks()); return; }
        if (_args.ContainsKey("feedback-test")) { AddChild(new CombatFeedbackChecks()); return; }
        if (_args.ContainsKey("audio-test")) { AddChild(new CombatAudioChecks()); return; }
        if (_args.ContainsKey("fleet-audio-test")) { AddChild(new FleetAudioChecks()); return; }
        if (_args.ContainsKey("shot") && !_args.ContainsKey("menu") && !_args.ContainsKey("battle") && !_args.ContainsKey("match") && !_args.ContainsKey("result-test"))
        { AddChild(new ScaleTest()); return; }
        Input.MouseMode = Input.MouseModeEnum.Visible;
        var layer = new CanvasLayer { Layer = 10 }; AddChild(layer);
        _overlay = new BattleOverlay { Host = this }; layer.AddChild(_overlay);
        string menu = _args.GetValueOrDefault("menu", "title");
        if(_args.ContainsKey("match"))BeginMatch(useRequestedSeed:true);
        else if (menu == "select") ChangeScreen(BattleScreen.Select);
        else if (menu == "hangar") ChangeScreen(BattleScreen.Hangar);
        else if (menu == "research") OpenResearch();
        else if (menu == "result" || _args.ContainsKey("result-test"))
        {
            StartBattle(_args.GetValueOrDefault("control", "BB-01"), useRequestedSeed:true);
            Battle!.AdvanceBattle(900);
            _fixtureOutcome = new(_args.GetValueOrDefault("result", "win") == "loss" ? Faction.Red : Faction.Blue, "검증용 판정", Battle.World.Rules!.Outcome?.Time ?? Battle.World.Time);
            Battle.BeginSpectating(); ChangeScreen(BattleScreen.Result);
        }
        else if (_args.ContainsKey("battle") || _args.ContainsKey("autoplay"))
            StartBattle(_args.GetValueOrDefault("control", "IC-21"), useRequestedSeed:true);
        else ChangeScreen(BattleScreen.Title);
    }
    public void ChangeScreen(BattleScreen screen)
    {
        Screen = screen;
        if (Battle is not null) Battle.RosterEnabled = screen == BattleScreen.Battle;
        if (screen != BattleScreen.Battle) { Battle?.CloseRoster(); Input.MouseMode = Input.MouseModeEnum.Visible; }
        _overlay.Rebuild();
    }
    public void StartBattle(string role, bool useRequestedSeed = false, MatchLineup? match = null, int? matchSeed = null)
    {
        if (Battle is not null) { RemoveChild(Battle); Battle.QueueFree(); }
        _fixtureOutcome = null; _resultDelay = 0; Role = role;
        Seed = matchSeed ?? (useRequestedSeed && _args.TryGetValue("seed", out string? seed) ? int.Parse(seed)
            : System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, int.MaxValue));
        (BattleObjective Objective,Battlefield Field,int FieldSeed,Engagement Course,Elevation Height) setup=match is not null&&_matchSetup is { } chosen?chosen:(SelectedObjective,SelectedBattlefield,FieldSeed,Engagement.Auto,Elevation.Auto);
        Battle = new ScaleTest { BattleMode = true, DevMode = _args.ContainsKey("dev"), AutoPlay = _args.ContainsKey("autoplay"),
            LaunchConfig = new BattleConfig { Seed=Seed, BlueDesign=PlayerDesign, RedDesign=EnemyDesign, Objective=setup.Objective,
                Battlefield=setup.Field,FieldSeed=setup.FieldSeed,Engagement=setup.Course,Elevation=setup.Height,Match=match }, LaunchControl=role };
        Battle.PauseRequested += TogglePause;
        Battle.SquadronLost += () => ChangeScreen(BattleScreen.Eliminated);
        Battle.ControlRecovered += () => ChangeScreen(BattleScreen.Battle);
        AddChild(Battle); ChangeScreen(BattleScreen.Battle);
        if (_args.TryGetValue("advance", out string? advance)) Battle.AdvanceBattle(double.Parse(advance, System.Globalization.CultureInfo.InvariantCulture));
        if (_args.ContainsKey("respawn-test")) Battle.Controlled!.Body.Damage.Breakup(Battle.World.Time, "검증");
        if (_args.GetValueOrDefault("menu") == "pause") TogglePause();
    }
    public void TogglePause()
    {
        if (Battle is null || Screen is BattleScreen.Result or BattleScreen.Eliminated) return;
        Battle.Paused = !Battle.Paused;
        ChangeScreen(Battle.Paused ? BattleScreen.Pause : BattleScreen.Battle);
    }
    public void SelectRole()
    {
        PendingMatch=null;
        if (Battle is not null) { RemoveChild(Battle); Battle.QueueFree(); Battle=null; }
        ChangeScreen(BattleScreen.Select);
    }
    public override void _Process(double delta)
    {
        if (_overlay is null) return;
        TrackPerformance(delta);
        if(_args.ContainsKey("flow-test"))CheckSceneFlow();
        if(_args.ContainsKey("match-flow-test"))CheckMatchFlow();
        if(_args.ContainsKey("hangar-test"))CheckHangarFlow();
        if(_args.ContainsKey("progression-test"))CheckProgressionFlow();
        StepMatching(delta);
        PreviewMatch();
        // Isolated screenshot fixtures; --shot profiles never read or save the player's ledger.
        if(_args.ContainsKey("shot")&&_frame==60&&_args.TryGetValue("menu-confirm",out var confirmKind))
        {
            if(_overlay.HangarView is {} hangar)
            {
                Hangar.Research!.Points[ShipDefinitions.ById(PlayerHullId).Design]=1200;
                if(confirmKind=="equipment")hangar.Install("shield-capacity");
                else {hangar.SelectSystem(ComponentSlot.MainGun);hangar.InstallSystem("main-triple",true);}
            }
            else if(_overlay.ResearchView is {} research)
            {
                if(confirmKind=="hull"){research.SelectNode("escort");research.Purchase();}
                else research.Purchase("drive");
            }
        }
        if(_args.ContainsKey("shot")&&_frame==60&&_args.TryGetValue("menu-details",out var detailKey))
        {
            foreach(var button in _overlay.FindChildren("*","Button",true,false).OfType<MenuItemButton>())
                if(button.GetMeta("focus_key","").AsString()==detailKey){button.ShowDetails();break;}
        }
        if (Screen is BattleScreen.Battle or BattleScreen.Eliminated && Outcome is not null)
        {
            if (_resultDelay == 0) {ClaimResearchReward();Battle!.BeginSpectating();}
            Battle!.SetBattleSpeed(1); _resultDelay += delta;
            if (_resultDelay >= 2) { Battle.BeginSpectating(); ChangeScreen(BattleScreen.Result); }
        }
        if (_args.TryGetValue("shot", out string? path) && ++_frame >= int.Parse(_args.GetValueOrDefault("frames", "120")))
        {
            GetViewport().GetTexture().GetImage().SavePng(path); GD.Print($"shot saved: {path}"); GetTree().Quit();
        }
    }
    public void Quit() => GetTree().Quit();
    public override void _UnhandledInput(InputEvent e)
    {
        var confirmation=_overlay?.HangarView is {} hangar?MenuConfirmation.Pending(hangar):
            _overlay?.ResearchView is {} research?MenuConfirmation.Pending(research):null;
        if(confirmation is not null)
        {
            if(e.IsActionPressed(InputSetup.ReleaseMouse))confirmation.Cancel();
            GetViewport().SetInputAsHandled();return;
        }
        if (e.IsActionPressed(InputSetup.ToggleMute))
        { SoundSettings.ToggleMute(); _overlay?.Rebuild(); GetViewport().SetInputAsHandled(); }
        else if(Screen==BattleScreen.Pause && e.IsActionPressed(InputSetup.ReleaseMouse))
        {TogglePause();GetViewport().SetInputAsHandled();}
        else if(Screen==BattleScreen.Hangar && e.IsActionPressed(InputSetup.ReleaseMouse))
        {if(_overlay?.HangarView?.CloseOverlay()!=true)ReturnToPort();GetViewport().SetInputAsHandled();}
        else if(Screen==BattleScreen.Research && e.IsActionPressed(InputSetup.ReleaseMouse))
        {OpenHangar();GetViewport().SetInputAsHandled();}
    }
}
