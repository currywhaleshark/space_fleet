using Godot;

namespace SpaceFleet.Game;

/// <summary>입력 액션을 코드에서 등록한다(project.godot의 직렬화 입력 맵보다 읽기 쉽다).</summary>
public static class InputSetup
{
    public const string ToggleMute = "toggle_mute";
    public const string PowerMenu = "power_menu";
    public const string GunneryMenu = "gunnery_menu";
    public const string SquadMenu = "squad_menu";
    public const string ThrottleUp = "throttle_up";
    public const string ThrottleDown = "throttle_down";
    public const string ThrottleZero = "throttle_zero";
    public const string StrafeLeft = "strafe_left";
    public const string StrafeRight = "strafe_right";
    public const string StrafeUp = "strafe_up";
    public const string StrafeDown = "strafe_down";
    public const string RollLeft = "roll_left";
    public const string RollRight = "roll_right";
    public const string Boost = "boost";
    public const string FlightAssist = "flight_assist";
    public const string AssistStyle = "assist_style";
    public const string SwitchShip = "switch_ship";
    public const string ToggleOrigin = "toggle_origin";
    public const string JumpFar = "jump_far";
    public const string ReleaseMouse = "release_mouse";
    public const string InspectTarget = "inspect_target";
    public const string TestFire = "test_fire";
    public const string ShowModules = "show_modules";
    public const string ToggleHelp = "toggle_help";
    public const string PowerEngines = "power_engines";
    public const string PowerShields = "power_shields";
    public const string PowerWeapons = "power_weapons";
    public const string PowerSensors = "power_sensors";
    public const string PowerEcm = "power_ecm";
    public const string PowerReset = "power_reset";
    public const string Repair = "repair";
    public const string Fire = "fire_railgun";
    public const string FireAssist = "fire_assist";
    public const string Practice = "shooting_practice";
    public const string LaunchMissile = "launch_missile";
    public const string Decoys = "decoys";
    public const string MissileDrill = "missile_drill";
    public const string AimPart = "aim_part";
    public const string OrderAttack = "order_attack";
    public const string OrderEscort = "order_escort";
    public const string OrderHold = "order_hold";
    public const string TimeSlower = "time_slower";
    public const string TimeFaster = "time_faster";

    public static void Register()
    {
        Bind(ToggleMute, Key.M);
        Bind(PowerMenu, Key.F);
        Bind(GunneryMenu, Key.B);
        Bind(SquadMenu, Key.N);
        Bind(ThrottleUp, Key.W);
        Bind(ThrottleDown, Key.S);
        Bind(ThrottleZero, Key.X);
        Bind(StrafeLeft, Key.A);
        Bind(StrafeRight, Key.D);
        Bind(StrafeUp, Key.Space);
        Bind(StrafeDown, Key.Ctrl);
        Bind(RollLeft, Key.Q);
        Bind(RollRight, Key.E);
        Bind(Boost, Key.Shift);
        Bind(FlightAssist, Key.Z);
        Bind(AssistStyle, Key.V);
        Bind(SwitchShip, Key.Tab);
        Bind(ToggleOrigin, Key.F2);
        Bind(JumpFar, Key.F3);
        Bind(ReleaseMouse, Key.Escape);
        Bind(InspectTarget, Key.R);
        Bind(TestFire, Key.F4);
        Bind(ShowModules, Key.F5);
        Bind(ToggleHelp, Key.F1);
        Bind(PowerEngines, Key.Key1);
        Bind(PowerShields, Key.Key2);
        Bind(PowerWeapons, Key.Key3);
        Bind(PowerSensors, Key.Key4);
        Bind(PowerEcm, Key.Key5);
        Bind(PowerReset, Key.Key0);
        Bind(Repair, Key.F6);
        Bind(FireAssist, Key.T);
        Bind(Practice, Key.F7);
        Bind(Decoys, Key.C);
        Bind(MissileDrill, Key.F8);
        Bind(AimPart, Key.Y);
        Bind(OrderAttack, Key.G);
        Bind(OrderEscort, Key.H);
        Bind(OrderHold, Key.J);
        Bind(TimeSlower, Key.Bracketleft);
        Bind(TimeFaster, Key.Bracketright);
        if (!InputMap.HasAction(LaunchMissile)) InputMap.AddAction(LaunchMissile);
        InputMap.ActionAddEvent(LaunchMissile, new InputEventMouseButton { ButtonIndex = MouseButton.Right });
        if (!InputMap.HasAction(Fire)) InputMap.AddAction(Fire);
        InputMap.ActionAddEvent(Fire, new InputEventMouseButton { ButtonIndex = MouseButton.Left });
    }

    private static void Bind(string action, Key key)
    {
        if (!InputMap.HasAction(action))
            InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }
}
