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
    internal Rect2 SquadronBounds { get; private set; }
    private void DrawSquadrons(ShipView controlled, Vector2 screen)
    {
        SquadronBounds=new();
        var squadrons = Game.World.Squadrons.Where(q => q.Faction == controlled.Body.Faction).ToList();
        if(Game.LaunchConfig?.Match is not null)squadrons.RemoveAll(q=>q!=Game.OwnedSquadron);
        if (squadrons.Count == 0) return;
        float row = HudFont(13) + 29;
        float bottom = screen.Y - (Game.BattleMode ? 284 : 232);
        var panel = new Rect2(16, bottom - squadrons.Count * row - 8, 250, squadrons.Count * row + 8);
        SquadronBounds=panel;
        DrawInterceptorStatus(controlled, screen, panel.Position.Y);
        DrawRect(panel, PanelBack);

        for (int i = 0; i < squadrons.Count; i++)
        {
            Squadron q = squadrons[i];
            float y = panel.Position.Y + 4 + i * row;
            bool mine = q.Members.Any(Game.OwnsShip);
            if (mine)
                DrawColoredPolygon(new[] { new Vector2(panel.Position.X + 6, y + 5), new Vector2(panel.Position.X + 13, y + 10),
                    new Vector2(panel.Position.X + 6, y + 15) }, ShipAffiliation.Owned);

            float x = panel.Position.X + 20;
            foreach (ShipBody ship in q.Members)
            {
                var center = new Vector2(x + 4, y + 10);
                float r = ship.Definition.BattleValue.HudMarkerRadius;
                if (ship.Damage.Destroyed)
                {
                    DrawLine(center + new Vector2(-r, -r), center + new Vector2(r, r), Dim, 1.5f);
                    DrawLine(center + new Vector2(-r, r), center + new Vector2(r, -r), Dim, 1.5f);
                }
                else
                {
                    Color c = ship.Damage.Disabled ? Hostile
                        : ship.Damage.Modules.Any(m => m.Destroyed) ? Motion : Game.ShipMarkerColor(ship);
                    DrawCircle(center, r, c);
                    if (ship == controlled.Body)
                        DrawArc(center, r + 2.5f, 0, Mathf.Tau, 12, Text, 1f);
                }
                x += r * 2 + 4;
            }

            string activity = q.Role == SquadronRole.InterceptorWing ? q.Activity : mine
                ? Game.SquadOrder switch
                {
                    SquadCommand.Focus => $"집중 {Game.SquadTarget?.Callsign}",
                    _ => SquadCommands.Label(Game.SquadOrder),
                }
                : q.Activity;
            Label(new Vector2(panel.Position.X + 120, y + 16), q.Name, 13, mine ? ShipAffiliation.Owned : Text,
                HorizontalAlignment.Left, panel.Size.X - 126);
            Label(new Vector2(panel.Position.X + 20, y + row - 7), activity, 13, Dim, width: panel.Size.X - 30);
        }
    }
}
