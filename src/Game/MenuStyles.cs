using System.Linq;
using Godot;

namespace SpaceFleet.Game;

/// <summary>Shared menu states; selection, keyboard focus and unavailable actions have different treatments.</summary>
internal static class MenuStyles
{
    internal static readonly Color Muted=new(.58f,.68f,.77f);
    internal static void Apply(Button button,Font font,Color text,bool primary=false,bool accent=false)
    {
        button.SetMeta("focus_key",button.Text.Split('\n')[0]);
        button.AddThemeFontOverride("font",font);
        button.AddThemeColorOverride("font_color",primary?new(.025f,.055f,.085f):text);
        button.AddThemeColorOverride("font_hover_color",primary?new(.025f,.055f,.085f):Colors.White);
        button.AddThemeColorOverride("font_pressed_color",primary?new(.025f,.055f,.085f):Colors.White);
        button.AddThemeColorOverride("font_focus_color",primary?new(.025f,.055f,.085f):Colors.White);
        button.AddThemeColorOverride("font_disabled_color",Muted);
        foreach(string state in new[]{"normal","hover","pressed","disabled"})
            button.AddThemeStyleboxOverride(state,new StyleBoxFlat{
                BgColor=primary&&state!="disabled" ? (state=="hover"?new(.6f,.85f,1):new(.35f,.75f,1))
                    :state=="hover"?new(.06f,.15f,.22f):new(.025f,.06f,.09f),
                BorderColor=state=="disabled"?new(.13f,.22f,.29f):accent||primary?text:new(.17f,.30f,.39f),
                BorderWidthBottom=1,BorderWidthTop=1,BorderWidthLeft=1,BorderWidthRight=1,
                CornerRadiusBottomLeft=3,CornerRadiusBottomRight=3,CornerRadiusTopLeft=3,CornerRadiusTopRight=3});
        button.AddThemeStyleboxOverride("focus",new StyleBoxFlat{DrawCenter=false,BorderColor=Colors.White,
            BorderWidthBottom=2,BorderWidthTop=2,BorderWidthLeft=2,BorderWidthRight=2,
            ExpandMarginBottom=3,ExpandMarginTop=3,ExpandMarginLeft=3,ExpandMarginRight=3});
    }
    internal static string? FocusKey(Control root)
    {
        var focus=root.GetViewport().GuiGetFocusOwner();
        return focus is not null&&root.IsAncestorOf(focus)&&focus.HasMeta("focus_key")?focus.GetMeta("focus_key").AsString():null;
    }
    internal static void RestoreFocus(Control root,string? key)
    {
        if(key is null)return;
        var buttons=root.FindChildren("*","Button",true,false).OfType<Button>().ToArray();
        var target=buttons.FirstOrDefault(b=>!b.Disabled&&b.GetMeta("focus_key","").AsString()==key)
            ??buttons.FirstOrDefault(b=>!b.Disabled&&b.GetMeta("focus_key","").AsString()=="research-action")
            ??buttons.FirstOrDefault(b=>!b.Disabled);
        target?.GrabFocus();
    }
}
