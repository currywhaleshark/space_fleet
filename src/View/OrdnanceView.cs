using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.View;

/// <summary>
/// 미사일(몸체 + 연소 중 화염 줄), 디코이(사라지는 섬광), 근접방어 예광, 폭발·요격 섬광. 판정에는 관여하지 않는다.
/// 크기는 카메라 거리에 비례해 키워 수 km 밖에서도 점으로 보이게 한다.
/// </summary>
public partial class OrdnanceView : Node3D
{
    private readonly Dictionary<uint, MeshInstance3D> _probes = new();
    private readonly Dictionary<uint, Node3D> _missiles = new();
    private readonly Dictionary<uint, MeshInstance3D> _decoys = new();
    private readonly List<MeshInstance3D> _tracers = new();
    private readonly List<MeshInstance3D> _flashes = new();
    private readonly List<MeshInstance3D> _shards = new();

    private readonly BoxMesh _body = new() { Size = new Vector3(0.6f, 0.6f, 4f) };
    private readonly CylinderMesh _trail = new() { TopRadius = 0f, BottomRadius = 1f, Height = 1f, RadialSegments = 6, CapTop = false, CapBottom = false };
    private readonly SphereMesh _sphere = new() { Radius = 1f, Height = 2f, RadialSegments = 10, Rings = 5 };
    private readonly BoxMesh _line = new() { Size = Vector3.One };

    private readonly StandardMaterial3D _blue = Glow(new Color(0.4f, 0.75f, 1f), 3f);
    private readonly StandardMaterial3D _red = Glow(new Color(1f, 0.45f, 0.25f), 3f);
    private readonly StandardMaterial3D _flame = Glow(new Color(1f, 0.85f, 0.6f), 5f, additive: true);
    private static Shader? _decoyShader;
    /// <summary>디코이 섬광의 실제 지름(m)과 가까울 때의 화면 크기 하한(px). 하한은 3 km 밖부터 거리에 반비례해 줄인다.</summary>
    public const float DecoyFlareMeters = 40f, DecoyMinPixels = 24f;
    /// <summary>검사용: 디코이 섬광 노드들.</summary>
    internal IEnumerable<MeshInstance3D> DecoyFlares => _decoys.Values;
    private readonly StandardMaterial3D _tracer = Glow(new Color(1f, 0.9f, 0.5f), 4f, additive: true);
    private readonly StandardMaterial3D _tracerHit = Glow(new Color(1f, 0.6f, 0.3f), 6f, additive: true);
    private readonly StandardMaterial3D _boom = Glow(new Color(1f, 0.6f, 0.25f), 6f, additive: true);
    private readonly StandardMaterial3D _puff = Glow(new Color(0.9f, 0.95f, 1f), 4f, additive: true);
    private readonly StandardMaterial3D _am = Glow(new Color(.55f,.9f,1),10f,additive:true);
    private readonly StandardMaterial3D _fragment = new() { AlbedoColor=new Color(.18f,.23f,.28f), Metallic=.8f, Roughness=.6f };

    private ShaderMaterial? _flashWhite, _flashWarm;
    /// <summary>끄면 정찰 프로브 표지를 그리지 않는다(티저가 근접 촬영용 외형을 따로 그릴 때).</summary>
    public bool DrawProbes { get; set; } = true;
    public void Sync(SimWorld world, Vec3d origin, double alpha, Vector3 cameraPosition,Camera3D? camera=null, Faction? viewer=null)
    {
        float Scale(Vector3 at, float minimum) => Mathf.Max(minimum, at.DistanceTo(cameraPosition) * 0.0025f);

        var active = new HashSet<uint>();
        foreach (Missile m in world.Missiles)
        {
            active.Add(m.Id);
            if (!_missiles.TryGetValue(m.Id, out Node3D? node))
                _missiles[m.Id] = node = MakeMissile(m.Assault is not null ? _am : m.Faction == Faction.Blue ? _blue : _red, m.Assault is not null);
            Vector3 pos = (Vec3d.Lerp(m.PrevPosition, m.Position, alpha) - origin).ToVector3();
            Vector3 dir = m.Assault is not null ? m.LaunchDirection
                : m.NoseDirection.LengthSquared()>.5f ? (m.PreviousNoseDirection.LengthSquared()>.5f
                    ? m.PreviousNoseDirection.Slerp(m.NoseDirection,(float)alpha).Normalized() : m.NoseDirection)
                : m.Velocity.LengthSquared() > 1f ? m.Velocity.Normalized() : Vector3.Forward;
            float baseScale = m.Assault is null ? m.Definition.BodyScale : .7f;
            // 실제 크기로 그리되, 멀어지면 몸체가 화면에서 약 4픽셀 아래로 줄지 않게만 한다(예전: 거리×0.25%로 계속 커짐).
            float s = camera is null ? Scale(pos, baseScale) : Mathf.Max(baseScale, CombatFx.PixelSize(camera, pos, 4) / 4f);
            node.Transform = new Transform3D(Basis.LookingAt(dir, Mathf.Abs(dir.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up).Scaled(Vector3.One * s), pos);
            var flame = node.GetChild<Node3D>(1);
            flame.Visible = m.Burning;
            if (m.Burning)
            {
                // 화염 꼬리: 가까이서는 실제 길이, 멀리서는 화면에서 약 7픽셀까지만(몸체보다 크게 늘이지 않는다).
                float real = (m.Assault is null ? 14f : 8f) * baseScale;
                float shown = camera is null ? real * s / baseScale : Mathf.Max(real, Mathf.Min(real * s / baseScale, CombatFx.PixelSize(camera, pos, 7)));
                flame.Scale = new Vector3(1, shown / (real * s / baseScale), 1);
            }
        }
        Prune(_missiles, active);

        active.Clear();
        foreach (var probe in world.Probes)
        {
            if (!DrawProbes) break;
            bool friendly = viewer is null || probe.Faction == viewer;
            var seen = viewer is { } side ? world.Sensors.SmallTrack(side, probe) : null;
            if (!friendly && (seen is null || !seen.Value.Fresh(world.Time))) continue;
            active.Add(probe.Id);
            if (!_probes.TryGetValue(probe.Id, out var mesh)) _probes[probe.Id] = mesh = Make(_sphere, probe.Faction == Faction.Blue ? _blue : _red);
            Vec3d observed = friendly ? Vec3d.Lerp(probe.PreviousPosition, probe.Position, alpha) : seen!.Value.Predict(world.Time);
            Vector3 pos = (observed - origin).ToVector3();
            mesh.Position = pos; mesh.Scale = new Vector3(1, .5f, 1) * Scale(pos, 1.5f);
        }
        Prune(_probes, active);
        active.Clear();
        foreach (Decoy d in world.Decoys)
        {
            active.Add(d.Id);
            if (!_decoys.TryGetValue(d.Id, out MeshInstance3D? flare))
            {
                var material = new ShaderMaterial { Shader = _decoyShader ??= GD.Load<Shader>("res://shaders/decoy_flare.gdshader") };
                material.SetShaderParameter("seed", (d.Id % 97) * 0.37f);
                _decoys[d.Id] = flare = Make(CombatFx.Quad, material);
            }
            Vector3 pos = (Vec3d.Lerp(d.PrevPosition, d.Position, alpha) - origin).ToVector3();
            float life = Mathf.Max(0f, 1f - (float)(d.Age / d.Definition.LifetimeSeconds));
            var flareMaterial = (ShaderMaterial)flare.MaterialOverride;
            flareMaterial.SetShaderParameter("life", life);
            flareMaterial.SetShaderParameter("time", (float)(world.Time % 1000.0));
            // 섬광 광륜(약 40 m)으로 그리고, 화면 크기 하한은 가까울 때만 둔다. 예전에는 거리에 비례해 키워서
            // 거리와 무관하게 같은 크기로 보였고 망원에서 크게 부풀었다.
            float worldSize = DecoyFlareMeters * (0.6f + 0.4f * life);
            if (camera is null) { flare.Position = pos; flare.Scale = Vector3.One * worldSize; }
            else
            {
                float near = Mathf.Clamp(3000f / Mathf.Max(1f, pos.DistanceTo(camera.GlobalPosition)), 0.25f, 1f);
                CombatFx.Flare(flare, camera, pos, worldSize, DecoyMinPixels * near);
            }
        }
        Prune(_decoys, active);

        // 근접방어 예광: 최근 사격을 가는 선으로. 매 프레임 다시 그린다.
        int t = 0;
        foreach (PointDefenseShot shot in world.PointDefenseShots)
        {
            MeshInstance3D line = t < _tracers.Count ? _tracers[t] : Add(_tracers, _line, _tracer);
            line.MaterialOverride = shot.Hit ? _tracerHit : _tracer;
            Vector3 a = (shot.From - origin).ToVector3(), b = (shot.To - origin).ToVector3();
            Vector3 v = b - a;
            float len = v.Length();
            if (len < 0.1f) { line.Visible = false; t++; continue; }
            Vector3 dir = v / len;
            Vector3 helper = Mathf.Abs(dir.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up;
            Vector3 x = helper.Cross(dir).Normalized(), y = dir.Cross(x).Normalized();
            float w = Scale(a, 0.3f) * 0.15f;
            line.Transform = new Transform3D(new Basis(x * w, y * w, dir * len), a + v * 0.5f);
            line.Visible = true;
            t++;
        }
        for (; t < _tracers.Count; t++) _tracers[t].Visible = false;

        // 폭발(주황)·요격(흰 구름) 섬광.
        int f = 0;
        int shardCount=0;
        foreach (OrdnanceEvent e in world.OrdnanceEvents)
        {
            float age = (float)(world.Time - e.Time);
            if(e.Kind is OrdnanceEventKind.DroneDestroyed or OrdnanceEventKind.ProbeDestroyed)
            {
                Vector3 at=(e.Position-origin).ToVector3();
                if(age<.65f)
                    for(int j=0;j<5;j++)
                    {
                        var fragment=shardCount<_shards.Count ? _shards[shardCount] : Add(_shards,_body,_fragment);
                        Vector3 direction=new(Mathf.Sin(j*2.4f),Mathf.Cos(j*1.7f),Mathf.Sin(j*3.8f+.5f));
                        fragment.Transform=new(new Basis(Vector3.Up,j+age*5).Scaled(Vector3.One*Scale(at,.8f)*(1-age/.65f)),
                            at+direction.Normalized()*(14+j*5)*age);
                        fragment.Visible=true; shardCount++;
                    }
                if(age>=.18f) continue;
                var spark=f<_flashes.Count ? _flashes[f] : Add(_flashes,_sphere,_puff);
                spark.MaterialOverride=_puff; spark.Position=at;
                spark.Scale=Vector3.One*Scale(at,7)*(1-age/.18f); spark.Visible=true; f++;
                continue;
            }
            if(e.Weapon==BattleWeapon.Antimatter)
            {
                bool energetic=e.Kind is OrdnanceEventKind.Detonation or OrdnanceEventKind.ContainmentFailure;
                Vector3 at=(e.Position-origin).ToVector3();
                if(energetic && age<.8f || e.Kind==OrdnanceEventKind.Jettisoned && age<1.5f)
                {
                    int count=energetic ? 7 : 2;
                    for(int j=0;j<count;j++)
                    {
                        var fragment=shardCount<_shards.Count ? _shards[shardCount] : Add(_shards,_body,_fragment);
                        var vector=new Vector3(Mathf.Sin(j*2.4f),Mathf.Cos(j*1.7f),Mathf.Sin(j*3.8f+.5f)).Normalized();
                        Vector3 travel=energetic ? vector*(18+j*7)*age : e.Direction*age*35+vector*age*4;
                        float fragmentSize=Scale(at,1)*(energetic ? 1-age/.8f : .7f);
                        fragment.Transform=new(new Basis(Vector3.Up,j+age*5).Scaled(Vector3.One*fragmentSize),at+travel);
                        fragment.Visible=true; shardCount++;
                    }
                }
                if(age>=.22f) continue;
                MeshInstance3D amFlash=f<_flashes.Count ? _flashes[f] : Add(_flashes,_sphere,_am);
                amFlash.MaterialOverride=energetic ? _am : _puff;
                amFlash.Position=at;
                float radius=Scale(at,energetic ? 32 : 3)*(1-age/.22f);
                amFlash.Scale=new Vector3(radius,radius*.32f,radius);
                amFlash.Visible=true; f++; continue;
            }
            if (age > 0.5f || e.Kind == OrdnanceEventKind.Expired) continue;
            MeshInstance3D flash = f < _flashes.Count ? _flashes[f] : Add(_flashes, _sphere, _boom);
            flash.MaterialOverride = e.Kind == OrdnanceEventKind.Detonation ? _boom : _puff;
            Vector3 pos = (e.Position - origin).ToVector3();
            float size = (e.Kind == OrdnanceEventKind.Detonation ? 25f : 10f) * (0.4f + age * 2f);
            flash.Position = pos;
            flash.Scale = Vector3.One * Mathf.Max(size, Scale(pos, 2f) * 3f * (1f - age * 1.5f));
            flash.Visible = true;
            f++;
        }
        // Keep f as the active count: re-billboarding retired pool entries would
        // resurrect them and double their size every frame after the event ended.
        for (int i=f; i < _flashes.Count; i++) _flashes[i].Visible = false;
        for (; shardCount<_shards.Count; shardCount++) _shards[shardCount].Visible=false;
        if(camera is not null)
        {
            _flashWhite??=CombatFx.Glow(new Color(.75f,.9f,1)); _flashWarm??=CombatFx.Glow(new Color(1,.75f,.45f));
            for(int i=0;i<f;i++)
            {
                var flash=_flashes[i]; float size=flash.Scale.X*2;
                bool warm=flash.MaterialOverride==_boom;
                flash.Mesh=CombatFx.Quad; flash.MaterialOverride=warm?_flashWarm:_flashWhite;
                CombatFx.Flare(flash,camera,flash.Position,size,8);
            }
        }
    }

    private Node3D MakeMissile(Material body, bool antimatter=false)
    {
        var root = new Node3D();
        root.AddChild(new MeshInstance3D { Mesh = _body, MaterialOverride = body, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        // 연소 화염: 몸체 뒤(+Z)로 뻗는 원뿔.
        var flame = new Node3D { Transform = new Transform3D(new Basis(Vector3.Right, Mathf.Pi / 2f), new Vector3(0, 0, 2f)) };
        flame.AddChild(new MeshInstance3D
        {
            Mesh = _trail, MaterialOverride = antimatter ? _am : _flame, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Transform = new Transform3D(Basis.Identity.Scaled(new Vector3(0.5f, antimatter ? 8f : 14f, 0.5f)), new Vector3(0, antimatter ? 4f : 7f, 0)),
        });
        root.AddChild(flame);
        AddChild(root);
        return root;
    }

    private MeshInstance3D Make(Mesh mesh, Material material)
    {
        var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(node);
        return node;
    }

    private MeshInstance3D Add(List<MeshInstance3D> pool, Mesh mesh, Material material)
    {
        MeshInstance3D node = Make(mesh, material);
        pool.Add(node);
        return node;
    }

    private static void Prune<T>(Dictionary<uint, T> nodes, HashSet<uint> active) where T : Node
    {
        foreach (uint id in nodes.Keys.Where(id => !active.Contains(id)).ToArray())
        {
            // QueueFree는 프레임 끝에 지우므로, 그때까지 남아 보이지 않게 먼저 숨긴다.
            if (nodes[id] is Node3D node) node.Visible = false;
            nodes[id].QueueFree();
            nodes.Remove(id);
        }
    }

    private static StandardMaterial3D Glow(Color color, float energy, bool additive = false) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = color,
        EmissionEnabled = true,
        Emission = color,
        EmissionEnergyMultiplier = energy,
        BlendMode = additive ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
        Transparency = additive ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
        NoDepthTest = false,
    };
}
