using System;
using System.Collections.Generic;
using Godot;

namespace SpaceFleet.Game;

/// <summary>
/// HUD v4 visual tokens (docs: HUD_Prototype_v4). One palette, a monospace face for numbers/labels,
/// thin-bordered panes and line icons rasterised once per pixel size. Drawing only; no game state.
/// </summary>
internal static class HudStyle
{
    public static readonly Color Ink=Rgb(0xd6e5ed),Dim=Rgb(0x73919f),Faint=Rgb(0x446474),Cyan=Rgb(0x64cbe5),
        Mint=Rgb(0x7fcdb5),Amber=Rgb(0xecbf7e),Red=Rgb(0xef8375),Line=new(128/255f,191/255f,215/255f,.26f),
        Track=Rgb(0x243641),PaneTop=new(6/255f,21/255f,33/255f,.85f),PaneBottom=new(5/255f,12/255f,23/255f,.71f);
    public static Color Rgb(int hex,float alpha=1)=>new(((hex>>16)&255)/255f,((hex>>8)&255)/255f,(hex&255)/255f,alpha);

    private static Font? _mono,_sans;
    public static Font Sans=>_sans??=new SystemFont{FontNames=new[]{"Malgun Gothic","Segoe UI"}};
    /// <summary>Numbers and short Latin labels; Korean falls back to the HUD sans face.</summary>
    public static Font Mono=>_mono??=new SystemFont{FontNames=new[]{"Cascadia Mono","Cascadia Code","Consolas"},Fallbacks=new Godot.Collections.Array<Font>{Sans}};

    // Prototype symbols (24×24, stroked). Kept as path data so they rasterise sharply at any HUD scale.
    private static readonly Dictionary<string,string> Paths=new()
    {
        ["gun"]="<path d='M3 21 13 11M6 21 16 11M1.5 16 9.5 8M13 10l5-5 3 3-5 5zM18 5l2-2m-6 8 2 2'/>",
        ["missile"]="<path d='M4 20 15 9l5-6-6 5L3 19zM10 13l-5-1-2 2m10-4 1-5 2-2M6 18 3 21m5-1 3-1'/>",
        ["torpedo"]="<path d='m2 20 13-13 6-4-3 6L5 22zm6-7-1-5 2-2m4 10 5 1-2 2M18 6l2 2'/>",
        ["laser"]="<path d='M2 12h7M15 12h7M9 8h6v8H9zM12 2v4M12 18v4'/>",
        ["bolt"]="<path d='m13 2-9 12h6l-1 8 11-13h-7l1-7z'/>",
        ["vent"]="<path d='M12 2v20M4 6l16 12M20 6 4 18M8 4l4 4 4-4M8 20l4-4 4 4M3 13l5 1-1 5M21 11l-5-1 1-5'/>",
        ["search"]="<circle cx='12' cy='12' r='9'/><circle cx='12' cy='12' r='5'/><circle cx='12' cy='12' r='1.5' fill='#fff'/><path d='M12 12 18 6M12 0v3M12 21v3M0 12h3M21 12h3'/>",
        ["jump"]="<path d='M5 5c4-4 11-2 13 2 4 8-3 14-9 13-6-1-9-7-6-12 3-5 11-6 14-1 3 5-1 11-6 10-4-1-4-6-1-8'/>",
        ["shield"]="<path d='m12 2 8 3v7c0 4.5-3 7.4-8 10-5-2.6-8-5.5-8-10V5z'/>",
        ["hull"]="<path d='m12 2 8 9-8 11-8-11zM12 2v20M4 11h16'/>",
        ["engine"]="<path d='M4 14 12 2l8 12-8-2-8 2zM9 17l-2 5m5-7v7m3-5 2 5'/>",
        ["heat"]="<path d='M10 4a2 2 0 0 1 4 0v10a5 5 0 1 1-4 0zM12 8v10'/><circle cx='12' cy='18' r='1'/>",
        ["repair"]="<path d='M3 20 15 8M14 3a6 6 0 0 0 7 7l-4-4 2-3a7 7 0 0 0-7 7M3 20l3 1'/>",
        ["drone"]="<path d='M12 3 18 9l-6 4-6-4 6-6zM6 9 1 13l5 3 6-3 6 3 5-3-5-4M12 13v8'/>",
        ["sensor"]="<path d='M12 18v4M12 18a6 6 0 1 0-6-6M4 5a11 11 0 0 1 16 0M2 2a14 14 0 0 1 20 0'/><circle cx='12' cy='12' r='2'/>",
        ["weapon"]="<path d='M12 3v18M3 12h18M7 7l3 3m4 4 3 3m0-10-3 3m-4 4-3 3'/>",
        ["power"]="<path d='M12 2v9m-5-7a10 10 0 1 0 10 0'/>",
        ["aim"]="<circle cx='12' cy='12' r='5'/><path d='M12 0v6M12 18v6M0 12h6m12 0h6'/>",
        ["skull"]="<path d='M12 2a9 9 0 0 0-5 16v3h10v-3A9 9 0 0 0 12 2zM9 14h0m6 0h0M9 21v-4m6 4v-4'/>",
        ["link"]="<path d='M10 8 8 6a4 4 0 0 0-6 6l4 4a4 4 0 0 0 6 0l1-1m1-6 1-1a4 4 0 0 1 6 0 4 4 0 0 1 0 6l-4 4a4 4 0 0 1-6 0l-2-2'/>",
        ["pierce"]="<path d='M2 12h14M12 7l5 5-5 5M19 4v16M22 6v12'/>",
        ["intercept"]="<path d='M4 20 20 4M14 4h6v6'/><circle cx='8' cy='16' r='3'/>",
        ["ecm"]="<path d='M2 12c2-4 4-4 6 0s4 4 6 0 4-4 6 0M2 17c2-4 4-4 6 0s4 4 6 0 4-4 6 0'/>",
        ["probe"]="<path d='M12 2v6M8 8h8l-2 10h-4zM6 22h12'/>",
        ["empty"]="<path d='M7 12h10' stroke-dasharray='2 3'/>",
    };
    private static readonly Dictionary<(string,int),Texture2D> Cache=new();
    public static Texture2D Icon(string name,int pixels)
    {
        pixels=Math.Max(8,pixels);
        if(Cache.TryGetValue((name,pixels),out var cached))return cached;
        var image=new Image();
        string svg=$"<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24'><g fill='none' stroke='#fff' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'>{Paths.GetValueOrDefault(name,Paths["empty"])}</g></svg>";
        image.LoadSvgFromString(svg,pixels/24f);
        return Cache[(name,pixels)]=ImageTexture.CreateFromImage(image);
    }
}

public partial class Hud
{
    private Font _mono=null!;
    /// <summary>A prototype pane: diagonal dark fill and one thin cyan edge.</summary>
    internal void Pane(Rect2 r,float alpha=1,Color? border=null)
    {
        var top=new Color(HudStyle.PaneTop,HudStyle.PaneTop.A*alpha);var bottom=new Color(HudStyle.PaneBottom,HudStyle.PaneBottom.A*alpha);
        DrawPolygon(new[]{r.Position,new(r.End.X,r.Position.Y),r.End,new(r.Position.X,r.End.Y)},new[]{top,top.Lerp(bottom,.5f),bottom,top.Lerp(bottom,.5f)});
        DrawRect(r,border??new Color(HudStyle.Line,HudStyle.Line.A*alpha),false,1);
    }
    private void Icon(string name,Vector2 center,float size,Color color)
    {
        int px=(int)MathF.Round(size);
        DrawTextureRect(HudStyle.Icon(name,px),new Rect2(center-new Vector2(px,px)*.5f,new Vector2(px,px)),false,color);
    }
    private void Mono(Vector2 at,string text,int size,Color color,HorizontalAlignment align=HorizontalAlignment.Left,float width=-1)
        =>DrawString(_mono,at,HudText.Fit(_mono,text,HudFont(size),width),align,width,HudFont(size),color);
    private float MonoWidth(string text,int size)=>_mono.GetStringSize(text,fontSize:HudFont(size)).X;
    /// <summary>Thin track with a fill (prototype .bar/.rail).</summary>
    private void Rail(Rect2 r,float fraction,Color fill)
    {
        DrawRect(r,HudStyle.Track);
        DrawRect(new Rect2(r.Position,new Vector2(r.Size.X*Mathf.Clamp(fraction,0,1),r.Size.Y)),fill);
    }
    /// <summary>Unknown value: dim diagonal hatching, never a guessed fill.</summary>
    private void UnknownRail(Rect2 r)
    {
        DrawRect(r,HudStyle.Track);
        for(float x=r.Position.X;x<r.End.X;x+=5)DrawLine(new(x,r.End.Y),new(Mathf.Min(x+3,r.End.X),r.Position.Y),new Color(HudStyle.Dim,.45f),1);
    }
}
