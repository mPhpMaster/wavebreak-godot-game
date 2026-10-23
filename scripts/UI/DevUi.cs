using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// Small builders for the developer panel (UI/DevPanel): one compact theme and
/// the few control shapes the tabs repeat. Buttons take no keyboard focus, so
/// Space or Enter typed for the game can never press one by accident.
/// </summary>
internal static class DevUi
{
    public static readonly Color Ink = new(0.92f, 0.94f, 0.98f);
    public static readonly Color Dim = new(0.60f, 0.65f, 0.74f);
    public static readonly Color Accent = new(0.45f, 0.85f, 1.00f);
    public static readonly Color Gold = new(1.00f, 0.84f, 0.38f);
    public static readonly Color Good = new(0.48f, 0.92f, 0.55f);
    public static readonly Color Bad = new(1.00f, 0.45f, 0.42f);
    public static readonly Color Back = new(0.055f, 0.065f, 0.10f, 0.97f);

    public static Theme MakeTheme()
    {
        var t = new Theme { DefaultFontSize = 15 };
        t.SetStylebox("normal", "Button", Box(new Color(0.13f, 0.16f, 0.24f), new Color(0.22f, 0.28f, 0.40f)));
        t.SetStylebox("hover", "Button", Box(new Color(0.18f, 0.23f, 0.34f), Accent));
        t.SetStylebox("pressed", "Button", Box(new Color(0.24f, 0.32f, 0.46f), Gold));
        t.SetStylebox("disabled", "Button", Box(new Color(0.10f, 0.11f, 0.14f), new Color(0.16f, 0.17f, 0.20f)));
        t.SetStylebox("focus", "Button", new StyleBoxEmpty());
        t.SetColor("font_color", "Button", Ink);
        t.SetColor("font_hover_color", "Button", Gold);
        t.SetColor("font_pressed_color", "Button", Gold);
        t.SetColor("font_disabled_color", "Button", Dim);

        t.SetStylebox("normal", "LineEdit", Box(new Color(0.03f, 0.04f, 0.07f), new Color(0.25f, 0.32f, 0.45f), 8));
        t.SetStylebox("focus", "LineEdit", Box(new Color(0, 0, 0, 0), Accent, 8));

        var panel = new StyleBoxFlat { BgColor = new Color(0.07f, 0.085f, 0.13f, 0.98f),
                                       ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 10, ContentMarginBottom = 10 };
        panel.SetCornerRadiusAll(4);
        t.SetStylebox("panel", "TabContainer", panel);
        t.SetStylebox("tab_selected", "TabContainer", Box(new Color(0.07f, 0.085f, 0.13f), Accent, 16, top: true));
        t.SetStylebox("tab_unselected", "TabContainer", Box(new Color(0.10f, 0.12f, 0.18f), new Color(0, 0, 0, 0), 16, top: true));
        t.SetStylebox("tab_hovered", "TabContainer", Box(new Color(0.14f, 0.17f, 0.25f), Gold, 16, top: true));
        t.SetColor("font_selected_color", "TabContainer", Gold);
        t.SetColor("font_unselected_color", "TabContainer", Dim);
        t.SetColor("font_hovered_color", "TabContainer", Ink);
        t.SetFontSize("font_size", "TabContainer", 17);

        t.SetConstant("separation", "HBoxContainer", 6);
        t.SetConstant("separation", "VBoxContainer", 6);
        t.SetConstant("h_separation", "HFlowContainer", 6);
        t.SetConstant("v_separation", "HFlowContainer", 6);
        return t;
    }

    private static StyleBoxFlat Box(Color back, Color border, int padX = 10, bool top = false)
    {
        var s = new StyleBoxFlat { BgColor = back, BorderColor = border,
                                   ContentMarginLeft = padX, ContentMarginRight = padX, ContentMarginTop = 4, ContentMarginBottom = 4 };
        if (top) { s.BorderWidthTop = 2; s.ContentMarginTop = 6; s.ContentMarginBottom = 6; }
        else s.SetBorderWidthAll(1);
        s.SetCornerRadiusAll(4);
        return s;
    }

    public static Button Btn(string text, Action onPressed, Color? color = null, float minWidth = 0f)
    {
        var b = new Button { Text = Loc.T(text), FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(minWidth, 30f) };
        if (color is { } c) b.AddThemeColorOverride("font_color", c);
        b.Pressed += onPressed;
        return b;
    }

    public static CheckButton Toggle(string text, bool on, Action<bool> onToggled)
    {
        var b = new CheckButton { Text = Loc.T(text), ButtonPressed = on, FocusMode = Control.FocusModeEnum.None };
        b.Toggled += v => onToggled(v);
        return b;
    }

    public static Label Lbl(string text, int size = 15, Color? color = null)
    {
        var l = new Label { Text = Loc.T(text), MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color ?? Ink);
        return l;
    }

    /// <summary>A heading line inside a tab.</summary>
    public static Label Heading(string text)
    {
        Label l = Lbl(text, 14, Accent);
        l.CustomMinimumSize = new Vector2(0f, 24f);
        l.VerticalAlignment = VerticalAlignment.Bottom;
        return l;
    }

    public static HFlowContainer Flow(params Control[] children)
    {
        var f = new HFlowContainer();
        foreach (Control c in children) f.AddChild(c);
        return f;
    }

    public static HBoxContainer Row(params Control[] children)
    {
        var r = new HBoxContainer();
        foreach (Control c in children) r.AddChild(c);
        return r;
    }

    public static SpinBox Spin(double min, double max, double value, double step = 1)
    {
        // Western digits like the rest of the game, in Arabic too.
        var s = new SpinBox { MinValue = min, MaxValue = max, Value = value, Step = step, CustomMinimumSize = new Vector2(96f, 30f),
                              LocalizeNumeralSystem = false };
        s.GetLineEdit().LocalizeNumeralSystem = false;
        s.GetLineEdit().FocusMode = Control.FocusModeEnum.Click;
        return s;
    }

    /// <summary>A vertical scroll area whose content is a VBox filling its width.</summary>
    public static (ScrollContainer scroll, VBoxContainer body) Scroll()
    {
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(body);
        return (scroll, body);
    }
}

/// <summary>A search box over a scrolling list of rows; typing filters rows by
/// their key (case-insensitive substring), and an optional extra predicate
/// narrows further (a kind toggle, locked/unlocked).</summary>
internal sealed class DevFilterList
{
    public readonly VBoxContainer Root = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    public readonly LineEdit Search;
    public readonly HBoxContainer Bar = new();
    public readonly VBoxContainer Rows;
    private readonly ScrollContainer _scroll;
    private readonly Label _count;
    private readonly List<(string Key, Control Row, object? Tag)> _rows = new();

    /// <summary>Extra filter on a row's tag; null shows everything.</summary>
    public Func<object?, bool>? Extra;

    public DevFilterList(string placeholder)
    {
        Search = new LineEdit { PlaceholderText = Loc.T(placeholder), ClearButtonEnabled = true,
                                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(180f, 32f) };
        Search.TextChanged += _ => Apply();
        _count = DevUi.Lbl("", 13, DevUi.Dim);
        Bar.AddChild(Search);
        Bar.AddChild(_count);
        Root.AddChild(Bar);
        (_scroll, Rows) = DevUi.Scroll();
        Root.AddChild(_scroll);
    }

    public void Add(string key, Control row, object? tag = null)
    {
        // Arabic names are searchable too (the English key stays first).
        _rows.Add(((Loc.IsArabic ? key + " " + Loc.T(key) : key).ToLowerInvariant(), row, tag));
        Rows.AddChild(row);
    }

    public int Shown { get; private set; }

    public void Apply()
    {
        string q = Search.Text.Trim().ToLowerInvariant();
        int shown = 0;
        foreach (var (key, row, tag) in _rows)
        {
            bool on = (q.Length == 0 || key.Contains(q)) && (Extra is null || Extra(tag));
            row.Visible = on;
            if (on) shown++;
        }
        Shown = shown;
        _count.Text = Loc.Ltr($"{shown}/{_rows.Count}");
    }
}
