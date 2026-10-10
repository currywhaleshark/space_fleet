using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.View;

/// <summary>
/// 미사일 연기 줄, 겹친 불덩이 폭발(연기·충격파·불꽃·순간광), 격침 연쇄 폭발과 파편, 불타는 잔해.
/// 티저(CineFx)에서 가져온 연출. 레일건 탄줄기·포구 섬광·명중 파편은 BallisticsView가 맡으므로 여기서는 그리지 않는다.
/// 시뮬레이션 사건을 보고 만들며 판정에는 관여하지 않는다. 시간은 시뮬레이션 기준이라 일시정지·배속을 따른다.
/// 모든 판은 종류별 MultiMesh 네 개(가산 빛·충격파·연기·파편)로 그리고, 프레임마다 버퍼를 한 번에 넘긴다.
/// </summary>
public partial class BattleFx : Node3D
{
    private enum Kind : byte { Glow, Fire, Puff, Spark, Ring, Chunk }

    private struct Particle
    {
        public Kind Kind;
        public Vec3d Origin;
        public Vector3 Velocity, Axis;
        public double Born;
        public float Life, From, To;
        public Color Color;
    }

    /// <summary>이보다 먼 사건은 화면에서 점도 안 되므로 효과를 만들지 않는다(m).</summary>
    private const double CullDistance = 80_000;
    private const int MaxLights = 8;

    private readonly List<(double At, Action Run)> _pending = new();
    private readonly HashSet<uint> _seenImpacts = new(), _seenMissiles = new();
    private readonly HashSet<(double, OrdnanceEventKind, Vec3d)> _seenEvents = new();
    private readonly Dictionary<ShipBody, double> _dead = new();
    private readonly Random _rng = new(17);
    private Layer _light = null!, _rings = null!, _smoke = null!, _chunks = null!;
    private readonly List<(OmniLight3D Node, Vec3d Origin, Vector3 Velocity, double Born, float Life, float Energy)> _lights = new();
    private long _lastTick = -1;
    private Vec3d _camera;

    /// <summary>끄면 아무것도 만들지 않는다(성능 비교용 --no-battle-fx).</summary>
    public bool Enabled { get; init; } = true;
    public int ActiveParticles => _light.Count + _rings.Count + _smoke.Count + _chunks.Count;

    public override void _Ready()
    {
        Texture2D soft = Radial(new[] { 0f, 0.25f, 0.6f, 1f }, new[] { 1f, 0.75f, 0.2f, 0f });
        Texture2D ring = Radial(new[] { 0f, 0.72f, 0.88f, 1f }, new[] { 0f, 0f, 1f, 0f });
        var quad = new QuadMesh { Size = new Vector2(2, 2) };
        _light = new Layer(this, quad, Sprite(soft, additive: true), 8192);
        _rings = new Layer(this, quad, Sprite(ring, additive: true), 256);
        _smoke = new Layer(this, quad, Sprite(soft, additive: false), 8192);
        _chunks = new Layer(this, new BoxMesh { Size = Vector3.One }, new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true, Metallic = 0.7f, Roughness = 0.6f,
            EmissionEnabled = true, Emission = new Color(0.35f, 0.1f, 0.03f),
        }, 512);
        for (int i = 0; i < MaxLights; i++)
        {
            var light = new OmniLight3D { ShadowEnabled = false, OmniAttenuation = 1.2f, Visible = false };
            AddChild(light);
            _lights.Add((light, Vec3d.Zero, Vector3.Zero, double.NegativeInfinity, 0, 0));
        }
    }

    private static StandardMaterial3D Sprite(Texture2D texture, bool additive) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoTexture = texture, VertexColorUseAsAlbedo = true,
        BlendMode = additive ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    private static Texture2D Radial(float[] offsets, float[] alpha) => new GradientTexture2D
    {
        Gradient = new Gradient { Offsets = offsets, Colors = alpha.Select(a => new Color(1, 1, 1, a)).ToArray() },
        Width = 128, Height = 128, Fill = GradientTexture2D.FillEnum.Radial,
        FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f),
    };

    /// <summary>지금 상태를 이미 본 것으로 기억만 한다(장면 시작, 빨리 감기 뒤).</summary>
    public void Adopt(SimWorld world)
    {
        foreach (var i in world.Impacts) _seenImpacts.Add(i.Id);
        foreach (var m in world.Missiles) _seenMissiles.Add(m.Id);
        foreach (var e in world.OrdnanceEvents) _seenEvents.Add((e.Time, e.Kind, e.Position));
        foreach (var s in world.Ships.Where(s => s.Damage.Destroyed)) _dead.TryAdd(s, world.Time - 60);
        _lastTick = world.Tick;
    }

    private float R() => (float)_rng.NextDouble();
    private bool Near(Vec3d at) => (at - _camera).LengthSquared() < CullDistance * CullDistance;

    /// <summary>시뮬레이션 한 틱 뒤에 부른다. camera는 카메라의 시뮬레이션 위치(먼 사건을 거르는 데 쓴다).</summary>
    public void Observe(SimWorld world, Vec3d camera)
    {
        if (!Enabled) return;
        _camera = camera;
        // 틱을 건너뛰었으면(빨리 감기) 그동안의 사건을 한꺼번에 터뜨리지 않는다.
        if (_lastTick >= 0 && world.Tick - _lastTick > 30) { Adopt(world); return; }
        _lastTick = world.Tick;
        double now = world.Time;
        if (_pending.Count > 0)
            foreach (var item in _pending.Where(p => p.At <= now).ToArray()) { item.Run(); _pending.Remove(item); }

        foreach (ProjectileImpact impact in world.Impacts)
        {
            if (!_seenImpacts.Add(impact.Id)) continue;
            ShotResult hit = impact.Hit;
            // 모듈이 부서진 명중만 불덩이를 더한다(나머지 명중 연출은 BallisticsView).
            if (hit.ShieldStopped || hit.Target is not ShipBody target || !hit.Modules.Any(m => m.Destroyed) || !Near(hit.Point)) continue;
            Blast(hit.Point, target.Velocity, now, target.Class.Length * 0.06f);
        }
        foreach (Missile m in world.Missiles)
        {
            if (_seenMissiles.Add(m.Id) && Near(m.Position))
                Add(_light, Kind.Glow, m.Position, m.Shooter.Velocity, now, 0.5f, 14, 14, new Color(1f, 0.8f, 0.55f), light: (120, 4));
            if (!m.Burning || !Near(m.Position)) continue;
            // 틱 사이 이동 거리를 나눠 채워 연기 줄이 구슬처럼 끊기지 않게 한다.
            double step = (m.Position - m.PrevPosition).Length();
            int n = Math.Clamp((int)(step / 7), 1, 12);
            for (int j = 0; j < n; j++)
            {
                Vec3d at = Vec3d.Lerp(m.PrevPosition, m.Position, (j + 1.0) / n);
                double born = now - SimWorld.TickDelta * (1 - (j + 1.0) / n);
                if (j % 2 == 0) Add(_light, Kind.Puff, at, m.Velocity * 0.02f, born, 0.3f, 2.5f, 5f, new Color(1f, 0.75f, 0.45f, 0.7f));
                Add(_smoke, Kind.Puff, at, RandomUnit() * 1.5f, born, 2.6f, 3f, 11f, new Color(0.62f, 0.64f, 0.7f, 0.22f));
            }
        }
        foreach (OrdnanceEvent e in world.OrdnanceEvents)
        {
            if (!_seenEvents.Add((e.Time, e.Kind, e.Position)) || now - e.Time > 0.1 || !Near(e.Position)) continue;
            if (e.Kind == OrdnanceEventKind.Detonation && e.Weapon != BattleWeapon.Antimatter)
                Blast(e.Position, Vector3.Zero, now, 24f);
            else if (e.Kind == OrdnanceEventKind.Intercepted)
                for (int i = 0; i < 4; i++)
                    Add(_smoke, Kind.Puff, e.Position, RandomUnit() * 6, now, 2.5f, 4, 14, new Color(0.7f, 0.7f, 0.72f, 0.4f));
        }
        foreach (ShipBody ship in world.Ships)
        {
            if (ship.Damage.Destroyed && _dead.TryAdd(ship, now) && Near(ship.Position)) Death(ship, now);
            // 불타는 잔해: 격침 뒤 한동안 선체 곳곳에서 불꽃과 연기가 샌다(3틱에 한 번).
            if (world.Tick % 3 != 0 || !_dead.TryGetValue(ship, out double died) || now - died > 40 || now - died < 0.4 || !Near(ship.Position)) continue;
            float l = ship.Class.Length;
            Vector3 local = new((R() - 0.5f) * l * 0.1f, (R() - 0.5f) * l * 0.08f, (R() - 0.5f) * l * 0.75f);
            Vec3d spot = ship.Position + Vec3d.From(ship.Orientation * local);
            Add(_light, Kind.Puff, spot, ship.Velocity + RandomUnit() * l * 0.01f, now, 0.9f, l * 0.012f, l * 0.035f, new Color(1f, 0.5f, 0.18f, 0.75f));
            Add(_smoke, Kind.Puff, spot, ship.Velocity + RandomUnit() * l * 0.015f, now, 5f, l * 0.025f, l * 0.09f, new Color(0.2f, 0.18f, 0.17f, 0.5f));
        }
    }

    private void Sparks(Vec3d at, Vector3 baseVelocity, double now, int count, float speed)
    {
        for (int i = 0; i < count; i++)
        {
            float size = Mathf.Max(0.5f, speed * 0.01f);
            Add(_light, Kind.Spark, at, baseVelocity + RandomUnit() * speed * (0.6f + R()), now, 0.4f + R() * 0.7f, size, size,
                new Color(1f, 0.6f + R() * 0.3f, 0.25f));
        }
    }

    /// <summary>불덩이 여러 겹 + 연기 + 충격파 + 불꽃 + 순간광.</summary>
    private void Blast(Vec3d at, Vector3 velocity, double now, float size)
    {
        Add(_light, Kind.Glow, at, velocity, now, 0.25f, size * 2.2f, size * 2.2f, new Color(1f, 0.97f, 0.9f), light: (size * 12, 12));
        for (int i = 0; i < 6; i++)
        {
            float s = size * (0.7f + R() * 0.6f);
            Add(_light, Kind.Fire, at + Vec3d.From(RandomUnit() * size * 0.5f), velocity + RandomUnit() * size * 0.6f, now + i * 0.03,
                1.0f + R() * 0.8f, s, s, Colors.White);
        }
        for (int i = 0; i < 5; i++)
            Add(_smoke, Kind.Puff, at + Vec3d.From(RandomUnit() * size * 0.4f), velocity + RandomUnit() * size * 0.35f, now + 0.3, 5f,
                size * 0.8f, size * 3.2f, new Color(0.22f, 0.17f, 0.15f, 0.55f));
        Add(_rings, Kind.Ring, at, velocity, now, 0.7f, size * 3f, size * 3f, new Color(1f, 0.82f, 0.62f));
        Sparks(at, velocity, now, 12, size * 2.5f);
    }

    /// <summary>격침: 첫 폭발은 바로 크게, 선체를 따라 연쇄 폭발, 마지막에 선체가 갈라지는 큰 섬광과 파편.</summary>
    private void Death(ShipBody ship, double now)
    {
        float l = ship.Class.Length;
        Blast(ship.Position + Vec3d.From(ship.Orientation * new Vector3(0, l * 0.02f, -l * 0.05f)), ship.Velocity, now, l * 0.22f);
        int count = ship.Class.Kind switch { HullKind.Battleship => 14, HullKind.Escort => 9, _ => 3 };
        for (int i = 0; i < count; i++)
        {
            var local = new Vector3((R() - 0.5f) * l * 0.12f, (R() - 0.5f) * l * 0.1f, (R() - 0.5f) * l * 0.8f);
            double delay = 0.12 + i * 1.25 / count + R() * 0.1;
            float size = l * (0.1f + R() * 0.09f);
            _pending.Add((now + delay, () => Blast(ship.Position + Vec3d.From(ship.Orientation * local), ship.Velocity, now + delay, size)));
        }
        double final = now + 1.5;
        _pending.Add((final, () =>
        {
            Add(_light, Kind.Glow, ship.Position, ship.Velocity, final, 0.8f, l * 0.7f, l * 0.7f, new Color(1f, 0.95f, 0.85f), light: (l * 5, 20));
            for (int i = 0; i < 12; i++)
            {
                float s = l * (0.22f + R() * 0.16f);
                Add(_light, Kind.Fire, ship.Position + Vec3d.From(ship.Orientation * new Vector3(0, 0, (R() - 0.5f) * l * 0.7f) + RandomUnit() * l * 0.06f),
                    ship.Velocity + RandomUnit() * l * 0.06f, final + i * 0.035, 2.0f + R(), s, s, Colors.White);
            }
            for (int i = 0; i < 10; i++)
                Add(_smoke, Kind.Puff, ship.Position + Vec3d.From(RandomUnit() * l * 0.2f), ship.Velocity + RandomUnit() * l * 0.05f, final + 0.4, 8f,
                    l * 0.15f, l * 0.6f, new Color(0.18f, 0.15f, 0.14f, 0.5f));
            Add(_rings, Kind.Ring, ship.Position, ship.Velocity, final, 2.0f, l * 1.3f, l * 1.3f, new Color(1f, 0.78f, 0.55f));
            for (int i = 0; i < 24; i++)
            {
                float s = l * (0.008f + R() * 0.025f);
                Add(_chunks, Kind.Chunk, ship.Position + Vec3d.From(ship.Orientation * new Vector3(0, 0, (R() - 0.5f) * l * 0.7f)),
                    ship.Velocity + RandomUnit() * l * (0.03f + R() * 0.09f), final, 40, s, s, new Color(0.16f, 0.17f, 0.19f),
                    axis: RandomUnit() * (0.5f + R() * 2f));
            }
        }));
    }

    private Vector3 RandomUnit()
    {
        Vector3 v;
        do v = new Vector3(R() * 2 - 1, R() * 2 - 1, R() * 2 - 1);
        while (v.LengthSquared() > 1 || v.LengthSquared() < 0.01f);
        return v.Normalized();
    }

    private void Add(Layer layer, Kind kind, Vec3d at, Vector3 velocity, double born, float life, float from, float to, Color color,
        (float Range, float Energy)? light = null, Vector3? axis = null)
    {
        layer.Add(new Particle
        {
            Kind = kind, Origin = at, Velocity = velocity, Born = born, Life = life, From = from, To = to, Color = color,
            Axis = axis ?? RandomUnit(),
        });
        if (light is not { } l) return;
        // 순간광은 8개를 돌려 쓴다. 가장 오래된 것을 덮는다.
        int slot = 0;
        for (int i = 1; i < _lights.Count; i++) if (_lights[i].Born < _lights[slot].Born) slot = i;
        var node = _lights[slot].Node;
        node.LightColor = color; node.OmniRange = l.Range;
        _lights[slot] = (node, at, velocity, born, life, l.Energy);
    }

    /// <summary>매 프레임 렌더 원점 기준으로 놓는다. 판은 카메라 쪽으로 돌린다.</summary>
    public void Sync(Vec3d origin, double time, Camera3D camera)
    {
        if (!Enabled) return;
        Basis facing = camera.GlobalBasis.Orthonormalized();
        Vector3 eye = camera.GlobalPosition;
        _light.Sync(origin, time, facing, eye);
        _rings.Sync(origin, time, facing, eye);
        _smoke.Sync(origin, time, facing, eye);
        _chunks.Sync(origin, time, facing, eye);
        for (int i = 0; i < _lights.Count; i++)
        {
            var (node, at, velocity, born, life, energy) = _lights[i];
            double age = time - born;
            node.Visible = age >= 0 && age < life;
            if (!node.Visible) continue;
            float k = (float)(age / life);
            node.Position = (at + Vec3d.From(velocity * (float)age) - origin).ToVector3();
            node.LightEnergy = energy * (1 - k) * (1 - k);
        }
    }

    /// <summary>한 종류의 판 묶음. 살아 있는 판만 앞쪽에 모아 두고(지운 자리는 맨 뒤 판으로 메운다) 버퍼를 한 번에 넘긴다.</summary>
    private sealed class Layer
    {
        private const int Stride = 16; // Transform3D 12 + Color 4
        private readonly MultiMesh _mesh;
        private readonly Particle[] _items;
        private readonly float[] _buffer;
        public int Count { get; private set; }

        public Layer(Node3D parent, Mesh mesh, Material material, int capacity)
        {
            _items = new Particle[capacity];
            _buffer = new float[capacity * Stride];
            _mesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = capacity,
                VisibleInstanceCount = 0, CustomAabb = new Aabb(new Vector3(-1e6f, -1e6f, -1e6f), new Vector3(2e6f, 2e6f, 2e6f)),
            };
            parent.AddChild(new MultiMeshInstance3D { Multimesh = _mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }

        public void Add(Particle p)
        {
            if (Count < _items.Length) _items[Count++] = p;
        }

        public void Sync(Vec3d origin, double time, Basis facing, Vector3 eye)
        {
            int live = 0;
            for (int i = 0; i < Count; i++)
            {
                ref Particle p = ref _items[i];
                double age = time - p.Born;
                float k = (float)(age / p.Life);
                if (k >= 1)
                {
                    _items[i] = _items[--Count];
                    i--;
                    continue;
                }
                Basis basis;
                Color color;
                Vector3 pos = (p.Origin + Vec3d.From(p.Velocity * (float)Math.Max(age, 0)) - origin).ToVector3();
                if (age < 0) { basis = Basis.Identity.Scaled(Vector3.Zero); color = new Color(0, 0, 0, 0); }
                else
                {
                    if (p.Kind is Kind.Glow or Kind.Fire)
                    {
                        // 선체 안에서 터지는 불꽃이 선체에 가려지지 않게 판을 카메라 쪽으로 당긴다.
                        Vector3 toward = eye - pos;
                        float d = toward.Length();
                        if (d > 1) pos += toward / d * Mathf.Min(p.From * 1.2f, d * 0.5f);
                    }
                    (basis, color) = Shape(ref p, k, (float)age, facing, pos - eye);
                }
                Write(live++, basis, pos, color);
            }
            _mesh.VisibleInstanceCount = live;
            if (live > 0) RenderingServer.MultimeshSetBuffer(_mesh.GetRid(), _buffer);
        }

        private static (Basis, Color) Shape(ref Particle p, float k, float age, Basis facing, Vector3 fromEye)
        {
            switch (p.Kind)
            {
                case Kind.Glow:
                    return (facing.Scaled(Vector3.One * p.From * (0.7f + k * 0.6f)), new Color(p.Color, (1 - k) * (1 - k)));
                case Kind.Fire:
                    float grow = 1 - Mathf.Pow(1 - k, 3);
                    Color c = new Color(1f, 0.93f, 0.75f).Lerp(new Color(1f, 0.5f, 0.15f), Mathf.Min(1, k * 2.5f))
                        .Lerp(new Color(0.55f, 0.12f, 0.03f), Mathf.Clamp(k * 2 - 0.6f, 0, 1));
                    return (facing.Rotated(facing.Z, p.Axis.X * 3).Scaled(Vector3.One * p.From * (0.35f + grow * 1.5f)), new Color(c, (1 - k) * 0.9f));
                case Kind.Puff:
                    float size = Mathf.Lerp(p.From, p.To, Mathf.Sqrt(k));
                    float fade = (1 - k) * Mathf.Min(1, k * 8 + 0.2f);
                    return (facing.Rotated(facing.Z, p.Axis.Y * 3).Scaled(Vector3.One * size), new Color(p.Color, p.Color.A * fade));
                case Kind.Spark:
                    // 진행 방향으로 늘인 빛 조각: 길이 축은 속도, 폭 축은 화면 쪽.
                    Vector3 v = p.Velocity.LengthSquared() > 0.01f ? p.Velocity.Normalized() : Vector3.Up;
                    Vector3 side = v.Cross(fromEye);
                    side = side.LengthSquared() > 1e-6f ? side.Normalized() : facing.X;
                    return (new Basis(side * p.From, v * p.From * 6, side.Cross(v)), new Color(p.Color, 1 - k));
                case Kind.Ring:
                    Basis b = Basis.LookingAt(p.Axis, Mathf.Abs(p.Axis.Dot(Vector3.Up)) > 0.9f ? Vector3.Right : Vector3.Up);
                    return (b.Scaled(Vector3.One * p.From * (0.15f + Mathf.Sqrt(k) * 1.1f)), new Color(p.Color, (1 - k) * (1 - k) * 0.2f));
                default: // Chunk
                    Basis spin = new Basis(p.Axis.Normalized(), p.Axis.Length() * age);
                    return (spin.Scaled(new Vector3(p.From, p.From * 0.6f, p.From * 1.7f)), p.Color);
            }
        }

        private void Write(int index, Basis b, Vector3 o, Color c)
        {
            int j = index * Stride;
            float[] f = _buffer;
            f[j] = b.Column0.X; f[j + 1] = b.Column1.X; f[j + 2] = b.Column2.X; f[j + 3] = o.X;
            f[j + 4] = b.Column0.Y; f[j + 5] = b.Column1.Y; f[j + 6] = b.Column2.Y; f[j + 7] = o.Y;
            f[j + 8] = b.Column0.Z; f[j + 9] = b.Column1.Z; f[j + 10] = b.Column2.Z; f[j + 11] = o.Z;
            f[j + 12] = c.R; f[j + 13] = c.G; f[j + 14] = c.B; f[j + 15] = c.A;
        }
    }
}
