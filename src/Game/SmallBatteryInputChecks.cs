using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class WeaponInputChecks
{
    private void CheckSmallBatteryInput()
    {
        InputSetup.Register();
        foreach (string id in new[] { "earth_air_defense_cruiser", "earth_air_defense_cruiser_t4", "earth_air_defense_cruiser_t5" })
        {
            var lineup = new MatchLineup(new[] {
                new MatchParticipant(1, Faction.Blue, id, true),
                new MatchParticipant(1, Faction.Red, "interceptor", false) });
            var game = new ScaleTest { LaunchControl="B01-1", LaunchConfig=new BattleConfig { Match=lineup } };
            AddChild(game); game.SetProcess(false); game.SetPhysicsProcess(false);
            var ship = game.Controlled!.Body;
            int n = 0;
            foreach (var body in game.World.Ships)
            {
                game.World.DetachBrain(body);
                body.Place(new Vec3d(1e9, ++n * 10000, 0), Quaternion.Identity);
                body.Velocity = Vector3.Zero;
            }
            ship.Place(Vec3d.Zero, Quaternion.Identity);
            var enemy = game.World.Ships.First(s => s.Faction != ship.Faction);
            enemy.Place(new Vec3d(0, 3000, -10000), Quaternion.Identity);
            game.World.Sensors.Update(game.World.Ships, game.World.Time, true);
            Check(game.World.LaunchMissile(enemy, ship).Fired, id + ": missile fixture launches");
            var missile = game.World.Missiles.Last();
            missile.Position = new Vec3d(0, 1500, -9000); missile.Velocity = Vector3.Zero;
            missile.Age = missile.Definition.BurnSeconds + 1; missile.Health = 100000;
            game.World.Sensors.Update(game.World.Ships, game.World.Time, true, game.World.Probes, game.World.Missiles);
            game.Camera.ResetAim(Quaternion.Identity); game._Process(.016); Follow(game);
            game.Gunnery!.Doctrine = FireDoctrine.Free;
            game.SelectTargetAt(game.Camera.UnprojectPosition((missile.Position - game.RenderOrigin).ToVector3()));
            Check(game.SelectedSmallContact?.Target == missile, id + ": R-target route selects detected missile");
            ulong Pulses() => ship.Railguns.Aggregate(0UL, (sum, gun) => sum + (gun.Laser?.Pulses ?? 0));
            Check(Pulses() == 0, id + ": selection itself never fires");
            Tick(game, 120);
            Check(Pulses() > 0, id + ": automatic doctrine fires at selected missile after traverse");
            Check(missile.Health < 100000, id + ": selected laser physically damages the missile");
            game.Gunnery.Doctrine = FireDoctrine.Hold; SimWorld.StopLasers(ship);
            ulong pulses = Pulses(); Tick(game, 12);
            Check(Pulses() == pulses, id + ": hold order suppresses selected-missile auto-fire");
            Input.ActionPress(InputSetup.Fire); Tick(game, 30); Input.ActionRelease(InputSetup.Fire);
            Mouse(game, MouseButton.Left, false);
            Check(Pulses() > pulses, id + ": held fire works with selected missile in hold doctrine");
            pulses = Pulses(); Tick(game, 12);
            Check(Pulses() == pulses, id + ": release stops manual missile irradiation");
            game.Gunnery.Doctrine = FireDoctrine.Focus; Tick(game, 15);
            Check(Pulses() > pulses, id + ": focused fire also engages selected missile");
            Mouse(game, MouseButton.Right); game._Process(.016);
            SimWorld.StopLasers(ship); pulses = Pulses(); Tick(game, 12);
            Check(Pulses() == pulses, id + ": scope retains manual trigger control");
            Mouse(game, MouseButton.Right, false);
            KeyEvent(game, Key.Key2); pulses = Pulses(); Tick(game, 12);
            Check(Pulses() == pulses, id + ": choosing missiles cannot keep firing primary lasers");
            KeyEvent(game, Key.Key1);
            missile.Position = new Vec3d(0,1500,-ship.Railguns.Max(SimWorld.SmallTargetRange)-4000);
            var relay=game.World.Ships.First(s=>s!=ship&&s.Faction==ship.Faction);
            relay.Place(missile.Position+new Vec3d(2000,0,0),Quaternion.Identity);
            game.World.Sensors.Update(game.World.Ships, game.World.Time, true, game.World.Probes, game.World.Missiles);
            SimWorld.StopLasers(ship); pulses = Pulses(); Tick(game, 12);
            Check(Pulses() == pulses, id + ": selected missile outside intercept range cannot fire");
            missile.Position = new Vec3d(1e12,0,0);
            game.World.Sensors.Update(game.World.Ships, game.World.Time, true, game.World.Probes, game.World.Missiles);
            Tick(game, 45);
            Check(Pulses() == pulses && !game.SmallAimSolved, id + ": lost contact cannot sustain auto-fire");
            // 2026-10-20 report: a missile crossing the bow (several degrees per second) was never irradiated, under
            // either automatic doctrine or a held trigger. Both must now destroy a real, moving, full-health missile.
            foreach (bool held in new[] { false, true })
            {
                enemy.Ordnance.Reset(); game.World.Sensors.Update(game.World.Ships, game.World.Time, true);
                Check(game.World.LaunchMissile(enemy, ship).Fired, id + ": crossing fixture launches");
                var crossing = game.World.Missiles.Last();
                crossing.Position = new Vec3d(-4500, 1200, -6000); crossing.Velocity = new Vector3(1500, 0, 300);
                crossing.NoseDirection = crossing.Velocity.Normalized(); crossing.Age = crossing.Definition.BurnSeconds + 1;
                game.World.Sensors.Update(game.World.Ships, game.World.Time, true, game.World.Probes, game.World.Missiles);
                game._Process(.016);
                game.Gunnery.Doctrine = held ? FireDoctrine.Hold : FireDoctrine.Free;
                game.SelectTargetAt(game.Camera.UnprojectPosition((crossing.Position - game.RenderOrigin).ToVector3()));
                Check(game.SelectedSmallContact?.Target == crossing, id + ": crossing missile selected");
                pulses = Pulses();
                if (held) Input.ActionPress(InputSetup.Fire);
                for (int i = 0; i < 240 && crossing.Health > 0 && game.World.Missiles.Contains(crossing); i++) Tick(game);
                if (held) { Input.ActionRelease(InputSetup.Fire); Mouse(game, MouseButton.Left, false); }
                Check(Pulses() > pulses && (crossing.Health <= 0 || !game.World.Missiles.Contains(crossing)),
                    $"{id}: {(held ? "held trigger" : "automatic doctrine")} destroys a crossing missile (health {crossing.Health:0.#}, pulses {Pulses() - pulses})");
            }
            game.Free();
        }
    }
}
