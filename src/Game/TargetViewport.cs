using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>One reused, low-cost target viewport. Only observation snapshots enter
/// its pose/state. An unclassified contact never instantiates a ship model.</summary>
public partial class TargetViewport : Control
{
    public ScaleTest Game { get; set; }=null!;
    internal bool PreviewEnabled {get;set;}=true;
    private SubViewport _viewport=null!;
    private Node3D _world=null!,_model=null!;
    private Camera3D _camera=null!;
    private Font _font=null!;
    private string? _modelId;
    private bool _poseReady;
    private ShipBody? _target;
    private float _length;
    private Vector3 _focus;
    private double _next;
    private readonly List<(ExternalPart Part,Vector2 Point,bool Selectable,bool Destroyed)> _markers=new();
    private ContactSnapshot? _contact;
    private string _hover="";
    private static readonly Color Ink=new(.78f,.88f,.94f),Dim=new(.51f,.65f,.72f),Gold=new(1,.8f,.35f);
    internal string? LoadedModel=>_modelId;
    internal bool ModelVisible=>_model is {Visible:true};
    internal Vector2? PartScreenPoint(string id)=>_markers.FirstOrDefault(m=>m.Part.Id==id&&m.Selectable).Part is not null?_markers.First(m=>m.Part.Id==id).Point:null;
    internal Transform3D CameraTransform=>_camera.Transform;
    internal int SelectableCount=>_markers.Count(m=>m.Selectable);
    public override void _Ready()
    {
        MouseFilter=MouseFilterEnum.Stop;ClipContents=true;
        _font=new SystemFont{FontNames=new[]{"Malgun Gothic","Segoe UI"}};
        _viewport=new SubViewport{Size=new(304,178),OwnWorld3D=true,TransparentBg=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled};AddChild(_viewport);
        _world=new();_viewport.AddChild(_world);
        _world.AddChild(new WorldEnvironment{Environment=new Godot.Environment{BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new(0,0,0,0),
            AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=Ink,AmbientLightEnergy=.9f}});
        _world.AddChild(new DirectionalLight3D{RotationDegrees=new(-35,-25,0),LightEnergy=1.5f,ShadowEnabled=false});
        _camera=new Camera3D{Projection=Camera3D.ProjectionType.Orthogonal};_world.AddChild(_camera);_camera.MakeCurrent();
        MouseExited+=()=>{_hover="";QueueRedraw();};
    }
    public override void _Process(double delta)
    {
        var screen=GetViewportRect().Size;Position=new(screen.X-320,8);Size=new(308,284+Mathf.Ceil((HudSettings.TextScale-1)*45));
        Visible=PreviewEnabled&&!Game.MenuOpen&&!Game.WorldMapOpen&&!Game.RosterOpen&&!Game.ShowHelp&&!Game.Spectating&&Game.InspectTarget is not null&&Game.SelectedFriendly is null;
        if(!Visible){_viewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled;return;}
        if(Time.GetTicksMsec()*.001<_next)return;_next=Time.GetTicksMsec()*.001+.1;
        long perf=PerfTrace.Begin();Refresh();PerfTrace.End("targetPreview",perf);QueueRedraw();
    }
    internal void Refresh()
    {
        _markers.Clear();var view=Game.InspectTarget;if(view is null||Game.Controlled is not {} me)return;
        _contact=Game.ContactOf(view);
        if(_target!=view.Body){_target=view.Body;_hover="";_poseReady=false;}
        if(_contact is null||(!_contact.SignalLost&&_contact.Track.Level<TrackLevel.Identified)||!_contact.Identified)
        {if(_model is not null)_model.Visible=false;_viewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled;return;}
        // A class identification reveals its public hull shape, not hidden refits/HP.
        var def=ShipDefinitions.ById(view.Body.Definition.Id);
        if(_modelId!=def.ModelId)
        {
            if(_model is not null){_world.RemoveChild(_model);_model.QueueFree();}
            _modelId=def.ModelId;_length=def.Flight.Length;
            _model=GD.Load<PackedScene>($"res://assets/ships/{def.ModelId}.glb").Instantiate<Node3D>();_world.AddChild(_model);
            var material=new StandardMaterial3D{AlbedoColor=new(.21f,.39f,.49f),Roughness=1,Metallic=0};
            Aabb bounds=default;bool first=true;
            foreach(var mesh in ImportedShipModels.Meshes(_model))
            {
                mesh.MaterialOverride=material;mesh.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
                for(int i=0;i<8;i++){var p=mesh.GlobalTransform*mesh.GetAabb().GetEndpoint(i);bounds=first?new(p,Vector3.Zero):bounds.Expand(p);first=false;}
            }
            _focus=bounds.GetCenter();_length=Math.Max(_length,bounds.Size.Length());
        }
        _model.Visible=true;
        bool render=!_contact.SignalLost||!_poseReady;
        if(render)
        {
            _poseReady=true;
            var track=_contact.Track;
            var direction=track.ObservedOrientation.Inverse()*((_contact.SignalLost?track.ObservationOrigin:me.Body.Position)-track.EstimatedPosition).ToVector3().Normalized();
            if(direction.LengthSquared()<.1f)direction=Vector3.Back;
            _camera.Position=_focus+direction*_length*3;
            _camera.Near=_length*.001f;_camera.Far=_length*8;_camera.Size=_length*.78f;
            _camera.LookAt(_focus,Math.Abs(direction.Dot(Vector3.Up))>.98f?Vector3.Forward:Vector3.Up);
            var model=ExternalTargets.For(def);
            if(!_contact.SignalLost&&track.Level==TrackLevel.Locked&&model is not null)
                for(int i=0;i<model.Parts.Length;i++)
                {
                    var p=model.Parts[i];if(!track.PartVisible(i)||!model.Visible(p,direction*_length*3,out var surface,out _,precision:false))continue;
                    bool can=ExternalTargets.TryAim(me.Body,view.Body,track,Game.World.Time,p.Id,out var aim,obstacles:Game.World.Obstacles);
                    // Destroyed external parts stay a subdued, observed X, never a live HP read.
                    var point=_camera.UnprojectPosition(can?aim.Point:surface)+new Vector2(2,70);
                    if(new Rect2(4,72,300,174).HasPoint(point))_markers.Add((p,point,can,track.PartDestroyed(i)));
                }
        }
        _viewport.RenderTargetUpdateMode=render?SubViewport.UpdateMode.Once:SubViewport.UpdateMode.Disabled;
    }
    public override void _GuiInput(InputEvent e)
    {
        if(e is InputEventMouseMotion motion)
        {
            var near=_markers.OrderBy(m=>m.Point.DistanceSquaredTo(motion.Position)).FirstOrDefault();
            _hover=near.Part is not null&&near.Point.DistanceTo(motion.Position)<14?near.Part.Label+(near.Selectable?"":" · 정밀 조준 불가"):"";QueueRedraw();
        }
        if(e is InputEventMouseButton {Pressed:true,ButtonIndex:MouseButton.Left} click)
        {
            var near=_markers.Where(m=>m.Selectable).OrderBy(m=>m.Point.DistanceSquaredTo(click.Position)).FirstOrDefault();
            if(near.Part is not null&&near.Point.DistanceTo(click.Position)<14)Game.SelectExternalTarget(near.Part.Id);
            else if(click.Position.Y>=70&&click.Position.Y<250)Game.SelectExternalTarget(null);
            AcceptEvent();
        }
    }
    private void Text(Vector2 at,string text,int size,Color color,float width=286)
        =>DrawString(_font,at,HudText.Fit(_font,text,HudSettings.FontSize(size),width),fontSize:HudSettings.FontSize(size),modulate:color);
    public override void _Draw()
    {
        if(_contact is not {} c||Game.Controlled is not {} me)return;
        DrawRect(new(Vector2.Zero,Size),new Color(.01f,.025f,.035f,.88f));
        bool identified=c.SignalLost?c.Identified:c.Track.Level>=TrackLevel.Identified;
        Text(new(10,23),identified?c.Name:"미식별 접촉",14,c.SignalLost?Dim:Ink,180);
        Text(new(205,23),Hud.FormatDistance((c.Position-me.Body.Position).Length()),12,Dim,96);
        string state=c.SignalLost?"신호 소실 · 마지막 관측":c.Track.ObservedDestroyed?"격침 확인":c.Track.ObservedDisabled?"무력화 확인":c.Track.Level switch{TrackLevel.Locked=>"잠금",TrackLevel.Identified=>"식별 · 잠금 대기",_=>"접촉 · 외형 미확인"};
        Text(new(10,52),state,12,c.SignalLost?Dim:Ink);
        if(identified)
        {
            DrawRect(new(10,61,286,3),new Color(Dim,.2f));
            DrawRect(new(10,61,286*Math.Clamp(c.Track.ObservedShieldFraction,0,1),3),c.SignalLost?Dim:Ink);
        }
        if(identified&&_model is {Visible:true})DrawTextureRect(_viewport.GetTexture(),new Rect2(2,70,304,178),false,c.SignalLost?new Color(.6f,.6f,.6f,.65f):Colors.White);
        else
        {
            // Same non-identifying signal for every hull; no GLB blur or class-sized outline.
            for(int i=5;i>0;i--)DrawCircle(new(154,152),i*7,new Color(.55f,.72f,.76f,.025f*(6-i)));
            DrawArc(new(154,152),45,0,Mathf.Tau,40,new Color(Dim,.25f),1);
            Text(new(44,225),"위치 오차 "+c.Track.ErrorMeters.ToString("0")+"m",11,Dim,230);
        }
        foreach(var marker in _markers)
        {
            Color color=Game.SelectedExternalId==marker.Part.Id?Gold:marker.Selectable?Ink:Dim;
            var p=marker.Point;
            if(marker.Destroyed){DrawLine(p-new Vector2(4,4),p+new Vector2(4,4),Dim);DrawLine(p+new Vector2(-4,4),p+new Vector2(4,-4),Dim);continue;}
            DrawPolyline(new[]{p+new Vector2(0,-5),p+new Vector2(5,0),p+new Vector2(0,5),p+new Vector2(-5,0),p+new Vector2(0,-5)},color,1.3f);
        }
        string caption=c.SignalLost?"관측 갱신 대기":_hover.Length>0?_hover:Game.SelectedExternalAim?.Part.Label
            ??(Game.SelectedExternalId is not null?"선체 조준 · 부위 잠금 대기":"선체 전체");
        Text(new(10,Size.Y-14),caption,12,Gold,226);
        Text(new(252,Size.Y-14),InputBindings.Text(InputSetup.AimPart),12,Dim,48);
    }
}
