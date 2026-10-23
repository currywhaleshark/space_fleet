using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

public partial class TargetingHudChecks : Node
{
    private int _checks;
    private void Check(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);_checks++;}
    private async Task Frame(Hud hud)
    {
        hud.TargetPreview.Refresh();hud.QueueRedraw();
        for(int i=0;i<4;i++)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
    }
    private void Shot(string name)
    {
        var args=BattleArgs.Parse(OS.GetCmdlineUserArgs());
        if(!args.TryGetValue("targeting-shots",out var dir))return;
        Directory.CreateDirectory(dir);
        Check(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(dir,name+".png"))==Error.Ok,"Saved actual GPU frame");
    }
    public override async void _Ready()
    {
        int old=HudSettings.TextPercent;
        try
        {
            InputSetup.Register();
            foreach(var role in new[]{"BB-01","IC-21"})
            {
                var game=new ScaleTest{BattleMode=true,LaunchControl=role};AddChild(game);
                game.SetProcess(false);game.SetPhysicsProcess(false);
                var me=game.Controlled!.Body;int i=0;
                foreach(var body in game.World.Ships)
                {game.World.DetachBrain(body);body.Place(new(1e9,++i*10000,0),Quaternion.Identity);body.Velocity=Vector3.Zero;}
                me.Place(Vec3d.Zero,Quaternion.Identity);game.Camera.ResetAim(me.Orientation);game._Process(0);
                for(int f=0;f<120;f++)game.Camera.Follow(me.Definition,Vector3.Zero,me.Orientation,1f/60);
                var target=game.Views.First(v=>v.Body.Callsign=="BB-X1");
                var hud=game.GetNode<Hud>("HudLayer/Hud");int rounds=me.Railguns.Sum(g=>g.Rounds);
                foreach(int percent in new[]{100,130})
                {
                    HudSettings.SetTextPercent(percent,false);
                    foreach(var level in new[]{TrackLevel.Contact,TrackLevel.Identified,TrackLevel.Locked})
                    {
                        game.TargetingFixture(target,level,new(Vector3.Up,.85f));
                        await Frame(hud);
                        Check(level==TrackLevel.Contact?!hud.TargetPreview.ModelVisible:hud.TargetPreview.ModelVisible,"Model respects sensor identification");
                        Check(level==TrackLevel.Locked||hud.TargetPreview.SelectableCount==0,"No part markers before lock");
                        Check(new Rect2(Vector2.Zero,GetViewport().GetVisibleRect().Size).Encloses(hud.UnifiedBarBounds),"Unified bar fits");
                        Check(!hud.UnifiedBarBounds.Intersects(new Rect2(hud.TargetPreview.Position,hud.TargetPreview.Size)),"Target model clears the weapon bar");
                        Shot(role+"-"+level+"-"+percent);
                    }
                    game.TargetingFixture(target,TrackLevel.Locked,Quaternion.Identity);
                    await Frame(hud);Shot(role+"-aft-"+percent);
                    var part=ExternalTargets.For(target.Body.Definition)!.Parts.FirstOrDefault(p=>ExternalTargets.TryAim(me,target.Body,game.TrackOf(target),game.World.Time,p.Id,out _));
                    if(part is not null)
                    {
                        Check(game.SelectExternalTarget(part.Id),"Same selection gate as simulation");
                        if(hud.TargetPreview.PartScreenPoint(part.Id) is {} clickAt)
                        {
                            game.SelectExternalTarget(null);
                            hud.TargetPreview._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=clickAt});
                            Check(game.SelectedExternalId is not null&&ExternalTargets.TryAim(me,target.Body,game.TrackOf(target),game.World.Time,game.SelectedExternalId,out _),"Preview click selects only an allowed surface");
                        }
                        game._UnhandledInput(new InputEventKey{PhysicalKeycode=Key.Y,Pressed=true});
                        Check(game.SelectedExternalId is null||ExternalTargets.TryAim(me,target.Body,game.TrackOf(target),game.World.Time,game.SelectedExternalId,out _),"Y cannot cycle into a hidden surface");
                        Input.ActionPress(InputSetup.Fire);game.SelectExternalTarget(null);
                        game._UnhandledInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=new(10,10)});
                        Check(me.Railguns.Sum(g=>g.Rounds)==rounds,"Part selection cannot leak a held click into a shot");
                        Input.ActionRelease(InputSetup.Fire);
                        game._UnhandledInput(new InputEventAction{Action=InputSetup.Fire,Pressed=false});
                    }
                    var frozen=hud.TargetPreview.CameraTransform;
                    game.LoseSelectionHudFixture(target);await Frame(hud);
                    Check(hud.TargetPreview.SelectableCount==0&&hud.TargetPreview.CameraTransform==frozen,"Lost model freezes and loses selection authority");
                    Shot(role+"-lost-"+percent);
                    game.TargetingFixture(target,TrackLevel.Locked,new(Vector3.Up,1.1f));
                    game.Camera.SetTelescope(true);for(int zoom=0;zoom<120;zoom++)game.Camera.Follow(me.Definition,Vector3.Zero,me.Orientation,1f/60);game._Process(0);await Frame(hud);Shot(role+"-scope-"+percent);
                    game.Camera.ResetTelescope();game._Process(0);
                    game.ToggleHudDetails();await Frame(hud);Shot(role+"-details-"+percent);game.ToggleHudDetails();
                }
                if(me.Definition.Antimatter is not null)
                {
                    game.SelectWeapon(PlayerWeapon.Antimatter);me.Step(1);
                    await Frame(hud);Shot(role+"-arming-130");
                    Check(!hud.AntimatterBounds.HasPoint(GetViewport().GetVisibleRect().Size*.5f)&&!hud.AntimatterBounds.Intersects(hud.UnifiedBarBounds),"AM warning clears reticle and weapon bar");
                    Check(me.Ordnance.Antimatter.Mode==AntimatterMode.Arming,"AM preparation remains visible alongside unified bar");
                }
                if(me.Railguns.Length>1)
                {
                    var gun=me.Railguns[0];var module=me.Damage.Modules.First(m=>m.Definition.Id==gun.Definition.ModuleId);
                    module.Health=0;
                    Check(Hud.ReadTurret(game,me,gun).Destroyed,"Destroyed gun has fixed X, no moving marker");
                    module.Health=module.Definition.HitPoints;me.Power.SetPips(2,2,0,4,0);
                    Check(!Hud.ReadTurret(game,me,gun).Destroyed,"Power loss is not destruction");
                    me.Power.Reset();
                }
                Check(me.Railguns.Sum(g=>g.Rounds)==rounds,"HUD never spends ammunition");
                game.Free();
            }
            await StressStates();
            GD.Print($"PASS: {_checks} native HUD v4 checks");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
        finally{HudSettings.SetTextPercent(old,false);Input.ActionRelease(InputSetup.Fire);}
    }
    private async Task StressStates()
    {
        var members=FleetLoadouts.Default("battleship").Members.ToArray();
        var bays=TacticalModulesCatalog.Bays(FleetLoadouts.ResolveFit(members[0]).Definition);
        members[0]=members[0] with{Components=new(){TacticalModules=new[]{new TacticalMount(bays[0].Id,"generator-overload"),new TacticalMount(bays[1].Id,"focus-search")}}};
        var match=new MatchLineup(new[]{new MatchParticipant(1,Faction.Blue,"battleship",true,FleetLoadouts.Resolve(new(members))),new MatchParticipant(1,Faction.Red,"battleship",false)});
        var game=new ScaleTest{BattleMode=true,LaunchConfig=new BattleConfig{Match=match},LaunchControl=match.Player.Callsign(0)};AddChild(game);
        game.SetProcess(false);game.SetPhysicsProcess(false);
        var me=game.Controlled!.Body;int n=0;
        foreach(var body in game.World.Ships){game.World.DetachBrain(body);body.Place(new(1e9,++n*10000,0),Quaternion.Identity);}
        me.Place(Vec3d.Zero,Quaternion.Identity);game.Camera.ResetAim(me.Orientation);game._Process(0);
        for(int f=0;f<120;f++)game.Camera.Follow(me.Definition,Vector3.Zero,me.Orientation,1f/60);
        var target=game.Views.First(v=>v.Body.Faction!=me.Faction);
        var hud=game.GetNode<Hud>("HudLayer/Hud");
        Check(me.Tactical.Activate(0,game.World).Fired,"Real overload activates");me.Step(9);
        Check(me.Tactical.Activate(1,game.World).Fired,"Real search activates");
        game.TargetingFixture(target,TrackLevel.Locked,new(Vector3.Up,.8f));
        CombatFeedbackChecks.Strike(game.World,me,target.Body,HitKind.Penetration,99601);
        var hit=game.World.Impacts[^1];game.World.Merits.Hit(hit.Id,me,hit.Hit,hit.Weapon,hit.Time,0,game.World.Merits.Capture(me));
        game.World.Sensors.Update(game.World.Ships,game.World.Time,true);game.World.Merits.Step();game.AdvanceMerits(.1f);
        HudSettings.SetTextPercent(130,false);await Frame(hud);Shot("stress-tactical-ribbons-130");
        Check(me.Tactical.Status(me.Tactical.ActiveSlots[0])==TacticalStatus.Cooldown&&me.Tactical.Status(me.Tactical.ActiveSlots[1])==TacticalStatus.Active,"Cooldown and active are independent");
        Check(!hud.RibbonStripBounds.Intersects(hud.UnifiedBarBounds),"Actual ribbons clear active/cooldown slots");
        me.Damage.Modules.First(m=>m.Definition.Kind==ModuleKind.Gun).Health=0;
        me.DamageControl.Priority=RepairPriority.Weapons;me.DamageControl.Step(1);
        me.Power.AddHeat(me.Definition.Power.HeatCapacityMj*1.1f);me.Power.Step(0);
        Check(me.Power.Overheated&&me.DamageControl.ActiveModule is not null,"Heat and repair fixture uses real subsystem states");
        await Frame(hud);Shot("stress-heat-repair-130");
        game.ToggleHudDetails();await Frame(hud);Shot("stress-details-drones-130");
        Check(!hud.SquadronBounds.Intersects(hud.UnifiedBarBounds),"Expanded squad clears unified bar");
        game.Free();
    }

}

public partial class ScaleTest
{
    internal void TargetingFixture(ShipView target,TrackLevel level,Quaternion orientation)
    {
        target.Body.Damage.Reset();
        target.Body.Place(RenderOrigin+Vec3d.From(Camera.Position+Camera.AimForward*3500),orientation);
        World.Sensors.Update(World.Ships,World.Time,true);
        var track=World.Sensors.Track(Controlled!.Body.Faction,target.Body) with{Level=level};
        _contacts.Observe(Controlled.Body.Faction,target.Body,track,World.Time);SelectEnemy(target);
        if(Gunnery is {} order){order.Doctrine=FireDoctrine.Manual;order.Engaged=null;order.Solution=null;}
        target.Sync(RenderOrigin,1,0);
    }
}
