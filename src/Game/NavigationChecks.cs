using System;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>--navigation-test: 관찰 유지 시간, 부드러운 복귀, 센서 기반 3D 레이더 좌표.</summary>
public partial class NavigationChecks : Node
{
    private int _checks;
    private void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); _checks++; }

    public override void _Ready()
    {
        try
        {
            CheckCamera(); CheckRadar(); CheckWorldMap();
            GD.Print($"PASS: {_checks} camera/radar/world map checks"); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void CheckWorldMap()
    {
        var map = new WorldMapState();
        Vec3d[] positions = { new(-12000, -4000, 5000), new(7000, 8000, -160000) };
        map.Fit(positions);
        Check(map.Initialized && map.Center != positions[0] && map.Center != positions[1], "Fit covers the battlefield rather than centering one ship");
        foreach (var p in positions) Check(map.Contains(p) && map.Project(p).Length() < 1, "Both sides fit inside the sphere with margins");
        Check(map.Contains(map.Center + new Vec3d(.5, .5, .5) * map.HalfSpan)
            && !map.Contains(map.Center + new Vec3d(.8, .8, 0) * map.HalfSpan), "Map volume is a sphere rather than a box");
        Vec3d center = map.Center;
        double span = map.HalfSpan;
        map.Project(positions[0] + new Vec3d(1e6, 2e6, -1e6));
        Check(map.Center == center && map.HalfSpan == span, "Moving contacts never pan or refit the map");

        var far = new WorldMapState();
        Vec3d offset = new(1e12, -1e12, 1e12);
        far.Fit(new[] { positions[0] + offset, positions[1] + offset });
        Check(far.Project(positions[0] + offset).IsEqualApprox(map.Project(positions[0])), "Large absolute coordinates retain local map precision");
        Vector3 before = map.Project(positions[0]);
        map.Zoom(.5f, new Vector2(before.X, before.Y));
        Vector3 after = map.Project(positions[0]);
        Check(new Vector2(before.X, before.Y).DistanceTo(new Vector2(after.X, after.Y)) < .0001f, "Zoom holds the world position under the cursor");
        Check(map.HalfSpan == span * .5, "Zoom changes scale");
        before = after;
        map.Pan(new Vector2(.3f, -.2f)); after = map.Project(positions[0]);
        Check(new Vector2(after.X - before.X, after.Y - before.Y).DistanceTo(new(.3f, -.2f)) < .0001f, "Drag moves the map in screen direction at its rotated angle");
        center = map.Center; span = map.HalfSpan; before = after;
        map.Orbit(new Vector2(120, 30));
        Check(map.Center == center && map.HalfSpan == span && !map.Project(positions[0]).IsEqualApprox(before), "Rotation reveals depth without following a ship or changing scale");
        Check(map.Contains(center + new Vec3d(0, 0, .9) * span)
            && !map.Contains(center + new Vec3d(0, 0, 1.1) * span), "Sphere boundary includes depth after rotation");
        map.Zoom(1e-9f, Vector2.Zero); Check(map.HalfSpan == 250, "Map zoom is bounded near individual ships");
        map.Zoom(1e12f, Vector2.Zero); Check(map.HalfSpan == 5e8, "Map zoom remains bounded at far distances");

        var enemy = new ShipBody("ENEMY", ShipClass.Escort, Faction.Red);
        enemy.Place(positions[1], Quaternion.Identity);
        var track = new SensorTrack(TrackLevel.Contact, 1, 1, 0, 0, 100, enemy.Position, new(100, -50, 20));
        Check(WorldMapState.KnownPosition(Faction.Blue, enemy, SensorTrack.Unknown) is null, "Full map does not reveal undetected enemies");
        Check(WorldMapState.KnownPosition(Faction.Blue, enemy, track) == enemy.Position + track.Offset, "Full map uses uncertain enemy positions");
        Check(WorldMapState.KnownPosition(Faction.Red, enemy, SensorTrack.Unknown) == enemy.Position, "Friendly datalink remains exact");
        var m = new InputEventKey { PhysicalKeycode = Key.M, Pressed = true };
        var f10 = new InputEventKey { PhysicalKeycode = Key.F10, Pressed = true };
        Check(m.IsActionPressed(InputSetup.WorldMap) && !m.IsActionPressed(InputSetup.ToggleMute)
            && f10.IsActionPressed(InputSetup.ToggleMute), "Map and mute have distinct physical keys");
    }

    private void CheckCamera()
    {
        var camera = new ChaseCamera { Mode = CameraMode.ShipFollow }; AddChild(camera);
        camera.ResetAim(Quaternion.Identity); camera.SetFreeLook(true); camera.AddMouse(new Vector2(350, -100));
        void Follow(float delta) => camera.Follow(ShipDefinitions.For(HullKind.Battleship), Vector3.Zero, Quaternion.Identity, delta);
        for (int i = 0; i < 120; i++) Follow(1f / 60);
        Vector3 released = camera.AimForward;
        camera.SetFreeLook(false);
        Check(!camera.FreeLooking && camera.FreeLookHoldRemaining == 3, "Release stops mouse tracking and starts three second hold");
        for (int i = 0; i < 174; i++) Follow(1f / 60);
        Check(camera.AimForward.AngleTo(released) < .001f, "Released view stays fixed for 2.9 seconds");
        float remaining = camera.FreeLookHoldRemaining;
        Follow(0); Check(camera.FreeLookHoldRemaining == remaining, "Pause does not consume hold time");
        Follow(.05f); Check(camera.AimForward.AngleTo(released) < .001f, "No early recenter before three seconds");
        Follow(.1f);
        Check(camera.FreeLookHoldRemaining == 0 && camera.AimForward.AngleTo(released) < .1f, "Deadline crossing starts smooth return without a snap");
        for (int i = 0; i < 180; i++) Follow(1f / 60);
        Check(camera.AimForward.AngleTo(Vector3.Forward) < .02f, "Camera returns to the ship heading");

        camera.SetFreeLook(true); camera.AddMouse(new Vector2(-300, 70)); Follow(.25f); camera.SetFreeLook(false); Follow(1);
        camera.SetFreeLook(true); Check(camera.FreeLookHoldRemaining == 0, "New drag cancels pending recenter");
        Vector3 before = camera.AimForward;
        camera.AddMouse(new Vector2(80, 0)); Follow(.25f);
        Check(camera.AimForward.AngleTo(before) > .01f, "New drag responds immediately during hold");
        camera.SetFreeLook(false); Check(camera.FreeLookHoldRemaining == 3, "Next release restarts the full delay");
        camera.ResetAim(Quaternion.Identity);
        Check(camera.FreeLookHoldRemaining == 0 && !camera.FreeLooking, "Ship handoff clears observation state");

        // 프레임을 나누는 간격이 달라도 실제 유지 시간은 같다.
        foreach (float dt in new[] { 1f / 30, 1f / 144 })
        {
            camera.ResetAim(Quaternion.Identity); camera.Turn(40, 10);
            for (int i = 0; i < 180; i++) Follow(1f / 60);
            camera.SetFreeLook(false); Vector3 initial = camera.AimForward;
            int frames = (int)(2.8f / dt);
            for (int i = 0; i < frames; i++) Follow(dt);
            Check(camera.AimForward.AngleTo(initial) < .001f && camera.FreeLookHoldRemaining > 0, $"Hold is frame-rate independent at {1 / dt:0} Hz");
        }
        camera.Mode = CameraMode.MouseAim; camera.ResetAim(Quaternion.Identity);
        camera.AddMouse(new Vector2(30, 0)); Follow(.016f);
        Check(camera.AimForward.AngleTo(Vector3.Forward) > .01f, "Pilot mouse aiming remains immediate");
        camera.QueueFree();
    }

    private void CheckRadar()
    {
        var radar = new TacticalRadar();
        Check(radar.Range == 200000, "Default range contains the initial 150 km battle");
        for (int i = 0; i < 10; i++) radar.Zoom(-1);
        Check(radar.Range == 2000, "Near range is clamped at two kilometers");
        for (int i = 0; i < 10; i++) radar.Zoom(1);
        Check(radar.Range == 500000, "Far range is clamped at 500 kilometers");
        var me = new ShipBody("ME", ShipClass.Interceptor, Faction.Blue);
        var enemy = new ShipBody("ENEMY", ShipClass.Escort, Faction.Red);
        var ally = new ShipBody("ALLY", ShipClass.Escort, Faction.Blue);
        Vec3d at = new(3000, 4000, -12000);
        var contact = new SensorTrack(TrackLevel.Contact, 1, 1, 0, 0, 100, at, new Vector3(100, -50, 20));
        RadarContact? Locate(ShipBody ship, SensorTrack track, Vec3d origin = default, Quaternion? orientation = null)
            => TacticalRadar.Locate(me, origin, orientation ?? Quaternion.Identity, ship, origin + at, track);
        Check(Locate(enemy, SensorTrack.Unknown) is null, "Undetected enemies are hidden");
        Check(Locate(me, contact) is null, "Own ship is not listed as a contact");
        RadarContact a = Locate(enemy, contact)!.Value;
        Check(a.Enemy && !a.Identified && a.Uncertainty == 100, "Unidentified contact keeps its uncertainty without identity");
        Check(a.Local.IsEqualApprox(new Vector3(3100, 3950, -11980)), "Radar uses the sensor offset rather than exact enemy position");
        Check(Math.Abs(a.Distance - a.Local.Length()) < .01, "Distance includes all three spatial axes");
        RadarContact far = Locate(enemy, contact, new Vec3d(1e12, -1e12, 1e12))!.Value;
        Check(a.Local.IsEqualApprox(far.Local), "Floating-origin jumps do not change radar coordinates");
        var turned = Locate(enemy, contact, orientation: new Quaternion(Vector3.Up, Mathf.Pi / 2))!.Value;
        Check(Math.Abs(turned.Local.Length() - a.Local.Length()) < .01 && turned.Local.X > 11000, "Radar rotates into the ship frame while preserving distance");
        RadarContact friend = Locate(ally, SensorTrack.Unknown)!.Value;
        Check(!friend.Enemy && friend.Identified && friend.Local.IsEqualApprox(at.ToVector3()), "Friendly datalink is exact even without an enemy sensor track");
        enemy.Damage.Breakup(0, "test");
        Check(Locate(enemy, contact) is not null, "Unidentified wreck state is not revealed by hiding a contact");
        Check(Locate(enemy, contact with { Level = TrackLevel.Identified }) is null, "Known destroyed ships leave the tactical map");
        Vector3 above = TacticalRadar.Project(new Vector3(.2f, .5f, -.3f));
        Vector3 below = TacticalRadar.Project(new Vector3(.2f, -.5f, -.3f));
        Check(above.Y < below.Y && above.Z != below.Z, "Height changes both screen elevation and depth");
        Check(TacticalRadar.Project(Vector3.Forward).Y < 0 && TacticalRadar.Project(Vector3.Back).Y > 0, "Forward and aft remain distinguishable in the sphere");
    }
}
