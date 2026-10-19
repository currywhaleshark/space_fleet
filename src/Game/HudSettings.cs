using System;
using Godot;

namespace SpaceFleet.Game;

public static class HudSettings
{
    private const string FileName = "user://hud.cfg";
    public static int TextPercent { get; private set; } = 100;
    public static float TextScale => TextPercent / 100f;
    public static int FontSize(int size) => (int)MathF.Round(size * TextScale);
    public static void Load()
    {
        using var config = new ConfigFile();
        if (config.Load(FileName) == Error.Ok)
            SetTextPercent((int)config.GetValue("hud", "text_percent", 100).AsInt32(), false);
    }
    public static void SetTextPercent(int value, bool persist = true)
    {
        TextPercent = value >= 123 ? 130 : value >= 108 ? 115 : 100;
        if (!persist) return;
        using var config = new ConfigFile();
        config.SetValue("hud", "text_percent", TextPercent);
        if (config.Save(FileName) != Error.Ok) GD.PushWarning("HUD 글자 크기를 저장하지 못했습니다.");
    }
    public static void Cycle() => SetTextPercent(TextPercent == 100 ? 115 : TextPercent == 115 ? 130 : 100);
}
