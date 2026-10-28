using Godot;

namespace Wavebreak.Game;

public static class GrappleWind
{
    public static float Intensity(bool city, bool attached, bool alive, float speed, float effects)
        => city && attached && alive && float.IsFinite(speed) && float.IsFinite(effects)
            ? Mathf.Clamp((speed - 18f) / 42f, 0f, 1f) * Mathf.Clamp(effects, 0f, 1f) : 0f;
}
