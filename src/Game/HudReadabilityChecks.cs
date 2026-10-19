using System;
using System.Diagnostics;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

/// <summary>Responsive geometry plus a real rendered dense-missile/selection fixture.</summary>
public partial class HudReadabilityChecks : Node
{
    private int _checks;
    private void Check(bool ok,string why) { if(!ok)throw new InvalidOperationException(why);_checks++; }
    public override async void _Ready()
    {
        try
        {
            LayoutChecks(); LabelChecks();
            InputSetup.Register();
            var game=new ScaleTest {BattleMode=true,LaunchControl="BB-01"};AddChild(game);
            game.SetProcess(false);game.SetPhysicsProcess(false);
            var me=game.Controlled!.Body;int index=0;
            foreach(var body in game.World.Ships)
            {game.World.DetachBrain(body);body.Place(new Vec3d(1e9,++index*10000,0),Quaternion.Identity);body.Velocity=Vector3.Zero;}
            me.Place(Vec3d.Zero,Quaternion.Identity);
            var ally=game.World.Ships.First(s=>s!=me&&s.Faction==me.Faction&&s.Class.Kind==HullKind.Escort);
            var enemy=game.World.Ships.First(s=>s.Faction!=me.Faction&&s.Class.Kind==HullKind.Escort);
            ally.Place(new Vec3d(2000,0,-2000),Quaternion.Identity);
            enemy.Place(new Vec3d(500,0,-8000),Quaternion.Identity);
            game.World.Sensors.Update(game.World.Ships,game.World.Time,true);
            game.Camera.ResetAim(me.Orientation);game._Process(1);
            for(int i=0;i<120;i++)game.Camera.Follow(me.Definition,Vector3.Zero,me.Orientation,1f/60);
            for(int i=0;i<15;i++)
            {
                bool own=i<12;var source=own?ally:enemy;
                source.Ordnance.Reset();
                Check(game.World.LaunchMissile(source,own?enemy:me).Fired,"Dense fixture launches real missile");
                var missile=game.World.Missiles.Last();
                Vector3 position=game.Camera.Position+game.Camera.AimForward*5000
                    +game.Camera.GlobalBasis.X*(own?550+i*3:-400+(i-12)*85);
                missile.Position=game.RenderOrigin+Vec3d.From(position);
                missile.PrevPosition=missile.Position;missile.Velocity=Vector3.Zero;
            }
            game.World.Sensors.Update(game.World.Ships,game.World.Time,true,game.World.Probes,game.World.Missiles);
            var before=game.World.Missiles.Select(m=>m.Position).ToArray();
            var hud=game.GetNode<Hud>("HudLayer/Hud");
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            Check(hud.DrawnMissileSymbols==15,$"Every missile still has a world symbol: {hud.DrawnMissileSymbols}/15");
            Check(hud.CompactMissileSymbols>=10,$"Dense allied missiles compact instead of disappearing: {hud.CompactMissileSymbols}");
            Check(game.World.Missiles.Select(m=>m.Position).SequenceEqual(before),"HUD packing never changes missile coordinates");
            var selected=game.World.Missiles.Last();
            Vector2 point=game.Camera.UnprojectPosition((selected.Position-game.RenderOrigin).ToVector3());
            game.SelectTargetAt(point);
            Check(game.SelectedSmallContact?.Target==selected,"R targeting still uses the original missile point, not the displaced text");
            hud.QueueRedraw();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            Check(hud.DrawnMissileSymbols==15,"Selecting a threat preserves all other missile symbols");

            var observed=new SmallContact(selected,SmallContactKind.Missile,TrackLevel.Contact,
                ally.Position+new Vec3d(0,0,-5000),new Vector3(0,0,1000),game.World.Time);
            Check(Hud.ApproachesAlly(observed,ally,game.World.Time),"Observed trajectory approaching protected ally is a threat");
            Check(!Hud.ApproachesAlly(observed with{Velocity=new Vector3(0,0,-1000)},ally,game.World.Time),"Receding allied contact is not promoted as incoming");
            Check(!Hud.ApproachesAlly(observed with{Position=ally.Position+new Vec3d(8000,0,-5000)},ally,game.World.Time),"Passing wide of protected ally is not promoted");
            Check(Hud.FiringStatus(me,GunneryStatus.Arc)=="포각 밖"&&Hud.FiringStatus(me,GunneryStatus.HullBlocked)=="선체 가림",
                "Mechanical firing failures are also readable as text");
            var laser=new ShipBody("HUD-LASER",ShipDefinitions.ById("earth_air_defense_cruiser"),Faction.Blue);
            laser.Railgun!.Laser!.AutoCooling=true;
            Check(Hud.MountStatus(laser.Railgun,laser.Power)=="냉각"&&Hud.FiringStatus(laser,GunneryStatus.Reload)=="냉각 중",
                "Automatic laser cooling is not mislabeled as reload/ready");
            GD.Print($"PASS: {_checks} HUD readability/marker preservation checks");GetTree().Quit();
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
    private void LayoutChecks()
    {
        foreach(var screen in new[]{new Vector2(960,720),new(1024,768),new(1180,812),new(1280,720),new(1364,1024),new(1600,900),new(1920,1080)})
            foreach(int mounts in new[]{1,2,3,4,8})
            foreach(float scale in new[]{1f,1.15f,1.3f})
            {
                var layout=CombatHudLayout.For(screen,true,mounts,scale);
                var full=new Rect2(Vector2.Zero,screen);
                var power=new Rect2(screen.X-266,screen.Y-150,250,118);
                var weapons=new Rect2(layout.Weapons,new Vector2(390,31));
                Check(full.Encloses(layout.Gunnery)&&full.Encloses(layout.Instruments),"Both panels remain on screen");
                Check(layout.Gunnery.Size.X>=332,"Firing display always has room for ring, target and sphere");
                Check(!layout.Gunnery.Intersects(power)&&!layout.Instruments.Intersects(power),"No firing/instrument content behind power panel");
                Check(!layout.Gunnery.Intersects(new Rect2(screen.X-332,8,320,424)),"Large text never pushes the firing sphere behind the damage panel");
                Check(!layout.Gunnery.Intersects(layout.Instruments)&&!layout.Gunnery.Intersects(weapons)&&!layout.Instruments.Intersects(weapons),"Bottom controls reflow without overlapping");
                Check(full.Encloses(layout.Selection) && !layout.Selection.Intersects(layout.Gunnery) && !layout.Selection.Intersects(weapons),
                    "Persistent selected-target readout fits between the responsive battery panel and weapon keys");
                Check(!layout.Gunnery.Intersects(new Rect2(layout.HitFeedback(screen*.5f)-new Vector2(140,20),new Vector2(280,36))),"Hit feedback does not cover the compact firing panel");
                Check(layout.Gunnery.Size.Y>=112+18*((mounts+3)/4),"All turret rows fit, including eight-mount hulls");
            }
    }
    private void LabelChecks()
    {
        var labels=new MarkerLabels();
        Vector2 Measure(string s,int n)=>new(s.Length*n*.55f,n+4);
        labels.Begin(new Vector2(500,300));labels.Reserve(new Rect2(0,220,500,80));
        for(int i=0;i<15;i++)labels.Add(i,new Vector2(240,100),new Vector2(12,-8),"접촉 "+i,Colors.White,10);
        labels.Add("selected",new Vector2(240,100),new Vector2(12,-8),"선택 표적 8.0 km",Colors.Orange,100);
        labels.Add("hover",new Vector2(240,100),new Vector2(12,-8),"호버 표적 9.0 km",Colors.White,90);
        labels.Arrange(Measure);
        Check(labels.Placements[0].Label.Key.Equals("selected"),"Selected label reserves space before nearby unselected contacts");
        Check(labels.Placements.Any(p=>p.Label.Key.Equals("hover")),"Hovered detail is recoverable in dense contact cluster");
        Check(labels.Placements.All(p=>p.Label.Point==new Vector2(240,100)),"Text movement never changes marker anchors");
        var positions=labels.Placements.ToArray();
        Check(positions.All(p=>new Rect2(8,8,484,284).Encloses(p.Bounds)),"Relocated text remains within viewport");
        Check(!positions.SelectMany((p,i)=>positions.Skip(i+1).Select(q=>p.Bounds.Intersects(q.Bounds))).Any(x=>x),"Relocated text rectangles do not collide");
        labels.Begin(new Vector2(500,300));labels.ProtectSymbol(new Vector2(270,90),12);
        labels.Add("clear",new Vector2(240,100),new Vector2(12,-8),"선택 표적",Colors.White,100);labels.Arrange(Measure);
        Check(!labels.Placements[0].Bounds.Intersects(new Rect2(258,78,24,24)),"Text backgrounds cannot cover another object's symbol");
        labels.Begin(new Vector2(500,300));labels.Reserve(new Rect2(0,220,500,80));
        labels.Add("selected",new Vector2(240,100),new Vector2(12,-8),"선택 표적 8.0 km",Colors.Orange,100);labels.Arrange(Measure);
        var old=labels.Placements[0].Baseline;
        labels.Begin(new Vector2(500,300));labels.Reserve(new Rect2(0,220,500,80));
        labels.Add("selected",new Vector2(241,100),new Vector2(12,-8),"선택 표적 8.0 km",Colors.Orange,100);labels.Arrange(Measure);
        Check(labels.Placements[0].Baseline==old+Vector2.Right,"Small camera movement retains label side and row");
        labels.Begin(new Vector2(500,300));
        labels.Add("edge",new Vector2(495,295),new Vector2(12,-8),"화면 끝 선택 표적",Colors.White,100);labels.Arrange(Measure);
        Check(labels.Placements.Count==1&&new Rect2(8,8,484,284).Encloses(labels.Placements[0].Bounds),"Selected edge label remains visible");
        var watch=Stopwatch.StartNew();
        for(int frame=0;frame<200;frame++)
        {
            labels.Begin(new Vector2(1600,900));
            for(int i=0;i<240;i++)labels.ProtectSymbol(new Vector2(300+i%20*32,200+i/20*24));
            for(int i=0;i<240;i++)labels.Add(i,new Vector2(300+i%20*32,200+i/20*24),new Vector2(12,-8),"DD-120 32.0 km",Colors.White,i==0?100:10);
            labels.Arrange(Measure);
        }
        watch.Stop();GD.Print($"HUD label packing: 240 contacts, {watch.Elapsed.TotalMilliseconds/200:0.000} ms/frame (layout only)");
    }
}
