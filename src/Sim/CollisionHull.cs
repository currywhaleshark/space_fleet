using System.Collections.Generic;
using Godot;

namespace SpaceFleet.Sim;

/// <summary>로컬 축에 정렬된 선체 부품. HalfSize는 전체 크기의 절반(m).</summary>
public readonly record struct HullBox(Vector3 Center, Vector3 HalfSize);

/// <summary>
/// 절차적 모델의 주요 부품을 근사한 복합 충돌체. 노드·메시를 읽지 않으므로 서버에서도 사용한다.
/// 포신·안테나·그리블·엔진 화염은 충돌체에 포함하지 않는다.
/// </summary>
public sealed class CollisionHull
{
    public CollisionHull(params HullBox[] boxes)
    {
        Boxes = System.Array.AsReadOnly(boxes);
        Vector3 minimum = boxes[0].Center - boxes[0].HalfSize;
        Vector3 maximum = boxes[0].Center + boxes[0].HalfSize;
        foreach (HullBox box in boxes)
        {
            BoundingRadius = Mathf.Max(BoundingRadius, box.Center.Length() + box.HalfSize.Length());
            minimum = minimum.Min(box.Center - box.HalfSize);
            maximum = maximum.Max(box.Center + box.HalfSize);
        }
        Bounds = new HullBox((minimum + maximum) * 0.5f, (maximum - minimum) * 0.5f);
    }

    public IReadOnlyList<HullBox> Boxes { get; }
    public float BoundingRadius { get; }
    public HullBox Bounds { get; }

    public static CollisionHull For(HullKind kind) => ShipDefinitions.For(kind).Hull;
}
