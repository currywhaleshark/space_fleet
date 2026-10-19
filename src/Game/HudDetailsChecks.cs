using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class HudDetailsChecks : Node
{
    private int _checks;
    private void Check(bool ok,string why) { if(!ok)throw new InvalidOperationException(why);_checks++; }
    private async Task Frame(Hud hud)
    {
        hud.QueueRedraw();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        hud.QueueRedraw();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
    }
    private void Shot(string name)
    {
        if(!BattleArgs.Parse(OS.GetCmdlineUserArgs()).TryGetValue("details-shots",out var folder))return;
        Directory.CreateDirectory(folder);
        Check(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder,name+".png"))==Error.Ok,"Saved HUD capture");
    }
    private static void Key(ScaleTest game,Key key) => game._UnhandledInput(new InputEventKey{PhysicalKeycode=key,Pressed=true});
    public override async void _Ready()
    {
        int old=HudSettings.TextPercent;
        try
        {
            InputSetup.Register();CheckContent();
            foreach(string role in new[]{"BB-01","DD-31","IC-21"})
            {
                var game=new ScaleTest{BattleMode=true,DevMode=false,LaunchControl=role};AddChild(game);
                game.SetProcess(false);game.SetPhysicsProcess(false);
                var me=game.Controlled!.Body;int index=0;
                foreach(var ship in game.World.Ships)
                {game.World.DetachBrain(ship);ship.Place(new Vec3d(1e9,++index*10000,0),Quaternion.Identity);ship.Velocity=Vector3.Zero;}
                me.Place(Vec3d.Zero,Quaternion.Identity);game.Camera.ResetAim(me.Orientation);game._Process(1);
                for(int f=0;f<120;f++)game.Camera.Follow(me.Definition,Vector3.Zero,me.Orientation,1f/60);
                var hud=game.GetNode<Hud>("HudLayer/Hud");
                var target=game.Views.First(v=>v.Body.Callsign=="BB-X1");
                game.SelectionHudFixture(target,TrackLevel.Locked,game.Camera.Position+game.Camera.AimForward*6000);
                CombatFeedbackChecks.Strike(game.World,me,target.Body,HitKind.Penetration,771);
                if(game.Gunnery is {} order)order.Doctrine=FireDoctrine.Manual;
                foreach(int percent in new[]{100,115,130})
                {
                    HudSettings.SetTextPercent(percent,false);await Frame(hud);
                    Check(hud.DrawnDamageNotices>0&&hud.DrawnDamageNotices<=2,"Real impact produces at most two recent damage notices");
                    Shot(role+"-hud-"+percent);
                    var mode=Input.MouseMode;int rounds=me.Railguns.Sum(g=>g.Rounds);
                    var aim=game.Camera.AimForward;
                    Key(game,Godot.Key.F1);await Frame(hud);
                    Check(game.ShowHelp&&Input.MouseMode==Input.MouseModeEnum.Visible,"Help releases flight input capture");
                    Check(new Rect2(Vector2.Zero,GetViewport().GetVisibleRect().Size).Encloses(hud.HelpBounds),"Help fits the viewport at every text size");
                    Check(hud.HelpPage==0&&hud.HelpPageCount==2,"Normal play starts with ship essentials and excludes debug tools");
                    if(percent==130)Shot(role+"-help-essential");
                    Input.ActionPress(InputSetup.Fire);
                    game._UnhandledInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=new(20,20)});
                    game._UnhandledInput(new InputEventMouseMotion{Relative=new(500,200),Position=new(20,20)});
                    Key(game,Godot.Key.Key2);Key(game,Godot.Key.M);Key(game,Godot.Key.Tab);
                    game._PhysicsProcess(SimWorld.TickDelta);game._Process(SimWorld.TickDelta);
                    Check(me.Railguns.Sum(g=>g.Rounds)==rounds&&game.SelectedWeapon==PlayerWeapon.MainGun,"Help clicks/weapon keys never fire or switch weapons");
                    Check(!game.WorldMapOpen&&!game.RosterOpen&&game.Camera.AimForward.IsEqualApprox(aim),"Help does not steer the ship or leak map/roster inputs");
                    Key(game,Godot.Key.Right);await Frame(hud);
                    Check(hud.HelpPage==1,"Keyboard switches to common/advanced controls");
                    Check(new Rect2(Vector2.Zero,GetViewport().GetVisibleRect().Size).Encloses(hud.HelpBounds),"Advanced help fits at every text size");
                    if(percent==130)Shot(role+"-help-advanced");
                    Key(game,Godot.Key.Escape);
                    Check(!game.ShowHelp&&!game.Paused&&Input.MouseMode==mode,"Escape closes help and safely restores cursor ownership");
                    game._PhysicsProcess(SimWorld.TickDelta);game._Process(SimWorld.TickDelta);
                    Check(me.Railguns.Sum(g=>g.Rounds)==rounds,"A held help click cannot fire on closing");
                    Input.ActionRelease(InputSetup.Fire);
                    game._UnhandledInput(new InputEventAction{Action=InputSetup.Fire,Pressed=false});
                }
                game.Free();
            }
            GD.Print($"PASS: {_checks} HUD text/damage/help checks");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
        finally{HudSettings.SetTextPercent(old,false);Input.ActionRelease(InputSetup.Fire);}
    }
    private void CheckContent()
    {
        foreach(var def in ShipDefinitions.All.Concat(ShipVariants.All.Select(v=>v.Definition)))
        {
            var ship=new ShipBody("HELP",def,Faction.Blue);
            var basic=string.Join(" ",ShipHelp.Entries(ship,ScaleTest.SchemeFor(ship),0).Select(r=>r.Action));
            var advanced=string.Join(" ",ShipHelp.Entries(ship,ScaleTest.SchemeFor(ship),1).Select(r=>r.Action));
            Check(basic.Contains("반물질")== (def.Antimatter is not null),"Essential weapons reflect actual hull capabilities");
            Check(advanced.Contains("프로브")== (def.ReconProbes is not null),"Probe controls appear only on equipped hulls");
            Check(advanced.Contains("드론")== (def.DefenseDrones is not null),"Drone controls appear only on equipped hulls");
            Check(def.Modules.All(m=>ModuleNames.Display(m).Any(c=>c>='가'&&c<='힣')),"Every module has a player-facing Korean name");
            Check(def.HullSections.All(s=>ModuleNames.Display(s).Any(c=>c>='가'&&c<='힣')),"Every armor section has a player-facing Korean name");
        }
        var design=ShipDefinitions.ById("mars_starter_escort");
        var module=design.Modules.First(m=>m.Id=="generator-port");
        Check(ModuleNames.Display(module)=="좌현 발전기","Internal generator ID becomes its physical system name");
        Check(ModuleNames.Translate(design,"service-generator-port 장갑에 차단")=="좌현 발전기 구획 장갑에 차단","Raw armor section names are translated too");
        var reports=new[]{new DamageReport(1,module.Name+" 파괴"),new DamageReport(2,"실드 흡수 5"),
            new DamageReport(3,"실드 흡수 8"),new DamageReport(4,"전력망 단절"),new DamageReport(5,"실드 흡수 10")};
        var summary=DamageReadout.Select(reports,design,6);
        Check(summary.Length==2&&summary.All(r=>r.Priority==3),"Critical compartment failures survive noisy shield hits");
        Check(summary.Any(r=>r.Message=="좌현 발전기 파괴"),"Damage log translates raw module names");
        var blocked=DamageReadout.Select(reports.Append(new DamageReport(6,"service-generator-port 장갑에 차단")),design,7);
        Check(blocked.Any(r=>r.Message=="좌현 발전기 파괴"),"Armor impacts on nearby plating cannot hide a destroyed internal module");
        summary=DamageReadout.Select(reports.Append(new DamageReport(6,module.Name+" 응급복구 · 35%")),design,7);
        Check(summary.Any(r=>r.Message.Contains("응급복구"))&&!summary.Any(r=>r.Message.Contains("발전기 파괴")),"A recovered compartment no longer claims to be destroyed");
        Check(DamageReadout.Select(reports,design,30).Length==0,"Old reports expire instead of lingering as current damage");
        Check(DamageReadout.Select(new[]{new DamageReport(1,"전원·추진 응급복구 · 무력화 해제")},design,2).Single().Priority==2,"Recovery is not presented as a new critical disable");
        Check(DamageReadout.Select(new[]{new DamageReport(1,"실드 소진"),new DamageReport(2,"실드 흡수 60")},design,3).Any(n=>n.Message=="실드 소진"),"Small shield hits cannot erase a recent shield collapse");
        var font=new SystemFont{FontNames=new[]{"Malgun Gothic","Segoe UI"}};
        foreach(int percent in new[]{100,115,130})
        {
            HudSettings.SetTextPercent(percent,false);
            Check(HudSettings.TextPercent==percent&&HudSettings.FontSize(12)>=(percent==100?12:14),"Text-size setting increases actual glyph size");
            var lines=HudText.Wrap(font,"좌현 발전기 출력 저하 · 응급복구가 필요합니다",HudSettings.FontSize(13),210);
            Check(lines.All(s=>font.GetStringSize(s,fontSize:HudSettings.FontSize(13)).X<=210),"Korean status text wraps inside its measured width");
        }
        HudSettings.SetTextPercent(100,false);
    }
}
