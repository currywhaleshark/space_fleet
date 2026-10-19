using System;
using System.Collections.Generic;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

internal readonly record struct HelpEntry(string Keys, string Action);
internal static class ShipHelp
{
    public static List<HelpEntry> Entries(ShipBody ship, ControlScheme scheme, int page)
    {
        bool pilot = scheme == ControlScheme.Pilot;
        var rows = new List<HelpEntry>();
        void Add(string keys, string action) => rows.Add(new(keys,action));
        if (page == 0)
        {
            Add("W / S · X", "스로틀 올리기 / 내리기 · 스로틀 0");
            Add("Q / E · Shift", "좌우 롤 · 부스트");
            if (pilot)
            {
                Add("마우스", "기수 조준 · 실제 선회 성능에 따라 회전");
                Add("A / D · Space / Ctrl", "좌우 · 상하 평행추력");
            }
            else
            {
                Add("A / D · Space / Ctrl", "좌우 선회 · 기수 올리기 / 내리기");
                Add("Alt + 이동키", "함체 방향을 유지하며 평행추력");
            }
            Add("1 · 2" + (ship.Definition.Antimatter is null ? "" : " · 3"),
                "주포 · 미사일" + (ship.Definition.Antimatter is null ? " 선택" : " · 반물질 어뢰 선택"));
            Add("R", "마우스 가까운 적 지정 · 비행/망원에서는 중앙 기준");
            Add("좌클릭", pilot ? "선택 무기 발사 · 주포는 기수 정렬 필요" : "표식/부위 선택 · 표식 밖 클릭은 선택 무기 발사");
            Add("우클릭 유지", "망원 조준 · 마우스로 둘러보기 · 좌클릭 수동 발사");
            Add(pilot ? "T" : "B · T", pilot ? "사격보조 켜기 / 끄기" : "사격 교리 라디얼 · 교리 순환");
            Add("5", "응급복구 · 자동/계통 우선/중지 · 자재 소모");
            if (!pilot) Add("중클릭 · 휠", "주변 관찰 · 놓으면 3초 유지 · 거리 조절");
        }
        else if (page == 1)
        {
            Add("F · N", "전력 배분 · 편대 명령 (유지 → 방향 선택 → 놓기)");
            Add("Y · C", "주포 조준 부위 변경 · 디코이 사출");
            if (ship.Definition.DefenseDrones is not null) Add("4", "드론의 전후좌우 방어구역 지정");
            if (ship.Definition.ReconProbes is not null)
            {
                Add("N", "정찰 / 방공 / 제공 / 강습 / 복귀 · 프로브 사출");
                Add("지도 Shift + 클릭", "프로브 탐지범위 미리보기 · 지정 방향으로 전개");
            }
            if (ship.Definition.Antimatter is not null)
            {
                Add("3 · 좌클릭", "어뢰 발사 준비 → 준비 완료 후 발사 · 다른 무기 선택 시 취소");
                Add("Backspace 1초", "반물질탄 전량 비상 투기 · 해당 전투에서 재사용 불가");
            }
            Add("M · PgUp / PgDn", "전체 지도 열기 / 닫기 · 레이더 범위 변경");
            Add("Tab", "양팀 함선·편대 생존 현황");
            Add("Z · V", "비행보조 · 항공식/우주식 전환");
            Add("[ / ]", "전투 배속 낮추기 / 높이기");
            Add("Esc · F10", pilot ? "커서 해제 후 일시정지 · 효과음 켜기/끄기" : "일시정지 · 효과음 켜기/끄기");
        }
        else
        {
            Add("F2 · F3", "렌더 원점 전환 · 1,000 km 도약");
            Add("F4 · F5", "시험 레이 발사 · 모듈 표시");
            Add("F6", "전체 함선·탄약 복구");
            Add("F7 · F8", "이동 표적 · 미사일 훈련");
            Add("F9", "조종 함선 순환");
            Add("G / H / J", "집중공격 / 호위 / 위치 유지 (개발 단축키)");
        }
        return rows;
    }
}

public partial class Hud
{
    internal int HelpPage { get; private set; }
    private readonly List<Rect2> _helpTabs = new();
    private Rect2 _helpClose;
    internal Rect2 HelpBounds { get; private set; }
    internal int HelpPageCount => Game.DevMode || !Game.BattleMode ? 3 : 2;
    internal void ResetHelpPage() => HelpPage = 0;
    internal void HandleHelpInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed:true, Echo:false } key)
        {
            if (key.PhysicalKeycode is Key.Right or Key.Pagedown) HelpPage = (HelpPage + 1) % HelpPageCount;
            if (key.PhysicalKeycode is Key.Left or Key.Pageup) HelpPage = (HelpPage + HelpPageCount - 1) % HelpPageCount;
        }
        if (e is InputEventMouseButton {Pressed:true,ButtonIndex:MouseButton.Left} click)
        {
            if (_helpClose.HasPoint(click.Position)) { Game.SetHelp(false); return; }
            for (int i=0;i<_helpTabs.Count;i++) if (_helpTabs[i].HasPoint(click.Position)) HelpPage=i;
        }
        QueueRedraw();
    }
    private void DrawHelp(ShipView controlled, Vector2 screen)
    {
        if (!Game.ShowHelp)
        {
            Label(new(16,screen.Y-12),"F1 도움말 · M 전체 지도 · F10 효과음",12,Dim);
            return;
        }
        var entries = ShipHelp.Entries(controlled.Body,Game.Scheme,HelpPage);
        float width = Math.Min(860,screen.X-32), keysWidth = Math.Max(172,HudFont(13)*12);
        float actionWidth = width-keysWidth-58;
        var wrapped = entries.ConvertAll(r=>HudText.Wrap(_font,r.Action,HudFont(14),actionWidth));
        float line = HudFont(14)+6;
        float rowHeight(int index) => Math.Max(28,wrapped[index].Count*line+8);
        float content=0;for(int i=0;i<entries.Count;i++)content+=rowHeight(i);
        float height=134+content;
        HelpBounds = new((screen.X-width)*.5f,Math.Max(16,(screen.Y-height)*.5f),width,height);
        DrawRect(new(Vector2.Zero,screen),new Color(0,0,0,.66f));
        DrawRect(HelpBounds,new Color(.015f,.03f,.045f,.99f));
        float x=HelpBounds.Position.X+20,y=HelpBounds.Position.Y;
        Label(new(x,y+30),$"{controlled.Body.Class.Kind switch{HullKind.Battleship=>"전함",HullKind.Escort=>"호위·순양함",_=>"요격함"}} 조작",18,Text);
        _helpClose=new(HelpBounds.End.X-150,y+10,134,30);
        Label(_helpClose.Position+new Vector2(8,21),"닫기 · F1 / Esc",12,Friendly);
        _helpTabs.Clear();
        string[] titles={"필수 조작","공통·심화","개발용"};
        for(int i=0;i<HelpPageCount;i++)
        {
            var tab=new Rect2(x+i*160,y+48,148,32);_helpTabs.Add(tab);
            if(i==HelpPage)DrawRect(new(tab.Position+new Vector2(0,29),new Vector2(tab.Size.X,3)),Friendly);
            Label(tab.Position+new Vector2(8,21),titles[i],14,i==HelpPage?Text:Dim);
        }
        y+=101;
        for(int i=0;i<entries.Count;i++)
        {
            Label(new(x,y+HudFont(14)),entries[i].Keys,13,Friendly,width:keysWidth-12);
            for(int j=0;j<wrapped[i].Count;j++)Label(new(x+keysWidth,y+j*line+HudFont(14)),wrapped[i][j],14,Text);
            y+=rowHeight(i);
        }
        Label(new(x,HelpBounds.End.Y-14),"← → 항목 전환 · 전투는 계속 진행됩니다 · 글자 크기는 일시정지에서 변경",12,Dim,width:width-40);
    }
}

public partial class ScaleTest
{
    private Input.MouseModeEnum _helpMouseMode;
    internal void SetHelp(bool open)
    {
        if (ShowHelp==open) return;
        ShowHelp=open; _fireReleaseGuard=true;
        if(Controlled?.Body is {} ship) SimWorld.StopLasers(ship);
        if(open)
        {
            Camera.ResetTelescope();Camera.SetFreeLook(false);
            if(MenuOpen){_radial.Cancel();_radialAction=null;}
            _helpMouseMode=Input.MouseMode;Input.MouseMode=Input.MouseModeEnum.Visible;
            _hud.ResetHelpPage();
        }
        else Input.MouseMode=Spectating||Paused?Input.MouseModeEnum.Visible:_helpMouseMode;
    }
    private bool HandleHelpOverlayInput(InputEvent e)
    {
        if (!ShowHelp && (Paused || WorldMapOpen || RosterOpen || !e.IsActionPressed(InputSetup.ToggleHelp))) return false;
        if(e.IsActionReleased(InputSetup.Fire))_fireReleaseGuard=false;
        if(e.IsActionPressed(InputSetup.ToggleHelp) || ShowHelp && e.IsActionPressed(InputSetup.ReleaseMouse)) SetHelp(!ShowHelp);
        else _hud.HandleHelpInput(e);
        GetViewport().SetInputAsHandled();return true;
    }
}
