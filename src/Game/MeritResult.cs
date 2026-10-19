using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class BattleOverlay
{
    private bool _showMerits=true;
    private int _meritScope;
    private void MeritResultButtons()
    {
        Button(_showMerits?"전투 흐름 · 기존 기록":"내 전투 성과",new(1160,120,280,40),()=>{_showMerits=!_showMerits;Rebuild();});
        if(!_showMerits)return;
        string[] scopes={"직접 조종","AI 편대원","내 편대 합계","아군 팀 합계"};
        for(int i=0;i<4;i++){int index=i;Button((_meritScope==i?"● ":"")+scopes[i],new(260+i*275,200,255,42),()=>{_meritScope=index;Rebuild();});}
    }
    private void DrawMeritResult(ScaleTest battle)
    {
        var ledger=battle.World.Merits;
        // Normal play freezes at the exact outcome tick. Screenshot fixtures may stop before victory.
        var snapshot=ledger.Outcome;
        var personal=snapshot?.Personal??ledger.Personal();
        var members=battle.OwnedSquadron?.Members??Array.Empty<ShipBody>();
        var squad=snapshot?.Sum(members)??ledger.Sum(members);
        var stats=_meritScope switch {1=>MeritStats.Difference(squad,personal),2=>squad,3=>snapshot?.Team(Faction.Blue)??ledger.Team(Faction.Blue),_=>personal};
        string[][] columns={
            new[]{"공격",$"격침  {stats[MeritKind.Kill]}",$"격침 지원  {stats[MeritKind.Assist]}",$"무력화  {stats[MeritKind.Disabled]}",$"명중 / 관통  {stats[MeritKind.Hit]} / {stats[MeritKind.Penetration]}",$"유효 피해  {stats.Damage:N0}",$"계통 파괴  {stats[MeritKind.ModuleDestroyed]}"},
            new[]{"방어",$"미사일 요격  {stats[MeritKind.MissileIntercept]}",$"강습어뢰 요격  {stats[MeritKind.TorpedoIntercept]}",$"프로브 격추  {stats[MeritKind.ProbeDestroyed]}",$"아군 보호  {stats[MeritKind.Protection]}",$"기함 보호  {stats[MeritKind.FlagshipProtection]}"},
            new[]{"정찰",$"신규 탐지  {stats[MeritKind.Detection]}",$"신규 식별  {stats[MeritKind.Identification]}",$"지원 명중  {stats[MeritKind.ScoutHit]}",$"지원 피해  {stats.ScoutDamage:N0}",$"지원 격침  {stats[MeritKind.ScoutKill]}"},
            new[]{"목표",$"점령 공로  {stats[MeritKind.Capture]}",$"거점 방어  {stats[MeritKind.Defense]}",$"점령 진행  {stats.CaptureProgress:0.0}",$"점령 참여  {stats.CaptureSeconds:0.0}초",$"경합 방어  {stats.DefenseSeconds:0.0}초",$"기함에 준 피해  {stats.FlagshipDamage:N0}"}};
        for(int i=0;i<4;i++)
        {
            float x=260+i*275;Text(new(x,284),columns[i][0],22,Blue);
            DrawLine(new(x,298),new(x+248,298),new Color(Ink,.25f));
            for(int row=1;row<columns[i].Length;row++)Text(new(x,305+row*27),columns[i][row],18);
        }
        Text(new(260,520),"주요 전과",20,Blue);
        var timeline=(snapshot?.Important??ledger.Important.ToArray()).Where(e=>_meritScope switch{
            0=>e.Personal,1=>!e.Personal&&members.Contains(e.Actor),2=>members.Contains(e.Actor),_=>e.Team==Faction.Blue})
            .TakeLast(5).ToArray();
        if(timeline.Length==0)Text(new(260,555),"기록된 중요 전과가 없습니다.",18,new Color(Ink,.65f));
        for(int i=0;i<timeline.Length;i++)
        {
            var e=timeline[i];string at=TimeSpan.FromSeconds(e.Time).ToString(@"mm\:ss");
            Text(new(260,551+i*25),$"{at}  {e.Actor.Callsign}  ·  {MeritText.Message(e)}",17);
        }
        Text(new(260,687),"정찰 지원은 실제 사용한 관측만 인정 · 전과는 전투 판정 시 확정",16,new Color(Ink,.65f));
        if(Host.LastResearchReward is {} reward)Center(710,$"연구 점수 +{reward} · 기존 보상 기준 적용",18,Blue);
    }
}
