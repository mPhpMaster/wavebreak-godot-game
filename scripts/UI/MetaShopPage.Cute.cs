using System.Collections.Generic;
using System.Linq;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The Pearl Shop's soft look: rounded night-blue pills with a pastel icon per
/// line, and a rounded detail card with a big icon of the focused line. Applied
/// as one pass after the page is built, so the page's own layout and logic are
/// untouched; the text colours it sets stay readable on the dark pills.
/// </summary>
public sealed partial class MetaShopPage
{
    private static readonly Dictionary<string, (string Glyph, Color Color)> ShopLook = new()
    {
        ["vitality"] = ("heart", CuteStyle.Rose),
        ["might"] = ("sword", CuteStyle.Coral),
        ["haste"] = ("hourglass", CuteStyle.Butter),
        ["swiftness"] = ("boot", CuteStyle.Seafoam),
        ["plating"] = ("shield", new Color(0.78f, 0.80f, 0.92f)),
        ["vigour"] = ("regen", CuteStyle.Mint),
        ["wisdom"] = ("star", CuteStyle.Sky),
        ["greed"] = ("coin", CuteStyle.Butter),
        ["fortune"] = ("clover", CuteStyle.Mint),
        ["reach"] = ("magnet", CuteStyle.Lavender),
        ["rerolls"] = ("dice", CuteStyle.Sky),
        ["banishes"] = ("skull", CuteStyle.Lavender),
        ["skips"] = ("next", CuteStyle.Peach),
        [ShopExtras.TogglerId] = ("toggle", CuteStyle.Mint),
        [ShopExtras.WeaponSlotId] = ("swords", CuteStyle.Peach),
        [ShopExtras.TomeSlotId] = ("tome", CuteStyle.Lavender),
    };

    private IconView? _bigIcon;

    private static (string Glyph, Color Color) LookOf(ShopUpgradeDef? def)
        => def is not null && ShopLook.TryGetValue(def.Id, out var look) ? look : ("gem", CuteStyle.Sky);

    /// <summary>Restyle the built page. Called once at the end of _Ready.</summary>
    private void CutePass()
    {
        foreach (var (button, def) in _rows) CuteRow(button, LookOf(def));
        CuteRow(_refund, ("coin", CuteStyle.Rose));
        foreach (Button b in FindChildren("*", nameof(Button), true, false).OfType<Button>())
            if (b.Text == "BACK") CuteRow(b, ("next", CuteStyle.Peach));

        foreach (PanelContainer panel in GetChildren().OfType<PanelContainer>())
        {
            panel.AddThemeStyleboxOverride("panel", CuteStyle.Box(new Color(CuteStyle.Night, 0.9f), new Color(CuteStyle.Lavender, 0.55f), 22, 3, 20));
            if (panel.GetChildCount() > 0 && panel.GetChild(0) is VBoxContainer box)
            {
                // The big icon sits beside the name.
                var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                head.AddThemeConstantOverride("separation", 12);
                _bigIcon = CuteStyle.Icon(new IconSpec("gem", CuteStyle.Sky), 56f, CuteStyle.Sky);
                _bigIcon.Disc = false;
                _bigIcon.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                box.RemoveChild(_name);
                head.AddChild(_bigIcon);
                head.AddChild(_name);
                box.AddChild(head);
                box.MoveChild(head, 0);
            }
        }
        foreach (var (button, def) in _rows)
        {
            ShopUpgradeDef d = def;
            button.FocusEntered += () => ShowBigIcon(d);
        }
        _refund.FocusEntered += () => ShowBigIcon(null, ("coin", CuteStyle.Rose));
    }

    private void ShowBigIcon(ShopUpgradeDef? def, (string Glyph, Color Color)? look = null)
    {
        if (_bigIcon is null) return;
        var (glyph, color) = look ?? LookOf(def);
        _bigIcon.Set(new IconSpec(glyph, color), color);
    }

    private static void CuteRow(Button b, (string Glyph, Color Color) look)
    {
        Color fill = new(0.16f, 0.15f, 0.27f, 0.92f);
        b.AddThemeStyleboxOverride("normal", CuteStyle.Box(fill, new Color(look.Color, 0.25f), 18, 2, 6));
        b.AddThemeStyleboxOverride("hover", CuteStyle.Box(fill.Lightened(0.08f), look.Color, 18, 2, 6));
        b.AddThemeStyleboxOverride("pressed", CuteStyle.Box(fill.Lightened(0.15f), Colors.White, 18, 3, 6));
        b.AddThemeStyleboxOverride("focus", CuteStyle.Box(new Color(0, 0, 0, 0), look.Color, 18, 3, 6));
        foreach (string side in new[] { "normal", "hover", "pressed", "focus" })
            if (b.GetThemeStylebox(side) is StyleBoxFlat s) { s.ContentMarginLeft = 12; s.ContentMarginRight = 14; }
        b.Icon = Icons.Texture(look.Glyph);
        b.ExpandIcon = true;
        b.AddThemeConstantOverride("icon_max_width", 28);
        b.AddThemeConstantOverride("h_separation", 10);
        foreach (string k in new[] { "icon_normal_color", "icon_hover_color", "icon_focus_color", "icon_pressed_color", "icon_hover_pressed_color" })
            b.AddThemeColorOverride(k, look.Color);
        b.CustomMinimumSize = new Vector2(0f, 42f);
    }

    /// <summary>Land focus on one line by id (menu captures: "shop:toggler").</summary>
    public void FocusLine(string id)
    {
        foreach (var (button, def) in _rows)
            if (def.Id == id)
            {
                button.GrabFocus();
                // Scroll once the list has its size (focus alone scrolls only after layout).
                if (button.GetParent()?.GetParent() is ScrollContainer scroll)
                    Callable.From(() => scroll.EnsureControlVisible(button)).CallDeferred();
                return;
            }
        FocusFirst();
    }
}
