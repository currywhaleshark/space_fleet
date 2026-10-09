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
    private SimWorld _driveWorld = null!;
    private ShipBody _driveShip = null!;
    private int _variantStep;
    private ulong _nextVariantAt;
    private readonly int[] _lastVariants = { -1, -1, -1, -1 };

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
            Check(_outgoing.AssetsReady && _incoming.AssetsReady, "All impact banks, propulsion loops and PD WAVs load");
            Check(_outgoing.GetChildren().OfType<AudioStreamPlayer>().All(p => p.Stream.GetLength() > .1), "Loaded streams contain useful audio");
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
                var active = _incoming.GetChildren().OfType<AudioStreamPlayer>().Where(p=>p.Playing).ToArray();
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
                Check(_incoming.GetChildren().OfType<AudioStreamPlayer>().Sum(p=>p.MaxPolyphony)==16, "Fourteen one-shot voices plus two loops stay bounded");
                _stage++;
            }
            else if (_stage == 8 && elapsed >= 5_800)
            {
                var droneWorld=new SimWorld();
                var pilot=droneWorld.Add(new ShipBody("DRONE-PILOT",ShipClass.Interceptor,Faction.Blue));
                var carrier=droneWorld.Add(new ShipBody("DRONE-ENEMY",ShipClass.Battleship,Faction.Red));
                droneWorld.Log=new BattleLog(droneWorld); _outgoing.Prime(droneWorld,pilot);
                int confirms=_outgoing.PlayCount(CombatSound.HitConfirm);
                droneWorld.DamageDrone(pilot,carrier,0,100,0); _outgoing.Observe(droneWorld,pilot);
                Check(_outgoing.PlayCount(CombatSound.HitConfirm)==confirms+1,"Drone kill plays the existing confirmation cue");
                _outgoing.Observe(droneWorld,pilot);
                Check(_outgoing.PlayCount(CombatSound.HitConfirm)==confirms+1,"Repeated observation does not replay a drone kill");
                _outgoing.SetPaused(true); droneWorld.DamageDrone(pilot,carrier,1,100,0); _outgoing.Observe(droneWorld,pilot);
                _outgoing.SetPaused(false); _outgoing.Observe(droneWorld,pilot);
                Check(_outgoing.PlayCount(CombatSound.HitConfirm)==confirms+1,"Paused drone kills do not replay on resume");
                _outgoing.Prime(droneWorld,pilot); _outgoing.Observe(droneWorld,pilot);
                Check(_outgoing.PlayCount(CombatSound.HitConfirm)==confirms+1,"Ship handover primes drone kill history");
                BeginPropulsion(); _stage++;
            }
            else if (_stage == 9 && elapsed >= 9_600)
            {
                Check(_incoming.EnginePlaying && _incoming.EngineLevel>.99f && !_incoming.BoostPlaying,
                    "Sustained thrust keeps the engine loop running beyond its first cycle");
                var engine=_incoming.GetNode<AudioStreamPlayer>("engine_drive_loop");
                Check(engine.VolumeDb>-3,"Full thrust uses an audible mix level rather than the previous heavily attenuated gain");
                Check(engine.Stream is AudioStreamWav { LoopMode: AudioStreamWav.LoopModeEnum.Forward }
                    && engine.GetPlaybackPosition()<engine.Stream.GetLength(),"Imported engine stream loops inside its duration");
                _driveShip.Control=new ShipControl { FlightAssist=false,Thrust=Vector3.Back,Boost=true };
                _driveWorld.Step(); _incoming.Observe(_driveWorld,_driveShip); _incoming.AdvanceLoops(.3f);
                Check(_incoming.BoostPlaying && _incoming.BoostLevel>.8f,"Actual boost adds its own sustained layer");
                _stage++;
            }
            else if (_stage == 10 && elapsed >= 12_600)
            {
                Check(_incoming.BoostPlaying,"Boost remains audible across its loop boundary");
                Check(_incoming.GetNode<AudioStreamPlayer>("boost_drive_loop").VolumeDb>-3,"Boost has an audible sustained mix level");
                float engine=_incoming.EngineLevel,boost=_incoming.BoostLevel;
                _incoming.SetPaused(true); _incoming.AdvanceLoops(1);
                Check(_incoming.EngineLevel==engine && _incoming.BoostLevel==boost
                    && _incoming.GetChildren().OfType<AudioStreamPlayer>().Where(p=>p.Playing).All(p=>p.StreamPaused),
                    "Pause freezes both propulsion envelopes and playback");
                _incoming.SetPaused(false);
                _driveShip.Control=new ShipControl { FlightAssist=false };
                _driveWorld.Step(); _incoming.Observe(_driveWorld,_driveShip); _incoming.AdvanceLoops(.1f);
                Check(_incoming.EngineLevel>0 && _incoming.EngineLevel<engine && _incoming.BoostLevel<boost,
                    "Release fades propulsion smoothly rather than chopping the waveform");
                _stage++;
            }
            else if (_stage == 11 && elapsed >= 14_700)
            {
                Check(_driveShip.Velocity.Length()>1 && _incoming.EnginePlaying && _incoming.EngineLevel>.3f
                    && _incoming.EngineLevel<.5f && !_incoming.BoostPlaying,
                    "Coasting settles to a quieter machinery bed while boost fades out");
                CheckIdleAndCruise(); CheckPropulsionCleanup(); CheckPointDefense();
                _incoming.Prime(_world,_red); _nextVariantAt=elapsed; _stage++;
            }
            else if (_stage == 12 && elapsed >= _nextVariantAt)
            {
                int kindIndex=_variantStep/4;
                HitKind kind=new[]{HitKind.Shield,HitKind.Armor,HitKind.Penetration,HitKind.Critical}[kindIndex];
                CombatSound sound=new[]{CombatSound.ShieldImpact,CombatSound.ArmorImpact,CombatSound.Penetration,CombatSound.Critical}[kindIndex];
                _red.Damage.Reset();
                int before=_incoming.PlayCount(sound);
                CombatFeedbackChecks.Strike(_world,_blue,_red,kind,(uint)(10000+_variantStep));
                _incoming.Observe(_world,_red);
                Check(_incoming.PlayCount(sound)==before+1,$"New {kind} damage event produces a sound");
                int variant=_incoming.LastVariant(sound);
                Check(variant>=0 && variant<4 && variant!=_lastVariants[kindIndex],$"{kind} bank never repeats its immediately previous sample");
                _lastVariants[kindIndex]=variant;
                _incoming.Observe(_world,_red);
                Check(_incoming.PlayCount(sound)==before+1,"Variant changes do not replay retained damage events");
                _nextVariantAt=elapsed+260;
                if(++_variantStep==16) { _stage++; _nextVariantAt=elapsed+2200; }
            }
            else if (_stage == 13 && elapsed >= _nextVariantAt)
            {
                Finish(); _stage++;
            }
        }
        catch(Exception error){Fail(error);}
    }
    private void BeginPropulsion()
    {
        _driveWorld=new SimWorld();
        _driveShip=_driveWorld.Add(new ShipBody("AUDIO-ENGINE",ShipClass.Interceptor,Faction.Blue));
        _driveShip.Control=new ShipControl { FlightAssist=false,Thrust=Vector3.Back };
        _incoming.Prime(_driveWorld,_driveShip);
        for(int tick=0;tick<30;tick++) _driveWorld.Step();
        _incoming.Observe(_driveWorld,_driveShip); _incoming.AdvanceLoops(.08f);
        Check(_incoming.EnginePlaying && _incoming.EngineLevel>0 && _incoming.EngineLevel<.8f,
            "Engine output attacks smoothly from silence");
        Check(!_incoming.BoostPlaying,"Normal thrust does not activate boost sound");
    }

    private void CheckIdleAndCruise()
    {
        _driveShip.Velocity=Vector3.Zero;
        _driveShip.Control=new ShipControl { FlightAssist=false };
        _driveWorld.Step(); _incoming.Observe(_driveWorld,_driveShip); _incoming.AdvanceLoops(2);
        Check(_driveShip.EngineOutput==0 && _incoming.EnginePlaying && !_incoming.BoostPlaying,
            "A stationary powered ship retains its pre-battle machinery ambience");
        float idleDb=_incoming.GetNode<AudioStreamPlayer>("engine_drive_loop").VolumeDb;
        Check(idleDb>-12 && idleDb<-8,"Idle is present but lower than powered acceleration");
        _driveShip.Control=new ShipControl { FlightAssist=true,Thrust=Vector3.Back };
        _driveShip.Velocity=Vector3.Forward*_driveShip.Class.MaxSpeed;
        _driveWorld.Step(); _incoming.Observe(_driveWorld,_driveShip); _incoming.AdvanceLoops(2);
        Check(_driveShip.EngineOutput<.01f && _incoming.EngineLevel>.6f && _incoming.EngineLevel<.85f
            && !_incoming.BoostPlaying,"Cruise remains audible after assisted acceleration reaches zero");
        Check(_incoming.GetNode<AudioStreamPlayer>("engine_drive_loop").VolumeDb>idleDb+4,
            "Held cruise throttle is clearly stronger than idle");
        _driveShip.Control=new ShipControl { FlightAssist=true,Thrust=Vector3.Back,Boost=true };
        _driveShip.Velocity=Vector3.Forward*(_driveShip.Class.MaxSpeed*_driveShip.Class.BoostMultiplier);
        _driveWorld.Step(); _incoming.Observe(_driveWorld,_driveShip); _incoming.AdvanceLoops(.5f);
        Check(_driveShip.EngineOutput<.01f && _incoming.BoostPlaying && _incoming.BoostLevel>.7f,
            "Held boost remains audible at the assisted boosted speed cap");
        _driveShip.Control=new ShipControl { FlightAssist=false,Boost=true };
        _driveWorld.Step(); _incoming.Observe(_driveWorld,_driveShip); _incoming.AdvanceLoops(2);
        Check(!_incoming.BoostPlaying && _incoming.EnginePlaying,"Boost key without forward demand does not add a permanent booster roar");
        foreach(var module in _driveShip.Damage.Modules.Where(m=>m.Definition.Kind is ModuleKind.Generator or ModuleKind.Reactor))
            _driveShip.Damage.Hurt(module,module.Health,_driveWorld.Time,0,0);
        _incoming.AdvanceLoops(2);
        Check(!_incoming.EnginePlaying && !_incoming.BoostPlaying,"Power failure also silences the idle machinery bed");
        _driveShip.Damage.Reset();
    }

    private void CheckPropulsionCleanup()
    {
        _driveShip.Control=new ShipControl { FlightAssist=false,Thrust=Vector3.Back,Boost=true };
        _driveWorld.Step(); _incoming.Observe(_driveWorld,_driveShip); _incoming.AdvanceLoops(.5f);
        Check(_incoming.EnginePlaying && _incoming.BoostPlaying,"Re-engaging thrust restarts both loops");
        foreach(var module in _driveShip.Damage.Modules.Where(m=>m.Definition.Kind==ModuleKind.Thruster))
            _driveShip.Damage.Hurt(module,module.Health,_driveWorld.Time,0,0);
        _incoming.AdvanceLoops(2);
        Check(!_incoming.EnginePlaying && !_incoming.BoostPlaying,"Engine module destruction silences propulsion without a fresh observation");
        _driveShip.Damage.Reset(); _driveWorld.Step(); _incoming.Observe(_driveWorld,_driveShip); _incoming.AdvanceLoops(.5f);
        Check(_incoming.EnginePlaying,"Repaired propulsion responds to live thrust again");
        _driveShip.Damage.Catastrophe(_driveWorld.Time,"audio fixture"); _incoming.AdvanceLoops(2);
        Check(!_incoming.EnginePlaying && !_incoming.BoostPlaying,"Destroyed ship cannot leave engine audio running");
        _driveShip.Damage.Reset(); _driveWorld.Step(); _incoming.Observe(_driveWorld,_driveShip); _incoming.AdvanceLoops(.5f);
        _incoming.Prime(_driveWorld,null);
        Check(!_incoming.EnginePlaying && !_incoming.BoostPlaying && _incoming.EngineLevel==0,"Handover/observer mode clears old propulsion immediately");
        _incoming.Prime(_driveWorld,_driveShip); _incoming.Observe(_driveWorld,_driveShip); _incoming.AdvanceLoops(.5f);
        var restarted=new SimWorld(); restarted.Add(_driveShip);
        _incoming.Observe(restarted,_driveShip);
        Check(!_incoming.EnginePlaying && !_incoming.BoostPlaying,"Rewinding the world resets propulsion and event history");
        _incoming.Observe(restarted,_driveShip); _incoming.AdvanceLoops(.5f); _incoming.StopAll(); _incoming.AdvanceLoops(1);
        Check(!_incoming.EnginePlaying && !_incoming.BoostPlaying,"StopAll cannot restart a stale engine loop next frame");
    }

    private void CheckPointDefense()
    {
        var world=new SimWorld();
        var carrier=world.Add(new ShipBody("AUDIO-PD",ShipClass.Battleship,Faction.Blue));
        var enemy=world.Add(new ShipBody("AUDIO-PD-TARGET",ShipClass.Interceptor,Faction.Red));
        carrier.Control=enemy.Control=new ShipControl { FlightAssist=false };
        enemy.Place(new Vec3d(1400,900,-1800),Quaternion.Identity);
        _outgoing.Prime(world,carrier);
        int before=_outgoing.PlayCount(CombatSound.PointDefense);
        for(int tick=0;tick<180;tick++) { enemy.Damage.Reset(); world.Step(); }
        Check(carrier.Ordnance.PointDefense.Any(p=>p.ShotCount>0),"Real mechanical PD mounts acquire and fire");
        _outgoing.Observe(world,carrier);
        Check(_outgoing.PlayCount(CombatSound.PointDefense)==before+1,"PD shot counters trigger a short sound without a battle log");
        for(int i=0;i<20;i++) _outgoing.Observe(world,carrier);
        Check(_outgoing.PlayCount(CombatSound.PointDefense)==before+1,"Retained tracers cannot retrigger the PD burst");
        _outgoing.SetPaused(true);
        for(int tick=0;tick<90;tick++) { enemy.Damage.Reset(); world.Step(); }
        _outgoing.Observe(world,carrier); _outgoing.SetPaused(false); _outgoing.Observe(world,carrier);
        Check(_outgoing.PlayCount(CombatSound.PointDefense)==before+1,"Paused PD events are consumed without replay on resume");
        _outgoing.Prime(world,carrier); _outgoing.Observe(world,carrier);
        Check(_outgoing.PlayCount(CombatSound.PointDefense)==before+1,"Control handover skips earlier PD salvos");
        enemy.Place(new Vec3d(0,0,-1000000),Quaternion.Identity); world.Step(); _outgoing.Observe(world,carrier);
        Check(_outgoing.PlayCount(CombatSound.PointDefense)==before+1,"Tracking stops sounding when nothing is fired");
        world.ResetWeapons(); _outgoing.Observe(world,carrier);
        Check(_outgoing.PlayCount(CombatSound.PointDefense)==before+1,"Counter reset is not mistaken for a new salvo");
    }

    private void Finish()
    {
        _recording.SetRecordingActive(false);
        using var mixed=_recording.GetRecording();
        Check(mixed is not null && mixed.Data.Length>4000,"Godot mixer produces recorded PCM");
        byte[] data=mixed!.Data; int peak=0;
        for(int i=0;i+1<data.Length;i+=2) peak=Math.Max(peak,Math.Abs((int)BitConverter.ToInt16(data,i)));
        Check(peak>100 && peak<31130,"Combined propulsion/PD/impacts stay below clipping");
        string recordingPath=OS.HasFeature("editor")?"res://shots/audio_mix.wav":"user://audio_mix.wav";
        Check(mixed.SaveToWav(recordingPath)==Error.Ok,"Mixed WAV saves");
        Restore(); GD.Print($"PASS: {_checks} audio checks; recorded peak {peak/32768f:0.000}"); GetTree().Quit();
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
