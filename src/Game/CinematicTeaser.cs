using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>Isolated, deterministic in-engine teaser stage. No changes to player scenes or ship balance.</summary>
public partial class CinematicTeaser : Node3D
{
    public Dictionary<string,string> Options { get; init; }=new();
    public static readonly string[] ShotNames={"fleet","battery","run","shield","torpedo","title"};
    public static readonly float[] Durations={6,5,6,5,10,6};
    public record SoundCue(float time,string sound,float gain,float pitch,float pan);
    private readonly List<SoundCue> _sounds=new();
    private readonly List<ShipView> _views=new();
    private SimWorld _world=null!;
    private Node3D _stage=null!;
    private Camera3D _camera=null!;
    private BallisticsView _rails=null!;
    private OrdnanceView _ordnance=null!;
    private MeshInstance3D _planet=null!;
    private TeaserOverlay _overlay=null!;
    private ShipBody _hero=null!,_other=null!;
    private int _shot=-1,_frame;
    private float _clock,_local;
    private bool _preview,_saving,_fired,_launched;
    private uint _impact=100000;
    private readonly HashSet<int> _events=new();
    private sealed record Bolt(float Arrival,float Energy,float Penetration,float Damage,Vector3 Aim,MeshInstance3D Head,MeshInstance3D Tail);
    private readonly List<Bolt> _bolts=new();
    private ShaderMaterial _boltLight=null!,_boltTrail=null!,_amLight=null!;
    private MeshInstance3D? _amHead,_amTail,_contactFlash;
    private OmniLight3D? _contactLight;
    private ShaderMaterial? _contactMaterial;
    private Missile? _torpedo;
    private float _launchAt,_impactAt=-1;
    private double _simCredit;
    private float _alpha=1;
    private Vector3 _cameraLook,_launchCamera,_launchLook,_impactCamera,_impactLook,_impactPoint,_impactSurface;
    public override void _Ready()
    {
        Input.MouseMode=Input.MouseModeEnum.Visible;
        _preview=Options.ContainsKey("teaser-preview");
        var sky=new ShaderMaterial { Shader=GD.Load<Shader>("res://shaders/starfield.gdshader") };
        sky.SetShaderParameter("star_brightness",.8f);
        AddChild(new WorldEnvironment { Environment=new Godot.Environment {
            BackgroundMode=Godot.Environment.BGMode.Sky, Sky=new Sky { SkyMaterial=sky },
            AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=new Color(.28f,.35f,.48f),AmbientLightEnergy=.5f,
            TonemapMode=Godot.Environment.ToneMapper.Agx,GlowEnabled=true,GlowIntensity=.7f,GlowBloom=.012f,GlowHdrThreshold=1.3f } });
        AddChild(new DirectionalLight3D { Name="Key",LightEnergy=2.0f,LightColor=new Color(.85f,.91f,1),
            ShadowEnabled=true,DirectionalShadowMaxDistance=8000,DirectionalShadowMode=DirectionalLight3D.ShadowMode.Parallel4Splits,
            Basis=Basis.LookingAt(new Vector3(-.55f,-.6f,.35f).Normalized()) });
        AddChild(new DirectionalLight3D { Name="Rim",LightEnergy=1.3f,LightColor=new Color(.95f,.65f,.38f),
            SkyMode=DirectionalLight3D.SkyModeEnum.LightOnly,Basis=Basis.LookingAt(new Vector3(.4f,-.2f,-.8f).Normalized()) });
        AddChild(new DirectionalLight3D { Name="UndersideFill",LightEnergy=.35f,LightColor=new Color(.4f,.65f,1),
            SkyMode=DirectionalLight3D.SkyModeEnum.LightOnly,Basis=Basis.LookingAt(new Vector3(.2f,.8f,-.4f).Normalized()) });
        _camera=new Camera3D { Near=.5f,Far=1000000,Fov=46 }; AddChild(_camera); _camera.MakeCurrent();
        var canvas=new CanvasLayer(); AddChild(canvas); _overlay=new TeaserOverlay(); canvas.AddChild(_overlay);
        int shot=_preview?Array.IndexOf(ShotNames,Options["teaser-preview"]):0;
        if(shot<0) throw new ArgumentException("Unknown teaser shot");
        Enter(shot); UpdateCamera(); Sync();
    }
    private ShipBody Add(string name,ShipClass kind,Faction faction,Vector3 position,Vector3? velocity=null)
    {
        var body=_world.Add(new ShipBody(name,kind,faction)); body.Place(Vec3d.From(position),Quaternion.Identity);
        body.Velocity=velocity??Vector3.Zero; body.Control=new ShipControl { FlightAssist=false };
        var view=ShipView.Create(body,17+_views.Count*13); _stage.AddChild(view); _views.Add(view); return body;
    }
    private void Enter(int shot)
    {
        if(_stage is not null) { RemoveChild(_stage); _stage.QueueFree(); }
        _shot=shot; _frame=0; _local=0; _events.Clear(); _views.Clear(); _fired=_launched=false;
        _bolts.Clear(); _torpedo=null; _impactAt=-1; _simCredit=0; _alpha=1; _amHead=_amTail=_contactFlash=null; _contactLight=null;
        _world=new SimWorld(); _stage=new Node3D { Name="TeaserStage" }; AddChild(_stage);
        _hero=Add("TEASER-BB",ShipClass.Battleship,Faction.Blue,Vector3.Zero);
        _other=_hero;
        if(shot is 0 or 5)
        {
            Add("ESCORT-1",ShipClass.Escort,Faction.Blue,new Vector3(740,-70,-340),new Vector3(0,0,-30));
            Add("ESCORT-2",ShipClass.Escort,Faction.Blue,new Vector3(-820,120,580),new Vector3(0,0,-30));
            Add("ESCORT-3",ShipClass.Escort,Faction.Blue,new Vector3(550,160,850),new Vector3(0,0,-30));
            _hero.Velocity=new Vector3(0,0,-30);
            foreach(var ship in _world.Ships) ship.Control=new ShipControl { FlightAssist=false,Thrust=new Vector3(0,0,.12f) };
        }
        else if(shot==2)
        {
            _other=_hero;
            _hero=Add("RAIDER",ShipClass.Interceptor,Faction.Red,new Vector3(-270,-160,1050),new Vector3(0,0,-230));
            _hero.Control=new ShipControl { FlightAssist=false,Thrust=new Vector3(0,0,.3f) };
        }
        else if(shot==3)
            _other=Add("ATTACKER",ShipClass.Interceptor,Faction.Red,new Vector3(20000,0,0));
        else if(shot==4)
        {
            _other=_hero;
            Vector3 start=new(1500,-160,1400);
            _hero=Add("ASSAULT",ShipClass.Interceptor,Faction.Red,start);
            _hero.Place(Vec3d.From(start),new Quaternion(Basis.LookingAt(-start.Normalized())));
            // This filming target has already lost its shield and fire-control sensors.
            // Missile speed, guidance and hull contact remain the normal simulation path.
            foreach(var m in _other.Damage.Modules.Where(m=>m.Definition.Kind is ModuleKind.ShieldEmitter or ModuleKind.Sensor))
                _other.Damage.Hurt(m,m.Health,0,0,0);
            _hero.Ordnance.Antimatter.BeginArming();
            for(int i=0;i<180;i++) _world.Step();
            _hero.Velocity=_hero.Forward*100;
            _hero.Control=new ShipControl { FlightAssist=false,Thrust=new Vector3(0,0,.25f) };
        }
        _rails=new BallisticsView(); _stage.AddChild(_rails); _ordnance=new OrdnanceView(); _stage.AddChild(_ordnance);
        _boltLight=CombatFx.Glow(new Color(1,.82f,.46f)); _boltTrail=CombatFx.Glow(new Color(1,.58f,.22f),true);
        _amLight=CombatFx.Glow(new Color(.65f,.9f,1));
        if(shot==3)
        {
            AddBolt(.8f,230,1,1,new Vector3(0,50,-250));
            AddBolt(1.65f,300,1,1,new Vector3(0,-20,30));
            AddBolt(2.55f,400,1,1,new Vector3(0,70,220));
            AddBolt(3.45f,400,1,1,new Vector3(0,50,-220));
            AddBolt(4.55f,800,2400,180,_hero.Definition.Modules.First(m=>m.Kind==ModuleKind.Gun).Center);
        }
        if(shot==4) {
            _amHead=CombatFx.Make(_stage,CombatFx.Quad,_amLight); _amHead.Visible=false;
            _amTail=CombatFx.Make(_stage,CombatFx.Quad,CombatFx.Glow(new Color(.25f,.65f,1),true)); _amTail.Visible=false;
            _contactMaterial=CombatFx.Glow(new Color(.7f,.88f,1));
            _contactFlash=CombatFx.Make(_stage,CombatFx.Quad,_contactMaterial); _contactFlash.Visible=false;
            _contactLight=new OmniLight3D { LightColor=new Color(.6f,.8f,1),OmniRange=400,ShadowEnabled=false,Visible=false }; _stage.AddChild(_contactLight); }
        _planet=new MeshInstance3D { Name="Backdrop", Mesh=new SphereMesh { Radius=6500,Height=13000,RadialSegments=128,Rings=64 },
            MaterialOverride=new ShaderMaterial { Shader=GD.Load<Shader>("res://shaders/gas_giant.gdshader") },
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,Position=new Vector3(-6500,2800,11000),Rotation=new Vector3(.25f,0,.3f) };
        _stage.AddChild(_planet);
        GD.Print($"TEASER shot {shot}: {ShotNames[shot]}");
    }
    private void Sound(string name,float gain=1,float pitch=1,float pan=0)
        => _sounds.Add(new SoundCue(_clock,name,gain,pitch,pan));
    private bool At(int id,float time) => _local>=time && _events.Add(id);
    private static readonly Vector3 BoltOrigin=new(1600,280,-850);
    private void AddBolt(float arrival,float energy,float penetration,float damage,Vector3 aim)
    {
        var head=CombatFx.Make(_stage,CombatFx.Quad,_boltLight); head.Visible=false;
        var tail=CombatFx.Make(_stage,CombatFx.Quad,_boltTrail); tail.Visible=false;
        _bolts.Add(new(arrival,energy,penetration,damage,aim,head,tail));
    }
    private void Impact(float energy,float penetration,float damage,Vector3 point)
    {
        Vector3 direction=(point-BoltOrigin).Normalized();
        float before=_hero.Damage.Shield;
        var hit=DamageRay.Apply(_hero,_hero.Position+Vec3d.From(BoltOrigin),direction,
            new DamagePacket(energy,penetration,damage,5000),_world.Time,++_impact);
        _world.RecordImpact(_impact,_other,hit,_world.Time,direction,energy,before,BattleWeapon.Railgun);
        Sound(hit.ShieldStopped?"shield_impact":hit.Modules.Count>0?"hull_penetration":"armor_block",.7f,.8f,-.15f);
    }
    private void Act()
    {
        if(_shot==1)
        {
            Vector3 aim=new Vector3(.38f,.1f,-1).Normalized();
            foreach(var gun in _hero.Railguns) gun.Aim(aim);
            if(_local>=1.6f && !_fired)
            {
                var result=_world.FireRailguns(_hero,aim);
                if(result.Fired) { _fired=true; Sound("railgun_fire",1,.78f,.15f); GD.Print($"TEASER battery: {result.Shots} rounds"); }
            }
            if(_local>=4.7f && _fired && At(9,4.7f)) { _world.FireRailguns(_hero,aim); Sound("railgun_fire",.8f,.8f,-.25f); }
        }
        if(_shot==2)
        {
            // Choreographed flyby, using the ordinary point-defense tracking and impact path.
            if(_hero.Damage.Shield<60 && !_hero.Damage.Destroyed) _hero.Damage.Reset();
            if(At(1,1.2f)) { _world.FireRailgun(_hero,Vector3.Forward); Sound("railgun_fire",.4f,1.15f,-.5f); }
            if(At(2,3.3f)) { _world.FireRailgun(_hero,Vector3.Forward); Sound("railgun_fire",.5f,1.15f,.4f); }
        }
        if(_shot==3)
        {
            for(int i=0;i<_bolts.Count;i++) { var bolt=_bolts[i];
                if(At(i,bolt.Arrival)) Impact(bolt.Energy,bolt.Penetration,bolt.Damage,bolt.Aim); }
        }
        if(_shot==4)
        {
            if(_local>=1.15f && !_launched)
            {
                _world.Sensors.Update(_world.Ships,_world.Time,force:true);
                var result=_world.LaunchAntimatter(_hero,_other);
                if(result.Fired) {
                    _launched=true; _launchAt=_local; _torpedo=_world.Missiles.Single(m=>m.Assault is not null);
                    _launchCamera=_camera.Position; _launchLook=_cameraLook;
                    Sound("railgun_fire",.8f,.6f); GD.Print("TEASER AM launched"); }
                else if(_local>2) throw new InvalidOperationException("Teaser AM launch failed: "+result.Reason);
            }
        }
    }
    private void AdvanceWorld()
    {
        float speed=1;
        if(_shot==4 && _launched && _impactAt<0 && _torpedo is not null)
            speed=SimWorld.HullDistance(_other,_torpedo.Position)<600?.3f:.72f;
        if(_impactAt>=0 && _local-_impactAt<.5f) speed=.4f;
        _simCredit+=speed/30.0;
        while(_simCredit>=SimWorld.TickDelta) { _world.Step(); _simCredit-=SimWorld.TickDelta; }
        _alpha=(float)(_simCredit/SimWorld.TickDelta);
        if(_shot!=4 || !_launched || _impactAt>=0) return;
        var contact=_world.Impacts.FirstOrDefault(i=>i.Weapon==BattleWeapon.Antimatter && i.Shooter==_hero && i.Hit.Target==_other && i.Hit.HullHit);
        if(contact is not null)
        {
            _impactAt=_local; _impactPoint=contact.Hit.Point.ToVector3(); _impactCamera=_camera.Position; _impactLook=_cameraLook;
            // The decorative armor sits outside the simplified damage boxes. Put the
            // filmed flash slightly back along the incoming path so the armor cannot hide it.
            _impactSurface=_impactPoint-contact.IncomingDirection*80;
            // Only an observed, physical AM hull hit can trigger this staged two-piece ending.
            if(!_other.Damage.Destroyed) _other.Damage.Breakup(_world.Time,"cinematic AM hull contact");
            if(_other.Wreck?.Pieces.Count!=2) throw new InvalidOperationException("Cinematic hit must leave two hull pieces");
            Sound("critical_impact",1.1f,.6f); Sound("hull_penetration",.7f,.75f,-.3f);
            GD.Print($"TEASER AM hull hit at {_local:F3}s / {_impactPoint}; continuous breakup=2");
        }
        else if(_torpedo is not null && !_world.Missiles.Contains(_torpedo))
            throw new InvalidOperationException("Filmed AM disappeared without a hull hit");
    }
    private void UpdateCamera()
    {
        float u=Mathf.Clamp(_local/Durations[_shot],0,1),s=u*u*(3-2*u);
        Vector3 position=Vector3.Zero,look=Vector3.Zero; _camera.Fov=46;
        Vector3 hero=_hero.InterpolatedPosition(_alpha).ToVector3();
        switch(_shot)
        {
            case 0: position=hero+new Vector3(1700,550,-1650).Lerp(new Vector3(1250,360,-1330),s); look=hero+new Vector3(30,0,80); break;
            case 1: position=hero+new Vector3(285,160,-535).Lerp(new Vector3(345,210,-345),s); look=hero+new Vector3(0,105,-270); _camera.Fov=54; break;
            case 2: position=hero+new Vector3(-35,22,65).Lerp(new Vector3(-48,28,80),s); look=hero+new Vector3(0,5,-15); _camera.Fov=62; break;
            case 3: position=hero+new Vector3(1250,570,-1420).Lerp(new Vector3(1150,490,-1250),s); look=hero+new Vector3(170,25,-30); _camera.Fov=55; break;
            case 4:
                TorpedoCamera(hero,out position,out look); break;
            case 5: position=hero+new Vector3(1100,480,2000).Lerp(new Vector3(1350,600,2500),s); look=hero+new Vector3(-200,160,150); _camera.Fov=52; break;
        }
        _camera.Position=position; _camera.LookAt(look); _cameraLook=look;
        if(_shot is 2 or 5) _planet.Position=new Vector3(-9500,3500,-15000);
        _overlay.Shot=_shot; _overlay.Time=_local; _overlay.Duration=Durations[_shot]; _overlay.QueueRedraw();
    }
    private void TorpedoCamera(Vector3 hero,out Vector3 position,out Vector3 look)
    {
        _camera.Fov=55;
        var bodyBasis=new Basis(_hero.Orientation);
        position=hero+bodyBasis*new Vector3(25,13,52); look=hero+_hero.Forward*15;
        if(!_launched || _torpedo is null) return;
        if(_impactAt>=0)
        {
            float age=_local-_impactAt, pull=Mathf.SmoothStep(0,1,Mathf.Clamp(age/2.4f,0,1));
            position=_impactCamera.Lerp(_other.Position.ToVector3()+new Vector3(1450,550,950),pull);
            look=_impactLook.Lerp(_other.Position.ToVector3(),pull); _camera.Fov=Mathf.Lerp(55,52,pull); return;
        }
        Vector3 at=Vec3d.Lerp(_torpedo.PrevPosition,_torpedo.Position,_alpha).ToVector3();
        var frame=Basis.LookingAt(_torpedo.Velocity.Normalized());
        float reach=(float)SimWorld.HullDistance(_other,Vec3d.From(at));
        float reveal=1-Mathf.SmoothStep(0,1,Mathf.Clamp((reach-90)/510,0,1));
        Vector3 chase=at+frame*new Vector3(11,5,28).Lerp(new Vector3(230,110,380),reveal);
        Vector3 aim=at+_torpedo.Velocity.Normalized()*Mathf.Lerp(8,0,reveal);
        float handoff=Mathf.SmoothStep(0,1,Mathf.Clamp((_local-_launchAt)/.85f,0,1));
        position=_launchCamera.Lerp(chase,handoff); look=_launchLook.Lerp(aim,handoff);
    }
    private void SyncProjectiles()
    {
        foreach(var bolt in _bolts)
        {
            float remaining=bolt.Arrival-_local; bolt.Head.Visible=bolt.Tail.Visible=remaining>0 && remaining<.7f;
            if(!bolt.Head.Visible) continue;
            Vector3 direction=(bolt.Aim-BoltOrigin).Normalized();
            DamageRay.FirstDefenseHitAtPose(_hero,Vec3d.From(BoltOrigin),direction,5000,_hero.Position,_hero.Orientation,out float distance);
            Vector3 head=BoltOrigin+direction*distance*(1-remaining/.7f);
            CombatFx.Flare(bolt.Head,_camera,head,1.5f,18);
            CombatFx.Streak(bolt.Tail,_camera,head,head-direction*150,CombatFx.PixelSize(_camera,head,3.2f));
        }
        if(_amHead is null || _amTail is null) return;
        if(_contactFlash is not null && _contactLight is not null)
        {
            float age=_local-_impactAt;
            bool active=_impactAt>=0 && age<.7f; _contactFlash.Visible=_contactLight.Visible=active;
            if(active) {
                float pulse=Mathf.Exp(-age*9); _contactMaterial!.SetShaderParameter("strength",pulse*5);
                CombatFx.Flare(_contactFlash,_camera,_impactSurface,350+age*160,180*(1-age/.7f));
                _contactLight.Position=_impactSurface; _contactLight.LightEnergy=22*pulse; }
        }
        _amHead.Visible=_amTail.Visible=_launched && _impactAt<0;
        if(!_amHead.Visible || _torpedo is null) return;
        Vector3 at=Vec3d.Lerp(_torpedo.PrevPosition,_torpedo.Position,_alpha).ToVector3();
        CombatFx.Flare(_amHead,_camera,at,.7f,12);
        CombatFx.Streak(_amTail,_camera,at,at-_torpedo.Velocity.Normalized()*18,CombatFx.PixelSize(_camera,at,2));
    }
    private void Sync()
    {
        foreach(var view in _views) { view.Sync(Vec3d.Zero,_alpha,1f/30); view.SyncCombat(_world,_camera); }
        _rails.Sync(_world,Vec3d.Zero,_alpha,_camera,_views); _ordnance.Sync(_world,Vec3d.Zero,_alpha,_camera.Position,_camera);
        if(_shot==4 && _impactAt>=0)
        {
            var wreck=_views.First(v=>v.Body==_other).Wreck;
            float age=(float)(_other.SimTime-_other.Damage.DestroyedAt);
            for(int i=0;i<wreck.LocalExplosionTimes.Count;i++)
                if(age>=wreck.LocalExplosionTimes[i] && _events.Add(2000+i))
                    Sound("armor_block",.16f,.65f+(i%3)*.08f,i%2==0?-.35f:.35f);
            if(age>=wreck.MainExplosionAt && _events.Add(2100)) Sound("critical_impact",.65f,.7f);
        }
        SyncProjectiles();
    }
    public override void _Process(double delta)
    {
        if(_saving) return;
        if(!_preview && _frame>=Durations[_shot]*30)
        {
            if(_shot==4 && _impactAt<0) throw new InvalidOperationException("Teaser cannot cut away before the filmed AM hull hit");
            if(_shot==ShotNames.Length-1) { _saving=true; Finish(); return; }
            Enter(_shot+1);
        }
        Act(); AdvanceWorld(); UpdateCamera(); Sync();
        _frame++; _local=_frame/30f; _clock+=1f/30;
        if(_preview)
        {
            float at=float.Parse(Options.GetValueOrDefault("teaser-time","2.5"),CultureInfo.InvariantCulture);
            if(_local>=at) { _saving=true; SavePreview(); }
        }
    }
    private async void SavePreview()
    {
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        string path=Options.GetValueOrDefault("shot","res://shots/teaser_preview.png");
        var error=GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"TEASER preview {path}: {error}"); GetTree().Quit(error==Error.Ok?0:1);
    }
    private void Finish()
    {
        if(Options.TryGetValue("teaser-cues",out string? path)) {
            using var file=FileAccess.Open(path,FileAccess.ModeFlags.Write);
            file.StoreString(JsonSerializer.Serialize(new { duration=Durations.Sum(),fps=30,shots=ShotNames,durations=Durations,cues=_sounds },new JsonSerializerOptions { WriteIndented=true })); }
        GD.Print("TEASER complete: 38 seconds / 1140 frames"); GetTree().Quit();
    }
}

public partial class TeaserOverlay : Control
{
    public int Shot;
    public float Time,Duration;
    private readonly SystemFont _font=new() { FontNames=new[]{"Malgun Gothic"},FontWeight=400 };
    private readonly SystemFont _title=new() { FontNames=new[]{"Bahnschrift","Segoe UI"},FontWeight=600 };
    private static readonly Color Ice=new(.75f,.88f,1),White=new(.94f,.96f,1);
    public override void _Ready() { SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); MouseFilter=MouseFilterEnum.Ignore; }
    public override void _Draw()
    {
        Vector2 size=GetViewportRect().Size; float scale=size.X/1920,bar=size.Y*.095f;
        DrawRect(new Rect2(0,0,size.X,bar),Colors.Black); DrawRect(new Rect2(0,size.Y-bar,size.X,bar),Colors.Black);
        void Text(Font font,string value,float x,float y,int points,Color color)
            => DrawString(font,new Vector2(x*scale,y*scale),value,HorizontalAlignment.Left,-1,(int)(points*scale),color);
        Text(_title,"S P A C E   F L E E T",82,67,20,Ice);
        Text(_font,"개발 중 · 실제 엔진 연출",1480,67,18,new Color(.55f,.61f,.68f));
        if(Shot==0)
        {
            float a=Mathf.SmoothStep(0,1,Time/.9f)*Mathf.Clamp((Duration-Time)/.6f,0,1);
            Text(_title,"COMMAND THE VOID",84,825,21,new Color(Ice,a));
            Text(_font,"전장을 지휘하라",80,889,48,new Color(White,a));
            DrawLine(new Vector2(84,776)*scale,new Vector2(190,776)*scale,new Color(Ice,a),2*scale);
        }
        if(Shot==2)
        {
            float a=Mathf.Clamp(Time/.5f,0,1)*Mathf.Clamp((Duration-Time)/.5f,0,1);
            Text(_title,"BREAK THE LINE",84,825,21,new Color(Ice,a));
            Text(_font,"사각으로 파고들어라",80,889,43,new Color(White,a));
        }
        if(Shot==4 && Time<1.5f)
            Text(_title,"ANTIMATTER  /  ARMED",84,886,24,Ice);
        if(Shot==5)
        {
            float a=Mathf.Clamp(Time/.8f,0,1);
            string name="S P A C E  F L E E T";
            float width=_title.GetStringSize(name,HorizontalAlignment.Left,-1,(int)(91*scale)).X;
            DrawString(_title,new Vector2((size.X-width)*.5f,490*scale),name,HorizontalAlignment.Left,-1,(int)(91*scale),new Color(White,a));
            string line="함대를 지휘하고, 직접 돌파하라.";
            float sub=_font.GetStringSize(line,HorizontalAlignment.Left,-1,(int)(26*scale)).X;
            DrawString(_font,new Vector2((size.X-sub)*.5f,560*scale),line,HorizontalAlignment.Left,-1,(int)(26*scale),new Color(Ice,a));
            Text(_title,"I N   D E V E L O P M E N T",740,875,23,new Color(White,a));
        }
        float fade=Shot==0?Mathf.Clamp(1-Time/.8f,0,1):Shot==5?Mathf.Clamp((Time-Duration+1.1f)/1.1f,0,1):0;
        if(fade>0) DrawRect(new Rect2(Vector2.Zero,size),new Color(0,0,0,fade));
    }
}
