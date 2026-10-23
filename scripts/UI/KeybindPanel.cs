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
/// All rows refresh after every change because a rebind can swap another
/// action's key.
/// </summary>
public sealed partial class KeybindPanel : VBoxContainer
{
    private static readonly Color Ink = new(0.93f, 0.95f, 0.98f);
    private static readonly Color Dim = new(0.60f, 0.65f, 0.75f);
    private static readonly Color Waiting = new(1.00f, 0.84f, 0.35f);

    private readonly Action _onBack;
    private readonly List<(string Action, Button[] Keys)> _rows = new();
    private (string Action, int Slot)? _listening;
    private Label _status = null!;

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
        AddThemeConstantOverride("separation", 8);

        AddChild(MainMenu.MakeLabel("KEY BINDINGS", 22, new Color(0.62f, 0.98f, 1.00f)));
        _status = MainMenu.MakeLabel("Click a key, then press the new one.  Backspace clears it, Esc cancels.", 14, Dim);
        AddChild(_status);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        Label h0 = MainMenu.MakeLabel("", 13, Dim); h0.CustomMinimumSize = new Vector2(220f, 0f);
        Label h1 = MainMenu.MakeLabel("primary", 13, Dim); h1.CustomMinimumSize = new Vector2(140f, 0f);
        h1.HorizontalAlignment = HorizontalAlignment.Center;
        Label h2 = MainMenu.MakeLabel("secondary", 13, Dim); h2.CustomMinimumSize = new Vector2(140f, 0f);
        h2.HorizontalAlignment = HorizontalAlignment.Center;
        header.AddChild(h0); header.AddChild(h1); header.AddChild(h2);
        AddChild(header);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(560f, 360f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(list);

        foreach (var (action, label) in Keybinds.Actions)
        {
            if (!InputMap.HasAction(action)) continue;
            string a = action;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);
            Label name = MainMenu.MakeLabel(label, 16, Ink);
            name.CustomMinimumSize = new Vector2(220f, 0f);
            row.AddChild(name);

            var keys = new Button[Keybinds.Slots];
            for (int slot = 0; slot < Keybinds.Slots; slot++)
            {
                int s = slot;
                var key = new Button { CustomMinimumSize = new Vector2(140f, 34f), FocusMode = FocusModeEnum.All };
                key.AddThemeFontSizeOverride("font_size", 16);
                key.Pressed += () => Listen(a, s);
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

    private void Listen(string action, int slot)
    {
        Refresh(null);
        Listening = (action, slot);
        foreach (var (a, keys) in _rows)
            if (a == action) keys[slot].Text = Loc.T("press a key…");
        _status.Text = Loc.T("Press the new key or mouse button.  Backspace clears, Esc cancels.");
        _status.AddThemeColorOverride("font_color", Waiting);
    }

    public override void _Input(InputEvent @event)
    {
        if (Listening is null || !IsVisibleInTree()) return;

        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            GetViewport().SetInputAsHandled();
            if (key.Keycode == Key.Escape) { Listening = null; Refresh(Loc.T("Cancelled.")); return; }
            if (key.Keycode is Key.Backspace or Key.Delete) { Commit(null); return; }
            Commit(new InputEventKey { PhysicalKeycode = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode });
        }
        else if (@event is InputEventMouseButton { Pressed: true } mouse
                 && mouse.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown))
        {
            // The click that started listening fired on release; this is a new press.
            GetViewport().SetInputAsHandled();
            Commit(new InputEventMouseButton { ButtonIndex = mouse.ButtonIndex });
        }
    }

    private void Commit(InputEvent? ev)
    {
        var (action, slot) = Listening!.Value;
        Listening = null;
        string? swapped = Keybinds.Rebind(action, slot, ev);
        string what = ev is null ? "cleared" : Keybinds.Describe(ev);
        string which = slot == 0 ? "primary" : "secondary";
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
                keys[slot].Text = Loc.T(Keybinds.Describe(Keybinds.Get(action, slot)));
        if (message is not null)
        {
            _status.Text = message;
            _status.AddThemeColorOverride("font_color", Ink);
        }
    }
}
