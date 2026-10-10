using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public enum BattleScreen { Title, Select, Battle, Pause, Result }
public partial class Main : Node3D
{
    public BattleScreen Screen { get; private set; } = BattleScreen.Title;
    public ScaleTest? Battle { get; private set; }
    public int Seed { get; private set; }
    public string Role { get; private set; } = "IC-21";
    public DesignFamily PlayerDesign { get; private set; } = DesignFamily.Earth;
    public DesignFamily EnemyDesign { get; private set; } = DesignFamily.Mars;
    public void ToggleDesign(bool enemy)
    {
        if (enemy) EnemyDesign = EnemyDesign == DesignFamily.Earth ? DesignFamily.Mars : DesignFamily.Earth;
        else PlayerDesign = PlayerDesign == DesignFamily.Earth ? DesignFamily.Mars : DesignFamily.Earth;
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
        if (_args.TryGetValue("blue-design", out var blue)) PlayerDesign = Enum.Parse<DesignFamily>(blue, true);
        if (_args.TryGetValue("red-design", out var red)) EnemyDesign = Enum.Parse<DesignFamily>(red, true);
        InputSetup.Register();
        SoundSettings.Initialize();
        FeedbackSettings.Load();
        if(_args.ContainsKey("teaser") || _args.ContainsKey("teaser-preview")) {
            AddChild(new CinematicTeaser { Options=_args }); return; }
        if (_args.ContainsKey("gallery")) { AddChild(ModelGallery.FromArgs(_args)); return; }
        if(_args.ContainsKey("combat-visual-test") || _args.ContainsKey("fx-preview")) {
            AddChild(new CombatVisualChecks { Preview=_args.GetValueOrDefault("fx-preview"),Output=_args.GetValueOrDefault("shot"),
                PreviewAge=float.TryParse(_args.GetValueOrDefault("fx-age"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float age)?age:.12f }); return; }
        if (_args.ContainsKey("model-test")) { AddChild(new ShipModelChecks()); return; }
        if (_args.ContainsKey("am-game-test")) { AddChild(new AntimatterGameChecks()); return; }
        if (_args.ContainsKey("weapon-input-test")) { AddChild(new WeaponInputChecks()); return; }
        if (_args.ContainsKey("telescope-test")) { AddChild(new TelescopeChecks()); return; }
        if (_args.ContainsKey("navigation-test")) { AddChild(new NavigationChecks()); return; }
        if (_args.ContainsKey("contact-test")) { AddChild(new ContactMemoryChecks()); return; }
        if (_args.ContainsKey("feedback-test")) { AddChild(new CombatFeedbackChecks()); return; }
        if (_args.ContainsKey("audio-test")) { AddChild(new CombatAudioChecks()); return; }
        if (_args.ContainsKey("fleet-audio-test")) { AddChild(new FleetAudioChecks()); return; }
        if (_args.ContainsKey("shot") && !_args.ContainsKey("menu") && !_args.ContainsKey("battle") && !_args.ContainsKey("result-test"))
        { AddChild(new ScaleTest()); return; }
        Input.MouseMode = Input.MouseModeEnum.Visible;
        var layer = new CanvasLayer { Layer = 10 }; AddChild(layer);
        _overlay = new BattleOverlay { Host = this }; layer.AddChild(_overlay);
        string menu = _args.GetValueOrDefault("menu", "title");
        if (menu == "select") ChangeScreen(BattleScreen.Select);
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
        if (screen != BattleScreen.Battle) Input.MouseMode = Input.MouseModeEnum.Visible;
        _overlay.Rebuild();
    }
    public void StartBattle(string role, bool useRequestedSeed = false)
    {
        if (Battle is not null) { RemoveChild(Battle); Battle.QueueFree(); }
        _fixtureOutcome = null; _resultDelay = 0; Role = role;
        Seed = useRequestedSeed && _args.TryGetValue("seed", out string? seed) ? int.Parse(seed)
            : System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, int.MaxValue);
        Battle = new ScaleTest { BattleMode = true, DevMode = _args.ContainsKey("dev"), AutoPlay = _args.ContainsKey("autoplay"),
            LaunchConfig = new BattleConfig { Seed=Seed, BlueDesign=PlayerDesign, RedDesign=EnemyDesign }, LaunchControl=role };
        Battle.PauseRequested += TogglePause;
        AddChild(Battle); ChangeScreen(BattleScreen.Battle);
        if (_args.TryGetValue("advance", out string? advance)) Battle.AdvanceBattle(double.Parse(advance, System.Globalization.CultureInfo.InvariantCulture));
        if (_args.ContainsKey("respawn-test")) Battle.Controlled!.Body.Damage.Breakup(Battle.World.Time, "검증");
        if (_args.GetValueOrDefault("menu") == "pause") TogglePause();
    }
    public void TogglePause()
    {
        if (Battle is null || Screen == BattleScreen.Result) return;
        Battle.Paused = !Battle.Paused;
        ChangeScreen(Battle.Paused ? BattleScreen.Pause : BattleScreen.Battle);
    }
    public void SelectRole()
    {
        if (Battle is not null) { RemoveChild(Battle); Battle.QueueFree(); Battle=null; }
        ChangeScreen(BattleScreen.Select);
    }
    public override void _Process(double delta)
    {
        if (_overlay is null) return;
        TrackPerformance(delta);
        if(_args.ContainsKey("flow-test"))CheckSceneFlow();
        if (Screen == BattleScreen.Battle && Outcome is not null)
        {
            if (_resultDelay == 0) Battle!.BeginSpectating();
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
        if (e.IsActionPressed(InputSetup.ToggleMute))
        { SoundSettings.ToggleMute(); _overlay?.Rebuild(); GetViewport().SetInputAsHandled(); }
        else if(Screen==BattleScreen.Pause && e.IsActionPressed(InputSetup.ReleaseMouse))
        {TogglePause();GetViewport().SetInputAsHandled();}
    }
}
