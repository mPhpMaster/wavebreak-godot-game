using Godot;

namespace Wavebreak.Game;

public static class SwimmingDash
{
    public const float Duration = .55f;
    public static Vector3 Steer(Vector3 direction, Vector3 wish, float delta)
    {
        if (wish.LengthSquared() < .01f) return direction;
        Vector3 target = wish.Normalized();
        if (direction.LengthSquared() < .001f) return target;
        direction = direction.Normalized();
        float angle = direction.AngleTo(target);
        if (angle < .001f) return target;
        Vector3 axis = direction.Cross(target);
        if (axis.LengthSquared() < .0001f)
            axis = direction.Cross(Mathf.Abs(direction.Y) < .9f ? Vector3.Up : Vector3.Right);
        return direction.Rotated(axis.Normalized(), Mathf.Min(angle, Mathf.Max(0, delta) * 12)).Normalized();
    }
    public static float Speed(float swimmingSpeed, float remaining)
        => swimmingSpeed + (Mathf.Max(16f, swimmingSpeed * 2.8f) - swimmingSpeed)
            * Mathf.Clamp(remaining / Duration, 0f, 1f);
}

