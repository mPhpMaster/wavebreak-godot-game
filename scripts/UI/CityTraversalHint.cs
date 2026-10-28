using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;
using Wavebreak.Meta;

namespace Wavebreak.UI;

public sealed partial class CityTraversalHint : Control
{
    private float _refresh;
    private Label _text = null!;
    private Label _reticle = null!;
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _text = new Label
        {
            AnchorLeft = 0.15f, AnchorRight = 0.85f, AnchorTop = 1f, AnchorBottom = 1f,
            OffsetTop = -165f, OffsetBottom = -95f,
            HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore,
        };
        _text.AddThemeFontSizeOverride("font_size", 16);
        _text.AddThemeColorOverride("font_outline_color", Colors.Black);
        _text.AddThemeConstantOverride("outline_size", 5);
        AddChild(_text);
        _reticle = new Label
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -20f, OffsetRight = 20f, OffsetTop = -20f, OffsetBottom = 20f,
            HorizontalAlignment = HorizontalAlignment.Center, Text = "+", MouseFilter = MouseFilterEnum.Ignore,
        };
        _reticle.AddThemeFontSizeOverride("font_size", 28);
        _reticle.AddThemeColorOverride("font_outline_color", Colors.Black);
        _reticle.AddThemeConstantOverride("outline_size", 4);
        AddChild(_reticle);
    }

    public override void _Process(double delta)
    {
        using var timing = Prof.Scope("UI.cityHint");
        Visible = Arena.IsCity && Player.Instance is { IsAlive: true };
        if (!Visible || Player.Instance is not { } player) return;
        _refresh -= (float)delta;
        if (_refresh > 0f) return;
        _refresh = .1f;
        string right = Keybinds.Describe(Keybinds.Get("city_hook_right", 0));
        string hook = Keybinds.Describe(Keybinds.Get("city_hook_left", 0));
        string jump = Keybinds.Describe(Keybinds.Get("jump", 0));
        string strike = Keybinds.Describe(Keybinds.Get("dash", 0));
        _text.Text = Loc.F("Hold {0}: LEFT hook  |  Hold {1}: RIGHT hook", hook, right)
            + "\n" + Loc.F("{0}: attached BOOST  |  {1}: dash / airborne core strike  |  Boost ready in {2:0.0}s", jump, strike, player.HookBoostCooldown)
            + "\n" + Loc.F("Left: {0}  |  Right: {1}", Loc.T(player.LeftHookAttached ? "Attached" : "Free"), Loc.T(player.RightHookAttached ? "Attached" : "Free"));
        if (player.CoreStrikeFeedback.Length > 0) _text.Text = Loc.T(player.CoreStrikeFeedback);
        _reticle.Modulate = player.Grappling ? Colors.Gold : player.HookTargetAvailable ? Colors.Aquamarine : Colors.White;
    }
}
