using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game.Cine;

/// <summary>
/// 티저 전용 효과: 포구 섬광·명중 불꽃·폭발 불덩이·연기·충격파·파편·순간광·미사일 연기 줄·불타는 잔해.
/// 시뮬레이션 사건을 보고 만든다(판정에는 관여하지 않는다). 시간은 시뮬레이션 기준이라 슬로모션에서 함께 느려진다.
/// 부드러운 원형 텍스처 판을 카메라 쪽으로 돌려 그린다. 소리 합성용 사건 기록도 여기서 남긴다.
/// </summary>
public partial class CineFx : Node3D
{
    private enum Kind { Glow, Fire, Smoke, Ring, Spark, Chunk }

    private sealed class Fx
    {
        public required Kind Kind;
        public required Vec3d Origin;
        public Vector3 Velocity;
        public required double Born;
        public required double Life;
        public required float Size;
        public required MeshInstance3D Node;
        public required StandardMaterial3D Material;
        public OmniLight3D? Light;
        public float LightEnergy;
        public Vector3 Axis;
        public Color Color;
    }

    public readonly record struct SoundEvent(double Time, string Kind, double Distance, float Size);

    private readonly List<Fx> _fx = new();
    private readonly List<(double At, Action Run)> _pending = new();
    private readonly HashSet<uint> _seenProjectiles = new(), _seenImpacts = new(), _seenMissiles = new();
    private readonly HashSet<(double, OrdnanceEventKind, Vec3d)> _seenEvents = new();
    private readonly Dictionary<ShipBody, double> _dead = new();
    private readonly QuadMesh _quad = new() { Size = new Vector2(2, 2) };
    private readonly BoxMesh _box = new() { Size = Vector3.One };
    private readonly Texture2D _soft = Radial(new[] { 0f, 0.25f, 0.6f, 1f }, new[] { 1f, 0.75f, 0.2f, 0f });
    private readonly Texture2D _ringTexture = Radial(new[] { 0f, 0.72f, 0.88f, 1f }, new[] { 0f, 0f, 1f, 0f });
    private readonly Random _rng = new(17);
    private Puffs _glowTrail = null!, _smokeTrail = null!;
    private readonly Dictionary<uint, (MeshInstance3D Glow, MeshInstance3D Core)> _streaks = new();
    private StandardMaterial3D _blueStreak = null!, _redStreak = null!, _coreStreak = null!;
    private int _pdShots;

    public List<SoundEvent> Sounds { get; } = new();
    public string Census() => string.Join(" ", _fx.GroupBy(f => f.Kind).Select(g => $"{g.Key}:{g.Count()}/{g.Count(f => f.Node.Visible)}"))
        + " " + string.Join(" ", _fx.Where(f => f.Kind == Kind.Fire).Take(3).Select(f => $"[fire size={f.Size:0} born={f.Born:0.00} life={f.Life:0.0} pos={f.Node.Position} a={f.Material.AlbedoColor.A:0.00}]"));
    /// <summary>영상 시간(초). 감독이 매 프레임 넣는다.</summary>
    public double VideoTime { get; set; }
    public Vec3d Camera { get; set; }
    /// <summary>카메라 흔들림 세기(감쇠는 감독이 한다).</summary>
    public float Trauma { get; set; }

    public override void _Ready()
    {
        _glowTrail = new Puffs(this, _soft, additive: true, 6144);
        _smokeTrail = new Puffs(this, _soft, additive: false, 8192);
        StandardMaterial3D Streak(Color c) => new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoTexture = _soft, AlbedoColor = c,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _blueStreak = Streak(new Color(0.4f, 0.72f, 1f, 0.9f));
        _redStreak = Streak(new Color(1f, 0.5f, 0.25f, 0.9f));
        _coreStreak = Streak(new Color(1f, 1f, 1f, 0.95f));
    }

    /// <summary>레일건 탄: 진행 방향으로 늘인 빛줄기. 멀어도 보이도록 폭은 거리에 비례한다.</summary>
    public void SyncProjectiles(SimWorld world, Vec3d origin, double alpha)
    {
        var active = new HashSet<uint>();
        foreach (RailProjectile p in world.Projectiles)
        {
            active.Add(p.Id);
            if (!_streaks.TryGetValue(p.Id, out var pair))
            {
                pair = (Quad(p.Shooter.Faction == Faction.Blue ? _blueStreak : _redStreak), Quad(_coreStreak));
                _streaks[p.Id] = pair;
            }
            Vector3 dir = p.Velocity.Normalized();
            float speed = p.Velocity.Length();
            float length = (float)Math.Min(speed * 0.06, speed * Math.Max(p.Age, 0.01)) ;
            Vector3 head = (Vec3d.Lerp(p.PrevPosition, p.Position, alpha) - origin).ToVector3();
            Vector3 center = head - dir * length * 0.5f;
            Vector3 toCam = (-center).Normalized();
            Vector3 side = dir.Cross(toCam);
            if (side.LengthSquared() < 1e-6f) side = dir.Cross(Vector3.Up);
            side = side.Normalized();
            float width = Mathf.Max(3f, center.Length() * 0.0035f);
            Vector3 normal = side.Cross(dir).Normalized();
            pair.Glow.Transform = new Transform3D(new Basis(side * width, dir * length * 0.5f, normal), center);
            pair.Core.Transform = new Transform3D(new Basis(side * width * 0.3f, dir * length * 0.42f, normal), center + dir * length * 0.05f);
            pair.Glow.Visible = pair.Core.Visible = true;
        }
        foreach (uint id in _streaks.Keys.Where(id => !active.Contains(id)).ToArray())
        {
            _streaks[id].Glow.QueueFree(); _streaks[id].Core.QueueFree(); _streaks.Remove(id);
        }
    }

    private MeshInstance3D Quad(Material material)
    {
        var node = new MeshInstance3D { Mesh = _quad, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(node);
        return node;
    }

    private static Texture2D Radial(float[] offsets, float[] alpha)
    {
        var gradient = new Gradient { Offsets = offsets, Colors = alpha.Select(a => new Color(1, 1, 1, a)).ToArray() };
        return new GradientTexture2D
        {
            Gradient = gradient, Width = 128, Height = 128, Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f),
        };
    }

    /// <summary>무대가 바뀌면 이전 사건을 새로 본 것으로 치지 않도록 지금 상태를 기억만 한다.</summary>
    public void Adopt(SimWorld world)
    {
        foreach (var p in world.Projectiles) _seenProjectiles.Add(p.Id);
        foreach (var i in world.Impacts) _seenImpacts.Add(i.Id);
        foreach (var m in world.Missiles) _seenMissiles.Add(m.Id);
        foreach (var e in world.OrdnanceEvents) _seenEvents.Add((e.Time, e.Kind, e.Position));
        foreach (var s in world.Ships.Where(s => s.Damage.Destroyed)) _dead.TryAdd(s, world.Time - 60);
    }

    public void Clear()
    {
        foreach (Fx fx in _fx) { fx.Node.QueueFree(); fx.Light?.QueueFree(); }
        _fx.Clear(); _pending.Clear();
        _seenProjectiles.Clear(); _seenImpacts.Clear(); _seenMissiles.Clear(); _seenEvents.Clear(); _dead.Clear();
        _glowTrail.Clear(); _smokeTrail.Clear();
        foreach (var pair in _streaks.Values) { pair.Glow.QueueFree(); pair.Core.QueueFree(); }
        _streaks.Clear();
    }

    private double Dist(Vec3d at) => (at - Camera).Length();
    private void Sound(string kind, Vec3d at, float size) => Sounds.Add(new SoundEvent(VideoTime, kind, Dist(at), size));
    public void Emit(string kind, Vec3d at, float size) => Sound(kind, at, size);
    private readonly HashSet<uint> _seenProbes = new();
    private float R() => (float)_rng.NextDouble();

    /// <summary>시뮬레이션 한 틱 뒤에 부른다.</summary>
    public void Observe(SimWorld world)
    {
        double now = world.Time;
        foreach (var item in _pending.Where(p => p.At <= now).ToArray()) { item.Run(); _pending.Remove(item); }

        foreach (RailProjectile p in world.Projectiles)
        {
            if (!_seenProjectiles.Add(p.Id)) continue;
            float s = Size(p.Shooter);
            Spawn(Kind.Glow, p.Position, p.Shooter.Velocity, now, 0.22, s * 0.02f, new Color(0.7f, 0.88f, 1f), light: s * 0.35f, energy: 7);
            Spawn(Kind.Glow, p.Position, p.Shooter.Velocity + p.Velocity.Normalized() * 30, now, 0.5, s * 0.012f, new Color(0.45f, 0.7f, 1f));
            for (int i = 0; i < 5; i++)
                _smokeTrail.Add(p.Position, p.Shooter.Velocity + (p.Velocity.Normalized() * (8 + i * 6) + RandomUnit() * 4) * s * 0.002f,
                    now, 1.6f, s * 0.003f, s * 0.012f, new Color(0.55f, 0.6f, 0.68f, 0.18f));
            Sound("rail", p.Position, s);
            Trauma += Mathf.Clamp(s / (float)Math.Max(Dist(p.Position), 50) * 0.5f, 0, 0.45f);
        }
        foreach (ProjectileImpact impact in world.Impacts)
        {
            if (!_seenImpacts.Add(impact.Id)) continue;
            ShotResult hit = impact.Hit;
            float s = hit.Target is null ? 60 : Size(hit.Target);
            Vector3 v = hit.Target?.Velocity ?? Vector3.Zero;
            if (hit.ShieldStopped)
            {
                Spawn(Kind.Glow, hit.Point, v, now, 0.5, s * 0.09f, new Color(0.35f, 0.7f, 1f), light: s * 0.5f, energy: 6);
                Spawn(Kind.Ring, hit.Point, v, now, 0.8, s * 0.22f, new Color(0.35f, 0.7f, 1f));
                Sound("shield", hit.Point, s);
                continue;
            }
            bool module = hit.Modules.Any(m => m.Destroyed);
            Spawn(Kind.Glow, hit.Point, v, now, 0.35, s * 0.08f, new Color(1f, 0.85f, 0.6f), light: s * 0.6f, energy: 10);
            Sparks(hit.Point, v, now, module ? 22 : 12, s * 0.12f);
            if (module) Blast(hit.Point, v, now, s * 0.11f);
            Sound(module ? "module" : "armor", hit.Point, s);
        }
        foreach (var probe in world.Probes)
            if (_seenProbes.Add(probe.Id)) Sound("probe", probe.Position, 12);
        foreach (Missile m in world.Missiles)
        {
            if (_seenMissiles.Add(m.Id))
            {
                Sound("launch", m.Position, Size(m.Shooter));
                Spawn(Kind.Glow, m.Position, m.Shooter.Velocity, now, 0.5, 14, new Color(1f, 0.8f, 0.55f), light: 120, energy: 4);
            }
            if (m.Burning)
            {
                // 틱 사이 이동 거리를 나눠 채워 연기 줄이 구슬처럼 끊기지 않게 한다.
                double step = (m.Position - m.PrevPosition).Length();
                int n = Math.Clamp((int)(step / 4), 1, 28);
                for (int j = 0; j < n; j++)
                {
                    Vec3d at = Vec3d.Lerp(m.PrevPosition, m.Position, (j + 1.0) / n);
                    double born = now - SimWorld.TickDelta * (1 - (j + 1.0) / n);
                    if (j % 2 == 0) _glowTrail.Add(at, m.Velocity * 0.02f, born, 0.3f, 2f, 4f, new Color(1f, 0.75f, 0.45f, 0.7f));
                    _smokeTrail.Add(at, RandomUnit() * 1.5f, born, 2.6f, 2.2f, 9f, new Color(0.62f, 0.64f, 0.7f, 0.22f));
                }
            }
        }
        foreach (OrdnanceEvent e in world.OrdnanceEvents)
        {
            if (!_seenEvents.Add((e.Time, e.Kind, e.Position))) continue;
            if (now - e.Time > 0.1) continue;
            if (e.Kind == OrdnanceEventKind.Detonation)
            {
                Blast(e.Position, Vector3.Zero, now, 24f);
                Sound("missile", e.Position, 30);
            }
            else if (e.Kind == OrdnanceEventKind.Intercepted)
            {
                Spawn(Kind.Glow, e.Position, Vector3.Zero, now, 0.35, 14f, new Color(1f, 0.95f, 0.85f), light: 90, energy: 3);
                for (int i = 0; i < 4; i++)
                    _smokeTrail.Add(e.Position, RandomUnit() * 6, now, 2.5f, 4, 14, new Color(0.7f, 0.7f, 0.72f, 0.4f));
                Sound("pop", e.Position, 10);
            }
        }
        _pdShots += world.PointDefenseShots.Count(s => now - s.Time < SimWorld.TickDelta * 1.5);
        if (world.Tick % 6 == 0 && _pdShots > 0)
        {
            Vec3d at = world.PointDefenseShots.Count > 0 ? world.PointDefenseShots[^1].From : Camera;
            Sounds.Add(new SoundEvent(VideoTime, "pd", Dist(at), _pdShots));
            _pdShots = 0;
        }
        foreach (ShipBody ship in world.Ships)
        {
            if (ship.Damage.Destroyed && _dead.TryAdd(ship, now)) Death(ship, now);
            // 불타는 잔해: 격침 뒤 한동안 선체 곳곳에서 불꽃과 연기가 샌다.
            if (_dead.TryGetValue(ship, out double died) && now - died < 40 && now - died > 0.4)
            {
                float l = ship.Class.Length;
                for (int i = 0; i < 2; i++)
                {
                    Vector3 local = new((R() - 0.5f) * l * 0.1f, (R() - 0.5f) * l * 0.08f, (R() - 0.5f) * l * 0.75f);
                    Vec3d at = ship.Position + Vec3d.From(ship.Orientation * local);
                    _glowTrail.Add(at, ship.Velocity + RandomUnit() * l * 0.01f, now, 0.9f, l * 0.01f, l * 0.03f, new Color(1f, 0.5f, 0.18f, 0.7f));
                    _smokeTrail.Add(at, ship.Velocity + RandomUnit() * l * 0.015f, now, 5f, l * 0.02f, l * 0.08f, new Color(0.2f, 0.18f, 0.17f, 0.5f));
                }
            }
        }
    }

    private static float Size(ShipBody ship) => ship.Class.Length;

    private void Sparks(Vec3d at, Vector3 baseVelocity, double now, int count, float speed)
    {
        for (int i = 0; i < count; i++)
            Spawn(Kind.Spark, at, baseVelocity + RandomUnit() * speed * (0.6f + R()), now, 0.4 + R() * 0.7,
                Mathf.Max(0.5f, speed * 0.01f), new Color(1f, 0.6f + R() * 0.3f, 0.25f));
    }

    /// <summary>불덩이 여러 겹 + 연기 + 충격파 + 불꽃 + 순간광.</summary>
    public void Blast(Vec3d at, Vector3 velocity, double now, float size)
    {
        Spawn(Kind.Glow, at, velocity, now, 0.25, size * 2.2f, new Color(1f, 0.97f, 0.9f), light: size * 12, energy: 12);
        for (int i = 0; i < 6; i++)
            Spawn(Kind.Fire, at + Vec3d.From(RandomUnit() * size * 0.5f), velocity + RandomUnit() * size * 0.6f, now + i * 0.03,
                1.0 + R() * 0.8, size * (0.7f + R() * 0.6f), Colors.White);
        for (int i = 0; i < 5; i++)
            _smokeTrail.Add(at + Vec3d.From(RandomUnit() * size * 0.4f), velocity + RandomUnit() * size * 0.35f, now + 0.3, 5f,
                size * 0.8f, size * 3.2f, new Color(0.22f, 0.17f, 0.15f, 0.55f));
        Spawn(Kind.Ring, at, velocity, now, 0.8, size * 4f, new Color(1f, 0.82f, 0.62f));
        Sparks(at, velocity, now, 12, size * 2.5f);
        Trauma += Mathf.Clamp(size * 5 / (float)Math.Max(Dist(at), 50), 0, 0.8f);
    }

    /// <summary>함선 격침: 선체를 따라 연쇄 폭발, 큰 섬광, 파편.</summary>
    public void Death(ShipBody ship, double now)
    {
        float l = ship.Class.Length;
        // 첫 폭발은 바로 크게, 이어서 선체를 따라 연쇄 폭발, 마지막에 선체가 갈라지는 큰 섬광.
        Blast(ship.Position + Vec3d.From(ship.Orientation * new Vector3(0, l * 0.02f, -l * 0.05f)), ship.Velocity, now, l * 0.22f);
        Sound("boom", ship.Position, l);
        int count = ship.Class.Kind switch { HullKind.Battleship => 14, HullKind.Escort => 9, _ => 3 };
        for (int i = 0; i < count; i++)
        {
            var local = new Vector3((R() - 0.5f) * l * 0.12f, (R() - 0.5f) * l * 0.1f, (R() - 0.5f) * l * 0.8f);
            double delay = 0.12 + i * 1.25 / count + R() * 0.1;
            float size = l * (0.1f + R() * 0.09f);
            _pending.Add((now + delay, () =>
            {
                Vec3d at = ship.Position + Vec3d.From(ship.Orientation * local);
                Blast(at, ship.Velocity, now + delay, size);
                Sound("boom", at, l);
            }));
        }
        double final = now + 1.5;
        _pending.Add((final, () =>
        {
            Spawn(Kind.Glow, ship.Position, ship.Velocity, final, 0.8, l * 0.7f, new Color(1f, 0.95f, 0.85f), light: l * 5, energy: 20);
            for (int i = 0; i < 12; i++)
                Spawn(Kind.Fire, ship.Position + Vec3d.From(ship.Orientation * new Vector3(0, 0, (R() - 0.5f) * l * 0.7f) + RandomUnit() * l * 0.06f),
                    ship.Velocity + RandomUnit() * l * 0.06f, final + i * 0.035, 2.0 + R(), l * (0.22f + R() * 0.16f), Colors.White);
            for (int i = 0; i < 10; i++)
                _smokeTrail.Add(ship.Position + Vec3d.From(RandomUnit() * l * 0.2f), ship.Velocity + RandomUnit() * l * 0.05f, final + 0.4, 8f,
                    l * 0.15f, l * 0.6f, new Color(0.18f, 0.15f, 0.14f, 0.5f));
            Spawn(Kind.Ring, ship.Position, ship.Velocity, final, 2.4, l * 2.6f, new Color(1f, 0.78f, 0.55f));
            Sound("kill", ship.Position, l);
            for (int i = 0; i < 30; i++)
            {
                var chunk = Spawn(Kind.Chunk, ship.Position + Vec3d.From(ship.Orientation * new Vector3(0, 0, (R() - 0.5f) * l * 0.7f)),
                    ship.Velocity + RandomUnit() * l * (0.03f + R() * 0.09f), final, 40, l * (0.008f + R() * 0.025f), new Color(0.16f, 0.17f, 0.19f));
                chunk.Axis = RandomUnit() * (0.5f + R() * 2f);
            }
            Trauma += 1f;
        }));
        Sound("break", ship.Position, l);
    }

    private Vector3 RandomUnit()
    {
        Vector3 v;
        do v = new Vector3(R() * 2 - 1, R() * 2 - 1, R() * 2 - 1);
        while (v.LengthSquared() > 1 || v.LengthSquared() < 0.01f);
        return v.Normalized();
    }

    private Fx Spawn(Kind kind, Vec3d at, Vector3 velocity, double now, double life, float size, Color color, float light = 0, float energy = 0)
    {
        bool solid = kind == Kind.Chunk;
        bool textured = kind is Kind.Glow or Kind.Fire or Kind.Smoke or Kind.Ring;
        var material = new StandardMaterial3D
        {
            ShadingMode = solid ? BaseMaterial3D.ShadingModeEnum.PerPixel : BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = color,
            AlbedoTexture = textured ? (kind == Kind.Ring ? _ringTexture : _soft) : null,
            Metallic = solid ? 0.7f : 0,
            Roughness = 0.6f,
            BlendMode = solid || kind == Kind.Smoke ? BaseMaterial3D.BlendModeEnum.Mix : BaseMaterial3D.BlendModeEnum.Add,
            Transparency = solid ? BaseMaterial3D.TransparencyEnum.Disabled : BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            EmissionEnabled = solid,
            Emission = new Color(0.9f, 0.3f, 0.08f),
            EmissionEnergyMultiplier = 0.6f,
        };
        var node = new MeshInstance3D
        {
            Mesh = textured ? _quad : _box, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false,
        };
        AddChild(node);
        OmniLight3D? omni = null;
        if (light > 0)
        {
            omni = new OmniLight3D { LightColor = color, OmniRange = light, LightEnergy = 0, ShadowEnabled = false, OmniAttenuation = 1.2f };
            AddChild(omni);
        }
        var fx = new Fx
        {
            Kind = kind, Origin = at, Velocity = velocity, Born = now, Life = life, Size = size, Node = node,
            Material = material, Light = omni, LightEnergy = energy, Color = color, Axis = RandomUnit(),
        };
        _fx.Add(fx);
        return fx;
    }

    /// <summary>매 프레임 렌더 원점 기준으로 놓는다. 판은 카메라 쪽으로 돌린다.</summary>
    public void Sync(Vec3d origin, double time, Basis camera)
    {
        for (int i = _fx.Count - 1; i >= 0; i--)
        {
            Fx fx = _fx[i];
            double age = time - fx.Born;
            if (age < 0) { fx.Node.Visible = false; continue; }
            float k = (float)(age / fx.Life);
            if (k >= 1) { fx.Node.QueueFree(); fx.Light?.QueueFree(); _fx.RemoveAt(i); continue; }
            fx.Node.Visible = true;
            Vector3 pos = (fx.Origin + Vec3d.From(fx.Velocity * (float)age) - origin).ToVector3();
            // 선체 안에서 터지는 불꽃이 선체에 가려지지 않게 판을 카메라 쪽으로 당긴다(카메라는 원점).
            if (fx.Kind is Kind.Glow or Kind.Fire && pos.LengthSquared() > 1)
                pos -= pos.Normalized() * Mathf.Min(fx.Size * 1.2f, pos.Length() * 0.5f);
            switch (fx.Kind)
            {
                case Kind.Glow:
                    fx.Node.Transform = new Transform3D(camera.Scaled(Vector3.One * fx.Size * (0.7f + k * 0.6f)), pos);
                    fx.Material.AlbedoColor = new Color(fx.Color, (1 - k) * (1 - k));
                    break;
                case Kind.Fire:
                    float grow = 1 - Mathf.Pow(1 - k, 3);
                    fx.Node.Transform = new Transform3D(camera.Rotated(camera.Z, fx.Axis.X * 3).Scaled(Vector3.One * fx.Size * (0.35f + grow * 1.5f)), pos);
                    Color c = new Color(1f, 0.93f, 0.75f).Lerp(new Color(1f, 0.5f, 0.15f), Mathf.Min(1, k * 2.5f))
                        .Lerp(new Color(0.55f, 0.12f, 0.03f), Mathf.Clamp(k * 2 - 0.6f, 0, 1));
                    fx.Material.AlbedoColor = new Color(c, (1 - k) * 0.9f);
                    break;
                case Kind.Ring:
                    Basis b = Basis.LookingAt(fx.Axis, Mathf.Abs(fx.Axis.Dot(Vector3.Up)) > 0.9f ? Vector3.Right : Vector3.Up);
                    float r = fx.Size * (0.15f + Mathf.Sqrt(k) * 1.1f);
                    fx.Node.Transform = new Transform3D(b.Scaled(Vector3.One * r), pos);
                    fx.Material.AlbedoColor = new Color(fx.Color, (1 - k) * (1 - k) * 0.32f);
                    break;
                case Kind.Spark:
                    Vector3 v = fx.Velocity.LengthSquared() > 0.01f ? fx.Velocity.Normalized() : Vector3.Up;
                    Basis sb = Basis.LookingAt(v, Mathf.Abs(v.Dot(Vector3.Up)) > 0.9f ? Vector3.Right : Vector3.Up);
                    fx.Node.Transform = new Transform3D(sb.Scaled(new Vector3(fx.Size, fx.Size, fx.Size * 10)), pos);
                    fx.Material.AlbedoColor = new Color(fx.Color, 1 - k);
                    break;
                case Kind.Chunk:
                    Basis cb = new Basis(fx.Axis.Normalized(), fx.Axis.Length() * (float)age);
                    fx.Node.Transform = new Transform3D(cb.Scaled(new Vector3(fx.Size, fx.Size * 0.6f, fx.Size * 1.7f)), pos);
                    fx.Material.EmissionEnergyMultiplier = Mathf.Max(0, 0.8f - (float)age * 0.15f);
                    break;
            }
            if (fx.Light is { } light)
            {
                light.Position = pos;
                light.LightEnergy = fx.LightEnergy * Mathf.Pow(1 - k, 2);
            }
        }
        _glowTrail.Sync(origin, time, camera);
        _smokeTrail.Sync(origin, time, camera);
    }

    /// <summary>작은 연기·불꽃 판 여러 개를 한 번에 그린다(MultiMesh 고리 버퍼).</summary>
    private sealed class Puffs
    {
        private readonly MultiMesh _mesh;
        private readonly Vec3d[] _pos;
        private readonly Vector3[] _vel;
        private readonly double[] _born;
        private readonly float[] _life, _from, _to;
        private readonly Color[] _color;
        private int _next;

        public Puffs(Node3D parent, Texture2D texture, bool additive, int capacity)
        {
            _pos = new Vec3d[capacity]; _vel = new Vector3[capacity]; _born = new double[capacity];
            _life = new float[capacity]; _from = new float[capacity]; _to = new float[capacity]; _color = new Color[capacity];
            _mesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true,
                Mesh = new QuadMesh { Size = new Vector2(2, 2) }, InstanceCount = capacity,
                CustomAabb = new Aabb(new Vector3(-1e6f, -1e6f, -1e6f), new Vector3(2e6f, 2e6f, 2e6f)),
            };
            var material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoTexture = texture, VertexColorUseAsAlbedo = true,
                BlendMode = additive ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            parent.AddChild(new MultiMeshInstance3D { Multimesh = _mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            Clear();
        }

        public void Clear()
        {
            for (int i = 0; i < _life.Length; i++) { _life[i] = 0; _mesh.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.Zero), Vector3.Zero)); }
        }

        public void Add(Vec3d at, Vector3 velocity, double now, float life, float from, float to, Color color)
        {
            int i = _next; _next = (_next + 1) % _life.Length;
            _pos[i] = at; _vel[i] = velocity; _born[i] = now; _life[i] = life; _from[i] = from; _to[i] = to; _color[i] = color;
        }

        public void Sync(Vec3d origin, double time, Basis camera)
        {
            for (int i = 0; i < _life.Length; i++)
            {
                if (_life[i] <= 0) continue;
                double age = time - _born[i];
                float k = (float)(age / _life[i]);
                if (k >= 1 || age < 0)
                {
                    if (k >= 1) _life[i] = 0;
                    _mesh.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.Zero), Vector3.Zero));
                    continue;
                }
                Vector3 pos = (_pos[i] + Vec3d.From(_vel[i] * (float)age) - origin).ToVector3();
                float size = Mathf.Lerp(_from[i], _to[i], Mathf.Sqrt(k));
                _mesh.SetInstanceTransform(i, new Transform3D(camera.Rotated(camera.Z, i * 0.7f).Scaled(Vector3.One * size), pos));
                float fade = (1 - k) * Mathf.Min(1, k * 8 + 0.2f);
                _mesh.SetInstanceColor(i, new Color(_color[i], _color[i].A * fade));
            }
        }
    }
}
