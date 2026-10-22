using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace SpaceFleet.Game;

/// <summary>Keyboard preferences are independent of the fleet/research save. Edits are applied only after a verified save.</summary>
internal static class InputBindings
{
    private static Dictionary<string, Key> _keys = new();
    private static bool _loaded;
    internal static string? LoadWarning { get; private set; }
    internal static string PathName => ProjectSettings.GlobalizePath("user://keybindings.json");
    internal static Dictionary<string, Key> Current() => InputSetup.Defaults.ToDictionary(p => p.Key, p => _keys.GetValueOrDefault(p.Key,p.Value));
    internal static void Load(bool isolated)
    {
        if (_loaded) return;
        _loaded = true;
        if (isolated || !File.Exists(PathName)) return;
        try { _keys = Parse(File.ReadAllText(PathName)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { LoadWarning = "단축키 파일을 읽지 못해 기본키를 사용합니다. 원본은 보존됩니다."; }
        Apply();
    }
    internal static Dictionary<string,Key> Parse(string json)
    {
        var data=JsonSerializer.Deserialize<Dictionary<string,long>>(json)??throw new InvalidDataException("빈 단축키 파일");
        var result=new Dictionary<string,Key>(InputSetup.Defaults);
        foreach(var (action,value) in data)
        {
            if (!result.ContainsKey(action)) throw new InvalidDataException("알 수 없는 조작: "+action);
            result[action]=(Key)value;
        }
        if (Validate(result) is { } error) throw new InvalidDataException(error);
        return result;
    }
    internal static string? Validate(IReadOnlyDictionary<string,Key> keys)
    {
        if (keys.Count!=InputSetup.Defaults.Count || InputSetup.Defaults.Keys.Any(k=>!keys.ContainsKey(k))) return "조작 목록이 올바르지 않습니다.";
        foreach(var (action,key) in keys)
        {
            if(!Enum.IsDefined(key)||key==Key.None) return "지원하지 않는 키입니다.";
            if(action==InputSetup.ReleaseMouse&&key!=Key.Escape || action!=InputSetup.ReleaseMouse&&key is Key.Escape or Key.Alt or Key.Home)
                return "Esc · Alt · Home은 메뉴/이동/지도 기본 조작으로 예약되어 있습니다.";
        }
        var duplicate=keys.GroupBy(k=>k.Value).FirstOrDefault(g=>g.Count()>1);
        return duplicate is null?null:$"{KeyText(duplicate.Key)} 중복: {string.Join(" / ",duplicate.Select(p=>Name(p.Key)))}";
    }
    internal static void Save(IReadOnlyDictionary<string,Key> keys,string? path=null)
    {
        if(Validate(keys) is { } error)throw new InvalidDataException(error);
        string json=JsonSerializer.Serialize(keys.ToDictionary(p=>p.Key,p=>(long)p.Value),new JsonSerializerOptions{WriteIndented=true});
        Parse(json);
        string full=path??PathName, temporary=full+".tmp";
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(full))!);
        using(var stream=new FileStream(temporary,FileMode.Create,System.IO.FileAccess.Write,FileShare.None))
        {using var writer=new StreamWriter(stream,new System.Text.UTF8Encoding(false),leaveOpen:true);writer.Write(json);writer.Flush();stream.Flush(true);}
        if(File.Exists(full))File.Copy(full,full+".bak",true);
        File.Move(temporary,full,true);
        _keys=new(keys);LoadWarning=null;Apply();
    }
    internal static void Apply()
    {
        foreach(var (action,key) in Current())
        {
            Input.ActionRelease(action);
            InputMap.ActionEraseEvents(action);
            InputMap.ActionAddEvent(action,new InputEventKey{PhysicalKeycode=key});
        }
    }
    internal static string Text(string action)=>KeyText(_keys.GetValueOrDefault(action,InputSetup.Defaults.GetValueOrDefault(action)));
    internal static string HintKeys(string keys)=>System.Text.RegularExpressions.Regex.Replace(keys,@"(?<![A-Za-z0-9])(?:Backspace|Space|Shift|Ctrl|PgUp|PgDn|Tab|Esc|F[1-9]0?|[A-Z1-7]|\[|\])(?![A-Za-z0-9])",m=>
    {
        // Text is authored in the default layout; substitute once, so swapping keys cannot cascade.
        foreach(var (action,key) in InputSetup.Defaults)if(KeyText(key)==m.Value)return Text(action);
        return m.Value;
    });
    internal static string KeyText(Key key)=>key switch
    {Key.None=>"—",Key.Escape=>"Esc",Key.Pageup=>"PgUp",Key.Pagedown=>"PgDn",Key.Bracketleft=>"[",Key.Bracketright=>"]",_=>OS.GetKeycodeString(key)};
    internal static string Name(string action)=>action switch
    {
        InputSetup.ThrottleUp=>"스로틀 올리기",InputSetup.ThrottleDown=>"스로틀 내리기",InputSetup.ThrottleZero=>"스로틀 0",
        InputSetup.StrafeLeft=>"좌 이동 / 선회",InputSetup.StrafeRight=>"우 이동 / 선회",InputSetup.StrafeUp=>"상승 / 기수 올리기",InputSetup.StrafeDown=>"하강 / 기수 내리기",
        InputSetup.RollLeft=>"좌 롤",InputSetup.RollRight=>"우 롤",InputSetup.Boost=>"부스트",InputSetup.FlightAssist=>"비행보조",InputSetup.AssistStyle=>"비행보조 방식",
        InputSetup.SelectMainGun=>"주포 선택",InputSetup.SelectMissile=>"미사일 선택",InputSetup.SelectAntimatter=>"어뢰 선택",
        "tactical_1"=>"전술 슬롯 1","tactical_2"=>"전술 슬롯 2","tactical_3"=>"전술 슬롯 3","tactical_4"=>"전술 슬롯 4",
        InputSetup.PowerMenu=>"전력 라디얼",InputSetup.GunneryMenu=>"사격 라디얼",InputSetup.SquadMenu=>"편대 라디얼",InputSetup.DroneMenu=>"드론 라디얼",InputSetup.DamageControlMenu=>"응급복구 라디얼",
        InputSetup.InspectTarget=>"표적 지정",InputSetup.FireAssist=>"사격보조 / 교리",InputSetup.AimPart=>"조준 부위 변경",InputSetup.Decoys=>"디코이",InputSetup.JettisonAntimatter=>"반물질탄 비상 투기",
        InputSetup.WorldMap=>"전체 지도",InputSetup.RadarNear=>"레이더 축소",InputSetup.RadarFar=>"레이더 확대",InputSetup.TeamRoster=>"양팀 편성",InputSetup.ToggleHelp=>"도움말",InputSetup.ToggleMute=>"효과음 켜기 / 끄기",
        InputSetup.SpectatePrevious=>"이전 함선 관전",InputSetup.SpectateNext=>"다음 함선 관전",InputSetup.ReleaseMouse=>"메뉴 / 커서 해제",
        InputSetup.TimeSlower=>"전투 배속 낮추기",InputSetup.TimeFaster=>"전투 배속 높이기",
        InputSetup.SwitchShip=>"개발 · 함선 순환",InputSetup.ToggleOrigin=>"개발 · 렌더 원점",InputSetup.JumpFar=>"개발 · 원거리 도약",InputSetup.TestFire=>"개발 · 시험 발사",InputSetup.ShowModules=>"개발 · 모듈 표시",InputSetup.Repair=>"개발 · 전체 복구",InputSetup.Practice=>"개발 · 이동 표적",InputSetup.MissileDrill=>"개발 · 미사일 훈련",InputSetup.OrderAttack=>"개발 · 집중공격",InputSetup.OrderEscort=>"개발 · 호위",InputSetup.OrderHold=>"개발 · 위치 유지",
        _=>action
    };
}
