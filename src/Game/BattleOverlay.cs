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
                for(int i=0;i<3;i++)
                {var kind=(HullKind)i;bool owned=Host.Hangar.Owned.Contains(ShipDefinitions.For(kind,Host.PlayerDesign).Id);Button(Host.SelectedKind==kind?"선택됨":owned?"이 편대 선택":"연구 필요",new(260+i*380,552,320,52),()=>{if(Host.SelectedKind!=kind)Host.ChooseHull(kind);});}
                Button($"내 함선 설계 · {DesignName(Host.PlayerDesign)}  ↔",new(610,230,380,42),()=>Host.ToggleDesign(false));
                Button("출격 · 10 대 10 AI 매칭",new(620,785,500,56),()=>Host.BeginMatch());
                Button("메인으로",new(310,785,260,56),Host.ReturnToPort);
                Button($"전투 목표 · {BattleRules.Name(Host.SelectedObjective)}  ↔",new(310,706,460,52),Host.CycleObjective);
                Button($"전장 · {AsteroidField.Name(Host.SelectedBattlefield)}  ↔",new(830,706,460,52),Host.CycleBattlefield);
                Button("격납고 · 함선/편대 세팅",new(1040,230,320,42),Host.OpenHangar); break;
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
            {DrawRect(new(480,120,640,150),new(.015f,.035f,.055f,.85f));Center(166,BattleRules.Name(Host.SelectedObjective)+" · "+AsteroidField.Name(Host.SelectedBattlefield),26);Center(206,$"{b.Controlled?.Body.Callsign}  ·  내 편대를 지휘하세요",19);Center(244,"Tab 양팀 편성   ·   N 편대   ·   F 전력   ·   F1 도움말",17,Blue);}
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
            bool mars = Host.PlayerDesign == DesignFamily.Mars;
            Color accent = mars ? Orange : Blue;
            Center(145,"내 함선과 편대",34);Center(187,"선택한 편대로 출격하면 나머지 참가자는 AI로 편성됩니다",18,Blue);
            string[] descriptions=mars ? new[]{"장거리 정밀포격 · 가벼운 방어","센서망 지원 · ECM · 분산 기동","고속 침투 · 반물질 강습 후 이탈"}
                : new[]{"전열 유지 · 지속포격 · 높은 방어","상호방공 · 측면 지원 · 집중사격","요격전 · 근접 기동 · 반물질 강습"};
            for(int i=0;i<3;i++)
            {
                float x=260+i*380;DrawRect(new(x,295,320,350),new(.025f,.06f,.095f));
                if(Host.SelectedKind==(HullKind)i)DrawRect(new(x,295,320,350),accent,false,2);
                Ship(new(x+160,395),(HullKind)i,accent,i==0?1.1f:1.35f);
                var def=Host.SelectedKind==(HullKind)i?ShipDefinitions.ById(Host.PlayerHullId):ShipDefinitions.For((HullKind)i,Host.PlayerDesign);
                Text(new(x+22,325),def.Flight.DisplayName,21,accent);
                string description=def.Branch switch {ShipBranch.GunCruiser=>"측면 대치 · 집중 주포 사격",ShipBranch.AirDefenseCruiser=>"주력함 방공 · 미사일 조기 요격",ShipBranch.MissileCruiser=>"장거리 일제 사격 · 발사 후 이탈",ShipBranch.Recon=>"외곽 정찰 · 프로브 · 잠금 공유",ShipBranch.Assault=>"편대 집결 · 반물질 강습",_=>descriptions[i]};
                Text(new(x+22,492),description,16);Text(new(x+30,531),i==2?"◉  마우스 비행 · 사격":"⌨  키보드 조함 · 자동 사격",17,accent);
                Text(new(x+30,630),Host.SelectedKind==(HullKind)i?$"편대 {Host.Hangar.Hulls[Host.PlayerHullId].Squadron.Length}척 · 격납고 세팅 적용":def.Squadron.Composition,17,accent);
            }
            Center(668,mars ? "얇은 방어 · 적은 방공  /  거리와 기동으로 우위를 확보하세요" : "두터운 방어 · 지속화력  /  전열과 상호지원을 유지하세요",18,accent);
            Center(691,Host.SelectedObjective switch
            {
                BattleObjective.Flagship => "적 기함 격침 또는 무력화 시 승리",
                BattleObjective.Domination => "3차원 거점 3곳을 점령하고 500점을 먼저 획득",
                _ => "적 함대의 전투능력을 제거하면 승리",
            },15,Ink);
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
    private static string DesignName(DesignFamily family) => family == DesignFamily.Mars ? "화성권" : "지구권";
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
