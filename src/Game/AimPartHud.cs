using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;
namespace SpaceFleet.Game;
public partial class Hud
{
    private void DrawAimPart(Camera3D cam, ShipView controlled)
    {
        if(Game.SelectedExternalAim is not {} aim || Game.FireTarget is not {} target)return;
        var track=Game.TrackOf(target);
        var world=track.EstimatedPosition+Vec3d.From(track.ObservedOrientation*aim.Point);
        var render=(world-Game.RenderOrigin).ToVector3();
        if(cam.IsPositionBehind(render))return;
        var point=cam.UnprojectPosition(render);DrawDiamond(point,7,Motion);
        Label(point+new Vector2(12,-8),aim.Part.Label,12,Motion);
    }
}
