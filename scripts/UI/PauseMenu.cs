using System;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Audio;
using Wavebreak.Game;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// Esc (or P) in a run: pause, free the mouse, and show the character sheet with
/// Resume / Settings / Restart / Main menu / Quit under it.
///
/// The sheet is the existing <see cref="StatsPanel"/> held open, not a second
/// copy — the pause screen is where people look at their build the longest, so
/// it has to show exactly what Tab shows.
///
/// It refuses to open over the level-up panel and the results screen: both
/// already own the paused tree, and a second owner that unpauses on close would
/// unfreeze the game behind a choice that has not been made.
/// </summary>
public sealed partial class PauseMenu : Node
{
    private readonly StatsPanel _sheet;
    private readonly LevelUpPanel _levelUp;
    private readonly ResultsScreen _results;

    private Control _settings = null!;
    private Button _resume = null!;
    private bool _open;
    private bool _mouseWasCaptured;

    public PauseMenu(StatsPanel sheet, LevelUpPanel levelUp, ResultsScreen results)
    {
        _sheet = sheet;
        _levelUp = levelUp;
        _results = results;
    }

    public bool IsOpen => _open;

    public override void _Ready()
    {
        Name = "PauseMenu";
        ProcessMode = ProcessModeEnum.Always;

        _resume = MainMenu.MenuButton("RESUME", Close);
        _sheet.ActionRow.AddChild(_resume);
        _sheet.ActionRow.AddChild(MainMenu.MenuButton("SETTINGS", () => ShowSettings(true)));
        _sheet.ActionRow.AddChild(MainMenu.MenuButton("RESTART", Restart));
        _sheet.ActionRow.AddChild(MainMenu.MenuButton("MAIN MENU", ToMainMenu));
        _sheet.ActionRow.AddChild(MainMenu.MenuButton("QUIT", () => GetTree().Quit()));
        foreach (Node child in _sheet.ActionRow.GetChildren())
            if (child is Button b) b.CustomMinimumSize = new Vector2(150f, 44f);

        _settings = BuildSettings();
        _sheet.AddChild(_settings);
        _settings.Visible = false;

        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--check-pause="))
                _ = CheckPause(arg["--check-pause=".Length..]);
    }

    /// <summary>
    /// --check-pause=&lt;dir&gt;: presses Esc for real, checks the game paused with
    /// the sheet pinned and the mouse free, captures the menu and its settings
    /// page, then presses Esc twice and checks it resumed. Prints PAUSE PASS/FAIL.
    /// </summary>
    private async System.Threading.Tasks.Task CheckPause(string dir)
    {
        SceneTree tree = GetTree();
        bool ok = true;
        void Expect(string what, bool cond) { GD.Print($"PAUSE {(cond ? "ok  " : "FAIL")} {what}"); ok &= cond; }
        void Esc()
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = false });
        }
        async System.Threading.Tasks.Task Wait(double s)
            => await tree.ToSignal(tree.CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);

        await Wait(2.0);
        Esc();
        await Wait(0.4);
        Expect("Esc opens the pause menu", _open && tree.Paused && _sheet.Visible && _sheet.Pinned);
        Expect("mouse is free while paused", Input.MouseMode == Input.MouseModeEnum.Visible);
        await Screenshot.Capture(tree, this, System.IO.Path.Combine(dir, "pause.png"));
        ShowSettings(true);
        await Wait(0.3);
        await Screenshot.Capture(tree, this, System.IO.Path.Combine(dir, "pause-settings.png"));
        Esc();
        await Wait(0.3);
        Expect("Esc closes settings first, menu stays", _open && !_settings.Visible && tree.Paused);
        Esc();
        await Wait(0.3);
        Expect("Esc again resumes the run", !_open && !tree.Paused && !_sheet.Pinned);
        GD.Print(ok ? "PAUSE PASS" : "PAUSE FAIL");
        tree.Quit();
    }

    // ----------------------------------------------------------------- input

    public override void _Input(InputEvent @event)
    {
        bool toggle = @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }
                      || (InputMap.HasAction("pause") && @event.IsActionPressed("pause"));
        if (!toggle) return;

        if (_open)
        {
            // Esc backs out of settings first, then out of the pause menu.
            if (_settings.Visible) ShowSettings(false);
            else Close();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!CanOpen()) return;
        Open();
        GetViewport().SetInputAsHandled();
    }

    private bool CanOpen()
    {
        if (Screenshot.Requested) return false;
        if (_levelUp.IsOpen || _results.Visible) return false;
        if (Main.Instance?.Run is { Over: true }) return false;
        return !GetTree().Paused;
    }

    // ------------------------------------------------------------ open/close

    private void Open()
    {
        _open = true;
        _mouseWasCaptured = Input.MouseMode == Input.MouseModeEnum.Captured;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GetTree().Paused = true;
        _sheet.Pinned = true;
        ShowSettings(false);
        _resume.GrabFocus();
    }

    private void Close()
    {
        if (!_open) return;
        _open = false;
        ShowSettings(false);
        _sheet.Pinned = false;
        GetTree().Paused = false;
        if (_mouseWasCaptured) PlayerCamera.TryCaptureMouse();
    }

    private void Restart()
    {
        _open = false;
        GetTree().Paused = false;
        GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
    }

    private void ToMainMenu()
    {
        _open = false;
        GetTree().Paused = false;
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, MainMenu.TitleScene);
    }

    // -------------------------------------------------------------- settings

    private void ShowSettings(bool show)
    {
        _settings.Visible = show;
        _sheet.ActionRow.Visible = _open && !show;
    }

    /// <summary>The same controls as the title screen's settings page, as an
    /// overlay on the sheet, so nothing has to be changed by leaving the run.</summary>
    private Control BuildSettings()
    {
        var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        centre.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var frame = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.07f, 0.11f, 0.98f),
            BorderColor = new Color(0.30f, 0.55f, 0.70f, 0.9f),
            ContentMarginLeft = 26, ContentMarginRight = 26, ContentMarginTop = 18, ContentMarginBottom = 18,
        };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(10);
        frame.AddThemeStyleboxOverride("panel", style);
        centre.AddChild(frame);

        var page = new VBoxContainer { CustomMinimumSize = new Vector2(440f, 0f) };
        page.AddThemeConstantOverride("separation", 10);
        frame.AddChild(page);

        page.AddChild(MainMenu.MakeLabel("SETTINGS", 22, new Color(0.62f, 0.98f, 1.00f)));

        CheckButton mute = new() { Text = "Mute all", ButtonPressed = AudioSettings.Muted };
        foreach (var (name, ch) in new[] { ("Master", VolumeChannel.Master), ("Effects", VolumeChannel.Sfx), ("Music", VolumeChannel.Music) })
        {
            VolumeChannel channel = ch;
            var slider = new HSlider { MinValue = 0, MaxValue = 10, Step = 1, Value = Mathf.Round(AudioSettings.Get(channel) * 10f),
                                       SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(200f, 24f) };
            Label value = MainMenu.MakeLabel($"{(int)slider.Value}/10", 16, Colors.White);
            slider.ValueChanged += v =>
            {
                int steps = (int)Math.Round(v - Math.Round(AudioSettings.Get(channel) * 10f));
                if (steps != 0) AudioSettings.Nudge(channel, steps);
                value.Text = $"{(int)slider.Value}/10";
                mute.SetPressedNoSignal(AudioSettings.Muted);
            };
            page.AddChild(Row(name, slider, value));
        }

        mute.AddThemeFontSizeOverride("font_size", 18);
        mute.Toggled += on => { if (on != AudioSettings.Muted) AudioSettings.ToggleMute(); };
        page.AddChild(mute);

        var sens = new HSlider { MinValue = GameSettings.MinSensitivity, MaxValue = GameSettings.MaxSensitivity,
                                 Step = GameSettings.SensitivityStep, Value = GameSettings.Sensitivity,
                                 SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(200f, 24f) };
        Label sensValue = MainMenu.MakeLabel($"{GameSettings.Sensitivity:0.00}x", 16, Colors.White);
        sens.ValueChanged += v => { GameSettings.SetSensitivity((float)v); sensValue.Text = $"{GameSettings.Sensitivity:0.00}x"; };
        page.AddChild(Row("Mouse", sens, sensValue));

        var full = new CheckButton { Text = "Fullscreen", ButtonPressed = GameSettings.Fullscreen };
        full.AddThemeFontSizeOverride("font_size", 18);
        full.Toggled += GameSettings.SetFullscreen;
        page.AddChild(full);

        page.AddChild(MainMenu.MenuButton("BACK", () => ShowSettings(false)));
        return centre;
    }

    private static HBoxContainer Row(string name, Control control, Label value)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        Label label = MainMenu.MakeLabel(name, 16, new Color(0.75f, 0.80f, 0.88f));
        label.CustomMinimumSize = new Vector2(110f, 0f);
        row.AddChild(label);
        row.AddChild(control);
        value.CustomMinimumSize = new Vector2(56f, 0f);
        row.AddChild(value);
        return row;
    }
}
