using System;
using Godot;
using Wavebreak.Data;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// Settings > Accessibility (<see cref="Accessibility"/>): the colour-blind
/// palette and reduced flashes, with the rarities drawn live so the choice can be
/// judged, and the way back to the Classic tutorial. The title's page and the pause menu both show it, framed with BACK.
/// Every change saves at once.
/// </summary>
public sealed partial class AccessibilityPanel : VBoxContainer
{
    private static readonly Color Ink = new(0.93f, 0.95f, 0.99f);
    private static readonly Color InkDim = new(0.66f, 0.71f, 0.80f);
    private static readonly Color Head = new(0.62f, 0.98f, 1.00f);
    private const float LabelWidth = 220f;

    private readonly Action _back;
    private OptionButton _vision = null!;
    private CheckButton _flashes = null!;
    private Label _replayNote = null!;
    private readonly (Label Label, Rarity Rarity)[] _swatches = new (Label, Rarity)[5];

    public AccessibilityPanel(Action back) => _back = back;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 10);
        AddChild(MainMenu.MakeLabel("ACCESSIBILITY", 22, Head));

        _vision = new OptionButton { FocusMode = FocusModeEnum.All };
        foreach (string name in Accessibility.VisionNames) _vision.AddItem(Loc.T(name));
        _vision.AddThemeFontSizeOverride("font_size", 17);
        _vision.ItemSelected += i => { Accessibility.SetVision((ColorVision)(int)i); Paint(); };
        AddChild(Row("Colour-blind mode", _vision));

        var swatches = new HBoxContainer();
        swatches.AddThemeConstantOverride("separation", 16);
        foreach (Rarity rarity in Enum.GetValues<Rarity>())
        {
            Label l = MainMenu.MakeLabel(Catalog.RarityName(rarity), 16, Ink);
            swatches.AddChild(l);
            _swatches[(int)rarity] = (l, rarity);
        }
        AddChild(swatches);
        Label note = MainMenu.MakeLabel("Changes rarity, team and ping colours, and paints enemy telegraphs with stripes in colours that stay apart.", 15, InkDim);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        note.CustomMinimumSize = new Vector2(LabelWidth + 260f, 0f);
        AddChild(note);

        _flashes = new CheckButton { FocusMode = FocusModeEnum.All };
        _flashes.Toggled += Accessibility.SetReduceFlashes;
        AddChild(Row("Reduce flashes", _flashes));
        Label flashNote = MainMenu.MakeLabel("Softens hit flashes, explosions, boss phase changes and the health bar.", 15, InkDim);
        flashNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        flashNote.CustomMinimumSize = new Vector2(LabelWidth + 260f, 0f);
        AddChild(flashNote);

        var replay = new HBoxContainer();
        replay.AddThemeConstantOverride("separation", 14);
        Button again = MainMenu.MenuButton("REPLAY TUTORIAL", () => { ClassicTutorial.Replay(); _replayNote.Text = Loc.T("It plays at the start of your next Classic run."); });
        again.CustomMinimumSize = new Vector2(0f, 40f);
        again.AddThemeFontSizeOverride("font_size", 17);
        replay.AddChild(again);
        _replayNote = MainMenu.MakeLabel("", 15, InkDim);
        _replayNote.VerticalAlignment = VerticalAlignment.Center;
        _replayNote.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _replayNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        replay.AddChild(_replayNote);
        AddChild(replay);

        AddChild(MainMenu.MenuButton("BACK", _back));
        Refresh();
    }

    public void FocusFirst() => _vision?.GrabFocus();

    /// <summary>Re-read the settings (the other copy of the panel may have changed them).</summary>
    public void Refresh()
    {
        if (_vision is null) return;
        _vision.Selected = (int)Accessibility.Vision;
        _flashes.SetPressedNoSignal(Accessibility.ReduceFlashes);
        Paint();
    }

    private void Paint()
    {
        foreach (var (label, rarity) in _swatches)
            label.AddThemeColorOverride("font_color", Catalog.RarityColor(rarity));
    }

    private static Control Row(string name, Control control)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        Label l = MainMenu.MakeLabel(name, 17, Ink);
        l.CustomMinimumSize = new Vector2(LabelWidth, 0f);
        l.VerticalAlignment = VerticalAlignment.Center;
        if (control is OptionButton) control.CustomMinimumSize = new Vector2(250f, 32f);
        control.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        if (control is CheckButton) control.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        row.AddChild(l);
        row.AddChild(control);
        return row;
    }
}
