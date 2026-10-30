using System;
using Godot;

namespace Wavebreak.Net;

/// <summary>
/// Host side sanity limits on what a client says it hit. The client owns its
/// build, so the host cannot recompute a hit; these bounds stop garbage and
/// floods, not a client that lies plausibly. They are set so a legitimate
/// late-game build never meets them:
///   * a hit is capped by the target, not by a fixed number: past
///     <see cref="OverkillFactor"/> times its health the extra cannot matter,
///     even through the strongest block (Enemy.Expansion.cs takes a share off
///     after this point);
///   * the per-second damage budget grows with what is alive on the field, so
///     endless scaling cannot outgrow it;
///   * the hit count allows every enemy the director can field being hit by
///     every weapon and status tick at once, many times over.
/// Rate limits drop what is over budget and never drop the player.
/// </summary>
public sealed class HitGuard
{
    public const int MaxHitsPerSecond = 20000;
    public const float MinHitCap = 1e6f, OverkillFactor = 100f;
    public const float MinDamagePerSecond = 1e8f, FieldClearsPerSecond = 8f;
    public const float MaxKnockback = 1000f, MaxStatusSeconds = 60f;
    public const int MaxFreezeStacks = 1000;

    private double _windowEndsAt;
    private int _hits;
    private float _damage;

    /// <summary>The most one hit may carry against a target of <paramref name="maxHealth"/>;
    /// a NaN or infinite amount counts as nothing.</summary>
    public static float ClampAmount(float amount, float maxHealth)
        => float.IsFinite(amount) && amount > 0f ? MathF.Min(amount, MathF.Max(MinHitCap, maxHealth * OverkillFactor)) : 0f;

    public static float ClampKnockback(float knockback)
        => float.IsFinite(knockback) ? Math.Clamp(knockback, 0f, MaxKnockback) : 0f;

    public static float ClampStatus(float seconds)
        => float.IsFinite(seconds) ? Math.Clamp(seconds, 0f, MaxStatusSeconds) : 0f;

    /// <summary>A horizontal direction of at most unit length.</summary>
    public static Vector3 CleanDirection(float x, float z)
    {
        if (!float.IsFinite(x) || !float.IsFinite(z)) return Vector3.Zero;
        var d = new Vector3(x, 0f, z);
        return d.LengthSquared() > 1f ? d.Normalized() : d;
    }

    /// <summary>A whole number in the range of ids handed out so far.</summary>
    public static bool ValidId(float raw, int nextId) => raw >= 1f && raw < nextId && raw == MathF.Floor(raw);

    /// <summary>A batch is whole records and no bigger than a second's budget.</summary>
    public static bool ValidBatch(int length, int stride) => length % stride == 0 && length <= MaxHitsPerSecond * stride;

    /// <summary>One more hit this second? True until the count runs out.</summary>
    public bool TakeHit(double now)
    {
        Roll(now);
        if (_hits >= MaxHitsPerSecond) return false;
        _hits++;
        return true;
    }

    /// <summary>Spend <paramref name="damage"/> of real health removed. The budget
    /// reads the field only when the flat floor is used up, so an ordinary second
    /// costs nothing; <paramref name="fieldHealth"/> is the health alive right now.</summary>
    public bool TakeDamage(float damage, double now, Func<float> fieldHealth)
    {
        Roll(now);
        float after = _damage + damage;
        if (after > MinDamagePerSecond && after > fieldHealth() * FieldClearsPerSecond) return false;
        _damage = after;
        return true;
    }

    private void Roll(double now)
    {
        if (now < _windowEndsAt) return;
        _windowEndsAt = now + 1.0;
        _hits = 0;
        _damage = 0f;
    }
}
