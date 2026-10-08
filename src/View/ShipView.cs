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
        public float Yaw, Pitch;
    }
    private readonly List<DefenseVisual> _defense = new();
    private double _defenseTime;
    private MeshInstance3D? _amGlow;
    private StandardMaterial3D? _amMaterial;
    /// <summary>롤 축(Z)에서 가장 먼 노즐까지의 거리(m).</summary>
    private float _rollArm = 1f;

    public ShipBody Body { get; private set; } = null!;
    public Palette Palette { get; private set; } = null!;

    /// <summary>이번 프레임에 보간된 시뮬레이션 위치.</summary>
    public Vec3d SimPosition { get; private set; }

    public static ShipView Create(ShipBody body, int seed)
    {
        ShipModel model = ShipModels.Build(body.Class, body.Faction, seed);
        var view = new ShipView
        {
            Name = body.Callsign, Body = body, Palette = model.Palette, _plumes = model.Plumes,
            _rcs = model.RcsJets, _rcsLevel = new float[model.RcsJets.Count],
        };
        foreach (RcsJet jet in model.RcsJets)
            view._rollArm = Mathf.Max(view._rollArm, new Vector2(jet.Position.X, jet.Position.Y).Length());
        view.AddChild(model.Root);
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
        view._defenseTime=body.SimTime;
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
        SyncPointDefense();
        if (_amGlow is not null)
        {
            var am=Body.Ordnance.Antimatter;
            _amGlow.Visible=!Body.Damage.Destroyed && am.Rounds>0 && (am.Warning || am.Mode is AntimatterMode.Arming or AntimatterMode.Armed);
            Color color=am.Warning ? new Color(1,.12f,.02f) : new Color(.3f,1,1);
            _amMaterial!.AlbedoColor=color; _amMaterial.Emission=color;
            _amMaterial.EmissionEnergyMultiplier=am.Mode==AntimatterMode.Armed ? 5 : 2.5f+Mathf.Sin((float)Body.SimTime*8)*1.5f;
        }
    }

    private void SyncTurrets(float alpha)
    {
        foreach (var (gun, rig, flashes) in _batteries)
        {
            rig.Yaw.Basis = rig.RestBasis * new Basis(Vector3.Up, Mathf.Lerp(gun.PreviousYaw, gun.Yaw, alpha));
            rig.Elevation.Basis = new Basis(Vector3.Right, Mathf.Lerp(gun.PreviousElevation, gun.Elevation, alpha));
            double age = Body.SimTime - gun.LastFiredAt;
            float kick = age >= 0 && age < .45 ? (float)(age < .05 ? age / .05 : (.45 - age) / .4) : 0;
            rig.Recoil.Position = Vector3.Back * kick * Body.Class.Length * .002f;
            for (int i = 0; i < flashes.Length; i++) flashes[i].Visible = i == gun.LastBarrel && age >= 0 && age < .075;
        }
    }

    private void SyncPointDefense()
    {
        float dt=(float)System.Math.Max(0,Body.SimTime-_defenseTime);
        _defenseTime=Body.SimTime;
        foreach(var mount in _defense)
        {
            var rig=mount.Rig;
            if(!Body.Damage.Destroyed && mount.State.LocalAim is Vector3 local)
            {
                Vector3 direction=rig.RestBasis.Inverse()*local;
                float yaw=Mathf.Atan2(-direction.X,-direction.Z);
                float pitch=Mathf.Atan2(direction.Y,new Vector2(direction.X,direction.Z).Length());
                mount.Yaw+=Mathf.Clamp(Mathf.AngleDifference(mount.Yaw,yaw),-Mathf.DegToRad(540)*dt,Mathf.DegToRad(540)*dt);
                mount.Pitch=Mathf.MoveToward(mount.Pitch,pitch,Mathf.DegToRad(360)*dt);
                rig.Yaw.Basis=rig.RestBasis*new Basis(Vector3.Up,mount.Yaw);
                rig.Elevation.Basis=new Basis(Vector3.Right,mount.Pitch);
            }
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
