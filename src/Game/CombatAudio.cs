using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public enum CombatSound { RailFire, ArmorImpact, ShieldImpact, HitConfirm, Penetration, Critical, PointDefense }

/// <summary>조종함의 구조 전달음과 명중 피드백. 시뮬레이션 판정에는 관여하지 않는다.</summary>
public partial class CombatAudio : Node
{
    private const int SoundCount = 7;
    private const float EngineGain = .8f;
    private const float BoostGain = .9f;
    private const float IdleEngineLevel = .38f;
    private const float CruiseEngineLevel = .72f;
    private readonly AudioStreamPlayer[][] _players = new AudioStreamPlayer[SoundCount][];
    private readonly AudioStream[][] _clips = new AudioStream[SoundCount][];
    private readonly ulong[] _nextPlay = new ulong[SoundCount];
    private readonly int[] _plays = new int[SoundCount];
    private readonly int[] _lastVariant = Enumerable.Repeat(-1, SoundCount).ToArray();
    private readonly int[] _nextVoice = new int[SoundCount];
    private uint _variationState = 0x49a370cd; // Presentation-only random stream; never changes simulation rolls.
    private AudioStreamPlayer _engine = null!, _boost = null!;
    private FleetAudio _fleet = null!;
    private float _engineTarget, _boostTarget;
    private HullKind _engineClass;
    private ShipBody? _controlled;
    private int _rails;
    private int _droneKills;
    private ulong _pdShots;
    private uint _amLaunches, _amJettisons;
    private AntimatterMode _amMode;
    private double _worldTime;
    private bool _paused;
    public CombatFeedback Feedback { get; } = new();
    public bool AssetsReady { get; private set; }
    public int PlayCount(CombatSound sound) => _plays[(int)sound];
    internal int LastVariant(CombatSound sound) => _lastVariant[(int)sound];
    internal float EngineLevel { get; private set; }
    internal float BoostLevel { get; private set; }
    internal bool EnginePlaying => _engine.Playing;
    internal bool BoostPlaying => _boost.Playing;

    public override void _Ready()
    {
        SoundSettings.Initialize();
        string[][] files = {
            new[] { "railgun_fire" },
            new[] { "armor_block", "armor_block_v2", "armor_block_v3", "armor_block_v4" },
            new[] { "shield_impact", "shield_impact_v2", "shield_impact_v3", "shield_impact_v4" },
            new[] { "hit_confirm" },
            new[] { "hull_penetration", "hull_penetration_v2", "hull_penetration_v3", "hull_penetration_v4" },
            new[] { "critical_impact", "critical_impact_v2", "critical_impact_v3", "critical_impact_v4" },
            new[] { "pd_fire_01", "pd_fire_02", "pd_fire_03" },
        };
        AssetsReady = true;
        for (int i = 0; i < files.Length; i++)
        {
            _clips[i] = files[i].Select(LoadClip).Where(s => s is not null).Cast<AudioStream>().ToArray();
            AssetsReady &= _clips[i].Length == files[i].Length;
            _players[i] = new AudioStreamPlayer[i == (int)CombatSound.HitConfirm ? 1 : i == (int)CombatSound.PointDefense ? 3 : 2];
            for (int voice = 0; voice < _players[i].Length; voice++)
            {
                // Separate voices preserve the previous variant's tail and pitch.
                var player = new AudioStreamPlayer { Name = $"{files[i][0]}_{voice}",
                    Stream = _clips[i].FirstOrDefault(), Bus = SoundSettings.Bus, MaxPolyphony = 1 };
                _players[i][voice] = player; AddChild(player);
            }
        }
        _engine = MakeLoop("engine_drive_loop"); _boost = MakeLoop("boost_drive_loop");
        _fleet = new FleetAudio { Name = "FleetAudio" }; AddChild(_fleet);
        AssetsReady &= _fleet.AssetsReady;
        if (!AssetsReady) GD.PushWarning("Combat audio assets are missing; see assets/audio.");
        Feedback.Received += PlayIncoming;
        Feedback.Confirmed += hit => Play(CombatSound.HitConfirm,
            hit.Kind == HitKind.Critical ? -10 : -15, hit.Kind switch
            { HitKind.Shield => 1.35f, HitKind.Armor => 1.15f, HitKind.Penetration => .95f, _ => .72f });
    }

    private static AudioStream? LoadClip(string name)
    {
        string path = $"res://assets/audio/{name}.wav";
        return ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;
    }

    private AudioStreamPlayer MakeLoop(string name)
    {
        var stream = LoadClip(name) is AudioStreamWav source ? (AudioStreamWav)source.Duplicate() : null;
        AssetsReady &= stream is not null;
        if (stream is not null)
        {
            stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            stream.LoopBegin = 0; stream.LoopEnd = Mathf.RoundToInt((float)stream.GetLength() * stream.MixRate);
        }
        var player = new AudioStreamPlayer { Name = name, Stream = stream, Bus = SoundSettings.Bus, VolumeDb = -80 };
        AddChild(player); return player;
    }

    public void Prime(SimWorld world, ShipBody? ship)
    {
        StopAll();
        _controlled = ship;
        _worldTime = world.Time;
        Feedback.Prime(world, ship);
        _fleet.Prime(world, ship);
        if (ship is null) return;
        var stats = world.Log?.Ship(ship);
        _rails = stats?.Rails ?? 0;
        _droneKills=stats?.DronesDestroyed??0;
        _pdShots = PointDefenseShots(ship);
        _amLaunches=ship.Ordnance.Antimatter.Launches; _amJettisons=ship.Ordnance.Antimatter.Jettisons;
        _amMode=ship.Ordnance.Antimatter.Mode;
    }

    public void Observe(SimWorld world, ShipBody? ship)
    {
        if (ship != _controlled || world.Time < _worldTime) { Prime(world, ship); return; }
        _fleet.Observe(world, ship);
        if (ship is null) return;
        var stats = world.Log?.Ship(ship);
        int rails = stats?.Rails ?? 0;
        int droneKills=stats?.DronesDestroyed??0;
        var am=ship.Ordnance.Antimatter;
        ulong pdShots = PointDefenseShots(ship);
        SetPropulsion(ship);
        if (!_paused)
        {
            if(droneKills>_droneKills) Play(CombatSound.HitConfirm,-13,1.2f);
            if(am.Launches>_amLaunches) Play(CombatSound.RailFire,-6,.68f);
            if(am.Mode==AntimatterMode.Armed && _amMode!=am.Mode) Play(CombatSound.HitConfirm,-19,.72f);
            if(am.Jettisons>_amJettisons) Play(CombatSound.ArmorImpact,-18,1.5f);
            if (rails > _rails) Play(CombatSound.RailFire, -8 + Mathf.Min(3, (rails - _rails - 1) * 1.2f), ship.Class.Kind switch
                { HullKind.Battleship => .85f, HullKind.Interceptor => 1.15f, _ => 1f });
            if (pdShots > _pdShots) Play(CombatSound.PointDefense,
                -19 + Mathf.Min(3, (float)(pdShots - _pdShots - 1) * .6f),
                ship.Class.Kind switch { HullKind.Battleship => .87f, HullKind.Interceptor => 1.16f, _ => 1f });
        }
        Feedback.Observe(world, ship, _paused);
        _rails = rails; _worldTime = world.Time;
        _droneKills=droneKills;
        _pdShots = pdShots;
        _amLaunches=am.Launches; _amJettisons=am.Jettisons; _amMode=am.Mode;
    }

    private static ulong PointDefenseShots(ShipBody ship)
    {
        ulong shots = 0;
        foreach (var mount in ship.Ordnance.PointDefense) shots += mount.ShotCount;
        return shots;
    }

    private void SetPropulsion(ShipBody ship)
    {
        _engineClass = ship.Class.Kind;
        if (ship.Damage.Destroyed || ship.Damage.GenerationFraction <= .001f || ship.Damage.PropulsionFraction <= .001f)
        { _engineTarget = _boostTarget = 0; return; }
        float health = Mathf.Min(ship.Damage.PropulsionFraction, Mathf.Clamp(ship.Damage.GenerationFraction, 0, 1));
        float throttle = Mathf.Clamp(ship.Control.Thrust.Z, 0, 1);
        float thrust = Mathf.Clamp(ship.EngineOutput * ship.Damage.PropulsionFraction, 0, 2);
        // Powered machinery/coolant continues to vibrate at idle. A held throttle
        // keeps the cruise bed audible after flight assist reaches its target speed.
        float machinery = Mathf.Lerp(IdleEngineLevel, CruiseEngineLevel, throttle) * health;
        _engineTarget = Mathf.Clamp(Mathf.Max(thrust, machinery), 0, 1);
        _boostTarget = ship.Control.Boost ? Mathf.Clamp(Mathf.Max(thrust / 1.2f, throttle * .85f * health), 0, 1) : 0;
    }

    public override void _Process(double delta) => AdvanceLoops((float)Math.Min(delta, .1));

    public void SyncListener(Vec3d position, Basis cameraBasis) => _fleet.SyncListener(position, cameraBasis);

    internal void AdvanceLoops(float dt)
    {
        if (_paused || !AssetsReady || dt <= 0) return;
        _fleet.Advance(dt);
        // Refresh damage even if physics has stopped immediately after a kill.
        if (_controlled is { } ship && (ship.Damage.Destroyed || ship.Damage.PropulsionFraction <= .001f
            || ship.Damage.GenerationFraction <= .001f)) _engineTarget = _boostTarget = 0;
        EngineLevel = SmoothLevel(EngineLevel, _engineTarget, dt, 7, 4);
        BoostLevel = SmoothLevel(BoostLevel, _boostTarget, dt, 12, 6);
        float bodyPitch = _engineClass switch { HullKind.Battleship => .7f, HullKind.Escort => .86f, _ => 1.1f };
        DriveLoop(_engine, EngineLevel, EngineGain, bodyPitch * (.88f + EngineLevel * .18f + BoostLevel * .04f));
        DriveLoop(_boost, BoostLevel, BoostGain, bodyPitch * (1f + BoostLevel * .16f));
    }

    private static float SmoothLevel(float level, float target, float dt, float attack, float release)
    {
        float next = Mathf.Lerp(level, target, 1 - Mathf.Exp(-dt * (target > level ? attack : release)));
        return target == 0 && next < .002f ? 0 : next;
    }

    private static void DriveLoop(AudioStreamPlayer player, float level, float gain, float pitch)
    {
        if (level <= 0) { player.Stop(); return; }
        player.VolumeDb = Mathf.LinearToDb(Mathf.Max(.0001f, level * gain));
        player.PitchScale = pitch;
        if (!player.Playing) player.Play();
    }

    private void PlayIncoming(HitFeedback hit)
    {
        _fleet.Duck(hit.Kind is HitKind.Critical or HitKind.Penetration ? 1.1f : .45f);
        CombatSound sound = hit.Kind switch { HitKind.Shield => CombatSound.ShieldImpact, HitKind.Armor => CombatSound.ArmorImpact,
            HitKind.Penetration => CombatSound.Penetration, _ => CombatSound.Critical };
        float bodyPitch = hit.Target.Class.Kind switch { HullKind.Battleship => .82f, HullKind.Escort => .95f, _ => 1.08f };
        float db = hit.Kind switch { HitKind.Shield => -11, HitKind.Armor => -8, HitKind.Penetration => -5, _ => -3 };
        Play(sound, db + Mathf.LinearToDb(Mathf.Lerp(.65f, 1f, hit.Strength)), bodyPitch);
        if (hit.ShieldBroken && hit.Kind != HitKind.Shield) Play(CombatSound.ShieldImpact, -15, .7f);
    }

    private void Play(CombatSound sound, float db, float pitch)
    {
        int index = (int)sound;
        ulong now = Time.GetTicksMsec();
        if (_paused || !AssetsReady || now < _nextPlay[index]) return;
        _nextPlay[index] = now + (sound is CombatSound.RailFire or CombatSound.PointDefense ? 90UL : 120UL);
        int count = _clips[index].Length;
        int variant = count <= 1 ? 0 : (int)(Variation() % (uint)(count - (_lastVariant[index] >= 0 ? 1 : 0)));
        if (count > 1 && _lastVariant[index] >= 0 && variant >= _lastVariant[index]) variant++;
        _lastVariant[index] = variant;
        var player = _players[index][_nextVoice[index]++ % _players[index].Length];
        player.Stop(); player.Stream = _clips[index][variant];
        float pitchRange = sound == CombatSound.HitConfirm ? .008f : sound is CombatSound.RailFire or CombatSound.PointDefense ? .035f : .085f;
        player.VolumeDb = db + (RandomUnit() * 2 - 1) * (sound == CombatSound.HitConfirm ? .3f : 1.2f);
        player.PitchScale = pitch * (1 + (RandomUnit() * 2 - 1) * pitchRange);
        player.Play(); _plays[index]++;
    }

    private uint Variation()
    {
        _variationState ^= _variationState << 13; _variationState ^= _variationState >> 17; _variationState ^= _variationState << 5;
        return _variationState;
    }
    private float RandomUnit() => (Variation() & 0xffffff) / 16777216f;

    public void SetPaused(bool paused)
    {
        if (_paused == paused) return;
        _paused = paused;
        _fleet.SetPaused(paused);
        foreach (var player in _players.SelectMany(p => p)) player.StreamPaused = paused;
        _engine.StreamPaused = _boost.StreamPaused = paused;
    }
    public void StopAll()
    {
        _fleet?.StopAll();
        foreach (var voices in _players) if (voices is not null) foreach (var player in voices) player.Stop();
        _engine?.Stop(); _boost?.Stop();
        EngineLevel = BoostLevel = _engineTarget = _boostTarget = 0;
        Array.Clear(_nextPlay);
    }
}

public static class SoundSettings
{
    public const string Bus = "CombatSfx";
    private const string ConfigPath = "user://audio.cfg";
    private static bool _initialized;
    public static float Volume { get; private set; } = 75;
    public static bool Muted { get; private set; }

    public static void Initialize()
    {
        if (!_initialized)
        {
            var config = new ConfigFile();
            if (config.Load(ConfigPath) == Error.Ok)
            {
                float saved = (float)config.GetValue("sound", "volume", 75f).AsDouble();
                if (float.IsFinite(saved)) Volume = Mathf.Clamp(saved, 0, 100);
                Muted = config.GetValue("sound", "muted", false).AsBool();
            }
            _initialized = true;
        }
        int bus = AudioServer.GetBusIndex(Bus);
        if (bus < 0)
        {
            bus = AudioServer.BusCount; AudioServer.AddBus(); AudioServer.SetBusName(bus, Bus);
            using var limiter = new AudioEffectHardLimiter { CeilingDb = -1 };
            AudioServer.AddBusEffect(bus, limiter);
        }
        Apply();
    }
    public static void SetVolume(float volume, bool persist = true)
    {
        Volume = Mathf.Clamp(volume, 0, 100); Apply(); if (persist) Save();
    }
    public static void SetMuted(bool muted, bool persist = true)
    { Muted = muted; Apply(); if (persist) Save(); }
    public static void ToggleMute() => SetMuted(!Muted);
    private static void Apply()
    {
        int bus = AudioServer.GetBusIndex(Bus);
        if (bus < 0) return;
        AudioServer.SetBusMute(bus, Muted || Volume <= 0);
        AudioServer.SetBusVolumeDb(bus, Volume > 0 ? Mathf.LinearToDb(Volume / 100) : -80);
    }
    private static void Save()
    {
        var config = new ConfigFile(); config.SetValue("sound", "volume", Volume); config.SetValue("sound", "muted", Muted);
        Error error = config.Save(ConfigPath); if (error != Error.Ok) GD.PushWarning($"Audio settings save failed: {error}");
    }
}
