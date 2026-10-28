using Godot;

namespace Wavebreak.Actors;

public sealed partial class Player
{
    private bool _cityDashHandled;

    private bool ReadCityDash()
    {
        bool requested = _requestedDash;
        _requestedDash = false;
        if (Net.Coop.LocalHold || !_inputAvailable || !_hasDash) return requested && !Net.Coop.LocalHold;
        if (requested) return true;
        if (!Input.IsActionJustPressed("dash")) return false;
        foreach (InputEvent binding in InputMap.ActionGetEvents("dash"))
        {
            if (InputMap.ActionHasEvent("city_hook_left", binding)
                || InputMap.ActionHasEvent("city_hook_right", binding)) continue;
            bool held = binding switch
            {
                InputEventKey key when key.PhysicalKeycode != Key.None => Input.IsPhysicalKeyPressed(key.PhysicalKeycode),
                InputEventKey key => Input.IsKeyPressed(key.Keycode),
                InputEventMouseButton mouse => Input.IsMouseButtonPressed(mouse.ButtonIndex),
                InputEventJoypadButton pad => Input.IsJoyButtonPressed(pad.Device < 0 ? 0 : pad.Device, pad.ButtonIndex),
                InputEventJoypadMotion axis => Input.GetJoyAxis(axis.Device < 0 ? 0 : axis.Device, axis.Axis) * Mathf.Sign(axis.AxisValue) > .5f,
                _ => false,
            };
            if (held) return true;
        }
        // ActionPress is used by deterministic engine fixtures without a physical device.
        return Input.IsActionPressed("dash") && Input.GetActionStrength("dash") > 0f
            && !Input.IsActionPressed("city_hook_left") && !Input.IsActionPressed("city_hook_right")
            && Meta.Launch.Automated;
    }
}
