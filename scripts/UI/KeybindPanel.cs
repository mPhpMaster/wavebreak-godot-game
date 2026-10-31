using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The key-binding list, shared by the title screen's settings and the pause
/// menu. Every action has two bindings, as the game ships with (WASD and the
/// arrow keys, Ctrl and C, Shift and right mouse). Click either one and press
/// the new key or mouse button; Backspace or Delete clears it; Esc cancels.
/// The third column is the gamepad's: press A on it, then the new button, or
/// push a stick or trigger; X on any binding clears it, B cancels.
/// All rows refresh after every change because a rebind can swap another
/// action's key.
/// </summary>
public sealed partial class KeybindPanel : VBoxContainer
{
    private static readonly Color Ink = new(0.93f, 0.95f, 0.98f);
    private static readonly Color Dim = new(0.60f, 0.65f, 0.75f);
    private static readonly Color Waiting = new(1.00f, 0.84f, 0.35f);

    private const float NameWidth = 182f;
    private const float KeyWidth = 128f;

    /// <summary>The column after the two key slots.</summary>
    private const int PadSlot = Keybinds.Slots;

    /// <summary>How far a stick or trigger must go to count as the new binding,
    /// past any rest drift.</summary>
    private const float StickBinds = 0.6f;

    private readonly Action _onBack;
    private readonly List<(string Action, Button[] Keys)> _rows = new();
    private (string Action, int Slot)? _listening;
    private Label _status = null!;
    private Control? _first;

    /// <summary>True while any panel waits for a key, so Esc handlers elsewhere
    /// (pause menu, title back-navigation) leave that Esc to cancel the bind.</summary>
    public static bool AnyListening { get; private set; }

    private (string Action, int Slot)? Listening
    {
        get => _listening;
        set { _listening = value; AnyListening = value is not null; }
    }

    public KeybindPanel(Action onBack) => _onBack = onBack;

    public override void _ExitTree() => AnyListening = false;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // usable from the paused pause menu
        Keybinds.Load();   // the pause menu is built before the run loads them
        AddThemeConstantOverride("separation", 8);

        AddChild(MainMenu.MakeLabel("KEY BINDINGS", 22, new Color(0.62f, 0.98f, 1.00f)));
        _status = MainMenu.MakeLabel("", 14, Dim);
        Hints.Follow(_status, () =>
        {
            if (Listening is not null) return _status.Text;
            return PadInput.UsingPad
                ? Loc.F("{0}: rebind   ·   {1}: clear   ·   {2}: back", Keybinds.PadLabel(JoyButton.A), Keybinds.PadLabel(JoyButton.X), Keybinds.PadLabel(JoyButton.B))
                : Loc.T("Click a key, then press the new one.  Backspace clears it, Esc cancels.");
        });
        AddChild(_status);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        Label h0 = MainMenu.MakeLabel("", 13, Dim); h0.CustomMinimumSize = new Vector2(NameWidth, 0f);
        header.AddChild(h0);
        foreach (string column in new[] { "primary", "secondary", "gamepad" })
        {
            Label h = MainMenu.MakeLabel(column, 13, Dim);
            h.CustomMinimumSize = new Vector2(KeyWidth, 0f);
            h.HorizontalAlignment = HorizontalAlignment.Center;
            header.AddChild(h);
        }
        AddChild(header);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(NameWidth + (KeyWidth + 12f) * (PadSlot + 1) + 8f, 360f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(list);
        BuildPadRows(list);

        foreach (var (action, label) in Keybinds.Actions)
        {
            if (!InputMap.HasAction(action)) continue;
            string a = action;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);
            Label name = MainMenu.MakeLabel(label, 16, Ink);
            name.CustomMinimumSize = new Vector2(NameWidth, 0f);
            name.ClipText = true;
            row.AddChild(name);

            var keys = new Button[PadSlot + 1];
            for (int slot = 0; slot <= PadSlot; slot++)
            {
                int s = slot;
                var key = new Button { CustomMinimumSize = new Vector2(KeyWidth, 34f), FocusMode = FocusModeEnum.All, ClipText = true };
                key.AddThemeFontSizeOverride("font_size", s == PadSlot ? 13 : 15);
                key.Pressed += () => Listen(a, s);
                key.GuiInput += e =>
                {
                    if (e is not InputEventJoypadButton { ButtonIndex: JoyButton.X, Pressed: true }) return;
                    key.AcceptEvent();
                    Listening = (a, s);
                    Commit(null);
                };
                row.AddChild(key);
                keys[slot] = key;
            }
            list.AddChild(row);
            _rows.Add((a, keys));
        }

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 12);
        buttons.AddChild(MainMenu.MenuButton("RESET TO DEFAULTS", () => { Keybinds.ResetAll(); Refresh(Loc.T("Defaults restored.")); }));
        buttons.AddChild(MainMenu.MenuButton("BACK", () => { Listening = null; _onBack(); }));
        AddChild(buttons);

        Refresh(null);
    }

    /// <summary>Land on the first row, for the keyboard and the pad.</summary>
    public void FocusFirst() => _first?.GrabFocus();

    /// <summary>The right stick's feel, above the bindings it goes with.</summary>
    private void BuildPadRows(VBoxContainer list)
    {
        PadSettings.Load();
        HSlider look = PadSlider(list, "Stick look speed", PadSettings.MinSensitivity, PadSettings.MaxSensitivity, PadSettings.SensitivityStep,
                                 PadSettings.LookSensitivity, "{0:0.00}x", PadSettings.SetLookSensitivity);
        PadSlider(list, "Look dead zone", PadSettings.MinDeadzone, PadSettings.MaxDeadzone, PadSettings.DeadzoneStep,
                  PadSettings.LookDeadzone, "{0:0.00}", PadSettings.SetLookDeadzone);
        PadSlider(list, "Move dead zone", PadSettings.MinDeadzone, PadSettings.MaxDeadzone, PadSettings.DeadzoneStep,
                  PadSettings.MoveDeadzone, "{0:0.00}", PadSettings.SetMoveDeadzone);

        var invert = new CheckButton { ButtonPressed = PadSettings.InvertLook, FocusMode = FocusModeEnum.All };
        invert.Toggled += PadSettings.SetInvertLook;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        Label name = MainMenu.MakeLabel("Invert stick look", 16, Ink);
        name.CustomMinimumSize = new Vector2(NameWidth, 0f);
        row.AddChild(name);
        row.AddChild(invert);
        list.AddChild(row);
        _first = look;
    }

    private static HSlider PadSlider(VBoxContainer list, string label, float min, float max, float step, float value, string format, Action<float> set)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        Label name = MainMenu.MakeLabel(label, 16, Ink);
        name.CustomMinimumSize = new Vector2(NameWidth, 0f);
        row.AddChild(name);
        var slider = new HSlider
        {
            MinValue = min, MaxValue = max, Step = step, Value = value, FocusMode = FocusModeEnum.All,
            CustomMinimumSize = new Vector2(220f, 28f), SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        Label shown = MainMenu.MakeLabel(string.Format(format, value), 16, Ink);
        slider.ValueChanged += v => { set((float)v); shown.Text = string.Format(format, v); };
        row.AddChild(slider);
        row.AddChild(shown);
        list.AddChild(row);
        return slider;
    }

    private void Listen(string action, int slot)
    {
        Refresh(null);
        Listening = (action, slot);
        foreach (var (a, keys) in _rows)
            if (a == action) keys[slot].Text = Loc.T(slot == PadSlot ? "press a button…" : "press a key…");
        _status.Text = Loc.T(slot == PadSlot
            ? "Press a pad button, or push a stick or trigger.  Backspace clears, Esc cancels."
            : "Press the new key or mouse button.  Backspace clears, Esc cancels.");
        _status.AddThemeColorOverride("font_color", Waiting);
    }

    public override void _Input(InputEvent @event)
    {
        if (Listening is null || !IsVisibleInTree()) return;
        bool pad = Listening.Value.Slot == PadSlot;

        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            GetViewport().SetInputAsHandled();
            if (key.Keycode == Key.Escape) { Cancel(); return; }
            if (key.Keycode is Key.Backspace or Key.Delete) { Commit(null); return; }
            if (!pad) Commit(new InputEventKey { PhysicalKeycode = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode });
        }
        else if (@event is InputEventMouseButton { Pressed: true } mouse
                 && mouse.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown))
        {
            // The click that started listening fired on release; this is a new press.
            GetViewport().SetInputAsHandled();
            if (!pad) Commit(new InputEventMouseButton { ButtonIndex = mouse.ButtonIndex });
        }
        else if (@event is InputEventJoypadButton { Pressed: true } button)
        {
            GetViewport().SetInputAsHandled();
            // The press of A that started listening fired on release too.
            if (pad) Commit(new InputEventJoypadButton { ButtonIndex = button.ButtonIndex });
            else if (button.ButtonIndex == JoyButton.B) Cancel();
        }
        else if (@event is InputEventJoypadMotion motion)
        {
            GetViewport().SetInputAsHandled();
            if (pad && Mathf.Abs(motion.AxisValue) >= StickBinds)
                Commit(new InputEventJoypadMotion { Axis = motion.Axis, AxisValue = Mathf.Sign(motion.AxisValue) });
        }
    }

    private void Cancel()
    {
        Listening = null;
        Refresh(Loc.T("Cancelled."));
    }

    private void Commit(InputEvent? ev)
    {
        var (action, slot) = Listening!.Value;
        Listening = null;
        string? swapped = slot == PadSlot ? Keybinds.RebindPad(action, ev) : Keybinds.Rebind(action, slot, ev);
        PadInput.IgnoreHeldDirection();
        string what = ev is null ? "cleared" : Keybinds.Describe(ev);
        string which = slot == PadSlot ? "gamepad" : slot == 0 ? "primary" : "secondary";
        Refresh(swapped is null || swapped == action
            ? Loc.F("{0} ({1}): {2}", LabelOf(action), which, what)
            : Loc.F("{0} ({1}): {2}  ·  swapped with {3}", LabelOf(action), which, what, LabelOf(swapped)));
    }

    private static string LabelOf(string action)
    {
        foreach (var (a, l) in Keybinds.Actions) if (a == action) return l;
        return action;
    }

    private void Refresh(string? message)
    {
        foreach (var (action, keys) in _rows)
            for (int slot = 0; slot < keys.Length; slot++)
                keys[slot].Text = Loc.T(Keybinds.Describe(slot == PadSlot ? Keybinds.PadEvent(action) : Keybinds.Get(action, slot)));
        if (message is not null)
        {
            _status.Text = message;
            _status.AddThemeColorOverride("font_color", Ink);
        }
    }
}
