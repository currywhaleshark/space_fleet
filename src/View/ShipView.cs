using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.View;

/// <summary>
/// ShipBody 하나를 화면에 그린다. 매 프레임 두 틱 사이를 보간하고,
/// 렌더 원점을 뺀 상대 좌표(float)로 놓는다.
/// </summary>
public partial class ShipView : Node3D
{
    private IReadOnlyList<Node3D> _plumes = new List<Node3D>();
    private float _plumeLevel;
    private IReadOnlyList<RcsJet> _rcs = new List<RcsJet>();
    private float[] _rcsLevel = System.Array.Empty<float>();
    private readonly List<(RailgunState Gun, TurretRig Rig, MeshInstance3D[] Flashes)> _batteries = new();
    private sealed class DefenseVisual
    {
        public required PointDefenseMountState State;
        public required PointDefenseRig Rig;
        public required MeshInstance3D[] Flashes;
    }
    private readonly List<DefenseVisual> _defense = new();
    private MeshInstance3D? _amGlow;
    private StandardMaterial3D? _amMaterial;
    public DroneSwarm? Drones { get; private set; }
    public ShieldView Shield { get; private set; } = null!;
    public WreckView Wreck { get; private set; } = null!;
    private Node3D _model=null!;
    private VisualHullSurface? _surface;
    public Vector3 ProjectFxPoint(Vector3 localPoint,Vector3 incoming)
        => ProjectFxSurface(localPoint,incoming).Point;
    public (Vector3 Point,Vector3 Normal) ProjectFxSurface(Vector3 localPoint,Vector3 incoming)
        => (_surface??=new VisualHullSurface(this,_model)).Project(localPoint,incoming);
    private readonly List<(StandardMaterial3D Material,float Energy)> _poweredLights=new();
    private readonly List<ShaderMaterial> _poweredPanels=new();
    /// <summary>롤 축(Z)에서 가장 먼 노즐까지의 거리(m).</summary>
    private float _rollArm = 1f;

    public ShipBody Body { get; private set; } = null!;
    public Palette Palette { get; private set; } = null!;

    /// <summary>이번 프레임에 보간된 시뮬레이션 위치.</summary>
    public Vec3d SimPosition { get; private set; }

    public static ShipView Create(ShipBody body, int seed)
    {
        ShipModel model = ShipModels.Build(body.Definition, body.Faction, seed);
        var view = new ShipView
        {
            Name = body.Callsign, Body = body, Palette = model.Palette, _plumes = model.Plumes,
            _rcs = model.RcsJets, _rcsLevel = new float[model.RcsJets.Count],
        };
        foreach (RcsJet jet in model.RcsJets)
            view._rollArm = Mathf.Max(view._rollArm, new Vector2(jet.Position.X, jet.Position.Y).Length());
        view.AddChild(model.Root);
        view._model=model.Root;
        view.Shield=new ShieldView { Name="ShieldSkin" }; view.AddChild(view.Shield);
        view.Wreck=new WreckView { Name="Wreck" }; view.AddChild(view.Wreck);
        var powerMaterials=new Dictionary<StandardMaterial3D,StandardMaterial3D>();
        var panelMaterials=new Dictionary<ShaderMaterial,ShaderMaterial>();
        foreach(var mesh in ImportedShipModels.Meshes(model.Root))
        for(int surface=0;surface<mesh.Mesh.GetSurfaceCount();surface++)
        {
            if(mesh.GetActiveMaterial(surface) is ShaderMaterial panel && WreckView.IsHullPanel(panel))
            {
                if(!panelMaterials.TryGetValue(panel,out var localPanel)) {
                    localPanel=(ShaderMaterial)panel.Duplicate(); panelMaterials.Add(panel,localPanel);
                    view._poweredPanels.Add(localPanel); }
                mesh.SetSurfaceOverrideMaterial(surface,localPanel);
                continue;
            }
            if(mesh.GetActiveMaterial(surface) is not StandardMaterial3D { EmissionEnabled:true } original) continue;
            if(!powerMaterials.TryGetValue(original,out var local)) {
                local=(StandardMaterial3D)original.Duplicate(); powerMaterials.Add(original,local);
                view._poweredLights.Add((local,original.EmissionEnergyMultiplier)); }
            mesh.SetSurfaceOverrideMaterial(surface,local);
        }
        if (body.Definition.DefenseDrones is not null)
        {
            view.Drones=DroneSwarm.Create(body,view.Palette);
            view.AddChild(view.Drones);
            view.Drones.Sync(1);
        }
        if (body.Definition.Antimatter is { } am)
        {
            view._amMaterial = new StandardMaterial3D { ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,
                EmissionEnabled=true, Emission=new Color(.3f,1,1), AlbedoColor=new Color(.3f,1,1), EmissionEnergyMultiplier=3 };
            view._amGlow = new MeshInstance3D { Name="AntimatterContainmentGlow",
                Mesh=new BoxMesh { Size=new Vector3(1.1f,.08f,1.9f) }, MaterialOverride=view._amMaterial, Visible=false,
                Position=body.Damage.Module(am.ModuleId).Definition.Center+new Vector3(0,-1.85f,0) };
            model.Root.AddChild(view._amGlow);
        }
        foreach (TurretRig rig in model.Turrets)
        {
            RailgunState gun = body.Railguns.Single(g => g.Definition.ModuleId == rig.ModuleId);
            var flashes = rig.Muzzles.Select(socket =>
            {
                float radius = body.Class.Length * .003f;
                var flash = new MeshInstance3D { Name = "MuzzleFlash", Visible = false,
                    Mesh = new SphereMesh { Radius = radius, Height = radius * 2, RadialSegments = 8, Rings = 4 },
                    MaterialOverride = model.Palette.Glow, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                socket.AddChild(flash); return flash;
            }).ToArray();
            view._batteries.Add((gun, rig, flashes));
        }
        foreach(var rig in model.PointDefense)
        {
            float radius=rig.Muzzles[0].Position.DistanceTo(rig.Muzzles[1].Position)*.4f;
            var flashes=rig.Muzzles.Select(socket=>
            {
                var flash=new MeshInstance3D { Name="PDFlash",Visible=false,
                    Mesh=new SphereMesh { Radius=radius,Height=radius*2,RadialSegments=8,Rings=4 },
                    MaterialOverride=model.Palette.Glow,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off };
                socket.AddChild(flash); return flash;
            }).ToArray();
            view._defense.Add(new DefenseVisual { State=body.Ordnance.PointDefense[rig.Index],Rig=rig,Flashes=flashes });
        }
        WreckView.PrepareGeometry(model.Root,body.Class.Length);
        return view;
    }

    public void Sync(Vec3d renderOrigin, double alpha, float delta)
    {
        SimPosition = Body.InterpolatedPosition(alpha);
        Transform = new Transform3D(
            new Basis(Body.InterpolatedOrientation((float)alpha)),
            (SimPosition - renderOrigin).ToVector3());

        _plumeLevel = Mathf.Lerp(_plumeLevel, Body.EngineOutput, 1f - Mathf.Exp(-delta * 6f));
        for (int i = 0; i < _plumes.Count; i++)
        {
            Node3D plume = _plumes[i];
            float level = _plumeLevel * Body.Damage.EngineFraction(i);
            bool burning = level > 0.03f;
            // 정지 상태의 짧은 원뿔은 뒤에서 보면 노즐 위에 겹쳐 하얗게 포화되므로 숨긴다.
            plume.Visible = burning;
            if (burning)
                plume.Scale = new Vector3(1f, level, 1f);
        }
        SyncRcs(delta);
        SyncTurrets((float)alpha);
        SyncPointDefense((float)alpha);
        foreach(var (material,energy) in _poweredLights) material.EmissionEnergyMultiplier=Body.Damage.GenerationFraction>.001f?energy:0;
        foreach(var material in _poweredPanels) material.SetShaderParameter("power",Body.Damage.GenerationFraction>.001f?1f:0f);
        Drones?.Sync((float)alpha);
        if (_amGlow is not null)
        {
            var am=Body.Ordnance.Antimatter;
            _amGlow.Visible=!Body.Damage.Destroyed && am.Rounds>0 && (am.Warning || am.Mode is AntimatterMode.Arming or AntimatterMode.Armed);
            Color color=am.Warning ? new Color(1,.12f,.02f) : new Color(.3f,1,1);
            _amMaterial!.AlbedoColor=color; _amMaterial.Emission=color;
            _amMaterial.EmissionEnergyMultiplier=am.Mode==AntimatterMode.Armed ? 5 : 2.5f+Mathf.Sin((float)Body.SimTime*8)*1.5f;
        }
    }

    public void SyncCombat(SimWorld world,Camera3D camera)
    { Shield.Sync(this,world); Wreck.Sync(this,_model,camera); }

    private void SyncTurrets(float alpha)
    {
        foreach (var (gun, rig, flashes) in _batteries)
        {
            rig.Yaw.Basis = rig.RestBasis * new Basis(Vector3.Up, Mathf.Lerp(gun.PreviousYaw, gun.Yaw, alpha));
            rig.Elevation.Basis = new Basis(Vector3.Right, Mathf.Lerp(gun.PreviousElevation, gun.Elevation, alpha));
            double age = Body.SimTime - gun.LastFiredAt;
            float kick = age >= 0 && age < .45 ? (float)(age < .05 ? age / .05 : (.45 - age) / .4) : 0;
            rig.Recoil.Position = Vector3.Back * kick * Body.Class.Length * .002f;
            for (int i = 0; i < flashes.Length; i++) {
                float muzzleAge=CarbonCombatFx.MuzzleAge(Body.SimTime,gun.LastFiredAt,i,0,gun.LastSalvoRounds);
                flashes[i].Visible = !Body.Damage.Destroyed && muzzleAge >= 0 && muzzleAge < .075;
            }
        }
    }

    private void SyncPointDefense(float alpha)
    {
        foreach(var mount in _defense)
        {
            var rig=mount.Rig;
            var state=mount.State;
            rig.Yaw.Basis=rig.RestBasis*new Basis(Vector3.Up,Mathf.Lerp(state.PreviousYaw,state.Yaw,alpha));
            rig.Elevation.Basis=new Basis(Vector3.Right,Mathf.Lerp(state.PreviousElevation,state.Elevation,alpha));
            double age=Body.SimTime-mount.State.LastFiredAt;
            foreach(var flash in mount.Flashes) flash.Visible=!Body.Damage.Destroyed && age>=0 && age<.045;
        }
    }

    /// <summary>
    /// 보조 추진기 분사. 노즐 위치에서 필요한 가속 = 병진(횡·상하·제동) + 회전(α × r).
    /// 노즐은 분사 방향의 반대로 밀므로, 필요한 가속이 그쪽을 향할 때만 켠다.
    /// </summary>
    private void SyncRcs(float delta)
    {
        if (_rcs.Count == 0)
            return;
        ShipClass cls = Body.Class;
        Vector3 a = Body.LocalAcceleration;
        // 전방 가속은 메인 추진 몫이라 뺀다(전력 계산과 같은 구분).
        Vector3 linear = new Vector3(a.X, a.Y, Mathf.Max(0f, a.Z)) / Mathf.Max(cls.StrafeAccel, 1e-3f);
        // 피치·요는 최대 각가속일 때 함수·함미 끝의 선가속도로, 롤은 최대 롤 각가속일 때
        // 가장 바깥 노즐의 선가속도로 정규화한다(롤 팔은 선체 반폭 정도라 길이 기준이면 거의 보이지 않는다).
        float pitchYawScale = Mathf.Max(Mathf.DegToRad(cls.PitchYawAccelDeg) * cls.Length * 0.5f, 1e-3f);
        float rollScale = Mathf.Max(Mathf.DegToRad(cls.RollAccelDeg) * _rollArm, 1e-3f);
        Vector3 angular = Body.AngularAcceleration;
        var pitchYaw = new Vector3(angular.X, angular.Y, 0f) / pitchYawScale;
        var roll = new Vector3(0f, 0f, angular.Z) / rollScale;
        bool alive = Body.Damage.ManeuverFraction > 0.01f;
        float follow = 1f - Mathf.Exp(-delta * 15f);
        for (int i = 0; i < _rcs.Count; i++)
        {
            RcsJet jet = _rcs[i];
            Vector3 need = linear + pitchYaw.Cross(jet.Position) + roll.Cross(jet.Position);
            float target = alive ? Mathf.Clamp(-jet.Exhaust.Dot(need), 0f, 1f) : 0f;
            float level = _rcsLevel[i] = Mathf.Lerp(_rcsLevel[i], target, follow);
            bool firing = level > 0.05f;
            jet.Pivot.Visible = firing;
            if (firing)
                jet.Pivot.Scale = new Vector3(1f, level, 1f);
        }
    }
}
