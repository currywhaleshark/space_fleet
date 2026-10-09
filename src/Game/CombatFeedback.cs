using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public enum HitKind { Shield, Armor, Penetration, Critical }

/// <summary>한 번의 확정된 피해 결과. Age는 배속과 무관한 화면 시간이며 일시정지 때 멈춘다.</summary>
public sealed class HitFeedback
{
    public required HitKind Kind { get; init; }
    public required string Label { get; init; }
    public required ShipBody Target { get; init; }
    public Vector3 SourceDirection { get; init; }
    public float Strength { get; init; }
    public bool ShieldBroken { get; init; }
    public float Age { get; set; }
    public float Lifetime => Kind == HitKind.Critical || ShieldBroken ? 1.6f : 1.0f;
    public float Fade => Mathf.Clamp((Lifetime - Age) / .45f, 0, 1);
}

/// <summary>명중 이벤트의 중복 제거와 화면/소리 공통 분류. 전투 판정을 바꾸지 않는다.</summary>
public sealed class CombatFeedback
{
    private readonly HashSet<uint> _seen = new();
    private readonly List<HitFeedback> _incoming = new();
    private readonly Dictionary<string, float> _health = new();
    private SimWorld? _world;
    private ShipBody? _ship;
    private double _time, _collisionTime, _shieldTime;
    private bool _destroyed;
    public IReadOnlyList<HitFeedback> Incoming => _incoming;
    public HitFeedback? Outgoing { get; private set; }
    public event Action<HitFeedback>? Received;
    public event Action<HitFeedback>? Confirmed;

    public void Prime(SimWorld world, ShipBody? ship)
    {
        _world = world; _ship = ship; _time = world.Time;
        _seen.Clear(); _seen.UnionWith(world.Impacts.Select(i => i.Id));
        ClearPresentation(); Snapshot(ship);
    }

    public void ClearPresentation() { _incoming.Clear(); Outgoing = null; }

    private void Snapshot(ShipBody? ship)
    {
        _health.Clear();
        if (ship is null) return;
        foreach (var module in ship.Damage.Modules) _health[module.Definition.Id] = module.Health;
        _collisionTime = ship.LastCollision?.Time ?? double.NegativeInfinity;
        _shieldTime = ship.Damage.LastShieldHitTime; _destroyed = ship.Damage.Destroyed;
    }

    public void Advance(float delta)
    {
        foreach (var hit in _incoming) hit.Age += delta;
        _incoming.RemoveAll(hit => hit.Age >= hit.Lifetime);
        if (Outgoing is { } outgoing && (outgoing.Age += delta) >= outgoing.Lifetime) Outgoing = null;
    }

    public void Observe(SimWorld world, ShipBody? ship, bool paused = false)
    {
        if (world != _world || ship != _ship || world.Time < _time) { Prime(world, ship); return; }
        if (ship is null) return;
        bool received = false;
        // ID는 발사 순서다. 느린 미사일이 나중에 명중할 수 있으므로 최댓값 커서를 쓰지 않는다.
        _seen.IntersectWith(world.Impacts.Select(i => i.Id));
        foreach (var impact in world.Impacts)
        {
            if (!_seen.Add(impact.Id) || paused || impact.Hit.Target is not { } target) continue;
            if (target == ship) { Receive(Describe(impact)); received = true; }
            else if (impact.Shooter == ship && target.Faction != ship.Faction)
            {
                var feedback = Describe(impact);
                // 같은 순간의 작은 탄이 모듈 파괴 알림을 즉시 덮지 않게 한다.
                if (Outgoing is null || Outgoing.Age > (Outgoing.Kind == HitKind.Critical ? .9f : .3f)
                    || Priority(feedback) >= Priority(Outgoing))
                    Outgoing = feedback;
                Confirmed?.Invoke(feedback);
            }
        }

        bool collision = ship.LastCollision is { } contact && contact.Time > _collisionTime && contact.ClosingSpeed > 2;
        var changed = ship.Damage.Modules.Where(m => _health.TryGetValue(m.Definition.Id, out float hp) && m.Health < hp).ToArray();
        if (!paused && (collision || (!received && (changed.Length > 0 || ship.Damage.LastShieldHitTime > _shieldTime
            || (ship.Damage.Destroyed && !_destroyed)))))
        {
            // 충돌·개발용 직접 피해도 처리하되 이미 기록된 무기 명중을 다시 재생하지 않는다.
            bool critical = (ship.Damage.Destroyed && !_destroyed) || changed.Any(m => m.Destroyed);
            HitKind kind = critical ? HitKind.Critical : changed.Length > 0 ? HitKind.Penetration
                : collision ? HitKind.Armor : HitKind.Shield;
            ShipBody? other = collision ? world.Ships.FirstOrDefault(s => s.Callsign == ship.LastCollision!.Value.OtherCallsign) : null;
            Vector3 direction = other is not null ? (other.Position - ship.Position).ToVector3().Normalized()
                : changed.Length > 0 ? (ship.Orientation * changed[0].Definition.Center).Normalized() : Vector3.Zero;
            Receive(new HitFeedback { Kind = kind, Target = ship, SourceDirection = direction,
                Label = ship.Damage.Destroyed ? "선체 붕괴" : changed.FirstOrDefault(m => m.Destroyed) is { } broken
                    ? broken.Definition.Name + " 파괴" : collision ? "선체 충돌" : kind == HitKind.Shield ? "실드 흡수" : "내부 손상",
                Strength = kind switch { HitKind.Shield => .10f, HitKind.Armor => .22f, HitKind.Penetration => .55f, _ => 1f } });
        }
        Snapshot(ship); _time = world.Time;
    }

    private static int Priority(HitFeedback hit) => (int)hit.Kind * 2 + (hit.ShieldBroken ? 1 : 0);

    private void Receive(HitFeedback hit)
    {
        // 같은 방향의 연타는 하나의 표시로 묶는다. 서로 다른 방향은 최대 6개까지 남긴다.
        int index = _incoming.FindIndex(old => old.Age < .25f && old.SourceDirection.Dot(hit.SourceDirection) > .85f);
        if (index >= 0)
        {
            if (Priority(hit) >= Priority(_incoming[index])) _incoming[index] = hit;
        }
        else { if (_incoming.Count >= 6) _incoming.RemoveAt(0); _incoming.Add(hit); }
        Received?.Invoke(hit);
    }

    public static HitFeedback Describe(ProjectileImpact impact)
    {
        ShotResult hit = impact.Hit;
        ShipBody target = hit.Target!;
        ModuleHit? destroyed = hit.Modules.Where(m => m.Destroyed).Select(m => (ModuleHit?)m).FirstOrDefault();
        HitKind kind = impact.TargetDestroyed || destroyed is not null ? HitKind.Critical
            : !hit.HullHit && hit.ShieldAbsorbed>0 ? HitKind.Shield
            : hit.Modules.Count > 0 || (!hit.ShieldStopped && !hit.ArmorStopped) ? HitKind.Penetration
            : hit.ShieldStopped ? HitKind.Shield : HitKind.Armor;
        string label = impact.TargetDestroyed ? "격침" : destroyed is { } module
            ? target.Damage.Module(module.Id).Definition.Name + " 파괴"
            : kind switch { HitKind.Shield => "실드 흡수", HitKind.Armor => "장갑 방어", _ => "관통" };
        if (impact.ShieldBroken) label = kind == HitKind.Shield ? "실드 붕괴" : "실드 붕괴 · " + label;
        float energy = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(impact.Energy, 1) / 300f), .45f, 1.5f);
        float strength = (kind switch { HitKind.Shield => .10f, HitKind.Armor => .22f, HitKind.Penetration => .50f, _ => .85f }) * energy;
        Vector3 source = -impact.IncomingDirection;
        if (source.LengthSquared() < .01f) source = (impact.Shooter.Position - target.Position).ToVector3().Normalized();
        return new HitFeedback { Kind = kind, Target = target, Label = label, ShieldBroken = impact.ShieldBroken,
            SourceDirection = source, Strength = Mathf.Clamp(strength, .05f, 1f) };
    }
}
