using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The graphics page, shared by the title screen's settings and the pause menu
/// like <see cref="KeybindPanel"/>. Everything applies live except window mode,
/// resolution and monitor: those wait for APPLY and then ask "keep these?" for
/// 10 seconds, reverting on their own if the answer never comes (a mode the
/// monitor cannot show leaves the player unable to click anything).
///
/// Picking a preset sets the quality options; touching any quality option
/// turns the preset into "Custom". Camera and HUD options are preferences and
/// no preset touches them.
/// </summary>
public sealed partial class GraphicsPanel : VBoxContainer
{
    private static readonly Color Ink = new(0.93f, 0.95f, 0.98f);
    private static readonly Color Dim = new(0.60f, 0.65f, 0.75f);
    private static readonly Color Head = new(0.62f, 0.98f, 1.00f);
    private static readonly Color Waiting = new(1.00f, 0.84f, 0.35f);

    private const double ConfirmSeconds = 10.0;

    private readonly Action _onBack;
    private readonly float _scrollHeight;
    private readonly List<Action> _refreshers = new();
    private readonly List<OptionButton> _options = new();

    private ScrollContainer _scroll = null!;
    private OptionButton _preset = null!;
    private OptionButton _mode = null!;
    private OptionButton _res = null!;
    private OptionButton? _monitor;
    private Label _status = null!;
    private HBoxContainer _confirm = null!;
    private Label _confirmLabel = null!;
    private Button _keep = null!;
    private Button _apply = null!;

    private readonly List<Vector2I> _sizes = new();
    private int _pendMode, _pendW, _pendH, _pendMonitor;
    private (int mode, int w, int h, int mon)? _revertTo;
    private double _confirmLeft;

    private static ulong _popupClosedAt;
    private static int _popupsOpen;
    private static GraphicsPanel? _confirming;

    /// <summary>True while a dropdown is open (or has just closed on this Esc)
    /// or a display change waits for confirmation: Esc handlers elsewhere leave
    /// that Esc alone.</summary>
    public static bool Blocking =>
        _popupsOpen > 0 || Time.GetTicksMsec() - _popupClosedAt < 150 ||
        (_confirming is { } c && IsInstanceValid(c) && c._revertTo is not null);

    public GraphicsPanel(Action onBack, float scrollHeight = 380f)
    {
        _onBack = onBack;
        _scrollHeight = scrollHeight;
    }

    public override void _ExitTree()
    {
        if (_revertTo is not null) Revert();
        _popupsOpen = 0;
        if (_confirming == this) _confirming = null;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // usable from the paused pause menu
        AddThemeConstantOverride("separation", 8);

        AddChild(MainMenu.MakeLabel("GRAPHICS", 22, Head));
        _status = MainMenu.MakeLabel("Changes apply at once.  Window mode and resolution wait for APPLY.", 14, Dim);
        AddChild(_status);

        var scroll = _scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(640f, _scrollHeight),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
        };
        AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 5);
        scroll.AddChild(list);

        GraphicsConfig C() => GraphicsSettings.Current;

        // ---------------------------------------------------------- preset
        _preset = Option(GraphicsSettings.PresetNames, () => C().Preset, i => GraphicsSettings.SetPreset(i));
        list.AddChild(Row("Preset", _preset));

        // ---------------------------------------------------------- display
        list.AddChild(Section("DISPLAY"));
        _mode = new OptionButton();
        StyleOption(_mode);
        foreach (string m in GraphicsSettings.WindowModes) _mode.AddItem(Loc.T(m));
        _mode.ItemSelected += i => { _pendMode = (int)i; PendingChanged(); };
        list.AddChild(Row("Window mode", _mode));

        _res = new OptionButton();
        StyleOption(_res);
        _res.ItemSelected += i => { _pendW = _sizes[(int)i].X; _pendH = _sizes[(int)i].Y; PendingChanged(); };
        list.AddChild(Row("Resolution", _res, "windowed"));

        int screens = DisplayServer.GetScreenCount();
        if (screens > 1)
        {
            _monitor = new OptionButton();
            StyleOption(_monitor);
            for (int s = 0; s < screens; s++)
            {
                Vector2I size = DisplayServer.ScreenGetSize(s);
                _monitor.AddItem(Loc.F("Monitor {0}  ({1}x{2})", s + 1, size.X, size.Y));
            }
            _monitor.ItemSelected += i => { _pendMonitor = (int)i; PendingChanged(); };
            list.AddChild(Row("Monitor", _monitor));
        }

        list.AddChild(Row("VSync", Option(GraphicsSettings.VSyncModes, () => C().VSync,
            i => GraphicsSettings.Change(c => c.VSync = i, false))));
        var caps = new List<string>();
        foreach (int f in GraphicsSettings.FpsCaps) caps.Add(f == 0 ? "Unlimited" : $"{f}");
        list.AddChild(Row("FPS cap", Option(caps.ToArray(), () => Math.Max(0, Array.IndexOf(GraphicsSettings.FpsCaps, C().FpsCap)),
            i => GraphicsSettings.Change(c => c.FpsCap = GraphicsSettings.FpsCaps[i], false))));

        // ---------------------------------------------------------- rendering
        list.AddChild(Section("RENDERING"));
        list.AddChild(SliderRow("Render scale", 50, 100, 5, () => C().RenderScale * 100f, v => $"{v:0}%",
            v => GraphicsSettings.Change(c => c.RenderScale = v / 100f, true)));
        OptionButton up = Option(GraphicsSettings.UpscaleModes, () => C().Upscale,
            i => GraphicsSettings.Change(c => c.Upscale = i, true));
        if (!GraphicsSettings.Fsr2Available) up.SetItemDisabled(2, true);
        list.AddChild(Row("Upscaling", up));
        list.AddChild(Row("Anti-aliasing", Option(GraphicsSettings.AntiAliasModes, () => C().AntiAlias,
            i => GraphicsSettings.Change(c => c.AntiAlias = i, true))));
        list.AddChild(Row("Shadows", Option(GraphicsSettings.ShadowModes, () => C().Shadows,
            i => GraphicsSettings.Change(c => c.Shadows = i, true))));

        // ---------------------------------------------------------- effects
        list.AddChild(Section("EFFECTS"));
        list.AddChild(Toggle("Ambient occlusion (SSAO)", () => C().Ssao, on => GraphicsSettings.Change(c => c.Ssao = on, true)));
        list.AddChild(Toggle("Glow / bloom", () => C().Glow, on => GraphicsSettings.Change(c => c.Glow = on, true)));
        list.AddChild(Toggle("Fog  (Sea Fret stays, thinner)", () => C().Fog, on => GraphicsSettings.Change(c => c.Fog = on, true)));
        list.AddChild(Toggle("Screen-space reflections", () => C().Ssr, on => GraphicsSettings.Change(c => c.Ssr = on, true)));
        list.AddChild(Toggle("Indirect light (SSIL)", () => C().Ssil, on => GraphicsSettings.Change(c => c.Ssil = on, true)));

        // ---------------------------------------------------------- camera and HUD
        list.AddChild(Section("CAMERA & HUD"));
        list.AddChild(SliderRow("Field of view", 50, 100, 1, () => C().Fov, v => $"{v:0}°",
            v => GraphicsSettings.Change(c => c.Fov = v, false)));
        list.AddChild(SliderRow("Camera shake", 0, 100, 5, () => C().Shake * 100f, v => $"{v:0}%",
            v => GraphicsSettings.Change(c => c.Shake = v / 100f, false)));
        list.AddChild(SliderRow("Speed & dash zoom", 0, 100, 5, () => C().ScreenFx * 100f, v => $"{v:0}%",
            v => GraphicsSettings.Change(c => c.ScreenFx = v / 100f, false)));
        list.AddChild(SliderRow("Weapon effects opacity", 10, 100, 5, () => C().WeaponFx * 100f, v => $"{v:0}%",
            v => GraphicsSettings.Change(c => c.WeaponFx = v / 100f, false)));
        list.AddChild(SliderRow("Brightness", 50, 150, 5, () => C().Brightness * 100f, v => $"{v:0}%",
            v => GraphicsSettings.Change(c => c.Brightness = v / 100f, false)));
        list.AddChild(SliderRow("UI scale", 80, 150, 5, () => C().UiScale * 100f, v => $"{v:0}%",
            v => GraphicsSettings.Change(c => c.UiScale = v / 100f, false), live: false));
        list.AddChild(Toggle("Show FPS counter", () => C().ShowFps, on => GraphicsSettings.Change(c => c.ShowFps = on, false)));

        // ---------------------------------------------------------- density
        list.AddChild(Section("VISUAL DENSITY"));
        list.AddChild(SliderRow("Props  (next map)", 25, 150, 5, () => C().PropDensity * 100f, v => $"{v:0}%",
            v => GraphicsSettings.Change(c => c.PropDensity = v / 100f, true)));
        var pops = new List<string>();
        foreach (int p in GraphicsSettings.PopupCaps) pops.Add($"{p}");
        list.AddChild(Row("Max damage numbers", Option(pops.ToArray(), () => Math.Max(0, Array.IndexOf(GraphicsSettings.PopupCaps, C().MaxPopups)),
            i => GraphicsSettings.Change(c => c.MaxPopups = GraphicsSettings.PopupCaps[i], true))));

        // ---------------------------------------------------------- confirm + buttons
        _confirm = new HBoxContainer { Visible = false };
        _confirm.AddThemeConstantOverride("separation", 12);
        _confirmLabel = MainMenu.MakeLabel("", 16, Waiting);
        _confirmLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _confirm.AddChild(_confirmLabel);
        _keep = MainMenu.MenuButton("KEEP", Keep);
        _confirm.AddChild(_keep);
        _confirm.AddChild(MainMenu.MenuButton("REVERT", Revert));
        AddChild(_confirm);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 12);
        _apply = MainMenu.MenuButton("APPLY", ApplyDisplay);
        buttons.AddChild(_apply);
        buttons.AddChild(MainMenu.MenuButton("RESET TO DEFAULTS", () =>
        {
            GraphicsSettings.ResetDefaults();
            Refresh();
            Say("Defaults restored (High).");
        }));
        buttons.AddChild(MainMenu.MenuButton("BACK", () => { if (_revertTo is not null) Revert(); _onBack(); }));
        AddChild(buttons);

        Refresh();
    }

    public void FocusFirst() => _preset.GrabFocus();

    /// <summary>Focus the last option (scrolls the list to its end).</summary>
    public async void FocusLast()
    {
        _options[^1].GrabFocus();
        // The list has no layout until it has been shown for a frame or two.
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _scroll.EnsureControlVisible(_options[^1]);
    }

    // ================================================================ display

    private void LoadPending()
    {
        GraphicsConfig c = GraphicsSettings.Current;
        _pendMode = c.WindowMode;
        _pendW = c.Width; _pendH = c.Height;
        _pendMonitor = c.Monitor >= 0 ? c.Monitor : DisplayServer.WindowGetCurrentScreen();
        BuildSizes();
    }

    private void BuildSizes()
    {
        _sizes.Clear();
        int screen = Math.Clamp(_pendMonitor, 0, Math.Max(0, DisplayServer.GetScreenCount() - 1));
        Vector2I max = DisplayServer.ScreenGetSize(screen);
        if (max.X <= 0 || max.Y <= 0) max = new Vector2I(7680, 4320);
        foreach (Vector2I s in GraphicsSettings.CommonSizes)
            if (s.X <= max.X && s.Y <= max.Y) _sizes.Add(s);
        if (_sizes.Count == 0) _sizes.Add(GraphicsSettings.CommonSizes[0]);
        if (!_sizes.Contains(new Vector2I(_pendW, _pendH))) _sizes.Add(new Vector2I(_pendW, _pendH));
        _res.Clear();
        foreach (Vector2I s in _sizes) _res.AddItem($"{s.X} x {s.Y}");
    }

    private void ShowPending()
    {
        _mode.Select(_pendMode);
        _res.Select(Math.Max(0, _sizes.IndexOf(new Vector2I(_pendW, _pendH))));
        _res.Disabled = _pendMode != 0;
        if (_monitor is not null) _monitor.Select(Math.Clamp(_pendMonitor, 0, _monitor.ItemCount - 1));
        _apply.Disabled = !PendingDiffers();
    }

    private bool PendingDiffers()
    {
        GraphicsConfig c = GraphicsSettings.Current;
        int mon = c.Monitor >= 0 ? c.Monitor : DisplayServer.WindowGetCurrentScreen();
        return _pendMode != c.WindowMode || _pendMonitor != mon
            || (_pendMode == 0 && (_pendW != c.Width || _pendH != c.Height));
    }

    private void PendingChanged()
    {
        if (_monitor is not null) BuildSizes();
        ShowPending();
        if (PendingDiffers()) Say("Press APPLY to use the new display settings.", Waiting);
    }

    private void ApplyDisplay()
    {
        if (!PendingDiffers()) return;
        GraphicsConfig c = GraphicsSettings.Current;
        _revertTo = (c.WindowMode, c.Width, c.Height, c.Monitor);
        _confirming = this;
        GraphicsSettings.CommitDisplay(_pendMode, _pendW, _pendH, _monitor is null ? c.Monitor : _pendMonitor);
        _confirmLeft = ConfirmSeconds;
        _confirm.Visible = true;
        UpdateConfirm();
        ShowPending();
        _keep.GrabFocus();
    }

    /// <summary>For the check: the confirmation is showing.</summary>
    public bool Confirming => _revertTo is not null;

    public void Keep()
    {
        _revertTo = null;
        _confirm.Visible = false;
        Say("Display settings kept.");
        _apply.GrabFocus();
        ShowPending();
    }

    public void Revert()
    {
        if (_revertTo is not { } r) return;
        _revertTo = null;
        GraphicsSettings.CommitDisplay(r.mode, r.w, r.h, r.mon);
        _confirm.Visible = false;
        if (IsInsideTree())
        {
            LoadPending();
            ShowPending();
            Say("Display settings reverted.");
            _mode.GrabFocus();
        }
    }

    private void UpdateConfirm()
        => _confirmLabel.Text = Loc.F("Keep these display settings?  Reverting in {0:0} s", Math.Ceiling(_confirmLeft));

    public override void _Process(double delta)
    {
        if (_revertTo is null) return;
        _confirmLeft -= delta;
        if (_confirmLeft <= 0) { Revert(); return; }
        UpdateConfirm();
    }

    public override void _Input(InputEvent @event)
    {
        if (_revertTo is null || !IsVisibleInTree()) return;
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } || @event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            Revert();
        }
    }

    // ================================================================ refresh

    /// <summary>Re-read every control from the settings (after a preset, a
    /// reset, or when the page is shown).</summary>
    public void Refresh()
    {
        foreach (Action r in _refreshers) r();
        if (_revertTo is null)
        {
            LoadPending();
            ShowPending();
        }
    }

    private void Say(string text, Color? color = null)
    {
        _status.Text = Loc.T(text);
        _status.AddThemeColorOverride("font_color", color ?? Ink);
    }

    // ================================================================ builders

    private OptionButton Option(string[] items, Func<int> get, Action<int> set)
    {
        var ob = new OptionButton();
        StyleOption(ob);
        foreach (string s in items) ob.AddItem(Loc.T(s));
        ob.ItemSelected += i => { set((int)i); Refresh(); };
        _refreshers.Add(() => ob.Select(get()));
        return ob;
    }

    private void StyleOption(OptionButton ob)
    {
        ob.FocusMode = FocusModeEnum.All;
        ob.CustomMinimumSize = new Vector2(280f, 32f);
        ob.AddThemeFontSizeOverride("font_size", 16);
        ob.AddThemeColorOverride("font_focus_color", new Color(1.00f, 0.84f, 0.38f));
        PopupMenu popup = ob.GetPopup();
        popup.AddThemeFontSizeOverride("font_size", 16);
        popup.AboutToPopup += () => _popupsOpen++;
        popup.PopupHide += () => { _popupsOpen = Math.Max(0, _popupsOpen - 1); _popupClosedAt = Time.GetTicksMsec(); };
        _options.Add(ob);
    }

    private Control Toggle(string name, Func<bool> get, Action<bool> set)
    {
        // The switch sits in the control column, its state spelled out beside it.
        var cb = new CheckButton { FocusMode = FocusModeEnum.All };
        cb.AddThemeFontSizeOverride("font_size", 16);
        Label state = MainMenu.MakeLabel("", 16, Ink);
        cb.Toggled += on => { set(on); Refresh(); };
        _refreshers.Add(() => { cb.SetPressedNoSignal(get()); state.Text = Loc.T(get() ? "On" : "Off"); });
        HBoxContainer row = Row(name, cb);
        row.AddChild(state);
        return row;
    }

    /// <summary>A slider row. <paramref name="live"/> false commits on release
    /// only (UI scale would otherwise move the slider under the mouse).</summary>
    private Control SliderRow(string name, double min, double max, double step, Func<float> get,
                              Func<double, string> format, Action<float> set, bool live = true)
    {
        var s = new HSlider
        {
            MinValue = min, MaxValue = max, Step = step, FocusMode = FocusModeEnum.All,
            CustomMinimumSize = new Vector2(280f, 28f), SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        Label value = MainMenu.MakeLabel("", 16, Ink);
        value.CustomMinimumSize = new Vector2(64f, 0f);
        s.ValueChanged += v =>
        {
            value.Text = format(v);
            if (live) { set((float)v); RefreshPreset(); }
        };
        if (!live)
            s.DragEnded += changed => { if (changed) set((float)s.Value); };
        // Keyboard and gamepad steps have no drag; commit those at once.
        if (!live)
            s.GuiInput += e =>
            {
                if (e is InputEventKey or InputEventJoypadButton or InputEventJoypadMotion)
                    Callable.From(() => { if (!Mathf.IsEqualApprox((float)s.Value, get())) set((float)s.Value); }).CallDeferred();
            };
        _refreshers.Add(() => { s.SetValueNoSignal(get()); value.Text = format(s.Value); });
        HBoxContainer row = Row(name, s);
        row.AddChild(value);
        return row;
    }

    private void RefreshPreset() => _preset.Select(GraphicsSettings.Current.Preset);

    private static HBoxContainer Row(string name, Control control, string? note = null)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        Label l = MainMenu.MakeLabel(name, 16, Ink);
        l.CustomMinimumSize = new Vector2(220f, 0f);
        row.AddChild(l);
        row.AddChild(control);
        if (note is not null) row.AddChild(MainMenu.MakeLabel(note, 13, Dim));
        return row;
    }

    private static Label Section(string text)
    {
        Label l = MainMenu.MakeLabel(text, 14, Dim);
        l.CustomMinimumSize = new Vector2(0f, 26f);
        l.VerticalAlignment = VerticalAlignment.Bottom;
        return l;
    }

    /// <summary>For the check: every dropdown on the page.</summary>
    public IReadOnlyList<OptionButton> Options => _options;
}
