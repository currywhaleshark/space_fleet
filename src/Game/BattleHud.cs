using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class Hud
{
    /// <summary>
    /// 기함전: 아군 기함(왼쪽)·적 기함(오른쪽) 아이콘. 전함 실루엣 + 실드 호(위)·선체 계통 막대(아래).
    /// 정상 = 진영색, 응급복구로 무력화 회복 중 = 주황 깜박임, 무력화 = 빨간 가로줄, 격침 = X, 적 미식별 = 점선 윤곽과 "?".
    /// </summary>
    private void DrawFlagshipObjective(BattleRules rules, float x)
    {
        DrawRect(new(x - 12, 48, 424, 44), PanelBack);
        DrawFlagshipIcon(rules.Flagship(Faction.Blue), new Vector2(x + 92, 70), Friendly, known: true);
        // 가운데: 두 기함을 잇는 표적 화살(이 모드의 승리 조건).
        DrawLine(new(x + 170, 70), new(x + 230, 70), Faint, 1.5f);
        DrawColoredPolygon(new[] { new Vector2(x + 236, 70), new Vector2(x + 228, 65), new Vector2(x + 228, 75) }, Faint);
        ShipBody? enemy = rules.Flagship(Faction.Red);
        bool known = enemy is not null && Game.World.Sensors.Track(Faction.Blue, enemy).Level >= TrackLevel.Identified;
        DrawFlagshipIcon(enemy, new Vector2(x + 308, 70), Hostile, known);
    }

    private void DrawFlagshipIcon(ShipBody? ship, Vector2 c, Color side, bool known)
    {
        // 전함 실루엣: 긴 선체 + 함교 + 함수 쐐기.
        Vector2[] hull = { c + new Vector2(-58, -4), c + new Vector2(40, -4), c + new Vector2(58, 0), c + new Vector2(40, 4), c + new Vector2(-58, 4) };
        if (ship is null || !known)
        {
            for (int i = 0; i < hull.Length; i++)
            {
                Vector2 a = hull[i], b = hull[(i + 1) % hull.Length];
                DrawDashedLine(a, b, new Color(side, .5f), 1.2f, 4);
            }
            Label(c + new Vector2(-4, -8), "?", 13, new Color(side, .7f));
            return;
        }
        bool recovering = ship.Damage.Disabled && ship.DamageControl.RecoveringDisable;
        Color color = ship.Damage.Destroyed ? Dim : recovering ? ((Time.GetTicksMsec() / 250) % 2 == 0 ? Motion : new Color(Motion, .35f))
            : ship.Damage.Disabled ? Hostile : side;
        DrawColoredPolygon(hull, new Color(color, .35f));
        DrawPolyline(hull.Append(hull[0]).ToArray(), color, 1.5f);
        DrawRect(new(c + new Vector2(14, -10), new Vector2(10, 6)), color);
        // 실드(위 호)와 선체 계통 평균(아래 막대).
        float shield = ship.Damage.ShieldCapacity > 0 ? ship.Damage.Shield / ship.Damage.ShieldCapacity : 0;
        HBar(new(c + new Vector2(-58, -16), new Vector2(116, 3)), shield, Friendly);
        float systems = (ship.Damage.PropulsionFraction + ship.Damage.WeaponsFraction + ship.Damage.SensorFraction + ship.Damage.GenerationFraction) / 4f;
        HBar(new(c + new Vector2(-58, 10), new Vector2(116, 3)), systems, Health(systems));
        if (ship.Damage.Destroyed)
        {
            DrawLine(c + new Vector2(-14, -12), c + new Vector2(14, 12), Hostile, 2.5f);
            DrawLine(c + new Vector2(-14, 12), c + new Vector2(14, -12), Hostile, 2.5f);
        }
        else if (ship.Damage.Disabled) DrawLine(c + new Vector2(-40, 0), c + new Vector2(40, 0), recovering ? Motion : Hostile, 2.5f);
    }

    /// <summary>
    /// 점령전: 점수 경주 막대(가운데에서 양쪽으로, 끝 = 목표 점수)와 거점 3개 원.
    /// 거점 원: 소유 색 채움, 바깥 호 = 점령 진행(파랑 시계·빨강 반시계), 경합 = 주황 깜박임 테두리. 글자는 거점 이름뿐.
    /// </summary>
    private void DrawDominationObjective(BattleRules rules, float x)
    {
        DrawRect(new(x - 12, 48, 424, 58), PanelBack);
        float blue = (float)(rules.Score(Faction.Blue) / rules.ScoreLimit), red = (float)(rules.Score(Faction.Red) / rules.ScoreLimit);
        DrawRect(new(x, 56, 185, 6), Faint); DrawRect(new(x + 185 * (1 - Mathf.Clamp(blue, 0, 1)), 56, 185 * Mathf.Clamp(blue, 0, 1), 6), Friendly);
        DrawRect(new(x + 215, 56, 185, 6), Faint); DrawRect(new(x + 215, 56, 185 * Mathf.Clamp(red, 0, 1), 6), Hostile);
        DrawLine(new(x + 200, 52), new(x + 200, 66), Dim, 1);
        int count = rules.CaptureZones.Count;
        for (int i = 0; i < count; i++)
        {
            CaptureZone zone = rules.CaptureZones[i];
            var c = new Vector2(x + 200 + (i - (count - 1) * .5f) * 70, 86);
            Color owner = zone.Owner == Faction.Blue ? Friendly : zone.Owner == Faction.Red ? Hostile : Dim;
            DrawCircle(c, 12, new Color(owner, zone.Owner is null ? .12f : .4f));
            DrawArc(c, 12, 0, Mathf.Tau, 24, owner, 1.5f);
            float progress = (float)(zone.Progress / 100.0);
            if (Mathf.Abs(progress) > .01f)
                DrawArc(c, 16, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Sign(progress) * Mathf.Tau * Mathf.Abs(progress), 32,
                    progress > 0 ? Friendly : Hostile, 2.5f);
            if (zone.Contested && (Time.GetTicksMsec() / 300) % 2 == 0) DrawArc(c, 20, 0, Mathf.Tau, 24, Motion, 2);
            CenteredLabel(c + new Vector2(0, 5), zone.Name, 12, Text);
        }
    }

    private void DrawBattleHud(Vector2 screen)
    {
        if(!Game.BattleMode||Game.World.Rules is not {} rules)return;
        // Prototype score strip: blue number · bar · objectives · bar · red number, clock underneath.
        float cx=Mathf.Min(screen.X*.5f,screen.X-23-302-200),cy=48;
        bool domination=rules.Objective==BattleObjective.Domination;
        var enemies=Game.World.Ships.Where(s=>s.Faction==Faction.Red).ToArray();
        var identified=enemies.Where(s=>Game.World.Sensors.Track(Faction.Blue,s).Level>=TrackLevel.Identified).ToArray();
        float initial=(float)rules.Initial(Faction.Red);
        float blue=domination?(float)(rules.Score(Faction.Blue)/rules.ScoreLimit):(float)rules.Fraction(Faction.Blue);
        float known=domination?(float)(rules.Score(Faction.Red)/rules.ScoreLimit):(float)identified.Where(Squadron.Active).Sum(s=>BattleRules.Weight(s.Definition))/initial;
        float unknown=domination?0:1-(float)identified.Sum(s=>BattleRules.Weight(s.Definition))/initial;
        string blueText=domination?$"{rules.Score(Faction.Blue):0}":$"{blue*100:0}",redText=domination?$"{rules.Score(Faction.Red):0}":unknown>.005f?$"{known*100:0}?":$"{known*100:0}";
        int zones=domination?rules.CaptureZones.Count:0;
        float middle=zones>0?zones*24+(zones-1)*5:28;
        var blueBar=new Rect2(cx-middle*.5f-14-125,cy-2,125,4);var redBar=new Rect2(cx+middle*.5f+14,cy-2,125,4);
        DrawRect(blueBar,HudStyle.Rgb(0x89acbb,.18f));DrawRect(new Rect2(blueBar.Position,new(125*Mathf.Clamp(blue,0,1),4)),HudStyle.Cyan);
        DrawRect(redBar,HudStyle.Rgb(0x89acbb,.18f));DrawRect(new Rect2(redBar.Position,new(125*Mathf.Clamp(known,0,1),4)),HudStyle.Red);
        float unknownStart=redBar.Position.X+125*Mathf.Clamp(known,0,1),unknownEnd=Mathf.Min(redBar.End.X,unknownStart+125*unknown);
        for(float at=unknownStart;at<unknownEnd;at+=5)DrawLine(new(at,redBar.End.Y),new(Mathf.Min(at+3,unknownEnd),redBar.Position.Y),new Color(HudStyle.Red,.45f),1);
        Mono(new(blueBar.Position.X-14-200,cy+6),blueText,18,HudStyle.Cyan,HorizontalAlignment.Right,200);
        Mono(new(redBar.End.X+14,cy+6),redText,18,HudStyle.Red);
        if(domination)
            for(int i=0;i<zones;i++)
            {
                CaptureZone zone=rules.CaptureZones[i];
                var c=new Vector2(cx-middle*.5f+12+i*29,cy);
                Color owner=zone.Owner==Faction.Blue?HudStyle.Cyan:zone.Owner==Faction.Red?HudStyle.Red:HudStyle.Dim;
                if(zone.Owner is not null)DrawCircle(c,12,new Color(owner,.12f));
                DrawArc(c,12,0,Mathf.Tau,32,owner,1,true);
                float progress=(float)(zone.Progress/100.0);
                if(Mathf.Abs(progress)>.01f)DrawArc(c,15,-Mathf.Pi/2,-Mathf.Pi/2+Mathf.Sign(progress)*Mathf.Tau*Mathf.Abs(progress),32,progress>0?HudStyle.Cyan:HudStyle.Red,2,true);
                if(zone.Contested&&(Time.GetTicksMsec()/300)%2==0)DrawArc(c,18,0,Mathf.Tau,32,Motion,1.5f,true);
                Mono(new(c.X-12,c.Y+HudFont(11)*.36f),zone.Name,11,owner,HorizontalAlignment.Center,24);
            }
        else DrawDiamond(new(cx,cy),5,new Color(HudStyle.Ink,.35f));
        int seconds=(int)Game.World.Time;
        Mono(new(cx-40,cy+24),$"{seconds/60:00}:{seconds%60:00}",11,HudStyle.Rgb(0x94a8b5),HorizontalAlignment.Center,80);
        if(rules.Objective==BattleObjective.Flagship)
        {
            DrawSetTransform(new Vector2(cx-200-(Mathf.Min(screen.X*.5f-200,screen.X-744)),34));
            DrawFlagshipObjective(rules,Mathf.Min(screen.X*.5f-200,screen.X-744));
            DrawSetTransform(Vector2.Zero);
        }
    }

}
