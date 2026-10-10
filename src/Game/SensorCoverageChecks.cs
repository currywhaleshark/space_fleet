using System;
using System.Threading.Tasks;
using Godot;

namespace SpaceFleet.Game;

/// <summary>Read actual GPU output: translucent union, no stacked opacity or internal wire rings.</summary>
public partial class SensorCoverageChecks : Node
{
    private int _checks;
    private void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); _checks++; }
    public override async void _Ready()
    {
        try
        {
            var viewport = new SubViewport { Size = new(256, 256), TransparentBg = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Disable3D = true };
            AddChild(viewport);
            var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sensor_coverage.gdshader") };
            viewport.AddChild(new ColorRect { Size = new(256, 256), Material = material, Color = Colors.White });
            var volumes = new Vector4[64]; var directions = new Vector4[64];
            material.SetShaderParameter("map_scale", new Vector2(2, 2));
            async Task<Image> Render(int count)
            {
                material.SetShaderParameter("volumes", volumes); material.SetShaderParameter("directions", directions);
                material.SetShaderParameter("volume_count", count);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                return viewport.GetTexture().GetImage();
            }
            var empty = await Render(0);
            Check(empty.GetPixel(128, 128).A < .01, "No sensors produce no phantom coverage");
            volumes[0] = new(0, 0, 0, .5f);
            var single = await Render(1); var center = single.GetPixel(128, 128);
            Check(center.A > .2f && center.A < .5f && center.B > center.R, "Sensor is a translucent blue volume");
            Check(single.GetPixel(230, 128).A < .01, "Space outside the volume remains transparent");
            Check(single.GetPixel(104, 104).Luminance > single.GetPixel(152, 152).Luminance, "Surface shading gives the volume a spherical form");
            volumes[1] = volumes[0];
            var duplicate = await Render(2);
            Check(Math.Abs(duplicate.GetPixel(128, 128).A - center.A) < .01,
                "Identical overlapping sensors do not darken or increase opacity");
            volumes[0] = new(-.25f, 0, 0, .5f); volumes[1] = new(.25f, 0, 0, .5f);
            var joined = await Render(2);
            Check(joined.GetPixel(128, 128).A > .2 && Math.Abs(joined.GetPixel(128, 128).A - joined.GetPixel(65, 128).A) < .02,
                "Overlapping volumes form one solid region without an internal opacity seam");
            for (int x = 80; x < 180; x += 10)
                Check(joined.GetPixel(x, 128).A > .2, "Merged coverage has no internal holes or wire rings");
            (volumes[0], volumes[1]) = (volumes[1], volumes[0]);
            var reversed = await Render(2);
            for (int x = 80; x < 180; x += 10)
                Check((new Vector3(joined.GetPixel(x, 128).R, joined.GetPixel(x, 128).G, joined.GetPixel(x, 128).B)
                    - new Vector3(reversed.GetPixel(x, 128).R, reversed.GetPixel(x, 128).G, reversed.GetPixel(x, 128).B)).Length() < .015,
                    "Network appearance does not depend on ship enumeration order");
            volumes[0] = new(-.6f, 0, 0, .25f); volumes[1] = new(.6f, 0, 0, .25f);
            var separate = await Render(2);
            Check(separate.GetPixel(128, 128).A < .01, "Disconnected sensor regions do not invent coverage between them");
            volumes[0] = new(0, 0, 3, .5f);
            var outside = await Render(1);
            Check(outside.GetPixel(128, 128).A < .01, "A volume outside the map's depth does not project false coverage");
            volumes[0] = new(0, 0, 0, .5f); directions[0] = new(1, 0, 0, 1);
            var probe = await Render(1);
            Check(probe.GetPixel(128, 128).G > probe.GetPixel(128, 128).R, "Probe coverage retains its green identity");
            Check(probe.GetPixel(183, 128).A > .1 && probe.GetPixel(73, 128).A < .01,
                "Probe surface reflects stronger forward observation");
            directions[0] = new(1, 0, 0, 2);
            var preview = await Render(1);
            Check(preview.GetPixel(128, 128).R > preview.GetPixel(128, 128).B, "Pending placement has a distinct amber volume");
            GD.Print($"PASS: {_checks} sensor coverage GPU checks"); GetTree().Quit();
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
}
