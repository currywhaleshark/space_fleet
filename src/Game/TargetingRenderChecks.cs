using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

/// <summary>Isolated real-render workload: same seed, tick rate and fleet for viewport A/B.</summary>
public partial class TargetingRenderChecks : Node
{
    public override void _Ready()
    {
        InputSetup.Register();
        var args=BattleArgs.Parse(OS.GetCmdlineUserArgs());
        bool large=args.GetValueOrDefault("render-count")=="80";
        var lineup=large?Matchmaking.Create(91,FleetLoadouts.Default("battleship")):null;
        var game=new ScaleTest{BattleMode=true,LaunchControl=lineup?.Player.Callsign(0)??"BB-01",
            LaunchConfig=new BattleConfig{Seed=91,Match=lineup,StartDistance=30000,Engagement=Engagement.HeadOn,Elevation=Elevation.Level}};
        AddChild(game);
        var hud=game.GetNode<Hud>("HudLayer/Hud");
        hud.TargetPreview.PreviewEnabled=!args.ContainsKey("preview-off");
        var target=game.Views.First(v=>v.Body.Faction!=game.Controlled!.Body.Faction&&v.Body.Class.Kind==HullKind.Battleship);
        game.SelectMapContact(target);
        GD.Print($"targeting-render: ships={game.World.Ships.Count} preview={hud.TargetPreview.PreviewEnabled} target={target.Body.Callsign}");
        PerfTrace.Enable(12);
    }
}
