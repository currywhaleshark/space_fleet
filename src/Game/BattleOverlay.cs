using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class BattleOverlay : Control
{
    public Main Host { get; set; } = null!;
    private Font _font = null!;
    private readonly List<(Button Button, Rect2 Bounds)> _buttons = new();
    private static readonly Color Blue = new(.4f,.72f,1), Red = new(1,.38f,.29f), Orange = new(1,.73f,.32f), Ink = new(.8f,.89f,1);
    private static readonly Color[] Phases = {new(.23f,.3f,.4f),new(.3f,.65f,.8f),new(.95f,.68f,.27f),new(.7f,.48f,.9f),new(.94f,.35f,.32f)};
    public override void _Ready()
    {
        _font = new SystemFont { FontNames = new[] { "Malgun Gothic", "Segoe UI" } };
        SetAnchorsPreset(LayoutPreset.TopLeft); Size=GetViewportRect().Size; Rebuild();
    }
    private void Button(string text, Rect2 rect, Action action)
    {
        var b = new Button {Text=text}; b.AddThemeFontOverride("font",_font); b.AddThemeFontSizeOverride("font_size",20);
        b.AddThemeColorOverride("font_color",Ink);
        foreach (string state in new[] {"normal","hover","pressed","focus"})
            b.AddThemeStyleboxOverride(state,new StyleBoxFlat {BgColor=state=="normal"?new Color(.025f,.06f,.1f,.95f):new Color(.06f,.15f,.23f),
                BorderColor=Blue,BorderWidthBottom=1,BorderWidthTop=1,BorderWidthLeft=1,BorderWidthRight=1,CornerRadiusTopLeft=4,CornerRadiusTopRight=4,CornerRadiusBottomLeft=4,CornerRadiusBottomRight=4});
        b.Pressed+=action; AddChild(b); _buttons.Add((b,rect));
    }
    public void Rebuild()
    {
        foreach(var item in _buttons) {RemoveChild(item.Button);item.Button.QueueFree();} _buttons.Clear();
        MouseFilter = Host.Screen == BattleScreen.Battle ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
        switch(Host.Screen)
        {
            case BattleScreen.Title:
                Button("출항",new(620,590,360,56),()=>Host.ChangeScreen(BattleScreen.Select));
                Button("종료",new(620,662,360,48),Host.Quit); break;
            case BattleScreen.Select:
                Button("함장  ·  BB-01",new(260,560,320,58),()=>Host.StartBattle("BB-01",true));
                Button("전투지휘관  ·  DD-31",new(640,560,320,58),()=>Host.StartBattle("DD-31",true));
                Button("조종사  ·  IC-21",new(1020,560,320,58),()=>Host.StartBattle("IC-21",true));
                Button("뒤로",new(650,716,300,46),()=>Host.ChangeScreen(BattleScreen.Title)); break;
            case BattleScreen.Pause:
                Button("계속",new(620,400,360,56),Host.TogglePause);
                Button("다시",new(620,476,360,56),()=>Host.StartBattle(Host.Role));
                Button("종료",new(620,552,360,56),Host.Quit); break;
            case BattleScreen.Result:
                Button("다시",new(260,730,320,56),()=>Host.StartBattle(Host.Role));
                Button("함선 바꾸기",new(640,730,320,56),Host.SelectRole);
                Button("종료",new(1020,730,320,56),Host.Quit); break;
        }
        LayoutButtons(); QueueRedraw();
    }
    internal void ActivateButton(int index)=>_buttons[index].Button.EmitSignal(Godot.Button.SignalName.Pressed);
    private void LayoutButtons()
    {
        float k=Mathf.Min(Size.X/1600,Size.Y/900); Vector2 offset=(Size-new Vector2(1600,900)*k)*.5f;
        foreach(var (b,r) in _buttons){b.Position=offset+r.Position*k;b.Size=r.Size*k;b.AddThemeFontSizeOverride("font_size",Math.Max(12,(int)(20*k)));}
    }
    public override void _Process(double delta) {Size=GetViewportRect().Size;LayoutButtons(); QueueRedraw(); }
    private void Text(Vector2 at,string text,int size=20,Color? color=null) => DrawString(_font,at,text,HorizontalAlignment.Left,-1,size,color??Ink);
    private void Center(float y,string text,int size=24,Color? color=null) => Text(new(800-_font.GetStringSize(text,fontSize:size).X*.5f,y),text,size,color);
    public override void _Draw()
    {
        float k=Mathf.Min(Size.X/1600,Size.Y/900); DrawSetTransform((Size-new Vector2(1600,900)*k)*.5f,0,Vector2.One*k);
        if(Host.Screen==BattleScreen.Battle)
        {
            if(Host.Battle is {BriefRemaining:>0} b)
            {DrawRect(new(480,120,640,150),new(.015f,.035f,.055f,.85f));Center(166,"150 km · 함대 교전",26);Center(206,$"{b.Controlled?.Body.Callsign}  ·  아군 편대를 지휘하세요",19);Center(244,"F1 조작 안내   ·   N 편대   ·   F 전력",17,Blue);}
            if(Host.Battle is {DeathRemaining:>0} lost)
            {DrawRect(new(0,0,1600,900),new(0,0,0,.72f));Center(420,lost.Controlled!.Body.Damage.Destroyed?"격침":"무력화",40,Red);Center(468,"지휘권 인계 중",20);}
            return;
        }
        DrawRect(new(0,0,1600,900),new(.012f,.025f,.043f,Host.Screen==BattleScreen.Pause?.94f:1));
        for(int x=0;x<=1600;x+=80)DrawLine(new(x,0),new(x,900),new(.2f,.4f,.6f,.065f));
        for(int y=0;y<=900;y+=80)DrawLine(new(0,y),new(1600,y),new(.2f,.4f,.6f,.065f));
        if(Host.Screen==BattleScreen.Title)
        {
            Center(198,"SPACE FLEET",58);Center(244,"한 판의 함대전",22,Blue);
            Ship(new(675,425),HullKind.Battleship,Blue,1.5f);Ship(new(860,440),HullKind.Escort,Blue);Ship(new(965,370),HullKind.Interceptor,Red);
            DrawArc(new(800,435),185,-2.8f,.2f,64,new(.4f,.72f,1,.22f),2);
            Center(820,"24척 · 세 역할 · 150 km에서 시작하는 전투",18,new Color(Ink,.5f));
        }
        else if(Host.Screen==BattleScreen.Select)
        {
            Center(164,"지휘권을 선택하세요",38);Center(206,"파랑 함대 · 선택한 함선의 편대를 지휘합니다",19,Blue);
            string[] descriptions={"전투단을 이끌고 포격선을 만든다","측면을 돌아 적 주력함을 친다","요격함을 막고 주력함 후미를 판다"};
            for(int i=0;i<3;i++)
            {
                float x=260+i*380;DrawRect(new(x,275,320,370),new(.025f,.06f,.095f));
                Ship(new(x+160,390),(HullKind)i,Blue,i==0?1.3f:1.6f);
                Text(new(x+22,492),descriptions[i],16);Text(new(x+30,531),i==2?"◉  마우스 비행 · 사격":"⌨  키보드 조함 · 자동 사격",17,Blue);
            }
        }
        else if(Host.Screen==BattleScreen.Pause){Center(298,"일시정지",40);Center(340,"함대가 대기합니다",18,Blue);}
        else DrawResult();
    }
    private void Ship(Vector2 p,HullKind kind,Color color,float scale=1)
    {
        Vector2[] points=kind switch
        {HullKind.Battleship=>new[]{new Vector2(0,-68),new(20,-42),new(26,-4),new(56,10),new(56,40),new(22,32),new(20,65),new(-20,65),new(-22,32),new(-56,40),new(-56,10),new(-26,-4),new(-20,-42)},
            HullKind.Escort=>new[]{new Vector2(0,-48),new(16,-22),new(34,16),new(15,27),new(12,42),new(-12,42),new(-15,27),new(-34,16),new(-16,-22)},
            _=>new[]{new Vector2(0,-28),new(12,-2),new(45,24),new(14,17),new(9,29),new(-9,29),new(-14,17),new(-45,24),new(-12,-2)}};
        DrawColoredPolygon(points.Select(v=>p+v*scale).ToArray(),new Color(color,.55f));
        DrawLine(p+new Vector2(0,-30)*scale,p+new Vector2(0,36)*scale,color,2);
    }
    private void DrawResult()
    {
        if(Host.Battle is not { } battle || Host.Outcome is not { } outcome)return;
        string title=outcome.Winner is null?"무승부":outcome.Winner==Faction.Blue?"승리":"패배";
        Center(126,title,44,outcome.Winner==Faction.Red?Red:Blue);Center(166,$"{outcome.Reason}  ·  {TimeSpan.FromSeconds(outcome.Time):mm\\:ss}",20);
        Text(new(260,224),"전투의 흐름",20);float width=1080;double duration=Math.Max(outcome.Time,30);
        foreach(var interval in (battle.World.Log!.OutcomeIntervals??battle.World.Log.Intervals).Where(i=>i.Start<duration))
        {float x=260+(float)(interval.Start/duration)*width;float w=(float)(Math.Min(interval.Duration,duration-interval.Start)/duration)*width;DrawRect(new(x,245,Math.Max(w,1),32),Phases[(int)interval.Phase]);}
        foreach(var e in battle.World.Log.Events.Where(e=>e.Kind==BattleEventKind.Destroyed&&e.Time<=duration))
        {float x=260+(float)(e.Time/duration)*width;Color c=e.Victim==Faction.Blue?Blue:Red;DrawLine(new(x-4,233),new(x+4,241),c,2);DrawLine(new(x+4,233),new(x-4,241),c,2);}
        foreach(var s in battle.ControlSegments)
            DrawLine(new(260+(float)(s.Start/duration)*width,288),new(260+(float)(Math.Min(s.End,duration)/duration)*width,288),Blue,3);
        string[] names={"접근","미사일","포격","정밀타격","난전"};
        for(int i=0;i<5;i++){float x=260+i*170;DrawRect(new(x,309,12,12),Phases[i]);Text(new(x+20,322),names[i],16);}
        Text(new(260,391),"함대 상태",20);
        foreach(Faction f in Enum.GetValues<Faction>())
        {
            float y=f==Faction.Blue?432:480;float x=290;
            foreach(var ship in battle.World.Ships.Where(s=>s.Faction==f))
            {
                Color c=ship.Damage.Disabled?Red:ship.Damage.Modules.Any(m=>m.Destroyed)?Orange:f==Faction.Blue?Blue:Red;
                if(ship.Damage.Destroyed){DrawLine(new(x-9,y-9),new(x+9,y+9),c,2);DrawLine(new(x+9,y-9),new(x-9,y+9),c,2);}
                else Ship(new(x,y),ship.Class.Kind,c,.22f);
                x+=48;
            }
        }
        PlayerRecord record=battle.ResultRecord;
        string[] values={$"{record.RailHits} / {record.Rails}",$"{record.MissileHits} / {record.Missiles}",$"{record.ModulesDestroyed}",$"{record.ShieldDamage:0}",$"{record.ArmorPenetrations}",$"{record.ModulesLost}"};
        string[] labels={"주포 명중 / 발사","미사일 명중 / 발사","모듈 파괴","받은 실드 피해","장갑 관통","모듈 손실"};
        for(int i=0;i<6;i++){float x=260+i*180;Text(new(x,592),values[i],32,Blue);Text(new(x,627),labels[i],16);}
        Text(new(260,677),"정상 · 진영색    손상 · 주황    무력화 · 빨강    격침 · ×",16,new Color(Ink,.6f));
        Text(new(1100,843),$"SEED {Host.Seed}",14,new Color(Ink,.45f));
    }
}
