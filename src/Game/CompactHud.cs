using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

public partial class ScaleTest
{
    public bool HudDetails {get;private set;}
    internal void ToggleHudDetails(){HudDetails=!HudDetails;GuardTargetChange();}
}

public partial class Hud
{
    internal static Rect2 CompactBar(Vector2 screen)
    {
        float width=Math.Min(screen.X-568,630+Mathf.Round((HudSettings.TextScale-1)*200));
        return new((screen.X-width)*.5f,screen.Y-90,width,62);
    }
    internal Rect2 UnifiedBarBounds {get;private set;}
    private void DrawCompactHud(ShipView view,Vector2 screen)
    {
        var ship=view.Body;var bar=CompactBar(screen);UnifiedBarBounds=bar;
        float cell=bar.Size.X/7;
        TacticalBounds=new(bar.Position+new Vector2(cell*3,0),new(cell*4,bar.Size.Y));
        for(int i=0;i<7;i++)
        {
            var r=new Rect2(bar.Position+new Vector2(i*cell,0),new(cell-3,bar.Size.Y));
            bool chosen=i<3&&(int)Game.SelectedWeapon==i;
            var color=chosen?Lead:Text;string key,name,value;
            float progress=0;
            if(i<3)
            {
                key=InputBindings.Text(i==0?InputSetup.SelectMainGun:i==1?InputSetup.SelectMissile:InputSetup.SelectAntimatter);
                name=i==0?ship.Railgun?.IsLaser==true?"레이저":"주포":i==1?"미사일":"어뢰";
                value=i==0?ship.Railgun?.IsLaser==true?"∞":ship.Railguns.Sum(g=>g.Rounds).ToString():i==1?ship.Definition.Missiles is null?"—":ship.Ordnance.Missiles.ToString():ship.Definition.Antimatter is null?"—":ship.Ordnance.Antimatter.Rounds.ToString();
                if(i==0&&ship.Railguns.Length>0)progress=ship.Railguns.Average(g=>g.IsLaser?g.Laser!.FocusFraction:1-g.ReloadRemaining/g.Definition.ReloadSeconds);
                if(i==2&&ship.Ordnance.Antimatter.Mode==AntimatterMode.Arming)progress=ship.Ordnance.Antimatter.ArmingElapsed/ship.Definition.Antimatter!.ArmingSeconds;
            }
            else
            {
                key=InputBindings.Text(InputSetup.TacticalSlots[i-3]);
                var slot=i-3<ship.Tactical.ActiveSlots.Length?ship.Tactical.ActiveSlots[i-3]:null;
                name=slot?.Definition.Effect switch{TacticalEffect.GeneratorOverload=>"과부하",TacticalEffect.HeatVent=>"열 배출",TacticalEffect.FocusSearch=>"탐색",TacticalEffect.ShortJump=>"도약",_=>"빈 슬롯"};
                var status=slot is null?TacticalStatus.Unavailable:ship.Tactical.Status(slot);
                color=status is TacticalStatus.Active or TacticalStatus.Preparing?Motion:status==TacticalStatus.Ready?Text:Dim;
                value=slot is null?"—":status==TacticalStatus.Cooldown?$"{Math.Ceiling(slot.CooldownUntil-ship.SimTime):0}초":status==TacticalStatus.Active?"가동":status==TacticalStatus.Preparing?"준비 중":status==TacticalStatus.Ready?"준비":TacticalState.Label(status);
            }
            DrawRect(r,PanelBack);
            Label(r.Position+new Vector2(6,18),key,11,color,width:25);
            Label(r.Position+new Vector2(6,38),name,12,color,width:r.Size.X-10);
            Label(r.Position+new Vector2(6,55),value,10,color,width:r.Size.X-10);
            if(chosen)DrawLine(r.Position,r.Position+new Vector2(r.Size.X,0),Lead,2);
            if(progress>0)HBar(new(r.Position+new Vector2(0,r.Size.Y-2),new(r.Size.X,2)),progress,color);
        }
        if(!Game.HudDetails)
        {
            var at=new Vector2(20,screen.Y-108);
            Label(at,ship.Callsign,14,ShipAffiliation.Owned,width:238);
            Label(at+new Vector2(0,24),$"실드 {ship.Damage.Shield/Math.Max(1,ship.Definition.Shield.Capacity):P0}",12,Friendly);
            DrawShieldBar(new(at+new Vector2(0,31),new(226,5)),ship);
            Label(at+new Vector2(0,56),$"구조 {ship.Damage.StructureFraction:P0}",12,Health(ship.Damage.StructureFraction));
            HBar(new(at+new Vector2(0,63),new(226,5)),ship.Damage.StructureFraction,Health(ship.Damage.StructureFraction));
            float y=bar.Position.Y-18;
            Label(new(bar.Position.X,y),$"{ship.Velocity.Length():0} m/s",13,Text);
            Label(new(bar.Position.X+125,y),$"추력 {Game.Throttle:P0}",12,Dim);
            Label(new(bar.Position.X+252,y),$"{ship.GLoad:0.0} G",12,ship.Control.Boost?Motion:Dim);
            Label(new(bar.End.X-160,y),ship.Control.Boost?"부스트":Game.Scheme==ControlScheme.Pilot?(Game.FireAssist?"사격보조":"수동 조준"):GunneryLabels.Doctrine(Game.Gunnery?.Doctrine??FireDoctrine.Free),12,Motion,width:160);
            // Power/heat remain visible even with the five-channel panel closed.
            var right=new Vector2(screen.X-250,screen.Y-108);
            Label(right,$"전력 {ship.Power.Supply:P0} · 열 {ship.Power.HeatFraction:P0}",12,ship.Power.Overheated?Hostile:ship.Power.Supply<.99f?Motion:Dim,width:230);
            HBar(new(right+new Vector2(0,9),new(225,4)),ship.Power.HeatFraction/ShipPower.HeatCeiling,ship.Power.Overheated?Hostile:Motion);
            var squad=Game.OwnedSquadron;
            if(squad is not null)Label(right+new Vector2(0,33),$"편대 {squad.Members.Count(s=>!s.Damage.Destroyed)}/{squad.Members.Count} · {squad.Activity}",11,ShipAffiliation.Owned,width:230);
            var dc=ship.DamageControl;
            if(dc.ActiveModule is {} repair)
            {
                Label(new(20,screen.Y-155),$"복구 · {ModuleNames.Display(repair.Definition)}",12,Motion,width:240);
                HBar(new(20,screen.Y-143,226,4),dc.Progress,Good);
            }
            if(ship.Power.Overheated)Label(right+new Vector2(0,-26),"과열 · 냉각 중",13,Hostile);
            else if(ship.Damage.Disabled)Label(new(20,screen.Y-183),"무력화 · 응급복구 필요",13,Hostile);
            DrawNearbyDrones(Game.Camera,view,screen);
        }
        Label(new(screen.X-250,screen.Y-36),$"{InputBindings.Text(InputSetup.HudDetails)} 상세 {(Game.HudDetails?"접기":"펼침")} · {InputBindings.Text(InputSetup.ToggleHelp)} 도움말",12,Dim,width:230);
        // Own-ship damage is a situational event. Enemy compartment logs are never read here.
        DrawnDamageNotices=0;float dy=screen.Y-(Game.HudDetails?332:212);
        foreach(var report in _damageReadout.Recent(ship,Game.World.Time).Take(2))
        {Label(new(20,dy),report.Message,12,report.Priority>=3?Hostile:Motion,width:245);dy-=HudFont(14)+4;DrawnDamageNotices++;}
    }
}
