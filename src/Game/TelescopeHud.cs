using Godot;
using SpaceFleet.View;

namespace SpaceFleet.Game;

public partial class Hud
{
    private void DrawTelescope(ShipView controlled,Vector2 screen)
    {
        var cam=Game.Camera;
        Vector2 center=cam.AimScreenPosition();
        float radius=Mathf.Max(60,Mathf.Min(screen.Y*.25f,Mathf.Min(screen.X*.2f,screen.Y*.5f-220)));
        bool blocked=Game.ScopeHullBlocked;
        Color ink=blocked ? Motion : Text;
        // An open optical frame keeps the radar, systems, target panel and battlefield visible.
        for(int quadrant=0;quadrant<4;quadrant++)
        {
            float angle=quadrant*Mathf.Pi/2;
            DrawArc(center,radius,angle+.16f,angle+Mathf.Pi/2-.16f,28,new Color(Friendly,.3f),1,true);
            Vector2 axis=Vector2.FromAngle(angle);
            DrawLine(center+axis*5,center+axis*19,ink,1.5f,true);
            DrawLine(center+axis*(radius-8),center+axis*(radius+5),Friendly,2,true);
        }
        DrawCircle(center,1.4f,ink);
        float focal=screen.Y*.5f/Mathf.Tan(Mathf.DegToRad(cam.Fov*.5f));
        for(int degree=1;degree<=5;degree++)
        {
            float pixel=Mathf.Tan(Mathf.DegToRad(degree))*focal;
            if(pixel>radius-20) break;
            float length=degree%2==0 ? 6 : 3;
            foreach(float side in new[]{-1f,1f})
            {
                Vector2 horizontal=center+new Vector2(side*pixel,0),vertical=center+new Vector2(0,side*pixel);
                DrawLine(horizontal+new Vector2(0,-length),horizontal+new Vector2(0,length),Dim,1,true);
                DrawLine(vertical+new Vector2(-length,0),vertical+new Vector2(length,0),Dim,1,true);
                if(degree%2==0) CenteredLabel(horizontal+new Vector2(0,19),$"{degree}°",10,Dim);
            }
        }
        float magnification=Mathf.Tan(Mathf.DegToRad(ChaseCamera.NormalFov*.5f))/Mathf.Tan(Mathf.DegToRad(cam.Fov*.5f));
        var badge=new Rect2(screen.X*.5f-118,22,236,58);
        DrawRect(badge,PanelBack); DrawRect(badge,new Color(Friendly,.5f),false);
        CenteredLabel(new(screen.X*.5f,43),"망원 조준",12,Friendly);
        CenteredLabel(new(screen.X*.5f,67),$"×{magnification:0.0}  ·  {(Game.Scheme==ControlScheme.Pilot ? "기수 연동" : "독립 조준")}",17,Text);
        Vector3 local=controlled.Quaternion.Inverse()*cam.AimForward;
        float bearing=Mathf.RadToDeg(Mathf.Atan2(local.X,-local.Z));
        float elevation=Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(local.Y,-1,1)));
        CenteredLabel(center+new Vector2(0,-radius-15),$"방위 {bearing:+0;-0;0}°    고각 {elevation:+0;-0;0}°",11,Dim);
        CenteredLabel(new(screen.X*.5f,screen.Y*.5f+radius+23),"좌클릭 발사 · R 표적 선택 · 우클릭 해제 복귀",11,Dim);
        if(blocked) CenteredLabel(center+new Vector2(0,48),"선체에 시야 가림",13,Motion);
        else if(Game.Scheme==ControlScheme.Pilot && controlled.Body.Forward.AngleTo(cam.AimForward)
            > Mathf.DegToRad(controlled.Body.Railgun!.Definition.TraverseDegrees))
            CenteredLabel(center+new Vector2(0,48),"기수 정렬 중",13,Motion);
        else if(Game.Scheme==ControlScheme.Helm && Game.LastFireFailed && Game.World.Time-Game.LastFireTime<1.5)
            CenteredLabel(center+new Vector2(0,48),Game.LastFireMessage,13,Motion);
    }
}
