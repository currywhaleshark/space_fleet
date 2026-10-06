using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

/// <summary>부위 조준 대상. 중심은 함선 중심이다.</summary>
public enum AimSubsystem
{
    Center,
    Engines,
    Radiators,
    Guns,
    Sensors,
}

/// <summary>
/// 부위 조준 도우미. 종류별로 살아 있는 모듈 가운데 관측자에게 가장 가까운 것을 고른다(보이는 쪽 면).
/// 방열판은 냉각 모듈 중 방열판 구획 안에 있는 것("radiator"로 시작하는 ID).
/// </summary>
public static class Subsystems
{
    public static readonly AimSubsystem[] Cycle =
        { AimSubsystem.Center, AimSubsystem.Engines, AimSubsystem.Radiators, AimSubsystem.Guns, AimSubsystem.Sensors };

    public static string Label(AimSubsystem kind) => kind switch
    {
        AimSubsystem.Engines => "추진기",
        AimSubsystem.Radiators => "방열판",
        AimSubsystem.Guns => "주포",
        AimSubsystem.Sensors => "센서",
        _ => "중심",
    };

    public static bool Matches(ModuleDefinition module, AimSubsystem kind) => kind switch
    {
        AimSubsystem.Engines => module.Kind == ModuleKind.Thruster,
        AimSubsystem.Radiators => module.Kind == ModuleKind.Cooling && module.Id.StartsWith("radiator"),
        AimSubsystem.Guns => module.Kind == ModuleKind.Gun,
        AimSubsystem.Sensors => module.Kind == ModuleKind.Sensor,
        _ => false,
    };

    /// <summary>표적에서 고른 부위 모듈. 중심이거나 남은 모듈이 없으면 null.</summary>
    public static ModuleState? Pick(ShipBody target, AimSubsystem kind, Vec3d from)
    {
        if (kind == AimSubsystem.Center) return null;
        return target.Damage.Modules
            .Where(m => !m.Destroyed && Matches(m.Definition, kind))
            .OrderBy(m => (target.Position + Vec3d.From(target.Orientation * m.Definition.Center) - from).LengthSquared())
            .FirstOrDefault();
    }

    /// <summary>모듈의 월드 위치.</summary>
    public static Vec3d WorldPosition(ShipBody ship, ModuleDefinition module) =>
        ship.Position + Vec3d.From(ship.Orientation * module.Center);
}
