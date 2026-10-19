using System;
using Godot;

namespace SpaceFleet.Game;

// The bottom instruments share a layout; shrinking one panel must not erase its contents.
internal readonly record struct CombatHudLayout(Rect2 Instruments, Rect2 Gunnery, Vector2 Weapons)
{
    public Rect2 Selection => new(Weapons - new Vector2(0, 27), new Vector2(390, 24));
    public Vector2 HitFeedback(Vector2 center)
    {
        if(Gunnery.Size.X==0 && center.X<780)return center-new Vector2(0,64);
        Vector2 at=center+new Vector2(0,83);
        return Gunnery.Size.X>0 && Gunnery.Intersects(new Rect2(at-new Vector2(140,20),new Vector2(280,36)))
            ?new Vector2(center.X,Math.Min(center.Y-64,Gunnery.Position.Y-24)):at;
    }
    public static CombatHudLayout For(Vector2 screen, bool helm, int mounts = 1, float textScale = 1)
    {
        float available = Math.Max(300, screen.X - 556), bottom = screen.Y - 32;
        float instrumentHeight = 118 + MathF.Ceiling((textScale-1)*80);
        var instruments = new Rect2(screen.X * .5f - 200, bottom - instrumentHeight, 400, instrumentHeight);
        var gun = new Rect2();
        float weaponY = instruments.Position.Y - 39;
        if (helm)
        {
            float height = 112 + MathF.Ceiling(18*textScale) * Math.Max(1, (mounts + 3) / 4);
            if (available >= 624)
            {
                float instrumentWidth = Math.Min(400, available - 344);
                float gunWidth = Math.Min(360, available - instrumentWidth - 12);
                float left = (screen.X - instrumentWidth - 12 - gunWidth) * .5f;
                instruments = new(left, bottom - instrumentHeight, instrumentWidth, instrumentHeight);
                gun = new(instruments.End.X + 12, bottom - height, gunWidth, height);
                weaponY = Math.Min(instruments.Position.Y, gun.Position.Y) - 39;
            }
            else
            {
                // Very small windows keep the full firing display in a second row.
                float width = Math.Min(360, Math.Max(332,screen.X - 616));
                gun = new(Math.Min((screen.X - width) * .5f,screen.X-344-width), weaponY - 39 - height, width, height);
            }
        }
        return new(instruments, gun, new Vector2(screen.X * .5f - 195, weaponY));
    }
}
