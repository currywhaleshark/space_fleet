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
    private static Shader? _beam;
    /// <summary>Constant pixel-width laser beam (shaders/laser_beam.gdshader); halo tinted per faction.</summary>
    public static ShaderMaterial Beam(Color halo)
    {
        var material=new ShaderMaterial { Shader=_beam??=GD.Load<Shader>("res://shaders/laser_beam.gdshader") };
        material.SetShaderParameter("halo_color",halo); return material;
    }
    /// <summary>Places a beam quad between two points, clipped to the visible depth range (the shader widens it on screen).</summary>
    public static bool Beam(MeshInstance3D node,Camera3D camera,Vector3 start,Vector3 end,float focus)
    {
        Vector3 eye=camera.GlobalPosition, forward=-camera.GlobalBasis.Z;
        float near=camera.Near*2, far=camera.Far*.95f;
        float a=(start-eye).Dot(forward), b=(end-eye).Dot(forward);
        if((a<near&&b<near)||(a>far&&b>far)) { node.Visible=false; return false; }
        Vector3 Clip(Vector3 inside,float di,Vector3 outside,float dout,float limit)=>inside+(outside-inside)*((limit-di)/(dout-di));
        if(a<near) { start=Clip(end,b,start,a,near); a=near; } else if(b<near) { end=Clip(start,a,end,b,near); b=near; }
        if(a>far) start=Clip(end,b,start,a,far); else if(b>far) end=Clip(start,a,end,b,far);
        Vector3 travel=end-start;
        if(travel.LengthSquared()<.000001f) { node.Visible=false; return false; }
        Vector3 y=travel.Normalized(), x=y.Cross(eye-(start+end)*.5f);
        x=x.LengthSquared()<.0001f?camera.GlobalBasis.X:x.Normalized();
        node.Transform=new(new Basis(x,travel,x.Cross(y)),(start+end)*.5f);
        node.ExtraCullMargin=64; node.SetInstanceShaderParameter("focus",Mathf.Clamp(focus,0,1)); node.Visible=true; return true;
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
