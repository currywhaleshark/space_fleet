using System;
using System.Collections.Generic;

namespace SpaceFleet.Sim;

/// <summary>Static, double precision BVH. Leaves identify original insertion indices.</summary>
internal sealed class ObstacleBvh
{
    private readonly record struct Node(Vec3d Min,Vec3d Max,int Left,int Right,int Item);
    private readonly Node[] _nodes;
    private readonly IReadOnlyList<Obstacle> _items;
    private const double CellSize=4000;
    private readonly Dictionary<(long X,long Y,long Z),int[]>? _cells;
    private readonly (Vec3d Min,Vec3d Max)[] _itemBounds;
    public ObstacleBvh(IReadOnlyList<Obstacle> items)
    {
        _items=items;
        int[] order=new int[items.Count];for(int i=0;i<order.Length;i++)order[i]=i;
        var bounds=_itemBounds=new (Vec3d Min,Vec3d Max)[items.Count];
        for(int i=0;i<items.Count;i++)
        {
            var obstacle=items[i];
            Vec3d min=new(double.PositiveInfinity,double.PositiveInfinity,double.PositiveInfinity),max=-min;
            foreach(var part in obstacle.Parts)
            {
                Vec3d a=obstacle.ToWorld(part.A),b=obstacle.ToWorld(part.B);double r=part.Radius;
                min=new(Math.Min(min.X,Math.Min(a.X,b.X)-r),Math.Min(min.Y,Math.Min(a.Y,b.Y)-r),Math.Min(min.Z,Math.Min(a.Z,b.Z)-r));
                max=new(Math.Max(max.X,Math.Max(a.X,b.X)+r),Math.Max(max.Y,Math.Max(a.Y,b.Y)+r),Math.Max(max.Z,Math.Max(a.Z,b.Z)+r));
            }
            bounds[i]=(min,max);
        }
        var nodes=new List<Node>(items.Count*2);
        int Build(int start,int count)
        {
            Vec3d min=new(double.PositiveInfinity,double.PositiveInfinity,double.PositiveInfinity),max=-min;
            for(int i=start;i<start+count;i++)
            {
                var b=bounds[order[i]];
                min=new(Math.Min(min.X,b.Min.X),Math.Min(min.Y,b.Min.Y),Math.Min(min.Z,b.Min.Z));
                max=new(Math.Max(max.X,b.Max.X),Math.Max(max.Y,b.Max.Y),Math.Max(max.Z,b.Max.Z));
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
        _cells=BuildCells(bounds);
    }
    private static (long X,long Y,long Z) Cell(Vec3d p)=>((long)Math.Floor(p.X/CellSize),(long)Math.Floor(p.Y/CellSize),(long)Math.Floor(p.Z/CellSize));
    private static Dictionary<(long X,long Y,long Z),int[]>? BuildCells((Vec3d Min,Vec3d Max)[] bounds)
    {
        // Cache only bounded, local cells. Unusually huge geometry keeps using
        // the BVH without an unbounded grid allocation.
        double total=0;
        foreach(var b in bounds)
        {
            if(Math.Max(Math.Abs(b.Min.X),Math.Abs(b.Max.X))>1e18 || Math.Max(Math.Abs(b.Min.Y),Math.Abs(b.Max.Y))>1e18 || Math.Max(Math.Abs(b.Min.Z),Math.Abs(b.Max.Z))>1e18)return null;
            var lo=Cell(b.Min);var hi=Cell(b.Max);
            total+=((double)hi.X-lo.X+1)*((double)hi.Y-lo.Y+1)*((double)hi.Z-lo.Z+1);
            if(total>200000)return null;
        }
        var cells=new Dictionary<(long X,long Y,long Z),List<int>>();
        for(int i=0;i<bounds.Length;i++)
        {
            var lo=Cell(bounds[i].Min);var hi=Cell(bounds[i].Max);
            for(long z=lo.Z;z<=hi.Z;z++)for(long y=lo.Y;y<=hi.Y;y++)for(long x=lo.X;x<=hi.X;x++)
            {
                var key=(x,y,z);if(!cells.TryGetValue(key,out var list))cells[key]=list=new();
                list.Add(i);
            }
        }
        var result=new Dictionary<(long X,long Y,long Z),int[]>(cells.Count);
        foreach(var cell in cells)result.Add(cell.Key,cell.Value.ToArray());
        return result;
    }
    public int Query(Vec3d a,Vec3d b,double padding,Span<int> result)
    {
        if(_nodes.Length==0)return 0;
        int count=0;Vec3d travel=b-a;double margin=ConservativePadding(a,padding);
        if(Math.Max(Math.Abs(travel.X),Math.Max(Math.Abs(travel.Y),Math.Abs(travel.Z)))<1000)
        {
            // Short physics sweeps and point queries need no per-node division.
            // A segment AABB can only admit extra candidates; narrow phase and
            // insertion ordering still decide every actual contact.
            Vec3d min=new(Math.Min(a.X,b.X)-margin,Math.Min(a.Y,b.Y)-margin,Math.Min(a.Z,b.Z)-margin);
            Vec3d max=new(Math.Max(a.X,b.X)+margin,Math.Max(a.Y,b.Y)+margin,Math.Max(a.Z,b.Z)+margin);
            if(_cells is not null && Cell(min) is var cell && cell==Cell(max))
            {
                if(!_cells.TryGetValue(cell,out var items))return 0;
                foreach(int i in items)
                {
                    var bounds=_itemBounds[i];
                    if(max.X<bounds.Min.X || min.X>bounds.Max.X || max.Y<bounds.Min.Y || min.Y>bounds.Max.Y || max.Z<bounds.Min.Z || min.Z>bounds.Max.Z)continue;
                    result[count++]=i;
                }
                return count; // Cell entries were built in insertion order.
            }
            QueryBox(0,min,max,result,ref count);
        }
        else QueryNode(0,a,travel,margin,result,ref count);
        result[..count].Sort();return count;
    }
    public bool Blocks(Vec3d a,Vec3d b,double padding)=>_nodes.Length>0 && BlocksNode(0,a,b,b-a,padding,ConservativePadding(a,padding));
    private bool BlocksNode(int index,Vec3d a,Vec3d b,Vec3d travel,double padding,double boundsPadding)
    {
        ref readonly var node=ref _nodes[index];if(!Bounds(a,travel,node.Min,node.Max,boundsPadding))return false;
        if(node.Item>=0)return ObstacleSet.Blocks(_items[node.Item],a,b,padding);
        return BlocksNode(node.Left,a,b,travel,padding,boundsPadding) || BlocksNode(node.Right,a,b,travel,padding,boundsPadding);
    }
    private void QueryNode(int index,Vec3d start,Vec3d travel,double padding,Span<int> result,ref int count)
    {
        ref readonly var node=ref _nodes[index];
        if(!Bounds(start,travel,node.Min,node.Max,padding))return;
        if(node.Item>=0){result[count++]=node.Item;return;}
        QueryNode(node.Left,start,travel,padding,result,ref count);QueryNode(node.Right,start,travel,padding,result,ref count);
    }
    private void QueryBox(int index,Vec3d min,Vec3d max,Span<int> result,ref int count)
    {
        ref readonly var node=ref _nodes[index];
        if(max.X<node.Min.X || min.X>node.Max.X || max.Y<node.Min.Y || min.Y>node.Max.Y || max.Z<node.Min.Z || min.Z>node.Max.Z)return;
        if(node.Item>=0){result[count++]=node.Item;return;}
        QueryBox(node.Left,min,max,result,ref count);QueryBox(node.Right,min,max,result,ref count);
    }
    // Hoist the identical distant-origin margin out of every tree node visit.
    private static double ConservativePadding(Vec3d start,double padding)=>padding+.01+Math.Max(Math.Abs(start.X),Math.Max(Math.Abs(start.Y),Math.Abs(start.Z)))*2e-15;
    private static bool Bounds(Vec3d start,Vec3d travel,Vec3d min,Vec3d max,double padding)
    {
        double lo=0,hi=1;
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
