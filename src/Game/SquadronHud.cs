using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>
/// 아군 편대 현황(하단 왼쪽, 계통 패널 위). 한 줄에 편대 하나: ▶ 내 편대, 구성원 점(파랑 정상·주황 손상·빨강 무력화·X 격침),
/// 현재 행동. 지휘관이 움직이는 편대는 행동이, 내 편대는 내 명령이 보인다.
/// </summary>
public partial class Hud
{
    private void DrawSquadrons(ShipView controlled, Vector2 screen)
    {
        var squadrons = Game.World.Squadrons.Where(q => q.Faction == controlled.Body.Faction).ToList();
        if (squadrons.Count == 0) return;
        const float row = 20f;
        var panel = new Rect2(16, screen.Y - 160 - squadrons.Count * row - 12, 250, squadrons.Count * row + 8);
        DrawRect(panel, PanelBack);

        for (int i = 0; i < squadrons.Count; i++)
        {
            Squadron q = squadrons[i];
            float y = panel.Position.Y + 4 + i * row;
            bool mine = q == controlled.Body.Squadron;
            if (mine)
                DrawColoredPolygon(new[] { new Vector2(panel.Position.X + 6, y + 5), new Vector2(panel.Position.X + 13, y + 10),
                    new Vector2(panel.Position.X + 6, y + 15) }, Text);

            float x = panel.Position.X + 20;
            foreach (ShipBody ship in q.Members)
            {
                var center = new Vector2(x + 4, y + 10);
                float r = ship.Class.Kind switch { HullKind.Battleship => 5f, HullKind.Escort => 4f, _ => 3f };
                if (ship.Damage.Destroyed)
                {
                    DrawLine(center + new Vector2(-r, -r), center + new Vector2(r, r), Dim, 1.5f);
                    DrawLine(center + new Vector2(-r, r), center + new Vector2(r, -r), Dim, 1.5f);
                }
                else
                {
                    Color c = ship.Damage.Disabled ? Hostile
                        : ship.Damage.Modules.Any(m => m.Destroyed) ? Motion : Friendly;
                    DrawCircle(center, r, c);
                    if (ship == controlled.Body)
                        DrawArc(center, r + 2.5f, 0, Mathf.Tau, 12, Text, 1f);
                }
                x += r * 2 + 4;
            }

            string activity = mine
                ? Game.SquadOrder switch
                {
                    OrderKind.Attack => $"공격 {Game.SquadTarget?.Callsign}",
                    OrderKind.Hold => "위치 유지",
                    _ => "나를 호위",
                }
                : q.Activity;
            Label(new Vector2(panel.Position.X + 120, y + 14), $"{q.Name} · {activity}", 12, mine ? Text : Dim,
                HorizontalAlignment.Left, panel.Size.X - 126);
        }
    }
}
