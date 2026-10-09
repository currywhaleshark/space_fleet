using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

/// <summary>--fleet-audio-test: live event/lifecycle tests and recorded camera-relative stereo.</summary>
public partial class FleetAudioChecks : Node3D
{
    private FleetAudio _audio = null!;
    private SimWorld _world = null!;
    private ShipBody _me = null!, _near = null!;
    private AudioEffectRecord _record = null!;
    private int _recordIndex = -1, _checks, _phase;
    private float _volume;
    private bool _muted, _recording;
    private ulong _phaseStart;
    private readonly (double Left, double Right)[] _levels = new (double, double)[7];
    private void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); _checks++; }

    public override void _Ready()
    {
        try
        {
            SoundSettings.Initialize(); _volume = SoundSettings.Volume; _muted = SoundSettings.Muted;
            SoundSettings.SetVolume(75, false); SoundSettings.SetMuted(false, false);
            // Godot resolves 3D listeners through a viewport containing a camera.
            var camera = new Camera3D(); AddChild(camera); camera.MakeCurrent();
            _audio = new FleetAudio(); AddChild(_audio);
            _audio.SyncListener(Vec3d.Zero, Basis.Identity);
            Check(_audio.AssetsReady, "Fleet banks and loops load");
            Check(_audio.GetChildren().OfType<AudioStreamPlayer3D>().Count() == FleetAudio.EngineSlots * 2 + FleetAudio.BurstSlots,
                "Fleet uses a bounded 20-voice pool");
            Check(_audio.GetChildren().OfType<AudioStreamPlayer3D>().All(p => p.Bus == SoundSettings.Bus && p.MaxPolyphony == 1),
                "All spatial voices share the existing mute, volume and limiter bus");
            Functional();
            _world = new SimWorld();
            _me = _world.Add(new ShipBody("LISTENER", ShipClass.Interceptor, Faction.Blue));
            _near = _world.Add(new ShipBody("NEAR", ShipClass.Battleship, Faction.Blue));
            _near.Control = new ShipControl { FlightAssist = false, Thrust = Vector3.Back };
            _record = new AudioEffectRecord { Format = AudioStreamWav.FormatEnum.Format16Bits };
            _recordIndex = AudioServer.GetBusEffectCount(0); AudioServer.AddBusEffect(0, _record);
            Phase(0);
        }
        catch (Exception ex) { Fail(ex); }
    }

    private void Functional()
    {
        var world = new SimWorld();
        var me = world.Add(new ShipBody("ME", ShipClass.Interceptor, Faction.Blue));
        var gun = world.Add(new ShipBody("GUN", ShipClass.Interceptor, Faction.Blue));
        var target = world.Add(new ShipBody("TARGET", ShipClass.Battleship, Faction.Red));
        gun.Place(new Vec3d(1000, 0, 0), Quaternion.Identity);
        target.Place(new Vec3d(1000, 0, -4000), Quaternion.Identity);
        _audio.Prime(world, me); _audio.Observe(world, me); _audio.Advance(.3f);
        Check(_audio.HasEngine(gun) && !_audio.HasEngine(me), "Nearby powered ships sound; own engine is excluded");
        gun.Control = new ShipControl { FlightAssist = false, Thrust = Vector3.Back, Boost = true };
        _audio.Advance(.5f);
        Check(_audio.GetChildren().OfType<AudioStreamPlayer3D>().Any(p => p.Name.ToString().StartsWith("Boost") && p.Playing),
            "Neighbor boost adds a separate propulsion layer");
        gun.Control = new ShipControl { FlightAssist = false }; _audio.Advance(2);
        Check(!_audio.GetChildren().OfType<AudioStreamPlayer3D>().Any(p => p.Name.ToString().StartsWith("Boost") && p.Playing),
            "Releasing neighboring boost fades the extra layer");
        int rails = _audio.PlayCount(CombatSound.RailFire);
        Check(world.FireRailgun(gun, Vector3.Forward).Fired, "Real neighbor railgun fires");
        _audio.Observe(world, me); _audio.Advance(.1f);
        Check(_audio.PlayCount(CombatSound.RailFire) == rails + 1, "Neighbor rail counter produces spatial shot without BattleLog");
        for (int i = 0; i < 10; i++) { _audio.Observe(world, me); _audio.Advance(.01f); }
        Check(_audio.PlayCount(CombatSound.RailFire) == rails + 1, "Shot counters do not repeat retained shots");
        Check(world.FireRailgun(me, Vector3.Forward).Fired, "Own rail fires for exclusion fixture");
        _audio.Observe(world, me); _audio.Advance(.1f);
        Check(_audio.PlayCount(CombatSound.RailFire) == rails + 1, "Own shot remains in the interior audio layer only");
        int shields = _audio.PlayCount(CombatSound.ShieldImpact);
        CombatFeedbackChecks.Strike(world, me, target, HitKind.Shield, 80001);
        _audio.Observe(world, me); _audio.Advance(.1f);
        Check(_audio.PlayCount(CombatSound.ShieldImpact) == shields + 1, "Real damage ray triggers spatial shield impact");
        _audio.Observe(world, me); _audio.Advance(.2f);
        Check(_audio.PlayCount(CombatSound.ShieldImpact) == shields + 1, "Impact snapshot sounds once");
        CombatFeedbackChecks.Strike(world, target, me, HitKind.Shield, 80002);
        _audio.Observe(world, me); _audio.Advance(.1f);
        Check(_audio.PlayCount(CombatSound.ShieldImpact) == shields + 1, "Own shield impact is not doubled");
        var active = _audio.GetChildren().OfType<AudioStreamPlayer3D>().Where(p => p.Playing).ToArray();
        Check(active.Length > 0, "Pause fixture has active spatial voices");
        _audio.SetPaused(true);
        Check(active.All(p => !p.Playing), "Pause suspends active voices including deferred 3D starts");
        gun.Railgun!.Reset(); world.FireRailgun(gun, Vector3.Forward);
        CombatFeedbackChecks.Strike(world, me, target, HitKind.Shield, 80003);
        _audio.Observe(world, me); _audio.SetPaused(false); _audio.Observe(world, me); _audio.Advance(.2f);
        Check(_audio.PlayCount(CombatSound.ShieldImpact) == shields + 1, "Paused events are consumed, not replayed");
        _audio.Prime(world, target); _audio.Observe(world, target); _audio.Advance(.2f);
        Check(!_audio.HasEngine(target) && _audio.HasEngine(me), "Handoff changes which ship is spatial");
        Check(_audio.PlayCount(CombatSound.ShieldImpact) == shields + 1, "Handoff skips earlier impacts");
        _audio.Prime(world, me);
        gun.Place(new Vec3d(1000000, 0, 0), Quaternion.Identity);
        world.ResetWeapons(); _audio.Observe(world, me);
        world.FireRailgun(gun, Vector3.Forward); _audio.Observe(world, me); _audio.Advance(.3f);
        int quietRails = _audio.PlayCount(CombatSound.RailFire);
        gun.Place(new Vec3d(1000, 0, 0), Quaternion.Identity); _audio.Observe(world, me); _audio.Advance(.2f);
        Check(_audio.PlayCount(CombatSound.RailFire) == quietRails, "Entering range never replays an inaudible old shot");
        Check(_audio.HasEngine(gun), "Entering range wakes the current engine loop");
        int critical = _audio.PlayCount(CombatSound.Critical);
        gun.Damage.Catastrophe(world.Time, "fleet audio fixture"); _audio.Observe(world, me); _audio.Advance(.1f);
        Check(_audio.PlayCount(CombatSound.Critical) == critical + 1, "Neighbor destruction plays a single spatial explosion");
        _audio.Observe(world, me); _audio.Advance(2);
        Check(!_audio.HasEngine(gun), "Destroyed neighbor engine fades to silence");
        Check(_audio.PlayCount(CombatSound.Critical) == critical + 1, "Wreck does not repeat explosion");
        gun.Damage.Reset(); _audio.Observe(world, me); _audio.Advance(.3f);
        Check(_audio.HasEngine(gun), "Repair restores neighboring engine");
        _audio.StopAll(); _audio.Advance(2);
        Check(_audio.ActiveEngines == 0 && _audio.ActiveBursts == 0, "StopAll cannot restart stale emitters");
        var restart = new SimWorld(); restart.Add(me); restart.Add(gun);
        _audio.Observe(restart, me);
        Check(_audio.ActiveBursts == 0, "New world identity resets audio even at equal world time");
        _audio.Prime(restart, null); _audio.Advance(.5f);
        Check(_audio.ActiveEngines == 0, "No controlled ship leaves spatial audio stopped");

        var busy = new SimWorld(); busy.Add(me);
        for (int i = 0; i < 40; i++)
        {
            var ship = busy.Add(new ShipBody($"BUSY{i}", ShipClass.Interceptor, Faction.Blue));
            ship.Place(new Vec3d(1000 + i * 100, 0, -1000), Quaternion.Identity);
        }
        _audio.Prime(busy, me);
        foreach (var ship in busy.Ships.Skip(1)) worldFire(ship);
        _audio.Observe(busy, me); _audio.Advance(.4f);
        Check(_audio.ActiveEngines == 4 && _audio.ActiveBursts <= 4, "Crowded battle selects four engines and caps starts per frame");
        Check(_audio.HasEngine(busy.Ships[1]) && !_audio.HasEngine(busy.Ships.Last()), "Nearest engines win the limited pool");
        _audio.Duck(1); _audio.Advance(.01f);
        var engine = _audio.GetChildren().OfType<AudioStreamPlayer3D>().First(p => p.Name == "Engine0");
        float quiet = engine.VolumeDb; _audio.Advance(1.1f);
        Check(engine.VolumeDb > quiet + 6, "Own hit ducking reduces surroundings then releases");
        Check(FleetAudio.DistanceGain(1000, 1800, 10000) > FleetAudio.DistanceGain(5000, 1800, 10000), "Distance attenuates monotonically");
        Check(FleetAudio.DistanceGain(10000, 1800, 10000) == 0, "Events are silent beyond their audible radius");
        var offset = new Vec3d(1e12, -1e12, 1e12);
        Check(FleetAudio.RelativePosition(offset + new Vec3d(1000, 250, -900), offset)
            .DistanceTo(FleetAudio.RelativePosition(new Vec3d(1000, 250, -900), Vec3d.Zero)) < .00001f,
            "Large world coordinates preserve exact audio bearing");

        var pdWorld = new SimWorld();
        var pd = pdWorld.Add(new ShipBody("PD", ShipClass.Battleship, Faction.Blue));
        var enemy = pdWorld.Add(new ShipBody("ENEMY", ShipClass.Interceptor, Faction.Red));
        enemy.Place(new Vec3d(1400, 900, -1800), Quaternion.Identity);
        pd.Control = enemy.Control = new ShipControl { FlightAssist = false };
        _audio.Prime(pdWorld, enemy); int pdBefore = _audio.PlayCount(CombatSound.PointDefense);
        for (int i = 0; i < 180; i++) { enemy.Damage.Reset(); pdWorld.Step(); }
        _audio.Observe(pdWorld, enemy); _audio.Advance(.2f);
        Check(pd.Ordnance.PointDefense.Any(p => p.ShotCount > 0), "Actual nearby mechanical PD mounts fire");
        Check(_audio.PlayCount(CombatSound.PointDefense) == pdBefore + 1, "Neighbor PD uses spatial burst bank");
        _audio.StopAll();
        void worldFire(ShipBody ship) { Check(busy.FireRailgun(ship, Vector3.Forward).Fired, "Crowded fixture fires real gun"); }
    }

    private void Phase(int phase)
    {
        _phase = phase; _recording = false; _phaseStart = Time.GetTicksMsec();
        if (phase >= 5)
        { SoundSettings.SetMuted(false, false); _audio.SetPaused(phase == 5); return; }
        Vec3d origin = phase == 3 ? new Vec3d(1e12, -1e12, 1e12) : Vec3d.Zero;
        _me.Place(origin, Quaternion.Identity);
        _near.Place(origin + new Vec3d(phase == 2 ? 18000 : 1500, 0, 0), Quaternion.Identity);
        _audio.Prime(_world, _me);
        _audio.SyncListener(origin, phase == 1 ? new Basis(Vector3.Up, Mathf.Pi) : Basis.Identity);
        SoundSettings.SetMuted(phase == 4, false);
    }

    public override void _Process(double delta)
    {
        try
        {
            _audio.Advance((float)Math.Min(delta, .1));
            ulong elapsed = Time.GetTicksMsec() - _phaseStart;
            if (!_recording && elapsed > 600) { _record.SetRecordingActive(true); _recording = true; }
            if (elapsed < 2100) return;
            _record.SetRecordingActive(false);
            using var wav = _record.GetRecording(); byte[] data = wav.Data;
            Check(data.Length > 4000 && wav.Stereo, "Spatial mixer records stereo PCM");
            double left = 0, right = 0; int peak = 0;
            for (int i = 0; i + 3 < data.Length; i += 4)
            {
                int l = BitConverter.ToInt16(data, i), r = BitConverter.ToInt16(data, i + 2);
                left += (double)l * l; right += (double)r * r; peak = Math.Max(peak, Math.Max(Math.Abs(l), Math.Abs(r)));
            }
            _levels[_phase] = (Math.Sqrt(left / (data.Length / 4)) / 32768, Math.Sqrt(right / (data.Length / 4)) / 32768);
            Check(peak < 31130, "Spatial mix has headroom below limiter");
            string directory = OS.HasFeature("editor") ? "res://shots/" : "user://";
            Check(wav.SaveToWav(directory + $"fleet_audio_{_phase}.wav") == Error.Ok, "Spatial preview WAV saved");
            GD.Print($"Fleet audio phase {_phase}: L {_levels[_phase].Left:0.00000}, R {_levels[_phase].Right:0.00000}, peak {peak / 32768f:0.000}");
            if (_phase < 6) { Phase(_phase + 1); return; }
            Check(_levels[0].Right > _levels[0].Left * 1.3 && _levels[0].Right > .002, "Source to camera right is audibly right-panned");
            Check(_levels[1].Left > _levels[1].Right * 1.3, "Turning camera 180 degrees reverses recorded stereo");
            Check(_levels[2].Right < _levels[0].Right * .3, "Distant engine is materially quieter in the real mixer");
            Check(_levels[3].Right > _levels[3].Left * 1.3 && _levels[3].Right > _levels[0].Right * .6 &&
                _levels[3].Right < _levels[0].Right * 1.5, "Floating origin jump preserves stereo and level");
            Check(_levels[4].Left < .00001 && _levels[4].Right < .00001, "Existing mute silences the spatial mix");
            Check(_levels[5].Left < .00001 && _levels[5].Right < .00001, "Pause is silent in recorded output");
            Check(_levels[6].Right > .002 && _levels[6].Right > _levels[6].Left * 1.3, "Resume restores audible spatial loop");
            Restore(); GD.Print($"PASS: {_checks} fleet audio checks"); SetProcess(false); GetTree().Quit();
        }
        catch (Exception ex) { Fail(ex); }
    }
    private void Restore()
    {
        _audio?.StopAll(); SoundSettings.SetVolume(_volume, false); SoundSettings.SetMuted(_muted, false);
        if (_recordIndex >= 0)
        { _record.SetRecordingActive(false); AudioServer.RemoveBusEffect(0, _recordIndex); _record.Dispose(); _recordIndex = -1; }
    }
    private void Fail(Exception ex) { Restore(); GD.PushError(ex.ToString()); SetProcess(false); GetTree().Quit(1); }
}
