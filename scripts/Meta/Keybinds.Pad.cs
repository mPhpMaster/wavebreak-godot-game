using System.Collections.Generic;
using Godot;

namespace Wavebreak.Meta;

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

    /// <summary>The pad button or stick direction on an action, or null.</summary>
    public static InputEvent? PadEvent(string action)
    {
        if (!InputMap.HasAction(action)) return null;
        foreach (InputEvent ev in InputMap.ActionGetEvents(action))
            if (ev is InputEventJoypadButton or InputEventJoypadMotion) return ev;
        return null;
    }
}
