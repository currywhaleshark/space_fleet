using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

public enum ControlScheme { Pilot, Helm }

public partial class ScaleTest
{
    public ControlScheme Scheme => Controlled is { } view ? SchemeFor(view.Body) : ControlScheme.Pilot;
    public static ControlScheme SchemeFor(ShipBody ship) =>
        ship.Class.Kind == HullKind.Interceptor ? ControlScheme.Pilot : ControlScheme.Helm;

    private ShipControl PlayerControl()
    {
        if (MapControlsBlocked) return new ShipControl
        { Thrust = new Vector3(0, 0, Throttle), FlightAssist = _flightAssist, Style = AssistStyle };
        bool translate = Scheme == ControlScheme.Pilot || Input.IsPhysicalKeyPressed(Key.Alt);
        float horizontal = Input.GetAxis(InputSetup.StrafeLeft, InputSetup.StrafeRight);
        float vertical = Input.GetAxis(InputSetup.StrafeDown, InputSetup.StrafeUp);
        return new ShipControl
        {
            Thrust = new Vector3((translate ? horizontal : 0) + (_shot?.Strafe.X ?? 0),
                (translate ? vertical : 0) + (_shot?.Strafe.Y ?? 0), Throttle),
            Yaw = (translate ? 0 : horizontal) + (_shot?.HelmYaw ?? 0),
            Pitch = (translate ? 0 : vertical) + (_shot?.HelmPitch ?? 0),
            Roll = Input.GetAxis(InputSetup.RollLeft, InputSetup.RollRight) + (_shot?.Roll ?? 0),
            Boost = Input.IsActionPressed(InputSetup.Boost),
            FlightAssist = _flightAssist, Style = AssistStyle,
            HelmForward = Scheme == ControlScheme.Pilot ? Camera.AimForward : null,
        };
    }

    private void SetControlScheme(ShipBody ship)
    {
        Camera.ResetTelescope();
        Camera.Mode = SchemeFor(ship) == ControlScheme.Pilot ? CameraMode.MouseAim : CameraMode.ShipFollow;
        Camera.ResetAim(ship.Orientation);
        Input.MouseMode = SchemeFor(ship) == ControlScheme.Pilot && _shot is null && !WorldMapOpen
            ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
    }
}
