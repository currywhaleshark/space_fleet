using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

/// <summary>--audio-test: 실제 무기 이벤트와 Godot 믹서 출력까지 확인한다.</summary>
public partial class CombatAudioChecks : Node
{
    private SimWorld _world = null!;
    private ShipBody _blue = null!, _red = null!;
    private CombatAudio _outgoing = null!, _incoming = null!;
    private AudioEffectRecord _recording = null!;
    private ulong _started;
    private int _stage, _checks;
    private float _volume;
    private bool _muted;
    private int _recordEffectIndex = -1;

    private void Check(bool condition, string description)
    { if (!condition) throw new InvalidOperationException(description); _checks++; }

    public override void _Ready()
    {
        try
        {
            _world = new SimWorld();
            _blue = _world.Add(new ShipBody("AUDIO-BLUE", ShipClass.Interceptor, Faction.Blue));
            _red = _world.Add(new ShipBody("AUDIO-RED", ShipClass.Battleship, Faction.Red));
            _red.Place(new Vec3d(0,0,-2_000), Quaternion.Identity);
            _world.Log = new BattleLog(_world);
            _outgoing = new CombatAudio(); _incoming = new CombatAudio();
            AddChild(_outgoing); AddChild(_incoming);
            Check(_outgoing.AssetsReady && _incoming.AssetsReady, "Six runtime WAVs load");
            Check(_outgoing.GetChildren().Cast<AudioStreamPlayer>().All(p => p.Stream.GetLength() > .1), "Loaded streams contain useful audio");
            int buses = AudioServer.BusCount; SoundSettings.Initialize();
            Check(AudioServer.BusCount == buses, "Scene restarts reuse one sound bus");
            _volume = SoundSettings.Volume; _muted = SoundSettings.Muted;
            SoundSettings.SetMuted(false, persist:false); SoundSettings.SetVolume(75, persist:false);
            _outgoing.Prime(_world, _blue); _incoming.Prime(_world, _red);
            _recording = new AudioEffectRecord { Format = AudioStreamWav.FormatEnum.Format16Bits };
            _recordEffectIndex = AudioServer.GetBusEffectCount(0);
            AudioServer.AddBusEffect(0, _recording); _recording.SetRecordingActive(true);
            _started = Time.GetTicksMsec();
        }
        catch (Exception error) { Fail(error); }
    }

    private void Step(int ticks)
    { for (int i=0;i<ticks;i++) { _world.Step(); _outgoing.Observe(_world,_blue); _incoming.Observe(_world,_red); } }

    public override void _Process(double delta)
    {
        try
        {
            ulong elapsed = Time.GetTicksMsec() - _started;
            if (_stage == 0 && elapsed >= 150)
            {
                Check(_world.FireRailgun(_blue,Vector3.Forward).Fired, "Real railgun fires");
                _outgoing.Observe(_world,_blue);
                Check(_outgoing.PlayCount(CombatSound.RailFire)==1, "One shot triggers one discharge");
                Check(_outgoing.GetChild<AudioStreamPlayer>(0).Playing, "Godot starts playback");
                _stage++;
            }
            else if (_stage == 1 && elapsed >= 550)
            {
                Step(45);
                Check(_world.Impacts.Any(i=>i.Hit.Target==_red&&i.Hit.ShieldStopped), "Actual projectile reaches target shield");
                Check(_outgoing.PlayCount(CombatSound.HitConfirm)==1, "Enemy impact triggers hit confirmation");
                Check(_incoming.PlayCount(CombatSound.ShieldImpact)==1&&_incoming.PlayCount(CombatSound.ArmorImpact)==0, "Shield absorption uses shield cue");
                for(int i=0;i<20;i++){_outgoing.Observe(_world,_blue);_incoming.Observe(_world,_red);}
                Check(_outgoing.PlayCount(CombatSound.HitConfirm)==1&&_incoming.PlayCount(CombatSound.ShieldImpact)==1, "Retained impact never repeats each frame");
                _stage++;
            }
            else if (_stage == 2 && elapsed >= 950)
            {
                var active = _incoming.GetChildren().Cast<AudioStreamPlayer>().Where(p=>p.Playing).ToArray();
                Check(active.Length>0,"Pause fixture contains an active voice");
                _incoming.SetPaused(true);
                Check(active.All(p=>p.StreamPaused), "Pause suspends active voices");
                _incoming.SetPaused(false);
                Check(active.All(p=>!p.StreamPaused), "Resume releases voices");
                SoundSettings.SetVolume(35,persist:false);
                Check(Math.Abs(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex(SoundSettings.Bus))-Mathf.LinearToDb(.35f))<.001, "Volume updates sound bus");
                SoundSettings.SetVolume(0,persist:false);
                Check(AudioServer.IsBusMute(AudioServer.GetBusIndex(SoundSettings.Bus)), "Zero volume mutes");
                SoundSettings.SetVolume(75,persist:false); SoundSettings.SetMuted(true,persist:false);
                Check(AudioServer.IsBusMute(AudioServer.GetBusIndex(SoundSettings.Bus)), "Mute applies to sound bus");
                SoundSettings.SetMuted(false,persist:false);
                _stage++;
            }
            else if (_stage == 3 && elapsed >= 1_250)
            {
                _red.Damage.AbsorbShield(1_000_000,_world.Time); _blue.Railgun!.Reset();
                _incoming.Prime(_world,_red);
                Check(_world.FireRailgun(_blue,Vector3.Forward).Fired,"Second projectile fires against spent shield");
                _outgoing.Observe(_world,_blue); Step(45);
                Check(_world.Impacts.Any(i=>i.Hit.Target==_red&&!i.Hit.ShieldStopped),"Actual projectile reaches armor");
                HitKind result = CombatFeedback.Describe(_world.Impacts.Last(i=>i.Shooter==_blue&&i.Hit.Target==_red)).Kind;
                CombatSound expected = result == HitKind.Armor ? CombatSound.ArmorImpact
                    : result == HitKind.Critical ? CombatSound.Critical : CombatSound.Penetration;
                Check(_incoming.PlayCount(expected)==1,"Hull impact uses the actual defense/penetration result");
                Check(_outgoing.PlayCount(CombatSound.HitConfirm)==2,"Second enemy impact is audible");
                int hits=_outgoing.PlayCount(CombatSound.HitConfirm);
                _outgoing.Prime(_world,_red); _outgoing.Observe(_world,_red);
                Check(_outgoing.PlayCount(CombatSound.HitConfirm)==hits,"Control handoff skips historical sounds");
                _stage++;
            }
            else if (_stage == 4 && elapsed >= 1_950)
            {
                var friendly = new SimWorld();
                var me = friendly.Add(new ShipBody("AUDIO-ME",ShipClass.Interceptor,Faction.Blue));
                var ally=friendly.Add(new ShipBody("AUDIO-ALLY",ShipClass.Battleship,Faction.Blue));
                ally.Place(new Vec3d(0,0,-2_000),Quaternion.Identity);friendly.Log=new BattleLog(friendly);
                _outgoing.Prime(friendly,me);Check(friendly.FireRailgun(me,Vector3.Forward).Fired,"Friendly-fire fixture fires");
                _outgoing.Observe(friendly,me);
                for(int i=0;i<45;i++){friendly.Step();_outgoing.Observe(friendly,me);}
                Check(friendly.Impacts.Any(i=>i.Hit.Target==ally),"Friendly target is actually struck");
                Check(_outgoing.PlayCount(CombatSound.HitConfirm)==2,"Friendly fire does not sound like enemy confirmation");
                _stage++;
            }
            else if (_stage == 5 && elapsed >= 2_500)
            {
                int before = _incoming.PlayCount(CombatSound.ArmorImpact);
                CombatFeedbackChecks.Strike(_world, _blue, _red, HitKind.Armor, 9000);
                _incoming.Observe(_world, _red);
                Check(_incoming.PlayCount(CombatSound.ArmorImpact)==before+1, "Blocked armor uses the short metallic cue");
                _stage++;
            }
            else if (_stage == 6 && elapsed >= 3_000)
            {
                int before = _incoming.PlayCount(CombatSound.Penetration), blocked = _incoming.PlayCount(CombatSound.ArmorImpact);
                CombatFeedbackChecks.Strike(_world, _blue, _red, HitKind.Penetration, 9001);
                _incoming.Observe(_world, _red);
                Check(_incoming.PlayCount(CombatSound.Penetration)==before+1, "Penetration plays its deeper layered cue");
                Check(_incoming.PlayCount(CombatSound.ArmorImpact)==blocked, "Penetration does not also trigger a blocked hit");
                _stage++;
            }
            else if (_stage == 7 && elapsed >= 3_600)
            {
                int before = _incoming.PlayCount(CombatSound.Critical);
                CombatFeedbackChecks.Strike(_world, _blue, _red, HitKind.Critical, 9002);
                _incoming.Observe(_world, _red);
                Check(_incoming.PlayCount(CombatSound.Critical)==before+1, "Module destruction plays critical cue");
                Check(_incoming.GetChildren().Cast<AudioStreamPlayer>().Sum(p=>p.MaxPolyphony)==11, "Voice count is bounded");
                _stage++;
            }
            else if (_stage == 8 && elapsed >= 5_800)
            {
                _recording.SetRecordingActive(false);
                using var mixed = _recording.GetRecording();
                Check(mixed is not null&&mixed.Data.Length>4_000,"Godot mixer produces recorded PCM");
                byte[] data=mixed!.Data;int peak=0;
                for(int i=0;i+1<data.Length;i+=2)peak=Math.Max(peak,Math.Abs((int)BitConverter.ToInt16(data,i)));
                Check(peak>100&&peak<31_130,"Mixed audio is non-silent and stays below clipping");
                Check(mixed.SaveToWav("res://shots/audio_mix.wav")==Error.Ok,"Mixed WAV saves");
                Restore();GD.Print($"PASS: {_checks} audio checks; recorded peak {peak/32768f:0.000}");GetTree().Quit();_stage++;
            }
        }
        catch(Exception error){Fail(error);}
    }
    private void Restore()
    {
        SoundSettings.SetVolume(_volume,persist:false);SoundSettings.SetMuted(_muted,persist:false);
        if (_recordEffectIndex >= 0)
        { _recording.SetRecordingActive(false);AudioServer.RemoveBusEffect(0,_recordEffectIndex);_recording.Dispose();_recordEffectIndex=-1; }
    }
    private void Fail(Exception error)
    {Restore();GD.PushError(error.ToString());GetTree().Quit(1);SetProcess(false);}
}
