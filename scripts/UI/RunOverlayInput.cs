using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

internal static class RunOverlayInput
{
    internal static bool ScoreboardHeld => !DevPanel.IsOpen && !KeybindPanel.AnyListening
        && !GraphicsPanel.Blocking && InputMap.HasAction(ScoreboardPanel.Action)
        && Input.IsActionPressed(ScoreboardPanel.Action);

    internal static bool IsEscape(InputEvent input) => input is InputEventKey
        { Pressed: true, Echo: false, Keycode: Key.Escape };

    internal static bool OwnsScoreboardEvent(InputEvent input) => !DevPanel.IsOpen
        && !KeybindPanel.AnyListening && !GraphicsPanel.Blocking && !IsEscape(input)
        && InputMap.HasAction(ScoreboardPanel.Action) && input.IsAction(ScoreboardPanel.Action);
}
