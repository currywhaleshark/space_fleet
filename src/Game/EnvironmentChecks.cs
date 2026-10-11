using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

public partial class EnvironmentChecks : Node
{
    private int _checks;
    private void Check(bool ok,string why) { if(!ok)throw new InvalidOperationException(why); _checks++; }
    public override void _Ready()
    {
        try { Run(); GD.Print($"PASS: {_checks} environment presentation checks"); GetTree().Quit(); }
        catch(Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
    private void Run()
    {
        var w=new SimWorld();
        var me=w.Add(new ShipBody("ME",ShipClass.Battleship,Faction.Blue));
        var target=w.Add(new ShipBody("MEMORY",ShipClass.Escort,Faction.Red));
        target.Place(new(0,0,-15000),Quaternion.Identity); target.Velocity=new(120,0,0);
        w.Sensors.Update(w.Ships,0,force:true);
        var contacts=new ContactMemory(); contacts.Observe(Faction.Blue,target,w.Sensors.Track(Faction.Blue,target),0);
        var original=contacts.Get(Faction.Blue,target)!;
        w.Obstacles.Add(new("WALL",new(0,0,-7000),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,1500)));
        w.Sensors.Update(w.Ships,.25,force:true);
        var memory=w.Sensors.Memory(Faction.Blue,target,.25)!;
        target.Place(new(1e8,0,0),Quaternion.Identity); target.Damage.Breakup(1,"hidden");
        contacts.Observe(Faction.Blue,target,SensorTrack.Unknown,12,memory);
        var predicted=contacts.Get(Faction.Blue,target)!;
        Check(predicted.SignalLost && predicted.Prediction==memory,"Presentation recognizes a separate occlusion memory");
        Check(predicted.Position==memory.Predict(12) && predicted.DisplayPosition(target.Position)==predicted.Position,"HUD/map/radar share predicted position, not actual hidden position");
        Check(predicted.State==original.State && !predicted.State!.Destroyed,"Hidden destruction does not leak into prediction");
        Check(predicted.UncertaintyMeters==memory.ErrorMeters(12) && predicted.UncertaintyMeters>original.Track.ErrorMeters,"Displayed uncertainty grows with time");
        contacts.Observe(Faction.Blue,target,SensorTrack.Unknown,25,memory);
        Check(contacts.Get(Faction.Blue,target)!.UncertaintyMeters>predicted.UncertaintyMeters,"Later marker has a larger uncertainty radius");
        contacts.Observe(Faction.Blue,target,SensorTrack.Unknown,60,memory);
        var expired=contacts.Get(Faction.Blue,target)!;
        Check(expired.Prediction is null && expired.Position==original.Position && expired.SignalLost,"Expired tactical prediction returns to existing frozen historical ghost");
        Check(!w.TryAutoFire(me,target,null,30,out _),"Presentation does not grant a firing lock");
        target.Damage.Reset(); target.Place(new(8000,0,-9000),Quaternion.Identity); w.Sensors.Update(w.Ships,61,force:true);
        contacts.Observe(Faction.Blue,target,w.Sensors.Track(Faction.Blue,target),61,w.Sensors.Memory(Faction.Blue,target,61));
        Check(contacts.Get(Faction.Blue,target) is {SignalLost:false,Prediction:null},"Reacquisition clears dashed prediction");
        var camera=new ChaseCamera(); AddChild(camera);
        foreach(double shift in new[]{0.0,1e12})
        {
            Vec3d origin=new(shift,shift,-shift); var obstacles=new ObstacleSet();
            obstacles.Add(new("CAMERA",origin+new Vec3d(0,0,600),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,150)));
            camera.Position=new(0,0,1000); var aim=camera.AimForward; float fov=camera.Fov;
            Check(camera.AvoidObstacles(obstacles,origin,origin) && camera.Position.Z<450 && camera.Position.Z>440,"Camera pulls before terrain surface in double coordinates");
            Check(camera.AimForward==aim && camera.Fov==fov,"Camera clipping preserves aim and lens");
            Check(!obstacles.SegmentBlocked(origin+Vec3d.From(camera.Position),origin+Vec3d.From(camera.Position),out _,2),"Camera remains outside padded terrain");
            camera.Follow(me.Definition,Vector3.Zero,Quaternion.Identity,.016f); var restored=camera.Position;
            Check(!camera.AvoidObstacles(new(),origin,origin) && camera.Position==restored && restored.Z>450,"Clear orbit restores normal camera position");
            camera.SetTelescope(true); camera.Position=new(0,0,1000);
            Check(!camera.AvoidObstacles(obstacles,origin,origin),"Third-person clip leaves the telescope's authored orbit intact"); camera.SetTelescope(false);
        }
        Check(GunneryLabels.Status(GunneryStatus.Obstructed)=="지형 가림" && GunneryLabels.Status(GunneryStatus.HullBlocked)=="선체 가림","Terrain has a distinct HUD status");
        var screen=new ObstacleSet();screen.Add(new("SCREEN",new(0,0,600),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,150)));
        camera.ResetAim(Quaternion.Identity);camera.Position=new(0,0,1000);camera.AvoidObstacles(screen,Vec3d.Zero,Vec3d.Zero);
        double pulled=camera.Position.Z;
        screen.Clear();screen.Add(new("FAR",new(10000,0,0),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,150)));
        camera.Follow(me.Definition,Vector3.Zero,Quaternion.Identity,.016f);camera.Position=new(0,0,1000);camera.AvoidObstacles(screen,Vec3d.Zero,Vec3d.Zero);
        Check(System.Math.Abs(camera.Position.Z-pulled)<.001,"A clear frame does not immediately snap the camera out");
        double previous=camera.Position.Z;
        for(int i=0;i<180;i++)
        {
            camera.Follow(me.Definition,Vector3.Zero,Quaternion.Identity,.016f);camera.Position=new(0,0,1000);camera.AvoidObstacles(screen,Vec3d.Zero,Vec3d.Zero);
            Check(camera.Position.Z>=previous && camera.Position.Z-previous<25,"Camera restores smoothly and monotonically");previous=camera.Position.Z;
        }
        Check(camera.Position.Z>995,"Camera eventually restores the complete orbit");
        screen.Clear();screen.Add(new("AHEAD",new(0,0,-3000),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,1000)));
        me.Velocity=new(0,0,-1000);Check(Hud.PredictTerrainImpact(screen,me,out _),"Four-second collision prediction includes hull radius");
        me.Velocity=new(0,0,1000);Check(!Hud.PredictTerrainImpact(screen,me,out _),"Receding terrain does not warn");
        me.Velocity=Vector3.Zero;Check(!Hud.PredictTerrainImpact(screen,me,out _),"Stationary ship does not flash a prediction");
        camera.QueueFree();
    }
}

public partial class ScaleTest
{
    /// <summary>Deterministic screenshot fixture; never used by ordinary battle setup.</summary>
    private void PreviewEnvironment()
    {
        if(_shot is null || !BattleArgs.Parse(OS.GetCmdlineUserArgs()).TryGetValue("environment-preview",out string? mode))return;
        if(_frame==70 && mode=="camera")
        {
            double actual=(RenderOrigin+Vec3d.From(Camera.Position)-Controlled!.Body.Position).Length();
            double desired=new Vector2(Controlled.Body.Class.CameraDistance,Controlled.Body.Class.CameraHeight).Length();
            GD.Print($"Camera obstacle pull: {desired:F2}m -> {actual:F2}m");
            if(actual>=desired*.8)GD.PushError("Camera preview did not meaningfully clip its orbit");
            return;
        }
        if(_frame==70 && mode=="map")
        {
            WorldMap.Fit(new[]{new Vec3d(-9500,-2000,-8500),new Vec3d(9500,2000,6500)});
            return;
        }
        if(_frame!=10 || Controlled is null)return;
        World.Obstacles.Clear();
        foreach(var ship in World.Ships)
        {
            World.DetachBrain(ship); ship.Place(new(1e8+World.Ships.ToList().IndexOf(ship)*10000,0,0),Quaternion.Identity);
            ship.Velocity=Vector3.Zero; ship.Control=new ShipControl{FlightAssist=false};
            if(ship.Gunnery is { } guns)guns.Doctrine=FireDoctrine.Hold;
        }
        Controlled.Body.Place(new(0,0,mode=="camera"?-2000:5000),Quaternion.Identity);
        var target=Views.First(v=>v.Body.Callsign=="BB-X1"); target.Body.Place(new(0,0,-5500),Quaternion.Identity); target.Body.Velocity=new(80,0,0);
        Camera.ResetAim(Quaternion.Identity);
        World.Sensors.Update(World.Ships,World.Time,force:true); UpdateContacts(); SelectEnemy(target);
        void Structure(string name,Vec3d position)
        {
            World.Obstacles.Add(ZoneStructureShapes.For(name,position));
            var view=CaptureZoneView.Create(new(name,position)); _worldRoot.AddChild(view); _captureViews.Add(view);
        }
        Structure("A",Vec3d.Zero);
        if(mode=="map") { Structure("B",new(6500,0,-500)); Structure("C",new(-6500,0,-500)); }
        for(int i=0;i<720;i++)World.Step();
        World.Sensors.Update(World.Ships,World.Time,force:true); UpdateContacts();
        if(mode=="fire")
        {
            var scout=World.Ships.First(s=>s.Callsign=="DD-11"); scout.Place(new(6500,0,-5000),Quaternion.Identity);
            World.Sensors.Update(World.Ships,World.Time,force:true);
            Gunnery!.Doctrine=FireDoctrine.Focus; World.Step(); UpdateContacts();
            GD.Print("Environment firing preview status: "+Gunnery.Status);
        }
        GD.Print($"Environment preview {mode}: memory={ContactOf(target)?.Prediction is not null}, obstacles={World.Obstacles.Count}");
        Paused=true;
        if(mode=="map") { _worldMap.Open(); _hud.Visible=false; }
    }
}
