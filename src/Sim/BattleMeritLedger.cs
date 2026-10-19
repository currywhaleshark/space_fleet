using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

public enum MeritKind { Hit, Penetration, ModuleDestroyed, Disabled, Kill, Assist, MissileIntercept, TorpedoIntercept,
    ProbeDestroyed, Detection, Identification, ScoutHit, ScoutKill, Capture, Defense, ShieldBreak, Protection, FlagshipProtection }
public enum ObservationKind { Ship, Probe }
public enum SupportReason { None, RequiredLink, ImprovedSolution }
public readonly record struct MeritSource(ObservationKind Kind, uint ObjectId, ShipBody Owner, double Time, float Quality, TrackLevel Level=TrackLevel.None, float TrackSnr=0, float DetectSnr=0);
/// <summary>Immutable launch provenance. A later control handoff or lost observer cannot rewrite it.</summary>
public readonly record struct ShotCredit(bool Personal, ShipBody? Target = null, MeritSource? Source = null,
    bool SupportPersonal = false, float SupportFraction = 0, SupportReason Reason=SupportReason.None);
public readonly record struct MeritEvent(long Id, long CauseId, double Time, MeritKind Kind, ShipBody Actor,
    string Squadron, Faction Team, ShipBody? Target, uint ObjectId, BattleWeapon Weapon, int Count,
    double Value, bool Personal, string Detail = "", ShipBody? Attacker = null, MeritSource? Source = null,
    DestructionKind Destruction = DestructionKind.None, long PublishedId = 0, uint AttackId = 0);

/// <summary>Copies are exposed to consumers; only the ledger mutates authoritative totals.</summary>
public sealed class MeritStats
{
    private readonly int[] _counts = new int[Enum.GetValues<MeritKind>().Length];
    public int this[MeritKind kind] => _counts[(int)kind];
    public double Damage { get; internal set; }
    public double ScoutDamage { get; internal set; }
    public double CaptureProgress { get; internal set; }
    public double CaptureSeconds { get; internal set; }
    public double DefenseSeconds { get; internal set; }
    public double FlagshipDamage { get; internal set; }
    internal void Set(MeritKind kind,int n)=>_counts[(int)kind]=n;
    internal void Count(MeritKind kind, int n = 1) => _counts[(int)kind] += n;
    internal void Add(MeritStats other, int sign = 1)
    {
        for (int i=0;i<_counts.Length;i++) _counts[i] += other._counts[i]*sign;
        Damage+=other.Damage*sign; ScoutDamage+=other.ScoutDamage*sign; CaptureProgress+=other.CaptureProgress*sign;
        CaptureSeconds+=other.CaptureSeconds*sign; DefenseSeconds+=other.DefenseSeconds*sign; FlagshipDamage+=other.FlagshipDamage*sign;
    }
    public MeritStats Copy() { var copy=new MeritStats(); copy.Add(this); return copy; }
    public static MeritStats Difference(MeritStats total, MeritStats part) { var copy=total.Copy(); copy.Add(part,-1); return copy; }
}

public readonly record struct UnattributedLoss(double Time,string Ship,DestructionKind Cause);

public sealed class MeritSnapshot
{
    private readonly Dictionary<ShipBody,MeritStats> _ships;
    private readonly MeritStats _personal;
    private readonly Dictionary<ShipBody,MeritStats> _personalShips;
    private readonly int[] _captures, _defenses;
    public double Time { get; }
    public IReadOnlyList<MeritEvent> Important { get; }
    public IReadOnlyList<UnattributedLoss> Unattributed { get; }
    public IReadOnlyList<(string Ship, bool Destroyed, bool Disabled, bool Damaged)> Fleet { get; }
    private readonly Dictionary<ShipBody,(double Damage,int Intercepts)> _rewards=new();
    public (double Damage,int Intercepts) Reward(IEnumerable<ShipBody> ships)
    {double damage=0;int intercepts=0;foreach(var ship in ships)if(_rewards.TryGetValue(ship,out var r)){damage+=r.Damage;intercepts+=r.Intercepts;}return(damage,intercepts);}
    internal MeritSnapshot(double time, Dictionary<ShipBody,MeritStats> ships, Dictionary<ShipBody,MeritStats> personalShips, MeritStats personal,
        IEnumerable<MeritEvent> events, BattleLog? log, int[] captures, int[] defenses, IEnumerable<UnattributedLoss> losses)
    {
        if(log is not null)foreach(var ship in ships.Keys){var s=log.Ship(ship);_rewards[ship]=(s.ModuleDamageInflicted+s.ShieldDamageInflicted*.2,s.MissilesIntercepted+s.TorpedoesIntercepted);}
        _captures=(int[])captures.Clone();_defenses=(int[])defenses.Clone();
        Time=time; _ships=ships.ToDictionary(p=>p.Key,p=>p.Value.Copy()); _personal=personal.Copy(); _personalShips=personalShips.ToDictionary(p=>p.Key,p=>p.Value.Copy());
        Important=Array.AsReadOnly(events.ToArray());Unattributed=Array.AsReadOnly(losses.ToArray());
        Fleet=Array.AsReadOnly(ships.Keys.Select(s=>(s.Callsign,s.Damage.Destroyed,s.Damage.Disabled,s.Damage.Modules.Any(m=>m.Destroyed))).ToArray());
    }
    public MeritStats Personal => _personal.Copy();
    public MeritStats PersonalShip(ShipBody ship) => _personalShips.TryGetValue(ship,out var stats)?stats.Copy():new();
    public MeritStats Ship(ShipBody ship) => _ships.TryGetValue(ship,out var stats)?stats.Copy():new();
    public MeritStats Sum(IEnumerable<ShipBody> ships) { var total=new MeritStats();foreach(var ship in ships)if(_ships.TryGetValue(ship,out var stats))total.Add(stats);return total; }
    public MeritStats Team(Faction team) {var total=Sum(_ships.Keys.Where(s=>s.Faction==team));total.Set(MeritKind.Capture,_captures[(int)team]);total.Set(MeritKind.Defense,_defenses[(int)team]);return total;}
}

/// <summary>Observer only: no rolls, damage, orders or sensor changes. Storage is bounded by roster pairs,
/// module IDs and fixed rings, never by the number of weapon pulses in a battle.</summary>
public sealed class BattleMeritLedger
{
    public const int EventCapacity=512, ImportantCapacity=32;
    public const double AssistWindow=120, LaserExposureSeconds=.5;
    private readonly SimWorld _world;
    private static readonly MeritKind[] DiscoveryKinds={MeritKind.Detection,MeritKind.Identification};
    private readonly Dictionary<ShipBody,MeritStats> _ships=new(), _visible=new(), _personalShips=new();
    private readonly MeritStats _personal=new(), _visiblePersonal=new();
    private readonly MeritEvent[] _events=new MeritEvent[EventCapacity];
    private readonly Dictionary<ShipBody,Queue<MeritEvent>> _important=new();
    private readonly int[] _teamCaptures=new int[2], _teamDefenses=new int[2];
    private readonly long[] _captureCauses=new long[2], _defenseCauses=new long[2];
    private readonly HashSet<(uint,bool)> _removals=new();
    private readonly Queue<(uint,bool)> _removalOrder=new();
    private int _eventCount;
    private long _sequence, _cause, _recordId;
    private readonly HashSet<ShipBody> _killed=new(), _disabled=new();
    private readonly Dictionary<ShipBody,UnattributedLoss> _unattributed=new();
    private readonly HashSet<(Faction,ShipBody,MeritKind)> _discoveries=new();
    private readonly Dictionary<(ShipBody,ShipBody),Contribution> _damage=new(), _scout=new();
    private readonly Dictionary<(ShipBody,ShipBody,string),double> _laserUnits=new();
    private readonly Dictionary<ShipBody,double> _scoutHistory=new();
    private readonly Dictionary<(ShipBody,ShipBody,MeritKind,bool),MeritEvent> _pending=new();
    private readonly List<(ShipBody,ShipBody,MeritKind,bool)> _released=new();
    private readonly Dictionary<CaptureZone,ZoneCredit> _zones=new();
    private readonly HashSet<(uint,BattleWeapon)> _seenHits=new();
    private readonly Queue<(uint,BattleWeapon)> _seenOrder=new();
    private readonly Dictionary<(ShipBody,ShipBody,bool),(MeritSource Sample,ShotCredit Credit)> _solutions=new();
    public BattleMeritLedger(SimWorld world) { _world=world; }
    public bool Enabled { get; set; }=true;
    public ShipBody? Controlled { get; set; }
    private bool _initialControl;
    public void BeginControl(ShipBody ship)
    {
        Controlled=ship;
        if(_initialControl || _world.Tick!=0)return;
        _initialControl=true;_personalShips[ship].Add(_ships[ship]);_personal.Add(_ships[ship]);_visiblePersonal.Add(_visible[ship]);
        for(int i=0;i<_eventCount;i++){int index=(int)((_sequence-_eventCount+i)%EventCapacity);if(_events[index].Actor==ship)_events[index]=_events[index] with {Personal=true};}
        int count=_important[ship].Count;for(int i=0;i<count;i++)_important[ship].Enqueue(_important[ship].Dequeue() with{Personal=true});
    }
    public MeritSnapshot? Outcome { get; private set; }
    public long LastEventId => _sequence;
    public int PendingCount => _pending.Count;
    public int UnattributedLosses => _unattributed.Count;
    public int StoredEvents => _eventCount;
    public IEnumerable<MeritEvent> Events { get { for(int i=0;i<_eventCount;i++)yield return _events[(int)((_sequence-_eventCount+i)%EventCapacity)]; } }
    public IEnumerable<MeritEvent> Important => _important.Values.SelectMany(q=>q).OrderBy(e=>e.Id);
    internal void Register(ShipBody ship) { _ships.TryAdd(ship,new());_visible.TryAdd(ship,new());_personalShips.TryAdd(ship,new());_important.TryAdd(ship,new(ImportantCapacity)); }
    public MeritStats Ship(ShipBody ship, bool confirmed=false) => (confirmed?_visible:_ships).TryGetValue(ship,out var s)?s.Copy():new();
    public MeritStats PersonalShip(ShipBody ship)=>_personalShips.TryGetValue(ship,out var stats)?stats.Copy():new();
    public MeritStats Personal(bool confirmed=false) => (confirmed?_visiblePersonal:_personal).Copy();
    public MeritStats Sum(IEnumerable<ShipBody> ships, bool confirmed=false) { var sum=new MeritStats();foreach(var ship in ships)if((confirmed?_visible:_ships).TryGetValue(ship,out var s))sum.Add(s);return sum; }
    public MeritStats Team(Faction team){var total=Sum(_world.Ships.Where(s=>s.Faction==team));total.Set(MeritKind.Capture,_teamCaptures[(int)team]);total.Set(MeritKind.Defense,_teamDefenses[(int)team]);return total;}
    private bool Live => Enabled && Outcome is null;
    public void Freeze(double time) { if(Outcome is null){ObserveLosses();Outcome=new(time,_ships,_personalShips,_personal,Important,_world.Log,_teamCaptures,_teamDefenses,_unattributed.Values);} }
    public ShotCredit Capture(ShipBody shooter, ShipBody? target=null, bool usedSolution=false, bool missile=false)
    {
        var result=new ShotCredit(shooter==Controlled,target);
        if(!Live || target is null || !usedSolution)return result;
        var track=_world.Sensors.Track(shooter.Faction,target);
        var source=track.CreditSource;
        if(source is not {} src || src.Owner==shooter || src.Owner.Faction!=shooter.Faction
            || _world.Time<src.Time-1e-6 || _world.Time-src.Time>FireControl.MaxObservationAge || _world.Sensors.Memory(shooter.Faction,target,_world.Time) is not null)return result;
        // A probe position never invents a railgun lock: only the sample actually consumed by Solve qualifies.
        if(!missile && (track.Level<TrackLevel.Locked || track.Observation?.Observer!=src.Owner || src.Kind!=ObservationKind.Ship))return result;
        if(missile && track.Level<TrackLevel.Contact)return result;
        var key=(shooter,target,missile);
        if(_solutions.TryGetValue(key,out var cached) && cached.Sample==src)
            return cached.Credit with {Personal=shooter==Controlled,SupportPersonal=src.Owner==Controlled};
        var own=_world.Sensors.MeasureForCredit(shooter,target,_world.Ships);
        float threshold=missile?SensorNet.ContactSnr:SensorNet.LockSnr*SensorNet.DropFactor;
        bool essential=(missile?Math.Max(own.Track,own.Detect):own.Track)<threshold;
        float ownQuality=Math.Max(.05f,shooter.Damage.SensorFraction*shooter.Power.SensorEffect);
        float ownScale=MathF.Sqrt(1+own.Jam)*Math.Clamp(MathF.Sqrt(SensorNet.LockSnr/Math.Max(own.Track,.001f)),.5f,1);
        bool improved=!missile && track.FireControlScale/Math.Max(.05f,src.Quality)<=ownScale/ownQuality*.8f;
        if(essential||improved)result=result with {Source=src,SupportPersonal=src.Owner==Controlled,SupportFraction=1,Reason=essential?SupportReason.RequiredLink:SupportReason.ImprovedSolution};
        _solutions[key]=(src,result);
        return result;
    }
    internal void MissileGuidance(Missile missile, double dt)
    {
        if(!Live || missile.InterceptTarget is not null)return;
        missile.CreditFlightSeconds+=dt;
        if(missile.SeekerTarget is not null || missile.Target is null)return;
        var credit=missile.Assault is not null?missile.Credit:Capture(missile.Shooter,missile.Target,true,true);
        if(credit.Source is not {} source)return;
        // One actual provider per missile, selected at its first useful link. Later independent guidance gets no credit.
        if(missile.SupportCredit.Source is null)missile.SupportCredit=credit;
        if(missile.SupportCredit.Source is {} saved && saved.Owner==source.Owner && saved.Kind==source.Kind && saved.ObjectId==source.ObjectId)missile.CreditLinkSeconds+=dt;
    }
    internal ShotCredit MissileCredit(Missile m)
    {
        float share=(float)(m.CreditLinkSeconds/Math.Max(.001,m.CreditFlightSeconds));
        return share>=.2f?m.SupportCredit with {Personal=m.Credit.Personal,SupportFraction=share}:m.Credit with {Source=null,SupportFraction=0,Reason=SupportReason.None};
    }
    internal void Hit(uint id, ShipBody attacker, ShotResult hit, BattleWeapon weapon, double time, float shieldBefore,
        ShotCredit credit, string mount="")
    {
        if(!Live || hit.Target is not {} target || target.Faction==attacker.Faction
            || target.Damage.Destroyed && !hit.CausedDestruction || !_seenHits.Add((id,weapon)))return;
        _seenOrder.Enqueue((id,weapon)); if(_seenOrder.Count>4096)_seenHits.Remove(_seenOrder.Dequeue());
        long cause=++_cause;
        double damage=Math.Max(0,hit.ShieldAbsorbed)+hit.StructuralDamage;
        bool critical=false;
        foreach(var h in hit.Modules) {damage+=h.Damage; if(h.Destroyed && target.Damage.Module(h.Id).Definition.Kind is ModuleKind.Thruster or ModuleKind.Reactor or ModuleKind.Generator or ModuleKind.PowerBus)critical=true;}
        _ships[attacker].Damage+=damage;if(credit.Personal){_personal.Damage+=damage;_personalShips[attacker].Damage+=damage;}
        if(_world.Rules?.Flagship(target.Faction)==target){_ships[attacker].FlagshipDamage+=damage;if(credit.Personal){_personal.FlagshipDamage+=damage;_personalShips[attacker].FlagshipDamage+=damage;}}
        bool unit=true;
        if(weapon==BattleWeapon.Laser)
        {
            var key=(attacker,target,mount);
            unit=!_laserUnits.TryGetValue(key,out var last)||time-last>=LaserExposureSeconds-1e-6;
            if(unit)_laserUnits[key]=time;
        }
        if(unit)Award(MeritKind.Hit,attacker,target,weapon,time,credit.Personal,cause,attackId:id,value:damage);
        if(unit && (hit.StructuralDamage>0||hit.Modules.Count>0))Award(MeritKind.Penetration,attacker,target,weapon,time,credit.Personal,cause,attackId:id);
        if(damage>0)Contribute(_damage,attacker,target,time,damage,critical,credit.Personal);
        foreach(var h in hit.Modules)if(h.Destroyed)Award(MeritKind.ModuleDestroyed,attacker,target,weapon,time,credit.Personal,cause,attackId:id,detail:target.Damage.Module(h.Id).Definition.Name);
        if(shieldBefore>0 && target.Damage.Shield<=0)Award(MeritKind.ShieldBreak,attacker,target,weapon,time,credit.Personal,cause,attackId:id);
        if(hit.CausedDisable && _disabled.Add(target))Award(MeritKind.Disabled,attacker,target,weapon,time,credit.Personal,cause,attackId:id);
        var support=credit.Target==target && credit.Source is {} s && s.Owner!=attacker && s.Owner.Faction==attacker.Faction && credit.SupportFraction>0 ? credit.Source : null;
        if(support is {} provider && damage>0)
        {
            double amount=damage*credit.SupportFraction;
            _ships[provider.Owner].ScoutDamage+=amount;if(credit.SupportPersonal){_personal.ScoutDamage+=amount;_personalShips[provider.Owner].ScoutDamage+=amount;}
            Contribute(_scout,provider.Owner,target,time,amount,false,credit.SupportPersonal);
            if(unit)Award(MeritKind.ScoutHit,provider.Owner,target,weapon,time,credit.SupportPersonal,cause,attackId:id,value:amount,attacker:attacker,source:provider);
        }
        if(!hit.CausedDestruction || !_killed.Add(target))return;
        _unattributed.Remove(target);
        Award(MeritKind.Kill,attacker,target,weapon,time,credit.Personal,cause,attackId:id);
        foreach(var ship in _world.Ships)
        {
            if(ship.Faction!=attacker.Faction)continue;
            if(ship!=attacker && Eligible(_damage,ship,target,time,out bool personal))Award(MeritKind.Assist,ship,target,weapon,time,personal,cause,attackId:id,attacker:attacker);
            if(support is {} final && final.Owner==ship)
                Award(MeritKind.ScoutKill,ship,target,weapon,time,credit.SupportPersonal,cause,attackId:id,attacker:attacker,source:final);
            else if(Eligible(_scout,ship,target,time,out bool scoutPersonal))Award(MeritKind.ScoutKill,ship,target,weapon,time,scoutPersonal,cause,attackId:id,attacker:attacker);
        }
    }
    private sealed class Contribution
    {
        public readonly double[] Damage=new double[121], Personal=new double[121], Times=new double[121];
        public double Critical=double.NegativeInfinity, PersonalCritical=double.NegativeInfinity;
        public void Add(double time,double damage,bool critical,bool personal)
        {
            int i=(int)Math.Floor(time)%121;
            if(Times[i]!=Math.Floor(time)){Times[i]=Math.Floor(time);Damage[i]=Personal[i]=0;}
            Damage[i]+=damage;if(personal)Personal[i]+=damage;
            if(critical){Critical=time;if(personal)PersonalCritical=time;}
        }
        public (double Damage,double Personal) Recent(double time)
        { double sum=0,personal=0;for(int i=0;i<121;i++)if(time-Times[i]<=AssistWindow){sum+=Damage[i];personal+=Personal[i];}return(sum,personal); }
    }
    private static void Contribute(Dictionary<(ShipBody,ShipBody),Contribution> map,ShipBody actor,ShipBody target,double time,double amount,bool critical,bool personal)
    { if(!map.TryGetValue((actor,target),out var c))map[(actor,target)]=c=new();c.Add(time,amount,critical,personal); }
    private static bool Eligible(Dictionary<(ShipBody,ShipBody),Contribution> map,ShipBody actor,ShipBody target,double time,out bool personal)
    {
        personal=false;if(!map.TryGetValue((actor,target),out var c))return false;
        var recent=c.Recent(time);double threshold=Math.Max(30,target.Definition.StructureHitPoints*.05);
        personal=recent.Personal>=threshold || time-c.PersonalCritical<=AssistWindow;
        return recent.Damage>=threshold || time-c.Critical<=AssistWindow;
    }
    internal void Intercept(ShipBody actor,Missile missile,BattleWeapon weapon,double time,ShotCredit? credit=null)
    {
        if(!Live || actor.Faction==missile.Faction || !Removed(missile.Id,false))return;
        long cause=++_cause;bool personal=credit?.Personal??actor==Controlled;
        Award(missile.Assault is null?MeritKind.MissileIntercept:MeritKind.TorpedoIntercept,actor,null,weapon,time,personal,cause,objectId:missile.Id);
        if(missile.SeekerTarget is Decoy || (missile.SeekerTarget as ShipBody ?? missile.Target) is not {} ally || ally.Faction!=actor.Faction || ally.Damage.Destroyed)return;
        Vector3 delta=(ally.Position-missile.Position).ToVector3(),velocity=missile.Velocity-ally.Velocity;
        float closing=delta.Dot(velocity);
        if(closing<=0 || velocity.LengthSquared()<1)return;
        float approach=closing/velocity.LengthSquared();
        float miss=(delta-velocity*approach).Length();
        if(approach>30 || miss>Math.Max(ally.Hull.BoundingRadius*3,missile.Definition.Accel*approach*approach*.5f))return;
        Award(MeritKind.Protection,actor,ally,weapon,time,personal,cause,objectId:missile.Id);
        if(_world.Rules?.Flagship(ally.Faction)==ally)Award(MeritKind.FlagshipProtection,actor,ally,weapon,time,personal,cause,objectId:missile.Id);
    }
    internal void ProbeDestroyed(ShipBody actor,ReconProbe probe,BattleWeapon weapon,double time,ShotCredit? credit=null)
    {if(Live && Removed(probe.Id,true))Award(MeritKind.ProbeDestroyed,actor,null,weapon,time,credit?.Personal??actor==Controlled,++_cause,objectId:probe.Id);}
    private bool Removed(uint id,bool probe) {if(!_removals.Add((id,probe)))return false;_removalOrder.Enqueue((id,probe));if(_removalOrder.Count>4096)_removals.Remove(_removalOrder.Dequeue());return true;}
    internal void Observe(ShipBody target,Faction team,SensorTrack track,double time)
    {
        if(!Live || track.CreditSource is not {} source || track.Level<TrackLevel.Contact || target.Damage.Destroyed)return;
        foreach(var kind in DiscoveryKinds)
        {
            if(kind==MeritKind.Identification && track.Level<TrackLevel.Identified)continue;
            if(_discoveries.Add((team,target,kind)))Award(kind,source.Owner,target,BattleWeapon.Railgun,time,source.Owner==Controlled,++_cause,source:source);
        }
    }
    private bool Confirmed(MeritEvent e)
    {
        if(e.Target is not {} target || target.Faction==e.Team || e.Kind==MeritKind.Detection)return true;
        var track=_world.Sensors.Track(e.Team,target);
        if(track.Level<TrackLevel.Identified || _world.Sensors.Memory(e.Team,target,_world.Time) is not null
            || track.CreditSource is not {} source || _world.Time-source.Time>FireControl.MaxObservationAge)return false;
        return e.Kind switch {MeritKind.Kill or MeritKind.Assist or MeritKind.ScoutKill=>track.ObservedDestroyed,
            MeritKind.Disabled=>track.ObservedDisabled||track.ObservedDestroyed, MeritKind.ModuleDestroyed=>track.Level>=TrackLevel.Locked,_=>true};
    }
    private void Award(MeritKind kind,ShipBody actor,ShipBody? target,BattleWeapon weapon,double time,bool personal,long cause,
        double value=0,string detail="",uint objectId=0,ShipBody? attacker=null,MeritSource? source=null,uint attackId=0)
    {
        _ships[actor].Count(kind);if(personal){_personal.Count(kind);_personalShips[actor].Count(kind);}
        var e=new MeritEvent(++_recordId,cause,time,kind,actor,actor.Squadron?.Name??"",actor.Faction,target,objectId,weapon,1,value,personal,detail,attacker,source,target?.Damage.Destruction??DestructionKind.None,AttackId:attackId);
        bool supportHistory=kind==MeritKind.ScoutHit&&(!_scoutHistory.TryGetValue(actor,out var lastSupport)||time-lastSupport>=10);
        if(supportHistory)_scoutHistory[actor]=time;
        if(IsImportant(kind)||kind is MeritKind.Detection or MeritKind.Identification||supportHistory){var queue=_important[actor];if(queue.Count>=ImportantCapacity)queue.Dequeue();queue.Enqueue(e);}
        int team=(int)actor.Faction;
        if(kind==MeritKind.Capture&&_captureCauses[team]!=cause){_captureCauses[team]=cause;_teamCaptures[team]++;}
        if(kind==MeritKind.Defense&&_defenseCauses[team]!=cause){_defenseCauses[team]=cause;_teamDefenses[team]++;}
        if(Confirmed(e))Publish(e);
        else if(target is not null)
        {
            var key=(actor,target,kind,personal);
            _pending[key]=_pending.TryGetValue(key,out var old)?e with {Count=old.Count+1,Value=old.Value+value}:e;
        }
    }
    private void Publish(MeritEvent e)
    {
        _visible[e.Actor].Count(e.Kind,e.Count);if(e.Personal)_visiblePersonal.Count(e.Kind,e.Count);
        e=e with {PublishedId=++_sequence};_events[(int)((_sequence-1)%EventCapacity)]=e;_eventCount=Math.Min(EventCapacity,_eventCount+1);
    }
    public static bool IsImportant(MeritKind kind) => kind is MeritKind.Kill or MeritKind.Assist or MeritKind.ScoutKill or MeritKind.Disabled
        or MeritKind.ModuleDestroyed or MeritKind.ShieldBreak or MeritKind.ProbeDestroyed or MeritKind.TorpedoIntercept or MeritKind.Capture or MeritKind.Defense or MeritKind.FlagshipProtection;
    internal void Step()
    {
        if(!Live || _world.Tick%15!=0)return;
        _released.Clear();foreach(var pair in _pending)if(Confirmed(pair.Value)){Publish(pair.Value);_released.Add(pair.Key);}
        foreach(var key in _released)_pending.Remove(key);
        ObserveLosses();
    }
    private void ObserveLosses()
    {foreach(var ship in _world.Ships)if(ship.Damage.Destroyed&&!_killed.Contains(ship))_unattributed.TryAdd(ship,new(ship.Damage.DestroyedAt,ship.Callsign,ship.Damage.Destruction));}
    private sealed class ZoneCredit
    {
        public readonly Dictionary<ShipBody,double> Progress=new(), PersonalProgress=new(), Defenders=new(), PersonalDefense=new();
        public bool Contested;
    }
    internal void CaptureZone(CaptureZone zone,double before,Faction? oldOwner,double dt,double time)
    {
        if(!Live)return;
        if(!_zones.TryGetValue(zone,out var record))_zones[zone]=record=new();
        double delta=zone.Progress-before, presence=delta>0?zone.BluePresence:zone.RedPresence;
        foreach(var ship in _world.Ships)
        {
            if(!Squadron.Active(ship)||(ship.Position-zone.Position).LengthSquared()>zone.RadiusMeters*zone.RadiusMeters)continue;
            bool personal=ship==Controlled;
            if(delta!=0 && (delta>0)==(ship.Faction==Faction.Blue))
            {
                double part=Math.Abs(delta)*ship.Definition.BattleValue.CaptureStrength/Math.Max(.001,presence);
                _ships[ship].CaptureProgress+=part;_ships[ship].CaptureSeconds+=dt;
                record.Progress[ship]=record.Progress.GetValueOrDefault(ship)+part;
                if(personal){_personal.CaptureProgress+=part;_personal.CaptureSeconds+=dt;_personalShips[ship].CaptureProgress+=part;_personalShips[ship].CaptureSeconds+=dt;record.PersonalProgress[ship]=record.PersonalProgress.GetValueOrDefault(ship)+part;}
            }
            if(zone.Contested && zone.Owner==ship.Faction)
            {
                _ships[ship].DefenseSeconds+=dt;record.Defenders[ship]=record.Defenders.GetValueOrDefault(ship)+dt;
                if(personal){_personal.DefenseSeconds+=dt;_personalShips[ship].DefenseSeconds+=dt;record.PersonalDefense[ship]=record.PersonalDefense.GetValueOrDefault(ship)+dt;}
            }
        }
        if(zone.Owner is {} owner && owner!=oldOwner)
        {
            long cause=++_cause;
            foreach(var ship in _world.Ships)if(ship.Faction==owner && record.Progress.GetValueOrDefault(ship)>=1)
                Award(MeritKind.Capture,ship,null,BattleWeapon.Railgun,time,record.PersonalProgress.GetValueOrDefault(ship)>=1,cause,detail:zone.Name);
            record.Progress.Clear();record.PersonalProgress.Clear();
        }
        if(record.Contested && !zone.Contested)
        {
            long cause=++_cause;
            foreach(var ship in _world.Ships)if(zone.Owner==ship.Faction && zone.ScoringOwner==ship.Faction && record.Defenders.GetValueOrDefault(ship)>=5)
                Award(MeritKind.Defense,ship,null,BattleWeapon.Railgun,time,record.PersonalDefense.GetValueOrDefault(ship)>=5,cause,detail:zone.Name);
            record.Defenders.Clear();record.PersonalDefense.Clear();
        }
        record.Contested=zone.Contested;
    }
}

public sealed partial class SimWorld
{
    private void CreditHit(uint id,ShipBody shooter,ShotResult hit,BattleWeapon weapon,double time,float shieldBefore,ShotCredit? credit=null,string mount="")
    {Log?.Hit(shooter,hit,weapon,time,shieldBefore);Merits.Hit(id,shooter,hit,weapon,time,shieldBefore,credit??Merits.Capture(shooter),mount);}
}
