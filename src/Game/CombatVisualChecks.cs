using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>GPU regression fixture and repeatable combat-effect frames, driven by real damage events.</summary>
public partial class CombatVisualChecks : Node3D
{
    public string? Preview { get; init; }
    public string? Output { get; init; }
    public float PreviewAge { get; init; }=.12f;
    private readonly SimWorld _world=new();
    private ShipView _ship=null!;
    private ShipBody _source=null!;
    private Camera3D _camera=null!;
    private BallisticsView _ballistics=null!;
    private int _frame,_checks;
    private bool _saving;
    private void Check(bool ok,string why) { if(!ok) throw new InvalidOperationException(why); _checks++; }
    public override void _Ready()
    {
        var body=_world.Add(new ShipBody("FX-BB",ShipClass.Battleship,Faction.Blue));
        body.Place(Vec3d.Zero,Quaternion.Identity);
        _source=_world.Add(new ShipBody("FX-SOURCE",ShipClass.Interceptor,Faction.Red));
        _source.Place(new Vec3d(0,0,-20000),Quaternion.Identity);
        _ship=ShipView.Create(body,5); AddChild(_ship);
        _camera=new Camera3D { Position=new Vector3(1100,680,-1100),Far=1e6f,Fov=48 }; AddChild(_camera);
        _camera.LookAt(new Vector3(0,30,-30)); _camera.MakeCurrent();
        if(Preview is "penetration" or "armor" or "critical") {
            Vector3 part=body.Definition.Modules.First(m=>m.Kind==ModuleKind.Gun).Center;
            _camera.Position=part+new Vector3(420,180,-160); _camera.LookAt(part); }
        AddChild(new WorldEnvironment { Environment=new Godot.Environment {
            BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new Color(.006f,.012f,.022f),
            AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=new Color(.35f,.42f,.55f),AmbientLightEnergy=.6f,
            TonemapMode=Godot.Environment.ToneMapper.Agx,GlowEnabled=true,GlowIntensity=.75f,GlowHdrThreshold=1.2f,GlowBloom=.015f } });
        var light=new DirectionalLight3D { LightEnergy=2.5f }; AddChild(light); light.RotationDegrees=new Vector3(-35,-45,0);
        _ballistics=new BallisticsView(); AddChild(_ballistics);
        UpdateViews();
        if(Preview is null) { try { Run(); GD.Print($"PASS: {_checks} combat visual checks"); } catch(Exception ex) { GD.PushError(ex.ToString()); GetTree().Quit(1); return; } GetTree().Quit(); }
    }
    private void UpdateViews()
    { _ship.Sync(Vec3d.Zero,1,1f/60); _ship.SyncCombat(_world,_camera); _ballistics.Sync(_world,Vec3d.Zero,1,_camera,new[]{_ship}); }
    private void Advance(int ticks)
    { for(int i=0;i<ticks;i++) _world.Step(); UpdateViews(); }
    private ProjectileImpact Hit(string mode)
    {
        var body=_ship.Body;
        if(mode is "armor" or "penetration" or "critical") body.Damage.AbsorbShield(1e8f,Preview is null?_world.Time:_world.Time-2);
        Vector3 at=body.Definition.Modules.First(m=>m.Kind==ModuleKind.Gun).Center;
        Vector3 dir=new Vector3(-1,-.25f,.15f).Normalized();
        if(Preview is "armor" or "penetration" or "critical")
        {
            // Broadside armor gives an unobstructed close-up. The automated fixture
            // above still exercises the harder turret-overhang hit.
            at=body.Definition.Modules.First(m=>m.Id=="magazine-starboard").Center;
            dir=Vector3.Left;
        }
        float energy=mode=="collapse"?body.Damage.Shield:300;
        var packet=new DamagePacket(energy,mode is "armor" or "shield" or "collapse"?1:1500,mode=="critical"?100000:30,5000);
        float before=body.Damage.Shield;
        var result=DamageRay.Apply(body,Vec3d.From(at-dir*2000),dir,packet,_world.Time,(uint)(_world.Tick+100));
        _world.RecordImpact((uint)(_world.Tick+100),_source,result,_world.Time,dir,energy,before,BattleWeapon.Railgun);
        if(Preview is "armor" or "penetration" or "critical" && result.LocalPoint is {} localPoint)
        {
            // Look along the incoming trajectory so the demonstration actually sees
            // the impacted armor, including shots under a turret overhang.
            Vector3 surface=_ship.ProjectFxPoint(localPoint,dir);
            _camera.Position=surface-dir*520; _camera.LookAt(surface);
        }
        UpdateViews(); return _world.Impacts[^1];
    }
    private void Run()
    {
        var timeline=CarbonCombatFx.ExplosionTimes(8,123);
        var same=CarbonCombatFx.ExplosionTimes(8,123);
        Check(timeline.LocalTimes.SequenceEqual(same.LocalTimes),"Carbon explosion schedule is replay deterministic");
        Check(timeline.LocalTimes.Zip(timeline.LocalTimes.Skip(1),(a,b)=>b>=a).All(x=>x)
            && timeline.GlobalTime>timeline.LocalTimes[^1] && timeline.EndTime>timeline.GlobalTime+1,
            "Local explosions precede the main burst, with bounded afterglow");
        Check(!timeline.LocalTimes.SequenceEqual(CarbonCombatFx.ExplosionTimes(8,124).LocalTimes),"Independent ships get distinct local explosion timing");
        Check(CarbonCombatFx.MuzzleAge(5.05,5,0,0,2)>=0 && CarbonCombatFx.MuzzleAge(5.05,5,1,0,2)>=0
            && CarbonCombatFx.MuzzleAge(5.05,5,2,0,2)<0,"Only barrels that fired receive their effect; twin rounds remain simultaneous");
        Check(CarbonCombatFx.MuzzleAge(5.05,5,1,0,2,.1f)<0
            && CarbonCombatFx.MuzzleAge(6,double.NegativeInfinity,0,0,2)<0,"Future and never-fired muzzle effects stay inactive");
        var skin=_ship.ProjectFxSurface(Vector3.Zero,Vector3.Left);
        Check(skin.Point.X>20 && skin.Normal.Dot(Vector3.Right)>0,"Visual hit projection finds outer armor and its outward normal");
        var pose=_ship.Transform;
        _ship.Position=new Vector3(2000,-700,500); _ship.Basis=new Basis(Vector3.Up,.7f);
        var shiftedSkin=_ship.ProjectFxSurface(Vector3.Zero,Vector3.Left);
        Check(shiftedSkin.Point.DistanceTo(skin.Point)<.05f && shiftedSkin.Normal.DistanceTo(skin.Normal)<.01f,
            "Armor projection remains in ship space across rotation and render-origin movement");
        _ship.Transform=pose;
        Check(_ship.Shield.ActivePatches==0,"Resting shield is entirely invisible");
        var absorbed=Hit("shield");
        Check(absorbed.Hit.ShieldStopped && absorbed.Hit.ShieldPoint is not null,"Shield event is at the shared exterior surface");
        Check(_ship.Shield.ActivePatches==1 && _ballistics.VisibleFragments==0,"Shield hit shows a patch with no hull debris");
        Advance(60); Check(_ship.Shield.ActivePatches==0,"Impact patch expires");
        Hit("collapse"); Check(_world.Impacts[^1].ShieldBroken && _ship.Shield.ActivePatches==1,"Depletion emits one collapse front");
        Advance(60); Check(_ship.Shield.ActivePatches==0,"Collapse leaves no persistent bubble");
        var armor=Hit("armor"); int small=_ballistics.VisibleFragments;
        Check(small>0 && small<20,"Armor ejects a small set of chips");
        Advance(5); Check(_ballistics.VisibleBurstSparks>0,"Armor impact emits directional high-energy streaks"); Advance(175);
        var penetration=Hit("penetration"); Check(_ballistics.VisibleFragments>small,"Penetration emits more debris than armor absorption");
        Check(BallisticsView.FragmentCount(absorbed)==0 && BallisticsView.FragmentCount(penetration)>BallisticsView.FragmentCount(armor),"Debris follows physical hit severity");
        Advance(180); Check(_ballistics.VisibleFragments==0 && _ballistics.VisibleBurstSparks==0 && _ballistics.VisibleBurstFlashes==0,"Debris and layered impacts expire together");
        _ship.Body.Damage.Reset(); UpdateViews();
        foreach(var m in _ship.Body.Damage.Modules.Where(m=>m.Definition.Kind is ModuleKind.Reactor or ModuleKind.Generator))
            _ship.Body.Damage.Hurt(m,m.Health,_world.Time,0,0);
        UpdateViews(); Check(_ship.Body.Damage.Disabled && _ship.Wreck.PieceCount==0,"A disabled ship remains intact");
        _ship.Body.Damage.Breakup(_world.Time,"visual fixture"); UpdateViews();
        Check(_ship.Wreck.PieceCount==2,"Structural loss splits the actual rendered model in two");
        Advance(12); Check(_ship.Wreck.VisibleExplosionSparks>0,"Local explosions follow the separating wreck");
        int pool=_ship.Wreck.ExplosionPoolSize; UpdateViews(); UpdateViews();
        Check(pool==_ship.Wreck.ExplosionPoolSize,"Repeated render updates neither replay bursts nor grow the pool");
        Advance(288); Check(_ship.Body.Hull.Boxes.Count>0,"Separated wreck retains collision geometry");
        Check(_ship.Wreck.VisibleExplosionFlashes==0 && _ship.Wreck.VisibleExplosionSparks==0,"All staged destruction effects expire, including after skipped render frames");
        _ship.Body.Damage.Reset(); UpdateViews(); Check(_ship.Wreck.PieceCount==0 && _ship.GetNode<Node3D>("Model").Visible
            && _ship.Wreck.VisibleExplosionSparks==0,"Repair clears fragments and effects and restores model");
        _ship.Body.Damage.Catastrophe(_world.Time,"visual fixture"); UpdateViews();
        Check(_ship.Wreck.PieceCount==8,"Energetic catastrophe shatters the model into eight major pieces");
        Vector3 cameraPosition=_camera.Position;
        _camera.Position=new Vector3(0,0,-3000000); Advance(1);
        Check(_ship.Wreck.VisibleExplosionFlashes>0,"Destruction remains visible beyond the normal camera far plane");
        _camera.Position=cameraPosition;
        _ship.Body.Damage.Reset(); UpdateViews();
        _world.FireRailguns(_ship.Body,Vector3.Forward); Advance(3);
        Check(_ballistics.VisibleTrails>0,"Actual battery fire creates visible rail trails");
        Check(_ballistics.VisibleBurstFlashes>=2,"A real twin-barrel salvo drives multiple muzzle flashes");
        Vector3 far=new(0,0,-200000);
        float normal=CombatFx.PixelSize(_camera,far,2); _camera.Fov=12;
        Check(CombatFx.PixelSize(_camera,far,2)<normal*.3f,"Minimum visual width follows scope FOV, without inflating zoomed rounds");
        var ordnance=new OrdnanceView(); AddChild(ordnance);
        _source.Ordnance.Antimatter.Jettison(); _world.Step();
        ordnance.Sync(_world,Vec3d.Zero,1,_camera.Position,_camera);
        Check(ordnance.GetChildren().OfType<MeshInstance3D>().Any(m=>m.Visible),"Actual AM jettison emits a visible flash/fragment event");
        Advance(180);
        for(int i=0;i<5;i++) ordnance.Sync(_world,Vec3d.Zero,1,_camera.Position,_camera);
        Check(!ordnance.GetChildren().OfType<MeshInstance3D>().Any(m=>m.Visible),"Expired ordnance pool entries stay hidden across repeated camera-facing updates");
        Check(_world.LaunchMissile(_source,_ship.Body).Fired,"Visual attitude fixture uses an actual missile launch");
        var missile=_world.Missiles.Single();
        missile.PreviousNoseDirection=Vector3.Up;
        missile.NoseDirection=Vector3.Up.Rotated(Vector3.Right,.2f);
        missile.Velocity=Vector3.Forward*1000;
        ordnance.Sync(_world,Vec3d.Zero,.5,_camera.Position,_camera);
        var missileNode=ordnance.GetChildren().OfType<Node3D>().Single(n=>n.GetChildCount()==2);
        Vector3 expected=missile.PreviousNoseDirection.Slerp(missile.NoseDirection,.5f);
        Check((-missileNode.Basis.Z).Normalized().DistanceTo(expected)<.001f,
            "Missile body and engine plume interpolate the physical nose, independently of momentum");
        missile.Age=missile.Definition.BurnSeconds;
        ordnance.Sync(_world,Vec3d.Zero,1,_camera.Position,_camera);
        Check(!missileNode.GetChild<Node3D>(1).Visible,"Coasting missile retains its body but extinguishes the engine plume");
        _world.ResetWeapons();
        ordnance.Sync(_world,Vec3d.Zero,1,_camera.Position,_camera);
        Check(!GodotObject.IsInstanceValid(missileNode) || missileNode.IsQueuedForDeletion(),"Reset removes missile visuals and their saved attitude");
    }
    public override void _Process(double delta)
    {
        if(Preview is null || _saving) return;
        _frame++;
        if(_frame==10)
        {
            if(Preview=="rail") _world.FireRailguns(_ship.Body,Vector3.Forward);
            else if(Preview=="breakup") _ship.Body.Damage.Breakup(_world.Time,"visual fixture");
            else if(Preview is "shatter" or "far") _ship.Body.Damage.Catastrophe(_world.Time,"visual fixture");
            else Hit(Preview);
            if(Preview=="far") { _camera.Position=new Vector3(0,0,-3000000); _camera.LookAt(Vector3.Zero); }
            UpdateViews();
        }
        if(_frame>10) Advance(1);
        if(_frame>=10+Mathf.Max(1,Mathf.RoundToInt(PreviewAge*60))) { _saving=true; Save(); }
    }
    private async void Save()
    {
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        if(_world.Impacts.LastOrDefault() is {} impact && impact.Hit.LocalPoint is {} local)
        {
            var skin=_ship.ProjectFxSurface(local,impact.IncomingDirection);
            GD.Print($"FX surface: hit={local}, visible={skin.Point}, normal={skin.Normal}, sparks={_ballistics.VisibleBurstSparks}");
        }
        if(Output is not null) GetViewport().GetTexture().GetImage().SavePng(Output);
        GD.Print("combat visual preview saved: "+Output); GetTree().Quit();
    }
}
