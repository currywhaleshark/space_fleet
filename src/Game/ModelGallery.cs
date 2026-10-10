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
        camera.MakeCurrent();
    }

    public override void _Process(double delta)
    {
        _view.Sync(Vec3d.Zero, 1, (float)delta);
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
    };
}
