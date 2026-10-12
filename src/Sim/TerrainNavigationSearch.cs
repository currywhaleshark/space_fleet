using System;
using System.Collections.Generic;

namespace SpaceFleet.Sim;

public sealed partial class TerrainNavigation
{
    // Setup/connectivity keeps the synchronous API. Runtime searches own sparse
    // state so requests can interleave without a full grid allocation per ship.
    public Search BeginPath(Vec3d start,Vec3d destination,double radius,int maxExpanded=30000,TerrainExposure? exposure=null,TerrainExposure? measurement=null)
        =>new(this,start,destination,radius,maxExpanded,exposure,measurement);

    public sealed class Search : IDisposable
    {
        private readonly TerrainNavigation _nav;
        private readonly Vec3d _start,_destination;
        private readonly double _radius;
        private readonly int _limit;
        private readonly TerrainExposure? _exposure;
        private readonly TerrainExposure? _measurement;
        private readonly IEnumerator<int> _steps;
        private int _remaining;
        private double _sampledCost;
        public bool Complete { get; private set; }
        public bool Found { get; private set; }
        public TerrainPath Path { get; private set; }=new(Array.Empty<Vec3d>(),0,0);
        public int Expanded { get; private set; }
        public double CoverLength { get; private set; }
        public double CoverExposure { get; private set; }
        internal Search(TerrainNavigation nav,Vec3d start,Vec3d destination,double radius,int limit,TerrainExposure? exposure,TerrainExposure? measurement)
        { _nav=nav;_start=start;_destination=destination;_radius=radius;_limit=limit;_exposure=exposure;_measurement=measurement;_steps=Run().GetEnumerator(); }

        /// <summary>Deterministic work credits, never a stopwatch deadline. Visibility
        /// samples charge one credit per observer; cached samples and grid work cost one.</summary>
        public int Step(int budget)
        {
            if(budget<1)throw new ArgumentOutOfRangeException(nameof(budget));
            int used=0;
            while(!Complete)
            {
                int take=Math.Min(_remaining,budget-used);_remaining-=take;used+=take;
                if(_remaining>0)return used;
                if(!_steps.MoveNext())
                { Complete=true;if(!Found)Path=new(Array.Empty<Vec3d>(),Expanded,0);_steps.Dispose();break; }
                _remaining=Math.Max(1,_steps.Current);
                if(used==budget)break;
            }
            return used;
        }
        public void Dispose(){_steps.Dispose();Complete=true;}

        private IEnumerable<int> MeasurePath()
        {
            if(_measurement is null)yield break;
            var previous=_start;
            foreach(var point in Path.Points)
            {
                var middle=Vec3d.Lerp(previous,point,.5);yield return _measurement.SampleWork(middle);
                double length=(point-previous).Length();CoverLength+=length;
                CoverExposure+=length*_measurement.At(middle);previous=point;
            }
        }

        private IEnumerable<int> SampleCost(Vec3d a,Vec3d b)
        {
            double exposed=0;
            for(int i=0;i<4;i++)
            {
                var point=Vec3d.Lerp(a,b,(i+.5)/4);
                yield return _exposure!.SampleWork(point);exposed+=_exposure.At(point);
            }
            _sampledCost=(b-a).Length()*(1+1.5*exposed/4);
        }
        private sealed class Node(double cost,int parent)
        { public double Cost=cost;public int Parent=parent;public bool Closed; }
        private IEnumerable<int> Run()
        {
            yield return 1;
            var n=_nav;var goal=n.NearestFree(_destination,_radius);
            yield return 1;
            if(!n._obstacles.BlocksSegment(_start,goal,_radius))
            { Path=new(new[]{goal},0,(goal-_start).Length());foreach(int work in MeasurePath())yield return work;Found=true;yield break; }
            // Runtime layers are prepared once during battlefield setup.
            n.Prepare(_radius);var layer=n._layers[_radius];int from=-1,to=-1;
            foreach(var point in new[]{_start,goal})
            {
                var c=n.Coordinates(n.Nearest(point));int best=-1;
                for(int ring=1;ring<=4 && best<0;ring++)
                {
                    double distance=double.PositiveInfinity;
                    for(int z=Math.Max(0,c.Z-ring);z<=Math.Min(n._nz-1,c.Z+ring);z++)
                    for(int y=Math.Max(0,c.Y-ring);y<=Math.Min(n._ny-1,c.Y+ring);y++)
                    for(int x=Math.Max(0,c.X-ring);x<=Math.Min(n._nx-1,c.X+ring);x++)
                    {
                        yield return 1;
                        int id=n.Id(x,y,z);if(layer.Cells[id]==0)continue;
                        double d=(n.Point(id)-point).LengthSquared();if(d>=distance)continue;
                        if(n._obstacles.BlocksSegment(point,n.Point(id),_radius))continue;
                        best=id;distance=d;
                    }
                }
                if(best<0)yield break;
                if(from<0)from=best;else to=best;
            }
            n.Searches++;
            var nodes=new Dictionary<int,Node>{{from,new(0,-1)}};
            var open=new PriorityQueue<int,(double F,double H,int Id)>();
            double firstH=n.Heuristic(from,to);open.Enqueue(from,(firstH,firstH,from));
            while(open.Count>0)
            {
                yield return 1;
                int current=open.Dequeue();var node=nodes[current];if(node.Closed)continue;
                node.Closed=true;if(++Expanded>_limit)yield break;
                if(current==to)
                {
                    var raw=new List<Vec3d>{goal};
                    for(int p=to;p>=0;p=nodes[p].Parent){yield return 1;raw.Add(n.Point(p));}
                    raw.Add(_start);raw.Reverse();
                    var smooth=new List<Vec3d>();int at=0;double length=0;
                    while(at<raw.Count-1)
                    {
                        int next=raw.Count-1;
                        while(next>at+1)
                        {
                            yield return 1;
                            if(n._obstacles.BlocksSegment(raw[at],raw[next],_radius)){next--;continue;}
                            if(_exposure is not null)
                            {
                                double original=0;
                                for(int i=at;i<next;i++)
                                { foreach(int work in SampleCost(raw[i],raw[i+1]))yield return work;original+=_sampledCost; }
                                foreach(int work in SampleCost(raw[at],raw[next]))yield return work;
                                if(_sampledCost>original*1.01){next--;continue;}
                            }
                            break;
                        }
                        length+=(raw[next]-raw[at]).Length();smooth.Add(raw[next]);at=next;
                    }
                    Path=new(smooth,Expanded,length);foreach(int work in MeasurePath())yield return work;Found=true;yield break;
                }
                var c=n.Coordinates(current);
                for(int direction=0;direction<6;direction++)
                {
                    yield return 1;
                    int x=c.X,y=c.Y,z=c.Z;
                    switch(direction){case 0:x--;break;case 1:x++;break;case 2:y--;break;case 3:y++;break;case 4:z--;break;default:z++;break;}
                    if(x<0 || x>=n._nx || y<0 || y>=n._ny || z<0 || z>=n._nz)continue;
                    int next=n.Id(x,y,z);if(layer.Cells[next]==0)continue;
                    nodes.TryGetValue(next,out var nextNode);if(nextNode?.Closed==true)continue;
                    if(layer.Cells[current]!=1 && layer.Cells[next]!=1)
                    {
                        int mask=1<<direction;
                        if((layer.Tested[current]&mask)==0)
                        {
                            bool clear=!n._obstacles.BlocksSegment(n.Point(current),n.Point(next),_radius);
                            layer.Tested[current]|=(byte)mask;layer.Tested[next]|=(byte)(1<<(direction^1));
                            if(clear){layer.Clear[current]|=(byte)mask;layer.Clear[next]|=(byte)(1<<(direction^1));}
                        }
                        if((layer.Clear[current]&mask)==0)continue;
                    }
                    double visibility=0;
                    if(_exposure is not null)
                    { var point=n.Point(next);yield return _exposure.SampleWork(point);visibility=_exposure.At(point); }
                    double cost=node.Cost+(_exposure is null?CellSize:CellSize*(1+1.5*visibility));
                    if(nextNode is not null && cost>=nextNode.Cost)continue;
                    if(nextNode is null)nodes.Add(next,new(cost,current));else{nextNode.Cost=cost;nextNode.Parent=current;}
                    double h=n.Heuristic(next,to);open.Enqueue(next,(cost+h,h,next));
                }
            }
        }
    }
}
