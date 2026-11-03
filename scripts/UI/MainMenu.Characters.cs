using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Audio;
using Wavebreak.Meta;

namespace Wavebreak.UI;

public sealed partial class MainMenu : Node3D
{
    private Control BuildCharacterPage()
    {
        var page = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        Label heading = MakeLabel("CHOOSE WHO WASHES UP", 20, InkDim);
        Place(heading, 60f, 196f, 400f, 28f);
        page.AddChild(heading);

        var list = new VBoxContainer();
        Place(list, 60f, 236f, 340f, 460f);
        // 6, not 10: seven characters and Back must fit above the window's edge.
        list.AddThemeConstantOverride("separation", 6);
        page.AddChild(list);

        // The roster outgrew the column: it scrolls, and follows keyboard focus.
        VBoxContainer charList = CharacterScroll(list);

        foreach (CharacterDef def in Characters.All)
        {
            CharacterDef c = def;
            bool open = c.IsAvailable;
            bool hidden = !open && Unlocks.ById(c.UnlockedBy) is { Secret: true };
            string label = open ? Loc.T(c.DisplayName) : hidden ? "???" : Loc.F("{0}  ·  locked", c.DisplayName);
            // Clicking picks the character; the run starts only from START RUN,
            // so the skin and hat can be chosen first.
            Button b = MenuButton(label, () => PickCharacter(c));
            if (ExpeditionPortraits.For(c.Id) is { } portrait)
            { b.Icon = portrait; b.ExpandIcon = true; b.AddThemeConstantOverride("icon_max_width", 28); }
            if (!open) b.AddThemeColorOverride("font_color", Locked);
            b.MouseEntered += () => b.GrabFocus();
            _charButtons.Add(b);
            charList.AddChild(b);
        }
        _difficulty = new DifficultyPicker();
        _difficulty.Changed += RefreshRouteBar;   // Endless switches the puzzle toggle off
        list.AddChild(_difficulty);

        // Route & challenges (UI/ChallengePicker), above the character panel: the
        // panel grows with skin, hat and passive rows and would cover it below.
        // As wide as the panel, no wider: the puzzle toggle stands beside it.
        _runButton = MenuButton("", () => ShowPage("run"));
        Place(_runButton, 470f, 186f, 520f, 42f);
        _runButton.AddThemeFontSizeOverride("font_size", 18);
        _runButton.ClipText = true;
        _runButton.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _runButton.MouseEntered += () => _runButton.GrabFocus();
        page.AddChild(_runButton);
        _difficulty.Button.FocusNeighborRight = _difficulty.Button.GetPathTo(_runButton);

        // The puzzle route in one press (Meta/Routes.SetPuzzle); the route
        // page's pill still cycles every route. A note under it says why it
        // is off limits with a map challenge or Endless.
        _puzzleToggle = PuzzleToggle(TogglePuzzleRoute);
        Place(_puzzleToggle, 1000f, 186f, 262f, 42f);
        _puzzleToggle.MouseEntered += () => _puzzleToggle.GrabFocus();
        page.AddChild(_puzzleToggle);
        // Under it, the Puzzle Gauntlet: puzzle maps only (UI/MainMenu.Gauntlet.cs).
        _gauntletToggle = GauntletToggle(ToggleGauntlet);
        Place(_gauntletToggle, 1000f, 234f, 262f, 42f);
        _gauntletToggle.MouseEntered += () => _gauntletToggle.GrabFocus();
        page.AddChild(_gauntletToggle);
        _puzzleNote = MakeLabel("", 14, Gold);
        _puzzleNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Place(_puzzleNote, 1000f, 282f, 262f, 64f);
        page.AddChild(_puzzleNote);
        PadInput.DeviceChanged += RefreshRouteBar;   // the [X] on the toggle
        _startButton = MenuButton("START RUN", () => { if (_shownCharacter is { } sel) StartRun(sel); });
        _startButton.AddThemeColorOverride("font_color", Gold);
        _startButton.AddThemeColorOverride("font_focus_color", Gold);
        _startButton.AddThemeColorOverride("font_hover_color", Gold);
        list.AddChild(MenuButton("BACK", () => ShowPage("main")));

        var panel = Panel(470f, 236f, 520f, 300f);
        page.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);
        _charName = MakeLabel("", 30, Ink);
        _charLook = MakeLabel("", 14, InkDim);
        _charBlurb = MakeLabel("", 17, Ink);
        _charBlurb.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _charStats = MakeLabel("", 16, Accent);
        _charStats.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _charLock = MakeLabel("", 16, Gold);
        _charLock.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _charPassive = MakeLabel("", 16, Gold);
        _charPassive.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        foreach (Label l in new[] { _charName, _charLook, _charBlurb, _charStats, _charPassive, _charLock }) box.AddChild(l);

        // Skin picker: the arrow buttons and the left/right keys both cycle it.
        // The buttons take no focus so the up/down character list keeps it.
        var skinRow = new HBoxContainer();
        skinRow.AddThemeConstantOverride("separation", 10);
        // Right to left, "previous" sits on the right and points that way.
        Button prev = SkinArrow(Loc.IsRtl ? ">" : "<", -1);
        Button next = SkinArrow(Loc.IsRtl ? "<" : ">", +1);
        _skinLabel = MakeLabel("", 18, Ink);
        _skinLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _skinLabel.HorizontalAlignment = HorizontalAlignment.Center;
        skinRow.AddChild(prev);
        skinRow.AddChild(_skinLabel);
        skinRow.AddChild(next);
        box.AddChild(skinRow);
        BuildHatRow(box);
        // Last in the panel, under the skin and hat it applies to.
        _startButton.CustomMinimumSize = new Vector2(0f, 46f);
        box.AddChild(_startButton);
        return page;
    }

    private Button SkinArrow(string text, int step)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(44f, 36f) };
        b.AddThemeFontSizeOverride("font_size", 20);
        b.Pressed += () => CycleSkin(step);
        return b;
    }

    private void CycleSkin(int step)
    {
        if (_shownCharacter is not { } c) return;
        int n = c.Skins.Count;
        _skinIndex = ((_skinIndex + step) % n + n) % n;
        ShowSkin();
    }

    private void ShowSkin()
    {
        if (_shownCharacter is not { } c) return;
        SkinDef skin = c.Skins[_skinIndex];
        int n = c.Skins.Count;
        if (skin.IsAvailable)
        {
            _skinLabel.Text = Loc.F("skin: {0}   ({1}/{2})", skin.Name, _skinIndex + 1, n);
            _skinLabel.AddThemeColorOverride("font_color", Ink);
        }
        else
        {
            UnlockDef? source = Unlocks.ById(Unlocks.SourceOf(skin.Id));
            string how = source is null ? "" : " — " + Loc.T(source.Secret && !Profile.IsUnlocked(source.Id) ? source.Hint : source.Description);
            _skinLabel.Text = Loc.F("skin: {0}  ·  locked{1}   ({2}/{3})", skin.Name, how, _skinIndex + 1, n);
            _skinLabel.AddThemeColorOverride("font_color", Locked);
        }
        _skinLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        ShowHeroSkin(skin);
        ShowHat();
    }

    private void ShowCharacter(CharacterDef c)
    {
        bool open = c.IsAvailable;
        UnlockDef? source = Unlocks.ById(c.UnlockedBy);
        bool hidden = !open && source is { Secret: true };

        // Name, blurb, starting weapon and stats, passive: UI/CharacterInfo,
        // shared with the CO-OP page.
        CharacterInfo info = CharacterInfo.Of(c);
        info.Into(_charName, _charBlurb, _charStats, null);
        _charPassive.Text = info.Passive;
        _charLook.Text = Loc.F("look: {0}   ·   id: {1}", Loc.Ltr(c.LookId), Loc.Ltr(c.Id));
        _charLook.AutoTranslateMode = AutoTranslateModeEnum.Disabled; // ids, a developer line
        _charLock.Text = open ? Loc.T("Pick a skin and a hat, then press START RUN")
            : hidden ? Loc.F("Secret — {0}", source!.Hint)
            : Loc.F("Locked — {0}", source?.Description ?? "");
        if (open) TintHero(c);

        if (_shownCharacter != c)
        {
            _shownCharacter = c;
            SkinDef chosen = c.SelectedSkin;
            _skinIndex = 0;
            for (int i = 0; i < c.Skins.Count; i++) if (c.Skins[i].Id == chosen.Id) _skinIndex = i;
        }
        ShowSkin();
    }

    /// <summary>Make <paramref name="c"/> the selection: the panel, hero, skin
    /// and hat rows follow it, its button turns gold, and START RUN is enabled
    /// when it is unlocked. Focus moves to START RUN so Enter twice still plays.</summary>
    private void PickCharacter(CharacterDef c, bool focusStart = true)
    {
        ShowCharacter(c);
        for (int i = 0; i < _charButtons.Count; i++)
        {
            CharacterDef each = Characters.All[i];
            Color colour = each == c ? Gold : each.IsAvailable ? Ink : Locked;
            _charButtons[i].AddThemeColorOverride("font_color", colour);
        }
        _startButton.Disabled = !c.IsAvailable;
        if (focusStart && c.IsAvailable) _startButton.GrabFocus();
    }

    /// <summary>Route and modifiers, plus the selected tier's run length.</summary>
    private static string RouteBarText()
    {
        DifficultyTier tier = EndlessMode.Selected ? DifficultyTiers.All[0] : DifficultyTiers.Selected;
        if (Routes.GauntletSelected && Routes.PuzzleBlockedReason() is null) return ChallengePicker.Summary() + "  ·  " + GauntletBarText();
        return ChallengePicker.Summary() + "  ·  " + Loc.F("{0} maps, portal {1} s", tier.Phases, tier.PortalSeconds);
    }

    /// <summary>The route bar and the puzzle toggle beside it.</summary>
    private void RefreshRouteBar()
    {
        if (_runButton is null || !IsInstanceValid(_runButton)) return;
        _runButton.Text = RouteBarText();
        _runButton.TooltipText = _runButton.Text;   // in full, when the bar trims it
        string? blocked = Routes.PuzzleBlockedReason();
        StylePuzzleToggle(_puzzleToggle, Routes.PuzzleSelected && blocked is null, blocked, padKey: true);
        StyleGauntletToggle(_gauntletToggle, Routes.GauntletSelected && blocked is null, blocked);
        _puzzleNote.Text = blocked is null ? "" : Loc.T(blocked);
    }

    private void TogglePuzzleRoute()
    {
        if (Routes.PuzzleBlockedReason() is not null) return;
        Routes.SetPuzzle(!Routes.PuzzleSelected);
        Profile.Save();
        RefreshRouteBar();
    }

    /// <summary>The PUZZLE ROUTE: ON/OFF toggle, on this page and in the CO-OP
    /// lobby: the puzzle glyph, then the state.</summary>
    internal static Button PuzzleToggle(Action pressed)
    {
        Button b = MenuButton("", pressed);
        b.Name = "PuzzleRouteToggle";
        b.Icon = Icons.Texture("puzzle");
        b.ExpandIcon = true;
        b.AddThemeConstantOverride("icon_max_width", 26);
        b.AddThemeConstantOverride("h_separation", 10);
        b.AddThemeFontSizeOverride("font_size", 18);
        b.ClipText = true;
        b.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        return b;
    }

    /// <summary>Show the toggle as on or off, or greyed out when
    /// <paramref name="blocked"/> gives a reason (it keeps focus, so a pad can
    /// still land on it and read why). With <paramref name="padKey"/> the pad
    /// button that presses it from anywhere on the page leads the text.</summary>
    internal static void StylePuzzleToggle(Button b, bool on, string? blocked, bool padKey)
    {
        string state = Loc.T(on ? "PUZZLE ROUTE: ON" : "PUZZLE ROUTE: OFF");
        b.Text = padKey && PadInput.UsingPad ? $"[{Keybinds.PadLabel(JoyButton.X)}]  {state}" : state;
        b.Disabled = blocked is not null;
        b.TooltipText = Loc.T(blocked ?? (on
            ? "A short puzzle map before each of Classic's maps. Press to go back to the route you had."
            : "Press for the puzzle route: a short puzzle map before each of Classic's maps."));
        Color tint = blocked is not null ? Locked : on ? Gold : Ink;
        foreach (string slot in new[] { "font_color", "font_disabled_color" }) b.AddThemeColorOverride(slot, tint);
        foreach (string slot in new[] { "icon_normal_color", "icon_disabled_color", "icon_hover_color", "icon_pressed_color" })
            b.AddThemeColorOverride(slot, tint);
        b.AddThemeColorOverride("icon_focus_color", blocked is not null ? Locked : Gold);
    }

    private void StartRun(CharacterDef c)
    {
        if (!c.IsAvailable) return;
        Profile.SelectedCharacter = c.Id;
        // Only a skin the player owns is remembered; picking a locked one
        // previews it but runs in the last owned choice.
        if (_shownCharacter == c && c.Skins[_skinIndex] is { IsAvailable: true } skin)
            Profile.SelectSkin(c.Id, skin.Id);
        SaveHatChoice(c);
        Profile.Save();
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, RunScene);
    }

}
