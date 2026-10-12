using System;
using System.Collections.Generic;
using System.Linq;

namespace SpaceFleet.Sim;

public sealed record TerrainPath(IReadOnlyList<Vec3d> Points,int Expanded,double Length);

/// <summary>Fixed 3-D occupancy, six-neighbour deterministic A*, exact swept-sphere edges.</summary>
public sealed partial class TerrainNavigation
{
    public const double CellSize=2000;
    private readonly ObstacleSet _obstacles;
    private readonly Vec3d _min;
    private readonly int _nx,_ny,_nz,_count;
    private sealed class Layer(int count)
    {
        // 0 occupied, 1 clear through an entire edge radius, 2 boundary.
        public readonly byte[] Cells=new byte[count];
        public readonly byte[] Tested=new byte[count],Clear=new byte[count];
    }
    private readonly Dictionary<double,Layer> _layers=new();
    private readonly double[] _cost;
    private readonly int[] _parent,_stamp,_closed;
    private int _generation;
    public int Searches { get; private set; }
    public TerrainNavigation(ObstacleSet obstacles,Vec3d center,Vec3d halfSize)
    {
        _obstacles=obstacles;_min=center-halfSize;
        _nx=(int)Math.Ceiling(halfSize.X*2/CellSize)+1;
        _ny=(int)Math.Ceiling(halfSize.Y*2/CellSize)+1;
        _nz=(int)Math.Ceiling(halfSize.Z*2/CellSize)+1;
        _count=checked(_nx*_ny*_nz);
        _cost=new double[_count];_parent=new int[_count];_stamp=new int[_count];_closed=new int[_count];
    }
    private int Id(int x,int y,int z)=>(z*_ny+y)*_nx+x;
    private (int X,int Y,int Z) Coordinates(int id)=>(id%_nx,id/_nx%_ny,id/(_nx*_ny));
    private Vec3d Point(int id)
    {var c=Coordinates(id);return _min+new Vec3d(c.X*CellSize,c.Y*CellSize,c.Z*CellSize);}
    private int Nearest(Vec3d p)=>Id(Math.Clamp((int)Math.Round((p.X-_min.X)/CellSize),0,_nx-1),
        Math.Clamp((int)Math.Round((p.Y-_min.Y)/CellSize),0,_ny-1),Math.Clamp((int)Math.Round((p.Z-_min.Z)/CellSize),0,_nz-1));
    public void Prepare(double radius)
    {
        if(_layers.ContainsKey(radius))return;
        var layer=new Layer(_count);
        for(int i=0;i<_count;i++)
            layer.Cells[i]=!_obstacles.ClosestApproach(Point(i),radius+CellSize,out var contact)?(byte)1:contact.Clearance<=-CellSize?(byte)0:(byte)2;
        _layers.Add(radius,layer);
    }
    public Vec3d NearestFree(Vec3d desired,double radius)
    {
        var point=desired;
        for(int i=0;i<12;i++)
        {
            if(!_obstacles.ClosestApproach(point,radius,out var hit))return point;
            point=hit.Position+hit.Normal*(radius+1);
        }
        Prepare(radius);var layer=_layers[radius];int cell=Nearest(desired);var c=Coordinates(cell);
        for(int ring=0;ring<8;ring++)
        for(int z=Math.Max(0,c.Z-ring);z<=Math.Min(_nz-1,c.Z+ring);z++)
        for(int y=Math.Max(0,c.Y-ring);y<=Math.Min(_ny-1,c.Y+ring);y++)
        for(int x=Math.Max(0,c.X-ring);x<=Math.Min(_nx-1,c.X+ring);x++)
            if(layer.Cells[Id(x,y,z)]!=0)return Point(Id(x,y,z));
        return desired;
    }
    private int Connector(Vec3d point,double radius,Layer layer)
    {
        var c=Coordinates(Nearest(point));
        for(int ring=1;ring<=4;ring++)
        {
            int best=-1;double distance=double.PositiveInfinity;
            for(int z=Math.Max(0,c.Z-ring);z<=Math.Min(_nz-1,c.Z+ring);z++)
            for(int y=Math.Max(0,c.Y-ring);y<=Math.Min(_ny-1,c.Y+ring);y++)
            for(int x=Math.Max(0,c.X-ring);x<=Math.Min(_nx-1,c.X+ring);x++)
            {
                int id=Id(x,y,z);if(layer.Cells[id]==0)continue;
                double d=(Point(id)-point).LengthSquared();if(d>=distance)continue;
                if(_obstacles.BlocksSegment(point,Point(id),radius))continue;
                best=id;distance=d;
            }
            if(best>=0)return best;
        }
        return -1;
    }
    private double Heuristic(int a,int b)
    {var p=Coordinates(a);var q=Coordinates(b);return (Math.Abs(p.X-q.X)+Math.Abs(p.Y-q.Y)+Math.Abs(p.Z-q.Z))*CellSize;}
    public bool FindPath(Vec3d start,Vec3d destination,double radius,out TerrainPath path,int maxExpanded=30000,TerrainExposure? exposure=null)
    {
        path=new(Array.Empty<Vec3d>(),0,0);
        Vec3d goal=NearestFree(destination,radius);
        if(!_obstacles.BlocksSegment(start,goal,radius))
        {path=new(new[]{goal},0,(goal-start).Length());return true;}
        Prepare(radius);var layer=_layers[radius];int from=Connector(start,radius,layer),to=Connector(goal,radius,layer);
        if(from<0 || to<0)return false;
        Searches++;int generation=++_generation;
        if(generation==int.MaxValue){Array.Clear(_stamp);Array.Clear(_closed);generation=_generation=1;}
        var open=new PriorityQueue<int,(double F,double H,int Id)>();
        _stamp[from]=generation;_cost[from]=0;_parent[from]=-1;
        double firstH=Heuristic(from,to);open.Enqueue(from,(firstH,firstH,from));int expanded=0;
        while(open.TryDequeue(out int current,out _))
        {
            if(_closed[current]==generation)continue;
            _closed[current]=generation;if(++expanded>maxExpanded)break;
            if(current==to)
            {
                var raw=new List<Vec3d>{goal};for(int p=to;p>=0;p=_parent[p])raw.Add(Point(p));raw.Add(start);raw.Reverse();
                var smooth=new List<Vec3d>();int at=0;double length=0;
                while(at<raw.Count-1)
                {
                    int next=raw.Count-1;
                    while(next>at+1 && (_obstacles.BlocksSegment(raw[at],raw[next],radius) || exposure is not null && !exposure.CanShortcut(raw,at,next)))next--;
                    length+=(raw[next]-raw[at]).Length();smooth.Add(raw[next]);at=next;
                }
                path=new(smooth,expanded,length);return true;
            }
            var c=Coordinates(current);
            for(int direction=0;direction<6;direction++)
            {
                int x=c.X,y=c.Y,z=c.Z;
                switch(direction){case 0:x--;break;case 1:x++;break;case 2:y--;break;case 3:y++;break;case 4:z--;break;default:z++;break;}
                if(x<0 || x>=_nx || y<0 || y>=_ny || z<0 || z>=_nz)continue;
                int next=Id(x,y,z);if(layer.Cells[next]==0 || _closed[next]==generation)continue;
                if(layer.Cells[current]!=1 && layer.Cells[next]!=1)
                {
                    int mask=1<<direction;
                    if((layer.Tested[current]&mask)==0)
                    {
                        bool clear=!_obstacles.BlocksSegment(Point(current),Point(next),radius);
                        layer.Tested[current]|=(byte)mask;layer.Tested[next]|=(byte)(1<<(direction^1));
                        if(clear){layer.Clear[current]|=(byte)mask;layer.Clear[next]|=(byte)(1<<(direction^1));}
                    }
                    if((layer.Clear[current]&mask)==0)continue;
                }
                double cost=_cost[current]+(exposure is null?CellSize:CellSize*(1+1.5*exposure.At(Point(next))));
                if(_stamp[next]==generation && cost>=_cost[next])continue;
                _stamp[next]=generation;_cost[next]=cost;_parent[next]=current;
                double h=Heuristic(next,to);open.Enqueue(next,(cost+h,h,next));
            }
        }
        path=new(Array.Empty<Vec3d>(),expanded,0);return false;
    }
}
