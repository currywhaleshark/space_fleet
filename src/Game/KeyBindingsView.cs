using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace SpaceFleet.Game;

public partial class KeyBindingsView : Control
{
    public Action Closed { get; set; } = null!;
    private Dictionary<string,Key> _draft = InputBindings.Current();
    private readonly Dictionary<string,Button> _rows = new();
    private Font _font=null!;
    private Label _message=null!;
    private Button _save=null!;
    private string? _listening;
    internal IReadOnlyDictionary<string,Key> Draft=>_draft;
    public override void _Ready()
    {
        Size=new(1600,900);MouseFilter=MouseFilterEnum.Stop;
        _font=new SystemFont{FontNames=new[]{"Malgun Gothic","Segoe UI"}};
        Label("단축키 설정",new(230,66,1100,55),32);
        Label("변경할 키를 선택하고 새 키를 누르세요 · Esc 입력 취소 · 저장 전까지 기존 설정 유지",new(230,129,1140,42),18);
        Label("좌클릭 발사 · 우클릭 망원 · 중클릭/휠 관찰 · Alt 평행이동 · 지도 Shift 클릭/ Home은 고정 조작입니다.",new(230,170,1140,42),16);
        var scroll=new ScrollContainer{Position=new(230,230),Size=new(1140,470),HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,FollowFocus=true};AddChild(scroll);
        var list=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};list.AddThemeConstantOverride("separation",8);scroll.AddChild(list);
        foreach(var (action,key) in _draft.OrderBy(p=>Group(p.Key)).ThenBy(p=>Array.IndexOf(InputSetup.Defaults.Keys.ToArray(),p.Key)))
        {
            var row=new HBoxContainer{CustomMinimumSize=new(0,42)};list.AddChild(row);
            var label=new Label{Text=InputBindings.Name(action),SizeFlagsHorizontal=SizeFlags.ExpandFill};Style(label,18);row.AddChild(label);
            var button=Button(InputBindings.KeyText(key),()=>Listen(action));button.CustomMinimumSize=new(190,42);
            button.Disabled=action==InputSetup.ReleaseMouse;row.AddChild(button);_rows.Add(action,button);
        }
        _message=Label(InputBindings.LoadWarning??"4–7 전술 모듈 · K 드론 · L 응급복구",new(230,711,1140,52),18);
        var reset=Button("기본값 복원",Reset);reset.Position=new(230,795);reset.Size=new(230,50);AddChild(reset);
        var cancel=Button("취소",()=>Closed());cancel.Position=new(850,795);cancel.Size=new(230,50);AddChild(cancel);
        _save=Button("저장하고 돌아가기",Save);_save.Position=new(1100,795);_save.Size=new(270,50);AddChild(_save);
        _rows.Values.First().GrabFocus();Refresh();
    }
    private static int Group(string action)=>InputBindings.Name(action).StartsWith("개발")?2:action.StartsWith("tactical")?0:1;
    private void Style(Control c,int size){c.AddThemeFontOverride("font",_font);c.AddThemeFontSizeOverride("font_size",size);c.AddThemeColorOverride("font_color",new Color(.81f,.89f,.96f));}
    private Label Label(string text,Rect2 rect,int size){var label=new Label{Text=text,Position=rect.Position,Size=rect.Size,AutowrapMode=TextServer.AutowrapMode.WordSmart};Style(label,size);AddChild(label);return label;}
    private Button Button(string text,Action action){var b=new Button{Text=text};MenuStyles.Apply(b,_font,new(.81f,.89f,.96f));b.AddThemeFontSizeOverride("font_size",18);b.Pressed+=action;return b;}
    internal void Listen(string action){_listening=action;_message.Text=$"{InputBindings.Name(action)} · 새 키를 누르세요 (Esc 취소)";Refresh();}
    internal void Assign(Key key)
    {
        if(_listening is not { } action)return;
        if(key==Key.Escape){_listening=null;_message.Text="입력을 취소했습니다.";Refresh();return;}
        _draft[action]=key;_listening=null;
        _message.Text=InputBindings.Validate(_draft)??"변경했습니다. 저장하면 적용됩니다.";Refresh();
    }
    internal void Reset(){_draft=new(InputSetup.Defaults);_listening=null;_message.Text="기본값으로 복원했습니다. 저장하면 적용됩니다.";Refresh();}
    private void Refresh()
    {
        foreach(var (action,b) in _rows)
        {
            bool conflict=_draft.Count(p=>p.Value==_draft[action])>1;
            b.Text=_listening==action?"키 입력 대기…":InputBindings.KeyText(_draft[action])+(conflict?" · 중복":"");
            b.Modulate=conflict?new Color(1,.65f,.4f):Colors.White;
        }
        _save.Disabled=_listening is not null||InputBindings.Validate(_draft) is not null;
    }
    private void Save()
    {
        try{InputBindings.Save(_draft);Closed();}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException)
        {_message.Text="저장하지 못했습니다 · "+e.Message;}
    }
    public override void _Input(InputEvent e)
    {
        if(e is not InputEventKey{Pressed:true,Echo:false} key)return;
        Key code=key.PhysicalKeycode!=Key.None?key.PhysicalKeycode:key.Keycode;
        if(_listening is not null){Assign(code);GetViewport().SetInputAsHandled();}
        else if(code==Key.Escape){Closed();GetViewport().SetInputAsHandled();}
    }
    public override void _Process(double delta)
    {
        var viewport=GetViewportRect().Size;float scale=Mathf.Min(viewport.X/1600,viewport.Y/900);
        Scale=Vector2.One*scale;Position=(viewport-Size*scale)*.5f;
    }
    public override void _Notification(int what)
    {
        if(what==NotificationApplicationFocusOut&&_listening is not null)Assign(Key.Escape);
    }
    public override void _Draw()=>DrawRect(new(0,0,1600,900),new(.012f,.025f,.043f));
}
