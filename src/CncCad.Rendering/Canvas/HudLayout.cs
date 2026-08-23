using Avalonia;

namespace CncCad.Rendering.Canvas;

public static class HudLayout
{
    public static Rect Place(Size canvas, Point cursor, Size hud, double offset = 16, double margin = 8)
    {
        var x = cursor.X + offset; var y = cursor.Y + offset;
        if (x + hud.Width > canvas.Width - margin) x = cursor.X - offset - hud.Width;
        if (y + hud.Height > canvas.Height - margin) y = cursor.Y - offset - hud.Height;
        return new(Math.Clamp(x, margin, Math.Max(margin, canvas.Width - hud.Width - margin)), Math.Clamp(y, margin, Math.Max(margin, canvas.Height - hud.Height - margin)), hud.Width, hud.Height);
    }
}
