using System;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class WorldMapOverlay
{
    private void DrawObstacles(Control canvas,Func<Vec3d,Vector2> at)
    {
        var color=new Color(.66f,.68f,.7f,.7f);
        foreach(var obstacle in Game.World.Obstacles)
        {
            if(obstacle.Name.StartsWith("Rock-",StringComparison.Ordinal) && obstacle.BoundingRadius/Map.HalfSpan*Radius<2)continue;
            if((obstacle.Position-Map.Center).Length()>Map.HalfSpan+obstacle.BoundingRadius)continue;
            foreach(var part in obstacle.Parts)
            {
                Vector2 a=at(obstacle.ToWorld(part.A)),b=at(obstacle.ToWorld(part.B));
                float radius=Mathf.Max(.6f,(float)(part.Radius/Map.HalfSpan)*Radius);
                if(a.DistanceSquaredTo(b)<.01f) { canvas.DrawArc(a,radius,0,Mathf.Tau,32,color,1,true); continue; }
                float angle=(b-a).Angle(); Vector2 normal=(b-a).Normalized().Orthogonal()*radius;
                canvas.DrawLine(a+normal,b+normal,color,1,true); canvas.DrawLine(a-normal,b-normal,color,1,true);
                canvas.DrawArc(a,radius,angle+Mathf.Pi/2,angle+Mathf.Pi*1.5f,20,color,1,true);
                canvas.DrawArc(b,radius,angle-Mathf.Pi/2,angle+Mathf.Pi/2,20,color,1,true);
            }
        }
    }
}
