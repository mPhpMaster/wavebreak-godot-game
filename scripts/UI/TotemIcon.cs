using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;

namespace Wavebreak.UI;

/// <summary>
/// The Tide Totem's map marker, shared by the minimap and the Tab sheet's big
/// map: a little upright aqua totem (two stacked rounded blocks, two dark eyes)
/// with an ink outline, so it reads apart from the chest squares and the
/// shrine diamonds. Drawn only for awake totems on revealed ground, the rule
/// chests and shrines follow (see "Fog of war" in ARCHITECTURE.md).
/// </summary>
public static class TotemIcon
{
    private static readonly Color Ink = new(0.08f, 0.10f, 0.14f);
    private static readonly Color Eye = new(0.06f, 0.16f, 0.22f);

    /// <summary>Awake totems on revealed ground, in world space.</summary>
    public static System.Collections.Generic.IEnumerable<Vector3> Visible()
    {
        if (Interactables.Instance is not { } hub) yield break;
        foreach (TideTotem t in hub.Totems)
            if (!t.Used && t.IsInsideTree() && FogOfWar.IsRevealed(t.GlobalPosition)) yield return t.GlobalPosition;
    }

    /// <summary>Draws one marker centred on <paramref name="at"/>; <paramref name="s"/>
    /// is the half-width in pixels (about 4 on the minimap).</summary>
    public static void Draw(CanvasItem c, Vector2 at, float s)
    {
        float w = s, h = s * 2.1f;
        var body = new Rect2(at - new Vector2(w, h * 0.5f), new Vector2(w * 2f, h));
        c.DrawRect(body.Grow(1.3f), Ink);
        c.DrawRect(body, TideTotem.TotemTint);
        // The seam between the two carved faces, and a pair of eyes on each.
        c.DrawRect(new Rect2(body.Position.X, at.Y - 0.6f, body.Size.X, 1.2f), Ink);
        float ex = w * 0.42f, er = Mathf.Max(0.9f, s * 0.22f);
        foreach (float y in new[] { at.Y - h * 0.25f, at.Y + h * 0.25f })
        {
            c.DrawCircle(new Vector2(at.X - ex, y), er, Eye);
            c.DrawCircle(new Vector2(at.X + ex, y), er, Eye);
        }
        // A pearl on top.
        c.DrawCircle(at + new Vector2(0f, -h * 0.5f - er * 1.2f), er * 1.3f, Ink);
        c.DrawCircle(at + new Vector2(0f, -h * 0.5f - er * 1.2f), er, Colors.White);
    }
}
