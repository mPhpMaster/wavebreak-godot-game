using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The "E — open chest · 25 gold" line that hangs over the chest in reach.
///
/// World-space rather than on the HUD: the question it answers is "which chest
/// will E open?", and text over the chest answers that without an arrow. It is
/// billboarded and ignores depth for the same reason damage numbers do — a
/// prompt hidden behind the lid it describes is no prompt.
///
/// The colour carries the price check before the key is pressed: pale when the
/// player can afford it, red when they cannot. Pressing anyway still gets a
/// <see cref="Deny"/> punch, because a refusal with no response reads as a
/// dropped key press.
/// </summary>
public sealed partial class ChestPrompt : Label3D
{
    private static readonly Color Affordable = new(0.95f, 0.97f, 1f);
    private static readonly Color FreeColor = new(0.55f, 1f, 0.65f);
    private static readonly Color TooPoor = new(1f, 0.32f, 0.3f);

    private const float BasePixelSize = 0.0085f;

    private float _punch;

    public ChestPrompt()
    {
        Name = "ChestPrompt";
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
        Visible = false;
    }

    /// <summary>Show over <paramref name="anchor"/>. Called every frame while a
    /// chest is in reach, so price and affordability are always current.</summary>
    public void ShowFor(Vector3 anchor, string key, int cost, bool free, int gold)
    {
        bool affordable = free || gold >= cost;
        Text = free
            ? Loc.F("{0} — open free chest", key)
            : affordable
                ? Loc.F("{0} — open chest · {1} gold", key, cost)
                : Loc.F("{0} — open chest · {1} gold  (need {2} more)", key, cost, cost - gold);

        if (_punch <= 0f) Modulate = free ? FreeColor : affordable ? Affordable : TooPoor;
        GlobalPosition = anchor;
        Visible = true;
    }

    public void HideNow() => Visible = false;

    /// <summary>The refusal: a brief swell, forced red, decaying in
    /// <see cref="_Process"/>.</summary>
    public void Deny()
    {
        _punch = 1f;
        Modulate = TooPoor;
    }

    public override void _Process(double delta)
    {
        if (_punch <= 0f) return;
        _punch = Mathf.Max(0f, _punch - (float)delta * 4f);
        // A quick swell plus a horizontal shudder: noticeable at a glance, gone
        // in a quarter second.
        PixelSize = BasePixelSize * (1f + 0.35f * _punch);
        Position += new Vector3(Mathf.Sin(_punch * 40f) * 0.05f * _punch, 0f, 0f);
    }
}
