using System;
using System.IO;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class MeritGameChecks : Node
{
    private int _checks;
    private void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);_checks++;}
    private async System.Threading.Tasks.Task Frame(Hud hud)
    {hud.QueueRedraw();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);hud.QueueRedraw();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);}
    private void Shot(string name)
    {
        if(!BattleArgs.Parse(OS.GetCmdlineUserArgs()).TryGetValue("merit-shots",out var dir))return;
        Directory.CreateDirectory(dir);Check(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(dir,name+".png"))==Error.Ok,"Screenshot saved");
    }
    public override async void _Ready()
    {
        int original=HudSettings.TextPercent;
        try
        {
            InputSetup.Register();
            foreach(string role in new[]{"IC-21","BB-01"})
            {
                var game=new ScaleTest{BattleMode=true,DevMode=false,LaunchControl=role};AddChild(game);game.SetProcess(false);game.SetPhysicsProcess(false);
                var me=game.Controlled!.Body;int i=0;
                foreach(var ship in game.World.Ships){game.World.DetachBrain(ship);ship.Place(new(1e9,++i*10000,0),Quaternion.Identity);ship.Velocity=Vector3.Zero;}
                me.Place(Vec3d.Zero,Quaternion.Identity);game.Camera.ResetAim(me.Orientation);game._Process(1);
                var target=game.Views.First(v=>v.Body.Callsign=="BB-X1");
                game.SelectionHudFixture(target,TrackLevel.Locked,game.Camera.Position+game.Camera.AimForward*6000);
                game.World.Sensors.Update(game.World.Ships,game.World.Time,true);
                for(int n=0;n<6;n++)
                {
                    CombatFeedbackChecks.Strike(game.World,me,target.Body,n==5?HitKind.Critical:HitKind.Penetration,(uint)(9000+n));
                    var hit=game.World.Impacts[^1];game.World.Merits.Hit(hit.Id,me,hit.Hit,hit.Weapon,hit.Time,0,game.World.Merits.Capture(me));
                }
                game.World.Sensors.Update(game.World.Ships,game.World.Time,true);game.World.Merits.Step();game.AdvanceMerits(.016f);
                var hud=game.GetNode<Hud>("HudLayer/Hud");
                Check(game.World.Merits.Personal()[MeritKind.Hit]==6,"Actual rays reach personal merit totals");
                foreach(int percent in new[]{100,130})
                {
                    HudSettings.SetTextPercent(percent,false);await Frame(hud);
                    Check(hud.MeritBounds.HasArea()&&new Rect2(Vector2.Zero,GetViewport().GetVisibleRect().Size).Encloses(hud.MeritBounds),"Ribbons fit supported resolution and font scale");
                    var size=GetViewport().GetVisibleRect().Size;
                    Check(!hud.MeritBounds.Intersects(hud.SquadronBounds),"Ribbons stop above squad/system panels");
                    Check(!hud.MeritBounds.HasPoint(size*.5f),"Central aiming view remains clear");
                    CheckPopupLayout(game,hud,size);
                    Shot(role+"-ribbons-"+percent);
                    // Same frame after the pop and banner animations have settled.
                    for(int k=0;k<5;k++)game.AdvanceMerits(.1f);
                    await Frame(hud);CheckPopupLayout(game,hud,size);Shot(role+"-merit-pop-"+percent);
                    game.Camera.SetTelescope(true);await Frame(hud);Check(hud.MeritBounds.HasArea(),"Scope retains personal ribbons");Shot(role+"-scope-"+percent);game.Camera.SetTelescope(false);
                }
                Check(game.MeritDisplay.Showing.Count<=2,"At most two important notices");
                if(game.MeritDisplay.Showing.Count>0)
                {
                    float age=game.MeritDisplay.Showing[0].Age;game.Paused=true;game.AdvanceMerits(1);
                    Check(game.MeritDisplay.Showing[0].Age==age,"Pause freezes screen-time notifications");game.Paused=false;
                }
                foreach(int speed in new[]{1,2,4})
                {game.SetBattleSpeed(speed);float age=game.MeritDisplay.Showing.FirstOrDefault()?.Age??0;game.AdvanceMerits(.016f);
                    Check(game.MeritDisplay.Showing.Count==0||Math.Abs(game.MeritDisplay.Showing[0].Age-age-.016f)<.001,"Notification lifetime independent of battle speed");}
                var before=game.World.Merits.Personal();game.BeginSpectating();game.AdvanceMerits(.016f);
                Check(game.World.Merits.Controlled is null&&game.World.Merits.Personal()[MeritKind.Hit]==before[MeritKind.Hit],"Spectating keeps personal attribution instead of adopting observed ship");
                await Frame(hud);Shot(role+"-spectating");
                game.World.Merits.Freeze(game.World.Time);var frozen=game.World.Merits.Outcome!.Personal;
                game.World.Merits.Hit(99999,me,game.World.Impacts[0].Hit,BattleWeapon.Railgun,99,0,new(true));
                Check(game.World.Merits.Outcome.Personal.Damage==frozen.Damage,"Official result remains frozen");
                if(role=="BB-01")
                {
                    var host=GetParent<Main>();var overlay=host.ShowMeritResultFixture(game);
                    for(int scope=0;scope<4;scope++){overlay.ActivateButton(4+scope);await Frame(hud);Shot("result-scope-"+scope);Check(overlay.Visible,"Result scope renders");}
                    overlay.ActivateButton(3);await Frame(hud);Shot("result-history");host.EndMeritResultFixture();
                }
                RemoveChild(game);game.QueueFree();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            CheckQueue();GD.Print($"PASS: {_checks} merit HUD/notification/control/freeze checks");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
        finally{HudSettings.SetTextPercent(original,false);Input.MouseMode=Input.MouseModeEnum.Visible;}
    }
    private void CheckPopupLayout(ScaleTest game,Hud hud,Vector2 size)
    {
        var screen=new Rect2(Vector2.Zero,size);var strip=hud.RibbonStripBounds;var banner=hud.MeritNoticeBounds;
        var layout=CombatHudLayout.For(size,game.Scheme==ControlScheme.Helm,game.Controlled!.Body.Railguns.Length,HudSettings.TextScale);
        Check(strip.HasArea()&&screen.Encloses(strip),"Ribbon pop-ups fit the screen");
        Check(!strip.Intersects(new Rect2(size*.5f-new Vector2(60,40),new Vector2(120,80)))&&strip.End.Y<=layout.Selection.Position.Y,"Ribbon pop-ups keep the reticle and weapon row clear");
        Check(!strip.Intersects(hud.MeritBounds)&&!strip.Intersects(hud.SquadronBounds)&&!strip.Intersects(layout.Instruments)&&!strip.Intersects(hud.AntimatterBounds)&&!strip.Intersects(hud.InterceptorStatusBounds),"Ribbon pop-ups do not cover HUD panels");
        Check(!banner.HasArea()||(screen.Encloses(banner)&&!banner.HasPoint(size*.5f)&&!banner.Intersects(strip)),"Merit banners stay above the reticle");
    }
    private void CheckQueue()
    {
        var w=new SimWorld();var a=w.Add(new ShipBody("A",ShipClass.Interceptor,Faction.Blue));var t=w.Add(new ShipBody("T",ShipClass.Interceptor,Faction.Red));
        w.Merits.Controlled=a;t.Place(new(0,0,-1000),Quaternion.Identity);w.Sensors.Update(w.Ships,0,true);
        var display=new MeritPresentation();uint id=20000;
        for(int n=0;n<10;n++)w.Merits.Hit(++id,a,new(t,t.Position,0,false,false,Array.Empty<ModuleHit>(),""){StructuralDamage=1},BattleWeapon.Railgun,0,0,new(true));
        display.Advance(w.Merits,.016f,false,false,null);Check(display.Showing.Count==0,"Ordinary burst does not create ten important alerts");
        var hits=display.Ribbons.Single(r=>r.Kind==MeritKind.Hit);
        Check(hits.Count==10&&display.Gains[MeritKind.Hit]==10&&display.Pulses[MeritKind.Hit]>0,"Burst merges into one hit ribbon with its count");
        for(int k=0;k<3;k++)display.Advance(w.Merits,.1f,false,false,null);
        float age=hits.Age;display.Advance(w.Merits,1,true,false,null);Check(hits.Age==age,"Pause freezes ribbon pop-ups");
        w.Merits.Hit(++id,a,new(t,t.Position,0,false,false,Array.Empty<ModuleHit>(),""){StructuralDamage=1},BattleWeapon.Railgun,0,0,new(true));
        display.Advance(w.Merits,.016f,false,false,null);
        Check(display.Ribbons.Count(r=>r.Kind==MeritKind.Hit)==1&&hits.Count==11&&hits.Bump<=0&&hits.Age<.02f,"A repeat hit pops the same ribbon again");
        t.Damage.Breakup(0,"test");w.Sensors.Update(w.Ships,0,true);
        w.Merits.Hit(++id,a,new(t,t.Position,0,false,false,Array.Empty<ModuleHit>(),""){CausedDestruction=true},BattleWeapon.Railgun,0,0,new(true));
        display.Advance(w.Merits,.016f,false,false,null);
        Check(display.Showing.Any(n=>n.Event.Kind==MeritKind.Kill),"Kill notification survives ordinary-hit spam");
        Check(display.Ribbons[^1].Kind==MeritKind.Kill&&display.Ribbons.Any(r=>r.Kind==MeritKind.Hit),"Kill ribbon joins the strip on the right");
        var fresh=display.Ribbons.Where(r=>r.Age<.02f).OrderByDescending(r=>r.Bump).ToArray();
        Check(fresh.Length<2||fresh.Zip(fresh.Skip(1)).All(p=>p.First.Bump-p.Second.Bump>MeritPresentation.CascadeStep*.9f),"Ribbons gained together pop in turn: "+string.Join(",",fresh.Select(r=>$"{r.Kind}:{r.Bump:0.000}")));
        Check(display.Ribbons.Count<=MeritPresentation.MaxRibbons,"Ribbon strip is bounded");
        for(int k=0;k<45;k++)display.Advance(w.Merits,.1f,false,false,null);
        Check(display.Ribbons.Count==0&&display.Showing.Count==0&&display.Gains.Count==0,"Pop-ups, banners and gains expire in screen time");
    }
}

public partial class Main
{
    internal BattleOverlay ShowMeritResultFixture(ScaleTest game)
    {
        SetProcess(false);Battle=game;_fixtureOutcome=new(Faction.Blue,"검증용 판정",game.World.Time);
        var layer=new CanvasLayer{Layer=10};AddChild(layer);
        _overlay=new BattleOverlay{Host=this};layer.AddChild(_overlay);ChangeScreen(BattleScreen.Result);return _overlay;
    }
    internal void EndMeritResultFixture(){var layer=_overlay.GetParent();RemoveChild(layer);layer.QueueFree();Battle=null;_fixtureOutcome=null;Screen=BattleScreen.Title;}
}
