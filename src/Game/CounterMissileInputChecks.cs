using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class WeaponInputChecks
{
    public bool CounterMissileOnly { get; init; }
    private void CheckCounterMissileInput()
    {
        InputSetup.Register();
        foreach (var design in new[] { DesignFamily.Earth, DesignFamily.Mars })
        {
            var g = new ScaleTest { BattleMode = true, DevMode = false, LaunchControl = "IC-21",
                LaunchConfig = new BattleConfig { BlueDesign = design } };
            AddChild(g); g.SetProcess(false); g.SetPhysicsProcess(false);
            var ship = g.Controlled!.Body;
            int index = 0;
            foreach (var body in g.World.Ships)
            { g.World.DetachBrain(body); body.Place(new Vec3d(1e9, ++index * 10000, 0), Quaternion.Identity); body.Velocity = Vector3.Zero; }
            ship.Place(Vec3d.Zero, Quaternion.Identity);
            var enemy = g.World.Ships.First(s => s.Faction != ship.Faction && s.Class.Kind == HullKind.Escort);
            enemy.Place(new Vec3d(1500, 0, -16000), Quaternion.Identity);
            g.World.Sensors.Update(g.World.Ships, 0, true);
            Check(g.World.LaunchMissile(enemy, ship).Fired, "Counter input fixture launches a heavy threat");
            var threat = g.World.Missiles.Last(); threat.Position = new(0, 0, -6000);
            threat.Velocity = Vector3.Back * 3000; threat.NoseDirection = Vector3.Back;
            g.World.Sensors.Update(g.World.Ships, 0, true, g.World.Probes, g.World.Missiles);
            g.Camera.ResetAim(ship.Orientation); g._Process(.016); Follow(g);
            g.SelectTargetAt(g.Camera.UnprojectPosition((threat.Position - g.RenderOrigin).ToVector3()));
            Check(g.SelectedSmallContact?.Target == threat, "Pointer selects the incoming missile");
            Input.MouseMode = Input.MouseModeEnum.Captured; Mouse(g, MouseButton.Left, false);
            KeyEvent(g, Key.Key2); Tick(g);
            Check(g.SmallAimSolved && g.SmallAimStatus.Contains("요격 미사일"), "Weapon 2 shows the valid counter-missile action");
            Mouse(g, MouseButton.Left); Mouse(g, MouseButton.Left, false);
            var counter = g.World.Missiles.Last();
            Check(counter.InterceptTarget == threat && counter.Target is null && ship.Ordnance.Missiles == 3
                && ship.Ordnance.Antimatter.Rounds == 3 && ship.Railgun!.Rounds == 200,
                "Weapon 2 click launches one light missile without firing a gun or torpedo");
            Mouse(g, MouseButton.Left); Mouse(g, MouseButton.Left, false);
            Check(ship.Ordnance.Missiles == 3, "Repeated click during reload does not double-launch");
            Tick(g, 130);
            Check(ship.Ordnance.Interceptor.MissileKills == 1 && !g.World.Missiles.Contains(threat), "Scene missile guidance physically intercepts the target");
            Check(!g.TrackingSmallAim, "Missile selection never commandeers the ship's heading");
            g.Free();
        }
    }
}
