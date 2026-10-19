using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>
/// 함선 외형 검토 장면(--gallery=함선ID). 게임과 같은 조명·하늘·임포트 경로로 한 척을 띄우고
/// 시안과 같은 3/4 정면(hero)·측면(side)·후면(rear) 구도로 찍는다. --gallery-view, --gallery-faction=Red, --shot=파일.
/// 엔진은 순항 출력으로 켠다. 시뮬레이션에는 관여하지 않는다.
/// --gallery=zone-A|zone-B|zone-C 는 점령 거점 구조물을 전함(크기 비교용)과 함께 띄운다.
/// </summary>
public partial class ModelGallery : Node3D
{
    public string ShipId { get; init; } = "battleship";
    public string View { get; init; } = "hero";
    public Faction Faction { get; init; } = Faction.Blue;
    public string? Output { get; init; }
    private ShipView _view = null!;
    private int _frame;

    public override void _Ready()
    {
        AddChild(new WorldEnvironment { Environment = BattleLook.Environment() });
        foreach (var light in BattleLook.Lights()) AddChild(light);
        if (ShipId.StartsWith("zone-")) { BuildZone(ShipId[5..]); return; }
        if (ShipId is "asteroids" or "asteroid-variants" or "asteroid") { BuildAsteroids(); return; }

        var body = new ShipBody("GALLERY", ShipDefinitions.ById(ShipId), Faction);
        body.Place(Vec3d.Zero, Quaternion.Identity);
        body.Control = new ShipControl { Thrust = Vector3.Forward * 0.6f, FlightAssist = false };
        for (int i = 0; i < 20; i++) body.Step(SimWorld.TickDelta);
        body.Place(Vec3d.Zero, Quaternion.Identity);
        _view = ShipView.Create(body, seed: 7);
        AddChild(_view);
        _view.Sync(Vec3d.Zero, 1, 1f / 60);

        float l = body.Class.Length;
        Vector3 dir = View switch
        {
            "side" => new Vector3(-1f, 0.12f, 0f),
            "rear" => new Vector3(-0.42f, 0.3f, 1f),
            "front" => new Vector3(0f, 0.08f, -1f),
            "top" => new Vector3(0.001f, 1f, 0.02f),
            // 조명 검토: 해를 등진 쪽(lit), 아래에서 올려다본 쪽(below). 해는 +X·+Y·+Z 쪽에 있다.
            "lit" => new Vector3(0.85f, 0.35f, 0.4f),
            "below" => new Vector3(0.2f, -1f, 0.3f),
            _ => new Vector3(-0.75f, 0.42f, -0.85f),
        };
        var camera = new Camera3D { Fov = 34, Near = Mathf.Max(0.05f, l * 0.002f), Far = l * 40 };
        AddChild(camera);
        float distance = l * (View is "side" or "top" ? 1.55f : 1.25f);
        Vector3 focus = Vector3.Zero;
        if (View == "turret" && body.Definition.Railgun?.Mounts is { Length: > 0 } mounts)
        {
            // 첫 주포탑 근접: 포탑 몸체와 포신 비례 확인용.
            focus = mounts[0].Pivot + new Vector3(0, 0, -l * 0.08f);
            dir = new Vector3(-0.8f, 0.45f, -0.55f);
            distance = l * 0.32f;
        }
        camera.LookAtFromPosition(focus + dir.Normalized() * distance, focus, View == "top" ? Vector3.Forward : Vector3.Up);
        BattleLook.AimViewFill(this, camera.Basis);
        camera.MakeCurrent();
        if (Studio)
        {
            // 도장 검토용 채움광(전투 조명에는 없음): 카메라 쪽 주광과 반대편 약한 역광.
            var key = new DirectionalLight3D { LightEnergy = 4f, ShadowEnabled = true };
            AddChild(key); key.LookAtFromPosition(camera.Position + Vector3.Up * l * .6f, focus, Vector3.Up);
            var fill = new DirectionalLight3D { LightEnergy = .8f, LightColor = new Color(.75f, .82f, 1f) };
            AddChild(fill); fill.LookAtFromPosition(-camera.Position + Vector3.Down * l * .3f, focus, Vector3.Up);
        }
    }

    /// <summary>--gallery-light=studio: 도장·표면 검토용 채움광을 더한다.</summary>
    public bool Studio { get; init; }

    private ZoneStructures.Built? _zone;

    private void BuildAsteroids()
    {
        bool variants=ShipId=="asteroid-variants";
        bool single=ShipId=="asteroid";
        var field=AsteroidField.Generate(new BattleConfig {Battlefield=Battlefield.AsteroidCluster});
        if(!variants && !single) {var view=AsteroidFieldView.Create(field);AddChild(view);view.Sync(field.Center);}
        else
        {
            var material=AsteroidFieldView.RockMaterial();
            for(int i=0;i<(single?1:12);i++)
            {
                var position=single?Vector3.Zero:new Vector3((i%4-1.5f)*4300,(1-i/4)*4100,0);
                AddChild(new MeshInstance3D {Mesh=AsteroidFieldView.BuildMesh(i),MaterialOverride=material,
                    Position=position,Scale=Vector3.One*1700,Layers=BattleLook.AsteroidLayer,
                    CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
                if(Array.IndexOf(OS.GetCmdlineUserArgs(),"--show-obstacles")>=0)
                {
                    var parts=new List<ObstaclePart>();
                    foreach(var p in AsteroidField.Template(i))parts.Add(ObstaclePart.Sphere(p.A*1700,p.Radius*1700));
                    var wire=ObstacleWireframe.Create(new("Preview",Vec3d.Zero,Quaternion.Identity,parts.ToArray()));
                    wire.Position=position;AddChild(wire);
                }
            }
        }
        var body=new ShipBody("SCALE",ShipDefinitions.ById("battleship"),Faction);
        body.Place(single?new(0,-2400,0):variants?new(0,-6300,0):new(0,0,10000),Quaternion.Identity);
        _view=ShipView.Create(body,seed:7);AddChild(_view);
        var camera=new Camera3D {Fov=45,Near=5,Far=1_000_000};AddChild(camera);
        // Inspect the same surface from the sun, terminator and night side.
        Vector3 rockEye=View switch
        {
            "sunlit"=>new Vector3(.75f,.35f,.35f).Normalized()*6500,
            "night"=>new Vector3(-.75f,-.35f,-.35f).Normalized()*6500,
            _=>new Vector3(0,0,6500),
        };
        camera.LookAtFromPosition(single?rockEye:variants?new(0,0,22000):new(-80000,65000,95000),Vector3.Zero,Vector3.Up);
        camera.MakeCurrent();
    }

    private void BuildZone(string name)
    {
        _zone = ZoneStructures.Create(name);
        AddChild(_zone.Root);
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--show-obstacles") >= 0)
            AddChild(ObstacleWireframe.Create(ZoneStructureShapes.For(name, Vec3d.Zero)));
        var body = new ShipBody("SCALE", ShipDefinitions.ById("battleship"), Faction);
        body.Place(new Vec3d(2600, -500, 1800), Quaternion.FromEuler(new Vector3(0, .6f, 0)));
        _view = ShipView.Create(body, seed: 7);
        AddChild(_view);
        Vector3 dir = View switch
        {
            "side" => new Vector3(-1f, .15f, 0f),
            "top" => new Vector3(.001f, 1f, .02f),
            _ => new Vector3(-.7f, .38f, .8f),
        };
        var camera = new Camera3D { Fov = 40, Near = 5, Far = 60_000 };
        AddChild(camera);
        camera.LookAtFromPosition(dir.Normalized() * 7200, new Vector3(400, 0, 400), View == "top" ? Vector3.Forward : Vector3.Up);
        camera.MakeCurrent();
    }

    public override void _Process(double delta)
    {
        _view.Sync(Vec3d.Zero, 1, (float)delta);
        if (_zone?.Spinner is { } spinner) spinner.RotateObjectLocal(Vector3.Up, _zone.SpinRate * (float)delta);
        if (++_frame < 12 || Output is null) return;
        Error err = GetViewport().GetTexture().GetImage().SavePng(Output);
        GD.Print(err == Error.Ok ? $"gallery saved: {Output}" : $"gallery failed: {err}");
        GetTree().Quit();
    }

    public static ModelGallery FromArgs(System.Collections.Generic.Dictionary<string, string> args) => new()
    {
        ShipId = args["gallery"],
        View = args.GetValueOrDefault("gallery-view", "hero"),
        Faction = string.Equals(args.GetValueOrDefault("gallery-faction", "Blue"), "Red", StringComparison.OrdinalIgnoreCase) ? Faction.Red : Faction.Blue,
        Output = args.GetValueOrDefault("shot"),
        Studio = args.GetValueOrDefault("gallery-light") == "studio",
    };
}
