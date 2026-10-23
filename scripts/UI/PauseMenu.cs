using System;
using System.Collections.Generic;
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
    private Control _keys = null!;
    private Control _graphics = null!;
    private GraphicsPanel _graphicsPanel = null!;
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
        _keys = BuildKeys();
        _sheet.AddChild(_keys);
        _keys.Visible = false;
        _graphicsPanel = new GraphicsPanel(() => ShowGraphics(false), 430f);
        _graphics = Framed(_graphicsPanel);
        _sheet.AddChild(_graphics);
        _graphics.Visible = false;

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
        // Key bindings: open the page, click the first row, press K for real.
        ShowKeys(true);
        await Wait(0.3);
        // FindChildren matches engine class names only, never a C# class, so
        // walk the tree and test the type directly.
        static IEnumerable<Node> Walk(Node n)
        {
            foreach (Node c in n.GetChildren()) { yield return c; foreach (Node d in Walk(c)) yield return d; }
        }
        var rowKeys = new List<Button>();
        foreach (Node n in Walk(_keys))
            if (n is Button b && b.GetParent() is HBoxContainer row && row.GetChildCount() == 3) rowKeys.Add(b);
        Expect("key-binding page has two keys per action", rowKeys.Count >= 4);
        string first = Keybinds.Actions[0].Action;
        void Press(Key k)
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
        }
        async System.Threading.Tasks.Task Bind(Button b, Key k)
        {
            b.EmitSignal(BaseButton.SignalName.Pressed);
            await Wait(0.1);
            Press(k);
            await Wait(0.2);
        }
        if (rowKeys.Count >= 2)
        {
            await Bind(rowKeys[0], Key.K);
            Expect($"primary rebinds to K (now {Keybinds.Describe(Keybinds.Get(first, 0))})", Keybinds.Describe(Keybinds.Get(first, 0)) == "K");
            await Bind(rowKeys[1], Key.J);
            Expect($"secondary rebinds to J (now {Keybinds.Describe(Keybinds.Get(first, 1))})", Keybinds.Describe(Keybinds.Get(first, 1)) == "J");
            await Bind(rowKeys[0], Key.Space);
            Expect($"taking Jump's Space swaps: jump primary is now {Keybinds.Describe(Keybinds.Get("jump", 0))}",
                   Keybinds.Describe(Keybinds.Get(first, 0)) == "Space" && Keybinds.Describe(Keybinds.Get("jump", 0)) == "K");
            await Bind(rowKeys[1], Key.Backspace);
            Expect("Backspace clears the secondary", Keybinds.Get(first, 1) is null);
        }
        await Screenshot.Capture(tree, this, System.IO.Path.Combine(dir, "pause-keys.png"));
        Keybinds.ResetAll();
        ShowKeys(false);
        await Wait(0.2);

        // Graphics page: open from settings, capture, Esc back to settings.
        ShowGraphics(true);
        await Wait(0.4);
        Expect("graphics page opens over the paused run", _graphics.Visible && !_settings.Visible && tree.Paused);
        await Screenshot.Capture(tree, this, System.IO.Path.Combine(dir, "pause-graphics.png"));
        Esc();
        await Wait(0.3);
        Expect("Esc on graphics returns to settings", !_graphics.Visible && _settings.Visible && _open && tree.Paused);

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
        if (!toggle || DevPanel.IsOpen) return;
        // That Esc belongs to the key-binding row waiting for a key.
        if (KeybindPanel.AnyListening || GraphicsPanel.Blocking) return;

        if (_open)
        {
            // Esc backs out of settings first, then out of the pause menu.
            if (_graphics.Visible) ShowGraphics(false);
            else if (_keys.Visible) ShowKeys(false);
            else if (_settings.Visible) ShowSettings(false);
            else Close();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!CanOpen()) return;
        Open();
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Alt-tab or clicking another window pauses the run behind the
    /// pause menu. Automated runs render in an unfocused window, so they skip it.</summary>
    public override void _Notification(int what)
    {
        if (what is not ((int)NotificationApplicationFocusOut or (int)NotificationWMWindowFocusOut)) return;
        if (_open || Meta.Launch.Automated || !IsInsideTree() || !CanOpen()) return;
        Open();
        // The camera frees the mouse on the same focus loss, possibly first; the
        // run had it captured, so take it back when the player resumes.
        _mouseWasCaptured = true;
    }

    private bool CanOpen()
    {
        if (Screenshot.Requested || DevPanel.IsOpen) return false;
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
        if (show) _keys.Visible = false;
        if (show || !_open) _graphics.Visible = false;
        _sheet.ActionRow.Visible = _open && !show && !_keys.Visible;
    }

    private void ShowKeys(bool show)
    {
        _keys.Visible = show;
        _settings.Visible = !show && _open;
        _sheet.ActionRow.Visible = false;
    }

    private void ShowGraphics(bool show)
    {
        _graphics.Visible = show;
        _keys.Visible = false;
        _settings.Visible = !show && _open;
        _sheet.ActionRow.Visible = false;
        if (show)
        {
            _graphicsPanel.Refresh();
            _graphicsPanel.FocusFirst();
        }
    }

    private static Control Framed(Control content)
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
        frame.AddChild(content);
        return centre;
    }

    private Control BuildKeys()
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
        frame.AddChild(new KeybindPanel(() => ShowKeys(false)));
        return centre;
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

        // Boombox (UI/BoomboxPage on the title screen has the full list).
        Boombox.Load();
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        Label boxLabel = MainMenu.MakeLabel("", 16, new Color(0.75f, 0.80f, 0.88f));
        boxLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        boxLabel.ClipText = true;
        void ShowBox()
        {
            string now = MusicTracks.ById(MusicDirector.NowPlaying)?.Title ?? "-";
            boxLabel.Text = $"Boombox: {Boombox.Describe()}  ·  {now}";
        }
        Button mode = MainMenu.MenuButton("MODE", () => { Boombox.CycleMode(); MusicDirector.Reapply(); ShowBox(); });
        Button next = MainMenu.MenuButton("SKIP", () => { MusicDirector.Reapply(skip: true); ShowBox(); });
        foreach (Button b in new[] { mode, next })
        {
            b.CustomMinimumSize = new Vector2(84f, 36f);
            b.AddThemeFontSizeOverride("font_size", 16);
        }
        box.AddChild(boxLabel);
        box.AddChild(mode);
        box.AddChild(next);
        box.VisibilityChanged += ShowBox;
        ShowBox();
        page.AddChild(box);

        var sens = new HSlider { MinValue = GameSettings.MinSensitivity, MaxValue = GameSettings.MaxSensitivity,
                                 Step = GameSettings.SensitivityStep, Value = GameSettings.Sensitivity,
                                 SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(200f, 24f) };
        Label sensValue = MainMenu.MakeLabel($"{GameSettings.Sensitivity:0.00}x", 16, Colors.White);
        sens.ValueChanged += v => { GameSettings.SetSensitivity((float)v); sensValue.Text = $"{GameSettings.Sensitivity:0.00}x"; };
        page.AddChild(Row("Mouse", sens, sensValue));

        page.AddChild(MainMenu.MenuButton("GRAPHICS", () => ShowGraphics(true)));
        page.AddChild(MainMenu.MenuButton("KEY BINDINGS", () => ShowKeys(true)));
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
