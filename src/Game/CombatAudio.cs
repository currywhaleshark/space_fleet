using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public enum CombatSound { RailFire, ArmorImpact, ShieldImpact, HitConfirm, Penetration, Critical }

/// <summary>조종함의 구조 전달음과 명중 피드백. 시뮬레이션 판정에는 관여하지 않는다.</summary>
public partial class CombatAudio : Node
{
    private readonly AudioStreamPlayer[] _players = new AudioStreamPlayer[6];
    private readonly ulong[] _nextPlay = new ulong[6];
    private readonly int[] _plays = new int[6];
    private ShipBody? _controlled;
    private int _rails;
    private uint _amLaunches, _amJettisons;
    private AntimatterMode _amMode;
    private double _worldTime;
    private bool _paused;
    public CombatFeedback Feedback { get; } = new();
    public bool AssetsReady { get; private set; }
    public int PlayCount(CombatSound sound) => _plays[(int)sound];

    public override void _Ready()
    {
        SoundSettings.Initialize();
        string[] files = { "railgun_fire", "armor_block", "shield_impact", "hit_confirm", "hull_penetration", "critical_impact" };
        AssetsReady = true;
        for (int i = 0; i < files.Length; i++)
        {
            string path = $"res://assets/audio/{files[i]}.wav";
            AudioStream? stream = ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;
            AssetsReady &= stream is not null;
            _players[i] = new AudioStreamPlayer { Name = files[i], Stream = stream, Bus = SoundSettings.Bus,
                MaxPolyphony = i == (int)CombatSound.HitConfirm ? 1 : 2 };
            AddChild(_players[i]);
        }
        if (!AssetsReady) GD.PushWarning("Combat audio assets are missing; see assets/audio.");
        Feedback.Received += PlayIncoming;
        Feedback.Confirmed += hit => Play(CombatSound.HitConfirm,
            hit.Kind == HitKind.Critical ? -10 : -15, hit.Kind switch
            { HitKind.Shield => 1.35f, HitKind.Armor => 1.15f, HitKind.Penetration => .95f, _ => .72f });
    }

    public void Prime(SimWorld world, ShipBody? ship)
    {
        StopAll();
        _controlled = ship;
        _worldTime = world.Time;
        Feedback.Prime(world, ship);
        if (ship is null) return;
        var stats = world.Log?.Ship(ship);
        _rails = stats?.Rails ?? 0;
        _amLaunches=ship.Ordnance.Antimatter.Launches; _amJettisons=ship.Ordnance.Antimatter.Jettisons;
        _amMode=ship.Ordnance.Antimatter.Mode;
    }

    public void Observe(SimWorld world, ShipBody? ship)
    {
        if (ship != _controlled || world.Time < _worldTime) { Prime(world, ship); return; }
        if (ship is null) return;
        var stats = world.Log?.Ship(ship);
        int rails = stats?.Rails ?? 0;
        var am=ship.Ordnance.Antimatter;
        if (!_paused)
        {
            if(am.Launches>_amLaunches) Play(CombatSound.RailFire,-6,.68f);
            if(am.Mode==AntimatterMode.Armed && _amMode!=am.Mode) Play(CombatSound.HitConfirm,-19,.72f);
            if(am.Jettisons>_amJettisons) Play(CombatSound.ArmorImpact,-18,1.5f);
            if (rails > _rails) Play(CombatSound.RailFire, -8 + Mathf.Min(3, (rails - _rails - 1) * 1.2f), ship.Class.Kind switch
                { HullKind.Battleship => .85f, HullKind.Interceptor => 1.15f, _ => 1f });
        }
        Feedback.Observe(world, ship, _paused);
        _rails = rails; _worldTime = world.Time;
        _amLaunches=am.Launches; _amJettisons=am.Jettisons; _amMode=am.Mode;
    }

    private void PlayIncoming(HitFeedback hit)
    {
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
        if (!AssetsReady || now < _nextPlay[index]) return;
        _nextPlay[index] = now + (sound == CombatSound.RailFire ? 90UL : 120UL);
        var player = _players[index];
        player.VolumeDb = db;
        player.PitchScale = pitch * (1f + ((_plays[index] % 3) - 1) * .025f);
        player.Play(); _plays[index]++;
    }

    public void SetPaused(bool paused)
    {
        if (_paused == paused) return;
        _paused = paused;
        foreach (var player in _players) player.StreamPaused = paused;
    }
    public void StopAll()
    {
        foreach (var player in _players) player?.Stop();
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
