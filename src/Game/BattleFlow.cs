using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

public partial class ScaleTest
{
    public bool BattleMode { get; init; }
    public bool DevMode { get; init; } = true;
    public BattleConfig? LaunchConfig { get; init; }
    public string? LaunchControl { get; init; }
    public bool AutoPlay { get; set; }
    private bool _paused;
    public bool Paused { get => _paused; set { _paused=value; _audio?.SetPaused(value); if (value) { if(Controlled?.Body is {} ship)SimWorld.StopLasers(ship); _fireReleaseGuard=true; JettisonProgress=0; Camera?.SetFreeLook(false); Camera?.ResetTelescope(); CloseWorldMap(); if(MenuOpen) { _radial.Cancel(); _radialAction=null; } } } }
    public bool Spectating { get; private set; }
    public double BriefRemaining { get; private set; } = 3;
    public double DeathRemaining { get; private set; } = -1;
    public event Action? PauseRequested;
    public event Action? SquadronLost;
    public event Action? ControlRecovered;
    public Squadron? OwnedSquadron { get; private set; }
    public bool SquadronEliminated => OwnedSquadron is { } squad && squad.Members.All(s => s.Damage.Destroyed);
    private ShipView? _spectatedView;
    private bool _awaitingSquadRecovery;
    public ShipView? SpectatedView => Spectating ? CameraView : null;
    private readonly List<ControlSegment> _controlSegments = new();
    private readonly PlayerRecord _record = new();
    private PlayerRecord? _recordStart;
    private ShipBody? _recordShip;
    private double _controlStart;
    public IReadOnlyList<ControlSegment> ControlSegments => _recordShip is null ? _controlSegments
        : _controlSegments.Concat(new[] {new ControlSegment(_recordShip.Callsign,_controlStart,World.Time)}).ToArray();
    public PlayerRecord ResultRecord
    {
        get { var result=_record.Copy(); if(_recordShip is not null && _recordStart is not null)result.AddDifference(PlayerRecord.Sample(_recordShip,World.Log!),_recordStart);return result; }
    }
    private void StartRecord(ShipBody ship)
    { if(!BattleMode)return;CloseRecord();_recordShip=ship;_controlStart=World.Time;_recordStart=PlayerRecord.Sample(ship,World.Log!); }
    private void CloseRecord()
    {
        if(_recordShip is null||_recordStart is null)return;
        _record.AddDifference(PlayerRecord.Sample(_recordShip,World.Log!),_recordStart);
        _controlSegments.Add(new(_recordShip.Callsign,_controlStart,World.Time));_recordShip=null;_recordStart=null;
    }
    private bool HandleBattleInput(InputEvent e)
    {
        if(!BattleMode)return false;
        if(Paused||Spectating)return true;
        if(e.IsActionPressed(InputSetup.ReleaseMouse))
        {
            Camera.ResetTelescope();
            if(Scheme==ControlScheme.Pilot&&Input.MouseMode==Input.MouseModeEnum.Captured)Input.MouseMode=Input.MouseModeEnum.Visible;
            else PauseRequested?.Invoke();
            GetViewport().SetInputAsHandled();return true;
        }
        if(!DevMode&&new[]{InputSetup.SwitchShip,InputSetup.ToggleOrigin,InputSetup.JumpFar,InputSetup.TestFire,InputSetup.ShowModules,
            InputSetup.Repair,InputSetup.Practice,InputSetup.MissileDrill,InputSetup.OrderAttack,
            InputSetup.OrderEscort,InputSetup.OrderHold}.Any(action=>e.IsActionPressed(action)))return true;
        return false;
    }
    private void StepBattleFlow(double delta)
    {
        if(!BattleMode)return;
        BriefRemaining=Math.Max(0,BriefRemaining-delta);
        if (Spectating && _awaitingSquadRecovery && World.Rules?.Outcome is null && OwnedSquadron is { } owned
            && owned.Members.FirstOrDefault(Squadron.Active) is { } recovered)
        {
            _awaitingSquadRecovery=false;Spectating=false; AutoPlay=false; SelectControl(recovered.Callsign);
            ControlRecovered?.Invoke(); return;
        }
        if(Spectating||AutoPlay||Controlled?.Body is not { } body)return;
        if(Squadron.Active(body) || body.DamageControl.RecoveringDisable) {DeathRemaining=-1;return;}
        if(DeathRemaining<0) {DeathRemaining=3;if(MenuOpen)_radial.Cancel();Input.MouseMode=Input.MouseModeEnum.Visible;}
        else DeathRemaining-=delta;
        if(DeathRemaining>0)return;
        ShipBody? next=BattleRules.Replacement(OwnedSquadron?.Members ?? new[]{body},body.Faction,body.Class.Kind);
        if(next is null){BeginSpectating(!SquadronEliminated);SquadronLost?.Invoke();}else{SelectControl(next.Callsign);DeathRemaining=-1;}
    }
    private void EnableAutoPlay()
    {
        foreach(var ship in World.Ships)
        {if(Squadron.Active(ship))World.AttachBrain(ship,ShipOrder.HoldAt(ship.Position));ship.Gunnery=null;}
        foreach(var squad in World.Squadrons)squad.PlayerLed=false;
        Input.MouseMode=Input.MouseModeEnum.Visible;
    }
    public void BeginSpectating(bool waitForRecovery=false)
    {
        if(Spectating)return;
        _awaitingSquadRecovery=waitForRecovery;
        CloseRecord();if(MenuOpen)_radial.Cancel();
        Controlled?.Body.Ordnance.Antimatter.Cancel();
        if(Controlled is { } old)
        {
            old.Body.Gunnery=null;
            if(Squadron.Active(old.Body))World.AttachBrain(old.Body,ShipOrder.HoldAt(old.Body.Position));
        }
        Spectating=true;AutoPlay=true;
        if(OwnedSquadron is { } owned)owned.PlayerLed=false;
        DeathRemaining=-1;BriefRemaining=0;
        CloseWorldMap();CloseRoster();_audio.Prime(World,null);Feedback.ClearPresentation();Camera.ClearImpacts();Camera.ResetTelescope();
        Input.MouseMode=Input.MouseModeEnum.Visible;Camera.Mode=CameraMode.ShipFollow;CycleSpectator(1);
    }
    public void CycleSpectator(int step)
    {
        if(!Spectating)return;
        var candidates=Views.Where(v=>v.Body.Faction==Faction.Blue&&!v.Body.Damage.Destroyed).ToArray();
        if(candidates.Length==0){_spectatedView=null;_audio.Prime(World,null);return;}
        int index=Array.IndexOf(candidates,_spectatedView);
        _spectatedView=candidates[(index+step+candidates.Length)%candidates.Length];
        Camera.ResetAim(_spectatedView.Body.Orientation);Camera.ClearImpacts();
        // Handoff snapshots counters so earlier salvos and impacts cannot replay.
        _audio.Prime(World,_spectatedView.Body);
    }
    public void AdvanceBattle(double seconds)
    {
        bool autoplay=AutoPlay;AutoPlay=true;EnableAutoPlay();
        double end=World.Time+seconds;while(World.Time<end)World.Step();
        UpdateContacts();
        _audio.Prime(World,AudioShip);
        Camera.ClearImpacts();
        AutoPlay=autoplay;BriefRemaining=0;
        if(!AutoPlay&&Controlled is { } me){World.DetachBrain(me.Body);MarkPlayerSquadron();SetupGunnery(null,me.Body);ApplySquadOrder();}
    }
    public void SetBattleSpeed(int speed)
    {_timeScaleIndex=Math.Max(0,Array.IndexOf(AvailableTimeScales,speed));}
    private ShipView CameraView => Spectating ? _spectatedView is {Body.Damage.Destroyed:false} ? _spectatedView
        : _spectatedView=Views.FirstOrDefault(v=>Squadron.Active(v.Body)&&v.Body.Faction==Faction.Blue)
            ?? Views.FirstOrDefault(v=>!v.Body.Damage.Destroyed&&v.Body.Faction==Faction.Blue) ?? Controlled! : Controlled!;
}
