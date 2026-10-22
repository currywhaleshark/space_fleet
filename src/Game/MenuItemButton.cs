using Godot;

namespace SpaceFleet.Game;

/// <summary>Image-led native button with full mouse and keyboard details.</summary>
public partial class MenuItemButton : Button
{
    public Texture2D? Picture { get; init; }
    public string Caption { get; init; }="";
    public string Detail { get; init; }="";
    public string Marker { get; init; }="";
    public bool Portrait { get; init; }
    public Font Typeface { get; init; }=null!;
    private PopupPanel? _details;
    public override void _Ready()
    {
        AccessibilityName=Caption;
        AccessibilityDescription=TooltipText;
        ClipContents=true;
        var imageRect=Portrait?new Rect2(8,4,Size.X-16,Size.Y-(Detail.Length>0?48:29)):new Rect2(10,8,Size.Y-16,Size.Y-16);
        // Set expansion before the texture/size: otherwise the texture's natural
        // minimum size expands the child beyond the clipped card on first layout.
        if(Picture is not null)AddChild(new TextureRect{ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,Texture=Picture,
            Position=imageRect.Position,Size=imageRect.Size,MouseFilter=MouseFilterEnum.Ignore});
        float x=Portrait?8:Picture is null?14:Size.Y+8,width=Size.X-x-10;
        AddText(Caption,new(x,Portrait?imageRect.End.Y:Detail.Length>0?Size.Y/2-23:Size.Y/2-13),new(width,26),16);
        if(Detail.Length>0)AddText(Detail,new(x,Portrait?Size.Y-23:Size.Y/2+4),new(width,23),14,MenuStyles.Muted);
        if(Marker.Length>0)AddText(Marker,new(10,5),new(36,20),13,new(.55f,.83f,1)).HorizontalAlignment=HorizontalAlignment.Left;
    }
    private Label AddText(string text,Vector2 at,Vector2 size,int fontSize,Color? color=null)
    {
        while(fontSize>14&&Typeface.GetStringSize(text,fontSize:fontSize).X>size.X)fontSize--;
        var label=new Label{Text=text,Position=at,Size=size,MouseFilter=MouseFilterEnum.Ignore,
            HorizontalAlignment=Portrait?HorizontalAlignment.Center:HorizontalAlignment.Left,
            TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis};
        label.AddThemeFontOverride("font",Typeface);label.AddThemeFontSizeOverride("font_size",fontSize);
        label.AddThemeColorOverride("font_color",color??new Color(.82f,.9f,.96f));AddChild(label);return label;
    }
    public override GodotObject _MakeCustomTooltip(string forText)=>Details(forText);
    private Control Details(string text)
    {
        var panel=new PanelContainer{CustomMinimumSize=new(390,0)};
        panel.AddThemeStyleboxOverride("panel",new StyleBoxFlat{BgColor=new(.025f,.055f,.08f),BorderColor=new(.35f,.65f,.8f),
            BorderWidthLeft=1,BorderWidthTop=1,BorderWidthRight=1,BorderWidthBottom=1,
            ContentMarginLeft=16,ContentMarginRight=16,ContentMarginTop=12,ContentMarginBottom=12});
        var label=new Label{Text=text,CustomMinimumSize=new(390,0),AutowrapMode=TextServer.AutowrapMode.WordSmart};
        label.AddThemeFontOverride("font",Typeface);label.AddThemeFontSizeOverride("font_size",17);
        label.AddThemeColorOverride("font_color",new(.84f,.92f,.98f));panel.AddChild(label);return panel;
    }
    internal void ShowDetails()
    {
        if(_details is not null||TooltipText.Length==0)return;
        _details=new PopupPanel{Title=Caption,Transient=true};AddChild(_details);
        _details.AddChild(Details(TooltipText));
        _details.WindowInput+=e=>
        {
            if(e is InputEventKey{Pressed:true,Keycode:Key.Escape})
            {var popup=_details;popup?.SetInputAsHandled();popup?.Hide();}
        };
        _details.PopupHide+=()=>{_details?.QueueFree();_details=null;};
        _details.PopupCentered();
    }
    public override void _GuiInput(InputEvent e)
    {
        if(e.IsActionPressed(InputSetup.ToggleHelp)){ShowDetails();AcceptEvent();}
    }
}
