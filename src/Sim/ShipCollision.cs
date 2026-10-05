using System;
using System.Collections.Generic;
using Godot;

namespace SpaceFleet.Sim;

public readonly record struct CollisionImpact(string OtherCallsign, double Time, float ClosingSpeed, float DeltaSpeed);

/// <summary>
/// 복합 OBB의 연속 선형 충돌과 질량별 비탄성 반응. 위치 차이는 double로 뺀 뒤 로컬 계산만 float로 한다.
/// 자세 변화는 SimWorld의 하위 틱으로 나눠 계산한다. 충격 피해는 CollisionDamage가 에너지 기준으로 계산하며 충격 각속도는 아직 없다.
/// </summary>
public static class ShipCollision
{
    private const float Skin = 0.02f;
    private const float Restitution = 0.05f;
    private const float Friction = 0.15f;
    private const int OverlapPasses = 8;

    private readonly record struct Contact(Vector3 Normal, float Correction, float Time)
    {
        public Vector3 ArmA { get; init; }
        public Vector3 ArmB { get; init; }
    }

    public static void Resolve(IReadOnlyList<ShipBody> ships, double time)
    {
        // 한 하위 틱의 이동 구간을 검사하므로 끝 위치가 이미 반대편이어도 통과를 막는다.
        for (int i = 0; i < ships.Count; i++)
        for (int j = i + 1; j < ships.Count; j++)
        {
            ShipBody a = ships[i], b = ships[j];
            if (BroadPhase(a, b, swept: true) && TryContact(a, b, swept: true, out Contact contact))
                Apply(a, b, contact, time);
        }

        // 여러 부품·여러 함선의 동시 접촉 및 시작부터 겹친 배치를 분리한다.
        for (int pass = 0; pass < OverlapPasses; pass++)
        {
            bool corrected = false;
            for (int i = 0; i < ships.Count; i++)
            for (int j = i + 1; j < ships.Count; j++)
            {
                ShipBody a = ships[i], b = ships[j];
                if (BroadPhase(a, b, swept: false) && TryContact(a, b, swept: false, out Contact contact))
                {
                    Apply(a, b, contact, time);
                    corrected = true;
                }
            }
            if (!corrected)
                break;
        }
        // 선체 안에 생성된 등 유효하지 않은 깊은 겹침은 전체 외곽으로 빼낸다.
        // 일반 근접 비행에는 부품별 판정만 사용한다.
        for (int i = 0; i < ships.Count; i++)
        for (int j = i + 1; j < ships.Count; j++)
        {
            ShipBody a = ships[i], b = ships[j];
            if (Overlaps(a, b) && TryBoundsContact(a, b, out Contact contact))
                Apply(a, b, contact, time);
        }
    }

    public static bool Overlaps(ShipBody a, ShipBody b) =>
        BroadPhase(a, b, swept: false) && TryContact(a, b, swept: false, out _);

    private static bool BroadPhase(ShipBody a, ShipBody b, bool swept)
    {
        Vec3d relative = b.Position - a.Position;
        if (swept)
        {
            Vec3d start = b.PrevPosition - a.PrevPosition;
            Vec3d travel = relative - start;
            double lengthSquared = travel.LengthSquared();
            double t = lengthSquared > 1e-12 ? Math.Clamp(-start.Dot(travel) / lengthSquared, 0.0, 1.0) : 0.0;
            relative = start + travel * t;
        }
        double radius = a.Hull.BoundingRadius + b.Hull.BoundingRadius + Skin;
        return relative.LengthSquared() <= radius * radius;
    }

    private static bool TryContact(ShipBody a, ShipBody b, bool swept, out Contact best)
    {
        best = new Contact(Vector3.Zero, float.PositiveInfinity, float.PositiveInfinity);
        bool found = false;
        // 분리축 15개: A의 세 축, B의 세 축, 두 축의 외적 아홉 개.
        Span<Vector3> axes = stackalloc Vector3[15];
        var basisA = new Basis(a.Orientation);
        var basisB = new Basis(b.Orientation);
        FillAxes(basisA, basisB, axes);

        Vector3 relativeEnd = (b.Position - a.Position).ToVector3();
        Vector3 relativeStart = swept ? (b.PrevPosition - a.PrevPosition).ToVector3() : relativeEnd;
        foreach (HullBox boxA in a.Hull.Boxes)
        foreach (HullBox boxB in b.Hull.Boxes)
        {
            Vector3 offset = basisB * boxB.Center - basisA * boxA.Center;
            if (!BoxContact(relativeStart + offset, relativeEnd + offset, basisA, boxA.HalfSize,
                basisB, boxB.HalfSize, axes, swept, out Contact candidate))
                continue;
            Vector3 pointA = basisA * boxA.Center + Support(basisA, boxA.HalfSize, candidate.Normal);
            Vector3 pointB = relativeEnd + basisB * boxB.Center - Support(basisB, boxB.HalfSize, candidate.Normal);
            Vector3 point = (pointA + pointB) * 0.5f;
            candidate = candidate with { ArmA = point, ArmB = point - relativeEnd };
            // 연속 충돌은 가장 먼저 닿은 부품, 겹침 분리는 가장 깊은 부품부터 밀어낸다.
            if (!found || candidate.Time < best.Time ||
                (candidate.Time == best.Time && candidate.Correction > best.Correction))
                best = candidate;
            found = true;
        }
        return found;
    }

    private static void FillAxes(Basis basisA, Basis basisB, Span<Vector3> axes)
    {
        axes[0] = basisA.X; axes[1] = basisA.Y; axes[2] = basisA.Z;
        axes[3] = basisB.X; axes[4] = basisB.Y; axes[5] = basisB.Z;
        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
        {
            Vector3 cross = axes[i].Cross(axes[j + 3]);
            axes[6 + i * 3 + j] = cross.LengthSquared() > 1e-10f ? cross.Normalized() : Vector3.Zero;
        }
    }

    private static bool TryBoundsContact(ShipBody a, ShipBody b, out Contact contact)
    {
        Span<Vector3> axes = stackalloc Vector3[15];
        var basisA = new Basis(a.Orientation);
        var basisB = new Basis(b.Orientation);
        FillAxes(basisA, basisB, axes);
        HullBox boxA = a.Hull.Bounds, boxB = b.Hull.Bounds;
        Vector3 relative = (b.Position - a.Position).ToVector3() + basisB * boxB.Center - basisA * boxA.Center;
        return BoxContact(relative, relative, basisA, boxA.HalfSize, basisB, boxB.HalfSize, axes, swept: false, out contact);
    }

    private static bool BoxContact(Vector3 start, Vector3 end, Basis a, Vector3 halfA,
        Basis b, Vector3 halfB, ReadOnlySpan<Vector3> axes, bool swept, out Contact contact)
    {
        contact = default;
        float entry = 0f, exit = 1f;
        Vector3 entryNormal = Vector3.Zero;
        float entryRadius = 0f;
        bool endOverlaps = true;
        float leastDepth = float.PositiveInfinity;
        Vector3 overlapNormal = Vector3.Zero;
        foreach (Vector3 axis in axes)
        {
            if (axis == Vector3.Zero)
                continue;
            float radius = Radius(a, halfA, axis) + Radius(b, halfB, axis);
            float endProjection = end.Dot(axis);
            float depth = radius - Mathf.Abs(endProjection);
            if (depth <= 0f)
                endOverlaps = false;
            if (depth < leastDepth)
            {
                leastDepth = depth;
                overlapNormal = endProjection < 0f ? -axis : axis;
            }
            if (!swept)
                continue;
            float startProjection = start.Dot(axis);
            float travel = endProjection - startProjection;
            if (Mathf.Abs(travel) < 1e-7f)
            {
                if (Mathf.Abs(startProjection) > radius)
                    return false;
                continue;
            }
            float first = (-radius - startProjection) / travel;
            float last = (radius - startProjection) / travel;
            if (first > last)
                (first, last) = (last, first);
            if (first > entry)
            {
                entry = first;
                entryNormal = startProjection + travel * first < 0f ? -axis : axis;
                entryRadius = radius;
            }
            exit = Mathf.Min(exit, last);
            if (entry > exit)
                return false;
        }

        if (swept && entryNormal != Vector3.Zero && entry <= 1f && exit >= 0f)
        {
            // 접촉 법선 방향만 되돌리고 접선 이동은 유지한다. 스칠 때 시작 위치로 되감지 않는다.
            float correction = entryRadius - end.Dot(entryNormal);
            contact = new Contact(entryNormal, Mathf.Max(0f, correction) + Skin, entry);
            return true;
        }
        if (!endOverlaps)
            return false;
        contact = new Contact(overlapNormal, leastDepth + Skin, 0f);
        return true;
    }

    private static float Radius(Basis basis, Vector3 half, Vector3 axis) =>
        Mathf.Abs(basis.X.Dot(axis)) * half.X + Mathf.Abs(basis.Y.Dot(axis)) * half.Y + Mathf.Abs(basis.Z.Dot(axis)) * half.Z;

    private static Vector3 Support(Basis basis, Vector3 half, Vector3 normal)
    {
        float Sign(float value) => Mathf.Abs(value) > 1e-5f ? Mathf.Sign(value) : 0f;
        return basis.X * half.X * Sign(basis.X.Dot(normal)) + basis.Y * half.Y * Sign(basis.Y.Dot(normal))
            + basis.Z * half.Z * Sign(basis.Z.Dot(normal));
    }

    private static void Apply(ShipBody a, ShipBody b, Contact contact, double time)
    {
        double invA = 1.0 / a.Class.MassKg, invB = 1.0 / b.Class.MassKg;
        double inverseSum = invA + invB;
        Vector3 normal = contact.Normal;
        Vec3d separation = Vec3d.From(normal) * contact.Correction;
        a.Position -= separation * (invA / inverseSum);
        b.Position += separation * (invB / inverseSum);

        // 회전하는 방열판·날개도 접촉면의 속도(ω × r)로 주변 함선을 밀어낸다.
        Vector3 surfaceA = a.Velocity + (a.Orientation * a.AngularVelocity).Cross(contact.ArmA);
        Vector3 surfaceB = b.Velocity + (b.Orientation * b.AngularVelocity).Cross(contact.ArmB);
        Vector3 relativeVelocity = surfaceB - surfaceA;
        float closing = -relativeVelocity.Dot(normal);
        if (closing <= 0f)
            return;
        Vector3 beforeA = a.Velocity, beforeB = b.Velocity;
        // 약한 접촉은 튕기지 않게 하여 계속 추력을 줄 때의 잔떨림을 줄인다.
        float restitution = closing > 2f ? Restitution : 0f;
        double impulse = (1.0 + restitution) * closing / inverseSum;
        a.Velocity -= normal * (float)(impulse * invA);
        b.Velocity += normal * (float)(impulse * invB);

        Vector3 tangent = relativeVelocity - normal * relativeVelocity.Dot(normal);
        float tangentSpeed = tangent.Length();
        if (tangentSpeed > 1e-6f)
        {
            double frictionImpulse = Math.Min(Friction * impulse, tangentSpeed / inverseSum);
            Vector3 direction = tangent / tangentSpeed;
            a.Velocity += direction * (float)(frictionImpulse * invA);
            b.Velocity -= direction * (float)(frictionImpulse * invB);
        }
        if (closing > 2f)
        {
            RecordImpact(a, b, time, closing, a.Velocity.DistanceTo(beforeA));
            RecordImpact(b, a, time, closing, b.Velocity.DistanceTo(beforeB));
            // 소산 에너지는 양쪽 선체에 모두 들어간다. 무거운 쪽도 접촉점이 파쇄된다.
            double energy = CollisionDamage.DissipatedEnergy(a.Class.MassKg, b.Class.MassKg, closing, restitution);
            CollisionDamage.Apply(a, b, contact.ArmA, -normal, energy, time);
            CollisionDamage.Apply(b, a, contact.ArmB, normal, energy, time);
        }
    }

    private static void RecordImpact(ShipBody ship, ShipBody other, double time, float closing, float deltaSpeed)
    {
        if (ship.LastCollision is not CollisionImpact previous || time - previous.Time > 0.1 || closing > previous.ClosingSpeed)
            ship.LastCollision = new CollisionImpact(other.Callsign, time, closing, deltaSpeed);
    }
}
