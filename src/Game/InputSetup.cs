using Godot;

namespace SpaceFleet.Game;

/// <summary>입력 액션을 코드에서 등록한다(project.godot의 직렬화 입력 맵보다 읽기 쉽다).</summary>
public static class InputSetup
{
    public const string ToggleMute = "toggle_mute";
    public const string WorldMap = "world_map";
    public const string RadarNear = "radar_near";
    public const string RadarFar = "radar_far";
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
    public const string Repair = "repair";
    public const string Fire = "fire_selected";
    public const string FireAssist = "fire_assist";
    public const string Practice = "shooting_practice";
    public const string SelectMainGun = "select_main_gun";
    public const string SelectMissile = "select_missile";
    public const string Telescope = "telescope";
    public const string SelectAntimatter = "select_antimatter";
    public const string JettisonAntimatter = "jettison_antimatter";
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
        Bind(ToggleMute, Key.F10);
        Bind(WorldMap, Key.M);
        Bind(RadarNear, Key.Pageup);
        Bind(RadarFar, Key.Pagedown);
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
        foreach(string old in new[]{"power_engines","power_shields","power_weapons","power_sensors","power_ecm","power_reset","launch_missile","fire_railgun"})
            if(InputMap.HasAction(old)) InputMap.EraseAction(old);
        Bind(SelectMainGun, Key.Key1);
        Bind(SelectMissile, Key.Key2);
        Bind(Repair, Key.F6);
        Bind(FireAssist, Key.T);
        Bind(Practice, Key.F7);
        Bind(Decoys, Key.C);
        Bind(SelectAntimatter, Key.Key3);
        Bind(JettisonAntimatter, Key.Backspace);
        Bind(MissileDrill, Key.F8);
        Bind(AimPart, Key.Y);
        Bind(OrderAttack, Key.G);
        Bind(OrderEscort, Key.H);
        Bind(OrderHold, Key.J);
        Bind(TimeSlower, Key.Bracketleft);
        Bind(TimeFaster, Key.Bracketright);
        BindMouse(Telescope, MouseButton.Right);
        BindMouse(Fire, MouseButton.Left);
    }

    private static void BindMouse(string action, MouseButton button)
    {
        if(!InputMap.HasAction(action)) InputMap.AddAction(action);
        InputMap.ActionEraseEvents(action);
        InputMap.ActionAddEvent(action,new InputEventMouseButton { ButtonIndex=button });
    }

    private static void Bind(string action, Key key)
    {
        if (!InputMap.HasAction(action))
            InputMap.AddAction(action);
        InputMap.ActionEraseEvents(action);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }
}
