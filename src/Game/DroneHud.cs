using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

public partial class Hud
{
    private void DrawDrones(Camera3D cam, ShipView controlled, Vector2 screen)
    {
        var ship=controlled.Body;
        DrawNearbyDrones(cam,controlled,screen);
        DrawAuxiliaryDefense(ship,screen);
        if (ship.Definition.DefenseDrones is not { } def || controlled.Drones is not { } swarm) return;
        var drones=ship.Ordnance.Drones;
        var panel=new Rect2(screen.X-266,screen.Y-254,250,96);
        DrawRect(panel,PanelBack);
        Label(panel.Position+new Vector2(10,18),$"4 드론 · {DefenseDroneState.Label(drones.Sector)} 방어",12,Text);
        string status=ship.Damage.Destroyed ? "모함 격침" : drones.SurvivingCount==0 ? "전멸" : !drones.Active ? "통제 불능" : drones.ArmedCount==0 ? "탄약 소진"
            : drones.RepositioningCount>0 ? $"재배치 {drones.RepositioningCount}/{def.Count}" : "경계 중";
        Color color=!drones.Active || drones.ArmedCount==0 ? Hostile : drones.RepositioningCount>0 ? Motion : Good;
        Label(panel.Position+new Vector2(80,40),$"{drones.SurvivingCount}/{def.Count} 생존 · {status}",11,color);
        Label(panel.Position+new Vector2(80,60),$"잔탄 {drones.RemainingRounds} · 무장 {drones.ArmedCount}기",12,Dim);
        Label(panel.Position+new Vector2(80,80),$"요격 {drones.Interceptions} · 요격함 명중 {drones.ShipHits}",11,Dim);

        // Carrier-local top view, nose up. Dots use actual interpolated positions, not commanded stations.
        Vector2 center=panel.Position+new Vector2(40,59);
        const float radius=26;
        DrawArc(center,radius,0,Mathf.Tau,48,Faint,1);
        if (drones.Sector!=DroneSector.AllAround)
        {
            Vector3 axis=DefenseDroneState.Direction(drones.Sector);
            float angle=Mathf.Atan2(axis.Z,axis.X);
            var wedge=new Vector2[19]; wedge[0]=center;
            for(int i=1;i<wedge.Length;i++) wedge[i]=center+Vector2.FromAngle(angle-Mathf.Pi/4+(i-1)*Mathf.Pi/34)*radius;
            DrawColoredPolygon(wedge,new Color(color,.15f));
            DrawArc(center,radius,angle-Mathf.Pi/4,angle+Mathf.Pi/4,18,color,2);
        }
        DrawColoredPolygon(new[]{center+new Vector2(0,-7),center+new Vector2(4,5),center+new Vector2(-4,5)},Dim);
        CenteredLabel(center+new Vector2(0,-32),"전",9,Dim);
        for(int i=0;i<def.Count;i++)
        {
            if (!drones.Alive(i)) continue;
            Vector3 point=swarm.Multimesh.GetInstanceTransform(i).Origin;
            bool firing=ship.SimTime-drones.LastFiredAt[i]<.18;
            Color dot=!drones.Active || drones.Rounds[i]==0 ? Dim : firing ? Text : Health(drones.HealthFraction(i));
            DrawCircle(center+new Vector2(point.X,point.Z)/def.OrbitMeters*radius,2,dot);
            if (ship.Damage.Destroyed) continue;
            Vector3 render=controlled.GlobalTransform*point;
            if (cam.IsPositionBehind(render)) continue;
            Vector2 p=cam.UnprojectPosition(render);
            if (!new Rect2(0,0,screen.X,screen.Y).HasPoint(p)) continue;
            // Small brackets identify otherwise subpixel drones without changing their physical size.
            DrawPolyline(new[]{p+new Vector2(-3,0),p+new Vector2(0,-3),p+new Vector2(3,0),p+new Vector2(0,3),p+new Vector2(-3,0)},dot,1);
            if (firing) DrawArc(p,6,0,Mathf.Tau,12,Text,1);
        }
    }

    private void DrawNearbyDrones(Camera3D cam,ShipView controlled,Vector2 screen)
    {
        if(controlled.Body.Damage.SensorFraction<=.01f) return;
        foreach(var carrier in Game.Views)
        {
            if(carrier==controlled || carrier.Body.Damage.Destroyed || carrier.Drones is not { } swarm) continue;
            var drones=carrier.Body.Ordnance.Drones;
            bool enemy=carrier.Body.Faction!=controlled.Body.Faction;
            if(enemy && Game.TrackOf(carrier).Level<TrackLevel.Contact) continue;
            for(int i=0;i<drones.Rounds.Length;i++)
            {
                if(!drones.Alive(i)) continue;
                Vector3 render=carrier.GlobalTransform*swarm.Multimesh.GetInstanceTransform(i).Origin;
                float range=render.DistanceTo(controlled.Position);
                if(range>4000 || cam.IsPositionBehind(render)) continue;
                Vector2 p=cam.UnprojectPosition(render);
                if(!new Rect2(0,0,screen.X,screen.Y).HasPoint(p)) continue;
                Color color=enemy ? Hostile : Friendly;
                DrawPolyline(new[]{p+new Vector2(-4,0),p+new Vector2(0,-4),p+new Vector2(4,0),p+new Vector2(0,4),p+new Vector2(-4,0)},color,1);
                if(enemy && range<2000)
                {
                    HBar(new Rect2(p.X-6,p.Y+7,12,2),drones.HealthFraction(i),color);
                    if((p-Game.Camera.AimScreenPosition()).LengthSquared()<100*100)
                        Label(p+new Vector2(9,-5),$"드론 · {range/1000:0.0} km",10,color);
                }
            }
        }
    }

    private void DrawAuxiliaryDefense(ShipBody ship,Vector2 screen)
    {
        var mounts=ship.Ordnance.PointDefense;
        if(ship.Class.Kind!=HullKind.Interceptor || mounts.Length==0) return;
        float height=50+mounts.Length*20;
        var panel=new Rect2(screen.X-266,screen.Y-158-height,250,height);
        DrawRect(panel,PanelBack);
        Label(panel.Position+new Vector2(10,18),$"보조 포탑 {mounts.Length}기 · 후방 방어",12,Text);
        for(int i=0;i<mounts.Length;i++)
        {
            var mount=mounts[i];
            string target=mount.TargetKind switch { PointDefenseTarget.Drone=>"드론",PointDefenseTarget.Missile=>"미사일/어뢰",PointDefenseTarget.Interceptor=>"요격함",_=>"" };
            string state=ship.Damage.Destroyed || ship.Damage.SensorFraction<=.01f || ship.Power.WeaponEffect<=.001f ? "통제 불능"
                : mount.TargetKind==PointDefenseTarget.None ? "경계 중" : ship.SimTime-mount.LastFiredAt<.3 ? $"{target} 사격" : $"{target} 추적";
            string deck=mount.Definition.Mounts[i].Y<0 ? "하부" : "상부";
            Label(panel.Position+new Vector2(10,39+i*20),$"{deck} · {state}",11,mount.TargetKind==PointDefenseTarget.Drone ? Good : Dim);
        }
        Label(panel.Position+new Vector2(10,height-10),$"드론 격추 {Game.World.Log?.Ship(ship).DronesDestroyed??0} · 자동 요격",11,Dim);
    }
}
