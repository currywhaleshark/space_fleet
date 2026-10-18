using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>Real renderer/audio fixture; uses resolved hangar equipment and real 60 Hz laser damage.</summary>
public partial class LaserGameChecks : Node3D
{
    public string? Shot { get; init; }
    private SimWorld _world = new();
    private ShipBody _source=null!,_target=null!;
    private ShipView _sourceView=null!,_targetView=null!;
    private Camera3D _camera=null!;
    private BallisticsView _effects=null!;
    private CombatAudio _audio=null!;
    private int _frame,_checks;
    private bool _saved;
    private void Check(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);_checks++;}
    public override void _Ready()
    {
        try
        {
            var def=FleetLoadouts.ResolveFit(FleetLoadouts.Empty("escort") with{Components=new(MainGun:"main-laser")}).Definition;
            _source=_world.Add(new ShipBody("LASER-01",def,Faction.Blue));
            _target=_world.Add(new ShipBody("TARGET",ShipDefinitions.ById("mars_battleship"),Faction.Red));
            _source.Place(Vec3d.Zero,Quaternion.Identity);_target.Place(new(0,0,-2700),Quaternion.Identity);
            _source.Control=_target.Control=new ShipControl{FlightAssist=false};
            _source.DamageControl.Priority=_target.DamageControl.Priority=RepairPriority.Hold;
            _world.Log=new BattleLog(_world);
            _sourceView=ShipView.Create(_source,1);_targetView=ShipView.Create(_target,2);AddChild(_sourceView);AddChild(_targetView);
            // --laser-view=chase: behind and above the shooter, the beam receding toward the target (gameplay view).
            bool chase=OS.GetCmdlineUserArgs().Contains("--laser-view=chase");
            _camera=new Camera3D{Position=chase?new(45,75,330):new(2100,1400,800),Near=.1f,Far=1e6f,Fov=chase?60:48};AddChild(_camera);
            _camera.LookAt(chase?new Vector3(0,0,-900):new Vector3(0,0,-1400));_camera.MakeCurrent();
            AddChild(new WorldEnvironment{Environment=BattleLook.Environment()});foreach(var light in BattleLook.Lights())AddChild(light);
            _effects=new BallisticsView();AddChild(_effects);_audio=new CombatAudio();AddChild(_audio);_audio.Prime(_world,_source);
            Check(_audio.AssetsReady,"Laser and existing audio assets load");
            Check(_sourceView.FindChild("ReplacementLaserEmitter",true,false) is MeshInstance3D,"Optical emitter replaces rail barrels");
            Check(MenuArt.Component(ComponentSlot.MainGun,"main-laser").GetWidth()>0,"Laser equipment icon loads");
            string comparison=ComponentComparison.Fitting(ComponentSlot.MainGun,ShipDefinitions.ById("escort"),def);
            Check(comparison.Contains("탄약 없음")&&comparison.Contains("폐열")&&comparison.Contains("장갑"),"Confirmation describes energy, armor and heat tradeoff");
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
    public override void _Process(double delta)
    {
        if(_saved)return;
        try
        {
            _frame++;
            if(_frame<=180) _world.FireRailguns(_source,Vector3.Forward,_target.Position);
            _world.Step();
            _sourceView.Sync(Vec3d.Zero,1,.016f);_targetView.Sync(Vec3d.Zero,1,.016f);
            _sourceView.SyncCombat(_world,_camera);_targetView.SyncCombat(_world,_camera);
            _effects.Sync(_world,Vec3d.Zero,1,_camera,new[]{_sourceView,_targetView});
            _audio.Observe(_world,_frame>=140&&_frame<165?_target:_source);
            _audio.SyncListener(Vec3d.From(_camera.Position),_camera.Basis);_audio.AdvanceLoops(.016f);
            if(_frame==120)
            {
                Check(_effects.VisibleLaserBeams>0,"Live laser beam renders");
                Check(_source.Railguns.Any(g=>g.Laser?.HitShip==_target),"Visual endpoint follows actual target collision");
                Check(_world.Projectiles.Count==0,"No rail projectile visual exists");
                Check(_audio.LaserPlaying,"Held emission has continuous onboard audio");
                Check(_audio.PlayCount(CombatSound.RailFire)==0,"Laser does not play railgun bangs");
                Check(_source.Railguns.All(g=>g.LastFiredAt==double.NegativeInfinity),"No ballistic muzzle flash or recoil event");
                if(Shot is not null){Save();return;}
            }
            if(_frame==160)
            {
                Check(!_audio.LaserPlaying,"Spectator handoff clears former onboard emitter");
                Check(_audio.Fleet.ActiveLasers==1,"Former ship becomes an audible nearby laser source");
            }
            if(_frame==175)Check(_audio.LaserPlaying&&_audio.Fleet.ActiveLasers==0,"Following source restores onboard loop without duplicate spatial voice");
            if(_frame==178)
            {
                _audio.StopAll();_audio.AdvanceLoops(.2f);
                Check(!_audio.LaserPlaying&&_audio.Fleet.ActiveLasers==0,"StopAll cannot restart a still-emitting ship without a fresh observation");
            }
            if(_frame==180){SimWorld.StopLasers(_source);_audio.SetPaused(true);_audio.SetPaused(false);}
            if(_frame==220)
            {
                Check(_effects.VisibleLaserBeams==0,"Released beam is removed");
                Check(!_audio.LaserPlaying,"Released loop fades and stops");
                _audio.StopAll();Check(!_audio.LaserPlaying,"Scene teardown stops loop");
                GD.Print($"PASS: {_checks} laser render/audio/equipment checks");_saved=true;GetTree().Quit();
            }
        }
        catch(Exception e){GD.PushError(e.ToString());_saved=true;GetTree().Quit(1);}
    }
    private async void Save()
    {
        _saved=true;await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        var image=GetViewport().GetTexture().GetImage();
        if(image.SavePng(Shot!)!=Error.Ok){GetTree().Quit(1);return;}
        GD.Print($"PASS: {_checks} laser preview checks; saved {Shot}");GetTree().Quit();
    }
}
