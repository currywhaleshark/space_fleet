using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>
/// 편대 명령 표시. 공격 중인 아군 → 그 표적의 추정 위치까지 옅은 선(끝에 작은 화살표),
/// 위치 유지 중인 아군 → 브래킷 옆 작은 사각형. 호위는 기본이라 표시하지 않는다.
/// </summary>
public partial class Hud
{
    private void DrawSquadOrders(Camera3D cam, ShipView controlled)
    {
        foreach (ShipView view in Game.Views)
        {
            ShipBody ship = view.Body;
            if (ship == controlled.Body || ship.Faction != controlled.Body.Faction || ship.Damage.Destroyed) continue;
            if (Game.World.BrainOf(ship) is not { Enabled: true } brain) continue;
            if (cam.IsPositionBehind(view.Position)) continue;
            Vector2 from = cam.UnprojectPosition(view.Position);

            switch (brain.Order.Kind)
            {
                case OrderKind.Attack when brain.Order.Target is ShipBody target:
                {
                    SensorTrack track = Game.World.Sensors.Track(ship.Faction, target);
                    if (track.Level == TrackLevel.None) break;
                    Vector3 render = (target.InterpolatedPosition(Engine.GetPhysicsInterpolationFraction()) + track.Offset - Game.RenderOrigin).ToVector3();
                    if (cam.IsPositionBehind(render)) break;
                    Vector2 to = cam.UnprojectPosition(render);
                    Vector2 d = to - from;
                    float len = d.Length();
                    if (len < 30f) break;
                    Vector2 dir = d / len;
                    // 양 끝 표시를 가리지 않게 조금씩 떼고 그린다.
                    Vector2 a = from + dir * 14f, b = to - dir * 16f;
                    DrawDashedLine(a, b, new Color(Friendly, 0.35f), 1f, 6f);
                    Vector2 n = new(-dir.Y, dir.X);
                    DrawColoredPolygon(new[] { b + dir * 6f, b - dir * 2f + n * 4f, b - dir * 2f - n * 4f }, new Color(Friendly, 0.6f));
                    break;
                }
                case OrderKind.Hold:
                    DrawRect(new Rect2(from + new Vector2(-16f, -4f), new Vector2(6f, 6f)), new Color(Friendly, 0.8f), false, 1.5f);
                    break;
            }
        }
    }
}
