using System;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class Hud
{
    internal Rect2 TacticalBounds { get; private set; }
    private void DrawTactical(ShipBody ship,Vector2 screen)
    {
        TacticalBounds=default;if(ship.Tactical.ActiveSlots.Length==0||Game.MenuOpen)return;
        // A compact column beside own-ship systems stays clear of the target damage diagram and ribbons.
        var origin=new Vector2(278,screen.Y-372);TacticalBounds=new(origin,new(156,110));
        if(AntimatterBounds.HasArea()&&TacticalBounds.Intersects(AntimatterBounds))
        {origin.X=Math.Max(origin.X,AntimatterBounds.End.X+8);origin.Y=Math.Min(origin.Y,screen.Y-430);TacticalBounds=new(origin,new(156,110));}
        for(int i=0;i<4;i++)
        {
            var at=origin+new Vector2(i%2*80,i/2*58);var slot=i<ship.Tactical.ActiveSlots.Length?ship.Tactical.ActiveSlots[i]:null;
            var status=slot is null?TacticalStatus.Unavailable:ship.Tactical.Status(slot);
            var color=status is TacticalStatus.Active or TacticalStatus.Preparing?Motion:status==TacticalStatus.Ready?Friendly:Dim;
            DrawRect(new(at,new(76,52)),new(.015f,.035f,.05f,.88f));
            Label(at+new Vector2(5,17),InputBindings.Text(InputSetup.TacticalSlots[i]),12,color,width:42);
            if(slot is not null)DrawTextureRect(MenuArt.Icon(HangarView.TacticalIcon(slot.Definition.Effect)),new(at+new Vector2(48,2),new(24,24)),false,color);
            string name=slot?.Definition.Effect switch{TacticalEffect.GeneratorOverload=>"과부하",TacticalEffect.HeatVent=>"열 배출",TacticalEffect.FocusSearch=>"탐색",TacticalEffect.ShortJump=>"도약",_=>"빈 슬롯"};
            string detail=slot is null?name:status==TacticalStatus.Cooldown?$"{Math.Ceiling(slot.CooldownUntil-ship.SimTime):0}초":status is TacticalStatus.Active or TacticalStatus.Preparing?$"{(status==TacticalStatus.Preparing?"준비":"가동")} {Math.Max(0,slot.ActiveUntil-ship.SimTime):0.0}":status==TacticalStatus.Ready?name:TacticalState.Label(status);
            Label(at+new Vector2(5,43),detail,11,color,width:66);
        }
    }
}
