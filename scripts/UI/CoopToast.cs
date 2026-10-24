using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// A short co-op notice across the top of the run screen, for things the whole
/// team must notice: "Host left — Guest is now the host". One at a time; a new
/// one replaces the last. It lives on its own canvas layer under the run's
/// root, so it goes with the run scene.
/// </summary>
public sealed partial class CoopToast : CanvasLayer
{
    private const float ShowSeconds = 6f, FadeSeconds = 0.5f;

    /// <summary>The last text shown (the co-op test reads it).</summary>
    public static string LastText { get; private set; } = "";
    public static int Shown { get; private set; }

    private static CoopToast? _current;

    private Label _label = null!;
    private PanelContainer _panel = null!;
    private float _left;
    private string? _pending;
    private bool _low;

    /// <summary>Show <paramref name="text"/>; <paramref name="low"/> puts it in
    /// the lower half (over the results screen, which owns the top).</summary>
    public static void Show(string text, bool low = false)
    {
        LastText = text;
        Shown++;
        GD.Print($"[coop] toast: {text}");
        Game.Main? main = Game.Main.Instance;
        if (main is null || !IsInstanceValid(main) || !main.IsInsideTree()) return;
        if (_current is null || !IsInstanceValid(_current))
        {
            _current = new CoopToast { Name = "CoopToast", Layer = 25 };
            main.AddChild(_current);
        }
        _current._low = low;
        _current.Display(text);
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.07f, 0.11f, 0.92f),
            BorderColor = new Color(1.00f, 0.80f, 0.40f),
            ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 10, ContentMarginBottom = 10,
        };
        style.BorderWidthTop = 3;
        style.BorderWidthBottom = 3;
        style.SetCornerRadiusAll(12);
        _panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _panel.AddThemeStyleboxOverride("panel", style);
        _panel.AnchorLeft = 0.5f; _panel.AnchorRight = 0.5f;
        _panel.AnchorTop = 0f; _panel.AnchorBottom = 0f;
        _panel.OffsetLeft = -330f; _panel.OffsetRight = 330f;
        _panel.OffsetTop = 168f; _panel.OffsetBottom = 214f;
        _panel.GrowHorizontal = Control.GrowDirection.Both;
        AddChild(_panel);
        _label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,   // already translated, holds names
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _label.AddThemeFontSizeOverride("font_size", 22);
        _label.AddThemeColorOverride("font_color", new Color(0.96f, 0.97f, 1f));
        _label.AddThemeColorOverride("font_outline_color", new Color(0.02f, 0.03f, 0.05f, 0.9f));
        _label.AddThemeConstantOverride("outline_size", 6);
        if (Loc.IsRtl) _label.TextDirection = Control.TextDirection.Rtl;
        _panel.AddChild(_label);
        if (_pending is not null) Display(_pending);
    }

    private void Display(string text)
    {
        if (_label is null) { _pending = text; return; }
        _label.Text = text;
        _panel.OffsetTop = _low ? 498f : 168f;
        _panel.OffsetBottom = _panel.OffsetTop + 46f;
        _left = ShowSeconds;
        _panel.Modulate = Colors.White;
        _panel.Visible = true;
    }

    public override void _Process(double delta)
    {
        if (_panel is null || !_panel.Visible) return;
        _left -= (float)delta;
        if (_left <= 0f) { _panel.Visible = false; return; }
        if (_left < FadeSeconds) _panel.Modulate = new Color(1f, 1f, 1f, _left / FadeSeconds);
    }

    public override void _ExitTree()
    {
        if (ReferenceEquals(_current, this)) _current = null;
    }
}
