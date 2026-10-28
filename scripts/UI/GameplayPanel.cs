using System;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// Settings > Gameplay (<see cref="GameplaySettings"/>): compact level-up,
/// auto-pick on level up (Off / On) and the auto-pick rule. The title's
/// GAMEPLAY page shows it framed with BACK; the pause menu's settings carry
/// the same rows inline. Every change saves at once.
/// </summary>
public sealed partial class GameplayPanel : VBoxContainer
{
    private static readonly Color Ink = new(0.93f, 0.95f, 0.99f);
    private static readonly Color InkDim = new(0.66f, 0.71f, 0.80f);
    private static readonly Color Head = new(0.62f, 0.98f, 1.00f);

    private readonly Action? _back;
    private readonly float _labelWidth;
    private readonly bool _withTitle;

    private CheckButton _compact = null!;
    private OptionButton _auto = null!;
    private OptionButton _rule = null!;
    private OptionButton _from = null!;

    public GameplayPanel(Action? back, float labelWidth = 220f, bool withTitle = true)
    {
        _back = back;
        _labelWidth = labelWidth;
        _withTitle = withTitle;
    }

    public override void _Ready()
    {
        GameplaySettings.Load();
        AddThemeConstantOverride("separation", 10);

        if (_withTitle) AddChild(MainMenu.MakeLabel("GAMEPLAY", 22, Head));
        else AddChild(MainMenu.MakeLabel("GAMEPLAY", 15, InkDim));

        _compact = new CheckButton { ButtonPressed = GameplaySettings.CompactLevelUp, FocusMode = FocusModeEnum.All };
        _compact.Toggled += on => GameplaySettings.SetCompact(on);
        AddChild(Row("Compact level-up", _compact));

        _auto = Options(new[] { "Off", "On" }, GameplaySettings.AutoPick ? 1 : 0);
        _auto.ItemSelected += i => { GameplaySettings.SetAutoPick(i == 1); _rule.Disabled = i != 1; _from.Disabled = i != 1; };
        AddChild(Row("Auto-pick on level up", _auto));

        _rule = Options(GameplaySettings.RuleNames, (int)GameplaySettings.Rule);
        _rule.Disabled = !GameplaySettings.AutoPick;
        _rule.ItemSelected += i => GameplaySettings.SetRule((AutoPickRule)(int)i);
        AddChild(Row("Auto-pick rule", _rule));

        string[] froms = System.Array.ConvertAll(GameplaySettings.FromLevels, l => l <= 1 ? Loc.T("First level-up") : Loc.F("Level {0}", l));
        _from = Options(froms, System.Math.Max(0, System.Array.IndexOf(GameplaySettings.FromLevels, GameplaySettings.AutoPickFromLevel)));
        _from.Disabled = !GameplaySettings.AutoPick;
        _from.ItemSelected += i => GameplaySettings.SetAutoPickFromLevel(GameplaySettings.FromLevels[i]);
        AddChild(Row("Auto-pick starts at", _from));

        if (_withTitle)
        {
            Label note = MainMenu.MakeLabel(
                "Compact level-up: small cards at the bottom of the screen and the game keeps running, so you can keep moving while you choose " +
                "(pick with 1 / 2 / 3 or a click). Co-op always uses it. " +
                "Auto-pick: every level-up takes a card at once by the rule, and a note shows what it took.", 15, InkDim);
            note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            note.CustomMinimumSize = new Vector2(_labelWidth + 260f, 0f);
            AddChild(note);
        }

        if (_back is not null)
        {
            Button back = MainMenu.MenuButton("BACK", _back);
            AddChild(back);
        }
    }

    public void FocusFirst() => _compact?.GrabFocus();

    /// <summary>Re-read the settings (the other copy of the panel may have changed them).</summary>
    public void Refresh()
    {
        if (_compact is null) return;
        _compact.SetPressedNoSignal(GameplaySettings.CompactLevelUp);
        _auto.Selected = GameplaySettings.AutoPick ? 1 : 0;
        _rule.Selected = (int)GameplaySettings.Rule;
        _rule.Disabled = !GameplaySettings.AutoPick;
        _from.Selected = System.Math.Max(0, System.Array.IndexOf(GameplaySettings.FromLevels, GameplaySettings.AutoPickFromLevel));
        _from.Disabled = !GameplaySettings.AutoPick;
    }

    private static OptionButton Options(string[] names, int selected)
    {
        var ob = new OptionButton { FocusMode = FocusModeEnum.All };
        foreach (string n in names) ob.AddItem(Loc.T(n));
        ob.Selected = Math.Clamp(selected, 0, names.Length - 1);
        ob.AddThemeFontSizeOverride("font_size", 17);
        return ob;
    }

    private Control Row(string name, Control control)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        Label l = MainMenu.MakeLabel(name, 17, Ink);
        l.CustomMinimumSize = new Vector2(_labelWidth, 0f);
        l.VerticalAlignment = VerticalAlignment.Center;
        control.CustomMinimumSize = new Vector2(control is CheckButton ? 0f : 230f, 32f);
        control.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        if (control is CheckButton) control.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        row.AddChild(l);
        row.AddChild(control);
        return row;
    }
}
