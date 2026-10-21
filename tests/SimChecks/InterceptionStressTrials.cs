using System.Text.Json;
using Godot;
using SpaceFleet.Sim;

// A paired tactical experiment, not a weapon balance patch. Keep the earlier front-wave
// geometry exactly; rotate that wave about ANCHOR for equal-distance, simultaneous flanks.
static class InterceptionStressTrials
{
    public enum Support { Absent, Silent, Ecm }
    public sealed record Sector(int Incoming,int Intercepted,int GuardHits,int AnchorHits,int OtherHits,int Remaining,
        double? MeanFirstLockKm,double? MeanFirstLockSeconds);
    public sealed record Row(int FrontRangeKm,int Directions,int SpeedMps,bool MotorBurning,Support Support,int Seed,int Incoming,
        int Intercepted,int GuardHits,int AnchorHits,int OtherHits,int ExpiredOrMissed,int Remaining,int InnerScreen,
        Dictionary<string,int> KillsByWeapon,Dictionary<string,int> KillsByShip,Sector[] Sectors,
        double? FirstLaserKm,double? FirstLaserSeconds,int LaserPulses,int LaserShipHitPulses,
        float PeakHeat,double CoolingMountSeconds,double EcmSeconds,float MinimumEcmEffect,
        bool GuardDisabled,bool GuardDestroyed,bool SupportDisabled,bool SupportDestroyed,
        double GuardTravelMeters,double GuardTurnDegrees,double MinimumStartKm,double MaximumStartKm);
    const int Count=48;
    const string Hull="earth_air_defense_cruiser_t5";
    static readonly JsonSerializerOptions Json=new(){WriteIndented=true,Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}};

    public static Row Wave(int range,int directions,int speed,Support support,int seed,bool motorBurning=false,List<object>? diagnostics=null)
    {
        var w=new SimWorld();
        var g=w.Add(new ShipBody("GUARD",CruiserTrials.Resolve(Hull,"""{"decoys":null}""").Definition,Faction.Blue));
        var a=w.Add(new ShipBody("ANCHOR",CruiserTrials.PassiveTarget(),Faction.Blue));
        a.Place(new(0,0,5000),Quaternion.Identity);
        var s=w.Add(new ShipBody("SOURCE",CruiserTrials.Resolve("escort",
            """{"railgun":null,"pointDefense":null,"defenseDrones":null,"decoys":null,"sensors":{"jammer":0}}""").Definition,Faction.Red));
        s.Place(new(0,0,-22000),new Quaternion(Vector3.Up,Mathf.Pi));
        var rng=new Random(seed);var incoming=new List<Missile>();
        for(int i=0;i<Count;i++)
        {
            var front=new Vec3d((rng.NextDouble()-.5)*3000,(i/directions%2==0?1:-1)*(200+rng.NextDouble()*500),-range*1000-i/4*300);
            var p=a.Position+Rotate(front-a.Position,i%directions);
            var missile=CruiserTrials.AddThreat(w,s,a,p,(a.Position-p).ToVector3().Normalized()*speed);
            if(motorBurning)
            {
                missile.Age=(speed-missile.Definition.EjectSpeed)/missile.Definition.Accel;
                if(!missile.Burning)throw new Exception("Flight profile is outside the stock motor burn window");
            }
            incoming.Add(missile);
        }
        s.Place(new(0,0,-1000000),Quaternion.Identity);
        ShipBody? jammer=null;
        if(support!=Support.Absent)
        {
            // Real stationary Mars escort, 6 km off the front lane / 16 km ahead of GUARD.
            // Both controls include this visible ship, hull obstruction and AI target distraction.
            jammer=w.Add(new ShipBody("ECM",CruiserTrials.Resolve("mars_escort",
                """{"railgun":null,"missiles":null,"pointDefense":null,"defenseDrones":null,"decoys":null}""").Definition,Faction.Red));
            jammer.Place(new(6000,0,-16000),Quaternion.Identity);
            if(support==Support.Ecm)jammer.Power.SetPips(2,2,0,2,2);
        }
        foreach(var ship in w.Ships)ship.Control=new(){FlightAssist=false};
        w.AttachBrain(g,ShipOrder.HoldAt(g.Position));
        w.Sensors.Update(w.Ships,0,true,w.Probes,w.Missiles);w.Log=new(w);
        var ids=incoming.Select(m=>m.Id).ToHashSet();
        var kills=new Dictionary<uint,(string Actor,string Weapon)>();
        var hits=new Dictionary<uint,string>();var crossed=new HashSet<uint>();
        var firstLock=new Dictionary<uint,(double Km,double Time)>();
        var consumed=new HashSet<long>();
        double? firstFireKm=null,firstFireSeconds=null;
        double ecmSeconds=0,coolingSeconds=0,travel=0,turn=0;
        float heat=0,minimumEcmEffect=float.MaxValue;
        var pulseCounts=new ulong[g.Railguns.Length];
        double minimumStart=incoming.Min(m=>(m.Position-g.Position).Length())/1000;
        double maximumStart=incoming.Max(m=>(m.Position-g.Position).Length())/1000;
        void Observe()
        {
            foreach(var m in incoming)
            {
                if(m.Health<=0||!w.Missiles.Contains(m))continue;
                if((m.Position-a.Position).Length()<=5000)crossed.Add(m.Id);
                if(!firstLock.ContainsKey(m.Id)&&w.Sensors.SmallTrack(Faction.Blue,m) is {} t&&t.Locked(w.Time))
                    firstLock.Add(m.Id,((m.Position-g.Position).Length()/1000,w.Time));
            }
        }
        Observe();
        for(int tick=0;tick<40*60;tick++)
        {
            var position=g.Position;var orientation=g.Orientation;
            bool flightActive=incoming.Any(m=>m.Health>0&&w.Missiles.Contains(m));
            w.Step();Observe();
            if(diagnostics is not null)
                for(int i=0;i<g.Railguns.Length;i++)
                {
                    var gun=g.Railguns[i];var laser=gun.Laser;
                    if(laser is null||laser.Pulses==pulseCounts[i])continue;
                    pulseCounts[i]=laser.Pulses;
                    if(laser.HitShip is not {} victim)continue;
                    diagnostics.Add(new{Time=w.Time,Mount=gun.Definition.ModuleId,Victim=victim.Callsign,
                        Friendly=victim.Faction==g.Faction,BeamEndKm=(laser.End-gun.MuzzlePosition).Length()/1000,
                        MissileKm=gun.DefenseTarget is Missile m?(double?)((m.Position-gun.MuzzlePosition).Length()/1000):null,
                        FullRayFriendlyBlock=w.FriendlyInLine(g,gun.MuzzlePosition,gun.Direction,laser.Definition.RayRange)});
                }
            // Diagnostics stop when the salvo resolves, so subsequent shooting at the
            // support hull does not inflate heat, rotation or laser expenditure.
            if(flightActive)
            {
                travel+=(g.Position-position).Length();turn+=orientation.AngleTo(g.Orientation)*180/Math.PI;
                heat=Math.Max(heat,g.Power.HeatFraction);
                coolingSeconds+=g.Railguns.Count(gun=>gun.Laser?.Cooling==true)/60d;
                if(jammer?.Power.EcmActive==true)
                {ecmSeconds+=1/60d;minimumEcmEffect=Math.Min(minimumEcmEffect,jammer.Power.EcmEffect);}
                foreach(var gun in g.Railguns)
                    if(firstFireKm is null&&gun.Laser?.Active==true&&gun.DefenseTarget is Missile target)
                    {firstFireKm=(target.Position-g.Position).Length()/1000;firstFireSeconds=w.Time;}
            }
            foreach(var impact in w.Impacts)
                if(impact.Weapon==BattleWeapon.Missile&&impact.Shooter==s&&ids.Contains(impact.Id))
                    hits.TryAdd(impact.Id,impact.Hit.Target?.Callsign??"UNKNOWN");
            foreach(var e in w.Merits.Events)
                if(e.Kind==MeritKind.MissileIntercept&&ids.Contains(e.ObjectId)&&consumed.Add(e.Id))
                {
                    if(!kills.TryAdd(e.ObjectId,(e.Actor.Callsign,e.Weapon.ToString())))
                        throw new Exception("Duplicate missile removal credit in stress trial");
                }
            if(!incoming.Any(m=>m.Health>0&&w.Missiles.Contains(m)))break;
        }
        if(kills.Count!=incoming.Count(m=>m.Health<=0)||kills.Keys.Any(hits.ContainsKey)
            ||hits.Count!=w.Log.Ship(s).MissileHits)
            throw new Exception("Missile outcomes disagree with physical state / BattleLog");
        if(kills.Values.Any(k=>k.Actor!="GUARD"))throw new Exception("Passive fixture contributed interception fire");
        int left=incoming.Count(w.Missiles.Contains);
        int missed=Count-kills.Count-hits.Count-left;
        if(missed<0)throw new Exception("Missile outcome accounting exceeded launched rounds");
        var sectors=Enumerable.Range(0,directions).Select(sector=>
        {
            var members=incoming.Where((_,i)=>i%directions==sector).ToArray();
            var locks=members.Where(m=>firstLock.ContainsKey(m.Id)).Select(m=>firstLock[m.Id]).ToArray();
            return new Sector(members.Length,members.Count(m=>kills.ContainsKey(m.Id)),
                members.Count(m=>hits.GetValueOrDefault(m.Id)=="GUARD"),members.Count(m=>hits.GetValueOrDefault(m.Id)=="ANCHOR"),
                members.Count(m=>hits.TryGetValue(m.Id,out var victim)&&victim!="GUARD"&&victim!="ANCHOR"),
                members.Count(w.Missiles.Contains),locks.Length>0?locks.Average(l=>l.Km):null,locks.Length>0?locks.Average(l=>l.Time):null);
        }).ToArray();
        return new(range,directions,speed,motorBurning,support,seed,Count,kills.Count,hits.Values.Count(v=>v=="GUARD"),
            hits.Values.Count(v=>v=="ANCHOR"),hits.Values.Count(v=>v!="GUARD"&&v!="ANCHOR"),missed,left,crossed.Count,
            kills.Values.GroupBy(k=>k.Weapon).ToDictionary(k=>k.Key,k=>k.Count()),
            kills.Values.GroupBy(k=>k.Actor).ToDictionary(k=>k.Key,k=>k.Count()),sectors,firstFireKm,firstFireSeconds,
            w.Log.Ship(g).LaserPulses,w.Log.Ship(g).LaserHits,heat,coolingSeconds,ecmSeconds,
            minimumEcmEffect==float.MaxValue?0:minimumEcmEffect,g.Damage.Disabled,g.Damage.Destroyed,
            jammer?.Damage.Disabled??false,jammer?.Damage.Destroyed??false,travel,turn,minimumStart,maximumStart);
    }

    static Vec3d Rotate(Vec3d v,int sector)=>sector switch
    {0=>v,1=>new(-v.Z,v.Y,v.X),2=>new(-v.X,v.Y,-v.Z),3=>new(v.Z,v.Y,-v.X),_=>throw new ArgumentOutOfRangeException(nameof(sector))};

    public static void Diagnostics(string output)
    {
        var trace=new List<object>();
        var row=Wave(32,4,2000,Support.Absent,1,diagnostics:trace);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output,JsonSerializer.Serialize(new{Trial=row,ShipHitPulses=trace},Json));
        Console.WriteLine($"PASS: {trace.Count} ship-hit pulses traced in the four-direction control -> {output}");
    }

    public static void FlightProfiles(string output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var rows=new List<Row>();
        foreach(var profile in new[]{(Speed:4000,Burn:false),(Speed:2000,Burn:true),(Speed:4000,Burn:true),(Speed:6000,Burn:true)})
        foreach(int directions in new[]{1,4})foreach(var support in Enum.GetValues<Support>())
        {
            var group=new List<Row>();
            foreach(int seed in Enumerable.Range(1,8))group.Add(Wave(32,directions,profile.Speed,support,seed,profile.Burn));
            rows.AddRange(group);File.WriteAllText(output,JsonSerializer.Serialize(rows,Json));
            Console.WriteLine($"32km {directions}dir {profile.Speed}m/s burning={profile.Burn} {support}: intercept {group.Sum(r=>r.Intercepted)}/{group.Sum(r=>r.Incoming)}, hit {group.Sum(r=>r.GuardHits+r.AnchorHits)}, other {group.Sum(r=>r.OtherHits)}, remaining {group.Sum(r=>r.Remaining)}, lost {group.Sum(r=>r.ExpiredOrMissed)}");
        }
        Console.WriteLine($"PASS: {rows.Count} flight-profile waves / {rows.Sum(r=>r.Incoming)} missiles -> {output}");
    }

    public static void Run(string output,bool smoke=false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        // Verify the new harness reproduces the previous physical outcomes before expanding it.
        var reference=InterceptionTrials.Wave(Hull,true,32,Count,1);
        var baseline=Wave(32,1,2000,Support.Absent,1);
        if(reference.Intercepted!=baseline.Intercepted||reference.Hits!=baseline.GuardHits+baseline.AnchorHits
            ||reference.InnerScreen!=baseline.InnerScreen||reference.Remaining!=baseline.Remaining)
            throw new Exception("Extended trial no longer reproduces the original wave");
        if(JsonSerializer.Serialize(baseline,Json)!=JsonSerializer.Serialize(Wave(32,1,2000,Support.Absent,1),Json))
            throw new Exception("Stress trial is not deterministic");
        Console.WriteLine("PASS: original-wave reproduction and deterministic repeat");
        var rows=new List<Row>();
        foreach(int range in smoke?new[]{32}:new[]{16,24,32})
        foreach(int directions in new[]{1,4})foreach(int speed in new[]{2000,6000})foreach(var support in Enum.GetValues<Support>())
        {
            var group=new List<Row>();
            foreach(int seed in Enumerable.Range(1,smoke?1:8))group.Add(Wave(range,directions,speed,support,seed));
            rows.AddRange(group);File.WriteAllText(output,JsonSerializer.Serialize(rows,Json));
            Console.WriteLine($"{range}km {directions}dir {speed}m/s {support}: intercept {group.Sum(r=>r.Intercepted)}/{group.Sum(r=>r.Incoming)}, guard-hit {group.Sum(r=>r.GuardHits)}, anchor-hit {group.Sum(r=>r.AnchorHits)}, other {group.Sum(r=>r.OtherHits)}, remaining {group.Sum(r=>r.Remaining)}, lost {group.Sum(r=>r.ExpiredOrMissed)}");
        }
        Console.WriteLine($"PASS: {rows.Count} stress waves / {rows.Sum(r=>r.Incoming)} missiles with reconciled removal ownership -> {output}");
    }
}
