using System.Collections.Generic;
using Godot;

namespace SpaceFleet.Game;

internal static class HudText
{
    public static string Fit(Font font, string text, int size, float width)
    {
        if (width < 0 || font.GetStringSize(text, fontSize: size).X <= width) return text;
        while (text.Length > 0 && font.GetStringSize(text + "…", fontSize: size).X > width) text = text[..^1];
        return text + "…";
    }
    public static List<string> Wrap(Font font, string text, int size, float width)
    {
        var lines = new List<string>();
        while (text.Length > 0)
        {
            if (font.GetStringSize(text, fontSize: size).X <= width) { lines.Add(text); break; }
            int length = 1;
            while (length < text.Length && font.GetStringSize(text[..(length + 1)], fontSize: size).X <= width) length++;
            int space = text.LastIndexOf(' ', length - 1, length);
            if (space > 0) length = space;
            lines.Add(text[..length]); text = text[length..].TrimStart();
        }
        return lines;
    }
}
