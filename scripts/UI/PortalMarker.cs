using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// Points at the open portal: a marker over it while it is on screen, an arrow
/// pinned to the screen edge while it is not, with the distance either way.
///
/// The portal's light beam already reads from most of the map, but not from
/// behind the camera, and "where is the exit" is the one question a player who
/// has just won a boss fight in a corner should never have to search for.
///
/// Drawn with <see cref="CanvasItem"/> calls rather than built from nodes: the
/// shape changes every frame with the camera, and moving a polygon is simpler
/// than re-anchoring a tree of controls.
/// </summary>
public sealed partial class PortalMarker : Control
{
    private static readonly Color Tint = new(0.55f, 1.00f, 0.92f);
    private static readonly Color Shadow = new(0.02f, 0.03f, 0.05f, 0.85f);

    /// <summary>Distance from the screen edge for the off-screen arrow. Far
    /// enough in that it clears the XP strip and the corner readouts.</summary>
    private const float EdgeMargin = 56f;

    private Vector3? _target;
    private float _time;

    public override void _Ready()
    {
        Name = "PortalMarker";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        _target = Main.Instance?.Stages?.PortalPosition;
        Visible = _target is not null;
        if (Visible) QueueRedraw();
    }

    public override void _Draw()
    {
        if (_target is not Vector3 portal) return;
        Camera3D? camera = GetViewport().GetCamera3D();
        if (camera is null) return;

        Vector3 aim = portal + Vector3.Up * 2.7f;
        Vector2 size = GetViewportRect().Size;
        Vector2 centre = size * 0.5f;

        float distance = Player.Instance is { } p && IsInstanceValid(p)
            ? new Vector2(p.GlobalPosition.X - portal.X, p.GlobalPosition.Z - portal.Z).Length()
            : 0f;
        string label = Loc.F("PORTAL  {0:0}m", distance);


        bool behind = camera.IsPositionBehind(aim);
        Vector2 screen = camera.UnprojectPosition(aim);
        var safe = new Rect2(new Vector2(EdgeMargin, EdgeMargin), size - new Vector2(EdgeMargin, EdgeMargin) * 2f);

        float bob = Mathf.Sin(_time * 5f) * 4f;
        Font font = ThemeDB.FallbackFont;

        if (!behind && safe.HasPoint(screen))
        {
            // On screen: a diamond hovering over the ring, bobbing so it reads as
            // a marker and not as a piece of the HUD that happens to be there.
            Vector2 at = screen + new Vector2(0f, -18f + bob);
            DrawDiamond(at + new Vector2(2f, 2f), 11f, Shadow);
            DrawDiamond(at, 11f, Tint);
            DrawLabel(font, at + new Vector2(0f, -20f), label);
            return;
        }

        // Off screen: project the direction onto the screen and pin an arrow to
        // the edge. A point behind the camera unprojects mirrored, so flip it.
        Vector2 dir = screen - centre;
        if (behind) dir = -dir;
        if (dir.LengthSquared() < 1f) dir = Vector2.Down;
        dir = dir.Normalized();

        // Scale the direction until it touches the safe rectangle's border.
        Vector2 half = safe.Size * 0.5f;
        float sx = Mathf.Abs(dir.X) > 1e-4f ? half.X / Mathf.Abs(dir.X) : float.MaxValue;
        float sy = Mathf.Abs(dir.Y) > 1e-4f ? half.Y / Mathf.Abs(dir.Y) : float.MaxValue;
        Vector2 tip = centre + dir * Mathf.Min(sx, sy) + dir * bob;

        DrawArrow(tip + new Vector2(2f, 2f), dir, Shadow);
        DrawArrow(tip, dir, Tint);
        DrawLabel(font, tip - dir * 42f, label);
    }

    private void DrawDiamond(Vector2 at, float r, Color color)
        => DrawColoredPolygon(new[]
        {
            at + new Vector2(0f, -r), at + new Vector2(r * 0.7f, 0f),
            at + new Vector2(0f, r), at + new Vector2(-r * 0.7f, 0f),
        }, color);

    private void DrawArrow(Vector2 tip, Vector2 dir, Color color)
    {
        Vector2 side = new(-dir.Y, dir.X);
        DrawColoredPolygon(new[] { tip, tip - dir * 26f + side * 15f, tip - dir * 18f, tip - dir * 26f - side * 15f }, color);
    }

    private void DrawLabel(Font font, Vector2 centreBottom, string text)
    {
        const int size = 16;
        Vector2 extent = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        Vector2 at = centreBottom - new Vector2(extent.X * 0.5f, 0f);
        // Outline first: this is drawn over a lit 3D scene, and bare text is lost
        // the moment something bright passes behind it.
        DrawStringOutline(font, at, text, HorizontalAlignment.Left, -1, size, 6, Shadow);
        DrawString(font, at, text, HorizontalAlignment.Left, -1, size, Tint);
    }
}
