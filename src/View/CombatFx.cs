using Godot;

namespace SpaceFleet.View;

internal static class CombatFx
{
    private static Shader? _glow;
    public static readonly QuadMesh Quad=new() { Size=Vector2.One };
    public static ShaderMaterial Glow(Color color,bool streak=false)
    {
        var material=new ShaderMaterial { Shader=_glow??=GD.Load<Shader>("res://shaders/combat_glow.gdshader") };
        material.SetShaderParameter("tint",color); material.SetShaderParameter("streak",streak); return material;
    }
    public static MeshInstance3D Make(Node parent,Mesh mesh,Material material)
    {
        var node=new MeshInstance3D { Mesh=mesh,MaterialOverride=material,
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off };
        parent.AddChild(node); return node;
    }
    public static float PixelSize(Camera3D camera,Vector3 position,float pixels)
        => 2*Mathf.Max(camera.Near,position.DistanceTo(camera.GlobalPosition))*Mathf.Tan(Mathf.DegToRad(camera.Fov*.5f))
            /Mathf.Max(1,camera.GetViewport().GetVisibleRect().Size.Y)*pixels;
    public static void Flare(MeshInstance3D node,Camera3D camera,Vector3 position,float size,float pixels)
    {
        float scale=1, distance=position.DistanceTo(camera.GlobalPosition);
        if(distance>camera.Far*.8f) { scale=camera.Far*.8f/distance; position=camera.GlobalPosition+(position-camera.GlobalPosition)*scale; }
        size=Mathf.Max(size*scale,PixelSize(camera,position,pixels));
        node.Transform=new Transform3D(camera.GlobalBasis.Scaled(Vector3.One*size),position); node.Visible=true;
    }
    public static void Streak(MeshInstance3D node,Camera3D camera,Vector3 head,Vector3 tail,float width)
    {
        Vector3 travel=head-tail;
        if(travel.LengthSquared()<.000001f) { node.Visible=false; return; }
        Vector3 y=travel.Normalized(), x=y.Cross(camera.GlobalPosition-(head+tail)*.5f).Normalized();
        if(x.LengthSquared()<.01f) x=camera.GlobalBasis.X;
        node.Transform=new(new Basis(x*width,y*travel.Length(),x.Cross(y)),(head+tail)*.5f); node.Visible=true;
    }
}
