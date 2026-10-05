using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>
/// 0단계 스케일 테스트 씬.
/// - 시뮬레이션: SimWorld가 60Hz 고정 틱, 위치는 double.
/// - 렌더링: 매 프레임 렌더 원점(기본은 조종함)을 빼서 float로 변환 = 연속 플로팅 오리진.
/// </summary>
public partial class ScaleTest : Node3D
{
    private const double JumpDistance = 1_000_000.0;
    private static readonly Vec3d PlanetPosition = new(-2.2e7, -0.9e7, -5.5e7);
    private const float PlanetRadius = 6.0e6f;

    // 먼지 상자 크기 = 함종 카메라 거리 × 이 값. 요격함 약 170 m, 전함 약 9.6 km.
    private const float DustBoxPerCameraDistance = 4f;

    private readonly List<ShipView> _playable = new();
    private Node3D _worldRoot = null!;
    private SpaceDust _dust = null!;
    private MeshInstance3D _planet = null!;
    private int _controlledIndex;
    private bool _flightAssist = true;
    public AssistStyle AssistStyle { get; private set; } = AssistStyle.Space;
    public bool ShowHelp { get; private set; }
    private ShotRequest? _shot;
    private int _frame;
    private int _testShots;
    private Vec3d _testOrigin;
    private Vector3 _testDirection;

    public SimWorld World { get; } = new();
    public List<ShipView> Views { get; } = new();
    public ChaseCamera Camera { get; private set; } = null!;
    public ShipView? Controlled => _playable.Count > 0 ? _playable[_controlledIndex] : null;
    public float Throttle { get; private set; }
    public bool FloatingOrigin { get; private set; } = true;
    public Vec3d RenderOrigin { get; private set; }
    public ShipView? InspectTarget { get; private set; }
    public bool ShowModules { get; private set; }
    public ShotResult? LastTestShot { get; private set; }
    public double LastTestShotTime { get; private set; }

    public override void _Ready()
    {
        InputSetup.Register();
        BuildEnvironment();

        _worldRoot = new Node3D { Name = "World" };
        AddChild(_worldRoot);
        SpawnFleets();
        BuildPlanet();
        _ballistics = new BallisticsView { Name = "Ballistics" };
        _worldRoot.AddChild(_ballistics);

        Camera = new ChaseCamera { Name = "Camera" };
        AddChild(Camera);
        Camera.MakeCurrent();
        _dust = SpaceDust.Create(seed: 11);
        AddChild(_dust);

        var layer = new CanvasLayer { Name = "HudLayer" };
        layer.AddChild(new Hud { Name = "Hud", Game = this });
        AddChild(layer);

        _shot = ShotRequest.Parse(OS.GetCmdlineUserArgs());
        SelectControl(_shot?.Control ?? "IC-21");
        if (_shot is not null)
            ApplyShotSetup(_shot);
        else
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void SpawnFleets()
    {
        // 아군: 전함 중심 전투단 + 호위함 2 + 요격함 2
        ShipView flagship = Spawn("BB-01", ShipClass.Battleship, Faction.Blue, new(0, 0, 0), Quaternion.Identity);
        Spawn("DD-11", ShipClass.Escort, Faction.Blue, new(-900, 160, -500), Quaternion.Identity);
        Spawn("DD-12", ShipClass.Escort, Faction.Blue, new(950, -140, -300), Quaternion.Identity);
        Spawn("IC-21", ShipClass.Interceptor, Faction.Blue, new(420, 110, 850), Quaternion.Identity);
        Spawn("IC-22", ShipClass.Interceptor, Faction.Blue, new(470, 70, 905), Quaternion.Identity);
        flagship.AddChild(DroneSwarm.Create(flagship.Palette, 48, 650f, 1000f, seed: 5));

        // 적: 150km 전방. 이 거리에선 전함도 몇 픽셀짜리 점이다.
        var facing = new Quaternion(Vector3.Up, Mathf.Pi);
        var anchor = new Vec3d(3000, 22000, -150000);
        ShipView enemyFlagship = Spawn("BB-X1", ShipClass.Battleship, Faction.Red, anchor, facing);
        Spawn("DD-X1", ShipClass.Escort, Faction.Red, anchor + new Vec3d(-2200, 400, 1500), facing);
        Spawn("DD-X2", ShipClass.Escort, Faction.Red, anchor + new Vec3d(2400, -600, 1800), facing);
        Spawn("IC-X1", ShipClass.Interceptor, Faction.Red, anchor + new Vec3d(-600, 300, 9000), facing);
        Spawn("IC-X2", ShipClass.Interceptor, Faction.Red, anchor + new Vec3d(500, 200, 9200), facing);
        enemyFlagship.AddChild(DroneSwarm.Create(enemyFlagship.Palette, 32, 650f, 1000f, seed: 9));

        foreach (ShipView view in Views)
            if (view.Body.Faction == Faction.Blue)
                _playable.Add(view);
    }

    private ShipView Spawn(string callsign, ShipClass shipClass, Faction faction, Vec3d position, Quaternion orientation)
    {
        var body = World.Add(new ShipBody(callsign, shipClass, faction));
        body.Place(position, orientation);
        var view = ShipView.Create(body, seed: Views.Count * 31 + 7);
        _worldRoot.AddChild(view);
        Views.Add(view);
        return view;
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
            AmbientLightEnergy = 0.3f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            GlowEnabled = true,
            GlowIntensity = 0.7f,
            GlowBloom = 0.02f,
            GlowHdrThreshold = 1.4f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive,
        };
        AddChild(new WorldEnvironment { Name = "Environment", Environment = env });

        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightEnergy = 2.4f,
            LightColor = new Color(1f, 0.95f, 0.88f),
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 6000f,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            Basis = Basis.LookingAt(new Vector3(-0.75f, -0.35f, -0.35f).Normalized(), Vector3.Up),
        };
        AddChild(sun);

        // 행성 반사광 역할의 약한 보조광(하늘에 원반은 그리지 않는다)
        var fill = new DirectionalLight3D
        {
            Name = "PlanetFill",
            LightEnergy = 0.18f,
            LightColor = new Color(0.55f, 0.62f, 0.85f),
            SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly,
            Basis = Basis.LookingAt(new Vector3(0.6f, 0.55f, 0.3f).Normalized(), Vector3.Up),
        };
        AddChild(fill);
    }

    private void BuildPlanet()
    {
        _planet = new MeshInstance3D
        {
            Name = "Planet",
            Mesh = new SphereMesh { Radius = PlanetRadius, Height = PlanetRadius * 2f, RadialSegments = 128, Rings = 64 },
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/gas_giant.gdshader") },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Rotation = new Vector3(0.35f, 0f, 0.25f),
        };
        _worldRoot.AddChild(_planet);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured:
                Camera.AddMouse(motion.Relative);
                return;
            case InputEventMouseButton { Pressed: true } button:
                if (button.ButtonIndex == MouseButton.WheelUp)
                    Camera.Zoom(0.9f);
                else if (button.ButtonIndex == MouseButton.WheelDown)
                    Camera.Zoom(1.1f);
                else if (button.ButtonIndex == MouseButton.Left)
                    Input.MouseMode = Input.MouseModeEnum.Captured;
                return;
        }

        if (e.IsActionPressed(InputSetup.SwitchShip))
            SelectControl(_playable[(_controlledIndex + 1) % _playable.Count].Body.Callsign);
        else if (e.IsActionPressed(InputSetup.ToggleOrigin))
            FloatingOrigin = !FloatingOrigin;
        else if (e.IsActionPressed(InputSetup.JumpFar))
            JumpFriendlies();
        else if (e.IsActionPressed(InputSetup.FlightAssist))
            _flightAssist = !_flightAssist;
        else if (e.IsActionPressed(InputSetup.AssistStyle))
            AssistStyle = AssistStyle == AssistStyle.Aircraft ? AssistStyle.Space : AssistStyle.Aircraft;
        else if (e.IsActionPressed(InputSetup.ThrottleZero))
            Throttle = 0f;
        else if (e.IsActionPressed(InputSetup.ReleaseMouse))
            Input.MouseMode = Input.MouseModeEnum.Visible;
        else if (e.IsActionPressed(InputSetup.InspectTarget))
            NextInspectTarget();
        else if (e.IsActionPressed(InputSetup.TestFire))
            FireTest(RenderOrigin + Vec3d.From(Camera.Position), Camera.AimForward);
        else if (e.IsActionPressed(InputSetup.ToggleHelp))
            ShowHelp = !ShowHelp;
        else if (e.IsActionPressed(InputSetup.ShowModules))
            ShowModules = !ShowModules;
        else if (e.IsActionPressed(InputSetup.Repair))
        {
            foreach (ShipBody ship in World.Ships) ship.Damage.Reset();
            LastTestShot = null;
            World.ResetWeapons();
            LastFireMessage = "전체 복구";
        }
        else if (e.IsActionPressed(InputSetup.FireAssist))
            FireAssist = !FireAssist;
        else if (e.IsActionPressed(InputSetup.Practice))
            SetupPractice();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)SimWorld.TickDelta;
        Throttle = Mathf.Clamp(Throttle + Input.GetAxis(InputSetup.ThrottleDown, InputSetup.ThrottleUp) * dt * 0.6f, -0.3f, 1f);

        ShipBody? player = Controlled?.Body;
        foreach (ShipBody ship in World.Ships)
        {
            ship.Control = ship == _practiceTarget && ship != player
                ? new ShipControl { FlightAssist = false }
                : ship != player
                ? ShipControl.Idle
                : new ShipControl
                {
                    Thrust = new Vector3(
                        Input.GetAxis(InputSetup.StrafeLeft, InputSetup.StrafeRight),
                        Input.GetAxis(InputSetup.StrafeDown, InputSetup.StrafeUp),
                        Throttle),
                    Roll = Input.GetAxis(InputSetup.RollLeft, InputSetup.RollRight),
                    Boost = Input.IsActionPressed(InputSetup.Boost),
                    FlightAssist = _flightAssist,
                    Style = AssistStyle,
                    AimForward = Camera.AimForward,
                };
        }

        World.Step();
        StepCombat();
        if (_shot?.DamageTarget is not null && _testShots < _shot.Pulses && World.Tick >= 30 + _testShots * 12)
        {
            FireTest(_testOrigin, _testDirection);
            _testShots++;
        }
    }

    public override void _Process(double delta)
    {
        // 스크린샷 모드는 초기화가 실패해도 반드시 끝나야 한다.
        if (_shot is not null && ++_frame >= _shot.Frames)
        {
            SaveShotAndQuit(_shot.Path);
            return;
        }

        if (Controlled is not ShipView controlled)
            return;

        double alpha = Engine.GetPhysicsInterpolationFraction();
        RenderOrigin = FloatingOrigin ? controlled.Body.InterpolatedPosition(alpha) : Vec3d.Zero;

        foreach (ShipView view in Views)
            view.Sync(RenderOrigin, alpha, (float)delta);
        _ballistics.Sync(World, RenderOrigin, alpha);
        PlaceBackdrop(_planet, PlanetPosition - RenderOrigin);

        Camera.Follow(controlled.Body.Class, controlled.Position,
            controlled.Body.InterpolatedOrientation((float)alpha), (float)delta);
        _dust.Sync(RenderOrigin + Vec3d.From(Camera.Position), Camera.Position, controlled.Body.Velocity,
            controlled.Body.Class.CameraDistance * DustBoxPerCameraDistance);
    }

    /// <summary>
    /// 아주 먼 천체를 BackdropDistance 위치로 당기고 같은 비율로 줄인다. 화면상 크기는 같다.
    /// </summary>
    private static void PlaceBackdrop(Node3D node, Vec3d relative)
    {
        const double BackdropDistance = 600_000.0;
        double distance = relative.Length();
        double k = distance > BackdropDistance ? BackdropDistance / distance : 1.0;
        node.Position = (relative * k).ToVector3();
        node.Scale = Vector3.One * (float)k;
    }

    private void SelectControl(string callsign)
    {
        int index = _playable.FindIndex(v => v.Body.Callsign == callsign);
        if (index < 0)
            index = 0;
        _controlledIndex = index;

        ShipBody body = _playable[index].Body;
        Camera.ResetAim(body.Orientation);
        // 현재 전진 속도를 스로틀로 이어받아 전환 직후 급감속하지 않게 한다.
        Throttle = Mathf.Clamp(body.Velocity.Dot(body.Forward) / body.Class.MaxSpeed, -0.3f, 1f);
        // 기본 표적은 가장 가까운 살아 있는 적. 사격통제가 아군을 잡지 않게 한다.
        if (InspectTarget is null || InspectTarget == Controlled)
            InspectTarget = Views
                .Where(v => v.Body.Faction != body.Faction && !v.Body.Damage.Destroyed)
                .OrderBy(v => (v.Body.Position - body.Position).LengthSquared())
                .FirstOrDefault() ?? Views.Find(v => v != Controlled);
    }

    private void NextInspectTarget()
    {
        int start = InspectTarget is null ? -1 : Views.IndexOf(InspectTarget);
        for (int i = 1; i <= Views.Count; i++)
        {
            ShipView candidate = Views[(start + i) % Views.Count];
            if (candidate == Controlled) continue;
            InspectTarget = candidate;
            Vector3 direction = (candidate.Body.Position - Controlled!.Body.Position).ToVector3().Normalized();
            if (direction.LengthSquared() > 0.1f)
                Camera.ResetAim(Basis.LookingAt(direction, Controlled.Body.Up).GetRotationQuaternion());
            break;
        }
    }

    private void FireTest(Vec3d origin, Vector3 direction)
    {
        LastTestShot = World.FireTestShot(Controlled!.Body, origin, direction, new DamagePacket(600f, 1200f, 120f));
        LastTestShotTime = World.Time;
        if (LastTestShot.Target is ShipBody target)
            InspectTarget = Views.Find(v => v.Body == target);
        GD.Print($"test shot: {LastTestShot.Target?.Callsign ?? "miss"} · {LastTestShot.Summary}");
    }

    private void JumpFriendlies()
    {
        foreach (ShipBody ship in World.Ships)
            if (ship.Faction == Faction.Blue)
                ship.Teleport(new Vec3d(JumpDistance, 0, 0));
    }

    private void ApplyShotSetup(ShotRequest shot)
    {
        if (shot.FixedOrigin)
            FloatingOrigin = false;
        if (shot.AircraftStyle)
            AssistStyle = AssistStyle.Aircraft;
        if (shot.Far)
            JumpFriendlies();
        if (shot.Ram is string ramTarget && Views.Find(v => v.Body.Callsign == ramTarget) is ShipView targetShip && targetShip != Controlled)
        {
            // 충돌 검증: 표적 후방에서 같은 자세로 접근한다.
            ShipBody targetBody = targetShip.Body;
            Controlled!.Body.Place(targetBody.Position + Vec3d.From(targetBody.Orientation * Vector3.Back) * targetBody.Class.Length,
                targetBody.Orientation);
            Camera.ResetAim(targetBody.Orientation);
        }
        if (shot.DamageTarget is string damageTarget && Views.Find(v => v.Body.Callsign == damageTarget) is ShipView damageView && damageView != Controlled)
        {
            ModuleDefinition module = Array.Find(damageView.Body.Definition.Modules, m => m.Id == (shot.DamageModule ?? "bus-port"))
                ?? throw new ArgumentException($"Unknown damage module: {shot.DamageModule ?? "bus-port"}");
            Vector3 localOrigin = module.Center + Vector3.Left * damageView.Body.Class.Length + Vector3.Forward * damageView.Body.Class.Length;
            _testOrigin = damageView.Body.Position + Vec3d.From(damageView.Body.Orientation * localOrigin);
            _testDirection = (damageView.Body.Orientation * (module.Center - localOrigin)).Normalized();
            Quaternion orientation = Basis.LookingAt(_testDirection, damageView.Body.Up).GetRotationQuaternion();
            Controlled!.Body.Place(_testOrigin, orientation);
            Camera.ResetAim(orientation);
            InspectTarget = damageView;
            ShowModules = true;
        }
        Throttle = shot.Throttle;
        Controlled!.Body.Velocity = Controlled.Body.Forward * shot.Speed;
        if (shot.BallisticsTarget is not null) SetupPractice(shot.BallisticsTarget, shot.TestDistance, shot.TargetSpeed);
        FireAssist = !shot.ManualFire;
        Camera.Zoom(shot.Zoom);
        if (shot.LookAt is string target && Views.Find(v => v.Body.Callsign == target) is ShipView targetView)
        {
            ShipBody me = Controlled!.Body;
            Vector3 dir = (targetView.Body.Position - me.Position).ToVector3().Normalized();
            Camera.ResetAim(Basis.LookingAt(dir, me.Up).GetRotationQuaternion());
        }
        Camera.Turn(shot.Yaw, shot.Pitch);
    }

    private void SaveShotAndQuit(string path)
    {
        if (_shot?.BallisticsTarget is not null)
            GD.Print($"railgun test: shots={_liveShots}, recent hits={World.Impacts.Count}, active={World.Projectiles.Count}, target shield={_practiceTarget?.Damage.Shield:0}, damaged modules={_practiceTarget?.Damage.Modules.Count(m => m.HealthFraction < 1)}");
        Image image = GetViewport().GetTexture().GetImage();
        Error err = image.SavePng(path);
        GD.Print(err == Error.Ok ? $"shot saved: {path}" : $"shot failed: {err}");
        GetTree().Quit();
    }

    /// <summary>
    /// 명령줄 스크린샷 모드. 예: -- --shot=C:/tmp/a.png --control=IC-21 --look-at=BB-01 --frames=120
    /// </summary>
    private sealed record ShotRequest(
        string Path, int Frames, string? Control, string? LookAt, string? Ram, string? DamageTarget, string? DamageModule, int Pulses,
        string? BallisticsTarget, float TestDistance, float TargetSpeed, bool ManualFire,
        float Yaw, float Pitch, float Throttle, float Speed, float Zoom, bool Far, bool FixedOrigin, bool AircraftStyle)
    {
        public static ShotRequest? Parse(string[] args)
        {
            var map = new Dictionary<string, string>();
            foreach (string arg in args)
            {
                string a = arg.TrimStart('-');
                int eq = a.IndexOf('=');
                map[eq < 0 ? a : a[..eq]] = eq < 0 ? "true" : a[(eq + 1)..];
            }
            if (!map.TryGetValue("shot", out string? path))
                return null;

            float F(string key, float fallback) =>
                map.TryGetValue(key, out string? v) ? float.Parse(v, CultureInfo.InvariantCulture) : fallback;

            float yaw = 0, pitch = 0;
            if (map.TryGetValue("turn", out string? turn))
            {
                string[] parts = turn.Split(',');
                yaw = float.Parse(parts[0], CultureInfo.InvariantCulture);
                pitch = parts.Length > 1 ? float.Parse(parts[1], CultureInfo.InvariantCulture) : 0f;
            }

            return new ShotRequest(
                path,
                (int)F("frames", 120),
                map.GetValueOrDefault("control"),
                map.GetValueOrDefault("look-at"),
                map.GetValueOrDefault("ram"),
                map.GetValueOrDefault("damage-test"),
                map.GetValueOrDefault("module"),
                (int)F("pulses", 8),
                map.GetValueOrDefault("ballistics-test"),
                F("test-distance", 6000),
                F("target-speed", 180),
                map.ContainsKey("manual-fire"),
                yaw, pitch,
                F("throttle", 0f),
                F("speed", 0f),
                F("zoom", 1f),
                map.ContainsKey("far"),
                map.ContainsKey("fixed-origin"),
                map.GetValueOrDefault("style") == "aircraft");
        }
    }
}
