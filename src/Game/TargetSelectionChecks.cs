using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>Rendered selection survives weapon changes, missing solutions, zoom, edge projection and stale observations.</summary>
public partial class TargetSelectionChecks : Node
{
    private int _checks;
    private void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); _checks++; }
    private async Task Frame(Hud hud)
    {
        hud.QueueRedraw();
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        hud.QueueRedraw();
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }
    private void Shot(string name)
    {
        if (!BattleArgs.Parse(OS.GetCmdlineUserArgs()).TryGetValue("selection-shots", out string? folder)) return;
        Directory.CreateDirectory(folder);
        Check(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder, name + ".png")) == Error.Ok, "Screenshot saved");
    }
    public override async void _Ready()
    {
        try
        {
            InputSetup.Register();
            foreach (string role in new[] { "BB-01", "IC-21" })
            {
                var game = new ScaleTest { BattleMode = true, LaunchControl = role };
                AddChild(game); game.SetProcess(false); game.SetPhysicsProcess(false);
                var me = game.Controlled!.Body;
                int i = 0;
                foreach (var body in game.World.Ships)
                {
                    game.World.DetachBrain(body);
                    body.Place(new Vec3d(1e9, ++i * 10000, 0), Quaternion.Identity);
                    body.Velocity = Vector3.Zero;
                }
                me.Place(Vec3d.Zero, Quaternion.Identity);
                game.Camera.ResetAim(me.Orientation); game._Process(1);
                for (int f = 0; f < 120; f++) game.Camera.Follow(me.Definition, Vector3.Zero, me.Orientation, 1f / 60);
                var target = game.Views.First(v => v.Body.Callsign == "BB-X1");
                var hud = game.GetNode<Hud>("HudLayer/Hud");
                int rounds = me.Railguns.Sum(g => g.Rounds), missiles = me.Ordnance.Missiles, torpedoes = me.Ordnance.Antimatter.Rounds;
                foreach (bool scope in new[] { false, true })
                {
                    game.Camera.SetTelescope(scope); game._Process(1);
                    foreach (TrackLevel level in new[] { TrackLevel.Contact, TrackLevel.Identified, TrackLevel.Locked })
                    {
                        game.SelectionHudFixture(target, level, game.Camera.Position + game.Camera.AimForward * 6000 + game.Camera.Basis.X * 180);
                        foreach (PlayerWeapon weapon in Enum.GetValues<PlayerWeapon>())
                        {
                            if (weapon == PlayerWeapon.Antimatter && me.Definition.Antimatter is null) continue;
                            game.SelectWeapon(weapon); await Frame(hud);
                            Check(game.Gunnery?.Solution is null && game.FiringSolution is null, "Fixture has no lead marker or valid firing solution");
                            Check(hud.DrawnSelection?.Key == target && !hud.SelectionOffscreen,
                                $"{role}/{scope}/{level}/{weapon}: selection remains visible without a firing lock");
                            Check(hud.DrawnSelection!.Level == level && hud.DrawnSelection.Name == game.ContactOf(target)!.Name,
                                "Display uses observed identity and quality, independent of the selected weapon");
                            Vector2 expected = game.Camera.UnprojectPosition((game.SelectedTargetDisplay()!.Position - game.RenderOrigin).ToVector3());
                            Check(hud.SelectionMarkerPosition.DistanceTo(expected) < 1, "Selection marker stays on the observed contact");
                            if (level == TrackLevel.Contact && weapon == PlayerWeapon.Missile) Shot(role + (scope ? "-scope-missile" : "-missile"));
                        }
                    }
                }
                game.SelectWeapon(PlayerWeapon.MainGun);
                game.SelectionHudFixture(target, TrackLevel.Locked, game.Camera.Position + game.Camera.AimForward * 6000);
                target.Body.Velocity = game.Camera.Basis.X * 700;
                game.World.Sensors.Update(game.World.Ships, game.World.Time, true);
                game.RefreshScopeHudFixture(assist: false);
                await Frame(hud);
                var expectedLead = FireControl.Solve(me, target.Body, game.World.Time, track: game.TrackOf(target), localAim: game.AimModule?.Definition.Center);
                Check(hud.DrawnLeadSolution is { Valid: true } lead && lead.AimPoint == expectedLead.AimPoint,
                    "Manual scope renders the shared sensor-based lead even with pilot assistance off");
                Check(game.Gunnery?.Solution is null && !game.CorrectingAim,
                    "A displayed scoped lead does not enable automatic firing or steering");
                Shot(role + "-scope-lead");
                game.World.Sensors.Update(new[] { target.Body }, game.World.Time, true);
                game.RefreshScopeHudFixture();
                await Frame(hud);
                Check(hud.DrawnLeadSolution is null, "Loss of firing lock removes the scoped intercept marker");
                target.Body.Velocity = Vector3.Zero;
                game.SelectionHudFixture(target, TrackLevel.Identified, game.Camera.Position + game.Camera.AimForward * 700);
                await Frame(hud);
                Check(hud.DrawnSelection?.Key == target && !hud.SelectionOffscreen && hud.BracketPositions.Any(p => p.View == target),
                    "Huge nearby scoped hull keeps selection and bracket picking point");
                Shot(role + "-scope-close");

                game.SelectionHudFixture(target, TrackLevel.Contact, game.Camera.Position - game.Camera.AimForward * 6000 + game.Camera.Basis.X * 800);
                await Frame(hud);
                Check(hud.DrawnSelection?.Key == target && hud.SelectionOffscreen && GetViewport().GetVisibleRect().HasPoint(hud.SelectionMarkerPosition),
                    "A rear target retains an on-screen directional selection marker");
                Shot(role + "-rear");

                // A real detected hostile missile uses the same selection treatment, including in scope.
                game.SelectionHudFixture(target, TrackLevel.Locked, game.Camera.Position + game.Camera.AimForward * 6000);
                target.Body.Ordnance.Reset();
                Check(game.World.LaunchMissile(target.Body, me).Fired, "Small-contact fixture launches a real hostile missile");
                var incoming = game.World.Missiles.Last();
                foreach (bool scope in new[] { false, true })
                {
                    game.Camera.SetTelescope(scope); game._Process(1);
                    incoming.Position = game.RenderOrigin + Vec3d.From(game.Camera.Position + game.Camera.AimForward * 1500 + game.Camera.Basis.X * 120);
                    incoming.PrevPosition = incoming.Position; incoming.Velocity = Vector3.Zero;
                    game.World.Sensors.Update(game.World.Ships, game.World.Time, true, game.World.Probes, game.World.Missiles);
                    game.SelectTargetAt(game.Camera.UnprojectPosition((incoming.Position - game.RenderOrigin).ToVector3()));
                    game.SelectWeapon(PlayerWeapon.Missile);
                    await Frame(hud);
                    Check(game.SelectedSmallContact?.Target == incoming && hud.DrawnSelection?.Key == incoming && !hud.SelectionOffscreen,
                        "Selected enemy missile keeps its marker and identity when missiles/zoom are selected");
                    if (scope) Shot(role + "-scope-intercept");
                }

                game.SelectionHudFixture(target, TrackLevel.Identified, game.Camera.Position + game.Camera.AimForward * 6000);
                Vec3d observed = game.SelectedTargetDisplay()!.Position;
                game.LoseSelectionHudFixture(target);
                await Frame(hud);
                Check(hud.DrawnSelection is { Lost: true, Destroyed: false } && hud.DrawnSelection.Position == observed,
                    "Signal loss retains last observation and never reveals hidden movement or destruction");
                Shot(role + "-lost");
                Check(me.Railguns.Sum(g => g.Rounds) == rounds && me.Ordnance.Missiles == missiles && me.Ordnance.Antimatter.Rounds == torpedoes,
                    "Selection, weapon switches, zoom and HUD rendering consume no ammunition");

                game.Camera.SetTelescope(false); game._Process(1);
                var squadmate = game.OwnedSquadron!.Members.First(s => s != me);
                var ally = game.World.Ships.First(s => s.Faction == me.Faction && !game.OwnsShip(s));
                Vec3d nearby = game.RenderOrigin + Vec3d.From(game.Camera.Position + game.Camera.AimForward * 6000);
                squadmate.Place(nearby, Quaternion.Identity); ally.Place(nearby, Quaternion.Identity);
                game.World.Sensors.Update(game.World.Ships, game.World.Time, true);
                game.RefreshScopeHudFixture(); game._Process(0);
                await Frame(hud);
                Check(hud.DrawnOwnedRadarGroups == 1 && hud.DrawnAlliedRadarGroups == 1,
                    "Overlapping owned and allied ships keep separate green/blue radar groups");
                squadmate.Teleport(nearby - Vec3d.From(game.Camera.Basis.X * 1400));
                ally.Teleport(nearby + Vec3d.From(game.Camera.Basis.X * 1400));
                game.World.Sensors.Update(game.World.Ships, game.World.Time, true);
                game.RefreshScopeHudFixture(); game._Process(0);
                await Frame(hud);
                Shot(role + "-squad-colors");
                game.ShowSquadColorMapFixture(me.Position, squadmate.Position, ally.Position);
                await Frame(hud);
                Shot(role + "-map-colors");
                game.Camera.ResetTelescope(); game.Free();
            }
            GD.Print($"PASS: {_checks} selected-target HUD checks"); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}

public partial class ScaleTest
{
    internal void RefreshScopeHudFixture(bool assist = true)
    {
        FireAssist = assist;
        UpdateContacts();
        if (Gunnery is { Doctrine: FireDoctrine.Manual } order) order.Status = GunneryStatus.Manual;
        StepCombat();
    }
    internal void ShowSquadColorMapFixture(params Vec3d[] positions)
    {
        ToggleWorldMap();
        _worldMap.Map.Fit(positions);
        _worldMap.QueueRedraw();
    }
    // Explicit sensor-quality fixtures exercise presentation without granting firing permission.
    internal void SelectionHudFixture(ShipView target, TrackLevel level, Vector3 render)
    {
        target.Body.Place(RenderOrigin + Vec3d.From(render), Quaternion.Identity);
        target.Body.Damage.Reset();
        World.Sensors.Update(World.Ships, World.Time, true);
        var track = World.Sensors.Track(Controlled!.Body.Faction, target.Body) with { Level = level };
        _contacts.Observe(Controlled.Body.Faction, target.Body, track, World.Time);
        SelectEnemy(target);
        if (Gunnery is { } order) { order.Solution = null; order.Engaged = null; }
        FiringSolution = null;
        target.Sync(RenderOrigin, 1, 0);
    }
    internal void LoseSelectionHudFixture(ShipView target)
    {
        target.Body.Teleport(new Vec3d(1e10, 0, 0));
        target.Body.Damage.Breakup(World.Time, "hidden test damage");
        _contacts.Observe(Controlled!.Body.Faction, target.Body, SensorTrack.Unknown, World.Time);
        target.Sync(RenderOrigin, 1, 0);
    }
}
