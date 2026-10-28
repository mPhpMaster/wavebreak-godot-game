using Godot;

namespace Wavebreak.UI;

/// <summary>
/// What to draw for one piece of content: a main glyph and up to three small
/// extras. Glyphs are names of white silhouettes in <c>assets/icons/</c>; the
/// colours tint them at draw time.
///
///   <see cref="Overlay"/> sits on top of the main glyph (a tome's stat on its cover);
///   <see cref="Badge"/> sits in a small disc at the bottom right (a weapon's element,
///   an item's second stat or its trigger);
///   <see cref="Corner"/> sits in a disc at the top right (the "upgrade" arrow).
/// </summary>
public readonly record struct IconSpec(
    string Glyph, Color Color,
    string? Badge = null, Color BadgeColor = default,
    string? Overlay = null, Color OverlayColor = default,
    string? Corner = null, Color CornerColor = default)
{
    public IconSpec WithBadge(string? glyph, Color color) => this with { Badge = glyph, BadgeColor = color };
    public IconSpec WithCorner(string? glyph, Color color) => this with { Corner = glyph, CornerColor = color };
}

