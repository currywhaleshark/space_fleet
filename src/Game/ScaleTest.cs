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
    private OrdnanceView _ordnance = null!;
    private BattleFx _battleFx = null!;
    private CombatAudio _audio = null!;
    public CombatFeedback Feedback => _audio.Feedback;
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
        _shot = ShotRequest.Parse(OS.GetCmdlineUserArgs());
        BuildEnvironment();

        _worldRoot = new Node3D { Name = "World" };
        AddChild(_worldRoot);
        SpawnFleets();
        BuildPlanet();
        _ballistics = new BallisticsView { Name = "Ballistics" };
        _worldRoot.AddChild(_ballistics);
        _ordnance = new OrdnanceView { Name = "Ordnance" };
        _worldRoot.AddChild(_ordnance);
        _battleFx = new BattleFx { Name = "BattleFx", Enabled = !BattleArgs.Parse(OS.GetCmdlineUserArgs()).ContainsKey("no-battle-fx") };
        _worldRoot.AddChild(_battleFx);
        _audio = new CombatAudio { Name = "CombatAudio" };
        AddChild(_audio);
        _audio.SetPaused(Paused);

        Camera = new ChaseCamera { Name = "Camera" };
        Camera.TelescopeChanged += OnTelescopeChanged;
        AddChild(Camera);
        Camera.MakeCurrent();
        Feedback.Received += hit => Camera.AddImpact(hit.SourceDirection, hit.Strength, hit.Target.Class.Kind);
        _dust = SpaceDust.Create(seed: 11);
        AddChild(_dust);

        var layer = new CanvasLayer { Name = "HudLayer" };
        _hud = new Hud { Name = "Hud", Game = this };
        layer.AddChild(_hud);
        _radial = new RadialMenu { Name = "RadialMenu" };
        layer.AddChild(_radial);
        AddChild(layer);

        CreateWorldMap();

        SelectControl(LaunchControl ?? _shot?.Control ?? "IC-21");
        // 피해·충돌 검증은 표적이 움직이면 안 되므로 AI를 끈다(--no-ai로도 끌 수 있다).
        SetupAI(disabled: _shot is { NoAi: true } || _shot?.DamageTarget is not null || _shot?.Ram is not null);
        if (_shot is not null)
            ApplyShotSetup(_shot);
        if (_shot is not null && BattleArgs.Parse(OS.GetCmdlineUserArgs()).TryGetValue("am-demo",out var amDemo))
            SetupAntimatterPractice(amDemo);
        var perfArgs = BattleArgs.Parse(OS.GetCmdlineUserArgs());
        if (AutoPlay) {EnableAutoPlay();SetBattleSpeed(int.Parse(perfArgs.GetValueOrDefault("autoplay-speed", "4")));}
        if (perfArgs.ContainsKey("perf-trace"))
            PerfTrace.Enable(double.Parse(perfArgs.GetValueOrDefault("perf-seconds", "1e9"), CultureInfo.InvariantCulture),
                profileSim: !perfArgs.ContainsKey("perf-no-sim-profile"));
        else PerfTrace.Disable();
        UpdateContacts();
        if (BattleArgs.Parse(OS.GetCmdlineUserArgs()).ContainsKey("full-map")) ToggleWorldMap();
        if (_shot is not null && BattleArgs.Parse(OS.GetCmdlineUserArgs()).ContainsKey("telescope")) Camera.SetTelescope(true);
    }

    /// <summary>
    /// 진영마다 편대 셋: 전투단(전함+호위함 2), 호위 전대(호위함 4), 요격 편대(요격함 5). 적은 150 km 앞에서 마주 본다.
    /// 호출부호 — 아군: BB-01·DD-11·DD-12 / DD-31~34 / IC-21~25, 적: BB-X1·DD-X1·DD-X2 / DD-X3~X6 / IC-X1~X5.
    /// </summary>
    private void SpawnFleets()
    {
        var args = BattleArgs.Parse(OS.GetCmdlineUserArgs());
        BattleRoster roster = BattleSetup.Spawn(World, LaunchConfig ?? new BattleConfig { Seed = _shot?.Seed ?? int.Parse(args.GetValueOrDefault("seed","0")) });
        foreach (ShipBody body in roster.Ships)
        {
            ShipView view = ShipView.Create(body, seed: Views.Count * 31 + 7);
            _worldRoot.AddChild(view);
            Views.Add(view);
            if (body.Faction == Faction.Blue) _playable.Add(view);
        }
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
        if (e.IsActionReleased(InputSetup.Fire)) _fireReleaseGuard = false;
        if (e.IsActionReleased(InputSetup.Telescope)) Camera.SetTelescope(false);
        if (WorldMapOpen) { _worldMap.HandleInput(e); return; }
        if (Paused || Spectating) return;
        if (e.IsActionPressed(InputSetup.WorldMap))
        { ToggleWorldMap(); GetViewport().SetInputAsHandled(); return; }
        if (_mapReleaseGuard && e is InputEventMouseButton or InputEventMouseMotion)
        { GetViewport().SetInputAsHandled(); return; }
        if (HandleRadialInput(e)) return;
        if (Camera.TelescopeHeld && e.IsActionPressed(InputSetup.ReleaseMouse))
        { Camera.ResetTelescope(); Input.MouseMode=Input.MouseModeEnum.Visible; _fireReleaseGuard=true; GetViewport().SetInputAsHandled(); return; }
        if (HandleBattleInput(e)) return;
        if (HandleRadarInput(e)) return;
        if (HandleWeaponMouse(e)) return;
        switch (e)
        {
            case InputEventMouseMotion motion when Camera.TelescopeHeld || (Scheme == ControlScheme.Pilot && Input.MouseMode == Input.MouseModeEnum.Captured) || Camera.FreeLooking:
                Camera.AddMouse(motion.Relative);
                return;
            case InputEventMouseButton button when button.ButtonIndex == MouseButton.Middle && Scheme == ControlScheme.Helm:
                if (Camera.TelescopeHeld) return;
                Camera.SetFreeLook(button.Pressed);
                return;
            case InputEventMouseButton { Pressed: true } button when button.ButtonIndex != MouseButton.Right:
                if (button.ButtonIndex == MouseButton.WheelUp)
                    Camera.Zoom(0.9f);
                else if (button.ButtonIndex == MouseButton.WheelDown)
                    Camera.Zoom(1.1f);
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
        else if (e.IsActionPressed(InputSetup.ReleaseMouse) && Scheme == ControlScheme.Pilot)
        { Input.MouseMode = Input.MouseModeEnum.Visible; Camera.ResetTelescope(); }
        else if (e.IsActionPressed(InputSetup.InspectTarget))
        { if (Camera.TelescopeHeld) SelectScopeTarget(); else NextInspectTarget(); }
        else if (e.IsActionPressed(InputSetup.TestFire))
            FireTest(RenderOrigin + Vec3d.From(Camera.Position), Camera.AimForward);
        else if (e.IsActionPressed(InputSetup.ToggleHelp))
            ShowHelp = !ShowHelp;
        else if (e.IsActionPressed(InputSetup.ShowModules))
            ShowModules = !ShowModules;
        else if (e.IsActionPressed(InputSetup.Repair))
        {
            foreach (ShipBody ship in World.Ships)
            {
                ship.Damage.Reset();
                ship.Power.Reset();
            }
            LastTestShot = null;
            ResumeBrains();
            World.ResetWeapons();
            LastFireMessage = "전체 복구";
        }
        else if (e.IsActionPressed(InputSetup.FireAssist))
        {
            if (Scheme == ControlScheme.Helm) CycleDoctrine();
            else FireAssist = !FireAssist;
        }
        else if (e.IsActionPressed(InputSetup.Practice))
            SetupPractice();
        else if (e.IsActionPressed(InputSetup.MissileDrill))
            SetupMissileDrill();
        else if (e.IsActionPressed(InputSetup.Decoys))
            LaunchDecoys();
        else if (e.IsActionPressed(InputSetup.SelectMainGun))
            SelectWeapon(PlayerWeapon.MainGun);
        else if (e.IsActionPressed(InputSetup.SelectMissile))
            SelectWeapon(PlayerWeapon.Missile);
        else if (e.IsActionPressed(InputSetup.SelectAntimatter))
            SelectWeapon(PlayerWeapon.Antimatter);
        else if (e.IsActionPressed(InputSetup.AimPart))
            CycleAimPart();
        else if (e.IsActionPressed(InputSetup.OrderAttack))
            IssueOrder(OrderKind.Attack);
        else if (e.IsActionPressed(InputSetup.OrderEscort))
            IssueOrder(OrderKind.Escort);
        else if (e.IsActionPressed(InputSetup.OrderHold))
            IssueOrder(OrderKind.Hold);
        else if (e.IsActionPressed(InputSetup.TimeSlower))
            CycleTimeScale(-1);
        else if (e.IsActionPressed(InputSetup.TimeFaster))
            CycleTimeScale(1);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Paused) return;
        StepBattleFlow(delta);
        float dt = (float)SimWorld.TickDelta;
        if (!AutoPlay && !MapControlsBlocked) Throttle = Mathf.Clamp(Throttle + Input.GetAxis(InputSetup.ThrottleDown, InputSetup.ThrottleUp) * dt * 0.6f, -0.3f, 1f);

        // 시간 배속: 한 물리 틱에 시뮬레이션을 여러 번 진행한다.
        long physics = PerfTrace.Begin();
        for (int step = 0; step < TimeScale; step++)
            StepOnce();
        PerfTrace.End("physics", physics);
    }

    private void StepOnce()
    {
        ShipBody? player = Controlled?.Body;
        foreach (ShipBody ship in World.Ships)
        {
            // AI가 켜진 함선은 World.Step 안에서 AI가 조종 입력을 쓴다.
            if ((AutoPlay || ship != player) && World.BrainOf(ship) is { Enabled: true })
                continue;
            ship.Control = ship == _practiceTarget && ship != player
                ? new ShipControl { FlightAssist = false }
                : ship != player
                ? ShipControl.Idle
                : PlayerControl();
        }

        if (Gunnery is { } order) order.AimPart = AimPart;
        if (!AutoPlay) StepSquadCommand();
        long t = PerfTrace.Begin();
        World.Step();
        PerfTrace.End("sim", t); t = PerfTrace.Begin();
        _battleFx.Observe(World, RenderOrigin + Vec3d.From(Camera.Position));
        PerfTrace.End("fxObserve", t); t = PerfTrace.Begin();
        UpdateContacts();
        PerfTrace.End("contacts", t); t = PerfTrace.Begin();
        if (!AutoPlay && !Spectating) StepCombat();
        StepAntimatterPreview();
        PerfTrace.End("combat+am", t);
        if (_shot?.DamageTarget is not null && _testShots < _shot.Pulses && World.Tick >= 30 + _testShots * 12)
        {
            FireTest(_testOrigin, _testDirection);
            _testShots++;
        }
        t = PerfTrace.Begin();
        if (!Spectating) _audio.Observe(World, Controlled?.Body);
        PerfTrace.End("audioObserve", t);
    }

    public override void _Process(double delta)
    {
        PerfTrace.Frame(this, World.Time);
        UpdateMapInputGuard();
        if (!Input.IsActionPressed(InputSetup.Fire) && !Input.IsMouseButtonPressed(MouseButton.Left)) _fireReleaseGuard = false;
        else if (Paused || MenuOpen || WorldMapOpen || RadarPointerCaptured) _fireReleaseGuard = true;
        StepShotRadial();
        PreviewFeedback();
        PreviewLostContact();
        // 스크린샷 모드는 초기화가 실패해도 반드시 끝나야 한다.
        if (!BattleMode && _shot is not null && ++_frame >= _shot.Frames)
        {
            SaveShotAndQuit(_shot.Path);
            return;
        }

        if (Controlled is null)
            return;
        ShipView controlled = CameraView;

        double alpha = Engine.GetPhysicsInterpolationFraction();
        RenderOrigin = FloatingOrigin ? controlled.Body.InterpolatedPosition(alpha) : Vec3d.Zero;

        long p = PerfTrace.Begin();
        foreach (ShipView view in Views)
            view.Sync(RenderOrigin, alpha, (float)delta);
        PerfTrace.End("shipViews", p);
        PlaceBackdrop(_planet, PlanetPosition - RenderOrigin);

        float feedbackDelta = Paused ? 0 : (float)delta;
        Feedback.Advance(feedbackDelta);
        Camera.AdvanceImpacts(feedbackDelta, FeedbackSettings.Shake / 100f);
        Camera.Follow(controlled.Body.Definition, controlled.Position,
            controlled.Body.InterpolatedOrientation((float)alpha), Paused ? 0 : (float)delta);
        p = PerfTrace.Begin();
        _audio.SyncListener(RenderOrigin + Vec3d.From(Camera.Position), Camera.GlobalBasis);
        PerfTrace.End("audioListener", p); p = PerfTrace.Begin();
        _ballistics.Sync(World,RenderOrigin,alpha,Camera,Views);
        PerfTrace.End("ballistics", p); p = PerfTrace.Begin();
        foreach(var view in Views) view.SyncCombat(World,Camera);
        PerfTrace.End("shipCombat", p); p = PerfTrace.Begin();
        _ordnance.Sync(World, RenderOrigin, alpha, Camera.Position,Camera);
        PerfTrace.End("ordnance", p); p = PerfTrace.Begin();
        _battleFx.Sync(RenderOrigin, World.Time + alpha * SimWorld.TickDelta, Camera);
        PerfTrace.End("fxSync", p);
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
        ShipBody? previous = _playable.Count > 0 ? _playable[_controlledIndex].Body : null;
        previous?.Ordnance.Antimatter.Cancel();
        SelectedWeapon=PlayerWeapon.MainGun; JettisonProgress=0;
        _fireReleaseGuard=Input.IsActionPressed(InputSetup.Fire) || Input.IsMouseButtonPressed(MouseButton.Left);
        _controlledIndex = index;

        ShipBody body = _playable[index].Body;
        _audio.Prime(World, body);
        StartRecord(body);
        HandOverControl(previous, body);
        SetupGunnery(previous, body);
        SetControlScheme(body);
        // 현재 전진 속도를 스로틀로 이어받아 전환 직후 급감속하지 않게 한다.
        Throttle = Mathf.Clamp(body.Velocity.Dot(body.Forward) / body.Class.MaxSpeed, -0.3f, 1f);
        // 기본 표적은 탐지된 가장 가까운 살아 있는 적. 사격통제가 아군을 잡지 않게 한다.
        if (InspectTarget is null || InspectTarget == Controlled)
            InspectTarget = Views
                .Where(v => v.Body.Faction != body.Faction && !v.Body.Damage.Destroyed && Known(v))
                .OrderBy(v => (v.Body.Position - body.Position).LengthSquared())
                .FirstOrDefault() ?? Views.Find(v => v != Controlled);
    }

    private void NextInspectTarget()
    {
        int start = InspectTarget is null ? -1 : Views.IndexOf(InspectTarget);
        for (int i = 1; i <= Views.Count; i++)
        {
            ShipView candidate = Views[(start + i) % Views.Count];
            // 탐지되지 않은 적은 고를 수 없다(아군은 데이터 링크로 항상 안다).
            if (candidate == Controlled || ContactOf(candidate) is null || (Scheme == ControlScheme.Helm && candidate.Body.Faction == Controlled!.Body.Faction)) continue;
            SelectEnemy(candidate);
            Vector3 direction = (ContactOf(candidate)!.DisplayPosition(candidate.SimPosition) - Controlled!.Body.Position).ToVector3().Normalized();
            if (Scheme == ControlScheme.Pilot && direction.LengthSquared() > 0.1f)
                Camera.ResetAim(Basis.LookingAt(direction, Controlled.Body.Up).GetRotationQuaternion());
            break;
        }
    }

    /// <summary>적 전함·호위함은 ECM을 켜고 접근한다(무장·센서 핍 하나씩을 ECM으로). 요격함은 조용히 온다.</summary>
    private static void ApplyDefaultPips(ShipBody ship)
    {
        if (ship.Faction == Faction.Red && ship.Class.Kind != HullKind.Interceptor)
            ship.Power.SetPips(2, 2, 1, 1, 2);
    }

    /// <summary>조종 진영의 센서망 추적.</summary>
    public SensorTrack TrackOf(ShipView view) =>
        World.Sensors.Track(Controlled?.Body.Faction ?? Faction.Blue, view.Body);

    private bool Known(ShipView view) => TrackOf(view).Level > TrackLevel.None;

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
        if (Gunnery is { } order)
        {
            order.Doctrine = Enum.TryParse<FireDoctrine>(shot.Doctrine, true, out var doctrine) ? doctrine : shot.ManualFire ? FireDoctrine.Manual : FireDoctrine.Free;
            if (shot.BallisticsTarget is not null && InspectTarget is { } practice) SelectEnemy(practice);
        }
        FireAssist = !shot.ManualFire;
        // 전력 검증: --pips=추진,실드,무장,센서[,ECM]  --heat=열 비율(0~1.25)
        if (shot.Pips is { Length: 4 or 5 } p)
            Controlled!.Body.Power.SetPips(p[0], p[1], p[2], p[3], p.Length > 4 ? p[4] : 0);
        if (shot.Heat > 0f)
            Controlled!.Body.Power.AddHeat(shot.Heat * Controlled.Body.Definition.Power.HeatCapacityMj);
        if (shot.Drill)
            SetupMissileDrill(shot.DrillDistance);
        SetBattleSpeed(shot.StartTimeScale);
        if (shot.Aim is string part && System.Enum.TryParse(part, ignoreCase: true, out AimSubsystem parsed))
            while (AimPart != parsed) CycleAimPart();
        if (shot.Order is "attack" or "hold" or "escort")
            IssueOrder(shot.Order switch { "attack" => OrderKind.Attack, "hold" => OrderKind.Hold, _ => OrderKind.Escort });
        else if (Enum.TryParse<SquadCommand>(shot.Order, true, out var command)) IssueSquadCommand(command);
        Camera.Zoom(shot.Zoom);
        if (shot.LookAt is string target && Views.Find(v => v.Body.Callsign == target) is ShipView targetView)
        {
            ShipBody me = Controlled!.Body;
            Vector3 dir = (targetView.Body.Position - me.Position).ToVector3().Normalized();
            Camera.Observe(dir, me.Orientation);
        }
        Camera.Turn(shot.Yaw, shot.Pitch);
        SetupShotRadial();
    }

    private void SaveShotAndQuit(string path)
    {
        if (Gunnery is { } order)
            GD.Print($"gunnery: doctrine={order.Doctrine}, engaged={order.Engaged?.Callsign}, status={order.Status}, rounds={Controlled?.Body.Railgun?.Rounds}");
        if (Controlled is { } ship)
            GD.Print("turrets: " + string.Join("; ", ship.Body.Railguns.Select(g => $"{g.Definition.ModuleId} yaw={Mathf.RadToDeg(g.Yaw):0.0} pitch={Mathf.RadToDeg(g.Elevation):0.0} shots={g.ShotCount} rounds={g.Rounds}")));
        if (Controlled?.Body.Ordnance.Antimatter is { Definition: not null } am)
            GD.Print($"AM: {am.Status} rounds={am.Rounds} launches={am.Launches} containment={am.Containment:0.00} jettisoned={am.Jettisoned}");
        if (_shot?.BallisticsTarget is not null)
            GD.Print($"railgun test: shots={_liveShots}, recent hits={World.Impacts.Count}, active={World.Projectiles.Count}, target shield={_practiceTarget?.Damage.Shield:0}, damaged modules={_practiceTarget?.Damage.Modules.Count(m => m.HealthFraction < 1)}");
        if (_shot is { Drill: true } || _shot is { Launch: > 0 })
            GD.Print($"missile test: in flight={World.Missiles.Count}, decoys={World.Decoys.Count}, intercepted={World.OrdnanceEvents.Count(e => e.Kind == OrdnanceEventKind.Intercepted)}, detonations={World.OrdnanceEvents.Count(e => e.Kind == OrdnanceEventKind.Detonation)}, my shield={Controlled?.Body.Damage.Shield:0}");
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
        float Yaw, float Pitch, float Throttle, float Speed, float Zoom, bool Far, bool FixedOrigin, bool AircraftStyle,
        int[]? Pips, float Heat, Vector2 Strafe, float Roll, bool KeepEcm, int Launch, bool Drill, float DrillDistance, bool AutoDecoys,
        bool NoAi, int StartTimeScale, string? Order, string? Aim, bool Stern, float HelmYaw, float HelmPitch, string? Doctrine, float Below,
        string? Radial, float? RadialDirection, int RadialHoldFrames, bool RadialRelease, int Seed)
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
                map.GetValueOrDefault("style") == "aircraft",
                map.TryGetValue("pips", out string? pips) ? pips.Split(',').Select(int.Parse).ToArray() : null,
                F("heat", 0f),
                map.TryGetValue("strafe", out string? strafe) && strafe.Split(',') is { Length: 2 } s
                    ? new Vector2(float.Parse(s[0], CultureInfo.InvariantCulture), float.Parse(s[1], CultureInfo.InvariantCulture))
                    : Vector2.Zero,
                F("roll", 0f),
                map.ContainsKey("keep-ecm"),
                (int)F("launch", 0),
                map.ContainsKey("drill"),
                F("drill-distance", 40_000f),
                map.ContainsKey("auto-decoys"),
                map.ContainsKey("no-ai"),
                (int)F("time-scale", 1),
                map.GetValueOrDefault("order"),
                map.GetValueOrDefault("aim"),
                map.ContainsKey("stern"), F("yaw", 0), F("pitch", 0), map.GetValueOrDefault("doctrine"), F("below", 0),
                map.GetValueOrDefault("radial"), map.ContainsKey("radial-dir") ? F("radial-dir", 0) : null,
                (int)F("radial-hold-frames", 60), map.ContainsKey("radial-release"), (int)F("seed", 0));
        }
    }
}
