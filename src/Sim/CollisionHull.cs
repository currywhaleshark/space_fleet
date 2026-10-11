using System.Collections.Generic;
using Godot;

namespace SpaceFleet.Sim;

/// <summary>로컬 축에 정렬된 선체 부품. HalfSize는 전체 크기의 절반(m).</summary>
public readonly record struct HullBox(Vector3 Center, Vector3 HalfSize);
internal readonly record struct HullGroup(int Start, int Count, HullBox Bounds);

/// <summary>
/// 절차적 모델의 주요 부품을 근사한 복합 충돌체. 노드·메시를 읽지 않으므로 서버에서도 사용한다.
/// 포신·안테나·그리블·엔진 화염은 충돌체에 포함하지 않는다.
/// </summary>
public sealed class CollisionHull
{
    private readonly HullBox[] _boxes;
    private HullGroup[] _groups;
    public CollisionHull(params HullBox[] boxes)
    {
        _boxes = boxes;
        _groups = new[] { new HullGroup(0, boxes.Length, default) };
        Boxes = System.Array.AsReadOnly(boxes);
        RefreshBounds();
    }

    // Only the owning wreck updates this buffer. Definition hulls remain immutable.
    internal System.Span<HullBox> MutableBoxes => _boxes;
    internal System.ReadOnlySpan<HullGroup> Groups => _groups;
    internal void SetGroups(System.ReadOnlySpan<int> counts)
    {
        _groups = new HullGroup[counts.Length];
        int start = 0;
        for (int i = 0; i < counts.Length; i++)
        {
            _groups[i] = new(start, counts[i], default);
            start += counts[i];
        }
        if (start != _boxes.Length) throw new System.ArgumentException("Hull groups must cover every box exactly once.");
    }
    internal void RefreshBounds()
    {
        BoundingRadius = 0;
        Vector3 minimum = _boxes[0].Center - _boxes[0].HalfSize;
        Vector3 maximum = _boxes[0].Center + _boxes[0].HalfSize;
        foreach (HullBox box in _boxes)
        {
            BoundingRadius = Mathf.Max(BoundingRadius, box.Center.Length() + box.HalfSize.Length());
            minimum = minimum.Min(box.Center - box.HalfSize);
            maximum = maximum.Max(box.Center + box.HalfSize);
        }
        Bounds = new HullBox((minimum + maximum) * 0.5f, (maximum - minimum) * 0.5f);
        for (int i = 0; i < _groups.Length; i++)
        {
            var group = _groups[i];
            Vector3 lo = _boxes[group.Start].Center - _boxes[group.Start].HalfSize;
            Vector3 hi = _boxes[group.Start].Center + _boxes[group.Start].HalfSize;
            for (int j = group.Start + 1; j < group.Start + group.Count; j++)
            {
                lo = lo.Min(_boxes[j].Center - _boxes[j].HalfSize);
                hi = hi.Max(_boxes[j].Center + _boxes[j].HalfSize);
            }
            _groups[i] = group with { Bounds = new((lo + hi) * .5f, (hi - lo) * .5f) };
        }
    }

    public IReadOnlyList<HullBox> Boxes { get; }
    public float BoundingRadius { get; private set; }
    public HullBox Bounds { get; private set; }

    public static CollisionHull For(HullKind kind) => ShipDefinitions.For(kind).Hull;
}
