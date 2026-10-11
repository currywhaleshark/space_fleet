using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class Hud
{
    private void DrawBattleHud(Vector2 screen)
    {
        if(!Game.BattleMode||Game.World.Rules is not {} rules)return;
        float x=screen.X*.5f-200;
        DrawRect(new(x-12,14,424,30),PanelBack);
        DrawRect(new(x,24,185,8),Faint);DrawRect(new(x,24,185*(float)rules.Fraction(Faction.Blue),8),Friendly);
        var enemies=Game.World.Ships.Where(s=>s.Faction==Faction.Red).ToArray();
        var identified=enemies.Where(s=>Game.World.Sensors.Track(Faction.Blue,s).Level>=TrackLevel.Identified).ToArray();
        float initial=(float)rules.Initial(Faction.Red);
        float known=(float)identified.Where(Squadron.Active).Sum(s=>BattleRules.Weight(s.Class.Kind))/initial;
        float unknown=1-(float)identified.Sum(s=>BattleRules.Weight(s.Class.Kind))/initial;
        var enemyBar=new Rect2(x+215,24,185,8);DrawRect(enemyBar,Faint);
        DrawRect(new(enemyBar.Position,new Vector2(185*known,8)),Hostile);
        float unknownStart=enemyBar.Position.X+185*known, unknownEnd=Mathf.Min(enemyBar.End.X,unknownStart+185*unknown);
        for(float at=unknownStart;at<unknownEnd;at+=7)
            DrawLine(new(at,32),new(Mathf.Min(at+5,unknownEnd),24),Dim,1);
        // The mode banner augments the legacy strength bars without revealing unknown enemy internals.
        if (rules.Objective == BattleObjective.Flagship)
        {
            ShipBody? own = rules.Flagship(Faction.Blue), enemyFlagship = rules.Flagship(Faction.Red);
            string ownState = own is null ? "기함 없음" : own.Damage.Destroyed ? "격침" : own.Damage.Disabled ? "무력화" : "작전 가능";
            bool identified = enemyFlagship is not null && Game.World.Sensors.Track(Faction.Blue, enemyFlagship).Level >= TrackLevel.Identified;
            string enemyState = enemyFlagship is null ? "미확인" : !identified ? "상태 불명"
                : enemyFlagship.Damage.Destroyed ? "격침" : enemyFlagship.Damage.Disabled ? "무력화" : "작전 가능";
            DrawRect(new Rect2(x-12,51,424,25),PanelBack);
            Label(new Vector2(x,69),$"기함전   아군 {ownState}  /  적군 {enemyState}",13,Text);
        }
        else if (rules.Objective == BattleObjective.Domination)
        {
            DrawRect(new Rect2(x-12,49,424,49),PanelBack);
            Label(new Vector2(x,65),$"점령전   {rules.Score(Faction.Blue):0} : {rules.Score(Faction.Red):0}   /   {rules.ScoreLimit}",14,Text);
            int i = 0;
            foreach (CaptureZone zone in rules.CaptureZones)
            {
                Color color = zone.Owner == Faction.Blue ? Friendly : zone.Owner == Faction.Red ? Hostile : Motion;
                float zx=x+5+i*135;
                Label(new Vector2(zx,87),zone.Name+(zone.Contested?"  경합":zone.Owner is null?"  중립":zone.Owner==Faction.Blue?"  아군":"  적군"),12,color);
                i++;
            }
        }
        float y=screen.Y-272;DrawRect(new(16,y,250,32),PanelBack);
        FleetPosture posture=Game.World.Fleet(Faction.Blue).Posture;
        for(int i=0;i<4;i++)
        {
            var p=new Vector2(46+i*59,y+16);
            bool active=posture==(i switch{0=>FleetPosture.Missile,1=>FleetPosture.Gunline,2=>FleetPosture.Close,_=>FleetPosture.Withdraw});
            Color c=active?Friendly:Faint;
            if(active)DrawRect(new(p-new Vector2(22,13),new Vector2(44,26)),new Color(Friendly,.12f));
            if(i==0){DrawLine(p+new Vector2(-10,7),p+new Vector2(7,-7),c,2);DrawLine(p+new Vector2(0,-7),p+new Vector2(7,-7),c,2);DrawLine(p+new Vector2(7,0),p+new Vector2(7,-7),c,2);}
            else if(i==1){for(int n=-1;n<=1;n++)DrawLine(p+new Vector2(-13,n*6),p+new Vector2(13,n*6),c,2);}
            else if(i==2){DrawDiamond(p,11,c);DrawLine(p+new Vector2(-7,-7),p+new Vector2(7,7),c,2);}
            else{DrawLine(p+new Vector2(12,0),p+new Vector2(-12,0),c,2);DrawLine(p+new Vector2(-4,-7),p+new Vector2(-12,0),c,2);DrawLine(p+new Vector2(-4,7),p+new Vector2(-12,0),c,2);}
        }
    }
}
