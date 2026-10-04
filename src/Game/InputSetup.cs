using Godot;

namespace SpaceFleet.Game;

/// <summary>입력 액션을 코드에서 등록한다(project.godot의 직렬화 입력 맵보다 읽기 쉽다).</summary>
public static class InputSetup
{
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
    public const string SwitchShip = "switch_ship";
    public const string ToggleOrigin = "toggle_origin";
    public const string JumpFar = "jump_far";
    public const string ReleaseMouse = "release_mouse";

    public static void Register()
    {
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
        Bind(SwitchShip, Key.Tab);
        Bind(ToggleOrigin, Key.F2);
        Bind(JumpFar, Key.F3);
        Bind(ReleaseMouse, Key.Escape);
    }

    private static void Bind(string action, Key key)
    {
        if (!InputMap.HasAction(action))
            InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }
}
