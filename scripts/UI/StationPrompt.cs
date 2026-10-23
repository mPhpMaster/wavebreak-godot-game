using Godot;

namespace Wavebreak.UI;

/// <summary>"E — trade with merchant", hung over the station in reach. The same
/// look as <see cref="ChestPrompt"/> so the two read as one system.</summary>
public sealed partial class StationPrompt : Label3D
{
    public StationPrompt()
    {
        Name = "StationPrompt";
        FontSize = 48;
        PixelSize = 0.0085f;
        OutlineSize = 12;
        OutlineModulate = new Color(0.03f, 0.03f, 0.05f, 0.95f);
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
        NoDepthTest = true;
        Shaded = false;
        DoubleSided = true;
        RenderPriority = 4;
        OutlineRenderPriority = 3;
        Visible = false;
    }

    public void ShowFor(Vector3 anchor, string text, Color colour)
    {
        Text = text;
        Modulate = colour;
        GlobalPosition = anchor;
        Visible = true;
    }

    public void HideNow() => Visible = false;
}
