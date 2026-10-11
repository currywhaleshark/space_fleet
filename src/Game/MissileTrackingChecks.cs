using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class WeaponInputChecks
{
    public bool TrackingOnly { get; init; }
    private void CheckMissileTracking()
    {
        InputSetup.Register();
        foreach (var design in new[] { DesignFamily.Earth, DesignFamily.Mars })
        foreach (float speed in new[] { 3000f, 6000f, 8500f })
        {
            var g = new ScaleTest { BattleMode = true, DevMode = false, LaunchControl = "IC-21",
                LaunchConfig = new BattleConfig { BlueDesign = design } };
            AddChild(g); g.SetProcess(false); g.SetPhysicsProcess(false);
            var ship = g.Controlled!.Body;
            int index = 0;
            foreach (var body in g.World.Ships)
            {
                g.World.DetachBrain(body); body.Place(new Vec3d(1e9, ++index * 10000, 0), Quaternion.Identity);
                body.Velocity = Vector3.Zero;
            }
            ship.Place(Vec3d.Zero, Quaternion.Identity);
            // Keep the rail-assist regression on the capital-ship threat profile. The new light
            // missile deliberately has much higher evasive acceleration and lower durability.
            var enemy = g.World.Ships.First(s => s.Faction != ship.Faction && s.Class.Kind == HullKind.Escort);
            enemy.Place(new Vec3d(700, 0, -8000), Quaternion.Identity);
            g.World.Sensors.Update(g.World.Ships, g.World.Time, true);
            Check(g.World.LaunchMissile(enemy, ship).Fired, "Fast incoming missile fixture launches");
            var missile = g.World.Missiles.Last(); missile.Position = new Vec3d(3300, 0, -9500);
            missile.Velocity = Vector3.Back * speed; missile.NoseDirection = Vector3.Back;
            g.World.Sensors.Update(g.World.Ships, g.World.Time, true, g.World.Probes, g.World.Missiles);
            g.Camera.ResetAim(ship.Orientation); g._Process(.016); Follow(g);
            var track = g.World.Sensors.SmallTrack(ship.Faction, missile);
            Check(track is { } c && g.World.SmallTargetAim(ship, c, out _, out _, playerAssist: true)
                && !g.World.SmallTargetAim(ship, c, out _, out _), "Player can prepare early without extending AI engagement range");
            g.SelectTargetAt(g.Camera.UnprojectPosition((missile.Position - g.RenderOrigin).ToVector3()));
            var original = ship.Orientation; var cameraAim = g.Camera.AimForward;
            int ammo = ship.Railgun!.Rounds;
            Input.MouseMode = Input.MouseModeEnum.Captured; Mouse(g, MouseButton.Left, false);
            Check(!g.CorrectingSmallAim && ship.Forward.AngleTo((missile.Position - ship.Position).ToVector3()) > Mathf.DegToRad(18),
                "Fast target starts outside the twelve-degree firing cone");
            Input.ActionPress(InputSetup.Fire); Tick(g);
            Check(g.TrackingSmallAim && g.Camera.AimForward.AngleTo(cameraAim) <= Mathf.DegToRad(2.01f)
                && ship.Railgun.Rounds == ammo && ship.Orientation != original,
                "Held fire begins bounded physical alignment without an instant turn or out-of-arc shot");
            double began = g.World.Time;
            bool unsafeShot = false;
            for (int i = 0; i < 150 && g.World.Missiles.Contains(missile); i++)
            {
                int previousAmmo = ship.Railgun.Rounds;
                Tick(g);
                if (ship.Railgun.Rounds < previousAmmo)
                    unsafeShot |= !g.SmallAimSolved || SimWorld.RailLine(ship, g.SmallAimDirection) != FireFailure.None;
            }
            Input.ActionRelease(InputSetup.Fire); Tick(g);
            Check(!unsafeShot, "Every assisted shot respects the physical firing cone");
            if (speed < 8500) Check(missile.Health < missile.Definition.HitPoints && (speed >= 6000 || ship.Ordnance.Interceptor.MissileKills == 1),
                $"{design} {speed:0}m/s assisted crossing hit, health {missile.Health}, ammo {ammo - ship.Railgun.Rounds}, status {g.SmallAimStatus}");
            else
                Check(ship.Ordnance.Interceptor.MissileKills == 0 && ship.Railgun.Rounds == ammo,
                    "Extreme crossing speed cannot bypass the ship's physical alignment limit");
            Check(!g.TrackingSmallAim, "Releasing fire releases heading assistance");
            GD.Print($"tracking trial {design}: incoming={speed:0}m/s, kills={ship.Ordnance.Interceptor.MissileKills}, elapsed={g.World.Time - began:0.000}s, rounds={ammo - ship.Railgun.Rounds}, closest-approach-at-selection={9500 / speed:0.00}s");
            if (design == DesignFamily.Earth && speed == 3000) CheckTrackingCancellation(g, enemy);
            g.Free();
        }
    }

    private void CheckTrackingCancellation(ScaleTest g, ShipBody enemy)
    {
        var ship = g.Controlled!.Body;
        enemy.Ordnance.Reset();
        g.World.LaunchMissile(enemy, ship);
        var missile = g.World.Missiles.Last();
        missile.Position = ship.Position + Vec3d.From(ship.Forward) * 8500;
        missile.Velocity = -ship.Forward * 3000; missile.NoseDirection = -ship.Forward;
        g.World.Sensors.Update(g.World.Ships, g.World.Time, true, g.World.Probes, g.World.Missiles);
        g.SelectTargetAt(g.Camera.UnprojectPosition((missile.Position - g.RenderOrigin).ToVector3()));
        Input.ActionPress(InputSetup.Fire);
        g._UnhandledInput(new InputEventMouseMotion { Relative = new Vector2(20, 0) }); Tick(g);
        Check(!g.TrackingSmallAim, "Intentional mouse steering temporarily overrides tracking");
        Input.ActionRelease(InputSetup.Fire);
        KeyEvent(g, Key.M); Input.ActionPress(InputSetup.Fire); Tick(g);
        Check(!g.TrackingSmallAim, "Map input cannot start heading tracking or shooting");
        Input.ActionRelease(InputSetup.Fire); KeyEvent(g, Key.M); g._Process(.016);
        missile.Position = ship.Position - Vec3d.From(ship.Forward) * 1500;
        missile.Velocity = -ship.Forward * 3000;
        g.World.Sensors.Update(g.World.Ships, g.World.Time, true, g.World.Probes, g.World.Missiles);
        Input.MouseMode = Input.MouseModeEnum.Captured; Mouse(g, MouseButton.Left, false);
        var before = g.Camera.AimForward; Input.ActionPress(InputSetup.Fire); Tick(g, 30); Input.ActionRelease(InputSetup.Fire);
        Check(!g.TrackingSmallAim && g.Camera.AimForward.IsEqualApprox(before), "Passed missile cannot pull the camera or ship into a pursuit turn");
    }
}
