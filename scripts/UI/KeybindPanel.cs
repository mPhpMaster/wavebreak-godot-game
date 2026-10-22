using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The key-binding list, shared by the title screen's settings and the pause
/// menu. Click a row, press the new key or mouse button; Esc cancels. Rows
/// refresh after every change because a rebind can swap another action's key.
/// </summary>
public sealed partial class KeybindPanel : VBoxContainer
{
    private static readonly Color Ink = new(0.93f, 0.95f, 0.98f);
    private static readonly Color Dim = new(0.60f, 0.65f, 0.75f);
    private static readonly Color Waiting = new(1.00f, 0.84f, 0.35f);

    private readonly Action _onBack;
    private readonly List<(string Action, Button Key, Label Alt)> _rows = new();
    private string? _listening;

    /// <summary>True while any panel waits for a key, so Esc handlers elsewhere
    /// (pause menu, title back-navigation) leave that Esc to cancel the bind.</summary>
    public static bool AnyListening { get; private set; }

    private string? Listening
    {
        get => _listening;
        set { _listening = value; AnyListening = value is not null; }
    }
    private Label _status = null!;

    public KeybindPanel(Action onBack) => _onBack = onBack;

    public override void _ExitTree() => AnyListening = false;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // usable from the paused pause menu
        AddThemeConstantOverride("separation", 8);

        AddChild(MainMenu.MakeLabel("KEY BINDINGS", 22, new Color(0.62f, 0.98f, 1.00f)));
        _status = MainMenu.MakeLabel("Click an action, then press a key or mouse button. Esc cancels.", 14, Dim);
        AddChild(_status);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(520f, 380f),
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
            var key = new Button { CustomMinimumSize = new Vector2(150f, 34f), FocusMode = FocusModeEnum.All };
            key.AddThemeFontSizeOverride("font_size", 16);
            key.Pressed += () => Listen(a);
            row.AddChild(key);
            Label alt = MainMenu.MakeLabel("", 13, Dim);
            row.AddChild(alt);
            list.AddChild(row);
            _rows.Add((a, key, alt));
        }

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 12);
        buttons.AddChild(MainMenu.MenuButton("RESET TO DEFAULTS", () => { Keybinds.ResetAll(); Refresh("Defaults restored."); }));
        buttons.AddChild(MainMenu.MenuButton("BACK", () => { Listening = null; _onBack(); }));
        AddChild(buttons);

        Refresh(null);
    }

    private void Listen(string action)
    {
        Listening = action;
        foreach (var r in _rows)
            if (r.Action == action) r.Key.Text = "press a key…";
        _status.Text = "Press the new key or mouse button. Esc cancels.";
        _status.AddThemeColorOverride("font_color", Waiting);
    }

    public override void _Input(InputEvent @event)
    {
        if (Listening is null || !IsVisibleInTree()) return;

        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            GetViewport().SetInputAsHandled();
            if (key.Keycode == Key.Escape) { Listening = null; Refresh("Cancelled."); return; }
            Commit(new InputEventKey { PhysicalKeycode = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode });
        }
        else if (@event is InputEventMouseButton { Pressed: true } mouse
                 && mouse.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown))
        {
            // The click that opened listening is a release by now; this is the new one.
            GetViewport().SetInputAsHandled();
            Commit(new InputEventMouseButton { ButtonIndex = mouse.ButtonIndex });
        }
    }

    private void Commit(InputEvent ev)
    {
        string action = Listening!;
        Listening = null;
        string? swapped = Keybinds.Rebind(action, ev);
        string label = LabelOf(action);
        Refresh(swapped is null
            ? $"{label}: {Keybinds.Describe(ev)}"
            : $"{label}: {Keybinds.Describe(ev)}  ·  swapped with {LabelOf(swapped)}");
    }

    private static string LabelOf(string action)
    {
        foreach (var (a, l) in Keybinds.Actions) if (a == action) return l;
        return action;
    }

    private void Refresh(string? message)
    {
        foreach (var (action, key, alt) in _rows)
        {
            key.Text = Keybinds.Describe(Keybinds.Primary(action));
            string all = Keybinds.DescribeAll(action);
            int sep = all.IndexOf(" / ", StringComparison.Ordinal);
            alt.Text = sep >= 0 ? "also " + all[(sep + 3)..] : "";
        }
        if (message is not null)
        {
            _status.Text = message;
            _status.AddThemeColorOverride("font_color", Ink);
        }
    }
}
