using System;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

public partial class Hud
{
    private void DrawTerrainWarning(ShipView view,Vector2 size)
    {
        if(Game.AutoPlay || Game.Spectating || Game.World.Obstacles.Count==0)return;
        var ship=view.Body;
        if(!PredictTerrainImpact(Game.World.Obstacles,ship,out var hit))return;
        Vector3 render=(hit.Position-Game.RenderOrigin).ToVector3();
        var camera=Game.Camera;Vector2 center=size*.5f;
        Vector2 direction=camera.UnprojectPosition(render)-center;
        if(camera.IsPositionBehind(render))direction=-direction;
        if(direction.LengthSquared()<4)direction=Vector2.Up;
        direction=direction.Normalized();
        Vector2 p=center+direction*Mathf.Min(size.X,size.Y)*.19f;
        Color color=new(1,.57f,.18f,.65f+.3f*Mathf.Sin((float)Time.GetTicksMsec()*.01f));
        Vector2 side=direction.Orthogonal();
        for(int i=0;i<3;i++)
        {
            Vector2 at=p+direction*i*10;
            DrawPolyline(new[]{at-direction*7-side*10,at,at-direction*7+side*10},color,2.5f,true);
        }
        DrawArc(p+direction*10,24,direction.Angle()-.6f,direction.Angle()+.6f,16,color,2,true);
    }
    internal static bool PredictTerrainImpact(ObstacleSet obstacles,ShipBody ship,out ObstacleHit hit)
    {
        hit=default;
        return ship.Velocity.Length()>30 && obstacles.SegmentBlocked(ship.Position,
            ship.Position+Vec3d.From(ship.Velocity)*4,out hit,ship.Hull.BoundingRadius);
    }
    private void DrawRadarTerrain(ShipView view,Func<Vector3,Vector2> project)
    {
        double range=Math.Min(Game.Radar.Range,20000);
        var rotation=view.Basis.GetRotationQuaternion().Inverse();
        foreach(var obstacle in Game.World.Obstacles)
        {
            if(obstacle.BoundingRadius<2000 || (obstacle.Position-view.SimPosition).Length()>range)continue;
            Vector3 local=rotation*(obstacle.Position-view.SimPosition).ToVector3()/Game.Radar.Range;
            float r=(float)(obstacle.BoundingRadius/Game.Radar.Range)*112;
            Vector2 center=project(local),radarCenter=project(Vector3.Zero);
            for(int i=0;i<32;i++)
            {
                Vector2 a=center+Vector2.FromAngle(i*Mathf.Tau/32)*r,b=center+Vector2.FromAngle((i+1)*Mathf.Tau/32)*r;
                if(a.DistanceSquaredTo(radarCenter)<=112*112 && b.DistanceSquaredTo(radarCenter)<=112*112)
                    DrawLine(a,b,new Color(.62f,.65f,.66f,.42f),1,true);
            }
            Vector2 foot=project(new(local.X,0,local.Z));
            DrawLine(foot,center,new Color(.62f,.65f,.66f,.18f),1,true);
        }
    }
}
