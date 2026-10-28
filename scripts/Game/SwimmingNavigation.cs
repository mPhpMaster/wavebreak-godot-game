using Godot;
using Wavebreak.Data;

namespace Wavebreak.Game;

public static class SwimmingNavigation
{
    public const float ReinforcementScale = 1.12f;

    public static SwimmingIntent WithVertical(Vector3 wish, bool rise, bool dive)
    {
        bool reinforces = rise != dive && new Vector2(wish.X, wish.Z).LengthSquared() > 0.0025f
            && (rise && wish.Y > 0.2f || dive && wish.Y < -0.2f);
        if (reinforces) return new SwimmingIntent(wish.LimitLength(1f), ReinforcementScale, true);
        if (rise || dive) wish.Y = (rise ? 1f : 0f) - (dive ? 1f : 0f);
        return new SwimmingIntent(wish.LimitLength(1f), 1f, false);
    }
    public static Vector3 Wish(Vector2 input, float yaw, float pitch)
    {
        Vector3 forward = new(-Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), -Mathf.Cos(yaw) * Mathf.Cos(pitch));
        Vector3 right = new(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
        return (forward * -input.Y + right * input.X).LimitLength(1f);
    }
}
