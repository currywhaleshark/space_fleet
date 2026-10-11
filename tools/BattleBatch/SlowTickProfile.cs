using System.Diagnostics;
using SpaceFleet.Sim;

/// <summary>Optional attribution of the 128 slowest ticks, separate from acceptance profiles.</summary>
sealed class SlowTickProfile
{
    private readonly long[] _previous=new long[Enum.GetValues<SimSection>().Length];
    private readonly long[] _current=new long[Enum.GetValues<SimSection>().Length];
    private readonly PriorityQueue<(long Tick,long[] Sections),long> _slowest=new();
    public SlowTickProfile()=>SimProfiler.CopyElapsedTicks(_previous);
    public void Capture(long tick)
    {
        SimProfiler.CopyElapsedTicks(_current);
        long duration=_current[0]-_previous[0];
        if(_slowest.Count<128 || _slowest.TryPeek(out _,out long minimum) && duration>minimum)
        {
            var values=new long[_current.Length];
            for(int i=0;i<values.Length;i++)values[i]=_current[i]-_previous[i];
            if(_slowest.Count==128)_slowest.Dequeue();
            _slowest.Enqueue((tick,values),duration);
        }
        _current.CopyTo(_previous,0);
    }
    public void Save(string path,SimWorld world)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sections=Enum.GetValues<SimSection>();
        File.WriteAllLines(path,new[]{"time,phase,"+string.Join(',',sections)}.Concat(
            _slowest.UnorderedItems.OrderByDescending(x=>x.Priority).Select(x=>
            {
                double time=(x.Element.Tick-1)*SimWorld.TickDelta;
                var interval=world.Log!.Intervals.FirstOrDefault(i=>time>=i.Start && time<i.Start+i.Duration);
                return $"{time:F6},{interval?.Phase.ToString()??"unknown"},"+string.Join(',',x.Element.Sections.Select(v=>(v*1000.0/Stopwatch.Frequency).ToString("F6")));
            })));
    }
}
