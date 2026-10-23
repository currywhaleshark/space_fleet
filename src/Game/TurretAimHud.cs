using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

internal readonly record struct TurretReadout(string State,bool Destroyed,bool Aligned,FiringSolution? Solution);

public partial class Hud
{
    internal TurretReadout[] TurretReadouts {get;private set;}=Array.Empty<TurretReadout>();
    private ShipBody? _turretShip;
    private readonly List<(Vector2 Point,int Count,int First,Color Color)> _boreGroups=new();

    // Pure presentation: no Aim(), firing or random-number calls here.
    internal static TurretReadout ReadTurret(ScaleTest game,ShipBody ship,RailgunState gun)
    {
        var module=ship.Damage.Modules.FirstOrDefault(m=>m.Definition.Id==gun.Definition.ModuleId);
        if(module?.Destroyed==true)return new("파괴",true,false,null);
        FiringSolution? solution=null;
        string state="표적 없음";
        if(game.SelectedSmallContact is {} small)
        {
            bool solved=game.World.SmallTargetAim(ship,small,out var direction,out var point,out state,gun,playerAssist:true);
            if(solved)solution=new(true,"",direction,point,0,0,(point-gun.MuzzlePosition).Length());
        }
        else if(game.FireTarget is {} target)
        {
            solution=FireControl.Solve(ship,target.Body,game.World.Time,track:game.TrackOf(target),localAim:game.AimLocalPoint,weapon:gun);
            state=solution.Valid?"정렬":solution.Reason;
        }
        bool aligned=false;
        if(solution is {Valid:true} s)
        {
            var line=SimWorld.RailLineLocal(ship,gun,ship.Orientation.Inverse()*s.Direction);
            aligned=gun.Aligned(s.Direction);
            state=s.FlightTime>SimWorld.GunneryFlightLimit(ship)?"사거리 밖"
                :line==FireFailure.Arc?"포각 밖":line==FireFailure.HullBlocked?"선체 가림"
                :game.World.Obstacles.BlocksSegment(gun.MuzzlePosition,s.AimPoint)?"지형 가림"
                :game.World.FriendlyInLine(ship,gun.MuzzlePosition,gun.Direction,(float)s.Range,includeShields:gun.IsLaser)?"아군 사선"
                :!aligned?"선회 중":"정렬";
            if(state=="정렬"&&gun.Laser is {} laser&&game.SelectedSmallContact is null&&laser.Definition.Attenuation((float)s.Range)<.25f)
                state="거리 감쇠";
        }
        if(!gun.HasAmmunition)state="소진";
        else if(gun.Output<=.01f||ship.Power.WeaponEffect<=.01f)state="전력 불능";
        else if(ship.Power.Overheated||gun.Laser is {Active:false,Cooling:true})state="냉각";
        else if(ship.Tactical.WeaponsBlocked)state="무장 차단";
        else if(gun.ReloadRemaining>0)state="장전";
        else if(gun.Laser?.Active==true)state="조사";
        else if(module is {HealthFraction:<.75f})state+=" · 손상";
        return new(state,false,aligned,solution);
    }

    private void DrawTurretAim(Camera3D camera,ShipView controlled,Vector2 screen)
    {
        var ship=controlled.Body;var guns=ship.Railguns;
        if(_turretShip!=ship||TurretReadouts.Length!=guns.Length)
        {_turretShip=ship;TurretReadouts=new TurretReadout[guns.Length];}
        for(int i=0;i<guns.Length;i++)TurretReadouts[i]=ReadTurret(Game,ship,guns[i]);
        _boreGroups.Clear();
        float width=CompactBar(screen).Size.X,cell=width/Math.Max(1,Math.Min(6,guns.Length));
        float top=CompactBar(screen).Position.Y-48-Math.Max(1,(guns.Length+5)/6)*HudFont(19);
        for(int i=0;i<guns.Length;i++)
        {
            var gun=guns[i];var read=TurretReadouts[i];
            var color=read.Destroyed?Hostile:read.State is "정렬" or "조사"?Good:Motion;
            var at=new Vector2((screen.X-width)*.5f+cell*(i%6+.5f),top+(i/6)*HudFont(19));
            if(!Game.HudDetails)CenteredLabel(at,$"{i+1} {(read.Destroyed?"X":read.State)}",11,color);
            if(!Game.HudDetails&&read.State=="장전")HBar(new(at+new Vector2(-cell*.33f,11),new(cell*.66f,2)),1-gun.ReloadRemaining/gun.Definition.ReloadSeconds,Motion);
            if(read.Destroyed||read.Solution is not {Valid:true} s||Game.SelectedWeapon!=PlayerWeapon.MainGun)continue;
            var lead=(s.AimPoint-Game.RenderOrigin).ToVector3();
            if(!camera.IsPositionBehind(lead))DrawDiamond(camera.UnprojectPosition(lead),3,new Color(Lead,.55f));
            // Actual yaw/elevation, at this gun's own solved range. Never animate a fake convergence.
            var bore=(gun.MuzzlePosition+Vec3d.From(gun.Direction)*s.Range-Game.RenderOrigin).ToVector3();
            if(camera.IsPositionBehind(bore))continue;
            var p=camera.UnprojectPosition(bore);
            if(!new Rect2(20,70,screen.X-40,screen.Y-240).HasPoint(p))continue;
            int near=_boreGroups.FindIndex(g=>g.Point.DistanceSquaredTo(p)<100);
            if(near>=0){var g=_boreGroups[near];_boreGroups[near]=(g.Point,g.Count+1,g.First,g.Color);}
            else _boreGroups.Add((p,1,i,color));
            if(read.State.Contains("가림")||read.State=="포각 밖")
                DrawLine(p-new Vector2(5,5),p+new Vector2(5,5),color,1.5f);

        }
        foreach(var group in _boreGroups)
        {
            DrawArc(group.Point,6,0,Mathf.Tau,16,group.Color,1.5f);
            Label(group.Point+new Vector2(8,11),group.Count>1?$"×{group.Count}":(group.First+1).ToString(),11,group.Color);
        }
    }
}
