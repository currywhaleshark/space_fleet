using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class ScaleTest
{
    public bool AntimatterSelected => SelectedWeapon==PlayerWeapon.Antimatter;
    public float JettisonProgress { get; private set; }
    public FiringSolution? AntimatterSolution
    {
        get
        {
            if (!AntimatterSelected || Controlled?.Body is not { } ship || ship.Definition.Antimatter is not { } am
                || FireTarget is not { } target || TrackOf(target) is not { Level: >= TrackLevel.Locked } track) return null;
            Vector3 direction=SimWorld.AntimatterLaunchDirection(ship,target.Body,track,AimModule?.Definition.Center,out double flight);
            Vec3d start=ship.Position+Vec3d.From(ship.Orientation*am.Flight.LaunchPoint);
            double range=(track.EstimatedPosition-start).Length();
            return new(flight>0,"AM 선행",direction,start+Vec3d.From(direction)*range,flight,track.ErrorMeters,range);
        }
    }

    private void FireSelectedWeapon()
    {
        if (SelectedWeapon==PlayerWeapon.Missile) { LaunchMissileAtTarget(); return; }
        if (!AntimatterSelected) return;
        if (Controlled?.Body is not { } player) return;
        var am=player.Ordnance.Antimatter;
        if (am.Mode==AntimatterMode.Safe)
        { bool began=am.BeginArming(); Notify(began ? "AM 발사 준비" : am.Status+(am.Warning ? " · 격리 손상, 투기 권고" : ""),!began); return; }
        if (InspectTarget is not { } target || target.Body.Faction==player.Faction)
        { Notify("AM 표적 없음 · R로 적 선택",true); return; }
        var attempt=World.LaunchAntimatter(player,target.Body,AimModule?.Definition.Center);
        Notify(attempt.Reason,!attempt.Fired);
    }

    internal void StepJettison()
    {
        if (Paused || Spectating || AutoPlay || MapControlsBlocked || MenuOpen || RadarPointerCaptured || !Input.IsActionPressed(InputSetup.JettisonAntimatter)
            || Controlled?.Body.Ordnance.Antimatter is not { Rounds: > 0 } am)
        { JettisonProgress=0; return; }
        JettisonProgress += (float)SimWorld.TickDelta;
        if (JettisonProgress < 1) return;
        bool ejected=am.Jettison(); JettisonProgress=0;
        Notify(ejected ? "AM 전량 투기 · 이번 전투 재사용 불가" : "AM 투기 불가",!ejected);
    }
}

public partial class Hud
{
    private void DrawAntimatter(ShipBody ship, Vector2 screen)
    {
        var am=ship.Ordnance.Antimatter;
        if(am.Definition is not { } def) return;
        float width=294+Mathf.Round((HudSettings.TextScale-1)*150);
        bool beside=screen.X>=1560;
        if(beside)width=Mathf.Min(width,screen.X*.5f-490);
        Vector2 start=beside ? new(screen.X*.5f+212,screen.Y-155) : new(screen.X*.5f-width*.5f,screen.Y-325);
        if(!beside)start.Y=CombatHudLayout.For(screen,false,ship.Railguns.Length,HudSettings.TextScale).Selection.Position.Y-137;
        if(Game.Camera.TelescopeHeld && !beside) start=new(16,screen.Y*.5f-40);
        // Stowed, healthy ordnance needs status, not firing instructions over the battlefield.
        // Any preparation, damage warning or jettison returns the complete warning/control panel.
        if(!Game.AntimatterSelected&&!am.Warning&&am.Mode==AntimatterMode.Safe&&Game.JettisonProgress<=0)
        {
            start+=new Vector2(0,75); // Keep the panel's bottom edge, leaving the aiming area clear.
            DrawRect(new(start,new Vector2(width,50)),PanelBack);
            Label(start+new Vector2(10,20),$"AM {am.Rounds}/{def.Rounds} · {am.Status}",15,Text);
            Label(start+new Vector2(10,41),$"격리용기 {am.Containment*100:0}% · 3 어뢰 선택",13,Dim);
            return;
        }
        var panel=new Rect2(start,new Vector2(width,125));
        DrawRect(panel,PanelBack);
        Color color=am.Warning || am.Mode==AntimatterMode.Failed ? Hostile : Game.AntimatterSelected ? Lead : Dim;
        DrawRect(panel,new Color(color,.5f),false,1);
        Label(start+new Vector2(10,20),$"AM {am.Rounds}/{def.Rounds}",15,color);
        Label(start+new Vector2(120,20),am.Status,13,color);
        Label(start+new Vector2(10,42),$"격리용기 {am.Containment*100:0}%",13,am.Warning ? Hostile : Text);
        var bar=new Rect2(start+new Vector2(180,34),new Vector2(102,5));
        DrawRect(bar,Faint); DrawRect(new Rect2(bar.Position,new Vector2(bar.Size.X*am.Containment,bar.Size.Y)),color);
        string range="권장 2–5 km · 최대 비행 8 km";
        Color rangeColor=Dim;
        if(Game.InspectTarget is { } target && target.Body.Faction!=ship.Faction && Game.TrackOf(target) is { Level: >= TrackLevel.Contact } track)
        {
            double distance=(track.EstimatedPosition-ship.Position).Length();
            bool suitable=distance>=def.RecommendedMinMeters && distance<=def.RecommendedMaxMeters;
            range=$"{distance/1000:0.0} km · "+(distance>def.MaxTravelMeters ? "도달 전 소멸" : suitable ? "강습 사거리" : distance<def.RecommendedMinMeters ? "근접 · 즉시 이탈 준비" : "권장 사거리 밖");
            rangeColor=suitable ? Good : Motion;
        }
        Label(start+new Vector2(10,64),am.Warning ? "격리 위험 · 탄두 투기 권고" : range,12,am.Warning ? Hostile : rangeColor);
        if(am.Mode==AntimatterMode.Arming)
            DrawRect(new Rect2(start+new Vector2(10,70),new Vector2(272*am.ArmingElapsed/def.ArmingSeconds,3)),Lead);
        Label(start+new Vector2(10,90),"3 어뢰 · 좌클릭 준비/발사 · 1/2 취소",11,Dim);
        Label(start+new Vector2(10,111),"Backspace 1초 유지 · AM 전량 투기",11,Game.JettisonProgress>0 ? Hostile : Dim);
        if(Game.JettisonProgress>0) DrawRect(new Rect2(start+new Vector2(10,118),new Vector2(272*Game.JettisonProgress,3)),Hostile);
    }
}
