using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>--model-test: imported meshes, axes, module sockets, faction materials and live damaged-engine FX.</summary>
public partial class ShipModelChecks : Node
{
    private int _checks;
    private void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); _checks++; }

    public override void _Ready()
    {
        try { Run(); GD.Print($"PASS: {_checks} ship model checks"); GetTree().Quit(); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private static IEnumerable<Node3D> Nodes(Node node)
    {
        if (node is Node3D n) yield return n;
        foreach (Node child in node.GetChildren()) foreach (var nested in Nodes(child)) yield return nested;
    }

    private void Run()
    {
        foreach (HullKind kind in Enum.GetValues<HullKind>())
        {
            ShipDefinition def = ShipDefinitions.For(kind);
            var colors = new List<Color>();
            foreach (Faction faction in new[] { Faction.Blue, Faction.Red })
            {
                ShipModel model = ShipModels.Build(def.Flight, faction, 47);
                AddChild(model.Root);
                try
                {
                    Node3D? hull = model.Root.GetNodeOrNull<Node3D>("BlenderHull");
                    Check(hull is not null, $"{kind}: imported asset must load without procedural fallback");
                    var meshes = ImportedShipModels.Meshes(hull!).ToArray();
                    Check(meshes.Length == 1 + 2 * (def.Railgun?.Mounts?.Length ?? 0), $"{kind}: hull and moving groups must be merged separately");
                    MeshInstance3D mesh = meshes.Single(m => m.Name == "FleetHull");
                    Aabb bounds = mesh.GetAabb();
                    Check(bounds.Size.Z > def.Flight.Length * .85f && bounds.Size.Z < def.Flight.Length * 1.15f,
                        $"{kind}: meter scale / longitudinal axis mismatch: {bounds}");
                    Check(bounds.Size.Z > bounds.Size.X && bounds.Size.Z > bounds.Size.Y,
                        $"{kind}: long axis must be Z after Blender import");
                    Check(mesh.Mesh.GetSurfaceCount() <= 12, $"{kind}: excessive draw surfaces");
                    Check(mesh.Transform.IsEqualApprox(Transform3D.Identity), $"{kind}: mesh transforms should be baked");
                    var nodes = Nodes(hull!).ToDictionary(n => n.Name.ToString());
                    Check(model.Turrets.Count == (def.Railgun?.Mounts?.Length ?? 0), $"{kind}: every turret is bound");
                    foreach (var rig in model.Turrets)
                    {
                        var mount = def.Railgun!.Mounts!.Single(m => m.ModuleId == rig.ModuleId);
                        Check(rig.Yaw.Position.DistanceTo(mount.Pivot) < .001f, $"{kind}/{rig.ModuleId}: yaw pivot");
                        Check(rig.Elevation.GetParent() == rig.Yaw && rig.Recoil.GetParent() == rig.Elevation,
                            $"{kind}/{rig.ModuleId}: independent yaw/elevation/recoil hierarchy");
                        for (int i = 0; i < rig.Muzzles.Count; i++)
                            Check(rig.Muzzles[i].GlobalPosition.DistanceTo(mount.Muzzle(0, 0, i)) < .002f,
                                $"{kind}/{rig.ModuleId}: neutral muzzle {i} matches simulation");
                    }
                    foreach (ModuleDefinition module in def.Modules)
                    {
                        string key = "module_" + module.Id.Replace('-', '_');
                        Check(nodes.TryGetValue(key, out Node3D? socket) && socket.Position.DistanceTo(module.Center) < .001f,
                            $"{kind}: missing or transformed damage-module socket {key}");
                    }
                    Check(nodes["socket_railgun_muzzle"].Position.DistanceTo(def.Railgun!.Muzzle) < .001f,
                        $"{kind}: railgun muzzle differs from simulation");
                    Check(nodes["socket_missile_launch"].Position.DistanceTo(def.Missiles!.LaunchPoint) < .001f,
                        $"{kind}: missile socket differs from simulation");
                    if (def.Antimatter is { } am)
                        Check(nodes["socket_antimatter_launch"].Position.DistanceTo(am.Flight.LaunchPoint)<.001f,
                            "AM pod launch socket matches simulation");
                    for (int i = 0; i < def.PointDefense!.Mounts.Length; i++)
                        Check(nodes[$"socket_point_defense_{i}"].Position.DistanceTo(def.PointDefense.Mounts[i]) < .001f,
                            $"{kind}: point defense socket {i}");
                    int engineCount = def.Modules.Count(m => m.VisualEngineIndex >= 0);
                    Check(model.Plumes.Count == engineCount, $"{kind}: propulsion damage-to-visual engine mapping");
                    for (int i = 0; i < engineCount; i++)
                    {
                        Check(model.Plumes[i].Position.DistanceTo(nodes[$"fx_engine_{i}"].Position) < .001f,
                            $"{kind}: plume {i} at nozzle");
                        Check(model.Plumes[i].Basis.Y.IsEqualApprox(Vector3.Back), $"{kind}: main exhaust points aft");
                    }
                    Check(model.RcsJets.Count == 14 && model.RcsJets.All(j => j.Position.IsFinite() && j.Exhaust.IsNormalized()
                        && j.Pivot.Basis.Y.IsEqualApprox(j.Exhaust)), $"{kind}: RCS axes must follow exhaust sockets");
                    int iff = Enumerable.Range(0, mesh.Mesh.GetSurfaceCount()).Single(i => mesh.Mesh.SurfaceGetMaterial(i).ResourceName == "IFF");
                    colors.Add(((StandardMaterial3D)mesh.GetSurfaceOverrideMaterial(iff)).AlbedoColor);
                    int glow = Enumerable.Range(0, mesh.Mesh.GetSurfaceCount()).Single(i => mesh.Mesh.SurfaceGetMaterial(i).ResourceName == "EngineGlow");
                    Check(mesh.GetSurfaceOverrideMaterial(glow) == model.Palette.Glow, $"{kind}: engine glow follows faction palette");
                }
                finally { model.Root.Free(); }
            }
            Check(colors[0] != colors[1], $"{kind}: faction markings remain distinguishable");
            CheckLiveFx(def);
            CheckLiveTurrets(def);
        }
    }

    private void CheckLiveTurrets(ShipDefinition def)
    {
        if (def.Railgun?.Mounts is null) return;
        var world = new SimWorld();
        var body = world.Add(new ShipBody("TURRET-CHECK", def.Flight, Faction.Blue));
        var origin = new Vec3d(1e9, -2e9, 3e9);
        body.Place(origin, new Quaternion(Vector3.Forward, .37f));
        body.Control = new ShipControl { FlightAssist = false };
        var view = ShipView.Create(body, 47); AddChild(view);
        try
        {
            var nodes = Nodes(view).Where(n => n.Name.ToString().StartsWith("muzzle_gun_")).ToDictionary(n => n.Name.ToString());
            var localAim = new Vector3(1, .035f, -.4f).Normalized();
            var aim = body.Orientation * localAim;
            for (int tick = 0; tick < 360; tick++)
            {
                foreach (var gun in body.Railguns) gun.Aim(aim);
                world.Step(); view.Sync(origin, .5, (float)SimWorld.TickDelta);
                if (tick % 60 != 0) continue;
                foreach (var gun in body.Railguns)
                {
                    var mount = gun.Mount!;
                    float yaw = Mathf.Lerp(gun.PreviousYaw, gun.Yaw, .5f), pitch = Mathf.Lerp(gun.PreviousElevation, gun.Elevation, .5f);
                    for (int i = 0; i < mount.Muzzles.Length; i++)
                    {
                        var socket = nodes[$"muzzle_{mount.ModuleId.Replace('-', '_')}_{i}"];
                        Check(socket.GlobalPosition.DistanceTo(body.Orientation * mount.Muzzle(yaw, pitch, i)) < .003f,
                            $"{def.Kind}/{mount.ModuleId}: moving render muzzle agrees with interpolated simulation");
                        Check((-socket.GlobalBasis.Z).Dot(body.Orientation * (mount.AimBasis(yaw, pitch) * Vector3.Forward)) > .99999f,
                            $"{def.Kind}/{mount.ModuleId}: animated barrel direction including ventral roll");
                    }
                }
            }
            var attempt = world.FireRailguns(body, aim);
            Check(attempt.Shots == body.Railguns.Length, $"{def.Kind}: visual broadside can fire every mount");
            view.Sync(origin, 1, .016f);
            var flashes = Nodes(view).Where(n => n.Name == "MuzzleFlash").ToArray();
            Check(flashes.Count(n => n.Visible) == body.Railguns.Length, $"{def.Kind}: flash only at each fired barrel");
            for (int i = 0; i < 3; i++) world.Step();
            view.Sync(origin, 1, .05f);
            Check(Nodes(view).Where(n => n.Name.ToString().StartsWith("recoil_gun_")).All(n => n.Position.Z > 0),
                $"{def.Kind}: firing recoils the barrel groups");
            for (int i = 0; i < 60; i++) world.Step();
            view.Sync(origin, 1, 1);
            Check(flashes.All(n => !n.Visible), $"{def.Kind}: muzzle flashes expire");
            Check(Nodes(view).Where(n => n.Name.ToString().StartsWith("recoil_gun_")).All(n => n.Position.IsZeroApprox()),
                $"{def.Kind}: barrels return to battery");
        }
        finally { view.Free(); }
    }

    private void CheckLiveFx(ShipDefinition def)
    {
        var body = new ShipBody("MODEL-CHECK", def.Flight, Faction.Blue);
        body.Control = new ShipControl { Thrust = new Vector3(1, 0, 1) };
        ShipView view = ShipView.Create(body, 47);
        AddChild(view);
        try
        {
            for (int i = 0; i < 60; i++) { body.Step(SimWorld.TickDelta); view.Sync(Vec3d.Zero, 1, (float)SimWorld.TickDelta); }
            var plumes = view.GetNode<Node3D>("Model").GetChildren().OfType<Node3D>()
                .Where(n => n.GetChildren().OfType<MeshInstance3D>().Any(m => m.MaterialOverride == view.Palette.Plume)).ToArray();
            Check(plumes.Length == def.Modules.Count(m => m.VisualEngineIndex >= 0), $"{def.Kind}: ShipView contains every engine plume");
            Check(plumes.All(n => n.Visible), $"{def.Kind}: forward thrust lights every healthy main engine");
            Check(Nodes(view).Any(n => n.Visible && n.GetChildren().OfType<MeshInstance3D>()
                .Any(m => m.MaterialOverride == view.Palette.RcsPlume)), $"{def.Kind}: lateral input lights RCS jets");
            ModuleState failed = body.Damage.Modules.First(m => m.Definition.VisualEngineIndex == 0);
            failed.Health = 0;
            body.Step(SimWorld.TickDelta); view.Sync(Vec3d.Zero, 1, .25f);
            Check(!plumes[0].Visible && plumes.Skip(1).All(n => n.Visible), $"{def.Kind}: only the destroyed engine plume stops");
        }
        finally { view.Free(); }
    }
}
