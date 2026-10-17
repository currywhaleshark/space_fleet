using System;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>Isolated preview world. All markers use the same local bay coordinates as damage.</summary>
public partial class HangarPreview : Control
{
    public ShipDefinition Definition { get; set; }=null!;
    public bool Cutaway { get; set; }
    public bool ShowBayMarkers { get; set; }=true;
    public string? SelectedBay { get; set; }
    public float Yaw { get; set; }=-.9f;
    public float Pitch { get; set; }=.42f;
    public float Zoom { get; set; }=1.35f;
    public event Action<string>? BaySelected;
    private SubViewport _viewport=null!;
    private Camera3D _camera=null!;
    private Font _font=null!;
    private Vector2 _press;
    private bool _drag,_moved;
    private float _fitLength;
    private Vector3 _focus;

    public override void _Ready()
    {
        ClipContents=true;MouseFilter=MouseFilterEnum.Stop;
        _font=new SystemFont{FontNames=new[]{"Malgun Gothic","Segoe UI"}};
        _viewport=new SubViewport{Size=new(780,414),OwnWorld3D=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Always};
        AddChild(_viewport);
        var root=new Node3D();_viewport.AddChild(root);
        root.AddChild(new WorldEnvironment{Environment=new Godot.Environment{
            BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new(.018f,.037f,.057f),
            AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=new(.65f,.78f,.95f),AmbientLightEnergy=.65f}});
        root.AddChild(new DirectionalLight3D{RotationDegrees=new(-35,-25,0),LightEnergy=2.5f});
        root.AddChild(new DirectionalLight3D{RotationDegrees=new(25,150,0),LightColor=new(.4f,.65f,1),LightEnergy=1.1f});
        var body=new ShipBody("HANGAR",Definition,Faction.Blue);body.Place(Vec3d.Zero,Quaternion.Identity);
        var model=ShipView.Create(body,7);root.AddChild(model);model.Sync(Vec3d.Zero,1,0);
        // Imported barrels and fins may extend beyond the nominal flight length.
        Aabb bounds=default;bool first=true;
        foreach(var mesh in ImportedShipModels.Meshes(model))
        {
            if(!mesh.IsVisibleInTree())continue;
            var local=mesh.GetAabb();
            for(int i=0;i<8;i++)
            {var point=mesh.GlobalTransform*local.GetEndpoint(i);bounds=first?new Aabb(point,Vector3.Zero):bounds.Expand(point);first=false;}
        }
        _focus=bounds.GetCenter();_fitLength=Mathf.Max(Definition.Flight.Length,bounds.Size.Length());
        if(Cutaway)
        {
            // Never mutate shared model materials: other ships and exterior previews retain their finish.
            var ghost=new StandardMaterial3D{Transparency=BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,AlbedoColor=new(.35f,.55f,.7f,.10f)};
            foreach(var mesh in ImportedShipModels.Meshes(model))mesh.MaterialOverride=ghost;
        }
        foreach(var bay in Definition.Bays)
        {
            bool selected=bay.Id==SelectedBay;
            var material=new StandardMaterial3D{Transparency=BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,NoDepthTest=true,
                AlbedoColor=selected?new(.2f,.8f,1,.65f):new(.25f,.65f,.9f,.25f)};
            root.AddChild(new MeshInstance3D{Name="Bay_"+bay.Id,Position=bay.Center,Mesh=new BoxMesh{Size=bay.HalfSize*2},
                MaterialOverride=material,Visible=Cutaway,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        }
        float length=Definition.Flight.Length;
        _camera=new Camera3D{Fov=34,Near=Mathf.Max(.02f,length*.001f),Far=length*20};root.AddChild(_camera);
        UpdateCamera();_camera.MakeCurrent();
    }
    private void UpdateCamera()
    {
        var direction=new Vector3(Mathf.Sin(Yaw)*Mathf.Cos(Pitch),Mathf.Sin(Pitch),-Mathf.Cos(Yaw)*Mathf.Cos(Pitch));
        _camera.LookAtFromPosition(_focus+direction*_fitLength*Zoom,_focus,Vector3.Up);
        QueueRedraw();
    }
    internal Vector2 BayScreen(string id)
    {
        foreach(var bay in Definition.Bays)if(bay.Id==id)return _camera.UnprojectPosition(bay.Center);
        throw new ArgumentException("Unknown bay",nameof(id));
    }
    private Rect2 Tag(int index)=>new(14+index*192,18,182,31);
    internal string? PickBay(Vector2 point)
    {
        if(!ShowBayMarkers)return null;
        for(int i=0;i<Definition.Bays.Length;i++)if(Tag(i).HasPoint(point))return Definition.Bays[i].Id;
        string? nearest=null;float distance=26;
        foreach(var bay in Definition.Bays)
        {float d=BayScreen(bay.Id).DistanceTo(point);if(d<distance){distance=d;nearest=bay.Id;}}
        return nearest;
    }
    public override void _GuiInput(InputEvent e)
    {
        if(e is InputEventMouseButton button)
        {
            if(button.Pressed&&button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {Zoom=Mathf.Clamp(Zoom*(button.ButtonIndex==MouseButton.WheelUp?.9f:1.1f),.65f,2.8f);UpdateCamera();AcceptEvent();}
            if(button.ButtonIndex==MouseButton.Left)
            {
                if(button.Pressed){_drag=true;_moved=false;_press=button.Position;}
                else if(_drag){_drag=false;if(!_moved&&PickBay(button.Position) is { } bay)BaySelected?.Invoke(bay);}
                AcceptEvent();
            }
        }
        if(e is InputEventMouseMotion motion&&_drag)
        {
            _moved|=motion.Position.DistanceTo(_press)>4;
            if(_moved){Yaw-=motion.Relative.X*.008f;Pitch=Mathf.Clamp(Pitch+motion.Relative.Y*.008f,-1.25f,1.25f);UpdateCamera();}
            AcceptEvent();
        }
    }
    public override void _Process(double delta)=>QueueRedraw();
    public override void _Draw()
    {
        if(_viewport is null)return;
        DrawTextureRect(_viewport.GetTexture(),new(Vector2.Zero,Size),false);
        if(!ShowBayMarkers)return;
        for(int i=0;i<Definition.Bays.Length;i++)
        {
            var bay=Definition.Bays[i];var p=BayScreen(bay.Id);var tag=Tag(i);
            Color color=bay.Id==SelectedBay?new(.3f,.85f,1):new(.4f,.6f,.75f);
            DrawLine(new(tag.GetCenter().X,tag.End.Y),p,new(color,.55f),1,true);
            DrawCircle(p,7,new(.015f,.04f,.06f));DrawArc(p,8,0,Mathf.Tau,32,color,2,true);
            DrawRect(tag,new(.02f,.06f,.09f,.95f));DrawRect(tag,color,false);
            DrawString(_font,tag.Position+new Vector2(10,22),HangarView.BayName(bay.Id),fontSize:16,modulate:color);
        }
    }
}
