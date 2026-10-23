using Godot;

namespace Wavebreak.UI;

/// <summary>
/// Draws one <see cref="IconSpec"/>: an optional soft disc tinted by rarity,
/// the main glyph, and its overlay, badge and corner marks.
///
/// Drawn in <c>_Draw</c> rather than built from TextureRects so a HUD row, a
/// card and a tooltip all get the same composition from one call, at any size,
/// without a node tree to keep in sync.
/// </summary>
public sealed partial class IconView : Control
{
    private IconSpec _spec = new(Icons.Fallback, Colors.White);
    private Color _ring = new(0.78f, 0.82f, 0.88f);
    private bool _disc = true;

    public IconView()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>A square icon of the given size, ready to add to a container.</summary>
    public static IconView Make(float size, bool disc = true)
    {
        var v = new IconView { CustomMinimumSize = new Vector2(size, size), _disc = disc };
        v.Size = new Vector2(size, size);
        return v;
    }

    /// <summary>A small icon followed by <paramref name="label"/> in one row:
    /// the shape every list (HUD, stats sheet, panels) uses.</summary>
    public static HBoxContainer Row(IconSpec spec, Control label, float size, Color? ring = null)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 6);
        IconView icon = Make(size, ring is not null);
        icon.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        icon.Set(spec, ring ?? Colors.White);
        row.AddChild(icon);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(label);
        return row;
    }

    /// <summary>Whether to draw the rarity disc behind the glyph.</summary>
    public bool Disc
    {
        get => _disc;
        set { _disc = value; QueueRedraw(); }
    }

    public void Set(IconSpec spec, Color ring)
    {
        _spec = spec;
        _ring = ring;
        QueueRedraw();
    }

    public void Set(IconSpec spec) => Set(spec, _ring);

    public override void _Draw()
    {
        float s = Mathf.Min(Size.X, Size.Y);
        if (s <= 1f) return;
        var c = new Vector2(Size.X * 0.5f, Size.Y * 0.5f);
        float r = s * 0.5f;

        float glyphFrac = 0.86f;
        if (_disc)
        {
            // Soft disc: a dark base so any tint reads, then rings of the rarity
            // colour fading toward the middle, then a crisp rim.
            DrawCircle(c, r, new Color(0.04f, 0.05f, 0.08f, 0.92f));
            for (int i = 0; i < 6; i++)
            {
                float t = i / 6f;
                DrawCircle(c, r * (1f - t * 0.55f), new Color(_ring, 0.07f));
            }
            DrawArc(c, r - 1.5f, 0f, Mathf.Tau, 64, new Color(_ring, 0.95f), Mathf.Max(1.5f, s * 0.03f), true);
            glyphFrac = 0.66f;
        }

        Texture2D? main = Icons.Texture(_spec.Glyph);
        float g = s * glyphFrac;
        if (main is not null)
            DrawTextureRect(main, new Rect2(c - new Vector2(g, g) * 0.5f, new Vector2(g, g)), false, _spec.Color);

        if (_spec.Overlay is not null && Icons.Texture(_spec.Overlay) is { } over)
        {
            // Centred on the book's cover, which sits a little right of the spine.
            float o = g * 0.5f;
            var oc = c + new Vector2(g * 0.04f, -g * 0.03f);
            DrawTextureRect(over, new Rect2(oc - new Vector2(o, o) * 0.5f, new Vector2(o, o)), false, _spec.OverlayColor);
        }

        if (_spec.Badge is not null)
            DrawMark(_spec.Badge, _spec.BadgeColor, c + new Vector2(r, r) * 0.64f, s);

        if (_spec.Corner is not null)
            DrawMark(_spec.Corner, _spec.CornerColor, c + new Vector2(r, -r) * 0.64f, s);
    }

    private void DrawMark(string glyph, Color color, Vector2 at, float s)
    {
        if (Icons.Texture(glyph) is not { } tex) return;
        float br = s * 0.2f;
        DrawCircle(at, br, new Color(0.04f, 0.05f, 0.08f, 0.96f));
        DrawArc(at, br, 0f, Mathf.Tau, 32, new Color(color, 0.85f), Mathf.Max(1f, s * 0.02f), true);
        float b = br * 1.45f;
        DrawTextureRect(tex, new Rect2(at - new Vector2(b, b) * 0.5f, new Vector2(b, b)), false, color);
    }
}
