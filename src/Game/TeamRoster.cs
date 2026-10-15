using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

// Public roster information only: no positions, target locks, HP or internal modules.
public sealed record TeamRosterRow(string Captain,string Hull,string Composition,HullKind Kind,int Surviving,int Total,bool Mine);
public static class TeamRoster
{
    public static TeamRosterRow[] Rows(MatchLineup? match, ScaleTest? battle, Faction team)
    {
        if(match is not null)return match.Participants.Where(p=>p.Team==team).Select(p=>
        {
            var squad=battle?.World.Squadrons.FirstOrDefault(s=>s.Name==p.SquadronName&&s.Faction==team);
            return new TeamRosterRow(p.Name,p.Hull.Flight.DisplayName,p.Composition,p.Kind,
                squad?.Members.Count(s=>!s.Damage.Destroyed)??p.ShipCount,p.ShipCount,p.IsPlayer);
        }).ToArray();
        return battle?.World.Squadrons.Where(s=>s.Faction==team).Select(s=>new TeamRosterRow(s.Name,
            s.Members[0].Class.DisplayName,$"{s.Members.Count}척",s.Members[0].Class.Kind,
            s.Members.Count(m=>!m.Damage.Destroyed),s.Members.Count,s==battle.OwnedSquadron)).ToArray()??Array.Empty<TeamRosterRow>();
    }
}

public partial class BattleOverlay
{
    private void DrawRoster(MatchLineup? match,ScaleTest? battle)
    {
        bool matching=battle is null;
        DrawRect(new(0,0,1600,900),new(.005f,.015f,.03f,.96f));
        Center(105,matching?"전투 편성 완료":"양팀 전투 현황",36);
        Center(147,matching?"10 대 10 편대 · AI 무작위 매칭":"공개 편성 · 생존 척수 / 출격 척수",18,Blue);
        foreach(Faction team in Enum.GetValues<Faction>())
        {
            var rows=TeamRoster.Rows(match,battle,team);
            float x=team==Faction.Blue?120:825;
            Color color=team==Faction.Blue?Blue:Red;
            DrawRect(new(x,180,655,570),new(.025f,.055f,.09f,.95f));
            Text(new(x+20,217),team==Faction.Blue?"아군 함대":"적군 함대",24,color);
            Text(new(x+365,217),$"{rows.Count(r=>r.Surviving>0)} / {rows.Length} 편대 · {rows.Sum(r=>r.Surviving)} / {rows.Sum(r=>r.Total)}척",17,color);
            Text(new(x+20,245),$"전함 편대 {rows.Count(r=>r.Kind==HullKind.Battleship)}   호위 편대 {rows.Count(r=>r.Kind==HullKind.Escort)}   요격 편대 {rows.Count(r=>r.Kind==HullKind.Interceptor)}",16);
            for(int i=0;i<rows.Length;i++)
            {
                var row=rows[i];float y=266+i*46;
                if(row.Mine)DrawRect(new(x+8,y,639,44),new Color(Blue,.14f));
                else if(i%2==0)DrawRect(new(x+8,y,639,44),new Color(Ink,.025f));
                Color ink=row.Surviving==0?new Color(Ink,.35f):Ink;
                Ship(new(x+31,y+23),row.Kind,row.Surviving==0?new Color(color,.3f):color,.22f);
                Text(new(x+58,y+19),row.Captain,17,row.Mine?Blue:ink);
                Text(new(x+190,y+19),row.Hull,16,ink);
                Text(new(x+190,y+37),row.Composition,13,new Color(ink,.6f));
                Text(new(x+533,y+26),row.Surviving==0?"전멸":$"{row.Surviving} / {row.Total}",18,row.Surviving==0?Red:ink);
            }
        }
        if(!matching)Center(805,"Tab을 놓으면 전장으로 돌아갑니다 · 전투는 계속 진행 중",18,Blue);
        else Center(775,$"출격 준비 · {Math.Max(1,(int)Math.Ceiling(Host.MatchingRemaining))}",18,Blue);
    }
}
