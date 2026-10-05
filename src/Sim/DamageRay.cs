using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

public readonly record struct DamagePacket(float Energy, float PenetrationMm, float ModuleDamage, float Range = 200_000f)
{
    public void Validate()
    {
        if (!float.IsFinite(Energy) || Energy <= 0 || !float.IsFinite(PenetrationMm) || PenetrationMm <= 0
            || !float.IsFinite(ModuleDamage) || ModuleDamage <= 0 || !float.IsFinite(Range) || Range <= 0)
            throw new ArgumentException("Damage packet values must be finite and positive");
    }
}
public readonly record struct ModuleHit(string Id, float Damage, bool Destroyed);
public sealed record ShotResult(ShipBody? Target, Vec3d Point, float Distance, bool ShieldStopped, bool ArmorStopped,
    IReadOnlyList<ModuleHit> Modules, string Summary);

/// <summary>명중 뒤의 관통 경로. 외부 실드, 선체 장갑 경계, 내부 모듈, 출구 장갑 순서로 진행한다.</summary>
public static class DamageRay
{
    private readonly record struct Span(float Enter, float Exit, Vector3 EnterNormal, Vector3 ExitNormal);
    private sealed record Region(HullSection EntrySection, Span Entry, HullSection ExitSection, Span Exit);

    public static bool FirstHit(ShipBody ship, Vec3d origin, Vector3 direction, float range, out float distance)
        => FirstHitAtPose(ship, origin, direction, range, ship.Position, ship.Orientation, out distance);

    public static bool FirstHitAtPose(ShipBody ship, Vec3d origin, Vector3 direction, float range,
        Vec3d position, Quaternion orientation, out float distance)
    {
        Vector3 localOrigin = orientation.Inverse() * (origin - position).ToVector3();
        Vector3 localDirection = orientation.Inverse() * direction;
        distance = float.PositiveInfinity;
        foreach (HullSection s in ship.Definition.HullSections)
            if (Box(localOrigin, localDirection, s.Center, s.HalfSize, range, out Span span))
                distance = Mathf.Min(distance, Mathf.Max(0f, span.Enter));
        return float.IsFinite(distance);
    }

    public static ShotResult Apply(ShipBody ship, Vec3d origin, Vector3 direction, DamagePacket packet, double time, uint sequence)
        => ApplyAtPose(ship, origin, direction, packet, time, sequence, ship.Position, ship.Orientation);

    /// <param name="shieldApplies">false면 실드를 건너뛴다(충돌 파쇄처럼 선체가 직접 맞닿는 경우).</param>
    public static ShotResult ApplyAtPose(ShipBody ship, Vec3d origin, Vector3 direction, DamagePacket packet,
        double time, uint sequence, Vec3d position, Quaternion orientation, bool shieldApplies = true)
    {
        packet.Validate();
        if (!direction.IsFinite() || direction.LengthSquared() < 1e-8f) throw new ArgumentException("Invalid ray direction");
        direction = direction.Normalized();
        Vector3 o = orientation.Inverse() * (origin - position).ToVector3();
        Vector3 d = orientation.Inverse() * direction;
        var spans = new List<(HullSection Section, Span Span)>();
        foreach (HullSection s in ship.Definition.HullSections)
            if (Box(o, d, s.Center, s.HalfSize, packet.Range, out Span span)) spans.Add((s, span));
        spans.Sort((a, b) => a.Span.Enter.CompareTo(b.Span.Enter));
        if (spans.Count == 0) return new ShotResult(null, origin, 0, false, false, Array.Empty<ModuleHit>(), "빗나감");

        // 겹치는 부품은 한 선체 구간으로 묶어 내부 경계에 외부 장갑을 중복 적용하지 않는다.
        var regions = new List<Region>();
        foreach (var item in spans)
        {
            if (regions.Count == 0 || item.Span.Enter > regions[^1].Exit.Exit + 0.0001f)
                regions.Add(new Region(item.Section, item.Span, item.Section, item.Span));
            else if (item.Span.Exit > regions[^1].Exit.Exit)
                regions[^1] = regions[^1] with { ExitSection = item.Section, Exit = item.Span };
        }
        var modules = new List<(ModuleState State, Span Span)>();
        foreach (ModuleState m in ship.Damage.Modules)
            if (Box(o, d, m.Definition.Center, m.Definition.HalfSize, packet.Range, out Span span)) modules.Add((m, span));
        modules.Sort((a, b) => a.Span.Enter.CompareTo(b.Span.Enter));
        var hits = new List<ModuleHit>();
        var touched = new HashSet<string>();
        float first = Mathf.Max(0f, spans[0].Span.Enter);
        Vec3d point = origin + Vec3d.From(direction) * first;
        float energy = packet.Energy, penetration = packet.PenetrationMm;
        bool armorStopped = false;
        string summary = "선체 관통";
        if (shieldApplies && spans[0].Span.Enter >= 0f)
        {
            energy = ship.Damage.AbsorbShield(energy, time);
            if (energy <= 0f) return new ShotResult(ship, point, first, true, false, hits, "실드가 차단");
            penetration *= energy / packet.Energy;
        }
        foreach (Region r in regions)
        {
            if (r.Entry.Enter >= 0 && !Armor(r.EntrySection, r.Entry.EnterNormal, entering: true)) break;
            foreach (var item in modules)
            {
                if (item.Span.Enter < r.Entry.Enter - 0.001f || item.Span.Enter > r.Exit.Exit + 0.001f
                    || !touched.Add(item.State.Definition.Id)) continue;
                float path = Mathf.Max(0f, Mathf.Min(item.Span.Exit, packet.Range) - Mathf.Max(0f, item.Span.Enter));
                float fraction = Mathf.Clamp(path / (item.State.Definition.HalfSize.Length() * 2), 0f, 1f);
                float amount = packet.ModuleDamage * penetration / packet.PenetrationMm * fraction;
                float damage = ship.Damage.Hurt(item.State, amount, time, energy, sequence);
                if (damage > 0) hits.Add(new ModuleHit(item.State.Definition.Id, damage, item.State.Destroyed));
                float before = penetration;
                penetration = Mathf.Max(0, penetration - item.State.Definition.ResistanceMm * fraction);
                energy *= before > 0 ? penetration / before : 0;
                if (penetration <= 0 || ship.Damage.Destroyed)
                { summary = ship.Damage.Destroyed ? "치명 파괴" : "내부 모듈에서 정지"; goto Done; }
            }
            if (r.Exit.Exit <= packet.Range && !Armor(r.ExitSection, r.Exit.ExitNormal, entering: false)) break;
        }
        Done:
        if (hits.Count > 0 && summary == "선체 관통") summary = $"{hits.Count}개 모듈 타격";
        return new ShotResult(ship, point, first, false, armorStopped, hits, summary);

        bool Armor(HullSection section, Vector3 normal, bool entering)
        {
            ArmorPlate plate = section.Plate(Side(normal));
            // 입사각과 데이터의 장갑 경사를 합성한 프로토타입 등가 두께. 도탄은 사격 단계에서 확장한다.
            float effective = plate.ThicknessMm / Mathf.Max(0.05f, Mathf.Abs(d.Dot(normal)) * Mathf.Cos(Mathf.DegToRad(plate.SlopeDegrees)));
            float before = penetration;
            penetration = Mathf.Max(0, penetration - effective);
            energy *= before > 0 ? penetration / before : 0;
            if (penetration > 0) return true;
            armorStopped = true;
            summary = entering ? $"{section.Name} 장갑에 차단" : $"{section.Name} 출구 장갑에서 정지";
            ship.Damage.Report(time, summary);
            return false;
        }
    }

    private static ArmorSide Side(Vector3 normal) => normal.X < -0.5f ? ArmorSide.Port : normal.X > 0.5f ? ArmorSide.Starboard
        : normal.Y < -0.5f ? ArmorSide.Ventral : normal.Y > 0.5f ? ArmorSide.Dorsal : normal.Z < 0 ? ArmorSide.Fore : ArmorSide.Aft;

    private static bool Box(Vector3 origin, Vector3 direction, Vector3 center, Vector3 half, float range, out Span span)
    {
        float near = float.NegativeInfinity, far = float.PositiveInfinity;
        Vector3 nearNormal = Vector3.Zero, farNormal = Vector3.Zero;
        Vector3 relative = origin - center;
        for (int axis = 0; axis < 3; axis++)
        {
            float p = relative[axis], v = direction[axis], h = half[axis];
            if (Mathf.Abs(v) < 1e-7f) { if (Mathf.Abs(p) > h) { span = default; return false; } continue; }
            float a = (-h - p) / v, b = (h - p) / v;
            Vector3 n = axis == 0 ? Vector3.Right : axis == 1 ? Vector3.Up : Vector3.Back;
            Vector3 na = -n, nb = n;
            if (a > b) { (a, b) = (b, a); (na, nb) = (nb, na); }
            if (a > near) { near = a; nearNormal = na; }
            if (b < far) { far = b; farNormal = nb; }
            if (near > far) { span = default; return false; }
        }
        span = new Span(near, far, nearNormal, farNormal);
        return far >= 0 && near <= range;
    }
}
