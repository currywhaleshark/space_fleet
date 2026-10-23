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
    private const float Width=302,Pad=13;
    // The model renders at 2× and is drawn into the 276×176 viewer pane (prototype .viewer-pane).
    private static readonly Vector2 ViewerSize=new(276,176);
    private const int Supersample=2;
    private SubViewport _viewport=null!;
    private Node3D _world=null!,_model=null!;
    private Camera3D _camera=null!;
    private Font _mono=null!;
    private GradientTexture2D _viewerBack=null!,_contactBack=null!,_blob=null!;
    private ShaderMaterial _hologram=null!;
    private string? _modelId;
    private bool _poseReady;
    private ShipBody? _target;
    private float _length;
    private Vector3 _focus,_observer=Vector3.Back;
    private double _next;
    private readonly List<(ExternalPart Part,Vector2 Point,bool Selectable,bool Destroyed)> _markers=new();
    private ContactSnapshot? _contact;
    private string _hover="";
    private static readonly Color Gold=HudStyle.Rgb(0xe9c58a),Marker=HudStyle.Rgb(0x72afc1);
    internal string? LoadedModel=>_modelId;
    internal bool ModelVisible=>_model is {Visible:true};
    internal Vector2? PartScreenPoint(string id)=>_markers.FirstOrDefault(m=>m.Part.Id==id&&m.Selectable).Part is not null?_markers.First(m=>m.Part.Id==id).Point:null;
    internal Transform3D CameraTransform=>_camera.Transform;
    internal int SelectableCount=>_markers.Count(m=>m.Selectable);
    private float Grow=>Mathf.Ceil((HudSettings.TextScale-1)*40);
    private Rect2 Viewer=>new(Pad,60+Grow*.5f,ViewerSize.X,ViewerSize.Y);
    public override void _Ready()
    {
        MouseFilter=MouseFilterEnum.Stop;ClipContents=true;
        _mono=HudStyle.Mono;
        _viewerBack=Radial(new(.48f,.38f),new[]{0f,.44f,.85f},new[]{HudStyle.Rgb(0x19323c),HudStyle.Rgb(0x0a1824),HudStyle.Rgb(0x050e18)});
        _contactBack=Radial(new(.5f,.5f),new[]{0f,.7f},new[]{HudStyle.Rgb(0x243b3e),HudStyle.Rgb(0x091723)});
        _blob=Radial(new(.5f,.5f),new[]{0f,.45f,1f},new[]{Colors.White,new Color(1,1,1,.45f),new Color(1,1,1,0)});
        _hologram=new ShaderMaterial{Shader=GD.Load<Shader>("res://shaders/target_hologram.gdshader")};
        _viewport=new SubViewport{Size=(Vector2I)(ViewerSize*Supersample),OwnWorld3D=true,TransparentBg=true,Msaa3D=Viewport.Msaa.Msaa2X,
            RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled};AddChild(_viewport);
        _world=new();_viewport.AddChild(_world);
        _world.AddChild(new WorldEnvironment{Environment=new Godot.Environment{BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new(0,0,0,0)}});
        _camera=new Camera3D{Projection=Camera3D.ProjectionType.Orthogonal};_world.AddChild(_camera);_camera.MakeCurrent();
        MouseExited+=()=>{_hover="";QueueRedraw();};
    }
    private static GradientTexture2D Radial(Vector2 center,float[] offsets,Color[] colors)=>new()
    {
        Width=128,Height=128,Fill=GradientTexture2D.FillEnum.Radial,FillFrom=center,FillTo=center+new Vector2(.5f,0),
        Gradient=new Gradient{Offsets=offsets,Colors=colors},
    };
    public override void _Process(double delta)
    {
        var screen=GetViewportRect().Size;Position=new(screen.X-23-Width,20);Size=new(Width,320+Grow);
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
            Aabb bounds=default;bool first=true;
            foreach(var mesh in ImportedShipModels.Meshes(_model))
            {
                mesh.MaterialOverride=_hologram;mesh.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
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
            _observer=direction;
            _camera.Position=_focus+direction*_length*3;
            _camera.Near=_length*.001f;_camera.Far=_length*8;_camera.Size=_length*.62f;
            _camera.LookAt(_focus,Math.Abs(direction.Dot(Vector3.Up))>.98f?Vector3.Forward:Vector3.Up);
            var model=ExternalTargets.For(def);
            var viewer=Viewer;
            if(!_contact.SignalLost&&track.Level==TrackLevel.Locked&&model is not null)
                for(int i=0;i<model.Parts.Length;i++)
                {
                    var p=model.Parts[i];if(!track.PartVisible(i)||!model.Visible(p,direction*_length*3,out var surface,out _,precision:false))continue;
                    bool can=ExternalTargets.TryAim(me.Body,view.Body,track,Game.World.Time,p.Id,out var aim,obstacles:Game.World.Obstacles);
                    // Destroyed external parts stay a subdued, observed X, never a live HP read.
                    var point=viewer.Position+_camera.UnprojectPosition(can?aim.Point:surface)/Supersample;
                    if(viewer.Grow(-4).HasPoint(point))_markers.Add((p,point,can,track.PartDestroyed(i)));
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
            else if(Viewer.HasPoint(click.Position))Game.SelectExternalTarget(null);
            AcceptEvent();
        }
    }
    private void Text(Vector2 at,string text,int size,Color color,float width=-1,HorizontalAlignment align=HorizontalAlignment.Left)
        =>DrawString(_mono,at,HudText.Fit(_mono,text,HudSettings.FontSize(size),width),align,width,HudSettings.FontSize(size),color);
    /// <summary>Relative aspect from the observed pose: which face of the target this sensor sees.</summary>
    internal static string Aspect(Vector3 d)
        =>(Math.Abs(d.Z)>=Math.Abs(d.X)?d.Z<0?"함수":"함미":d.X>0?"우현":"좌현")+" · "+(d.Y>.35f?"상부":d.Y<-.35f?"하부":"측면");
    public override void _Draw()
    {
        if(_contact is not {} c||Game.Controlled is not {} me)return;
        var box=new Rect2(Vector2.Zero,Size);
        var top=HudStyle.PaneTop;var bottom=HudStyle.PaneBottom;
        DrawPolygon(new[]{box.Position,new(box.End.X,0),box.End,new(0,box.End.Y)},new[]{top,top.Lerp(bottom,.5f),bottom,top.Lerp(bottom,.5f)});
        DrawRect(box.Grow(-.5f),HudStyle.Line,false,1);
        bool lost=c.SignalLost,identified=lost?c.Identified:c.Track.Level>=TrackLevel.Identified,locked=!lost&&c.Track.Level==TrackLevel.Locked;
        int stage=lost?0:c.Track.Level switch{TrackLevel.Locked=>3,TrackLevel.Identified=>2,_=>1};
        float g=Grow*.5f;

        // Head: name, lock glyph, distance and the three-step sensor grade.
        Text(new(Pad,28),identified?c.Name:"미식별 접촉",16,lost?HudStyle.Dim:HudStyle.Rgb(0xe1eaf0),Width-60);
        var lockColor=locked?HudStyle.Amber:new Color(HudStyle.Dim,.7f);
        DrawTextureRect(HudStyle.Icon("aim",17),new Rect2(Width-Pad-17,13,17,17),false,lockColor);
        string range=Hud.FormatDistance((c.Position-me.Body.Position).Length());
        int space=range.LastIndexOf(' ');
        Text(new(Pad,50+g),range[..space],13,HudStyle.Rgb(0xc4d9e5));
        Text(new(Pad+_mono.GetStringSize(range[..space]+" ",fontSize:HudSettings.FontSize(13)).X,50+g),range[(space+1)..],13,new Color(HudStyle.Rgb(0xc4d9e5),.6f));
        for(int i=0;i<3;i++)DrawRect(new Rect2(Width-Pad-23*3-10+i*28,45+g,23,3),i<stage?HudStyle.Cyan:HudStyle.Rgb(0x416176));

        DrawViewer(c,identified,locked,lost);

        // Aim line: the current exterior choice, Y cycles visible parts only.
        var viewer=Viewer;float y=viewer.End.Y+5;float alpha=locked?1:.55f;
        DrawLine(new(Pad,y),new(Width-Pad,y),new Color(HudStyle.Rgb(0x679eb5),.16f),1);
        string aimText=lost?"관측 갱신 대기":!identified?"신호 접촉 · 외형 미확인":!locked?"형상 식별 · 잠금 대기"
            :"◇ "+(Game.SelectedExternalAim?.Part.Label??(Game.SelectedExternalId is not null?"선체 · 부위 잠금 대기":"선체"));
        Text(new(Pad+7,y+16),aimText,11,new Color(HudStyle.Rgb(0xe3b37a),alpha),Width-Pad*2-40);
        string key=locked?InputBindings.Text(InputSetup.AimPart):"—";
        float kw=Math.Max(16,_mono.GetStringSize(key,fontSize:HudSettings.FontSize(10)).X+10);
        var kbd=new Rect2(Width-Pad-7-kw,y+5,kw,15);
        DrawRect(kbd,new Color(HudStyle.Rgb(0x324d5a),alpha),false,1);
        Text(new(kbd.Position.X,y+16),key,10,new Color(HudStyle.Rgb(0x7b9ead),alpha),kw,HorizontalAlignment.Center);

        // Observed shield only; structure is never observed, so it stays an unknown hatch.
        y+=23+8+Grow*.5f;float barX=Pad+15+8,barW=Width-Pad*2-15-8-37-8;
        float rows=identified?1:.13f;
        DrawTextureRect(HudStyle.Icon("shield",15),new Rect2(Pad,y,15,15),false,new Color(HudStyle.Dim,rows));
        var shieldBar=new Rect2(barX,y+5,barW,5);
        if(identified){DrawRect(shieldBar,HudStyle.Track);DrawRect(new Rect2(shieldBar.Position,new(barW*Math.Clamp(c.Track.ObservedShieldFraction,0,1),5)),lost?HudStyle.Dim:HudStyle.Cyan);}
        else DrawRect(shieldBar,new Color(HudStyle.Track,rows));
        Text(new(Width-Pad-37,y+11),identified?$"≈{c.Track.ObservedShieldFraction*100:0}":"?",11,new Color(HudStyle.Rgb(0x9cb4c1),identified?1:.4f),37,HorizontalAlignment.Right);
        y+=21;
        DrawTextureRect(HudStyle.Icon("hull",15),new Rect2(Pad,y,15,15),false,new Color(HudStyle.Dim,rows));
        var hullBar=new Rect2(barX,y+5,barW,5);DrawRect(hullBar,new Color(HudStyle.Track,identified?1:.13f));
        if(identified)for(float x=hullBar.Position.X;x<hullBar.End.X;x+=5)DrawLine(new(x,hullBar.End.Y),new(Mathf.Min(x+3,hullBar.End.X),hullBar.Position.Y),new Color(HudStyle.Dim,.4f),1);
        string status=c.Track.ObservedDestroyed?"격침":c.Track.ObservedDisabled?"무력화":"—";
        Text(new(Width-Pad-37,y+11),status,11,new Color(c.Track.ObservedDestroyed||c.Track.ObservedDisabled?HudStyle.Red:HudStyle.Rgb(0x9cb4c1),identified?1:.4f),37,HorizontalAlignment.Right);
    }
    private void DrawViewer(ContactSnapshot c,bool identified,bool locked,bool lost)
    {
        var viewer=Viewer;
        DrawTextureRect(identified?_viewerBack:_contactBack,viewer,false);
        if(identified&&_model is {Visible:true})
            DrawTextureRect(_viewport.GetTexture(),viewer,false,lost?new Color(.6f,.65f,.7f,.6f):new Color(1,1,1,locked?.96f:.83f));
        else DrawContactSignal(viewer,c);
        // Scanlines and the small sight ticks: a tactical reconstruction, not a free model viewer.
        for(float y=viewer.Position.Y+3;y<viewer.End.Y;y+=4)DrawLine(new(viewer.Position.X,y),new(viewer.End.X,y),new Color(.59f,.87f,1f,.024f),1);
        var tick=new Color(HudStyle.Rgb(0x6eaac2),.2f);var mid=viewer.GetCenter();
        DrawLine(new(viewer.Position.X,mid.Y-6),new(viewer.Position.X,mid.Y+6),tick,1);DrawLine(new(viewer.End.X-1,mid.Y-6),new(viewer.End.X-1,mid.Y+6),tick,1);
        DrawLine(new(mid.X,viewer.Position.Y+6),new(mid.X,viewer.Position.Y+14),tick,1);DrawLine(new(mid.X,viewer.End.Y-6),new(mid.X,viewer.End.Y-14),tick,1);
        foreach(var marker in _markers)
        {
            bool chosen=Game.SelectedExternalId==marker.Part.Id,hovered=_hover.StartsWith(marker.Part.Label);
            var p=marker.Point;
            if(marker.Destroyed){var x=new Color(HudStyle.Dim,.8f);DrawLine(p-new Vector2(4,4),p+new Vector2(4,4),x,1.2f);DrawLine(p+new Vector2(-4,4),p+new Vector2(4,-4),x,1.2f);continue;}
            Color color=chosen?Gold:hovered?HudStyle.Rgb(0xd8edf3):marker.Selectable?Marker:new Color(Marker,.45f);
            float s=chosen||hovered?7:5;
            DrawPolyline(new[]{p+new Vector2(0,-s),p+new Vector2(s,0),p+new Vector2(0,s),p+new Vector2(-s,0),p+new Vector2(0,-s)},color,chosen||hovered?1.7f:1.1f,true);
            if(chosen)DrawArc(p,11,0,Mathf.Tau,24,new Color(HudStyle.Rgb(0xe7ba7e),.45f),1,true);
        }
        if(locked&&Game.SelectedExternalAim is null)DrawRect(new Rect2(mid-new Vector2(7,7),new Vector2(14,14)),Gold,false,1);
        string state=lost?"LOST / LAST SEEN":c.Track.ObservedDestroyed?"LOCKED / DESTROYED":c.Track.ObservedDisabled?"LOCKED / DISABLED"
            :c.Track.Level switch{TrackLevel.Locked=>"LOCKED / EXTERNAL",TrackLevel.Identified=>"IDENTIFIED / HULL",_=>"CONTACT / RADAR"};
        Text(viewer.Position+new Vector2(8,15),state,10,HudStyle.Rgb(0xc4dce7));
        Text(viewer.Position+new Vector2(8,15),identified?Aspect(_observer):"방위 미확인",10,HudStyle.Rgb(0x6b96aa),viewer.Size.X-16,HorizontalAlignment.Right);
        string foot=lost?$"마지막 관측 {Math.Max(0,Game.World.Time-c.LastSeenAt):0}초 전":!identified?"불확실한 신호":!locked?"외형 인식"
            :SelectableCount>0?"▲ 부위 조준 가능":"▽ 부위 정밀도 부족";
        string instruction=lost?"갱신 중지":!identified?$"±{Hud.FormatDistance(c.Track.ErrorMeters)}":!locked?"잠금 대기":SelectableCount>0?"가시 외장 클릭":"선체 조준";
        Text(new(viewer.Position.X+8,viewer.End.Y-8),foot,10,HudStyle.Rgb(0x7da2b4));
        Text(new(viewer.Position.X+8,viewer.End.Y-8),instruction,10,HudStyle.Rgb(0x7da2b4),viewer.Size.X-16,HorizontalAlignment.Right);
        if(_hover.Length>0)Text(new(viewer.Position.X+8,viewer.End.Y-25),_hover,11,HudStyle.Rgb(0xe2c18d),viewer.Size.X-16,HorizontalAlignment.Right);
    }
    /// <summary>
    /// Unidentified radar return: the same soft, drifting smear for every hull. Its spread follows the
    /// position error only; the class, length and pose stay hidden until identification.
    /// </summary>
    private void DrawContactSignal(Rect2 viewer,ContactSnapshot c)
    {
        float t=(float)Game.World.Time,spread=Mathf.Clamp(Mathf.Log(1+c.Track.ErrorMeters/200f)*.18f+.7f,.75f,1.25f);
        var center=viewer.GetCenter();
        DrawSetTransform(center,-.3f,Vector2.One);
        for(int i=0;i<7;i++)
        {
            float k=i/6f-.5f;
            var offset=new Vector2(k*150*spread+Mathf.Sin(t*1.7f+i*2.1f)*5,Mathf.Sin(t*1.1f+i*1.3f)*6);
            var size=new Vector2(104-Math.Abs(k)*50,70-Math.Abs(k)*30)*spread;
            DrawTextureRect(_blob,new Rect2(offset-size*.5f,size),false,new Color(.55f,.64f,.58f,.34f+.06f*Mathf.Sin(t*3+i)));
        }
        var glow=new Vector2(150,90)*spread;
        DrawTextureRect(_blob,new Rect2(-glow*.5f,glow),false,new Color(.86f,.6f,.31f,.22f));
        DrawSetTransform(Vector2.Zero);
        // Faint sweep lines and noise dashes: the receiver, not the target, is what flickers.
        for(int i=0;i<14;i++){float y=viewer.Position.Y+(i*37+21+(int)(t*9))%viewer.Size.Y;DrawLine(new(viewer.Position.X,y),new(viewer.End.X,y),new Color(.62f,.83f,.88f,.06f),1);}
        for(int i=0;i<36;i++)
        {
            float x=viewer.Position.X+(i*79+25+(int)(t*13)*(i%3))%viewer.Size.X,y=viewer.Position.Y+(i*23+33)%viewer.Size.Y;
            DrawRect(new Rect2(x,y,i%4==0?5:1,1),i%3==0?new Color(.69f,.86f,.89f,.5f):new Color(.26f,.4f,.47f,.7f));
        }
    }
}
