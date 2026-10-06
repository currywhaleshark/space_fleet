using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace SpaceFleet.Game;

public partial class Main
{
    private double _fpsSeconds, _fpsMin=double.PositiveInfinity;
    private int _fpsFrames, _resultPerfFrames;
    private bool _perfStart, _perfTen, _perfOutcome, _perfFinished;
    private void TrackPerformance(double delta)
    {
        if(!_args.ContainsKey("autoplay")||Battle is null)return;
        if(delta>0){_fpsSeconds+=delta;_fpsFrames++;_fpsMin=Math.Min(_fpsMin,1/delta);}
        if(!_perfStart&&_fpsFrames>=120){_perfStart=true;PerformanceSnapshot("start");}
        if(!_perfTen&&Battle.World.Time>=600){_perfTen=true;PerformanceSnapshot("10min");}
        if(!_perfOutcome&&Outcome is not null){_perfOutcome=true;PerformanceSnapshot("outcome");}
        if(_perfFinished||Screen!=BattleScreen.Result||!_args.ContainsKey("autoplay-quit"))return;
        if(++_resultPerfFrames<3)return;
        SavePerformanceShot("result");
        Battle.AdvanceBattle(double.Parse(_args.GetValueOrDefault("post-seconds","300"),System.Globalization.CultureInfo.InvariantCulture));
        GD.Print($"autoplay after outcome: sim={Battle.World.Time:F2}s projectiles={Battle.World.Projectiles.Count} missiles={Battle.World.Missiles.Count} impacts={Battle.World.Impacts.Count} ordnanceEvents={Battle.World.OrdnanceEvents.Count} battleEvents={Battle.World.Log!.Events.Count} intervals={Battle.World.Log.Intervals.Count}");
        _perfFinished=true;GetTree().Quit();
    }
    private void PerformanceSnapshot(string stage)
    {
        GD.Print($"autoplay {stage}: sim={Battle!.World.Time:F2}s FPS min={_fpsMin:F2} mean={_fpsFrames/Math.Max(_fpsSeconds,.0001):F2} engine={Engine.GetFramesPerSecond():F1}");
        SavePerformanceShot(stage);
    }
    private void SavePerformanceShot(string stage)
    {
        if(!_args.ContainsKey("perf"))return;
        string path=ProjectSettings.GlobalizePath($"res://shots/s7_autoplay_{stage}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);GetViewport().GetTexture().GetImage().SavePng(path);
    }
}
