using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

internal static class MeritText
{
    internal static string Name(MeritKind kind) => kind switch {
        MeritKind.Hit=>"명중",MeritKind.Penetration=>"관통",MeritKind.ModuleDestroyed=>"계통 파괴",
        MeritKind.Disabled=>"무력화",MeritKind.Kill=>"격침",MeritKind.Assist=>"격침 지원",
        MeritKind.MissileIntercept=>"미사일 요격",MeritKind.TorpedoIntercept=>"어뢰 요격",MeritKind.ProbeDestroyed=>"프로브 격추",
        MeritKind.Detection=>"신규 탐지",MeritKind.Identification=>"신규 식별",MeritKind.ScoutHit=>"정찰 지원",
        MeritKind.ScoutKill=>"정찰 지원 격침",MeritKind.Capture=>"거점 점령",MeritKind.Defense=>"거점 방어",
        MeritKind.ShieldBreak=>"실드 붕괴",MeritKind.Protection=>"아군 보호",_=>"기함 보호" };
    internal static string Message(MeritEvent e) => e.Kind switch {
        MeritKind.ModuleDestroyed=>$"{e.Target?.Callsign} · {(e.Target is {} target?ModuleNames.Translate(target.Definition,e.Detail):e.Detail)} 파괴",
        MeritKind.Capture or MeritKind.Defense=>$"거점 {e.Detail} · {(e.Kind==MeritKind.Capture?"점령":"방어")}",
        MeritKind.Detection=>"새 적 접촉 확보",
        MeritKind.ScoutHit=>$"정찰 지원 · {e.Attacker?.Callsign} → {e.Target?.Callsign}", // A contact does not expose its callsign/class.
        MeritKind.FlagshipProtection=>"위협 무장 요격 · 기함 보호",
        _=>Name(e.Kind)+(e.Target is null?"":" · "+e.Target.Callsign) };
    internal static int Priority(MeritKind kind) => kind switch {
        MeritKind.Kill=>6,MeritKind.ScoutKill or MeritKind.Assist=>5,MeritKind.TorpedoIntercept or MeritKind.FlagshipProtection=>4,
        MeritKind.Disabled or MeritKind.Capture or MeritKind.Defense=>3,MeritKind.ModuleDestroyed=>2,_=>1 };
}

/// <summary>A small screen-time queue, independent of simulation speed. Never consumes unconfirmed events.</summary>
internal sealed class MeritPresentation
{
    internal sealed class Notice { public MeritEvent Event; public float Age; public Notice(MeritEvent e){Event=e;} }
    private BattleMeritLedger? _ledger;
    private long _cursor;
    private readonly List<MeritEvent> _waiting=new(8);
    internal List<Notice> Showing { get; }=new(2);
    internal readonly Dictionary<MeritKind,float> Pulses=new();
    private readonly List<MeritKind> _keys=new();
    internal void Advance(BattleMeritLedger ledger,float delta,bool paused,bool hidden,Action<MeritEvent>? sound)
    {
        if(_ledger!=ledger){_ledger=ledger;_cursor=0;Showing.Clear();_waiting.Clear();Pulses.Clear();}
        if(paused)return;
        delta=Math.Min(delta,.1f);
        foreach(var n in Showing)n.Age+=delta;Showing.RemoveAll(n=>n.Age>=4);
        _keys.Clear();_keys.AddRange(Pulses.Keys);foreach(var key in _keys)Pulses[key]=Math.Max(0,Pulses[key]-delta);
        MeritEvent? audible=null;
        foreach(var e in ledger.Events)
        {
            if(e.PublishedId<=_cursor)continue;
            _cursor=e.PublishedId;if(!e.Personal)continue;
            Pulses[e.Kind]=.65f;
            if(audible is null || MeritText.Priority(e.Kind)>MeritText.Priority(audible.Value.Kind))audible=e;
            if(!BattleMeritLedger.IsImportant(e.Kind))continue;
            int same=_waiting.FindIndex(old=>old.Kind==e.Kind&&old.Target==e.Target&&e.Time-old.Time<1);
            if(same>=0)_waiting[same]=e with{Count=_waiting[same].Count+e.Count};
            else {if(_waiting.Count>=8){int lowest=0;for(int i=1;i<_waiting.Count;i++)if(MeritText.Priority(_waiting[i].Kind)<MeritText.Priority(_waiting[lowest].Kind))lowest=i;
                if(MeritText.Priority(e.Kind)<MeritText.Priority(_waiting[lowest].Kind))continue;_waiting.RemoveAt(lowest);}_waiting.Add(e);}
        }
        if(hidden)return;
        while(_waiting.Count>0)
        {
            int best=0;for(int i=1;i<_waiting.Count;i++)if(MeritText.Priority(_waiting[i].Kind)>MeritText.Priority(_waiting[best].Kind))best=i;
            if(Showing.Count>=2)
            {
                int lowest=MeritText.Priority(Showing[0].Event.Kind)<=MeritText.Priority(Showing[1].Event.Kind)?0:1;
                if(MeritText.Priority(_waiting[best].Kind)<=MeritText.Priority(Showing[lowest].Event.Kind))break;
                Showing.RemoveAt(lowest);
            }
            Showing.Add(new(_waiting[best]));_waiting.RemoveAt(best);
        }
        if(audible is {} cue)sound?.Invoke(cue);
    }
}

public partial class ScaleTest
{
    internal MeritPresentation MeritDisplay { get; }=new();
    internal void AdvanceMerits(float delta) => MeritDisplay.Advance(World.Merits,delta,Paused,
        WorldMapOpen||MenuOpen||RosterOpen||ShowHelp||Spectating,e=>_audio.MeritCue(e));
}

public partial class Hud
{
    internal Rect2 MeritBounds { get; private set; }
    internal Rect2 MeritNoticeBounds { get; private set; }
    private static readonly (MeritKind Kind,string Label)[] Ribbons={
        (MeritKind.Hit,"명중"),(MeritKind.Penetration,"관통"),(MeritKind.ScoutHit,"정찰"),
        (MeritKind.MissileIntercept,"요격"),(MeritKind.Kill,"격침"),(MeritKind.Assist,"지원")};
    private void DrawMerits(Vector2 screen)
    {
        MeritBounds=MeritNoticeBounds=new();
        if(Game.WorldMapOpen||Game.MenuOpen||Game.RosterOpen||Game.ShowHelp)return;
        var stats=Game.World.Merits.Personal(true);
        var active=Ribbons;
        double capture=Game.World.Merits.Personal().CaptureProgress;
        if(!active.Any(r=>stats[r.Kind]>0)&&Game.MeritDisplay.Showing.Count==0&&capture==0&&stats[MeritKind.Detection]==0)return;
        float row=HudFont(11)+6,y=16+248*Mathf.Clamp(Mathf.Min(screen.X/1600,screen.Y/900),.6f,1)+18;
        float top=y-18;
        int rows=(active.Length+2)/3;
        MeritBounds=new(16,top,250,row*rows+24);
        DrawRect(MeritBounds,new Color(PanelBack,.70f));
        Label(new(24,y),Game.Spectating?"내 전과 · 관전 중":capture>0?$"내 전과 · 점령 기여 {capture:0.0}":stats[MeritKind.Detection]>0?$"내 전과 · 탐지 {stats[MeritKind.Detection]}":"내 전과",12,Dim);y+=row;
        for(int i=0;i<active.Length;i++)
        {
            var r=active[i];float x=24+i%3*80,yy=y+i/3*row;
            Color color=stats[r.Kind]==0?new Color(Text,.35f):Text;
            if(Game.MeritDisplay.Pulses.GetValueOrDefault(r.Kind)>0)color=Colors.White;
            // A small notched ribbon, no separate card or animation layer.
            DrawPolyline(new[]{new Vector2(x,yy-9),new(x+5,yy-9),new(x+5,yy),new(x+2.5f,yy-2),new(x,yy),new(x,yy-9)},color,1);
            Label(new(x+9,yy),$"{r.Label} {stats[r.Kind]}",11,color);
        }
        y=133;float noticeX=screen.X-608;
        if(Game.MeritDisplay.Showing.Count>0)
        {MeritNoticeBounds=new(noticeX-8,112,264,Game.MeritDisplay.Showing.Count*(HudFont(12)+6)+12);DrawRect(MeritNoticeBounds,new Color(PanelBack,.8f));}
        foreach(var notice in Game.MeritDisplay.Showing)
        {
            string message=MeritText.Message(notice.Event)+(notice.Event.Count>1?$" ×{notice.Event.Count}":"");
            Label(new(noticeX,y),HudText.Fit(_font,message,HudFont(12),248),12,new Color(Text,Math.Min(1,4-notice.Age)));y+=HudFont(12)+6;
        }
    }
}

public partial class CombatAudio
{
    private ulong _nextMeritCue;
    internal void MeritCue(MeritEvent e)
    {
        if(Time.GetTicksMsec()<_nextMeritCue)return;
        // Ordinary hit feedback already has a sound. Additional cues share one voice and a global throttle.
        if(e.Kind is MeritKind.Hit or MeritKind.Penetration or MeritKind.ModuleDestroyed or MeritKind.ShieldBreak)return;
        _nextMeritCue=Time.GetTicksMsec()+450;
        if(MeritText.Priority(e.Kind)>=3)_nextPlay[(int)CombatSound.HitConfirm]=0;
        Play(CombatSound.HitConfirm,e.Kind==MeritKind.Kill?-9:-17,e.Kind switch {
            MeritKind.Kill=>.65f,MeritKind.ScoutHit or MeritKind.Detection or MeritKind.Identification=>1.5f,
            MeritKind.MissileIntercept or MeritKind.TorpedoIntercept=>1.2f,_=>.85f});
    }
}
