using Godot;
using Wavebreak.Data;

namespace Wavebreak.Game;

public static class GrapplePhysics
{
    public const float Range = 92f;
    public const float MaxSpeed = 46f;
    public const float BoostSpeed = 60f;

    public static Vector3 Step(Vector3 position, Vector3 velocity, Vector3 anchor,
        Vector3 steering, ref float length, float gravity, float dt)
    {
        var hook = new HookAttachment { Attached = true, Anchor = anchor, Length = length };
        velocity = StepDual(position, velocity, hook, null, steering, gravity, dt, MaxSpeed);
        length = hook.Length;
        return velocity;
    }

    public static Vector3 StepDual(Vector3 position, Vector3 velocity, HookAttachment left,
        HookAttachment? right, Vector3 steering, float gravity, float dt, float cap)
    {
        int count = (left.Attached ? 1 : 0) + (right?.Attached == true ? 1 : 0);
        velocity += Vector3.Down * gravity * dt;
        if (count == 0) return velocity;
        Vector3 axis = Vector3.Zero;
        if (left.Attached) axis += (left.Anchor - position).Normalized();
        if (right?.Attached == true) axis += (right.Anchor - position).Normalized();
        axis = axis.Normalized();
        velocity += (steering - axis * steering.Dot(axis)).LimitLength() * 9f * dt;
        if (left.Attached) velocity = Pull(position, velocity, left, 1f / count, dt);
        if (right?.Attached == true) velocity = Pull(position, velocity, right, 1f / count, dt);
        return velocity.LimitLength(cap);
    }

    private static Vector3 Pull(Vector3 position, Vector3 velocity, HookAttachment hook, float weight, float dt)
    {
        Vector3 delta = hook.Anchor - position;
        float distance = delta.Length();
        Vector3 toward = delta / Mathf.Max(distance, 0.01f);
        hook.Length = Mathf.Max(3f, hook.Length - 11f * dt);
        if (distance > 3f) velocity += toward * 56f * weight * dt;
        if (distance > hook.Length)
        {
            velocity += toward * Mathf.Max(0f, velocity.Dot(-toward)) * Mathf.Min(1f, dt * 10f) * weight;
            velocity += toward * Mathf.Min(26f, (distance - hook.Length) * 9f) * weight * dt;
        }
        return velocity;
    }
}
