using System.Text.Json;
using Godot;
using SpaceFleet.Sim;

// Repeatable branch comparisons. The target is deliberately unarmed, so this measures
// mission behavior and delivery rather than claiming a fleet balance result.
internal static class BranchTrials
{
    public sealed record Result(string Hull,double Seconds,int Probes,int Contacts,int Identifications,
        double? FirstAction,double? FirstAssault,double AssaultSpread,double RallySeconds,
        double MinimumRange,int Torpedoes,int TorpedoHits,int Survivors,string LastMission);

    public static (SimWorld World,Squadron Wing,ShipBody Anchor,ShipBody Target) Fixture(string id,double range=42000)
    {
        var w=new SimWorld {Doctrine=new FleetDoctrine()};
        var anchor=w.Add(new ShipBody("ANCHOR",CruiserTrials.Resolve("battleship",
            """{"railgun":null,"missiles":null,"pointDefense":null,"defenseDrones":null,"sensors":{"strength":10000}}""").Definition,Faction.Blue));
        anchor.Control=new ShipControl{FlightAssist=false};
        w.AddSquadron("BG",Faction.Blue,SquadronRole.BattleGroup,new[]{anchor});
        var members=Enumerable.Range(0,5).Select(i=>
        {
            var s=w.Add(new ShipBody("W"+i,ShipDefinitions.ById(id),Faction.Blue));
            s.Place(new((i-2)*900,0,3000+i*800),Quaternion.Identity);
            w.AttachBrain(s,ShipOrder.HoldAt(s.Position));return s;
        }).ToArray();
        var wing=w.AddSquadron("W",Faction.Blue,SquadronRole.InterceptorWing,members);
        var target=w.Add(new ShipBody("TARGET",CruiserTrials.PassiveTarget(),Faction.Red));
        target.Place(new(0,0,-range),Quaternion.Identity);target.Control=new ShipControl{FlightAssist=false};
        w.Sensors.Update(w.Ships,0,true);w.EnableCommander(Faction.Blue);w.Log=new BattleLog(w);
        return(w,wing,anchor,target);
    }
    public static Result Run(string id,double seconds=150)
    {
        var(w,wing,anchor,target)=Fixture(id);double rally=0,minRange=double.MaxValue;
        while(w.Time<seconds)
        {
            w.Step();if(wing.Rallying)rally+=SimWorld.TickDelta;
            foreach(var s in wing.Members)minRange=Math.Min(minRange,(s.Position-target.Position).Length());
        }
        var first=wing.Members.Select(s=>s.Ordnance.Interceptor.FirstAssault).Where(t=>t.HasValue).Select(t=>t!.Value).ToArray();
        var action=wing.Members.Select(s=>s.Ordnance.Interceptor.FirstAction).Where(t=>t.HasValue).Select(t=>t!.Value).ToArray();
        return new(id,w.Time,wing.Members.Sum(s=>s.Ordnance.Interceptor.Launched),
            wing.Members.Sum(s=>s.Ordnance.Interceptor.NewContacts),wing.Members.Sum(s=>s.Ordnance.Interceptor.NewIdentifications),
            action.Length>0?action.Min():null,first.Length>0?first.Min():null,first.Length>0?first.Max()-first.Min():0,
            rally,minRange,wing.Members.Sum(s=>w.Log!.Ship(s).Torpedoes),wing.Members.Sum(s=>w.Log!.Ship(s).TorpedoHits),
            wing.ActiveMembers.Count(),wing.Mission.ToString());
    }
    public static void Save(string output)
    {
        var results=new[]{"interceptor_t3","earth_recon_t3","mars_interceptor_t3","mars_assault_t3",
            "earth_recon_t5","mars_assault_t5"}.Select(id=>Run(id)).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        foreach(var r in results)Console.WriteLine($"{r.Hull}: probes={r.Probes}, first assault={r.FirstAssault:F2}, rally={r.RallySeconds:F2}s, nearest={r.MinimumRange/1000:F2}km, AM={r.TorpedoHits}/{r.Torpedoes}, alive={r.Survivors}");
    }
}
