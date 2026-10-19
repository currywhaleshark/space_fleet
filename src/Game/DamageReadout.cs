using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

// Display-only names: module IDs, geometry and saved loadouts stay stable.
internal static class ModuleNames
{
    public static string Display(ModuleDefinition module)
    {
        if (module.Name.Any(c => c >= '가' && c <= '힣')) return module.Name;
        string id = module.Id.ToLowerInvariant();
        string side = id.Contains("starboard") ? "우현 " : id.Contains("port") ? "좌현 "
            : id.Contains("upper") || id.Contains("dorsal") ? "상부 " : id.Contains("lower") || id.Contains("ventral") ? "하부 " : "";
        string part = module.Kind switch
        {
            ModuleKind.Sensor => "센서", ModuleKind.Gun => "주포", ModuleKind.Magazine => "탄약고",
            ModuleKind.Generator => "발전기", ModuleKind.Reactor => "반응로",
            ModuleKind.Cooling => id.Contains("radiator") ? "방열판" : "냉각 장치",
            ModuleKind.Thruster => "주추진기", ModuleKind.ManeuverThruster => "기동 추진기",
            ModuleKind.PowerBus => "전력망", ModuleKind.ShieldEmitter => "실드 발생기",
            ModuleKind.AntimatterContainment => "반물질 격리용기", ModuleKind.ShieldBooster => "실드 증폭기",
            ModuleKind.SensorBooster => "보조 센서", ModuleKind.EcmSuite => "전자전 장비", ModuleKind.DroneBay => "드론 격납고",
            _ => "내부 모듈",
        };
        string number = id.Split('-').Last();
        return side + (int.TryParse(number, out int n) ? $"{n + (module.Kind == ModuleKind.Thruster ? 1 : 0)}번 " : "") + part;
    }

    public static string Display(HullSection section)
    {
        if (section.Name.Any(c => c >= '가' && c <= '힣')) return section.Name;
        string id = section.Id.ToLowerInvariant();
        string part = id.Contains("generator") ? "발전기 구획" : id.Contains("cooling") ? "냉각 구획"
            : id.Contains("radiator") ? "방열판" : id.Contains("engine") || id.Contains("engineering") ? "기관 구획"
            : id.Contains("sensor") || id.Contains("mast") ? "센서 구조물" : id.Contains("bridge") ? "함교"
            : id.Contains("missile") || id.Contains("bank-root") ? "미사일 발사대"
            : id.Contains("turret") || id.Contains("gun-") ? "주포 받침" : id.Contains("rail") ? "전방 주포 구조물"
            : id.Contains("defense") || id.Contains("pd-deck") ? "근접방어 받침" : id.Contains("probe") ? "프로브 격납부"
            : id.Contains("am-keel") ? "반물질 격리부" : id.Contains("fin") ? "안정판" : id.Contains("wing") ? "날개"
            : id.Contains("truss") || id.Contains("root") || id.Contains("pylon") ? "연결 구조물"
            : id.Contains("bow") || id.Contains("nose") || id.Contains("prow") ? "함수 장갑"
            : id.Contains("aft") ? "함미 장갑" : id.Contains("stores") ? "저장 구획" : "선체 장갑";
        string side = section.Center.X < -.1f ? "좌현 " : section.Center.X > .1f ? "우현 " : "";
        return side + part;
    }

    private static readonly ConditionalWeakTable<ShipDefinition, (string Raw, string Display)[]> Names = new();
    public static string Translate(ShipDefinition ship, string message)
    {
        var names = Names.GetValue(ship, d => d.Modules.SelectMany(m => new[] { (m.Name, Display(m)), (m.Id, Display(m)) })
            .Concat(d.HullSections.SelectMany(s => new[] { (s.Name, Display(s)), (s.Id, Display(s)) }))
            .Where(n => n.Item1 != n.Item2).Distinct().OrderByDescending(n => n.Item1.Length).ToArray());
        foreach (var (raw, shown) in names) message = message.Replace(raw, shown, StringComparison.OrdinalIgnoreCase);
        return message;
    }
}

internal readonly record struct DamageNotice(double Time, string Message, int Priority);
internal sealed class DamageReadout
{
    public const double RecentSeconds = 15;
    private ShipBody? _ship;
    private int _count;
    private DamageReport _last;
    private double _expires;
    private DamageNotice[] _shown = Array.Empty<DamageNotice>();
    public IReadOnlyList<DamageNotice> Recent(ShipBody ship, double time)
    {
        var reports = ship.Damage.Reports;
        var last = reports.Count == 0 ? default : reports[^1];
        if (_ship == ship && _count == reports.Count && last == _last && time < _expires) return _shown;
        _ship = ship; _count = reports.Count; _last = last;
        _shown = Select(reports, ship.Definition, time);
        _expires = _shown.Length == 0 ? double.PositiveInfinity : _shown.Min(r => r.Time) + RecentSeconds + .001;
        return _shown;
    }
    internal static DamageNotice[] Select(IEnumerable<DamageReport> reports, ShipDefinition ship, double time)
    {
        var seen = new HashSet<string>();
        var candidates = new List<DamageNotice>();
        foreach (var report in reports.Reverse())
        {
            if (report.Time > time || time - report.Time > RecentSeconds) continue;
            string message = ModuleNames.Translate(ship, report.Message);
            message=message.Replace("손상 · 100%","경미한 손상");
            // A newer recovery/damage report supersedes older reports for the same compartment.
            string key = (message.Contains("장갑") ? null : ship.Modules.FirstOrDefault(m => report.Message.StartsWith(m.Name+" ", StringComparison.OrdinalIgnoreCase)
                || report.Message.StartsWith(m.Id+" ", StringComparison.OrdinalIgnoreCase))?.Id)
                ?? (message.StartsWith("실드 흡수") ? "shield-hit" : message.StartsWith("선체 구조") ? "structure" : message);
            if (!seen.Add(key)) continue;
            int priority = message.Contains("격침") || message.Contains("격리 실패") ? 4
                : message.Contains("무력화 해제") ? 2
                : message.Contains("파괴") || message.Contains("단절") || message.Contains("무력화") || message.Contains("위험") ? 3
                : message.Contains("복구") || message.Contains("출력 저하") || message.Contains("실드 소진") || message.Contains("구조 손상") ? 2 : 1;
            candidates.Add(new(report.Time, message, priority));
        }
        return candidates.OrderByDescending(r => r.Priority).ThenByDescending(r => r.Time).Take(2).ToArray();
    }
}
