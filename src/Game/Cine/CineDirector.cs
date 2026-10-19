using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game.Cine;

/// <summary>
/// 개발 중 소개 티저 감독. 장면 목록(CineScript)을 영상 시간 순서로 진행하고, 카메라·효과·자막을 맞춘다.
/// 녹화: Godot 무비 메이커(--write-movie, --fixed-fps 30)로 프레임을 받아 tools/render-cine.ps1이 영상으로 묶는다.
/// 인자: --cine (전체), --cine-still=장면@초 --cine-out=파일.png (한 장 확인), --cine-events=파일.json (소리 사건 기록).
/// </summary>
public partial class CineDirector : Node3D
{
    private static readonly Vec3d PlanetPosition = new(-2.2e7, -0.9e7, -5.5e7);
    private const float PlanetRadius = 6.0e6f;

    private List<CineShot> _shots = null!;
    private readonly List<double> _starts = new();
    private CineStage? _stage;
    private CineFx _fx = null!;
    private CineOverlay _overlay = null!;
    private Camera3D _camera = null!;
    private MeshInstance3D _planet = null!;
    private int _index = -1;
    private double _local, _video, _accum;
    private Dictionary<string, string> _args = null!;
    private double _stillAt = -1;
    private int _stillFrames;

    public override void _Ready()
    {
        _args = BattleArgs.Parse(OS.GetCmdlineUserArgs());
        // 편집본 선택: v3(기본, 지구 대 화성 티저) / v4(새 함급·전개·정찰·방공·강습·원거리 격침).
        bool v4 = _args.GetValueOrDefault("cine-cut", "v3") == "v4";
        _shots = v4 ? CineScriptV4.Build() : CineScript.Build();
        double start = 0;
        foreach (CineShot shot in _shots) { _starts.Add(start); start += shot.Duration; }
        GD.Print($"cine: {_shots.Count} shots, {start:0.0} s");

        BuildEnvironment();
        // 전투 화면과 같은 화면 원판 행성(화면 가장자리에서도 둥글다).
        var planetMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/gas_giant_disc.gdshader") };
        planetMaterial.SetShaderParameter("radius", PlanetRadius);
        _planet = new MeshInstance3D
        {
            Name = "Planet",
            Mesh = new QuadMesh { Size = new Vector2(2, 2) },
            MaterialOverride = planetMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            CustomAabb = new Aabb(-Vector3.One * PlanetRadius, Vector3.One * PlanetRadius * 2),
            Rotation = new Vector3(0.35f, 0f, 0.25f),
        };
        AddChild(_planet);
        _camera = new Camera3D { Name = "CineCamera", Near = 0.5f, Far = 1.0e6f, Fov = 50 };
        AddChild(_camera);
        _camera.MakeCurrent();
        // CineFx는 소리 사건·흔들림만 기록한다. 화면 연출은 게임과 같은 BallisticsView·OrdnanceView·BattleFx·ShipView가 그린다.
        _fx = new CineFx { Name = "Fx", Visible = false };
        AddChild(_fx);
        var layer = new CanvasLayer { Name = "CineLayer" };
        _overlay = new CineOverlay { Name = "Overlay" };
        if (v4) _overlay.UseCutV4();
        layer.AddChild(_overlay);
        AddChild(layer);

        if (_args.TryGetValue("cine-still", out string? still))
        {
            string[] parts = still.Split('@');
            int index = _shots.FindIndex(s => s.Name == parts[0]);
            if (index < 0) throw new ArgumentException($"Unknown cine shot: {parts[0]}");
            _stillAt = _starts[index] + (parts.Length > 1 ? double.Parse(parts[1], CultureInfo.InvariantCulture) : 0);
        }
    }

    public override void _Process(double delta)
    {
        const double FrameTime = 1.0 / 30.0;
        if (_stillAt >= 0)
        {
            // 한 장 확인: 원하는 시각까지 그리지 않고 진행한 뒤 몇 프레임 그려서 저장한다.
            if (_stillFrames == 0)
                while (_video + FrameTime <= _stillAt && _index < _shots.Count) Advance(FrameTime);
            Render(FrameTime);
            if (++_stillFrames >= 4) SaveStill();
            return;
        }
        if (!Advance(FrameTime)) { Finish(); return; }
        Render(FrameTime);
    }

    /// <summary>영상 시간을 dt만큼 진행한다. 장면이 끝났으면 false.</summary>
    private bool Advance(double dt)
    {
        if (_index < 0) StartShot(0);
        if (_index >= _shots.Count) return false;
        CineShot shot = _shots[_index];
        _fx.VideoTime = _video;
        if (_stage is not null && !shot.Card)
        {
            _stage.Local = _local;
            shot.Tick?.Invoke(_stage, _local);
            _accum += dt * shot.Rate(_local) * SimWorld.TickRate;
            while (_accum >= 1)
            {
                _stage.StepDuels();
                _stage.World.Step();
                _fx.Observe(_stage.World);
                _stage.Fx.Observe(_stage.World, _fx.Camera);
                _stage.ProbeModel.Track(_stage.World.Time);
                _accum -= 1;
            }
            _stage.Alpha = _accum;
        }
        _local += dt;
        _video += dt;
        _fx.Trauma = Mathf.Max(0, Mathf.Min(1.2f, _fx.Trauma) - (float)dt * 1.4f);
        if (_local >= shot.Duration - 1e-6)
        {
            if (_index + 1 >= _shots.Count) { _index++; return false; }
            StartShot(_index + 1);
        }
        return true;
    }

    private void StartShot(int index)
    {
        _index = index;
        _local = 0;
        CineShot shot = _shots[index];
        GD.Print($"cine shot {index}: {shot.Name} at {_video:0.00}s");
        if (shot.Card)
        {
            if (_stage is not null) _stage.Root.Visible = false;
            return;
        }
        if (!shot.Continue || _stage is null)
        {
            _stage?.Dispose();
            _fx.Clear();
            _stage = new CineStage(this);
            _stage.Sound = _fx.Emit;
            _accum = 0;
        }
        _stage.Root.Visible = true;
        shot.Setup?.Invoke(_stage);
        if (shot.PreRoll > 0) _stage.Settle(shot.PreRoll);
        _fx.Adopt(_stage.World);
        _stage.Fx.Adopt(_stage.World);
    }

    private void Render(double delta)
    {
        if (_index < 0 || _index >= _shots.Count) return;
        CineShot shot = _shots[_index];
        _overlay.Shot = shot;
        _overlay.Local = _local;
        float fade = 0;
        if (shot.FadeIn > 0) fade = Mathf.Max(fade, 1 - (float)(_local / shot.FadeIn));
        if (shot.FadeOut > 0) fade = Mathf.Max(fade, 1 - (float)((shot.Duration - _local) / shot.FadeOut));
        _overlay.Fade = Mathf.Clamp(fade, 0, 1);
        if (shot.Card || _stage is null || shot.Camera is null) return;

        CamPose pose = shot.Camera(_stage, _local);
        Vec3d origin = pose.Position;
        _fx.Camera = origin;
        foreach (ShipView view in _stage.Views) view.Sync(origin, _stage.Alpha, (float)delta);
        foreach (var (node, at, rotation) in _stage.Props) node.Transform = new Transform3D(new Basis(rotation), (at - origin).ToVector3());
        PlaceBackdrop(_planet, PlanetPosition - origin);
        _planet.Visible = shot.Planet;

        Vector3 forward = (pose.Target - pose.Position).ToVector3().Normalized();
        Basis basis = Basis.LookingAt(forward, pose.Up);

        // 폭발 흔들림: 충격 크기의 제곱에 비례하는 작은 회전.
        float shake = _fx.Trauma * _fx.Trauma * 0.012f;
        if (shake > 0)
        {
            double n = _video * 31;
            basis = basis * new Basis(Vector3.Up, shake * (float)(Math.Sin(n) + Math.Sin(n * 2.3) * 0.5))
                          * new Basis(Vector3.Right, shake * (float)(Math.Sin(n * 1.7 + 1) + Math.Sin(n * 3.1) * 0.5));
        }
        _camera.Transform = new Transform3D(basis, Vector3.Zero);
        _camera.Fov = pose.Fov;
        // 카메라가 놓인 뒤에 게임 연출을 맞춘다(화면 크기 하한·판 방향이 카메라를 쓴다).
        _stage.Ballistics.Sync(_stage.World, origin, _stage.Alpha, _camera, _stage.Views);
        foreach (ShipView view in _stage.Views) view.SyncCombat(_stage.World, _camera);
        _stage.Ordnance.Sync(_stage.World, origin, _stage.Alpha, Vector3.Zero, _camera);
        _stage.Fx.Sync(origin, _stage.World.Time + _stage.Alpha * SimWorld.TickDelta, _camera);
        _stage.ProbeModel.Sync(origin, _stage.Alpha, _stage.World.Time + _stage.Alpha * SimWorld.TickDelta);
    }

    private static void PlaceBackdrop(Node3D node, Vec3d relative)
    {
        const double BackdropDistance = 600_000.0;
        double distance = relative.Length();
        double k = distance > BackdropDistance ? BackdropDistance / distance : 1.0;
        node.Position = (relative * k).ToVector3();
        node.Scale = Vector3.One * (float)k;
    }

    private void SaveStill()
    {
        string path = _args.GetValueOrDefault("cine-out", "user://cine_still.png");
        Error err = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print(err == Error.Ok ? $"cine still saved: {path}" : $"cine still failed: {err}");
        PrintStageState();
        GetTree().Quit();
    }

    private void PrintStageState()
    {
        if (_stage is null) return;
        foreach (ShipBody ship in _stage.World.Ships)
            GD.Print($"  {ship.Callsign}: pos={ship.Position} vel={ship.Velocity.Length():0} fwd={ship.Forward} shield={ship.Damage.Shield:0} destroyed={ship.Damage.Destroyed} modulesLost={ship.Damage.Modules.Count(m => m.Destroyed)} rounds={ship.Railguns.Sum(g => g.Rounds)} gunnery={ship.Gunnery?.Status} heat={ship.Power.HeatFraction:0.00} guns=[{string.Join(" ", ship.Railguns.Select(g => $"{g.Definition.ModuleId}:{g.ShotCount}"))}]");
        GD.Print($"  fx: {_fx.Census()} time={_stage.World.Time:0.00}");
        foreach (var probe in _stage.World.Probes)
            GD.Print($"  probe: pos={probe.Position} dest={(probe.Destination - probe.Position).Length():0} m speed={probe.Velocity.Length():0} onStation={probe.OnStation} since={_stage.ProbeModel.SinceStation(_stage.World.Time):0.0}");
        GD.Print("  events: " + string.Join(" ", _stage.World.OrdnanceEvents.Select(e => $"{e.Kind}/{e.Weapon}@{e.Time:0.0}")));
        GD.Print($"  projectiles={_stage.World.Projectiles.Count} missiles={_stage.World.Missiles.Count} pd={_stage.World.PointDefenseShots.Count} sounds={_fx.Sounds.Count}");
    }

    private void Finish()
    {
        if (_args.TryGetValue("cine-events", out string? path))
        {
            var sb = new StringBuilder();
            sb.Append("{\"duration\":").Append(_video.ToString("0.000", CultureInfo.InvariantCulture)).Append(",\"shots\":[");
            sb.Append(string.Join(",", _shots.Select((s, i) => $"{{\"name\":\"{s.Name}\",\"start\":{_starts[i].ToString("0.000", CultureInfo.InvariantCulture)},\"duration\":{s.Duration.ToString("0.000", CultureInfo.InvariantCulture)}}}")));
            sb.Append("],\"events\":[");
            sb.Append(string.Join(",", _fx.Sounds.Select(e =>
                $"{{\"t\":{e.Time.ToString("0.000", CultureInfo.InvariantCulture)},\"kind\":\"{e.Kind}\",\"d\":{e.Distance.ToString("0", CultureInfo.InvariantCulture)},\"s\":{e.Size.ToString("0.0", CultureInfo.InvariantCulture)}}}")));
            sb.Append("]}");
            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
            file.StoreString(sb.ToString());
            GD.Print($"cine events: {_fx.Sounds.Count} -> {path}");
        }
        GD.Print($"cine done at {_video:0.00}s");
        GetTree().Quit();
    }

    private void BuildEnvironment()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky
            {
                SkyMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/starfield.gdshader") },
                RadianceSize = Sky.RadianceSizeEnum.Size256,
            },
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.24f, 0.28f, 0.36f),
            AmbientLightEnergy = 0.32f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            GlowEnabled = true,
            GlowIntensity = 0.85f,
            GlowBloom = 0.04f,
            GlowHdrThreshold = 1.2f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive,
            AdjustmentEnabled = true,
            AdjustmentContrast = 1.08f,
            AdjustmentSaturation = 1.05f,
        };
        AddChild(new WorldEnvironment { Name = "Environment", Environment = env });
        AddChild(new DirectionalLight3D
        {
            Name = "Sun", LightEnergy = 2.6f, LightColor = new Color(1f, 0.95f, 0.88f), ShadowEnabled = true,
            DirectionalShadowMaxDistance = 8000f, DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            Basis = Basis.LookingAt(new Vector3(-0.75f, -0.35f, -0.35f).Normalized(), Vector3.Up),
        });
        AddChild(new DirectionalLight3D
        {
            Name = "PlanetFill", LightEnergy = 0.22f, LightColor = new Color(0.55f, 0.62f, 0.85f),
            SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly,
            Basis = Basis.LookingAt(new Vector3(0.6f, 0.55f, 0.3f).Normalized(), Vector3.Up),
        });
    }
}
