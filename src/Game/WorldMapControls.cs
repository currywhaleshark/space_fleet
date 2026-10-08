using Godot;

namespace SpaceFleet.Game;

public partial class ScaleTest
{
    private WorldMapOverlay _worldMap = null!;
    private Input.MouseModeEnum _mapPreviousMouse;
    private bool _mapReleaseGuard;
    public bool WorldMapOpen => _worldMap is { Visible: true };
    public WorldMapState WorldMap => _worldMap.Map;
    private bool MapControlsBlocked => WorldMapOpen || _mapReleaseGuard;

    private void CreateWorldMap()
    {
        var layer = new CanvasLayer { Name = "WorldMapLayer", Layer = 5 };
        _worldMap = new WorldMapOverlay { Name = "WorldMap", Game = this, Visible = false };
        _worldMap.CloseRequested += CloseWorldMap;
        layer.AddChild(_worldMap); AddChild(layer);
    }

    private void ToggleWorldMap()
    {
        if (WorldMapOpen) { CloseWorldMap(); return; }
        if (Paused) return;
        if (MenuOpen) { _radial.Cancel(); _radialAction = null; }
        Camera.SetFreeLook(false);
        Camera.ResetTelescope();
        JettisonProgress=0;
        _mapPreviousMouse = Input.MouseMode;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _worldMap.Open(); _hud.Visible = false;
    }

    private void CloseWorldMap()
    {
        if (!WorldMapOpen) return;
        _worldMap.Close(); _hud.Visible = true;
        Input.MouseMode = _mapPreviousMouse == Input.MouseModeEnum.Captured
            && Scheme == ControlScheme.Pilot && !Paused && !Spectating
            ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        // 닫기 버튼을 누른 채 돌아와도 그 클릭이 주포 발사로 이어지지 않는다.
        _mapReleaseGuard = true;
    }

    private void UpdateMapInputGuard()
    {
        if (_mapReleaseGuard && !Input.IsActionPressed(InputSetup.Fire)
            && !Input.IsMouseButtonPressed(MouseButton.Left) && !Input.IsMouseButtonPressed(MouseButton.Right)
            && !Input.IsMouseButtonPressed(MouseButton.Middle)) _mapReleaseGuard = false;
    }
}
