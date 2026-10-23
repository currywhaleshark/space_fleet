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

    internal Rect2 MountChipBounds {get;private set;}
    private enum MountLook{Ready,Moving,Limited,Blocked,Dead,Off}
    private static MountLook Look(TurretReadout r)=>r.Destroyed?MountLook.Dead:r.State switch
    {
        "정렬" or "조사"=>MountLook.Ready,
        "선회 중" or "장전"=>MountLook.Moving,
        "포각 밖" or "사거리 밖" or "거리 감쇠"=>MountLook.Limited,
        "선체 가림" or "지형 가림" or "아군 사선"=>MountLook.Blocked,
        _ when r.State.StartsWith("정렬")=>MountLook.Ready,
        _ when r.State.StartsWith("선회")=>MountLook.Moving,
        _=>MountLook.Off,
    };
    private static Color LookColor(MountLook look)=>look switch
    {
        MountLook.Ready=>HudStyle.Rgb(0xa0eddb),MountLook.Moving=>HudStyle.Rgb(0x90afc0),MountLook.Limited=>HudStyle.Rgb(0xf1b773),
        MountLook.Blocked=>HudStyle.Rgb(0xef887c),MountLook.Dead=>HudStyle.Rgb(0xd27f82),_=>HudStyle.Rgb(0x6b8391),
    };

    private void DrawTurretAim(Camera3D camera,ShipView controlled,Vector2 screen)
    {
        var ship=controlled.Body;var guns=ship.Railguns;MountChipBounds=new();
        if(_turretShip!=ship||TurretReadouts.Length!=guns.Length)
        {_turretShip=ship;TurretReadouts=new TurretReadout[guns.Length];}
        for(int i=0;i<guns.Length;i++)TurretReadouts[i]=ReadTurret(Game,ship,guns[i]);
        if(guns.Length==0||Game.SelectedWeapon!=PlayerWeapon.MainGun)return;
        _boreGroups.Clear();
        // The shared lead anchor: the displayed solution, or the screen centre without one.
        Vector2 anchor=screen*.5f;bool hasLead=false;
        if(Game.DisplayLeadSolution is {Valid:true} lead&&!camera.IsPositionBehind((lead.AimPoint-Game.RenderOrigin).ToVector3()))
        {anchor=camera.UnprojectPosition((lead.AimPoint-Game.RenderOrigin).ToVector3());hasLead=true;}
        if(hasLead)
        {
            DrawArc(anchor,7,0,Mathf.Tau,24,new Color(Lead,.85f),1.25f,true);
            for(int d=-10;d<=10;d+=4){DrawLine(anchor+new Vector2(d,0),anchor+new Vector2(d+1,0),new Color(HudStyle.Rgb(0x93b9c6),.58f),1.3f);DrawLine(anchor+new Vector2(0,d),anchor+new Vector2(0,d+1),new Color(HudStyle.Rgb(0x93b9c6),.58f),1.3f);}
        }
        for(int i=0;i<guns.Length;i++)
        {
            var gun=guns[i];var read=TurretReadouts[i];
            if(read.Destroyed||read.Solution is not {Valid:true} s)continue;
            // Actual yaw/elevation, at this gun's own solved range. Never animate a fake convergence.
            var bore=(gun.MuzzlePosition+Vec3d.From(gun.Direction)*s.Range-Game.RenderOrigin).ToVector3();
            if(camera.IsPositionBehind(bore))continue;
            var p=camera.UnprojectPosition(bore);
            if(!new Rect2(20,70,screen.X-40,screen.Y-240).HasPoint(p))continue;
            int near=_boreGroups.FindIndex(g=>g.Point.DistanceSquaredTo(p)<100);
            var color=LookColor(Look(read));
            if(near>=0){var g=_boreGroups[near];_boreGroups[near]=(g.Point,g.Count+1,g.First,g.Color);}
            else _boreGroups.Add((p,1,i,color));
        }
        foreach(var group in _boreGroups)
        {
            var p=group.Point;var look=Look(TurretReadouts[group.First]);var color=group.Color;
            if(hasLead&&p.DistanceTo(anchor)>24)
                for(float t=0;t<1;t+=9/Math.Max(9,p.DistanceTo(anchor)))DrawLine(p.Lerp(anchor,t),p.Lerp(anchor,Math.Min(1,t+2/p.DistanceTo(anchor))),new Color(color,.22f),1);
            const float r=8;
            float width=look is MountLook.Limited?2:look is MountLook.Blocked?1.8f:1.4f;
            DrawPolyline(new[]{p+new Vector2(-r-4,-4),p+new Vector2(-r-4,-r-4),p+new Vector2(-4,-r-4)},new Color(color,.85f),width,true);
            DrawPolyline(new[]{p+new Vector2(4,r+4),p+new Vector2(r+4,r+4),p+new Vector2(r+4,4)},new Color(color,.85f),width,true);
            if(look==MountLook.Limited){DrawLine(p+new Vector2(-4,-7),p+new Vector2(-4,7),color,2);DrawLine(p+new Vector2(4,-7),p+new Vector2(4,7),color,2);}
            else if(look==MountLook.Blocked){DrawLine(p-new Vector2(6,6),p+new Vector2(6,6),color,1.8f);DrawLine(p+new Vector2(-6,6),p+new Vector2(6,-6),color,1.8f);}
            else if(look==MountLook.Ready)DrawCircle(p,2.5f,new Color(color,.85f));
            else DrawArc(p,3.5f,0,Mathf.Tau,12,color,1.3f,true);
            if(group.Count>1||Game.HudDetails)Mono(p+new Vector2(13,-10),group.Count>1?$"×{group.Count}":(group.First+1).ToString(),10,color);
        }
        // Numbered mount chips under the lead: fixed identity, × for destroyed, colour for the reason.
        float chipW=24,chipH=19,gap=4;
        float total=guns.Length*chipW+(guns.Length-1)*gap+12;
        var box=new Rect2(anchor.X-total*.5f,anchor.Y+30,total,chipH+4);
        box.Position=new(Mathf.Clamp(box.Position.X,8,screen.X-8-total),Mathf.Clamp(box.Position.Y,80,Math.Max(80,CompactBar(screen).Position.Y-box.Size.Y-60)));
        MountChipBounds=box;
        DrawRect(box,new Color(3/255f,12/255f,21/255f,.76f));DrawRect(box,HudStyle.Rgb(0x6698ab,.24f),false,1);
        for(int i=0;i<guns.Length;i++)
        {
            var read=TurretReadouts[i];var look=Look(read);var color=LookColor(look);
            var chip=new Rect2(box.Position.X+6+i*(chipW+gap),box.Position.Y+2,chipW,chipH);
            DrawRect(chip,look==MountLook.Dead?new Color(84/255f,25/255f,34/255f,.28f):new Color(42/255f,75/255f,91/255f,.12f));
            if(read.State=="장전")DrawRect(new Rect2(chip.Position.X,chip.End.Y-2,chipW*Mathf.Clamp(1-guns[i].ReloadRemaining/guns[i].Definition.ReloadSeconds,0,1),2),new Color(HudStyle.Mint,.6f));
            DrawRect(chip,look switch{MountLook.Ready=>HudStyle.Rgb(0x77dbc5,.55f),MountLook.Limited=>HudStyle.Rgb(0xeaae6f,.5f),MountLook.Blocked=>HudStyle.Rgb(0xed7e70,.6f),
                MountLook.Dead=>HudStyle.Rgb(0xd87075,.48f),_=>HudStyle.Rgb(0x65a0b5,.27f)},false,1);
            Mono(new(chip.Position.X,chip.Position.Y+13),look==MountLook.Dead?"×":(i+1).ToString(),10,color,HorizontalAlignment.Center,chipW);
            if(look==MountLook.Off)DrawLine(new(chip.Position.X+5,chip.Position.Y+3),new(chip.End.X-5,chip.Position.Y+3),new Color(HudStyle.Amber,.6f),1);
        }
    }
}
