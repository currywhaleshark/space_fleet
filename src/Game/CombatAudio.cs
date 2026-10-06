using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public enum CombatSound { RailFire, ArmorImpact, ShieldImpact, HitConfirm }

/// <summary>조종함의 구조 전달음과 명중 피드백. 시뮬레이션 판정에는 관여하지 않는다.</summary>
public partial class CombatAudio : Node
{
    private readonly AudioStreamPlayer[] _players = new AudioStreamPlayer[4];
    private readonly ulong[] _nextPlay = new ulong[4];
    private readonly int[] _plays = new int[4];
    private ShipBody? _controlled;
    private int _rails, _missileHits;
    private uint _enemyImpact;
    private double _shieldTime, _moduleTime, _armorTime, _collisionTime, _worldTime;
    private bool _paused;
    public bool AssetsReady { get; private set; }
    public int PlayCount(CombatSound sound) => _plays[(int)sound];

    public override void _Ready()
    {
        SoundSettings.Initialize();
        string[] files = { "railgun_fire", "armor_impact", "shield_impact", "hit_confirm" };
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
    }

    public void Prime(SimWorld world, ShipBody? ship)
    {
        StopAll();
        _controlled = ship;
        _worldTime = world.Time;
        if (ship is null) return;
        var stats = world.Log?.Ship(ship);
        _rails = stats?.Rails ?? 0;
        _missileHits = stats?.MissileHits ?? 0;
        _enemyImpact = EnemyImpact(world, ship);
        _shieldTime = ship.Damage.LastShieldHitTime;
        _moduleTime = ModuleTime(ship);
        _armorTime = ArmorTime(world, ship);
        _collisionTime = ship.LastCollision?.Time ?? double.NegativeInfinity;
    }

    public void Observe(SimWorld world, ShipBody? ship)
    {
        if (ship != _controlled || world.Time < _worldTime) { Prime(world, ship); return; }
        if (ship is null) return;
        var stats = world.Log?.Ship(ship);
        int rails = stats?.Rails ?? 0;
        int missileHits = stats?.MissileHits ?? 0;
        uint enemyImpact = EnemyImpact(world, ship);
        double shield = ship.Damage.LastShieldHitTime, module = ModuleTime(ship);
        double armor = ArmorTime(world, ship), collision = ship.LastCollision?.Time ?? double.NegativeInfinity;
        if (!_paused)
        {
            if (rails > _rails) Play(CombatSound.RailFire, -8, ship.Class.Kind switch
                { HullKind.Battleship => .85f, HullKind.Interceptor => 1.15f, _ => 1f });
            // 같은 충격이 실드와 선체를 관통하면 더 무거운 선체음을 우선한다.
            if (module > _moduleTime || armor > _armorTime || (collision > _collisionTime && ship.LastCollision!.Value.ClosingSpeed > 2))
                Play(CombatSound.ArmorImpact, -5, .98f);
            else if (shield > _shieldTime) Play(CombatSound.ShieldImpact, -7, 1f);
            if (missileHits > _missileHits || enemyImpact > _enemyImpact) Play(CombatSound.HitConfirm, -14, 1.12f);
        }
        _rails = rails; _missileHits = missileHits; _enemyImpact = enemyImpact; _shieldTime = shield; _moduleTime = module;
        _armorTime = armor; _collisionTime = collision; _worldTime = world.Time;
    }

    private static double ModuleTime(ShipBody ship) => ship.Damage.Modules.Max(m => m.LastHitTime);
    private static double ArmorTime(SimWorld world, ShipBody ship) => world.Impacts
        .Where(i => i.Hit.Target == ship && !i.Hit.ShieldStopped).Select(i => i.Time).DefaultIfEmpty(double.NegativeInfinity).Max();
    private static uint EnemyImpact(SimWorld world, ShipBody ship) => world.Impacts
        .Where(i => i.Shooter == ship && i.Hit.Target is { } target && target.Faction != ship.Faction)
        .Select(i => i.Id).DefaultIfEmpty(0U).Max();

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
