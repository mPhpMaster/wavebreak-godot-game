using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The difficulty line on the character page: one focusable button that
/// cycles through the unlocked tiers when pressed (Enter / gamepad A / click),
/// and a note under it saying what the tier does or how the next one unlocks.
/// The choice is saved to the profile straight away.
///
/// Pressing rather than left/right on purpose: the character page already
/// uses left/right for skins, and up/down moves between the list's buttons.
/// </summary>
public sealed partial class DifficultyPicker : VBoxContainer
{
    private static readonly Color Ink = new(0.93f, 0.95f, 0.99f);
    private static readonly Color InkDim = new(0.62f, 0.68f, 0.78f);
    private static readonly Color Hard = new(1.00f, 0.62f, 0.30f);
    private static readonly Color Nightmare = new(1.00f, 0.36f, 0.34f);

    public Button Button { get; }
    private readonly Label _note;

    public DifficultyPicker()
    {
        Name = "DifficultyPicker";
        AddThemeConstantOverride("separation", 2);
        Button = MainMenu.MenuButton("", Cycle);
        Button.MouseEntered += () => Button.GrabFocus();
        AddChild(Button);
        _note = MainMenu.MakeLabel("", 13, InkDim);
        _note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _note.CustomMinimumSize = new Vector2(0f, 34f);
        AddChild(_note);
        Refresh();
    }

    private void Cycle()
    {
        DifficultyTiers.CycleSelected();
        Profile.Save();
        Refresh();
    }

    public void Refresh()
    {
        DifficultyTier tier = DifficultyTiers.Selected;
        int index = DifficultyTiers.IndexOf(tier);
        Button.Text = Loc.F("DIFFICULTY  ·  {0}", Loc.IsArabic ? Loc.T(tier.Name) : tier.Name.ToUpperInvariant());
        Button.AddThemeColorOverride("font_color", index switch { 0 => Ink, 1 => Hard, _ => Nightmare });

        string note = tier.Summary;
        int next = -1;
        for (int i = 0; i < DifficultyTiers.All.Count; i++)
            if (!DifficultyTiers.IsUnlocked(i)) { next = i; break; }
        if (next > 0)
            note += "\n" + Loc.F("{0}: win a run on {1} to unlock.", DifficultyTiers.All[next].Name, DifficultyTiers.All[next - 1].Name);
        else if (DifficultyTiers.All.Count > 1)
            note += "\n" + Loc.T("Press to change.");
        _note.Text = note;
    }
}
