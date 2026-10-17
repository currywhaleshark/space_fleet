using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

/// <summary>One pending edit, committed only by its explicit confirmation button.</summary>
internal partial class MenuConfirmation : Control
{
    private bool _closed;
    private Control _owner=null!;
    private string? _focus;
    private Action _apply=null!;
    private ScrollContainer _scroll=null!;
    internal Button ConfirmButton { get; private set; }=null!;
    internal Button CancelButton { get; private set; }=null!;
    internal string Body { get; private set; }="";
    internal static MenuConfirmation? Pending(Control owner)=>owner.GetChildren().OfType<MenuConfirmation>().FirstOrDefault();
    internal static string Price(DesignFamily design,long points,long cost)=>
        $"{(design==DesignFamily.Earth?"지구권":"화성권")} 연구 점수   {points:N0} → {points-cost:N0} RP   ·   비용 {cost:N0} RP";

    internal static void Show(Control owner,Font font,string title,string body,string price,string caution,string confirm,Action apply)
    {
        if(Pending(owner) is not null)return;
        var dialog=new MenuConfirmation{_owner=owner,_focus=MenuStyles.FocusKey(owner),_apply=apply,Body=body,
            Size=new(1600,900),MouseFilter=MouseFilterEnum.Stop};
        owner.AddChild(dialog);
        dialog.AddChild(new ColorRect{Size=dialog.Size,Color=new(0,0,0,.76f),MouseFilter=MouseFilterEnum.Ignore});
        var panel=new Panel{Position=new(350,115),Size=new(900,670)};
        panel.AddThemeStyleboxOverride("panel",new StyleBoxFlat{BgColor=new(.025f,.055f,.08f),BorderColor=new(.25f,.43f,.54f),
            BorderWidthBottom=1,BorderWidthTop=1,BorderWidthLeft=1,BorderWidthRight=1});dialog.AddChild(panel);
        Label Text(string text,Vector2 pos,Vector2 size,int fontSize,Color color)
        {
            var label=new Label{Text=text,Position=pos,Size=size,AutowrapMode=TextServer.AutowrapMode.WordSmart,MouseFilter=MouseFilterEnum.Ignore};
            label.AddThemeFontOverride("font",font);label.AddThemeFontSizeOverride("font_size",fontSize);
            label.AddThemeColorOverride("font_color",color);panel.AddChild(label);return label;
        }
        Text(title,new(30,23),new(840,45),28,Colors.White);
        var scroll=new ScrollContainer{Position=new(30,84),Size=new(840,355),HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,
            FocusMode=FocusModeEnum.None};panel.AddChild(scroll);
        dialog._scroll=scroll;
        var content=Text(body,Vector2.Zero,Vector2.Zero,20,new(.83f,.9f,.96f));panel.RemoveChild(content);
        content.SizeFlagsHorizontal=SizeFlags.ExpandFill;scroll.AddChild(content);
        Text(price,new(30,461),new(840,40),21,new(.43f,.81f,1));
        Text(caution,new(30,508),new(840,65),18,new(1,.74f,.43f));
        Button Button(string caption,float x,bool primary)
        {
            var button=new Button{Text=caption,Position=new(x,594),Size=new(405,48)};
            MenuStyles.Apply(button,font,new(.35f,.75f,1),primary);button.AddThemeFontSizeOverride("font_size",20);
            panel.AddChild(button);return button;
        }
        dialog.CancelButton=Button("취소 · Esc",30,false);dialog.CancelButton.Pressed+=()=>dialog.Close(false);
        dialog.ConfirmButton=Button(confirm,465,true);dialog.ConfirmButton.Pressed+=()=>dialog.Close(true);
        foreach(var (a,b) in new[]{(dialog.CancelButton,dialog.ConfirmButton),(dialog.ConfirmButton,dialog.CancelButton)})
        {
            var path=a.GetPathTo(b);a.FocusNext=path;a.FocusPrevious=path;
            a.FocusNeighborLeft=path;a.FocusNeighborRight=path;a.FocusNeighborTop=path;a.FocusNeighborBottom=path;
        }
        dialog.CancelButton.GrabFocus();
    }
    public override void _Input(InputEvent e)
    {
        if(e is not InputEventKey{Pressed:true} key)return;
        Key code=key.Keycode!=Key.None?key.Keycode:key.PhysicalKeycode;
        if(code==Key.Escape){GetViewport().SetInputAsHandled();Close(false);}
        else if(code is Key.Pageup or Key.Pagedown or Key.Home or Key.End)
        {
            var bar=_scroll.GetVScrollBar();
            bar.Value=code switch{Key.Home=>0,Key.End=>bar.MaxValue,Key.Pageup=>bar.Value-280,_=>bar.Value+280};
            GetViewport().SetInputAsHandled();
        }
        else if(key.Echo&&code is Key.Enter or Key.KpEnter or Key.Space)GetViewport().SetInputAsHandled();
    }
    internal void Cancel()=>Close(false);
    private void Close(bool apply)
    {
        if(_closed||!IsInsideTree())return;
        _closed=true;
        _owner.RemoveChild(this);QueueFree();
        MenuStyles.RestoreFocus(_owner,_focus);
        if(apply)_apply();
    }
}
