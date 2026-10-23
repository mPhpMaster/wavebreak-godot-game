using Godot;

namespace Wavebreak.UI;

/// <summary>
/// The line over the map shrine in reach: "E — offer 56 gold", "Charging 64%".
/// Same pattern as <see cref="ChestPrompt"/> — world-space, billboarded, no depth
/// test, red when the player cannot pay, and a swell on refusal — but it takes
/// free text, because four shrine kinds say four different things.
/// </summary>
public sealed partial class ShrinePrompt : Label3D
{
    public static readonly Color Neutral = new(0.95f, 0.97f, 1f);
    public static readonly Color TooPoor = new(1f, 0.32f, 0.3f);

    private const float BasePixelSize = 0.0075f;

    private float _punch;

    public ShrinePrompt()
    {
        Name = "ShrinePrompt";
        FontSize = 48;
        PixelSize = BasePixelSize;
        OutlineSize = 12;
        OutlineModulate = new Color(0.03f, 0.03f, 0.05f, 0.95f);
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
        NoDepthTest = true;
        Shaded = false;
        DoubleSided = true;
        RenderPriority = 4;
        OutlineRenderPriority = 3;
        HorizontalAlignment = HorizontalAlignment.Center;
        Visible = false;
    }

    /// <summary>Show <paramref name="text"/> over <paramref name="anchor"/>.
    /// Called every frame while a shrine is in reach.</summary>
    public void ShowText(Vector3 anchor, string text, Color color)
    {
        Text = text;
        if (_punch <= 0f) Modulate = color;
        GlobalPosition = anchor;
        Visible = true;
    }

    public void HideNow() => Visible = false;

    public void Deny()
    {
        _punch = 1f;
        Modulate = TooPoor;
    }

    public override void _Process(double delta)
    {
        if (_punch <= 0f) return;
        _punch = Mathf.Max(0f, _punch - (float)delta * 4f);
        PixelSize = BasePixelSize * (1f + 0.35f * _punch);
        Position += new Vector3(Mathf.Sin(_punch * 40f) * 0.05f * _punch, 0f, 0f);
    }
}
