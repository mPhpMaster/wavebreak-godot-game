using System.Collections.Generic;
using Godot;

namespace Wavebreak.Meta;

/// <summary>Whose button names to show.</summary>
public enum PadStyle
{
    Xbox,
    PlayStation,
    Nintendo,
}

/// <summary>
/// The gamepad half of <see cref="Keybinds"/>. project.godot carries keys and
/// mouse buttons only, so every action gets its pad default here, before the
/// shipped defaults are captured. Same InputMap actions, so nothing downstream
/// (Player, the panels) knows which device a press came from.
///
/// The defaults keep one input per action, the rule the rebind swap relies on:
/// the level-up actions sit on the triggers and L3 rather than sharing a face
/// button with something the player does in a fight.
/// </summary>
public static partial class Keybinds
{
    private static InputEvent Btn(JoyButton button) => new InputEventJoypadButton { ButtonIndex = button };

    private static InputEvent Axis(JoyAxis axis, float sign) => new InputEventJoypadMotion { Axis = axis, AxisValue = sign };

    /// <summary>What each action does on a pad out of the box.</summary>
    private static readonly (string Action, InputEvent Event)[] PadShipped =
    {
        ("move_left", Axis(JoyAxis.LeftX, -1f)),
        ("move_right", Axis(JoyAxis.LeftX, 1f)),
        ("move_forward", Axis(JoyAxis.LeftY, -1f)),
        ("move_back", Axis(JoyAxis.LeftY, 1f)),
        ("look_left", Axis(JoyAxis.RightX, -1f)),
        ("look_right", Axis(JoyAxis.RightX, 1f)),
        ("look_up", Axis(JoyAxis.RightY, -1f)),
        ("look_down", Axis(JoyAxis.RightY, 1f)),
        ("jump", Btn(JoyButton.A)),
        ("slide", Btn(JoyButton.B)),
        ("dash", Btn(JoyButton.X)),
        ("interact", Btn(JoyButton.Y)),
        ("city_hook_left", Btn(JoyButton.LeftShoulder)),
        ("city_hook_right", Btn(JoyButton.RightShoulder)),
        ("stats", Btn(JoyButton.Back)),
        ("scoreboard", Btn(JoyButton.DpadDown)),
        ("ping", Btn(JoyButton.RightStick)),
        ("pause", Btn(JoyButton.Start)),
        ("camera_zoom_in", Btn(JoyButton.DpadRight)),
        ("camera_zoom_out", Btn(JoyButton.DpadLeft)),
        ("help_toggle", Btn(JoyButton.DpadUp)),
        ("levelup_reroll", Axis(JoyAxis.TriggerLeft, 1f)),
        ("levelup_banish", Axis(JoyAxis.TriggerRight, 1f)),
        ("levelup_skip", Btn(JoyButton.LeftStick)),
    };

    private static void AddPadDefaults()
    {
        foreach (var (action, ev) in PadShipped)
        {
            if (!InputMap.HasAction(action)) InputMap.AddAction(action, 0.2f);
            if (PadEvent(action) is null) InputMap.ActionAddEvent(action, ev);
        }
    }

    private static readonly Dictionary<string, InputEvent> PadDefaults = new();

    private static void CapturePadDefaults()
    {
        foreach (var (action, _) in Actions)
            if (PadEvent(action) is { } ev) PadDefaults[action] = (InputEvent)ev.Duplicate();
    }

    /// <summary>
    /// Put <paramref name="ev"/> on the action's pad slot; null clears it. As
    /// with keys, an input another action already uses swaps the two, so a
    /// button never drives two actions and nothing is silently lost. Returns
    /// the action that gave it up, or null.
    /// </summary>
    public static string? RebindPad(string action, InputEvent? ev)
    {
        InputEvent? old = PadEvent(action);
        string? swapped = null;
        if (ev is not null)
            foreach (var (other, _) in Actions)
                if (other != action && PadEvent(other) is { } theirs && SamePad(theirs, ev))
                {
                    SetPad(other, old);
                    swapped = other;
                    break;
                }
        SetPad(action, ev);
        Save();
        UI.Hints.Refresh();
        return swapped;
    }

    private static void SetPad(string action, InputEvent? ev)
    {
        foreach (InputEvent e in InputMap.ActionGetEvents(action))
            if (e is InputEventJoypadButton or InputEventJoypadMotion) InputMap.ActionEraseEvent(action, e);
        if (ev is not null) InputMap.ActionAddEvent(action, ev);
    }

    private static void ResetPad()
    {
        foreach (var (action, _) in Actions)
            if (InputMap.HasAction(action))
                SetPad(action, PadDefaults.TryGetValue(action, out InputEvent? ev) ? (InputEvent)ev.Duplicate() : null);
    }

    private static bool SamePad(InputEvent a, InputEvent b) => (a, b) switch
    {
        (InputEventJoypadButton x, InputEventJoypadButton y) => x.ButtonIndex == y.ButtonIndex,
        (InputEventJoypadMotion x, InputEventJoypadMotion y) => x.Axis == y.Axis && Mathf.Sign(x.AxisValue) == Mathf.Sign(y.AxisValue),
        _ => false,
    };

    // Stored as "button:<index>" or "axis:<index>:<+1 or -1>", empty when cleared,
    // beside the keys in their own section: a file from before pads has none,
    // and the shipped defaults stand.
    private static string EncodePad(InputEvent? ev) => ev switch
    {
        InputEventJoypadButton b => $"button:{(long)b.ButtonIndex}",
        InputEventJoypadMotion m => $"axis:{(long)m.Axis}:{(m.AxisValue < 0f ? -1 : 1)}",
        _ => "",
    };

    private static InputEvent? DecodePad(string s)
    {
        string[] p = s.Split(':');
        if (p.Length >= 2 && p[0] == "button" && long.TryParse(p[1], out long button))
            return new InputEventJoypadButton { ButtonIndex = (JoyButton)button };
        if (p.Length == 3 && p[0] == "axis" && long.TryParse(p[1], out long axis) && int.TryParse(p[2], out int sign))
            return new InputEventJoypadMotion { Axis = (JoyAxis)axis, AxisValue = sign < 0 ? -1f : 1f };
        return null;
    }

    private static void WritePad(ConfigFile cfg)
    {
        foreach (var (action, _) in Actions)
            if (InputMap.HasAction(action)) cfg.SetValue("pad", action, EncodePad(PadEvent(action)));
    }

    private static void ReadPad(ConfigFile cfg)
    {
        foreach (var (action, _) in Actions)
            if (InputMap.HasAction(action) && cfg.HasSectionKey("pad", action))
                SetPad(action, DecodePad(cfg.GetValue("pad", action).AsString()));
    }

    /// <summary>--check-pad: the file format, without touching the real file.</summary>
    internal static void SaveForCheck(string path) => WriteTo(path);

    internal static void LoadPadForCheck(string path)
    {
        var cfg = new ConfigFile();
        if (cfg.Load(path) == Error.Ok) ReadPad(cfg);
    }

    /// <summary>"A", "LT", "Left stick up": what a binding row shows. English
    /// keys, like the mouse names; the letters stay as printed on the pad.</summary>
    private static string PadName(JoyButton button)
    {
        PadStyle style = UI.PadInput.Style;
        return button switch
        {
            JoyButton.A => style switch { PadStyle.PlayStation => "Cross", PadStyle.Nintendo => "B", _ => "A" },
            JoyButton.B => style switch { PadStyle.PlayStation => "Circle", PadStyle.Nintendo => "A", _ => "B" },
            JoyButton.X => style switch { PadStyle.PlayStation => "Square", PadStyle.Nintendo => "Y", _ => "X" },
            JoyButton.Y => style switch { PadStyle.PlayStation => "Triangle", PadStyle.Nintendo => "X", _ => "Y" },
            JoyButton.LeftShoulder => style switch { PadStyle.PlayStation => "L1", PadStyle.Nintendo => "L", _ => "LB" },
            JoyButton.RightShoulder => style switch { PadStyle.PlayStation => "R1", PadStyle.Nintendo => "R", _ => "RB" },
            JoyButton.LeftStick => "L3",
            JoyButton.RightStick => "R3",
            JoyButton.Start => style switch { PadStyle.PlayStation => "Options", PadStyle.Nintendo => "Plus", _ => "Menu" },
            JoyButton.Back => style switch { PadStyle.PlayStation => "Share", PadStyle.Nintendo => "Minus", _ => "View" },
            JoyButton.Guide => "Home",
            JoyButton.DpadUp => "D-pad up",
            JoyButton.DpadDown => "D-pad down",
            JoyButton.DpadLeft => "D-pad left",
            JoyButton.DpadRight => "D-pad right",
            _ => $"Pad {(int)button}",
        };
    }

    private static string PadName(JoyAxis axis, float value)
    {
        PadStyle style = UI.PadInput.Style;
        bool negative = value < 0f;
        return axis switch
        {
            JoyAxis.LeftX => negative ? "Left stick left" : "Left stick right",
            JoyAxis.LeftY => negative ? "Left stick up" : "Left stick down",
            JoyAxis.RightX => negative ? "Right stick left" : "Right stick right",
            JoyAxis.RightY => negative ? "Right stick up" : "Right stick down",
            JoyAxis.TriggerLeft => style switch { PadStyle.PlayStation => "L2", PadStyle.Nintendo => "ZL", _ => "LT" },
            JoyAxis.TriggerRight => style switch { PadStyle.PlayStation => "R2", PadStyle.Nintendo => "ZR", _ => "RT" },
            _ => $"Axis {(int)axis}",
        };
    }

    /// <summary>
    /// What to press for an action, as a hint shows it: the pad's binding while a
    /// pad is in use, else the primary key, else whichever exists. Translated.
    /// </summary>
    public static string Hint(string action)
    {
        InputEvent? pad = PadEvent(action);
        InputEvent? key = InputMap.HasAction(action) ? Primary(action) : null;
        return Loc.T(Describe(UI.PadInput.UsingPad ? pad ?? key : key ?? pad));
    }

    /// <summary>The name of a pad button that is not on an action.</summary>
    public static string PadLabel(JoyButton button) => Loc.T(PadName(button));

    /// <summary>The name of a trigger or stick that is not on an action.</summary>
    public static string PadLabel(JoyAxis axis) => Loc.T(PadName(axis, 1f));

    /// <summary>The pad button or stick direction on an action, or null.</summary>
    public static InputEvent? PadEvent(string action)
    {
        if (!InputMap.HasAction(action)) return null;
        foreach (InputEvent ev in InputMap.ActionGetEvents(action))
            if (ev is InputEventJoypadButton or InputEventJoypadMotion) return ev;
        return null;
    }
}
