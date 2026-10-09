using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

/// <summary>Nearby fleet sounds. Camera-relative spatial audio, independent of simulation RNG and floating origin.</summary>
public partial class FleetAudio : Node3D
{
    internal const int EngineSlots = 4, BurstSlots = 12;
    private sealed class EngineVoice
    {
        public required AudioStreamPlayer3D Engine, Boost;
        public ShipBody? Ship;
        public float Level, BoostLevel;
    }
    private sealed class BurstVoice
    {
        public required AudioStreamPlayer3D Player;
        public Cue Cue;
        public float EndsAt;
    }
    private readonly record struct Snapshot(ulong Rails, ulong Pd, bool Destroyed);
    private readonly record struct Cue(ShipBody? Source, Vec3d Position, CombatSound Sound,
        float Gain, float Range, float Pitch, float Importance = 1);
    private readonly Dictionary<ShipBody, Snapshot> _ships = new();
    private readonly HashSet<(uint, double)> _impacts = new();
    private readonly HashSet<OrdnanceEvent> _ordnance = new();
    private readonly Dictionary<(ShipBody, CombatSound), float> _cooldowns = new();
    private readonly List<Cue> _pending = new();
    private readonly List<EngineVoice> _engines = new();
    private readonly List<BurstVoice> _bursts = new();
    private readonly Dictionary<AudioStreamPlayer3D, float> _resume = new();
    private readonly Dictionary<CombatSound, AudioStream[]> _clips = new();
    private readonly Dictionary<CombatSound, int> _variants = new();
    private readonly int[] _plays = new int[7];
    private AudioListener3D _listener = null!;
    private SimWorld? _world;
    private ShipBody? _controlled;
    private Vec3d _ear;
    private double _worldTime;
    private float _clock, _duckUntil;
    private bool _paused, _enabled, _listenerReady;
    private uint _random = 0x7041b6a3;
    public bool AssetsReady { get; private set; } = true;
    internal int PlayCount(CombatSound sound) => _plays[(int)sound];
    internal int ActiveEngines => _engines.Count(v => v.Engine.Playing);
    internal int ActiveBursts => _bursts.Count(v => v.Player.Playing);
    internal bool HasEngine(ShipBody ship) => _engines.Any(v => v.Ship == ship && v.Engine.Playing);

    public override void _Ready()
    {
        SoundSettings.Initialize();
        // A compact audio-only coordinate space avoids precision loss and false
        // Doppler spikes when the render origin moves by millions of metres.
        TopLevel = true; Transform = Transform3D.Identity;
        _listener = new AudioListener3D { Name = "FleetListener" }; AddChild(_listener);
        Bank(CombatSound.RailFire, "railgun_fire");
        Bank(CombatSound.PointDefense, "pd_fire_01", "pd_fire_02", "pd_fire_03");
        foreach (var (sound, file) in new[] { (CombatSound.ShieldImpact, "shield_impact"),
            (CombatSound.ArmorImpact, "armor_block"), (CombatSound.Penetration, "hull_penetration"),
            (CombatSound.Critical, "critical_impact") })
            Bank(sound, file, file + "_v2", file + "_v3", file + "_v4");
        AudioStream? engine = Loop("engine_drive_loop"), boost = Loop("boost_drive_loop");
        for (int i = 0; i < EngineSlots; i++)
            _engines.Add(new EngineVoice { Engine = Player($"Engine{i}", engine), Boost = Player($"Boost{i}", boost) });
        for (int i = 0; i < BurstSlots; i++) _bursts.Add(new BurstVoice { Player = Player($"Burst{i}") });
    }

    private AudioStream? Load(string name)
    {
        string path = $"res://assets/audio/{name}.wav";
        var clip = ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;
        AssetsReady &= clip is not null; return clip;
    }
    private void Bank(CombatSound sound, params string[] names) =>
        _clips[sound] = names.Select(Load).OfType<AudioStream>().ToArray();
    private AudioStream? Loop(string name)
    {
        if (Load(name) is not AudioStreamWav source) { AssetsReady = false; return null; }
        var clip = (AudioStreamWav)source.Duplicate(); clip.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        clip.LoopBegin = 0; clip.LoopEnd = Mathf.RoundToInt((float)clip.GetLength() * clip.MixRate); return clip;
    }
    private AudioStreamPlayer3D Player(string name, AudioStream? stream = null)
    {
        var player = new AudioStreamPlayer3D { Name = name, Stream = stream, Bus = SoundSettings.Bus,
            MaxPolyphony = 1, VolumeDb = -80, MaxDb = 0,
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled,
            AttenuationFilterDb = 0, DopplerTracking = AudioStreamPlayer3D.DopplerTrackingEnum.Disabled };
        AddChild(player); return player;
    }

    public void SyncListener(Vec3d worldPosition, Basis cameraBasis)
    {
        _ear = worldPosition; _listener.Basis = cameraBasis.Orthonormalized();
        if (!_listenerReady) { _listener.MakeCurrent(); _listenerReady = true; }
    }

    private static Snapshot Read(ShipBody ship) => new(
        ship.Railguns.Aggregate(0UL, (sum, gun) => sum + gun.ShotCount),
        ship.Ordnance.PointDefense.Aggregate(0UL, (sum, gun) => sum + gun.ShotCount), ship.Damage.Destroyed);

    public void Prime(SimWorld world, ShipBody? controlled)
    {
        StopAll(); _world = world; _controlled = controlled; _worldTime = world.Time;
        _ships.Clear(); foreach (var ship in world.Ships) _ships[ship] = Read(ship);
        _impacts.Clear(); _impacts.UnionWith(world.Impacts.Select(i => (i.Id, i.Time)));
        _ordnance.Clear(); _ordnance.UnionWith(world.OrdnanceEvents);
        _enabled = controlled is not null;
    }

    public void Observe(SimWorld world, ShipBody? controlled)
    {
        if (_world != world || _controlled != controlled || world.Time < _worldTime)
        { Prime(world, controlled); return; }
        _enabled = controlled is not null; _worldTime = world.Time;
        foreach (var ship in world.Ships)
        {
            Snapshot now = Read(ship);
            if (_ships.TryGetValue(ship, out var before) && ship != controlled)
            {
                float pitch = BodyPitch(ship);
                if (now.Rails > before.Rails)
                    Queue(new(ship, ship.Railguns.OrderByDescending(g => g.LastFiredAt).First().MuzzlePosition,
                        CombatSound.RailFire, .36f, ship.Class.Kind == HullKind.Battleship ? 48000 : 30000, pitch));
                if (now.Pd > before.Pd)
                    Queue(new(ship, ship.Position, CombatSound.PointDefense, .16f, 10000, pitch * 1.1f, .6f));
                if (now.Destroyed && !before.Destroyed)
                    Queue(new(ship, ship.Position, CombatSound.Critical, .65f, 60000, pitch * .8f, 2));
            }
            _ships[ship] = now;
        }
        foreach (var removed in _ships.Keys.Where(s => !world.Ships.Contains(s)).ToArray())
        {
            _ships.Remove(removed);
            foreach (var key in _cooldowns.Keys.Where(k => k.Item1 == removed).ToArray()) _cooldowns.Remove(key);
        }
        _impacts.IntersectWith(world.Impacts.Select(i => (i.Id, i.Time)));
        foreach (var impact in world.Impacts)
        {
            if (!_impacts.Add((impact.Id, impact.Time)) || impact.Hit.Target is not { } target || target == controlled) continue;
            // A destruction cue already carries this impact; don't double the explosion.
            if (impact.TargetDestroyed) continue;
            var kind = CombatFeedback.Describe(impact).Kind;
            CombatSound sound = kind switch { HitKind.Shield => CombatSound.ShieldImpact,
                HitKind.Armor => CombatSound.ArmorImpact, HitKind.Penetration => CombatSound.Penetration, _ => CombatSound.Critical };
            Queue(new(target, impact.Hit.ShieldPoint ?? impact.Hit.Point, sound,
                kind == HitKind.Critical ? .46f : .3f, kind == HitKind.Critical ? 45000 : 24000, BodyPitch(target)));
        }
        _ordnance.IntersectWith(world.OrdnanceEvents);
        foreach (var effect in world.OrdnanceEvents)
        {
            if (!_ordnance.Add(effect)) continue;
            // Hits already sound through ProjectileImpact; expired/jettisoned AM stays quiet.
            if (effect.Kind is OrdnanceEventKind.Intercepted or OrdnanceEventKind.DroneDestroyed)
                Queue(new(null, effect.Position, CombatSound.ArmorImpact, .18f, 12000, 1.3f, .65f));
            else if (effect.Kind == OrdnanceEventKind.Detonation && !world.Impacts.Any(i =>
                i.Weapon == effect.Weapon && Math.Abs(i.Time - effect.Time) < .001 &&
                ((i.Hit.ShieldPoint ?? i.Hit.Point) - effect.Position).LengthSquared() < 1))
                Queue(new(null, effect.Position, CombatSound.Penetration, .3f, 26000, .9f));
        }
    }

    private void Queue(Cue cue)
    {
        if (_paused || !_enabled || !_listenerReady || !AssetsReady || Gain(cue) < .002f) return;
        if (cue.Source is { } source)
        {
            var key = (source, cue.Sound);
            if (_cooldowns.TryGetValue(key, out float until) && until > _clock) return;
            _cooldowns[key] = _clock + (cue.Sound == CombatSound.PointDefense ? .16f : .10f);
        }
        _pending.Add(cue);
        // Fast-forward and very large battles cannot build up a delayed sound queue.
        if (_pending.Count > 64) _pending.Remove(_pending.MinBy(Score));
    }
    private float Gain(Cue cue) => cue.Gain * DistanceGain((cue.Position - _ear).Length(), 1800, cue.Range);
    private float Score(Cue cue) => Gain(cue) * cue.Importance;

    internal static float DistanceGain(double distance, float near, float range)
    {
        if (distance >= range) return 0;
        float edge = 1 - Mathf.SmoothStep(range * .7f, range, (float)distance);
        return edge / (1 + MathF.Pow((float)distance / near, 1.25f));
    }
    internal static Vector3 RelativePosition(Vec3d source, Vec3d listener)
    {
        Vec3d offset = source - listener;
        return offset.LengthSquared() < .000001 ? Vector3.Forward * 10 : (offset * (10 / offset.Length())).ToVector3();
    }
    private static float BodyPitch(ShipBody ship) => ship.Class.Kind switch
    { HullKind.Battleship => .74f, HullKind.Escort => .9f, _ => 1.12f };
    private static float EngineRange(ShipBody ship) => ship.Class.Kind switch
    { HullKind.Battleship => 22000, HullKind.Escort => 15000, _ => 7000 };
    private static float Demand(ShipBody ship)
    {
        if (ship.Damage.Destroyed) return 0;
        float health = Mathf.Clamp(Mathf.Min(ship.Damage.GenerationFraction, ship.Damage.PropulsionFraction), 0, 1);
        return Mathf.Max(.38f + .62f * Mathf.Clamp(ship.Control.Thrust.Z, 0, 1), Mathf.Clamp(ship.EngineOutput, 0, 1)) * health;
    }
    private float EngineGain(ShipBody ship) => .42f * Demand(ship) *
        DistanceGain((ship.Position - _ear).Length(), 1800, EngineRange(ship));

    internal void Advance(float dt)
    {
        if (_paused || !_enabled || !_listenerReady || !AssetsReady || _world is null || dt <= 0) return;
        _clock += dt;
        // Carbon Audio's relevance/culling approach: bounded voices, range and
        // event importance, plus a small preference for already active emitters.
        var audible = _world.Ships.Where(s => s != _controlled && EngineGain(s) > .002f)
            .OrderByDescending(s => EngineGain(s) * (_engines.Any(v => v.Ship == s) ? 1.2f : 1))
            .Take(EngineSlots).ToArray();
        foreach (var voice in _engines)
        {
            bool keep = voice.Ship is { } ship && audible.Contains(ship);
            if (!keep)
            {
                voice.Level = Smooth(voice.Level, 0, dt); voice.BoostLevel = Smooth(voice.BoostLevel, 0, dt);
                if (voice.Level == 0 && voice.BoostLevel == 0)
                {
                    voice.Engine.Stop(); voice.Boost.Stop();
                    voice.Ship = audible.FirstOrDefault(s => !_engines.Any(v => v.Ship == s));
                }
            }
            if (voice.Ship is not { } source) continue;
            if (audible.Contains(source))
            {
                float gain = EngineGain(source);
                voice.Level = Smooth(voice.Level, gain, dt);
                bool boosting = source.Control.Boost && (source.Control.Thrust.Z > .01f || source.EngineOutput > .01f);
                voice.BoostLevel = Smooth(voice.BoostLevel, boosting ? gain * 1.25f : 0, dt);
            }
            Drive(voice.Engine, source.Position, voice.Level, BodyPitch(source));
            Drive(voice.Boost, source.Position, voice.BoostLevel, BodyPitch(source) * 1.08f);
        }
        foreach (var cue in _pending.OrderByDescending(Score).Take(4)) Emit(cue);
        _pending.Clear();
        foreach (var voice in _bursts)
        {
            if (_clock >= voice.EndsAt) { voice.Player.Stop(); continue; }
            if (voice.Player.Playing) Spatial(voice.Player, voice.Cue.Position, Gain(voice.Cue));
        }
    }

    private void Emit(Cue cue)
    {
        if (Gain(cue) < .002f) return;
        var voice = _bursts.FirstOrDefault(v => !v.Player.Playing || _clock >= v.EndsAt);
        if (voice is null)
        {
            voice = _bursts.MinBy(v => Score(v.Cue))!;
            if (Score(cue) <= Score(voice.Cue) * 1.2f) return;
        }
        var bank = _clips[cue.Sound]; int last = _variants.GetValueOrDefault(cue.Sound, -1);
        _random ^= _random << 13; _random ^= _random >> 17; _random ^= _random << 5;
        int variant = bank.Length == 1 ? 0 : (int)(_random % (uint)(bank.Length - (last >= 0 ? 1 : 0)));
        if (bank.Length > 1 && last >= 0 && variant >= last) variant++;
        _variants[cue.Sound] = variant;
        voice.Player.Stop(); voice.Player.Stream = bank[variant]; voice.Cue = cue;
        voice.Player.PitchScale = cue.Pitch * (.97f + (_random & 255) / 255f * .06f);
        voice.EndsAt = _clock + (float)(bank[variant].GetLength() / voice.Player.PitchScale);
        Spatial(voice.Player, cue.Position, Gain(cue)); voice.Player.Play(); _plays[(int)cue.Sound]++;
    }
    private static float Smooth(float value, float target, float dt)
    {
        float next = Mathf.Lerp(value, target, 1 - Mathf.Exp(-dt * (target > value ? 5 : 7)));
        return target == 0 && next < .001f ? 0 : next;
    }
    private void Drive(AudioStreamPlayer3D player, Vec3d position, float gain, float pitch)
    {
        if (gain <= 0) { player.Stop(); return; }
        Spatial(player, position, gain); player.PitchScale = pitch;
        if (!player.Playing) player.Play();
    }
    private void Spatial(AudioStreamPlayer3D player, Vec3d position, float gain)
    {
        player.Position = RelativePosition(position, _ear);
        player.VolumeDb = Mathf.LinearToDb(Mathf.Max(.0001f, gain * (_clock < _duckUntil ? .38f : 1)));
    }
    public void Duck(float seconds) => _duckUntil = MathF.Max(_duckUntil, _clock + seconds);
    public void SetPaused(bool paused)
    {
        if (_paused == paused) return;
        _paused = paused;
        if (paused)
        {
            _pending.Clear(); _resume.Clear();
            // 3D Play is deferred until the next physics frame. StreamPaused
            // cannot pause that pending start; explicitly suspend at its offset.
            foreach (var player in _engines.SelectMany(v => new[] { v.Engine, v.Boost }).Concat(_bursts.Select(v => v.Player)))
            {
                if (player.Playing) _resume[player] = player.GetPlaybackPosition();
                player.Stop();
            }
        }
        else
        {
            foreach (var (player, offset) in _resume) player.Play(offset);
            _resume.Clear();
        }
    }
    public void StopAll()
    {
        _enabled = false; _pending.Clear(); _cooldowns.Clear(); _resume.Clear(); _clock = _duckUntil = 0;
        foreach (var voice in _engines)
        { voice.Engine.Stop(); voice.Boost.Stop(); voice.Ship = null; voice.Level = voice.BoostLevel = 0; }
        foreach (var voice in _bursts) { voice.Player.Stop(); voice.EndsAt = 0; }
    }
}
