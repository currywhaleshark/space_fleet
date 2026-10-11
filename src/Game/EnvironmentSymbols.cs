using Godot;

namespace SpaceFleet.Game;

internal static class EnvironmentSymbols
{
    public static readonly Color Terrain=new(.94f,.72f,.34f);
    public static void DashedCircle(CanvasItem canvas,Vector2 center,float radius,Color color)
    {
        for(int i=0;i<32;i++)
            canvas.DrawArc(center,radius,i*Mathf.Tau/32,(i+.55f)*Mathf.Tau/32,4,color,1,true);
    }
    public static void Memory(CanvasItem canvas,Vector2 center,float radius,Color color)
    {
        DashedCircle(canvas,center,radius,new Color(color,.45f));
        var points=new[]{Vector2.Up,Vector2.Right,Vector2.Down,Vector2.Left,Vector2.Up};
        for(int i=0;i<4;i++)canvas.DrawDashedLine(center+points[i]*9,center+points[i+1]*9,color,1.5f,3);
    }
    public static void Blocked(CanvasItem canvas,Vector2 p)
    {
        canvas.DrawPolyline(new[]{p+new Vector2(-7,5),p+new Vector2(-3,-5),p+new Vector2(3,-5),p+new Vector2(7,5),p+new Vector2(-7,5)},Terrain,2,true);
        canvas.DrawLine(p+new Vector2(-9,8),p+new Vector2(9,8),Terrain,2,true);
    }
}
