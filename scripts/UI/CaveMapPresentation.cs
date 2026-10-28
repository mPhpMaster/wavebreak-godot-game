using Godot;
using Wavebreak.Data;
using Wavebreak.Game;
using Wavebreak.Meta;

namespace Wavebreak.UI;

public static class CaveMapPresentation
{
    public static readonly Color Ink = new(0.025f, 0.045f, 0.065f, 0.95f);
    public static string Name(CaveMapMarkerKind kind, bool compact = false) => Loc.T(kind == CaveMapMarkerKind.Entrance
        ? compact ? "Entrance" : "Cave entrance" : compact ? "Exit" : "Cave exit");
    public static string Elevation(Vector3 portal, Vector3 player)
    {
        int meters = Mathf.RoundToInt(portal.Y - player.Y);
        string arrow = meters > 0 ? "↑" : meters < 0 ? "↓" : "=";
        string value = meters > 0 ? $"+{meters}" : meters.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Loc.F("{0} {1} m", arrow, value);
    }

    public static void Icon(Control canvas, Vector2 at, CaveMapMarkerKind kind, float radius)
    {
        Color color = kind == CaveMapMarkerKind.Entrance ? new Color(0.5f, 0.95f, 1) : new Color(1, 0.86f, 0.5f);
        canvas.DrawCircle(at, radius + 2, Ink);
        if (kind == CaveMapMarkerKind.Entrance)
        {
            canvas.DrawPolyline(new[] { at + new Vector2(-radius, radius), at + new Vector2(-radius, -radius * 0.25f),
                at + new Vector2(-radius * 0.55f, -radius), at + new Vector2(radius * 0.55f, -radius),
                at + new Vector2(radius, -radius * 0.25f), at + new Vector2(radius, radius) }, color, 2, true);
            canvas.DrawLine(at + new Vector2(-radius, radius), at + new Vector2(radius, radius), color, 2, true);
        }
        else
        {
            canvas.DrawRect(new Rect2(at - Vector2.One * radius, Vector2.One * radius * 2), color, false, 2);
            canvas.DrawLine(at + Vector2.Left * radius * 0.4f, at + Vector2.Right * (radius + 2), color, 2, true);
            canvas.DrawPolyline(new[] { at + new Vector2(radius * 0.5f, -radius * 0.6f), at + Vector2.Right * (radius + 2),
                at + new Vector2(radius * 0.5f, radius * 0.6f) }, color, 2, true);
        }
    }

    public static void Label(Control canvas, string text, Vector2 center, int size)
    {
        Font font = ThemeDB.FallbackFont;
        Vector2 extent = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        canvas.DrawRect(new Rect2(center - new Vector2(extent.X * 0.5f + 4, 9), new Vector2(extent.X + 8, 19)), Ink);
        Vector2 baseline = center + new Vector2(-extent.X * 0.5f, (font.GetAscent(size) - font.GetDescent(size)) * 0.5f);
        canvas.DrawString(font, baseline, text, HorizontalAlignment.Left, -1, size, Colors.White);
    }
}
