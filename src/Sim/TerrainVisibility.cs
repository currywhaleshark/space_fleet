using System;
using System.Collections.Generic;

namespace SpaceFleet.Sim;

internal sealed partial class ObstacleBvh
{
    /// <summary>Fixed-observer directional broad phase. Every candidate still uses the original exact ray.</summary>
    internal sealed class VisibilityQuery(ObstacleBvh index,Vec3d origin)
    {
        private const int Resolution=8;
        private readonly Dictionary<int,int[]> _bins=new();
        private readonly double _padding=ConservativePadding(origin,0);
        public bool Blocks(Vec3d end)
        {
            Vec3d travel=end-origin;
            double ax=Math.Abs(travel.X),ay=Math.Abs(travel.Y),az=Math.Abs(travel.Z);
            double largest=Math.Max(ax,Math.Max(ay,az));
            // Keep the original path for degenerate or unbounded coordinates.
            if(!ObstacleMath.Finite(end) || !ObstacleMath.Finite(origin) || largest<1e-10 || largest>1e15
                || Math.Max(Math.Abs(origin.X),Math.Max(Math.Abs(origin.Y),Math.Abs(origin.Z)))>1e15)
                return index.Blocks(origin,end,0);
            int axis=ax>=ay && ax>=az?0:ay>=az?1:2;
            int face=axis*2+(ObstacleMath.Component(travel,axis)<0?1:0);
            double u=ObstacleMath.Component(travel,(axis+1)%3)/largest;
            double v=ObstacleMath.Component(travel,(axis+2)%3)/largest;
            int x=Math.Clamp((int)((u+1)*(Resolution*.5)),0,Resolution-1);
            int y=Math.Clamp((int)((v+1)*(Resolution*.5)),0,Resolution-1);
            int key=(face*Resolution+y)*Resolution+x;
            if(!_bins.TryGetValue(key,out var candidates))
            {
                if(_bins.Count>=256)return index.Blocks(origin,end,0);
                var found=new List<int>();
                // Widen beyond division rounding at face/cell seams. Geometry
                // has twice the original world-coordinate padding as well.
                double loU=x*(2.0/Resolution)-1-2e-12,hiU=(x+1)*(2.0/Resolution)-1+2e-12;
                double loV=y*(2.0/Resolution)-1-2e-12,hiV=(y+1)*(2.0/Resolution)-1+2e-12;
                index.AngularCandidates(0,origin,face,loU,hiU,loV,hiV,_padding*2,found);
                _bins.Add(key,candidates=found.ToArray());
            }
            foreach(int item in candidates)
            {
                var bounds=index._itemBounds[item];
                // Same leaf bounds, padding and narrow phase as BlocksNode.
                // A leaf is contained by every ancestor, so a passing leaf
                // needs no repeated ancestor tests. Order cannot change a bool.
                if(Bounds(origin,travel,bounds.Min,bounds.Max,_padding)
                    && ObstacleSet.Blocks(index._items[item],origin,end,0))return true;
            }
            return false;
        }
    }

    private void AngularCandidates(int nodeIndex,Vec3d origin,int face,double loU,double hiU,double loV,double hiV,double padding,List<int> found)
    {
        ref readonly var node=ref _nodes[nodeIndex];
        Vec3d min=node.Min-origin-new Vec3d(padding,padding,padding);
        Vec3d max=node.Max-origin+new Vec3d(padding,padding,padding);
        int axis=face/2,u=(axis+1)%3,v=(axis+2)%3;
        double loD=ObstacleMath.Component(min,axis),hiD=ObstacleMath.Component(max,axis);
        if((face&1)!=0)(loD,hiD)=(-hiD,-loD);
        if(hiD<0)return;
        double minU=ObstacleMath.Component(min,u),maxU=ObstacleMath.Component(max,u);
        double minV=ObstacleMath.Component(min,v),maxV=ObstacleMath.Component(max,v);
        // Reject only if the entire padded AABB is outside a cone plane.
        if(maxU-loU*(loU>=0?loD:hiD)<0 || hiU*(hiU>=0?hiD:loD)-minU<0
            || maxV-loV*(loV>=0?loD:hiD)<0 || hiV*(hiV>=0?hiD:loD)-minV<0)return;
        if(node.Item>=0){found.Add(node.Item);return;}
        AngularCandidates(node.Left,origin,face,loU,hiU,loV,hiV,padding,found);
        AngularCandidates(node.Right,origin,face,loU,hiU,loV,hiV,padding,found);
    }
}
