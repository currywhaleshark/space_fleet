using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

/// <summary>Actual renderer: observed missile markers and readable range/lock/laser-hold states.</summary>
public partial class InterceptionHudChecks : Node
{
    private int _checks;
    private void Check(bool value,string why){if(!value)throw new InvalidOperationException(why);_checks++;}
    private async Task Frame(Hud hud)
    {
        for(int i=0;i<2;i++){hud.QueueRedraw();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);}
    }
    private void Shot(string name)
    {
        if(!BattleArgs.Parse(OS.GetCmdlineUserArgs()).TryGetValue("interception-shots",out var folder))return;
        Directory.CreateDirectory(folder);
        Check(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder,name+".png"))==Error.Ok,"Screenshot saved");
    }
    public override async void _Ready()
    {
        try
        {
            InputSetup.Register();
            var game=new ScaleTest{BattleMode=true,LaunchControl="DD-11",LaunchConfig=new BattleConfig{
                HullOverrides=new Dictionary<string,string>{{"DD-11","earth_air_defense_cruiser_t5"}}}};
            AddChild(game);game.SetProcess(false);game.SetPhysicsProcess(false);
            int index=0;foreach(var body in game.World.Ships)
            {game.World.DetachBrain(body);body.Place(new(1e9,++index*10000,0),Quaternion.Identity);body.Velocity=Vector3.Zero;}
            var me=game.Controlled!.Body;me.Place(Vec3d.Zero,Quaternion.Identity);
            var source=game.World.Ships.First(s=>s.Faction!=me.Faction&&s.Class.Kind==HullKind.Escort);
            source.Place(new(0,0,-8000),Quaternion.Identity);
            game.World.Sensors.Update(game.World.Ships,0,true);
            Check(game.World.LaunchMissile(source,me).Fired,"Fixture missile launches");
            var missile=game.World.Missiles.Last();missile.Velocity=Vector3.Zero;
            source.Place(new(1e9,0,0),Quaternion.Identity);
            var scout=game.World.Ships.First(s=>s!=me&&s.Faction==me.Faction);
            game.Camera.ResetAim(me.Orientation);game._Process(1);
            for(int f=0;f<120;f++)game.Camera.Follow(me.Definition,Vector3.Zero,me.Orientation,1f/60);
            var hud=game.GetNode<Hud>("HudLayer/Hud");
            foreach(var (name,range,link) in new[]{("locked",24000,false),("lock-wait",60000,false),("out-of-range",64000,true)})
            {
                missile.Position=missile.PrevPosition=new(0,800,-range);
                scout.Place(link?missile.Position+new Vec3d(2000,0,0):new(1e9,50000,0),Quaternion.Identity);
                game.World.Sensors.Update(game.World.Ships,0,true,game.World.Probes,game.World.Missiles);
                var seen=game.World.Sensors.SmallTrack(me.Faction,missile)!.Value;
                game.SelectTargetAt(game.Camera.UnprojectPosition((seen.Position-game.RenderOrigin).ToVector3()));
                Check(game.SelectedSmallContact?.Target==missile,"Observed missile remains selectable");
                Check(name=="locked"?game.SmallAimSolved:name=="lock-wait"?game.SmallAimStatus.Contains("잠금"):
                    game.SmallAimStatus.Contains("사거리 밖")&&game.SmallAimStatus.Contains("40.0 km"),"Specific firing reason: "+game.SmallAimStatus);
                await Frame(hud);Shot(name);
            }
            int visible=hud.DrawnMissileSymbols;
            var hidden=new Missile{Id=99999,Shooter=source,Definition=missile.Definition,Health=10,
                Position=game.RenderOrigin+Vec3d.From(game.Camera.Position+game.Camera.AimForward*5000+Vector3.Right*300)};
            ((List<Missile>)game.World.Missiles).Add(hidden);
            await Frame(hud);Check(hud.DrawnMissileSymbols==visible,"No marker for an unobserved live missile");
            ((List<Missile>)game.World.Missiles).Remove(missile);
            await Frame(hud);Check(hud.DrawnMissileSymbols==visible,"Unobserved removal cannot erase the last known missile marker");
            ((List<Missile>)game.World.Missiles).Add(missile);
            // A distant locked hull displays automatic attenuation hold, not reload or a false range limit.
            var target=game.Views.First(v=>v.Body==source);source.Place(new(0,0,-150000),Quaternion.Identity);
            scout.Place(source.Position+new Vec3d(4000,0,0),Quaternion.Identity);
            game.World.Sensors.Update(game.World.Ships,0,true,game.World.Probes,game.World.Missiles);
            game._PhysicsProcess(SimWorld.TickDelta);
            game._Process(.016);
            game.SelectMapContact(target);game.Gunnery!.Doctrine=FireDoctrine.Focus;
            game.World.TryAutoFire(me,source,null,100,out var status);game.Gunnery.Status=status;
            Check(status==GunneryStatus.Attenuated&&Hud.FiringStatus(me,status).Contains("거리 감쇠"),"Low-output hull fire explains the hold");
            await Frame(hud);Shot("attenuated");
            GD.Print($"PASS: {_checks} missile interception HUD checks");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
