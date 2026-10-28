using Godot;
using Wavebreak.Actors;
using Wavebreak.Meta;

namespace Wavebreak.UI;

public sealed partial class SwimmingHint : CanvasLayer
{
    private Label _label = null!;
    private readonly Game.TraversalPractice _practice = new();
    private readonly Game.TraversalHelpSession _help = new();
    private float _refresh;

    public override void _Ready()
    {
        Layer = 3;
        if (DisplayServer.GetName() == "headless")
        {
            SetProcess(false);
            return;
        }
        _label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 0.1f, AnchorRight = 0.9f, AnchorTop = 1f, AnchorBottom = 1f,
            OffsetTop = -185f, OffsetBottom = -95f,
        };
        _label.AddThemeFontSizeOverride("font_size", 18);
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", 5);
        AddChild(_label);
    }

    public override void _Process(double delta)
    {
        using var timing = Game.Prof.Scope("UI.swimHint");
        _help.Tick((float)delta);
        _label.Visible = _help.Visible && Player.Instance is { IsSwimming: true };
        if (!_label.Visible) return;
        _refresh -= (float)delta;
        if (!Game.PerformanceScenario.Reference && _refresh > 0) return;
        _refresh = .1f;
        string up = Keybinds.Describe(Keybinds.Get("jump", 0));
        string down = Keybinds.Describe(Keybinds.Get("slide", 0));
        _label.Text = Loc.F("SWIM  |  Hold {0}: rise  |  Hold {1}: dive", up, down);
        if (Player.Instance is not { } player) return;
        _label.Text += "\n" + Loc.F("Look + forward: swim in 3D  |  Depth: {0:0} m",
            Mathf.Max(0f, Game.WaterSurfaceHeight.At(player.GlobalPosition) - player.CenterPosition.Y));
        if (player.IsBreaching) _label.Text = Loc.T("BREACHING: steer and fall back into the water");
        else if (Game.WaterSurfaceHeight.At(player.GlobalPosition) - player.CenterPosition.Y < 7f)
            _label.Text = Loc.F("Hold {0} + {1} near the surface to breach", up, Keybinds.Describe(Keybinds.Get("dash", 0)));
        else if (Game.ReefCurrents.VelocityAt(player.GlobalPosition).Length() > 0.5f)
            _label.Text = Loc.T("CURRENT: steer sideways to leave the flow") + "\n"
                + Loc.F("SWIM  |  Hold {0}: rise  |  Hold {1}: dive", up, down);
        _practice.Tick(player, false);
        _label.Text = Loc.T(_practice.Instruction) + "\n" + _label.Text;
        _label.Text += "\n" + Loc.T("F2: skip help  |  F1: replay help");
    }
}


