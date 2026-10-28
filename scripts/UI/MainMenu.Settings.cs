using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Audio;
using Wavebreak.Meta;

namespace Wavebreak.UI;

public sealed partial class MainMenu : Node3D
{
    private Control BuildCodexPage()
    {
        var page = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _codex = new Codex();
        Place(_codex, 60f, 196f, 1160f, 450f);
        page.AddChild(_codex);

        Button back = MenuButton("BACK", () => ShowPage("main"));
        Place(back, 60f, 660f, 200f, 44f);
        page.AddChild(back);
        _codex.BackButton = back;
        return page;
    }

    // ----------------------------------------------------------- leaderboard

    private LeaderboardPage _leaderboard = null!;
    private Button _leaderboardBack = null!;

    private Control BuildLeaderboardPage()
    {
        var page = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _leaderboard = new LeaderboardPage();
        Place(_leaderboard, 60f, 196f, 1000f, 450f);
        page.AddChild(_leaderboard);
        _leaderboardBack = MenuButton("BACK", () => ShowPage("main"));
        Place(_leaderboardBack, 60f, 660f, 200f, 44f);
        page.AddChild(_leaderboardBack);
        return page;
    }

    // -------------------------------------------------------------- settings

    private Control BuildSettingsPage()
    {
        // On its own panel: the rows (label, slider, value) are wider than the
        // menu's dark strip and used to sit on the bright scene.
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        PanelContainer panel = Panel(44f, 178f, 560f, 450f);
        root.AddChild(panel);
        var page = new VBoxContainer();
        panel.AddChild(page);
        page.AddThemeConstantOverride("separation", 6);

        page.AddChild(SectionLabel("SOUND"));
        foreach (var (name, ch) in new[] { ("Master", VolumeChannel.Master), ("Effects", VolumeChannel.Sfx), ("Music", VolumeChannel.Music) })
        {
            VolumeChannel channel = ch;
            HSlider s = Slider(0, 10, 1, Mathf.Round(AudioSettings.Get(channel) * 10f));
            Label value = MakeLabel("", 16, Ink);
            void Show() => value.Text = $"{(int)s.Value}/10";
            Show();
            s.ValueChanged += v =>
            {
                int steps = (int)Math.Round(v - Math.Round(AudioSettings.Get(channel) * 10f));
                if (steps != 0) AudioSettings.Nudge(channel, steps);
                Show();
                _mute.SetPressedNoSignal(AudioSettings.Muted);
            };
            _volumeSliders.Add((s, channel));
            page.AddChild(Row(name, s, value));
        }
        // A row like the sliders: the label on the reading side, the switch
        // in the slider column, a short state word where the values are.
        _mute = new CheckButton { ButtonPressed = AudioSettings.Muted };
        Label muteState = MakeLabel(AudioSettings.Muted ? "On" : "Off", 16, Ink);
        _mute.Toggled += on =>
        {
            if (on != AudioSettings.Muted) AudioSettings.ToggleMute();
            muteState.Text = Loc.T(AudioSettings.Muted ? "On" : "Off");
        };
        var muteBox = new HBoxContainer();
        muteBox.AddChild(_mute);
        page.AddChild(Row("Mute all", muteBox, muteState));

        page.AddChild(SectionLabel("CONTROLS & DISPLAY"));
        HSlider sens = Slider(GameSettings.MinSensitivity, GameSettings.MaxSensitivity, GameSettings.SensitivityStep, GameSettings.Sensitivity);
        Label sensValue = MakeLabel($"{GameSettings.Sensitivity:0.00}x", 16, Ink);
        sens.ValueChanged += v => { GameSettings.SetSensitivity((float)v); sensValue.Text = $"{GameSettings.Sensitivity:0.00}x"; };
        page.AddChild(Row("Mouse sensitivity", sens, sensValue));
        page.AddChild(Row("Language", LanguagePicker(), MakeLabel("", 16, Ink)));

        Label keys = MakeLabel("In a run:  - / =  volume   ·   9 / 0  effects   ·   [ / ]  music   ·   M  mute", 15, InkDim);
        keys.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        page.AddChild(keys);
        // Side by side: with the language row added, three stacked buttons no
        // longer fit above the window's bottom edge.
        var pages = new HBoxContainer();
        pages.AddThemeConstantOverride("separation", 8);
        pages.AddThemeConstantOverride("separation", 6);
        foreach (Button b in new[] { MenuButton("GAMEPLAY", () => ShowPage("gameplay")), MenuButton("GRAPHICS", () => ShowPage("graphics")), MenuButton("KEY BINDINGS", () => ShowPage("keys")) })
        {
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            b.AddThemeFontSizeOverride("font_size", 16);
            // Three side by side: tighter margins so they share the column.
            foreach (string box in new[] { "normal", "hover", "pressed", "focus" })
                if (b.GetThemeStylebox(box) is StyleBoxFlat flat) { flat.ContentMarginLeft = 10; flat.ContentMarginRight = 8; }
            pages.AddChild(b);
        }
        page.AddChild(pages);
        page.AddChild(MenuButton("BACK", () => ShowPage("main")));
        return root;
    }

    /// <summary>English / العربية. Choosing one applies it (locale, layout
    /// direction, font fallback) and rebuilds the title on the settings page, so
    /// every label built in code is redone in the new language at once.</summary>
    private OptionButton LanguagePicker()
    {
        var ob = new OptionButton { FocusMode = Control.FocusModeEnum.All };
        // Native names are never translated: a player lost in the wrong
        // language must still find their own.
        ob.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
        ob.GetPopup().AutoTranslateMode = AutoTranslateModeEnum.Disabled;
        for (int i = 0; i < Loc.Codes.Length; i++) ob.AddItem(Loc.NativeNames[i], i);
        ob.Selected = Math.Max(0, Array.IndexOf(Loc.Codes, Loc.Language));
        ob.AddThemeFontSizeOverride("font_size", 18);
        ob.ItemSelected += index =>
        {
            string code = Loc.Codes[Math.Clamp((int)index, 0, Loc.Codes.Length - 1)];
            if (code == Loc.Language) return;
            Loc.SetLanguage(code, GetTree());
            DevReturnPage = "settings";
            GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
        };
        return ob;
    }

    private Control BuildKeysPage()
    {
        // Wider than the left vignette, so it carries its own backing.
        var frame = new PanelContainer();
        var style = new StyleBoxFlat { BgColor = PanelBack, ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 12, ContentMarginBottom = 12 };
        style.SetCornerRadiusAll(6);
        frame.AddThemeStyleboxOverride("panel", style);
        Place(frame, 44f, 180f, 640f, 530f);
        frame.AddChild(new KeybindPanel(() => ShowPage("settings")));
        return frame;
    }

    private Control BuildGraphicsPage()
    {
        var frame = new PanelContainer();
        var style = new StyleBoxFlat { BgColor = PanelBack, ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 12, ContentMarginBottom = 12 };
        style.SetCornerRadiusAll(6);
        frame.AddThemeStyleboxOverride("panel", style);
        Place(frame, 44f, 176f, 700f, 534f);
        frame.AddChild(_graphics = new GraphicsPanel(() => ShowPage("settings"), 370f));
        return frame;
    }

    private GameplayPanel _gameplay = null!;

    private Control BuildGameplayPage()
    {
        var frame = new PanelContainer();
        var style = new StyleBoxFlat { BgColor = PanelBack, ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 12, ContentMarginBottom = 12 };
        style.SetCornerRadiusAll(6);
        frame.AddThemeStyleboxOverride("panel", style);
        Place(frame, 44f, 186f, 560f, 0f);
        frame.AddChild(_gameplay = new GameplayPanel(() => ShowPage("settings"), 240f));
        return frame;
    }

    private static Control Row(string name, Control control, Label value)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        Label l = MakeLabel(name, 18, Ink);
        l.CustomMinimumSize = new Vector2(170f, 0f);
        control.CustomMinimumSize = new Vector2(200f, 28f);
        control.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(l);
        row.AddChild(control);
        row.AddChild(value);
        return row;
    }

    /// <summary>Right to left, the mute switch sits in a row like the sliders
    /// (name, then the control under the sliders' start) instead of a lone
    /// switch at the far edge. <paramref name="row"/> builds the row with an
    /// empty HBox in the control slot; the switch moves into it, text-less.</summary>
    internal static Control MuteRow(CheckButton mute, Func<string, Control> row)
    {
        string name = mute.Text;
        mute.Text = "";
        mute.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        mute.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        Control r = row(name);
        foreach (Node n in r.GetChildren())
            if (n is HBoxContainer slot) { slot.AddChild(mute); break; }
        return r;
    }

    private static HSlider Slider(double min, double max, double step, double value)
        => new() { MinValue = min, MaxValue = max, Step = step, Value = value, FocusMode = Control.FocusModeEnum.All };

    // ---------------------------------------------------------------- pages

}
