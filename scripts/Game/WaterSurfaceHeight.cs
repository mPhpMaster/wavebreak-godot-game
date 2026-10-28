using Godot;

namespace Wavebreak.Game;

/// <summary>The same wave equation is evaluated by movement and the surface shader.</summary>
public static class WaterSurfaceHeight
{
    public const float BaseHeight = 70f;
    public static float Seconds => Main.Instance?.Run?.Seconds ?? 0f;

    public static float At(Vector3 at) => At(at.X, at.Z, Seconds);

    public static float At(float x, float z, float seconds)
        => BaseHeight + 1.2f * Mathf.Sin(x * 0.13f + seconds * 0.9f)
            + 0.75f * Mathf.Cos(z * 0.17f - seconds * 0.8f)
            + 0.35f * Mathf.Sin((x + z) * 0.25f + seconds * 1.3f);
}
