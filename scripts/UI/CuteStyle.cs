using System;
using Godot;

namespace Wavebreak.UI;

/// <summary>
/// The soft, friendly look shared by the challenge picker, the Toggler page and
/// the new shrine panels: cream cards with round corners, pastel accents, dark
/// ink on light ground. One palette and a few stylebox makers, so every page
/// that uses it agrees on what "selected", "done" and "off" look like.
/// </summary>
public static class CuteStyle
{
    // Ground and ink.
    public static readonly Color Cream = new(1.00f, 0.97f, 0.93f, 0.97f);
    public static readonly Color CreamHover = new(1.00f, 0.99f, 0.97f, 1.00f);
    public static readonly Color Paper = new(0.97f, 0.93f, 0.89f, 0.96f);
    public static readonly Color Ink = new(0.22f, 0.20f, 0.30f);
    public static readonly Color InkSoft = new(0.45f, 0.42f, 0.52f);
    public static readonly Color Night = new(0.13f, 0.12f, 0.22f, 0.90f);
    public static readonly Color OnNight = new(0.98f, 0.96f, 1.00f);
    public static readonly Color OnNightDim = new(0.78f, 0.76f, 0.88f);

    // Pastels.
    public static readonly Color Mint = new(0.55f, 0.90f, 0.74f);
    public static readonly Color Peach = new(1.00f, 0.72f, 0.58f);
    public static readonly Color Lavender = new(0.74f, 0.66f, 1.00f);
    public static readonly Color Sky = new(0.55f, 0.80f, 1.00f);
    public static readonly Color Rose = new(1.00f, 0.60f, 0.72f);
    public static readonly Color Butter = new(1.00f, 0.86f, 0.45f);
    public static readonly Color Seafoam = new(0.50f, 0.88f, 0.86f);
    public static readonly Color Coral = new(1.00f, 0.52f, 0.47f);
    public static readonly Color Muted = new(0.70f, 0.68f, 0.74f);

    /// <summary>A rounded panel. <paramref name="border"/> alpha 0 draws none.</summary>
    public static StyleBoxFlat Box(Color fill, Color border, int radius = 16, int borderWidth = 3, int pad = 12)
    {
        var s = new StyleBoxFlat { BgColor = fill, BorderColor = border };
        s.SetCornerRadiusAll(radius);
        s.SetBorderWidthAll(border.A > 0f ? borderWidth : 0);
        s.SetContentMarginAll(pad);
        s.AntiAliasing = true;
        // A soft drop shadow under solid boxes only: under a see-through focus
        // ring it would grey out the card it outlines.
        if (fill.A > 0.05f)
        {
            s.ShadowColor = new Color(0.05f, 0.03f, 0.12f, 0.35f);
            s.ShadowSize = 4;
            s.ShadowOffset = new Vector2(0f, 2f);
        }
        return s;
    }

    /// <summary>A rounded card panel.</summary>
    public static PanelContainer Panel(Color fill, Color border, int radius = 18, int pad = 14)
    {
        var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        p.AddThemeStyleboxOverride("panel", Box(fill, border, radius, 3, pad));
        return p;
    }

    /// <summary>Dark-on-light text, no heavy outline.</summary>
    public static Label Text(string text, int size, Color color, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = new Label { Text = text, HorizontalAlignment = align, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    /// <summary>Light text over the 3D backdrop: a soft dark outline.</summary>
    public static Label Title(string text, int size, Color color, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        Label l = Text(text, size, color, align);
        l.AddThemeColorOverride("font_outline_color", new Color(0.12f, 0.08f, 0.22f, 0.85f));
        l.AddThemeConstantOverride("outline_size", 6);
        return l;
    }

    /// <summary>
    /// A pill button: rounded, pastel, focusable. Focus and hover lift it with a
    /// white border; <paramref name="accent"/> fills it when "on".
    /// </summary>
    public static Button Pill(string text, Color accent, Action onPressed, int fontSize = 17, bool on = false)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.All };
        b.AddThemeFontSizeOverride("font_size", fontSize);
        StylePill(b, accent, on);
        b.Pressed += onPressed;
        b.MouseEntered += () => { if (b.IsVisibleInTree()) b.GrabFocus(); };
        return b;
    }

    /// <summary>Restyle a pill for its on/off state.</summary>
    public static void StylePill(Button b, Color accent, bool on)
    {
        Color fill = on ? accent : Cream;
        Color hover = on ? accent.Lightened(0.15f) : CreamHover;
        b.AddThemeStyleboxOverride("normal", Box(fill, new Color(accent, on ? 0.9f : 0.55f), 20, 2, 8));
        b.AddThemeStyleboxOverride("hover", Box(hover, accent, 20, 3, 8));
        b.AddThemeStyleboxOverride("pressed", Box(accent.Lightened(0.1f), Colors.White, 20, 3, 8));
        b.AddThemeStyleboxOverride("focus", Box(new Color(0, 0, 0, 0), Colors.White, 20, 3, 8));
        b.AddThemeStyleboxOverride("disabled", Box(new Color(Cream, 0.5f), new Color(0, 0, 0, 0), 20, 2, 8));
        foreach (string k in new[] { "font_color", "font_hover_color", "font_focus_color", "font_pressed_color", "font_hover_pressed_color" })
            b.AddThemeColorOverride(k, Ink);
        b.AddThemeColorOverride("font_disabled_color", InkSoft);
    }

    /// <summary>
    /// A card button: a cream rounded rectangle whose border takes
    /// <paramref name="accent"/>. Content (icons, labels) goes in a child
    /// container that ignores the mouse, so the whole card is one target.
    /// </summary>
    public static Button Card(Color accent, Action onPressed, bool selected = false)
    {
        var b = new Button { FocusMode = Control.FocusModeEnum.All, Text = "", ClipText = true };
        StyleCard(b, accent, selected, false);
        b.Pressed += onPressed;
        b.MouseEntered += () => { if (b.IsVisibleInTree()) b.GrabFocus(); };
        return b;
    }

    public static void StyleCard(Button b, Color accent, bool selected, bool dim)
    {
        Color fill = dim ? new Color(0.86f, 0.84f, 0.88f, 0.92f) : selected ? accent.Lerp(Colors.White, 0.55f) : Cream;
        Color border = selected ? accent.Darkened(0.15f) : new Color(accent, dim ? 0.25f : 0.6f);
        b.AddThemeStyleboxOverride("normal", Box(fill, border, 16, selected ? 4 : 2, 8));
        b.AddThemeStyleboxOverride("hover", Box(fill.Lightened(0.05f), accent, 16, 4, 8));
        b.AddThemeStyleboxOverride("pressed", Box(accent.Lerp(Colors.White, 0.4f), Colors.White, 16, 4, 8));
        b.AddThemeStyleboxOverride("focus", Box(new Color(0, 0, 0, 0), new Color(1f, 1f, 1f, 0.95f), 16, 4, 8));
    }

    /// <summary>A small rounded tag ("DONE", "+60", "HARD").</summary>
    public static PanelContainer Tag(string text, Color fill, Color ink, int size = 12)
    {
        var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var s = new StyleBoxFlat { BgColor = fill };
        s.SetCornerRadiusAll(10);
        s.ContentMarginLeft = 8; s.ContentMarginRight = 8; s.ContentMarginTop = 1; s.ContentMarginBottom = 1;
        p.AddThemeStyleboxOverride("panel", s);
        p.AddChild(Text(text, size, ink, HorizontalAlignment.Center));
        return p;
    }

    /// <summary>An icon in a pastel disc.</summary>
    public static IconView Icon(IconSpec spec, float size, Color ring)
    {
        IconView v = IconView.Make(size);
        v.Set(spec, ring);
        v.MouseFilter = Control.MouseFilterEnum.Ignore;
        return v;
    }

    /// <summary>Make every descendant ignore the mouse, so a card's button
    /// receives the click wherever it lands.</summary>
    public static void PassMouse(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is Control c) c.MouseFilter = Control.MouseFilterEnum.Ignore;
            PassMouse(child);
        }
    }

    /// <summary>A control filling its parent with a margin.</summary>
    public static MarginContainer Inset(Control child, int left, int top, int right, int bottom)
    {
        var m = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        m.AddThemeConstantOverride("margin_left", left);
        m.AddThemeConstantOverride("margin_top", top);
        m.AddThemeConstantOverride("margin_right", right);
        m.AddThemeConstantOverride("margin_bottom", bottom);
        m.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        m.AddChild(child);
        return m;
    }
}
