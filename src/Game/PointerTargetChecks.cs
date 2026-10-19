using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class WeaponInputChecks
{
    private void CheckPointerTargets()
    {
        InputSetup.Register();
        var g = new ScaleTest { BattleMode = true, DevMode = false, LaunchControl = "IC-21" }; AddChild(g);
        g.SetProcess(false); g.SetPhysicsProcess(false);
        var ship = g.Controlled!.Body;
        int n = 0;
        foreach (var body in g.World.Ships)
        {
            g.World.DetachBrain(body); body.Place(new Vec3d(1e9, ++n * 10000, 0), Quaternion.Identity);
            body.Velocity = Vector3.Zero;
        }
        ship.Place(Vec3d.Zero, Quaternion.Identity);
        var enemy = g.Views.First(v => v.Body.Faction != ship.Faction && v.Body.Class.Kind == HullKind.Interceptor);
        enemy.Body.Place(new Vec3d(700, 0, -8000), Quaternion.Identity);
        g.World.Sensors.Update(g.World.Ships, g.World.Time, true);
        Check(g.World.LaunchMissile(enemy.Body, ship).Fired, "Target fixture launches an enemy missile");
        var missile = g.World.Missiles.Last();
        missile.Position = new Vec3d(420, 0, -2800); missile.Velocity = new Vector3(180, 0, 900);
        missile.NoseDirection = missile.Velocity.Normalized();
        Check(g.World.LaunchProbe(enemy.Body, Vector3.Forward).Fired, "Target fixture deploys enemy probe");
        var probe = g.World.Probes.Last(); probe.Position = probe.Destination = new Vec3d(-550, 120, -2700); probe.Velocity = Vector3.Zero;
        void Observe()
        {
            g.World.Sensors.Update(g.World.Ships, g.World.Time, true, g.World.Probes, g.World.Missiles);
            Tick(g); g._Process(.016); Follow(g);
        }
        Vector2 Screen(Vec3d p) => g.Camera.UnprojectPosition((p - g.RenderOrigin).ToVector3());
        g.Camera.ResetAim(ship.Orientation); Observe();
        int rounds = ship.Railgun!.Rounds;
        var before = g.Camera.AimForward;
        g.SelectTargetAt(Screen(missile.Position));
        Check(g.SelectedSmallContact?.Target == missile && g.InspectTarget is null && g.FireTarget is null,
            "Cursor chooses a missile, clearing the old ship firing target");
        Check(g.Camera.AimForward == before && ship.Railgun.Rounds == rounds,
            "Selection never snaps the camera or consumes ammunition");
        Check(g.CorrectingSmallAim && ship.Forward.AngleTo(g.SmallAimDirection) > Mathf.DegToRad(6),
            "Explicit missile selection assists beyond the old six-degree cursor cone within the gun arc");
        // Missiles are now valid weapon-2 targets; probes must still never fall back to the old ship.
        Input.ActionPress(InputSetup.Fire);
        g.SelectTargetAt(Screen(probe.Position));Tick(g);
        Check(ship.Railgun.Rounds==rounds,"Held fire cannot discharge on the first tick of a new small-target selection");
        Input.ActionRelease(InputSetup.Fire);Mouse(g,MouseButton.Left,false);
        KeyEvent(g, Key.Key2); int missiles = ship.Ordnance.Missiles;
        Mouse(g, MouseButton.Left); Mouse(g, MouseButton.Left, false);
        Check(ship.Ordnance.Missiles == missiles, "Unsupported probe target cannot launch at the previous ship");
        KeyEvent(g, Key.Key1); Observe();
        g.SelectTargetAt(Screen(probe.Position));
        Check(g.SelectedSmallContact?.Target == probe, "Probe nearest the cursor takes priority over ship and missile");
        g.SelectTargetAt(Screen(g.ContactOf(enemy)!.Position));
        Check(g.InspectTarget == enemy && g.SelectedSmallContact is null, "Choosing a ship clears ordnance selection");
        g.SelectTargetAt(Screen(missile.Position));
        probe.Position = probe.Destination = new Vec3d(0, 0, -2200); Observe();
        Check(g.SelectedSmallContact?.Target == missile, "New closer contacts never steal an explicit selection");
        Input.MouseMode = Input.MouseModeEnum.Captured; KeyEvent(g, Key.R);
        Check(g.SelectedSmallContact?.Target == probe, "Actual R key uses the flight crosshair when the mouse is captured");
        KeyEvent(g, Key.N); KeyEvent(g, Key.R); KeyEvent(g, Key.Escape);
        Check(g.SelectedSmallContact?.Target == probe, "Radial consumes R without changing target");
        Mouse(g, MouseButton.Left, false);
        // A steady crossing missile: use the existing physical projectile/collision path, not a hit roll.
        probe.Position = probe.Destination = new Vec3d(-900, 0, -2700);
        missile.Position = new Vec3d(340, 0, -3200); missile.Velocity = new Vector3(180, 0, 900);
        missile.Age = missile.Definition.BurnSeconds + 1;
        Tick(g, 16); Observe(); g.SelectTargetAt(Screen(missile.Position));
        rounds = ship.Railgun.Rounds;
        Input.MouseMode = Input.MouseModeEnum.Captured; Mouse(g, MouseButton.Left, false);
        Input.ActionPress(InputSetup.Fire); Tick(g, 35); Input.ActionRelease(InputSetup.Fire);
        Check(ship.Railgun.Rounds < rounds && ship.Ordnance.Interceptor.MissileKills > 0,
            $"Selected crossing missile: rounds {rounds}->{ship.Railgun.Rounds}, kills {ship.Ordnance.Interceptor.MissileKills}, health {missile.Health}, active {g.World.Missiles.Contains(missile)}, mode {Input.MouseMode}, status {g.SmallAimStatus}, message {g.LastFireMessage}, position {missile.Position}, shots {g.World.Projectiles.Count}");
        Tick(g, 35);
        Check(g.SelectedSmallContact?.Target == missile && !g.CorrectingSmallAim && g.SmallAimStatus == "신호 소실",
            "Stale selected missile retains its last observation but loses aim assistance");
        rounds = ship.Railgun.Rounds;
        Input.ActionPress(InputSetup.Fire); Tick(g); Input.ActionRelease(InputSetup.Fire);
        Check(ship.Railgun.Rounds == rounds, "Lost-contact assist cannot waste a round on a hidden target");
        // A powered turning missile exercises the acceleration estimated from successive sensor samples.
        enemy.Body.Ordnance.Reset(); ship.Railgun.Reset();
        Check(g.World.LaunchMissile(enemy.Body, ship).Fired, "Powered missile fixture launches");
        var powered = g.World.Missiles.Last(); powered.Position = new Vec3d(160, 0, -3500);
        powered.Velocity = new Vector3(240, 0, 1000); powered.NoseDirection = powered.Velocity.Normalized();
        Observe(); Tick(g, 30); g.SelectTargetAt(Screen(powered.Position));
        int kills = ship.Ordnance.Interceptor.MissileKills;
        Input.ActionPress(InputSetup.Fire); Tick(g, 45); Input.ActionRelease(InputSetup.Fire);
        Check(ship.Ordnance.Interceptor.MissileKills > kills,
            $"Selected burning missile receives usable lead from observed acceleration: {g.SmallAimStatus}, health {powered.Health}");
        g.SelectTargetAt(Screen(probe.Position));
        probe.Position = probe.Destination = new Vec3d(2000, 0, -2200); Observe();
        rounds = ship.Railgun.Rounds;
        Input.ActionPress(InputSetup.Fire); Tick(g); Input.ActionRelease(InputSetup.Fire);
        Check(!g.CorrectingSmallAim && ship.Railgun.Rounds == rounds, "Selection cannot bypass the fixed gun traverse limit");
        probe.Position = probe.Destination = new Vec3d(0, 0, -10000);
        var observer = g.World.Ships.First(s => s != ship && s.Faction == ship.Faction && s.Class.Kind == HullKind.Escort);
        observer.Place(new Vec3d(3000, 0, -9000), Quaternion.Identity); Observe();
        Check(!g.SmallAimSolved, "Selected small target outside interception range has no firing solution");
        // Map input and preview use the same destination, sensor strength and moving observation point.
        KeyEvent(g, Key.M);
        var map = g.GetNode<WorldMapOverlay>("WorldMapLayer/WorldMap");
        map.Map.Fit(new[] { ship.Position, new Vec3d(-40000, 0, -40000), new Vec3d(40000, 0, 40000) });
        Vector2 pointer = new(700, 380);
        var preview = map.PreviewAt(pointer);
        Check(preview is { Ready: true }, "Map produces a deployable probe preview under the cursor");
        g._UnhandledInput(new InputEventMouseMotion { Position = pointer, ShiftPressed = true });
        Check(map.ActiveProbePreview is not null, "Holding Shift previews the cursor position without launching");
        var movedPointer = pointer + new Vector2(60, 25);
        g._UnhandledInput(new InputEventMouseMotion { Position = movedPointer, ShiftPressed = true });
        Check(map.ActiveProbePreview?.Destination != preview?.Destination, "Probe preview follows mouse motion");
        KeyEvent(g, Key.Shift, false);
        Check(map.ActiveProbePreview is null, "Releasing Shift hides the preview");
        Check(preview is { } level && Math.Abs(level.Destination.Y - ship.Position.Y) < 1,
            "Plain placement lies level with the controlled ship, whatever the map angle");
        // Shift+drag raises the point, release arms it, right click cancels without launching.
        int stock = ship.Ordnance.Interceptor.Probes;
        void MapClick(Vector2 at, bool pressed, bool shift = false, MouseButton button = MouseButton.Left)
            => g._UnhandledInput(new InputEventMouseButton { Position = at, ShiftPressed = shift, ButtonIndex = button, Pressed = pressed });
        MapClick(pointer, true, true);
        g._UnhandledInput(new InputEventMouseMotion { Position = pointer - new Vector2(0, 80) });
        Check(map.ActiveProbePreview is { } raised && raised.Destination.Y > ship.Position.Y + 1000 && map.ProbeHeight > 0
            && Math.Abs(raised.Requested.X - preview!.Value.Requested.X) < 1 && Math.Abs(raised.Requested.Z - preview.Value.Requested.Z) < 1,
            "Dragging up raises the probe point above its ground position");
        g._UnhandledInput(new InputEventMouseMotion { Position = pointer + new Vector2(0, 80) });
        Check(map.ActiveProbePreview is { } lowered && lowered.Destination.Y < ship.Position.Y - 1000, "Dragging down lowers it");
        MapClick(pointer + new Vector2(0, 80), false);
        Check(map.ProbeArmed && ship.Ordnance.Interceptor.Probes == stock && map.ActiveProbePreview is not null,
            "Releasing a drag arms the placement without launching");
        MapClick(pointer, true, button: MouseButton.Right);
        Check(!map.ProbeArmed && ship.Ordnance.Interceptor.Probes == stock, "Right click cancels an armed placement");
        MapClick(pointer, true, true); g._UnhandledInput(new InputEventMouseMotion { Position = pointer - new Vector2(0, 60) });
        MapClick(pointer - new Vector2(0, 60), false);
        var armed = map.ActiveProbePreview!.Value;
        MapClick(pointer + new Vector2(200, 0), true); MapClick(pointer + new Vector2(200, 0), false);
        var lifted = g.World.Probes.Last(p => p.Owner == ship);
        Check(!map.ProbeArmed && ship.Ordnance.Interceptor.Probes == stock - 1 && (lifted.Destination - armed.Destination).Length() < .01
            && lifted.Destination.Y > ship.Position.Y + 1000, "A click after the drag launches to the raised point, wherever it lands");
        // A plain Shift+click still launches straight away at the level point.
        ship.Ordnance.Interceptor.NextLaunch = 0; ship.Ordnance.Interceptor.Probes++;
        g._UnhandledInput(new InputEventMouseMotion { Position = pointer, ShiftPressed = true });
        MapClick(pointer, true, true); MapClick(pointer, false, true);
        var deployed = g.World.Probes.Last(p => p.Owner == ship);
        Check(preview is { } p && (deployed.Destination - p.Destination).Length() < .01,
            "Shift click deploys to exactly the previewed location");
        var volume = map.SensorVolumes().Single(v => v.Probe == deployed.Definition && v.Position == deployed.Position);
        Check(volume.Position != deployed.Destination && volume.Radius(deployed.Direction) > volume.Radius(-deployed.Direction),
            "Probe range follows its actual position and respects its weaker rear sensitivity");
        Check(map.SensorVolumes().Count(v => v.Probe is null) == g.World.Ships.Count(s => s.Faction == ship.Faction && SensorNet.Strength(s) > .01f),
            "Map exposes friendly sensor ranges only");
        var near = ship.Position + Vec3d.From(Vector3.Forward) * 5000;
        Check(SimWorld.TryProbeDestination(ship, near, out var close) && (close - near).Length() < .01,
            "Nearby map placement no longer overshoots to the fixed thirty-kilometer distance");
        Check(SimWorld.TryProbeDestination(ship, ship.Position + Vec3d.From(Vector3.Right) * 100000, out var far)
            && Math.Abs((far - ship.Position).Length() - ship.Hull.BoundingRadius - 4 - ship.Definition.ReconProbes!.DeploymentMeters) < .01,
            "Map placement beyond deployment reach clamps to the actual maximum");
        foreach (var sensor in ship.Damage.Modules.Where(m => m.Definition.Kind == ModuleKind.Sensor))
            ship.Damage.Hurt(sensor, sensor.Health, g.World.Time, 0, 1);
        Check(!map.SensorVolumes().Any(v => v.Probe is null && v.Position == ship.Position),
            "Destroyed friendly sensors remove their nominal coverage");
        g.Free();
        CheckHelmSmallTarget();
    }

    private void CheckHelmSmallTarget()
    {
        var g = new ScaleTest { LaunchControl = "BB-01" }; AddChild(g);
        g.SetProcess(false); g.SetPhysicsProcess(false);
        foreach (var body in g.World.Ships) g.World.DetachBrain(body);
        var ship = g.Controlled!.Body;
        int index = 0;
        foreach (var other in g.World.Ships.Where(s => s != ship)) other.Place(new Vec3d(1e9, ++index * 10000, 0), Quaternion.Identity);
        var enemy = g.Views.First(v => v.Body.Faction != ship.Faction && v.Body.Class.Kind == HullKind.Interceptor);
        enemy.Body.Place(ship.Position + Vec3d.From(ship.Forward) * 4000, ship.Orientation);
        g.World.LaunchProbe(enemy.Body, enemy.Body.Forward);
        var probe = g.World.Probes.Last();
        probe.Position = probe.Destination = ship.Position + Vec3d.From(ship.Forward * 12000 + ship.Up * 1500);
        probe.Velocity = Vector3.Zero;
        g.World.Sensors.Update(g.World.Ships, g.World.Time, true, g.World.Probes, g.World.Missiles);
        Tick(g); g._Process(.016);
        Vector2 point = g.Camera.UnprojectPosition((probe.Position - g.RenderOrigin).ToVector3());
        Input.MouseMode = Input.MouseModeEnum.Visible; g.GetViewport().WarpMouse(point);
        g._UnhandledInput(new InputEventMouseMotion { Position = point }); KeyEvent(g, Key.R);
        Check(g.SelectedSmallContact?.Target == probe && g.Gunnery!.PlayerSmallTarget,
            $"Helm R near free cursor: intended {point}, actual {g.GetViewport().GetMousePosition()}, selection {g.SelectedSmallContact?.Kind}, ship {g.InspectTarget?.Body.Callsign}, status {g.LastFireMessage}");
        int rounds = ship.Railguns.Sum(x => x.Rounds);
        Tick(g, 30);
        Check(ship.Railguns.Sum(x => x.Rounds) == rounds && g.Gunnery!.Engaged is null,
            "Small selection cannot secretly continue automatic fire at an old ship target");
        Mouse(g, MouseButton.Right);
        for (int i = 0; i < 3; i++)
        {
            g.Camera.Observe((probe.Position - g.RenderOrigin - Vec3d.From(g.Camera.Position)).ToVector3().Normalized(), ship.Orientation);
            g._Process(.1);
        }
        KeyEvent(g, Key.R);
        Check(g.Camera.TelescopeHeld && g.SelectedSmallContact is not null, $"Scope ordnance selection: held {g.Camera.TelescopeHeld}, alive {probe.Active}, fresh {g.World.Sensors.SmallTrack(ship.Faction, probe)?.Fresh(g.World.Time)}, selected {g.InspectTarget?.Body.Callsign}, status {g.LastFireMessage}, screen {g.Camera.UnprojectPosition((probe.Position - g.RenderOrigin).ToVector3())}, crosshair {g.Camera.AimScreenPosition()}");
        Mouse(g, MouseButton.Right, false);
        Check(g.Gunnery!.PlayerSmallTarget, "Scope exit does not resume unrelated automatic fire");
        KeyEvent(g, Key.F9);
        Check(g.SelectedSmallContact is null, "Control handover clears old ordnance selection");
        g.Free();
    }
}
