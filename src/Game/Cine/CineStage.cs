using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game.Cine;

/// <summary>
/// 티저 한 장면의 무대. 시뮬레이션 월드와 함선 화면을 함께 들고, 연출용 배치·사격 도우미를 준다.
/// 판정은 실제 시뮬레이션이 한다(탄도·미사일·근접방어·피해). 연출은 배치와 카메라만 고른다.
/// </summary>
public sealed class CineStage
{
    public SimWorld World { get; } = new();
    public Node3D Root { get; }
    public List<ShipView> Views { get; } = new();
    public BallisticsView Ballistics { get; }
    public OrdnanceView Ordnance { get; }
    /// <summary>게임과 같은 폭발·연기·잔해 연출.</summary>
    public BattleFx Fx { get; }
    /// <summary>근접 촬영용 프로브 외형(장면이 Probe를 지정할 때만 보인다).</summary>
    public CineProbe ProbeModel { get; }
    /// <summary>연출용 소리 사건(종류, 위치, 크기). 감독이 소리 기록기에 연결한다.</summary>
    public Action<string, Vec3d, float>? Sound { get; set; }
    /// <summary>이번 프레임 보간 비율(두 틱 사이).</summary>
    public double Alpha { get; set; }
    /// <summary>장면 시작 후 영상 시간(초).</summary>
    public double Local { get; set; }

    public CineStage(Node3D parent)
    {
        Root = new Node3D { Name = "Stage" };
        parent.AddChild(Root);
        Ballistics = new BallisticsView { Name = "Ballistics" };
        Root.AddChild(Ballistics);
        Ordnance = new OrdnanceView { Name = "Ordnance" };
        Root.AddChild(Ordnance);
        Fx = new BattleFx { Name = "BattleFx" };
        Root.AddChild(Fx);
        ProbeModel = new CineProbe { Name = "ProbeModel" };
        Root.AddChild(ProbeModel);
        ProbeModel.Pinged += at => Sound?.Invoke("ping", at, 40);
    }

    public void Dispose()
    {
        Root.GetParent()?.RemoveChild(Root);
        Root.QueueFree();
    }

    public ShipBody Ship(string callsign) => Views.First(v => v.Body.Callsign == callsign).Body;

    /// <summary>함선 하나를 놓는다. heading은 전방(-Z)이 향할 방향, 속도는 전방 기준 m/s.</summary>
    public ShipBody Spawn(string callsign, ShipClass cls, Faction faction, Vec3d at, Vector3 heading, float speed = 0, float roll = 0, bool drones = true)
        // 청군은 지구 설계, 적군은 화성 설계(아레스·솔리스·로웰).
        => Place(callsign, ShipDefinitions.For(cls.Kind, faction == Faction.Red ? DesignFamily.Mars : DesignFamily.Earth), faction, at, heading, speed, roll, drones);

    /// <summary>함급 ID로 놓는다(T3~T5 변형·순양함). 공장 기본 무장(방공 순양함의 레이저 등)까지 합성한다.</summary>
    public ShipBody Spawn(string callsign, string hullId, Faction faction, Vec3d at, Vector3 heading, float speed = 0, float roll = 0, bool drones = true)
        => Place(callsign, ShipVariants.All.Any(v => v.Node.Id == hullId) ? ShipVariants.Resolve(hullId).Definition
            : ShipLoadouts.Resolve(new(hullId)).Definition, faction, at, heading, speed, roll, drones);

    private ShipBody Place(string callsign, ShipDefinition definition, Faction faction, Vec3d at, Vector3 heading, float speed, float roll, bool drones)
    {
        var body = World.Add(new ShipBody(callsign, definition, faction));
        Quaternion q = Basis.LookingAt(heading.Normalized(), Vector3.Up).GetRotationQuaternion();
        if (roll != 0) q = q * new Quaternion(Vector3.Forward, Mathf.DegToRad(roll));
        body.Place(at, q);
        body.Velocity = body.Forward * speed;
        // 연출 기본: 보조 OFF로 관성 비행(속도를 지킨다).
        body.Control = new ShipControl { FlightAssist = false };
        ShipView view = ShipView.Create(body, seed: Views.Count * 31 + 7);
        Root.AddChild(view);
        Views.Add(view);
        if (drones && body.Definition.DefenseDrones is not null)
            view.AddChild(DroneSwarm.Create(body, view.Palette));
        return body;
    }

    /// <summary>시뮬레이션 좌표에 고정된 배경 소품(정박지 등). 연출이 매 프레임 렌더 원점에 맞춰 옮긴다.</summary>
    public List<(Node3D Node, Vec3d At, Quaternion Rotation)> Props { get; } = new();

    /// <summary>격납고 미리보기와 같은 궤도 정박지를 함선 둘레에 세운다(함선 크기에 비례).</summary>
    public void Dock(ShipBody ship)
    {
        ShipView view = Views.First(v => v.Body == ship);
        Transform3D toShip = view.GlobalTransform.AffineInverse();
        Aabb bounds = default; bool first = true;
        foreach (var mesh in ImportedShipModels.Meshes(view))
        {
            if (!mesh.IsVisibleInTree()) continue;
            Aabb local = mesh.GetAabb();
            for (int i = 0; i < 8; i++)
            {
                Vector3 point = toShip * (mesh.GlobalTransform * local.GetEndpoint(i));
                bounds = first ? new Aabb(point, Vector3.Zero) : bounds.Expand(point); first = false;
            }
        }
        var dock = new Node3D { Name = "Dock" };
        HangarPreview.BuildDock(dock, bounds, ship.Definition.Flight.Length, planet: false);
        // 전투 조명(강한 태양) 아래에서는 격납고용 강판이 하얗게 떠 보인다. 어둡고 거칠게 바꾼다(재질은 이 정박지 전용).
        var dimmed = new HashSet<StandardMaterial3D>();
        foreach (var box in dock.GetChildren().OfType<MeshInstance3D>())
            if (box.MaterialOverride is StandardMaterial3D m && m.ShadingMode != BaseMaterial3D.ShadingModeEnum.Unshaded && dimmed.Add(m))
            { m.AlbedoColor *= 0.4f; m.Metallic = 0.25f; m.Roughness = 0.9f; }
        Root.AddChild(dock);
        Props.Add((dock, ship.Position, ship.Orientation));
    }

    /// <summary>보간된 위치(카메라 계산용).</summary>
    public Vec3d At(ShipBody ship) => ship.InterpolatedPosition(Alpha);
    public Quaternion Orientation(ShipBody ship) => ship.InterpolatedOrientation((float)Alpha);
    /// <summary>함선 로컬 좌표 → 월드(보간).</summary>
    public Vec3d Local3(ShipBody ship, Vector3 local) => At(ship) + Vec3d.From(Orientation(ship) * local);

    /// <summary>자동 사격통제(집중)로 표적을 쏘게 한다. 포탑 선회·잠금·선행 계산은 실제 사격통제가 한다.</summary>
    public void Focus(ShipBody shooter, ShipBody target) =>
        shooter.Gunnery = new GunneryOrder { Doctrine = FireDoctrine.Focus, Target = target };

    /// <summary>
    /// 포탑마다 선행 조준점을 겨누고, 장전·정렬된 포탑은 쏜다. 매 시뮬레이션 틱 부른다(포탑 선회는 실제 구동 속도).
    /// 마지막 실패 사유를 돌려준다(확인용).
    /// </summary>
    public FireFailure Volley(ShipBody shooter, ShipBody target)
    {
        if (shooter.Damage.Destroyed || target.Damage.Destroyed) return FireFailure.NotReady;
        FireFailure last = FireFailure.None;
        foreach (RailgunState gun in shooter.Railguns)
        {
            Vec3d muzzle = gun.MuzzlePosition;
            double distance = (target.Position - muzzle).Length();
            double flight = distance / gun.Definition.MuzzleSpeed;
            Vec3d aim = target.Position + Vec3d.From((target.Velocity - shooter.Velocity) * (float)flight);
            FireAttempt shot = World.FireRailgun(shooter, gun, (aim - muzzle).ToVector3());
            if (!shot.Fired) last = shot.Failure;
        }
        return last;
    }

    private readonly List<(ShipBody Shooter, ShipBody Target)> _duels = new();
    /// <summary>장면 내내 표적을 쏘게 한다(틱마다 Volley). 한 함선은 표적 하나만 가진다.</summary>
    public void Duel(ShipBody shooter, ShipBody target)
    {
        _duels.RemoveAll(d => d.Shooter == shooter);
        _duels.Add((shooter, target));
    }
    public void StepDuels() { foreach (var (a, b) in _duels) Volley(a, b); }

    /// <summary>미사일 한 발. 연출용으로 장전을 건너뛴다.</summary>
    public bool Missile(ShipBody shooter, ShipBody target)
    {
        shooter.Ordnance.Reset();
        return World.LaunchMissile(shooter, target).Fired;
    }

    private readonly HashSet<string> _cues = new();
    /// <summary>장면 시간 t가 at을 처음 넘은 프레임에 한 번만 참.</summary>
    public bool Cue(string key, double t, double at) => t >= at && _cues.Add(key);

    public void Settle(double seconds)
    {
        for (int i = 0; i < seconds * SimWorld.TickRate; i++) { StepDuels(); World.Step(); }
    }
}

/// <summary>카메라 자세(시뮬레이션 좌표).</summary>
public readonly record struct CamPose(Vec3d Position, Vec3d Target, Vector3 Up, float Fov)
{
    public static CamPose Look(Vec3d from, Vec3d to, float fov = 50, Vector3? up = null) => new(from, to, up ?? Vector3.Up, fov);
}

/// <summary>장면 하나. 같은 무대를 이어 쓰면(Continue) 앞 장면의 월드가 그대로 이어진다.</summary>
public sealed class CineShot
{
    public required string Name { get; init; }
    public required double Duration { get; init; }
    public bool Continue { get; init; }
    /// <summary>검은 화면 장면(글자·그래픽만).</summary>
    public bool Card { get; init; }
    /// <summary>원경 행성을 보일지(모든 장면에 같은 방향으로 나오면 단조로워서 골라 쓴다).</summary>
    public bool Planet { get; init; }
    public double FadeIn { get; init; } = 0.4;
    public double FadeOut { get; init; } = 0.4;
    /// <summary>시작 전에 미리 돌릴 시뮬레이션 시간(초).</summary>
    public double PreRoll { get; init; }
    /// <summary>국면 표시(0이면 숨김, 1~5).</summary>
    public int Phase { get; init; }
    public Action<CineStage>? Setup { get; init; }
    public Action<CineStage, double>? Tick { get; init; }
    public Func<CineStage, double, CamPose>? Camera { get; init; }
    /// <summary>영상 1초에 진행할 시뮬레이션 초(슬로모션 곡선).</summary>
    public Func<double, double> Rate { get; init; } = _ => 1;
    /// <summary>게임 전과 배너(조준점 위): 시각, 제목, 설명, 색.</summary>
    public (double At, string Title, string Detail, Color Tint)[] Banners { get; init; } = Array.Empty<(double, string, string, Color)>();
    /// <summary>게임 전과 리본(조준점 아래): 시각마다 해당 리본이 튀어나오고 같은 이름이면 ×N으로 쌓인다.</summary>
    public (double At, string Label, Color Tint)[] Ribbons { get; init; } = Array.Empty<(double, string, Color)>();
    /// <summary>화면 아래 짧은 자막(시작, 끝, 글).</summary>
    public (double From, double To, string Text, string Sub)[] Captions { get; init; } = Array.Empty<(double, double, string, string)>();
}

public static class CineMath
{
    public static double Smooth(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
    public static double Ease(double t) { t = Math.Clamp(t, 0, 1); return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2; }
    public static Vec3d Lerp(Vec3d a, Vec3d b, double t) => Vec3d.Lerp(a, b, t);
    public static Vec3d V(double x, double y, double z) => new(x, y, z);
    /// <summary>손으로 든 듯한 작은 흔들림(결정적).</summary>
    public static Vector3 Drift(double t, float amount) => new(
        (float)(Math.Sin(t * 0.73) + Math.Sin(t * 1.91 + 1.3) * 0.5) * amount,
        (float)(Math.Sin(t * 0.61 + 2.1) + Math.Sin(t * 1.37 + 0.4) * 0.5) * amount,
        (float)(Math.Sin(t * 0.47 + 0.7)) * amount);
}
