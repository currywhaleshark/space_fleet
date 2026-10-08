using Godot;

namespace SpaceFleet.Game;

public static class FeedbackSettings
{
    private const string Path = "user://feedback.cfg";
    public static float Shake { get; private set; } = 75;
    public static void Load()
    {
        using var config = new ConfigFile();
        if (config.Load(Path) == Error.Ok)
        {
            float value = (float)config.GetValue("feedback", "shake", 75f).AsDouble();
            if (float.IsFinite(value)) Shake = Mathf.Clamp(value, 0, 100);
        }
    }
    public static void SetShake(float value, bool persist = true)
    {
        if (!float.IsFinite(value)) return;
        Shake = Mathf.Clamp(value, 0, 100);
        if (!persist) return;
        using var config = new ConfigFile(); config.SetValue("feedback", "shake", Shake);
        if (config.Save(Path) != Error.Ok) GD.PushWarning("화면 흔들림 설정을 저장하지 못했습니다.");
    }
}
