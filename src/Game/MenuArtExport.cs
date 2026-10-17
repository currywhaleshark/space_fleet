using System;
using System.IO;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>Developer-only --export-menu-art. Rebuilds portraits from the actual stock GLBs.</summary>
public partial class MenuArtExport : Node
{
    public override async void _Ready()
    {
        try
        {
            var directory=ProjectSettings.GlobalizePath("res://assets/ui/ships");Directory.CreateDirectory(directory);
            var selected=OS.GetCmdlineUserArgs().FirstOrDefault(a=>a.StartsWith("--portrait-models="))?
                .Split('=',2)[1].Split(',');
            foreach(var def in FleetLoadouts.Hulls.GroupBy(d=>d.ModelId).Select(g=>g.First())
                .Where(d=>selected==null||selected.Contains(d.ModelId)))
            {
                var viewport=new SubViewport{Size=new(640,240),OwnWorld3D=true,TransparentBg=true,
                    Msaa3D=Viewport.Msaa.Msaa4X,RenderTargetUpdateMode=SubViewport.UpdateMode.Always};AddChild(viewport);
                var root=new Node3D();viewport.AddChild(root);
                root.AddChild(new WorldEnvironment{Environment=new Godot.Environment{
                    BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new(0,0,0,0),
                    AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=new(.7f,.8f,1),AmbientLightEnergy=.85f}});
                root.AddChild(new DirectionalLight3D{RotationDegrees=new(-40,-35,0),LightEnergy=3.2f});
                root.AddChild(new DirectionalLight3D{RotationDegrees=new(20,140,0),LightEnergy=1.8f,LightColor=new(.65f,.8f,1)});
                var body=new ShipBody("PORTRAIT",def,Faction.Blue);body.Place(Vec3d.Zero,Quaternion.Identity);
                var model=ShipView.Create(body,7);root.AddChild(model);model.Sync(Vec3d.Zero,1,0);
                var hull=model.FindChild("BlenderHull",true,false)??model;
                foreach(var mesh in ImportedShipModels.Meshes(model))if(!hull.IsAncestorOf(mesh)&&mesh!=hull)mesh.Hide();
                var points=ImportedShipModels.Meshes(hull).Where(m=>m.IsVisibleInTree())
                    .SelectMany(m=>Enumerable.Range(0,8).Select(i=>m.GlobalTransform*m.GetAabb().GetEndpoint(i))).ToArray();
                var bounds=new Aabb(points[0],Vector3.Zero);foreach(var point in points)bounds=bounds.Expand(point);
                var center=bounds.GetCenter();float length=bounds.Size.Length();
                var camera=new Camera3D{Projection=Camera3D.ProjectionType.Orthogonal,Near=Mathf.Max(.01f,length*.001f),Far=length*10};root.AddChild(camera);
                camera.LookAtFromPosition(center+new Vector3(-1f,.45f,-.3f).Normalized()*length*3,center,Vector3.Up);
                var inverse=camera.GlobalBasis.Inverse();var local=points.Select(p=>inverse*(p-center)).ToArray();
                float height=local.Max(p=>p.Y)-local.Min(p=>p.Y),width=local.Max(p=>p.X)-local.Min(p=>p.X);
                camera.Size=Mathf.Max(height,width/(640f/240))*1.1f;camera.MakeCurrent();
                for(int frame=0;frame<8;frame++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                string file=Path.Combine(directory,def.ModelId+".png");
                var image=viewport.GetTexture().GetImage();
                var used=image.GetUsedRect();
                var crop=used.Grow(6).Intersection(new Rect2I(Vector2I.Zero,image.GetSize()));
                if(image.GetRegion(crop).SavePng(file)!=Error.Ok)throw new IOException(file);
                GD.Print("Portrait: "+def.ModelId);viewport.QueueFree();
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            GetTree().Quit();
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
}
