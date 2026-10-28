using Godot;
using Wavebreak.Game;

namespace Wavebreak.UI;

public sealed partial class ExpeditionObjectivesHud : Label
{
    private float _refresh;
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AnchorLeft = .18f; AnchorRight = .82f;
        OffsetTop = 139; OffsetBottom = 163;
        HorizontalAlignment = HorizontalAlignment.Center;
        AddThemeFontSizeOverride("font_size", 15);
        AddThemeColorOverride("font_outline_color", Colors.Black);
        AddThemeConstantOverride("outline_size", 4);
    }
    public override void _Process(double delta)
    {
        _refresh -= (float)delta;
        if (_refresh > 0) return;
        _refresh = .2f;
        Text = Main.Instance?.Stages is { } stages ? ExpeditionObjectives.Describe(stages) : "";
    }
}
