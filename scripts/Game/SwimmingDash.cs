using Godot;

namespace Wavebreak.Game;

public static class SwimmingDash
{
    public const float Duration = .55f;
    public static Vector3 Steer(Vector3 direction, Vector3 wish, float delta)
    {
        if (wish.LengthSquared() < .01f) return direction;
        Vector3 target = wish.Normalized();
        Vector3 next = direction.MoveToward(target, Mathf.Max(0f, delta) * 8f);
        return next.LengthSquared() > .001f ? next.Normalized() : target;
    }
    public static float Speed(float swimmingSpeed, float remaining)
        => swimmingSpeed + (Mathf.Max(16f, swimmingSpeed * 2.8f) - swimmingSpeed)
            * Mathf.Clamp(remaining / Duration, 0f, 1f);
}
