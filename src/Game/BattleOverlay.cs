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
    private HSlider? _volume;
    private HSlider? _shake;
    internal HangarView? HangarView;
    internal ResearchView? ResearchView;
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
    /// <summary>Option chip on the battle settings screen; the chosen one is filled.</summary>
    private void Chip(string text,string key,Rect2 rect,bool selected,Action action)
    {
        var b=new Button{Text=text};b.SetMeta("key",key);b.AddThemeFontOverride("font",_font);
        b.AddThemeColorOverride("font_color",selected?new Color(.02f,.06f,.1f):Ink);
        b.AddThemeColorOverride("font_hover_color",selected?new Color(.02f,.06f,.1f):Colors.White);
        foreach (string state in new[] {"normal","hover","pressed","focus"})
            b.AddThemeStyleboxOverride(state,new StyleBoxFlat {BgColor=selected?(state=="hover"?new Color(.6f,.84f,1):Blue):state=="normal"?new Color(.025f,.06f,.1f,.95f):new Color(.06f,.15f,.23f),
                BorderColor=Blue,BorderWidthBottom=1,BorderWidthTop=1,BorderWidthLeft=1,BorderWidthRight=1,CornerRadiusTopLeft=4,CornerRadiusTopRight=4,CornerRadiusBottomLeft=4,CornerRadiusBottomRight=4});
        b.Pressed+=action;AddChild(b);_buttons.Add((b,rect));
    }
    internal void ActivateButton(string key)=>_buttons.Single(x=>x.Button.HasMeta("key")&&x.Button.GetMeta("key").AsString()==key).Button.EmitSignal(Godot.Button.SignalName.Pressed);
    private const float RowX=470,ChipW=170,ChipGap=14;
    public void Rebuild()
    {
        foreach(var item in _buttons) {RemoveChild(item.Button);item.Button.QueueFree();} _buttons.Clear();
        if (_volume is not null) { RemoveChild(_volume); _volume.QueueFree(); _volume = null; }
        if (_shake is not null) { RemoveChild(_shake); _shake.QueueFree(); _shake = null; }
        if (HangarView is not null) { RemoveChild(HangarView); HangarView.QueueFree(); HangarView=null; }
        if (ResearchView is not null) { RemoveChild(ResearchView); ResearchView.QueueFree(); ResearchView=null; }
        MouseFilter = Host.Screen == BattleScreen.Battle ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
        switch(Host.Screen)
        {
            case BattleScreen.Title:
                Button("격납고로",new(620,590,360,56),Host.OpenHangar);
                Button("종료",new(620,662,360,48),Host.Quit); break;
            case BattleScreen.Select:
            {
                var s=Host.Settings;
                void Row<T>(float y,string prefix,T current,(string label,T value)[] options,Func<T,MatchSettings> set)
                {
                    for(int i=0;i<options.Length;i++)
                    {
                        var option=options[i];
                        Chip(option.label,prefix+"-"+i,new(RowX+i*(ChipW+ChipGap),y,ChipW,48),Equals(current,option.value),()=>Host.SetSettings(set(option.value)));
                    }
                }
                Row(240,"objective",s.Objective,new (string,BattleObjective?)[]{("무작위",null),(BattleRules.Name(BattleObjective.Annihilation),BattleObjective.Annihilation),
                    (BattleRules.Name(BattleObjective.Flagship),BattleObjective.Flagship),(BattleRules.Name(BattleObjective.Domination),BattleObjective.Domination)},v=>s with{Objective=v});
                Row(370,"field",s.Battlefield,new (string,Battlefield?)[]{("무작위",null),(AsteroidField.Name(Battlefield.DeepSpace),Battlefield.DeepSpace),
                    (AsteroidField.Name(Battlefield.AsteroidCluster),Battlefield.AsteroidCluster)},v=>s with{Battlefield=v});
                Row(500,"course",s.Course,new[]{("무작위",Engagement.Auto),("대향",Engagement.HeadOn),("동항",Engagement.Parallel),("반항",Engagement.Opposite)},v=>s with{Course=v});
                Row(630,"height",s.Height,new[]{("무작위",Elevation.Auto),("평면",Elevation.Level),("머리 위",Elevation.Overhead)},v=>s with{Height=v});
                Button("격납고로",new(310,785,260,56),Host.OpenHangar);
                Button("이 설정으로 출격",new(620,785,500,56),Host.LaunchFromSettings);
                _buttons[^1].Button.SetMeta("key","launch");
                Button("모두 무작위",new(1140,785,180,56),()=>Host.SetSettings(MatchSettings.Random));
                break;
            }
            case BattleScreen.Research:
                ResearchView=new ResearchView {Host=Host};AddChild(ResearchView);break;
            case BattleScreen.Hangar:
                HangarView=new HangarView {Host=Host};AddChild(HangarView);break;
            case BattleScreen.Matching:
                Button("취소 · 격납고로",new(620,800,360,48),Host.ReturnToHangar); break;
            case BattleScreen.Eliminated:
                Button("계속 관전",new(420,520,360,56),Host.ContinueSpectating);
                Button("메인으로 나가기",new(820,520,360,56),Host.ReturnToPort); break;
            case BattleScreen.Battle when Host.Battle?.Spectating==true:
                Button("← 이전",new(430,800,160,44),()=>Host.Battle.CycleSpectator(-1));
                Button("다음 →",new(610,800,160,44),()=>Host.Battle.CycleSpectator(1));
                Button("메인으로 나가기",new(950,800,230,44),Host.ReturnToPort); break;
            case BattleScreen.Pause:
                Button("계속",new(620,400,360,56),Host.TogglePause);
                Button("다시 매칭",new(620,476,360,56),()=>Host.BeginMatch());
                Button("메인으로 나가기",new(620,552,360,56),Host.ReturnToPort);
                _volume = new HSlider { MinValue = 0, MaxValue = 100, Step = 5, Value = SoundSettings.Volume };
                _volume.ValueChanged += value => SoundSettings.SetVolume((float)value);
                AddChild(_volume);
                _shake = new HSlider { MinValue = 0, MaxValue = 100, Step = 5, Value = FeedbackSettings.Shake };
                _shake.ValueChanged += value => FeedbackSettings.SetShake((float)value);
                AddChild(_shake);
                Button(SoundSettings.Muted ? "효과음 켜기 · F10" : "효과음 끄기 · F10",new(620,760,360,46),
                    () => { SoundSettings.ToggleMute(); Rebuild(); });
                Button($"HUD 글자 {HudSettings.TextPercent}% · 변경",new(620,818,360,46),
                    () => { HudSettings.Cycle(); Rebuild(); }); break;
            case BattleScreen.Result:
                Button("다시 매칭",new(260,730,320,56),()=>Host.BeginMatch());
                Button("격납고로",new(640,730,320,56),Host.ReturnToHangar);
                Button("메인으로",new(1020,730,320,56),Host.ReturnToPort); MeritResultButtons(); break;
        }
        LayoutButtons(); QueueRedraw();
    }
    internal void ActivateButton(int index)=>_buttons[index].Button.EmitSignal(Godot.Button.SignalName.Pressed);
    private void LayoutButtons()
    {
        float k=Mathf.Min(Size.X/1600,Size.Y/900); Vector2 offset=(Size-new Vector2(1600,900)*k)*.5f;
        foreach(var (b,r) in _buttons){b.Position=offset+r.Position*k;b.Size=r.Size*k;b.AddThemeFontSizeOverride("font_size",Math.Max(12,(int)(20*k)));}
        if (_volume is not null) { _volume.Position=offset+new Vector2(730,650)*k;_volume.Size=new Vector2(250,34)*k; }
        if (_shake is not null) { _shake.Position=offset+new Vector2(750,704)*k;_shake.Size=new Vector2(230,34)*k; }
    }
    public override void _Process(double delta)
    {
        Size=GetViewportRect().Size;LayoutButtons();
        foreach(var (button,_) in _buttons)button.Visible=Host.Screen!=BattleScreen.Battle || Host.Battle?.RosterOpen!=true;
        QueueRedraw();
    }
    private void Text(Vector2 at,string text,int size=20,Color? color=null) => DrawString(_font,at,text,HorizontalAlignment.Left,-1,size,color??Ink);
    private void Center(float y,string text,int size=24,Color? color=null) => Text(new(800-_font.GetStringSize(text,fontSize:size).X*.5f,y),text,size,color);
    public override void _Draw()
    {
        if(Host.Screen is BattleScreen.Hangar or BattleScreen.Research)return;
        float k=Mathf.Min(Size.X/1600,Size.Y/900); DrawSetTransform((Size-new Vector2(1600,900)*k)*.5f,0,Vector2.One*k);
        if(Host.Screen==BattleScreen.Battle)
        {
            if(Host.Battle?.RosterOpen==true){DrawRoster(Host.Battle.LaunchConfig?.Match,Host.Battle);return;}
            if(Host.Battle?.Spectating==true)
            {
                DrawRect(new(380,729,840,132),new(.015f,.035f,.055f,.92f));
                Center(765,$"관전 중 · {Host.Battle.SpectatedView?.Body.Callsign ?? "전장"}",24,Blue);
                Center(789,"← → 관전 함선 변경 · 중클릭 회전 · 휠 줌 · Tab 양팀 편성",16);
            }
            if (Host.Battle?.WorldMapOpen == true) return;
            if(Host.Battle is {BriefRemaining:>0} b)
            {
                var config=b.LaunchConfig!;var (course,height)=Deployment.Resolve(config);
                DrawRect(new(480,120,640,150),new(.015f,.035f,.055f,.85f));
                Center(166,$"{BattleRules.Name(config.Objective)} · {AsteroidField.Name(config.Battlefield)} · {Deployment.Name(course)} · {Deployment.Name(height)}",26);Center(206,$"{b.Controlled?.Body.Callsign}  ·  내 편대를 지휘하세요",19);Center(244,"Tab 양팀 편성   ·   N 편대   ·   F 전력   ·   F1 도움말",17,Blue);}
            if(Host.Battle is {DeathRemaining:>0} lost)
            {DrawRect(new(0,0,1600,900),new(0,0,0,.72f));Center(420,lost.Controlled!.Body.Damage.Destroyed?"격침":"무력화",40,Red);Center(468,"지휘권 인계 중",20);}
            return;
        }
        if(Host.Screen==BattleScreen.Matching){DrawRoster(Host.PendingMatch,null);return;}
        if(Host.Screen==BattleScreen.Eliminated)
        {
            DrawRect(new(0,0,1600,900),new(0,.015f,.025f,.8f));
            Center(360,Host.Battle?.SquadronEliminated==true?"내 편대 전멸":"내 편대 전투불능",42,Red);
            Center(415,"남은 아군의 전투를 관전하거나 메인화면으로 돌아갈 수 있습니다.",22);
            Center(456,Host.Battle?.SquadronEliminated==true?"전투는 계속 진행됩니다.":"편대원이 응급복구되면 지휘권을 되찾습니다.",18,Blue);
            return;
        }
        DrawRect(new(0,0,1600,900),new(.012f,.025f,.043f,Host.Screen==BattleScreen.Pause?.94f:1));
        for(int x=0;x<=1600;x+=80)DrawLine(new(x,0),new(x,900),new(.2f,.4f,.6f,.065f));
        for(int y=0;y<=900;y+=80)DrawLine(new(0,y),new(1600,y),new(.2f,.4f,.6f,.065f));
        if(Host.Screen==BattleScreen.Title)
        {
            Center(198,"SPACE FLEET",58);Center(244,"내 함선, 내 편대와 함께 출격",22,Blue);
            Ship(new(675,425),HullKind.Battleship,Blue,1.5f);Ship(new(860,440),HullKind.Escort,Blue);Ship(new(965,370),HullKind.Interceptor,Red);
            DrawArc(new(800,435),185,-2.8f,.2f,64,new(.4f,.72f,1,.22f),2);
            Center(550,$"내 함선 · {ShipDefinitions.ById(Host.PlayerHullId).Flight.DisplayName}",22,Blue);
            Center(820,"지구권 · 화성권  /  10 대 10 편대 · AI 무작위 매칭",18,new Color(Ink,.5f));
        }
        else if(Host.Screen==BattleScreen.Select)
        {
            var s=Host.Settings;
            Center(118,"전장 설정",36);
            Center(160,"항목마다 지정하거나 무작위에 맡깁니다 · 격납고에서 바로 출격하면 모두 무작위로 정해집니다",18,Blue);
            void Label(float y,string title,string detail){Text(new(260,y+32),title,22);Text(new(RowX,y+80),detail,16,new Color(Ink,.75f));}
            Label(240,"전투 규칙",s.Objective switch
            {
                BattleObjective.Flagship => "적 기함 격침 또는 무력화 시 승리",
                BattleObjective.Domination => "3차원 거점 3곳을 점령하고 500점을 먼저 획득",
                BattleObjective.Annihilation => "적 함대의 전투능력을 제거하면 승리",
                _ => $"{BattleRules.Name(BattleObjective.Annihilation)} · {BattleRules.Name(BattleObjective.Flagship)} · {BattleRules.Name(BattleObjective.Domination)} 중 하나",
            });
            Label(370,"전장",s.Battlefield switch
            {
                Battlefield.DeepSpace => "장애물 없는 열린 공간",
                Battlefield.AsteroidCluster => "소행성 사이로 엄폐와 우회가 가능한 전장",
                _ => "심우주 · 소행성 군집 중 하나 (지형은 매번 다름)",
            });
            Label(500,"진형 · 방향",s.Course switch
            {
                Engagement.HeadOn => "정면 150 km에서 서로 마주 보고 시작",
                Engagement.Parallel => "옆으로 150 km, 같은 방향으로 나란히 진행",
                Engagement.Opposite => "반대 방향으로 진행하며 엇갈려 지나감",
                _ => "대향 · 동항 · 반항 중 하나",
            });
            Label(630,"진형 · 높낮이",s.Height switch
            {
                Elevation.Level => "같은 높이에서 시작",
                Elevation.Overhead => "상대가 머리 위에 있고, 위쪽 함대는 뒤집힌 자세",
                _ => "평면 · 머리 위 중 하나",
            });
        }
        else if(Host.Screen==BattleScreen.Pause)
        {Center(298,"일시정지",40);Center(340,"함대가 대기합니다",18,Blue);Text(new(620,671),$"효과음 {SoundSettings.Volume:0}%",17,Ink);
            Text(new(620,725),$"흔들림 {FeedbackSettings.Shake:0}%",17,Ink);}
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
        Center(126,title,44,outcome.Winner==Faction.Red?Red:Blue);Center(166,$"{BattleRules.Name(battle.World.Rules!.Objective)} · {outcome.Reason}  ·  {TimeSpan.FromSeconds(outcome.Time):mm\\:ss}",20);
        if(_showMerits){DrawMeritResult(battle);return;}
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
                var frozen=battle.World.Merits.Outcome?.Fleet.FirstOrDefault(s=>s.Ship==ship.Callsign);
                bool disabled=frozen?.Disabled??ship.Damage.Disabled, destroyed=frozen?.Destroyed??ship.Damage.Destroyed;
                bool damaged=frozen?.Damaged??ship.Damage.Modules.Any(m=>m.Destroyed);
                Color c=disabled?Red:damaged?Orange:f==Faction.Blue?Blue:Red;
                if(destroyed){DrawLine(new(x-9,y-9),new(x+9,y+9),c,2);DrawLine(new(x+9,y-9),new(x-9,y+9),c,2);}
                else Ship(new(x,y),ship.Class.Kind,c,.22f);
                x+=Mathf.Min(48,1000f/battle.World.Ships.Count(s=>s.Faction==f));
            }
        }
        PlayerRecord record=battle.ResultRecord;
        string[] values={$"{record.RailHits} / {record.Rails}",$"{record.MissileHits} / {record.Missiles}",$"{record.ModulesDestroyed}",$"{record.ShieldDamage:0}",$"{record.ArmorPenetrations}",$"{record.ModulesLost}"};
        string[] labels={"주포 명중 / 발사","미사일 명중 / 발사","모듈 파괴","받은 실드 피해","장갑 관통","모듈 손실"};
        for(int i=0;i<6;i++){float x=260+i*180;Text(new(x,592),values[i],32,Blue);Text(new(x,627),labels[i],16);}
        Text(new(260,653),$"반물질 강습어뢰 · 명중 {record.TorpedoHits} / 발사 {record.Torpedoes}",16,Blue);
        Text(new(260,677),"정상 · 진영색    손상 · 주황    무력화 · 빨강    격침 · ×",16,new Color(Ink,.6f));
        if(Host.LastResearchReward is { } reward)Center(706,$"연구 점수 +{reward} · 출격 진영에 지급",18,Blue);
        Text(new(1100,843),$"SEED {Host.Seed}",14,new Color(Ink,.45f));
    }
}
