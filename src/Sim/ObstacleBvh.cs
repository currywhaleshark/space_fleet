using System;
using System.Collections.Generic;

namespace SpaceFleet.Sim;

/// <summary>Static, double precision BVH. Leaves identify original insertion indices.</summary>
internal sealed class ObstacleBvh
{
    private readonly record struct Node(Vec3d Min,Vec3d Max,int Left,int Right,int Item);
    private readonly Node[] _nodes;
    private readonly IReadOnlyList<Obstacle> _items;
    public ObstacleBvh(IReadOnlyList<Obstacle> items)
    {
        _items=items;
        int[] order=new int[items.Count];for(int i=0;i<order.Length;i++)order[i]=i;
        var nodes=new List<Node>(items.Count*2);
        int Build(int start,int count)
        {
            Vec3d min=new(double.PositiveInfinity,double.PositiveInfinity,double.PositiveInfinity),max=-min;
            for(int i=start;i<start+count;i++)
            {
                var o=items[order[i]];double r=o.BoundingRadius;
                min=new(Math.Min(min.X,o.Position.X-r),Math.Min(min.Y,o.Position.Y-r),Math.Min(min.Z,o.Position.Z-r));
                max=new(Math.Max(max.X,o.Position.X+r),Math.Max(max.Y,o.Position.Y+r),Math.Max(max.Z,o.Position.Z+r));
            }
            int index=nodes.Count;nodes.Add(default);
            if(count==1){nodes[index]=new(min,max,-1,-1,order[start]);return index;}
            Vec3d extent=max-min;int axis=extent.X>=extent.Y && extent.X>=extent.Z?0:extent.Y>=extent.Z?1:2;
            Array.Sort(order,start,count,Comparer<int>.Create((a,b)=>
            {int d=ObstacleMath.Component(items[a].Position,axis).CompareTo(ObstacleMath.Component(items[b].Position,axis));return d==0?a.CompareTo(b):d;}));
            int half=count/2,left=Build(start,half),right=Build(start+half,count-half);
            nodes[index]=new(min,max,left,right,-1);return index;
        }
        if(items.Count>0)Build(0,items.Count);_nodes=nodes.ToArray();
    }
    public int Query(Vec3d a,Vec3d b,double padding,Span<int> result)
    {
        if(_nodes.Length==0)return 0;
        int count=0;QueryNode(0,a,b-a,padding,result,ref count);result[..count].Sort();return count;
    }
    public bool Blocks(Vec3d a,Vec3d b,double padding)=>_nodes.Length>0 && BlocksNode(0,a,b,b-a,padding);
    private bool BlocksNode(int index,Vec3d a,Vec3d b,Vec3d travel,double padding)
    {
        var node=_nodes[index];if(!Bounds(a,travel,node.Min,node.Max,padding))return false;
        if(node.Item>=0)return ObstacleSet.Blocks(_items[node.Item],a,b,padding);
        return BlocksNode(node.Left,a,b,travel,padding) || BlocksNode(node.Right,a,b,travel,padding);
    }
    private void QueryNode(int index,Vec3d start,Vec3d travel,double padding,Span<int> result,ref int count)
    {
        var node=_nodes[index];
        if(!Bounds(start,travel,node.Min,node.Max,padding))return;
        if(node.Item>=0){result[count++]=node.Item;return;}
        QueryNode(node.Left,start,travel,padding,result,ref count);QueryNode(node.Right,start,travel,padding,result,ref count);
    }
    private static bool Bounds(Vec3d start,Vec3d travel,Vec3d min,Vec3d max,double padding)
    {
        double lo=0,hi=1;
        // Conservative allowance for the legacy quadratic's tangent tolerance and
        // AABB endpoint quantization at distant origins. Never changes narrow phase.
        padding+=.01+Math.Max(Math.Abs(start.X),Math.Max(Math.Abs(start.Y),Math.Abs(start.Z)))*2e-15;
        for(int axis=0;axis<3;axis++)
        {
            double p=ObstacleMath.Component(start,axis),d=ObstacleMath.Component(travel,axis),
                a=ObstacleMath.Component(min,axis)-p-padding,b=ObstacleMath.Component(max,axis)-p+padding;
            if(Math.Abs(d)<1e-20){if(a>0 || b<0)return false;continue;}
            double x=a/d,y=b/d;if(x>y)(x,y)=(y,x);lo=Math.Max(lo,x);hi=Math.Min(hi,y);if(lo>hi)return false;
        }
        return true;
    }
}
