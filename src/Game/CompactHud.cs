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

/// <summary>
/// HUD v4 always-on layer (prototype study 04): the 1–7 bar, the flight cluster, own-ship, squad,
/// support and power panes. Expanded (I) keeps these panes and adds labels/drop panels.
/// </summary>
public partial class Hud
{
    private const float SlotW=66,SlotH=56,SlotGap=4,BarPad=5,SplitW=9,Glyph=26;
    internal static Rect2 CompactBar(Vector2 screen)
    {
        float width=SlotW*7+SlotGap*6+SplitW+BarPad*2;
        return new((screen.X-width)*.5f,screen.Y-100-(SlotH+BarPad*2),width,SlotH+BarPad*2);
    }
    internal Rect2 UnifiedBarBounds {get;private set;}
    internal Rect2 FlightBounds {get;private set;}
    internal Rect2 OwnPaneBounds {get;private set;}
    internal Rect2 SquadPaneBounds {get;private set;}
    internal Rect2 PowerPaneBounds {get;private set;}
    internal Rect2 SupportPaneBounds {get;private set;}
    private static readonly Color SlotIdle=HudStyle.Rgb(0xa5c9dc),SlotNum=HudStyle.Rgb(0x8ba7b6),SlotCaption=HudStyle.Rgb(0x6d92a4),
        Cooling=HudStyle.Rgb(0x547f8d),Lit=HudStyle.Rgb(0xf0d19a),Chip=HudStyle.Rgb(0x36515f);

    private void DrawCompactHud(ShipView view,Vector2 screen)
    {
        var ship=view.Body;
        DrawHotbar(ship,screen);
        DrawFlight(ship,screen);
        DrawOwnPane(ship,screen);
        DrawSquadPane(view,screen);
        DrawPowerPane(ship,screen);
        DrawSupportPane(ship,screen);
        DrawNearbyDrones(Game.Camera,view,screen);
        Label(new(OwnPaneBounds.Position.X,screen.Y-6),$"{InputBindings.Text(InputSetup.ToggleHelp)} 도움말 · {InputBindings.Text(InputSetup.HudDetails)} 상세 {(Game.HudDetails?"접기":"펼침")}",10,new Color(Dim,.6f));
    }

    // ── 1–3 무장 · 4–7 전술 ─────────────────────────────────────

    private void DrawHotbar(ShipBody ship,Vector2 screen)
    {
        var bar=CompactBar(screen);UnifiedBarBounds=bar;
        const float c=8;
        var outline=new[]{bar.Position+new Vector2(c,0),new(bar.End.X-c,bar.Position.Y),new(bar.End.X,bar.Position.Y+c),new(bar.End.X,bar.End.Y-c),
            new(bar.End.X-c,bar.End.Y),new(bar.Position.X+c,bar.End.Y),new(bar.Position.X,bar.End.Y-c),new(bar.Position.X,bar.Position.Y+c)};
        DrawColoredPolygon(outline,new Color(2/255f,11/255f,20/255f,.87f));
        DrawPolyline(outline.Append(outline[0]).ToArray(),HudStyle.Rgb(0x5b93a9,.37f),1);
        float x=bar.Position.X+BarPad,y=bar.Position.Y+(bar.Size.Y-SlotH)*.5f;
        for(int i=0;i<7;i++)
        {
            if(i==3)
            {
                DrawLine(new(x+SplitW*.5f-SlotGap*.5f+2,y+SlotH*.5f-13),new(x+SplitW*.5f-SlotGap*.5f+2,y+SlotH*.5f+14),HudStyle.Rgb(0x8ab9c9,.37f),1);
                x+=SplitW;
            }
            var r=new Rect2(x,y,SlotW,SlotH);
            if(i<3)DrawWeaponSlot(ship,i,r);else DrawTacticalSlot(ship,i-3,r);
            if(i==3)TacticalBounds=new(r.Position,new(bar.End.X-BarPad-r.Position.X,SlotH));
            x+=SlotW+SlotGap;
        }
    }
    private void SlotFrame(Rect2 r,Color border,Color from,Color to)
    {
        DrawPolygon(new[]{r.Position,new(r.End.X,r.Position.Y),r.End,new(r.Position.X,r.End.Y)},new[]{from,from.Lerp(to,.5f),to,from.Lerp(to,.5f)});
        DrawRect(r,border,false,1);
    }
    private void SlotText(Rect2 r,string key,string caption,Color captionColor)
    {
        Mono(r.Position+new Vector2(5,3+HudFont(11)*.85f),key,11,SlotNum,width:SlotW-10);
        Mono(new(r.Position.X+2,r.End.Y-5),caption,9,captionColor,HorizontalAlignment.Center,SlotW-4);
    }
    private void DrawWeaponSlot(ShipBody ship,int i,Rect2 r)
    {
        bool chosen=(int)Game.SelectedWeapon==i;
        bool fitted=i==0||(i==1?ship.Definition.Missiles is not null:ship.Definition.Antimatter is not null);
        if(chosen)
        {
            SlotFrame(r,HudStyle.Cyan,HudStyle.Rgb(0x1e4b60,.79f),HudStyle.Rgb(0x092335,.78f));
            DrawRect(r.Grow(1),new Color(HudStyle.Cyan,.25f),false,1);
        }
        else SlotFrame(r,HudStyle.Rgb(0x619bb2,.27f),HudStyle.Rgb(0x09202f,.86f),HudStyle.Rgb(0x050d16,.67f));
        bool laser=ship.Railgun?.IsLaser==true;
        string glyph=i==0?laser?"laser":"gun":i==1?"missile":"torpedo";
        string key=InputBindings.Text(i==0?InputSetup.SelectMainGun:i==1?InputSetup.SelectMissile:InputSetup.SelectAntimatter);
        string caption=!fitted?"—":i==0?laser?"∞":ship.Railguns.Sum(g=>g.Rounds).ToString():i==1?ship.Ordnance.Missiles.ToString():ship.Ordnance.Antimatter.Rounds.ToString();
        Color tint=!fitted?new Color(SlotIdle,.3f):chosen?HudStyle.Rgb(0xeafaff):SlotIdle;
        Icon(glyph,r.GetCenter()-new Vector2(0,2),Glyph,tint);
        float progress=0;
        if(i==0&&ship.Railguns.Length>0)progress=ship.Railguns.Average(g=>g.IsLaser?g.Laser!.FocusFraction:1-g.ReloadRemaining/g.Definition.ReloadSeconds);
        if(i==2&&ship.Ordnance.Antimatter.Mode==AntimatterMode.Arming)progress=ship.Ordnance.Antimatter.ArmingElapsed/ship.Definition.Antimatter!.ArmingSeconds;
        if(fitted&&progress>0&&progress<.999f)DrawRect(new Rect2(r.Position.X+3,r.End.Y-2,(r.Size.X-6)*progress,1.5f),new Color(HudStyle.Mint,.7f));
        if(Game.HudDetails)caption=(i==0?laser?"레이저":"주포":i==1?"미사일":"어뢰")+" "+caption;
        SlotText(r,key,caption,fitted?SlotCaption:new Color(SlotCaption,.5f));
    }
    private static string TacticalGlyph(TacticalEffect effect)=>effect switch
    {TacticalEffect.GeneratorOverload=>"bolt",TacticalEffect.HeatVent=>"vent",TacticalEffect.FocusSearch=>"search",TacticalEffect.ShortJump=>"jump",_=>"empty"};
    internal static string TacticalName(TacticalEffect? effect)=>effect switch
    {TacticalEffect.GeneratorOverload=>"과부하",TacticalEffect.HeatVent=>"열 배출",TacticalEffect.FocusSearch=>"탐색",TacticalEffect.ShortJump=>"도약",_=>"빈 슬롯"};
    private void DrawTacticalSlot(ShipBody ship,int index,Rect2 r)
    {
        var slot=index<ship.Tactical.ActiveSlots.Length?ship.Tactical.ActiveSlots[index]:null;
        var status=slot is null?TacticalStatus.Unavailable:ship.Tactical.Status(slot);
        bool on=status is TacticalStatus.Active or TacticalStatus.Preparing,cooling=status==TacticalStatus.Cooldown;
        SlotFrame(r,on?HudStyle.Rgb(0xe6b66c,.56f):HudStyle.Rgb(0x619bb2,.27f),HudStyle.Rgb(0x09202f,.86f),HudStyle.Rgb(0x050d16,.67f));
        string caption;Color captionColor=SlotCaption,tint;
        if(slot is null){caption="—";tint=new Color(SlotIdle,.25f);}
        else if(on){caption=$"{Math.Ceiling(Math.Max(0,slot.ActiveUntil-ship.SimTime)):0}s";tint=Lit;captionColor=Lit;}
        else if(cooling){caption=$"{Math.Ceiling(slot.CooldownUntil-ship.SimTime):0}s";tint=Cooling;captionColor=HudStyle.Amber;}
        else if(status==TacticalStatus.Ready){caption="READY";tint=HudStyle.Mint;}
        else{caption=TacticalState.Label(status);tint=new Color(HudStyle.Red,.6f);captionColor=new Color(HudStyle.Red,.8f);}
        Icon(slot is null?"empty":TacticalGlyph(slot.Definition.Effect),r.GetCenter()-new Vector2(0,2),Glyph,tint);
        if(cooling)
        {
            DrawRect(r,new Color(3/255f,10/255f,18/255f,.38f));
            float left=(float)((slot!.CooldownUntil-ship.SimTime)/Math.Max(.01,slot.Definition.Cooldown));
            DrawRect(new Rect2(r.Position.X+3,r.End.Y-2,(r.Size.X-6)*Mathf.Clamp(left,0,1),1.5f),new Color(HudStyle.Amber,.5f));
        }
        else if(on)DrawRect(new Rect2(r.Position.X+3,r.End.Y-2,r.Size.X-6,1.5f),new Color(HudStyle.Amber,.7f));
        if(Game.HudDetails&&slot is not null)caption=TacticalName(slot.Definition.Effect)+" "+caption;
        SlotText(r,InputBindings.Text(InputSetup.TacticalSlots[index]),caption,captionColor);
    }

    // ── 속도 · 이동 방향 · G · 상태 칩 ───────────────────────────

    private void DrawFlight(ShipBody ship,Vector2 screen)
    {
        float cy=screen.Y-19-37.5f;
        float speed=ship.Velocity.Length();
        string speedText=$"{speed:0}";
        float speedW=Math.Max(MonoWidth(speedText,25),MonoWidth("m/s",11))+10+38;
        var chips=FlightChips(ship);
        float chipsW=chips.Sum(c=>Math.Max(32,MonoWidth(c.Text,11)+14))+(chips.Length-1)*6;
        float gW=46+9+MonoWidth($"{ship.GLoad:0.0}",22)+MonoWidth(" G",10);
        float total=speedW+20+62+20+gW+20+chipsW;
        float x=screen.X*.5f-total*.5f;
        FlightBounds=new(x-8,cy-40,total+16,80);

        // Speed number and a three-line tape: reference, actual speed, throttle command.
        Mono(new(x,cy+4),speedText,25,HudStyle.Rgb(0xdbecf5));
        Mono(new(x,cy+20),"m/s",11,HudStyle.Rgb(0x7294a4));
        float tx=x+speedW-38;float max=ship.Class.MaxSpeed*ship.Class.BoostMultiplier;
        DrawRect(new Rect2(tx,cy-6,38,3),HudStyle.Rgb(0x385869));
        DrawRect(new Rect2(tx,cy,38*Mathf.Clamp(speed/max,0,1),3),speed>ship.Class.MaxSpeed+1?HudStyle.Amber:HudStyle.Cyan);
        DrawRect(new Rect2(tx,cy+6,38*Mathf.Clamp(Math.Abs(Game.Throttle),0,1),3),Game.Throttle<0?HudStyle.Red:ship.Control.Boost?HudStyle.Amber:HudStyle.Cyan);
        x+=speedW+20;

        // Movement circle: nose up, ◆ = actual travel direction (farther out = more drift).
        var c=new Vector2(x+31,cy);
        DrawCircle(c,31,new Color(2/255f,17/255f,28/255f,.5f));
        DrawArc(c,22,0,Mathf.Tau,40,new Color(5/255f,17/255f,27/255f,.6f),17);
        DrawArc(c,31,0,Mathf.Tau,48,HudStyle.Rgb(0x355a68),1,true);
        DrawLine(c-new Vector2(0,25),c+new Vector2(0,25),HudStyle.Rgb(0x294857),1);DrawLine(c-new Vector2(25,0),c+new Vector2(25,0),HudStyle.Rgb(0x294857),1);
        DrawColoredPolygon(new[]{c+new Vector2(0,-12),c+new Vector2(7,3),c+new Vector2(-7,3)},HudStyle.Rgb(0xa1dbe8));
        float drift=speed<1?0:ship.DriftDegrees;
        if(speed>=1)
        {
            Vector3 local=ship.Orientation.Inverse()*ship.Velocity;
            var lateral=new Vector2(local.X,-local.Y);
            var dir=lateral.LengthSquared()>1e-6f?lateral.Normalized():Vector2.Zero;
            var p=c+dir*29*(drift/180f);
            DrawColoredPolygon(new[]{p+new Vector2(0,-5),p+new Vector2(5,0),p+new Vector2(0,5),p+new Vector2(-5,0)},drift>30?HudStyle.Red:HudStyle.Amber);
            DrawPolyline(new[]{p+new Vector2(0,-5),p+new Vector2(5,0),p+new Vector2(0,5),p+new Vector2(-5,0),p+new Vector2(0,-5)},HudStyle.Rgb(0xffdfa2),1);
            float settle=ship.LateralSettleSeconds;
            if(ship.Control.FlightAssist&&drift>1&&float.IsFinite(settle))
                DrawArc(c,34,-Mathf.Pi/2,-Mathf.Pi/2+Mathf.Tau*Mathf.Clamp(settle/SettleScaleSeconds,0,1),32,new Color(HudStyle.Amber,.7f),2);
        }
        Mono(new(c.X-40,c.Y+31+12),$"편류 {drift:0}°",9,HudStyle.Rgb(0x7493a0),HorizontalAlignment.Center,80);
        x+=62+20;

        // G gauge ring.
        var g=new Vector2(x+23,cy);float fraction=Mathf.Clamp(ship.GLoad/ship.Class.MaxAccelG,0,1);
        DrawArc(g,21,0,Mathf.Tau,40,HudStyle.Rgb(0x294d5d),4,true);
        if(fraction>.005f)DrawArc(g,21,-Mathf.Pi/2+.7f,-Mathf.Pi/2+.7f+Mathf.Tau*fraction,Math.Max(4,(int)(40*fraction)),fraction>.95f?HudStyle.Amber:HudStyle.Cyan,4,true);
        string gText=$"{ship.GLoad:0.0}";
        Mono(new(x+55,cy+8),gText,22,HudStyle.Rgb(0xd2e9ef));
        Mono(new(x+55+MonoWidth(gText,22),cy+8)," G",10,HudStyle.Dim);
        x+=gW+20;

        foreach(var chip in chips)
        {
            float w=Math.Max(32,MonoWidth(chip.Text,11)+14);
            var r=new Rect2(x,cy-13.5f,w,27);
            Color border=chip.Alert?HudStyle.Rgb(0x895d34):chip.On?HudStyle.Rgb(0x427e78):Chip;
            Color text=chip.Alert?chip.Color:chip.On?HudStyle.Rgb(0xa4d3c7):HudStyle.Rgb(0x87a7b8,.75f);
            DrawRect(r,new Color(2/255f,13/255f,24/255f,.52f));DrawRect(r,border,false,1);
            Mono(new(r.Position.X,cy+HudFont(11)*.35f),chip.Text,11,text,HorizontalAlignment.Center,w);
            x+=w+6;
        }
    }
    private (string Text,bool On,bool Alert,Color Color)[] FlightChips(ShipBody ship)
    {
        bool assist=ship.Control.FlightAssist;
        var chips=new System.Collections.Generic.List<(string,bool,bool,Color)>
        {
            ("◈",assist,false,HudStyle.Mint),
            (Game.AssistStyle==AssistStyle.Space?"○":"△",assist,false,HudStyle.Mint),
            ("⇧",false,ship.Control.Boost,HudStyle.Amber),
            ("⌁",ship.Power.EcmActive,false,HudStyle.Mint),
        };
        if(Game.Scheme==ControlScheme.Helm)chips.Add((GunneryLabels.Doctrine(Game.Gunnery?.Doctrine??FireDoctrine.Free),true,false,HudStyle.Mint));
        else chips.Add((ship.TurnBraking?"선회 감속":Game.FireAssist?"사격보조":"수동 조준",Game.FireAssist||ship.TurnBraking,ship.TurnBraking,HudStyle.Amber));
        if(Game.TimeScale>1)chips.Add(($"×{Game.TimeScale}",false,true,HudStyle.Amber));
        if(IncomingMissiles>0)chips.Add(($"미사일 {IncomingMissiles}",false,(int)(Game.World.Time*4)%2==0,HudStyle.Red));
        return chips.ToArray();
    }

    // ── 자함 · 편대 ─────────────────────────────────────────────

    private static readonly (string Icon,string Name,Func<ShipDamage,float> Value)[] OwnSystems=
    {
        ("engine","추진",d=>d.PropulsionFraction),("weapon","무장",d=>d.WeaponsFraction),("sensor","센서",d=>d.SensorFraction),
        ("vent","냉각",d=>d.CoolingFraction),("power","발전",d=>d.GenerationFraction),
    };
    private void DrawOwnPane(ShipBody ship,Vector2 screen)
    {
        float h=151+(Game.HudDetails?HudFont(10)+2:0);
        var r=new Rect2(24,screen.Y-20-h,250,h);OwnPaneBounds=r;
        bool collided=ship.LastCollision is CollisionImpact impact&&Game.World.Time-impact.Time<2.0;
        Pane(r,1,collided?Hostile:null);
        float x=r.Position.X+14,y=r.Position.Y+12;
        Mono(new(x,y+15),ship.Callsign,17,Game.ShipMarkerColor(ship),width:150);
        var dc=ship.DamageControl;
        string kit=$"{InputBindings.Text(InputSetup.DamageControlMenu)} · {dc.Kits}";
        float kw=MonoWidth(kit,10)+10;var key=new Rect2(r.End.X-14-kw,y+2,kw,16);
        DrawRect(key,HudStyle.Rgb(0x48697b),false,1);
        Mono(new(key.Position.X,y+14),kit,10,dc.ActiveModule is not null?HudStyle.Amber:HudStyle.Rgb(0x8ca7b3),HorizontalAlignment.Center,kw);
        y+=17+15;
        float railX=x+16+9,railW=r.Size.X-28-16-9-36-9;
        float design=Mathf.Max(1,ship.Definition.Shield.Capacity),shield=ship.Damage.Shield/design;
        float flash=Mathf.Clamp(1-(float)(Game.World.Time-ship.Damage.LastShieldHitTime)/.4f,0,1);
        Icon("shield",new(x+7.5f,y+7.5f),15,HudStyle.Dim);
        var shieldRail=new Rect2(railX,y+5,railW,5);
        Rail(shieldRail,shield,HudStyle.Cyan.Lerp(Colors.White,flash*.7f));
        float lost=1-ship.Damage.ShieldCapacity/design;
        if(lost>.005f)DrawRect(new Rect2(shieldRail.End.X-railW*lost,shieldRail.Position.Y,railW*lost,5),new Color(Hostile,.45f));
        Mono(new(r.End.X-14-36,y+11),$"{shield*100:0}",11,HudStyle.Rgb(0xa5bfcb),HorizontalAlignment.Right,36);
        y+=24;
        float structure=ship.Damage.StructureFraction;
        Icon("hull",new(x+7.5f,y+7.5f),15,HudStyle.Dim);
        Rail(new Rect2(railX,y+5,railW,5),structure,structure<=.25f?HudStyle.Red:structure<=.5f?HudStyle.Amber:HudStyle.Mint);
        Mono(new(r.End.X-14-36,y+11),$"{structure*100:0}",11,HudStyle.Rgb(0xa5bfcb),HorizontalAlignment.Right,36);
        y+=24+4;
        DrawLine(new(x,y),new(r.End.X-14,y),HudStyle.Rgb(0x749bb1,.17f),1);
        y+=10;
        float cell=(r.Size.X-28)/OwnSystems.Length;
        for(int i=0;i<OwnSystems.Length;i++)
        {
            float v=OwnSystems[i].Value(ship.Damage);var cx=x+cell*(i+.5f);
            Color tint=v<=.01f?HudStyle.Red:v<.8f?HudStyle.Amber:new Color(HudStyle.Ink,.8f);
            Icon(OwnSystems[i].Icon,new(cx,y+8),16,tint);
            Rail(new Rect2(cx-16,y+21,32,3),v,v<=.01f?HudStyle.Red:v<.8f?HudStyle.Amber:HudStyle.Mint);
            if(Game.HudDetails)Mono(new(cx-20,y+26+HudFont(10)),OwnSystems[i].Name,10,HudStyle.Rgb(0xa5bfca),HorizontalAlignment.Center,40);
        }
        if(collided&&ship.LastCollision is CollisionImpact hit)Label(r.Position+new Vector2(0,-8),$"충돌 {hit.OtherCallsign} · Δv {hit.DeltaSpeed:0}",13,Hostile);
    }
    private void DrawSquadPane(ShipView view,Vector2 screen)
    {
        SquadPaneBounds=new();
        var squad=Game.OwnedSquadron??Game.World.Squadrons.FirstOrDefault(q=>q.Members.Contains(view.Body));
        if(squad is null)return;
        float nameW=MonoWidth(squad.Name,13),dots=squad.Members.Count*13-5;
        string count=$"{squad.Members.Count(s=>!s.Damage.Destroyed)}/{squad.Members.Count}";
        bool posture=Game.BattleMode&&Game.World.Rules is not null;
        float w=Math.Max(214,11+nameW+12+dots+12+MonoWidth(count,11)+(posture?34:0)+11);
        var r=new Rect2(24,OwnPaneBounds.Position.Y-13-36,w,36);SquadPaneBounds=r;
        Pane(r);
        float x=r.Position.X+11,cy=r.Position.Y+18;
        Mono(new(x,cy+HudFont(13)*.36f),squad.Name,13,HudStyle.Rgb(0xc9e5ee));x+=nameW+12;
        foreach(var member in squad.Members)
        {
            var c=new Vector2(x+4,cy);
            if(member.Damage.Destroyed)DrawArc(c,3.5f,0,Mathf.Tau,12,HudStyle.Rgb(0x688595),1,true);
            else
            {
                Color color=member.Damage.Disabled?HudStyle.Red:member.Damage.Modules.Any(m=>m.Destroyed)?HudStyle.Amber:HudStyle.Cyan;
                DrawCircle(c,4,color);
                if(member==view.Body)DrawArc(c,6.5f,0,Mathf.Tau,16,Text,1,true);
            }
            x+=13;
        }
        x+=7;
        Mono(new(x,cy+HudFont(11)*.36f),count,11,HudStyle.Dim);
        if(posture)DrawPostureGlyph(new(r.End.X-22,cy),Game.World.Fleet(view.Body.Faction).Posture);
        if(Game.HudDetails)
        {
            string activity=squad.Role==SquadronRole.InterceptorWing?squad.Activity:Game.SquadOrder switch
            {SquadCommand.Focus=>$"집중 {Game.SquadTarget?.Callsign}",_=>SquadCommands.Label(Game.SquadOrder)};
            Mono(new(r.Position.X+11,r.Position.Y-8),activity,11,HudStyle.Rgb(0xa9c1cb),width:r.Size.X-11);
        }
    }
    /// <summary>Fleet posture as one small glyph inside the squad pane (missile · gunline · close · withdraw).</summary>
    private void DrawPostureGlyph(Vector2 p,FleetPosture posture)
    {
        var c=HudStyle.Mint;
        switch(posture)
        {
            case FleetPosture.Missile:DrawLine(p+new Vector2(-7,5),p+new Vector2(5,-5),c,1.5f);DrawLine(p+new Vector2(0,-5),p+new Vector2(5,-5),c,1.5f);DrawLine(p+new Vector2(5,0),p+new Vector2(5,-5),c,1.5f);break;
            case FleetPosture.Gunline:for(int n=-1;n<=1;n++)DrawLine(p+new Vector2(-8,n*4),p+new Vector2(8,n*4),c,1.5f);break;
            case FleetPosture.Close:DrawDiamond(p,7,c);DrawLine(p+new Vector2(-4,-4),p+new Vector2(4,4),c,1.5f);break;
            default:DrawLine(p+new Vector2(8,0),p+new Vector2(-8,0),c,1.5f);DrawLine(p+new Vector2(-3,-5),p+new Vector2(-8,0),c,1.5f);DrawLine(p+new Vector2(-3,5),p+new Vector2(-8,0),c,1.5f);break;
        }
    }

    // ── 전력 · 지원 ─────────────────────────────────────────────

    private static readonly (PowerChannel Channel,string Icon,string Name)[] PowerColumns=
    {(PowerChannel.Engines,"engine","추진"),(PowerChannel.Shields,"shield","실드"),(PowerChannel.Weapons,"weapon","무장"),(PowerChannel.Sensors,"sensor","센서"),(PowerChannel.Ecm,"ecm","ECM")};
    private void DrawPowerPane(ShipBody ship,Vector2 screen)
    {
        var power=ship.Power;
        float h=161+(Game.HudDetails?HudFont(9)+2:0);
        var r=new Rect2(screen.X-23-257,screen.Y-20-h,257,h);PowerPaneBounds=r;
        bool blink=power.Overheated&&(int)(Game.World.Time*4)%2==0;
        Pane(r,1,blink?Hostile:null);
        float x=r.Position.X+14,y=r.Position.Y+12;
        Mono(new(x,y+11),InputBindings.Text(InputSetup.PowerMenu),11,HudStyle.Rgb(0xb3c9d4));
        bool balanced=PowerColumns.All(c=>power.Pips(c.Channel)==(c.Channel==PowerChannel.Ecm?0:ShipPower.BalancedPips));
        string mode=power.Overheated?"과열 · 냉각 중":power.Supply<.99f?$"전력 {power.Supply*100:0}%":balanced?"AUTO":"MANUAL";
        Mono(new(x,y+11),mode,11,power.Overheated?Hostile:power.Supply<.99f?Motion:HudStyle.Dim,HorizontalAlignment.Right,r.Size.X-28);
        y+=13+13;
        float cell=(r.Size.X-28)/PowerColumns.Length;
        for(int i=0;i<PowerColumns.Length;i++)
        {
            var (channel,icon,name)=PowerColumns[i];var cx=x+cell*(i+.5f);
            int pips=power.Pips(channel);
            Icon(icon,new(cx,y+8),16,HudStyle.Rgb(0x9cb6c4));
            Color on=channel==PowerChannel.Weapons?HudStyle.Amber:HudStyle.Cyan;
            if(power.Supply<.99f)on=on.Lerp(HudStyle.Amber,.5f);
            if(power.Overheated)on=new Color(on,.5f);
            for(int p=0;p<ShipPower.MaxPips;p++)DrawRect(new Rect2(cx-12+p*6,y+21,4,28),p<pips?on:HudStyle.Rgb(0x294653));
            if(Game.HudDetails)
            {
                float draw=power.DrawMw(channel)/Mathf.Max(.001f,power.MaxDrawMw(channel));
                Mono(new(cx-24,y+49+HudFont(9)+2),$"{name} {draw*100:0}%",9,HudStyle.Rgb(0x7897a5),HorizontalAlignment.Center,48);
            }
        }
        y+=64+(Game.HudDetails?HudFont(9)+2:0)+10;
        DrawLine(new(x,y),new(r.End.X-14,y),HudStyle.Rgb(0x749bb1,.17f),1);
        y+=10;
        float heat=power.HeatFraction;
        Color heatColor=power.Overheated?HudStyle.Red:heat>=ShipPower.RecoverHeatFraction?HudStyle.Amber:HudStyle.Amber.Lerp(HudStyle.Dim,.3f);
        Icon("heat",new(x+7.5f,y+7.5f),15,HudStyle.Amber);
        var line=new Rect2(x+16+10,y+6,r.Size.X-28-16-10-44-8,4);
        DrawRect(line,HudStyle.Rgb(0x37424c));DrawRect(new Rect2(line.Position,new(line.Size.X*Mathf.Clamp(heat/ShipPower.HeatCeiling,0,1),4)),heatColor);
        DrawLine(new(line.Position.X+line.Size.X/ShipPower.HeatCeiling,line.Position.Y-2),new(line.Position.X+line.Size.X/ShipPower.HeatCeiling,line.End.Y+2),new Color(HudStyle.Red,.6f),1);
        Mono(new(r.End.X-14-44,y+11),$"{heat*100:0}%",11,power.Overheated?Hostile:Text,HorizontalAlignment.Right,44);
    }
    private void DrawSupportPane(ShipBody ship,Vector2 screen)
    {
        SupportPaneBounds=new();
        string icon,title,detail,count;Color countColor=HudStyle.Rgb(0xa5bdc8);
        if(ship.Definition.DefenseDrones is {} def)
        {
            var drones=ship.Ordnance.Drones;
            icon="drone";title=InputBindings.Text(InputSetup.DroneMenu);detail=DefenseDroneState.Label(drones.Sector);
            count=$"{drones.SurvivingCount}/{def.Count}";
            if(!drones.Active||drones.ArmedCount==0)countColor=Hostile;else if(drones.RepositioningCount>0)countColor=Motion;
        }
        else if(ship.Class.Kind==HullKind.Interceptor&&ship.Ordnance.PointDefense.Length>0)
        {
            var mounts=ship.Ordnance.PointDefense;
            icon="weapon";title="PD";detail="보조 포탑";
            count=$"{mounts.Count(m=>m.TargetKind!=PointDefenseTarget.None)}/{mounts.Length}";
        }
        else return;
        string? extra=null;
        if(Game.HudDetails)
        {
            if(ship.Definition.DefenseDrones is not null){var d=ship.Ordnance.Drones;extra=$"잔탄 {d.RemainingRounds} · 무장 {d.ArmedCount}기 · 요격 {d.Interceptions}";}
            else extra=$"드론 격추 {Game.World.Log?.Ship(ship).DronesDestroyed??0} · 자동 요격";
        }
        float extraH=extra is null?0:HudFont(11)+6;
        var r=new Rect2(screen.X-23-257,PowerPaneBounds.Position.Y-13-42-extraH,257,42+extraH);SupportPaneBounds=r;
        Pane(r);
        if(extra is not null)Mono(new(r.Position.X+12,r.End.Y-9),extra,11,HudStyle.Rgb(0xa9c1cb),width:r.Size.X-24);
        float cy=r.Position.Y+21;
        Icon(icon,new(r.Position.X+12+8,cy),16,HudStyle.Mint);
        Mono(new(r.Position.X+12+16+8,cy+HudFont(13)*.36f),title,13,Text);
        Mono(new(r.Position.X,cy+HudFont(12)*.36f),detail,12,HudStyle.Dim,HorizontalAlignment.Center,r.Size.X);
        Mono(new(r.Position.X,cy+HudFont(11)*.36f),count,11,countColor,HorizontalAlignment.Right,r.Size.X-12);
    }

    // ── 상세(I): 계통 · 응급복구 드롭 ───────────────────────────

    /// <summary>Expanded own-ship systems above the squad pane. Returns the drop's top edge.</summary>
    private float DrawSystemsDrop(ShipBody ship,float bottom)
    {
        float row=HudFont(10)+6;
        var dc=ship.DamageControl;
        float h=12+HudFont(11)+8+row*4+8+HudFont(11)*2+18;
        var r=new Rect2(24,bottom-h,250,h);
        Pane(r);
        float x=r.Position.X+12,y=r.Position.Y+12+HudFont(11)*.85f;
        Mono(new(x,y),"계통",11,HudStyle.Rgb(0xb3c9d4));
        Mono(new(x,y),$"{InputBindings.Text(InputSetup.DamageControlMenu)} {DamageControlState.Label(dc.Priority)} · {dc.Kits}/{dc.Capacity}",11,HudStyle.Dim,HorizontalAlignment.Right,r.Size.X-24);
        y+=8;
        float col=(r.Size.X-24-12)/2;
        for(int i=0;i<Systems.Length;i++)
        {
            float v=Systems[i].Value(ship.Damage);
            var at=new Vector2(x+(i/4)*(col+12),y+(i%4)*row);
            Color color=v<=.01f?HudStyle.Red:v<.75f?HudStyle.Amber:HudStyle.Mint;
            Mono(at+new Vector2(0,HudFont(10)),Systems[i].Label,10,HudStyle.Rgb(0xa5bfca));
            Rail(new Rect2(at.X+30,at.Y+HudFont(10)*.5f+1,col-30-30,3),v,color);
            Mono(at+new Vector2(col-28,HudFont(10)),$"{v*100:0}",10,v<.75f?color:HudStyle.Dim,HorizontalAlignment.Right,28);
        }
        y+=row*4+4;
        DrawLine(new(x,y),new(r.End.X-12,y),HudStyle.Rgb(0x749bb1,.17f),1);
        string state=ship.Damage.Destroyed?"복구 불가":dc.Priority==RepairPriority.Hold?"작업 중지":dc.Kits==0?"자재 소진"
            :dc.ActiveModule is {} module?ModuleNames.Display(module.Definition):$"대기 · {dc.RestoreFraction*100:0}% 응급복구";
        y+=HudFont(11)+4;
        Label(new(x,y),state,11,dc.ActiveModule is null?Dim:Motion,width:r.Size.X-24);
        string progress=dc.ActiveModule is null?"":dc.QuietRemaining>.01?$"피격 중단 · {dc.QuietRemaining:0.0}초 후 재개":$"작업 {dc.Progress*100:0}% · {dc.WorkRemaining:0.0}초";
        y+=HudFont(11)+3;
        if(progress.Length>0){Label(new(x,y),progress,11,dc.QuietRemaining>.01?Hostile:Good,width:r.Size.X-24);HBar(new(x,y+5,r.Size.X-24,3),dc.Progress,Good);}
        return r.Position.Y;
    }

    // ── 사건 표시(복구 · 무력화 · 자함 피해) ─────────────────────

    private void DrawEventStack(ShipBody ship,float y)
    {
        // Never climb into the merit strip under the radar.
        float floor=MeritBounds.HasArea()?MeritBounds.End.Y+HudFont(14)+4:0;
        var dc=ship.DamageControl;
        if(dc.ActiveModule is {} repair&&!Game.HudDetails)
        {
            HBar(new(24,y-4,226,3),dc.Progress,Good);
            Icon("repair",new(31,y-16),13,Motion);
            Label(new(42,y-11),$"복구 · {ModuleNames.Display(repair.Definition)}",12,Motion,width:208);
            y-=HudFont(12)+14;
        }
        if(ship.Damage.Disabled){Label(new(24,y),"무력화 · 응급복구 필요",13,Hostile,width:240);y-=HudFont(13)+6;}
        // Own-ship damage is a situational event. Enemy compartment logs are never read here.
        DrawnDamageNotices=0;
        foreach(var report in _damageReadout.Recent(ship,Game.World.Time).Take(2))
        {
            if(y<floor&&DrawnDamageNotices>0)break;
            Label(new(24,Math.Max(y,floor)),report.Message,12,report.Priority>=3?Hostile:Motion,width:245);y-=HudFont(14)+4;DrawnDamageNotices++;
        }
    }
}
