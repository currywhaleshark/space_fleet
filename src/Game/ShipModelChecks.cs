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
        foreach (ShipDefinition def in FleetLoadouts.Hulls.GroupBy(d=>d.ModelId).Select(g=>g.First()))
        {
            HullKind kind = def.Kind;
            var colors = new List<Color>();
            foreach (Faction faction in new[] { Faction.Blue, Faction.Red })
            {
                ShipModel model = ShipModels.Build(def, faction, 47);
                AddChild(model.Root);
                try
                {
                    Node3D? hull = model.Root.GetNodeOrNull<Node3D>("BlenderHull");
                    Check(hull is not null, $"{kind}: imported asset must load without procedural fallback");
                    var meshes = ImportedShipModels.Meshes(hull!).ToArray();
                    Check(meshes.Length == 1 + (def.Battery?.Weapon.Laser is null?2:3) * (def.Railgun?.Mounts?.Length ?? 0) + 2 * (def.PointDefense?.Mounts.Length ?? 0),
                        $"{kind}: hull, main and PD moving groups must be merged separately");
                    MeshInstance3D mesh = meshes.Single(m => m.Name == "FleetHull");
                    Aabb bounds = mesh.GetAabb();
                    Check(bounds.Size.Z > def.Flight.Length * .85f && bounds.Size.Z < def.Flight.Length * 1.15f,
                        $"{kind}: meter scale / longitudinal axis mismatch: {bounds}");
                    // The length check above pins Z; a traced transverse wing (Polaris) may span wider than the hull is long.
                    Check(bounds.Size.Z > bounds.Size.Y,
                        $"{kind} {def.Id}: long axis must be Z after Blender import: {bounds}");
                    Check(mesh.Mesh.GetSurfaceCount() <= 12, $"{kind}: excessive draw surfaces");
                    Check(mesh.Transform.IsEqualApprox(Transform3D.Identity), $"{kind}: mesh transforms should be baked");
                    var nodes = Nodes(hull!).Where(n=>n.Name!="ReplacementLaserEmitter"&&n.Name!="LaserOptic").ToDictionary(n => n.Name.ToString());
                    Check(model.Turrets.Count == (def.Railgun?.Mounts?.Length ?? 0), $"{kind}: every turret is bound");
                    foreach (var rig in model.Turrets)
                    {
                        var mount = def.Railgun!.Mounts!.Single(m => m.ModuleId == rig.ModuleId);
                        Check(rig.Yaw.Position.DistanceTo(mount.Pivot) < .001f, $"{kind}/{rig.ModuleId}: yaw pivot");
                        Check(rig.Elevation.GetParent() == rig.Yaw && rig.Recoil.GetParent() == rig.Elevation,
                            $"{kind}/{rig.ModuleId}: independent yaw/elevation/recoil hierarchy");
                        var housing = rig.Yaw.GetChildren().OfType<MeshInstance3D>().Single();
                        var barrels = rig.Recoil.GetChildren().OfType<MeshInstance3D>().Single(n=>n.Name!="LaserOptic");
                        Check(housing.Transform.IsEqualApprox(Transform3D.Identity) && !housing.TopLevel
                            && barrels.Transform.IsEqualApprox(Transform3D.Identity) && !barrels.TopLevel,
                            $"{kind}/{rig.ModuleId}: visible meshes inherit the turret rig transforms");
                        for (int i = 0; i < rig.Muzzles.Count; i++)
                            Check(rig.Muzzles[i].GlobalPosition.DistanceTo(mount.Muzzle(0, 0, i)) < .002f,
                                $"{kind}/{rig.ModuleId}: neutral muzzle {i} matches simulation");
                    }
                    Check(model.PointDefense.Count == def.PointDefense!.Mounts.Length, $"{kind}: all PD mounts are rigged");
                    foreach(var rig in model.PointDefense)
                    {
                        Check(rig.Yaw.Position.DistanceTo(def.PointDefense.Mounts[rig.Index])<.001f
                            && rig.Elevation.GetParent()==rig.Yaw, $"{kind} {def.Id}/PD{rig.Index}: horizontal and vertical pivot hierarchy: {rig.Yaw.Position} vs {def.PointDefense.Mounts[rig.Index]}");
                        Check(rig.Yaw.GetChildren().OfType<MeshInstance3D>().Count()==1
                            && rig.Elevation.GetChildren().OfType<MeshInstance3D>().Count()==1,
                            $"{kind}/PD{rig.Index}: receiver and barrels are actual moving meshes");
                    }
                    foreach (ModuleDefinition module in def.Modules)
                    {
                        string key = "module_" + module.Id.Replace('-', '_');
                        Check(nodes.TryGetValue(key, out Node3D? socket) && socket.Position.DistanceTo(module.Center) < .001f,
                            $"{kind}: missing or transformed damage-module socket {key}");
                    }
                    Check(nodes["socket_railgun_muzzle"].Position.DistanceTo(def.DefaultMainGun is null?def.Railgun!.Muzzle:CruiserModelSource(def).Railgun!.Muzzle) < .001f,
                        $"{kind}: railgun muzzle differs from simulation");
                    Check(nodes["socket_missile_launch"].Position.DistanceTo(def.Missiles!.LaunchPoint) < .001f,
                        $"{kind}: missile socket differs from simulation");
                    if (def.Antimatter is { } am)
                        Check(nodes["socket_antimatter_launch"].Position.DistanceTo(am.Flight.LaunchPoint)<.001f,
                            "AM pod launch socket matches simulation");
                    if(def.ReconProbes?.LaunchPoints is { } ports)
                        for(int i=0;i<ports.Length;i++)
                            Check(nodes[$"socket_probe_{i}"].Position.DistanceTo(ports[i])<.001f,"Authored probe exits match simulation");
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
            CheckAutomaticElevation(def);
            CheckPointDefense(def);
        }
        CheckDroneCombatVisuals(0);
        CheckDroneCombatVisuals(1);
        CheckLoadoutModels();
        CheckCruiserModels();
        CheckReplacementModels();
        CheckPartModels();
    }

    /// <summary>Modular main gun parts: procedural barrels only on changed sockets, at the data muzzles, sized by caliber.</summary>
    private void CheckPartModels()
    {
        (string Hull,string?[] Guns)[] fits={("battleship",new[]{"earth-rail-l3",null,"earth-rail-s2"}),("escort",new[]{"earth-rail-m1-hp","earth-rail-s2"}),
            ("mars_escort",new[]{"mars-rail-m1-long",null}),("earth_air_defense_cruiser",new[]{"earth-rail-s2","earth-rail-s2"}),
            ("battleship",new[]{"earth-laser-l","earth-laser-l","earth-laser-l"})};
        foreach(var (hull,guns) in fits)
        {
            var def=ShipLoadouts.Resolve(ShipVariants.Loadout(hull) with{MainGuns=guns}).Definition;
            var stock=MainGunParts.StockFor(def);
            var model=ShipModels.Build(def,Faction.Blue,47);AddChild(model.Root);
            try
            {
                var heights=new Dictionary<string,float>();
                Check(model.Turrets.Count==def.PrimaryMounts.Length,$"{hull}: part fit keeps every hardpoint");
                foreach(var binding in def.PrimaryMounts)
                {
                    var mount=binding.Turret!;var rig=model.Turrets.Single(r=>r.ModuleId==mount.ModuleId);
                    Check(rig.Muzzles.Count==mount.Muzzles.Length,$"{hull}/{mount.ModuleId}: rig binds the part's barrel count");
                    for(int i=0;i<rig.Muzzles.Count;i++)
                        Check(rig.Muzzles[i].GlobalPosition.DistanceTo(mount.Muzzle(0,0,i))<.002f,$"{hull}/{mount.ModuleId}: render muzzle is the projectile exit");
                    var meshes=rig.Recoil.GetChildren().OfType<MeshInstance3D>().ToArray();
                    if(binding.Weapon.Laser is not null)
                        Check(meshes.Any(m=>m.Name=="ReplacementLaserEmitter"),$"{hull}: laser part shows an emitter");
                    else if(binding.Weapon.Id==stock.Id)
                        Check(meshes.All(m=>m.Name!="PartPrimaryBarrels"),$"{hull}/{mount.ModuleId}: stock socket keeps its imported barrels");
                    else
                    {
                        var barrels=meshes.Single(m=>m.Name=="PartPrimaryBarrels");
                        Check(meshes.Length==1,$"{hull}/{mount.ModuleId}: original barrels replaced without duplicates");
                        Check(barrels.Mesh.GetSurfaceCount()==(def.Design==DesignFamily.Mars?3:2),$"{hull}: faction style (Mars glowing heat sinks)");
                        heights[binding.Weapon.Id]=barrels.Mesh.GetAabb().Size.Y/(-mount.Muzzles.Average(m=>m.Z));
                    }
                }
                if(heights.TryGetValue("earth-rail-l3",out float big)&&heights.TryGetValue("earth-rail-s2",out float small))
                    Check(big>small*1.3f,$"Caliber reads in barrel thickness: 240 mm {big:0.000} vs 100 mm {small:0.000}");
            }
            finally{model.Root.Free();}
            CheckLiveTurrets(def);
            CheckAutomaticElevation(def);
            // Effects follow caliber and overcharge; stock guns stay exactly 1.
            var body=new ShipBody("FX",def,Faction.Blue);
            foreach(var g in body.Railguns)
                Check(g.Weapon.Id==stock.Id&&g.SupplyRatio==1?WeaponLooks.Scale(g)==1:WeaponLooks.Scale(g)>0,$"{hull}: effect scale defined");
        }
        var stockBody=new ShipBody("STOCKFX",ShipLoadouts.Resolve(new("battleship")).Definition,Faction.Blue);
        Check(stockBody.Railguns.All(g=>WeaponLooks.Scale(g)==1),"Stock effects unchanged");
        var mix=new ShipBody("MIXFX",ShipLoadouts.Resolve(new ShipLoadout("battleship"){MainGuns=new[]{"earth-rail-l3",null,"earth-rail-s2"}}).Definition,Faction.Blue);
        Check(WeaponLooks.Scale(mix.Railguns[0])>1&&WeaponLooks.Scale(mix.Railguns[2])<.6f,"240 mm flashes larger than 100 mm on the same hull");
        CheckKitModels();
    }

    /// <summary>Phase 6 weapon kit: Blender pieces load; missile cells sit where each cell fires; fixed axial guns get a receiver.</summary>
    private void CheckKitModels()
    {
        Check(WeaponKit.Available,"Blender weapon kit loads from "+WeaponKit.Path);
        foreach(var (hull,launcher,cells) in new[]{("mars_missile_cruiser_t4","launcher-vls8",8),("battleship","launcher-quad",4),("escort","launcher-twin",2)})
        {
            var def=ShipLoadouts.Resolve(ShipVariants.Loadout(hull) with{Components=new ShipComponentSelection{Launcher=launcher}}).Definition;
            var model=ShipModels.Build(def,Faction.Blue,47);AddChild(model.Root);
            try
            {
                var row=model.Root.FindChild("MissileCells",true,false) as Node3D;
                Check(row is not null&&row.GetChildren().OfType<MeshInstance3D>().Count()==1,$"{hull}: one merged cell hatch mesh");
                var markers=row!.GetChildren().OfType<Node3D>().Where(n=>n.Name.ToString().StartsWith("cell_")).ToArray();
                Check(markers.Length==cells,$"{hull}: {cells} hatches for {cells} cells");
                // Each hatch is where OrdnanceState launches that cell's missile.
                var body=new ShipBody("CELLS",def,Faction.Blue);var expected=new List<Vector3>();
                for(int i=0;i<cells;i++){expected.Add(def.Missiles!.LaunchPoint+body.Ordnance.CellOffset);body.Ordnance.ConsumeMissile();}
                Check(markers.All(m=>expected.Any(e=>e.DistanceTo(m.Position)<.01f))&&expected.All(e=>markers.Any(m=>e.DistanceTo(m.Position)<.01f)),$"{hull}: hatches match the launch positions");
            }
            finally{model.Root.Free();}
        }
        var stock=ShipModels.Build(ShipLoadouts.Resolve(ShipVariants.Loadout("mars_missile_cruiser_t4")).Definition,Faction.Blue,47);AddChild(stock.Root);
        try{Check(stock.Root.FindChild("MissileCells",true,false) is null,"Single tube keeps the stock model");}finally{stock.Root.Free();}
        var axial=ResearchTree.Catalog.Resolve("interceptor_t3",new[]{"fire-control"},Array.Empty<EquipmentMount>(),new ShipComponentSelection{MainGuns=new[]{"earth-rail-m1-axial"}}).Definition;
        var small=ShipModels.Build(axial,Faction.Blue,47);AddChild(small.Root);
        try{Check(small.Root.FindChild("PartFixedReceiver",true,false) is MeshInstance3D,"Medium axial gun gets a receiver pod");}finally{small.Root.Free();}
        var plain=ShipModels.Build(ShipLoadouts.Resolve(ShipVariants.Loadout("interceptor_t3")).Definition,Faction.Blue,47);AddChild(plain.Root);
        try{Check(plain.Root.FindChild("PartFixedReceiver",true,false) is null,"Stock axial gun keeps the stock model");}finally{plain.Root.Free();}
    }

    private static ShipDefinition CruiserModelSource(ShipDefinition def)=>ShipVariants.Resolve(def.Id,new[]{"{\"defaultMainGun\":null}"}).Definition;

    private void CheckReplacementModels()
    {
        foreach(var original in FleetLoadouts.Hulls.DistinctBy(d=>d.ModelId))
        {
            foreach(var part in ShipComponents.All.Where(p=>p.Slot==ComponentSlot.MainGun&&ShipComponents.Available(original,p)))
            {
                var def=FleetLoadouts.ResolveFit(FleetLoadouts.Empty(original.Id) with{Components=new(MainGun:part.Id)}).Definition;
                var model=ShipModels.Build(def,Faction.Blue,47);AddChild(model.Root);
                try
                {
                    if(def.Railgun!.Mounts is { } mounts)
                    {
                        Check(model.Turrets.Count==mounts.Length,"Replacement preserves every hull hardpoint");
                        foreach(var rig in model.Turrets)
                        {
                            var mount=mounts.Single(m=>m.ModuleId==rig.ModuleId);
                            Check(rig.Muzzles.Count==mount.Muzzles.Length,"Replacement rig binds the selected barrel count");
                            Check(def.Battery!.Weapon.Laser is not null ? rig.Recoil.GetChildren().OfType<MeshInstance3D>().Count()==2 && rig.Recoil.FindChild("ReplacementLaserEmitter",false,false) is MeshInstance3D : rig.Recoil.GetChildren().OfType<MeshInstance3D>().Single().Name=="ReplacementPrimaryBarrels",
                                "Original barrels are replaced, without duplicate floating guns");
                            for(int i=0;i<rig.Muzzles.Count;i++)
                                Check(rig.Muzzles[i].GlobalPosition.DistanceTo(mount.Muzzle(0,0,i))<.002f,
                                    "Replacement mesh muzzle uses the projectile exit position");
                        }
                    }
                    else Check(model.Root.FindChild("ReplacementFixedReceiver",true,false) is Node3D,
                        "Fixed primary uses an acceleration pack while preserving its original muzzle");
                }
                finally{model.Root.Free();}
                CheckLiveTurrets(def);
            }
            var systems=new ShipComponentSelection(Secondary:"pd-longrange",Engine:"engine-vector",Sensor:"sensor-lowprofile",
                Generator:"generator-output",Shield:"shield-recharge");
            var other=FleetLoadouts.ResolveFit(FleetLoadouts.Empty(original.Id) with{Components=systems}).Definition;
            var stock=ShipModels.Build(original,Faction.Blue,47);var changed=ShipModels.Build(other,Faction.Blue,47);
            try
            {
                string[] Shape(ShipModel m)=>Nodes(m.Root).OfType<MeshInstance3D>()
                    .Select(n=>$"{n.Transform}:{n.Mesh.GetAabb()}").ToArray();
                Check(Shape(stock).SequenceEqual(Shape(changed)),"Secondary, engine, sensor, generator and shield keep the original appearance");
            }
            finally{stock.Root.Free();changed.Root.Free();}
        }
    }

    private void CheckLoadoutModels()
    {
        foreach (var original in ShipDefinitions.All)
        {
            var equipment = original.Bays.Select(b => new EquipmentMount(b.Id, b.Accepts.Contains(EquipmentCategory.Drone)
                ? "drone-hangar" : b.Accepts.Contains(EquipmentCategory.Shield) ? "shield-capacity" : "sensor-array")).ToArray();
            var def = ShipLoadouts.Resolve(new(original.Id, $"{{\"id\":\"test-{original.Id}\"}}", Equipment: equipment)).Definition;
            var body = new ShipBody("LOADOUT", def, Faction.Blue);
            var view = ShipView.Create(body, 47); AddChild(view);
            try
            {
                view.Sync(Vec3d.Zero, 1, .016f);
                Check(def.Id != def.ModelId && view.FindChild("BlenderHull", true, false) is Node3D,
                    "Loadout variant resolves the existing model through modelId");
                var nodes = Nodes(view).Where(n => n.Name.ToString().StartsWith("module_", StringComparison.Ordinal))
                    .ToDictionary(n => n.Name.ToString(), n => n, StringComparer.Ordinal);
                foreach (var module in def.Modules)
                    Check(nodes["module_" + module.Id.Replace('-', '_')].Position.DistanceTo(module.Center) < .001f,
                        "Variant model sockets remain aligned with its damage modules");
                if (def.DefenseDrones is { } drones)
                    Check(view.Drones!.Multimesh.InstanceCount == drones.Count,
                        "Expanded hangar allocates a visible instance for every additional drone");
            }
            finally { view.Free(); }
        }
    }

    private void CheckCruiserModels()
    {
        foreach(var variant in ShipVariants.All.Where(v=>v.Node.Tier>=3))
        {
            var def=variant.Definition;var body=new ShipBody("CRUISER",def,Faction.Blue);
            var view=ShipView.Create(body,47);AddChild(view);
            try
            {
                view.Sync(Vec3d.Zero,1,.016f);
                Check(view.FindChild("BlenderHull",true,false) is not null,"Cruiser resolves its base model");
                foreach(var module in def.Modules)
                    Check(((Node3D)view.FindChild("module_"+module.Id.Replace('-','_'),true,false)).Position.DistanceTo(module.Center)<.001f,
                        "Cruiser module socket matches damage geometry");
                for(int i=0;i<def.PointDefense!.Mounts.Length;i++)
                {
                    var yaw=(Node3D)view.FindChild($"pd_yaw_{i}",true,false);
                    Check(yaw.Position.DistanceTo(def.PointDefense.Mounts[i])<.001f,"Cruiser PD mounting position matches simulation");
                    Check(view.FindChild($"pd_pitch_{i}",true,false) is not null && view.FindChild($"pd_muzzle_{i}_0",true,false) is not null,
                        "Every cruiser PD has moving elevation and muzzle nodes");
                }
                if(def.DefenseDrones is { } drones)Check(view.Drones!.Multimesh.InstanceCount==drones.Count,"Cruiser defense drones render");
            }
            finally{view.Free();}
            if(def.Branch==ShipBranch.AirDefenseCruiser&&variant.Node.Tier==3)CheckPointDefense(def);
        }
    }

    private void CheckDroneCombatVisuals(int mountIndex)
    {
        var world=new SimWorld();
        var carrier=world.Add(new ShipBody("DRONE-CARRIER",ShipClass.Battleship,Faction.Red));
        var interceptor=world.Add(new ShipBody("AUX-INTERCEPTOR",ShipClass.Interceptor,Faction.Blue));
        carrier.Control=interceptor.Control=new ShipControl { FlightAssist=false };
        foreach(var module in carrier.Damage.Modules.Where(m=>m.Definition.Kind==ModuleKind.Sensor))
            carrier.Damage.Hurt(module,module.Health,0,0,1);
        var drone=carrier.Ordnance.Drones;
        Vector3 direction=interceptor.Definition.PointDefense!.Normals![mountIndex].Normalized();
        interceptor.Place(drone.WorldPosition(0)-Vec3d.From(direction)*500,Quaternion.Identity);
        var carrierView=ShipView.Create(carrier,3); var interceptorView=ShipView.Create(interceptor,4);
        AddChild(carrierView); AddChild(interceptorView);
        try
        {
            var turret=interceptor.Ordnance.PointDefense[mountIndex];
            for(int i=0;i<600 && turret.ShotCount==0;i++) world.Step();
            carrierView.Sync(Vec3d.Zero,1,.016f); interceptorView.Sync(Vec3d.Zero,1,.016f);
            var barrel=(MeshInstance3D)interceptorView.FindChild($"mesh_pd_pitch_{mountIndex}",true,false);
            Check(turret.TargetKind==PointDefenseTarget.Drone && turret.ShotCount>0,"Interceptor auxiliary fires at actual deployed drones");
            Check((-barrel.GlobalBasis.Z).Dot(interceptor.Orientation*turret.LocalDirection)>.99999f,
                "Rendered auxiliary yaw and pitch match physical drone tracking");
            Check(Nodes(interceptorView).Any(n=>n.Name=="PDFlash" && n.Visible),"Actual anti-drone fire flashes the auxiliary muzzles");
            var swarm=carrierView.Drones!;
            int alive=Enumerable.Range(0,8).First(drone.Alive);
            world.DamageDrone(interceptor,carrier,alive,100,world.Time); carrierView.Sync(Vec3d.Zero,1,.016f);
            Check(swarm.Multimesh.GetInstanceTransform(alive).Basis.Determinant()==0,"Only the destroyed drone's mesh is removed");
            Check(Enumerable.Range(0,8).Where(drone.Alive).All(i=>Math.Abs(swarm.Multimesh.GetInstanceTransform(i).Basis.Determinant()-1)<.001),"Surviving drone meshes retain their transforms");
            carrier.Damage.Reset(); carrierView.Sync(Vec3d.Zero,1,.016f);
            Check(swarm.Multimesh.GetInstanceTransform(alive).Basis.Determinant()==0,"Carrier repair cannot resurrect a destroyed visual drone");
            var effects=new OrdnanceView(); AddChild(effects);
            try
            {
                effects.Sync(world,Vec3d.Zero,1,Vector3.Zero);
                Check(effects.GetChildren().OfType<MeshInstance3D>().Count(n=>n.Visible)>=6,"Drone destruction shows a brief flash and five debris fragments");
                for(int i=0;i<60;i++) world.Step();
                effects.Sync(world,Vec3d.Zero,1,Vector3.Zero);
                Check(world.OrdnanceEvents.Any(e=>e.Kind==OrdnanceEventKind.DroneDestroyed),"Destruction remains observable until the shared event prune window");
            }
            finally { effects.Free(); }
        }
        finally { carrierView.Free(); interceptorView.Free(); }
    }

    private void CheckLiveTurrets(ShipDefinition def)
    {
        if (def.Railgun?.Mounts is null) return;
        var world = new SimWorld();
        var body = world.Add(new ShipBody("TURRET-CHECK", def, Faction.Blue));
        var origin = new Vec3d(1e9, -2e9, 3e9);
        body.Place(origin, new Quaternion(Vector3.Forward, .37f));
        body.Control = new ShipControl { FlightAssist = false };
        var view = ShipView.Create(body, 47); AddChild(view);
        try
        {
            var nodes = Nodes(view).Where(n => n.Name.ToString().StartsWith("muzzle_gun_")).ToDictionary(n => n.Name.ToString());
            var localAim = new Vector3(1, .035f, -.4f).Normalized();
            var aim = body.Orientation * localAim;
            for (int tick = 0; tick < TurretSettleTicks(body); tick++)
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
            Check(attempt.Shots == body.Railguns.Sum(g=>g.BarrelCount), $"{def.Id}: visual broadside fires every installed barrel");
            view.Sync(origin, 1, .016f);
            var flashes = Nodes(view).Where(n => n.Name == "MuzzleFlash").ToArray();
            Check(flashes.Count(n => n.Visible) == (body.Railgun!.IsLaser?0:body.Railguns.Sum(g=>g.BarrelCount)), $"{def.Id}: all fired barrels flash together");
            for (int i = 0; i < 3; i++) world.Step();
            view.Sync(origin, 1, .05f);
            Check(Nodes(view).Where(n => n.Name.ToString().StartsWith("recoil_gun_")).All(n => body.Railgun!.IsLaser ? n.Position.IsZeroApprox() : n.Position.Z > 0),
                $"{def.Kind}: firing recoils the barrel groups");
            for (int i = 0; i < 60; i++) world.Step();
            view.Sync(origin, 1, 1);
            Check(flashes.All(n => !n.Visible), $"{def.Kind}: muzzle flashes expire");
            Check(Nodes(view).Where(n => n.Name.ToString().StartsWith("recoil_gun_")).All(n => n.Position.IsZeroApprox()),
                $"{def.Kind}: barrels return to battery");
        }
        finally { view.Free(); }
    }

    private void CheckAutomaticElevation(ShipDefinition def)
    {
        if(def.Railgun?.Mounts is null) return;
        var world=new SimWorld(); var body=world.Add(new ShipBody("AUTO",def,Faction.Blue));
        var enemy=world.Add(new ShipBody("TARGET",ShipClass.Battleship,Faction.Red));
        body.Control=enemy.Control=new ShipControl { FlightAssist=false };
        body.Gunnery=new GunneryOrder { Doctrine=FireDoctrine.Focus,Target=enemy };
        var view=ShipView.Create(body,47); AddChild(view);
        try
        {
            foreach(float side in new[]{1f,-1f})
            {
                enemy.Place(new Vec3d(18000,side*9000,-7000),Quaternion.Identity);
                world.Sensors.Update(world.Ships,world.Time,force:true);
                for(int i=0;i<TurretSettleTicks(body);i++) { enemy.Damage.Reset(); world.Step(); view.Sync(Vec3d.Zero,1,(float)SimWorld.TickDelta); }
                foreach(var gun in body.Railguns.Where(g=>g.Mount!.Ventral==(side<0)))
                {
                    var key=gun.Definition.ModuleId.Replace('-','_');
                    var barrel=(MeshInstance3D)(view.FindChild($"recoil_{key}",true,false).GetChildren().OfType<MeshInstance3D>().Single(n=>n.Name!="LaserOptic"));
                    Check(Math.Abs(gun.Yaw)>.5f && gun.Elevation>.2f && (gun.IsLaser?gun.Laser!.Pulses>0:gun.ShotCount>0),
                        $"{def.Kind}/{key}: automatic fire slews sideways and elevates toward {(side>0 ? "upper" : "lower")} target");
                    Check((-barrel.GlobalBasis.Z).Dot(gun.Direction)>.99999f,
                        $"{def.Kind}/{key}: visible barrel follows automatic azimuth and elevation");
                }
            }
        }
        finally { view.Free(); }
    }

    // These stationary targets require less than 90 degrees on either axis.
    // Allow the actual drive to settle; the battleship now traverses at 8 deg/s.
    private static int TurretSettleTicks(ShipBody body) => (int)Math.Ceiling(Math.Max(6,
        90.0 / body.Railguns.Min(g => Math.Min(g.Mount!.YawRate, g.Mount.ElevationRate)) + 1) * SimWorld.TickRate);

    private void CheckPointDefense(ShipDefinition def)
    {
        var world=new SimWorld(); var body=world.Add(new ShipBody("PD",def,Faction.Blue));
        var enemy=world.Add(new ShipBody("RAIDER",ShipClass.Interceptor,Faction.Red));
        Vec3d origin=new(1e9,-2e9,3e9); body.Place(origin,new Quaternion(Vector3.Forward,.37f));
        body.Control=enemy.Control=new ShipControl { FlightAssist=false };
        var view=ShipView.Create(body,47); AddChild(view);
        try
        {
            var pd=def.PointDefense!;
            for(int index=0;index<pd.Mounts.Length;index++)
            {
                Vector3 direction=(pd.Normals![index].Normalized()+Vector3.Right*.2f).Normalized();
                Vector3 offset=pd.Mounts[index]+direction*pd.RangeMeters*.45f;
                enemy.Place(origin+Vec3d.From(body.Orientation*offset),Quaternion.Identity);
                var state=body.Ordnance.PointDefense[index];
                var barrel=(MeshInstance3D)view.FindChild($"mesh_pd_pitch_{index}",true,false);
                for(int i=0;i<120;i++)
                {
                    enemy.Damage.Reset(); world.Step();
                    if(i==0)
                    {
                        view.Sync(origin,.5,(float)SimWorld.TickDelta);
                        Vector3 bore=state.MountBasis*new Basis(Vector3.Up,Mathf.Lerp(state.PreviousYaw,state.Yaw,.5f))
                            *new Basis(Vector3.Right,Mathf.Lerp(state.PreviousElevation,state.Elevation,.5f))*Vector3.Forward;
                        Check((-barrel.GlobalBasis.Z).Dot(body.Orientation*bore)>.99999f,
                            $"{def.Kind} {def.Id}/PD{index}: mesh interpolates the simulation drive during traverse");
                    }
                    view.Sync(origin,1,(float)SimWorld.TickDelta);
                }
                Check(state.LocalAim is not null && double.IsFinite(state.LastFiredAt),$"{def.Kind} {def.Id}/PD{index}: actual PD target and firing state recorded");
                Check((-barrel.GlobalBasis.Z).Dot(body.Orientation*direction)>.9999f,
                    $"{def.Kind} {def.Id}/PD{index}: visible PD barrel tracks sideways and vertically, including ventral mounts");
                Check(Math.Abs(barrel.GetParent<Node3D>().Rotation.X)>.15f,$"{def.Kind} {def.Id}/PD{index}: nonzero gun elevation");
            }
            enemy.Place(origin+new Vec3d(0,0,100000),Quaternion.Identity); world.Step(); view.Sync(origin,1,.016f);
            Check(body.Ordnance.PointDefense.All(m=>m.LocalAim is null),$"{def.Kind}: PD releases target outside range");
            world.ResetWeapons();
            Check(body.Ordnance.PointDefense.All(m=>m.LocalAim is null && double.IsNegativeInfinity(m.LastFiredAt)),
                $"{def.Kind}: resetting weapons clears PD flash and target history");
        }
        finally { view.Free(); }
    }

    private void CheckLiveFx(ShipDefinition def)
    {
        var body = new ShipBody("MODEL-CHECK", def, Faction.Blue);
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
