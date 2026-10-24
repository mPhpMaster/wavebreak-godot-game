using System;
using Godot;

namespace Wavebreak.Actors;

/// <summary>
/// Host migration (Net/NetSession.Migration.cs): when the host leaves, the
/// player elected to take over turns every puppet it holds into a real enemy.
/// The body, position, facing and the host's last health number stay; the
/// contact damage and speed the puppet never needed are worked out again from
/// the director's current multipliers, and the AI starts from a clean slate.
/// </summary>
public sealed partial class Enemy
{
    /// <summary>A puppet becomes an enemy this machine simulates. One the
    /// local player had predicted dead (waiting on a confirmation that will
    /// never come now) dies here for real, drops and all.</summary>
    internal void PromoteFromPuppet(float damageMultiplier, float speedMultiplier)
    {
        if (_dead || _def is null) return;
        if (_health <= 0f)
        {
            _health = 0.01f;   // so Die() runs its normal path
            Die();
            return;
        }
        _contactDamage = MathF.Max(0f, _def.Damage * MathF.Max(0f, damageMultiplier));
        _abilityDamage = _contactDamage * MathF.Max(0f, _def.AbilityDamage);
        _speed = MathF.Max(0.1f, _def.MoveSpeed * MathF.Max(0.05f, speedMultiplier));
        _speed = MathF.Min(_speed, _def.IsBoss || _def.IsElite ? 5.6f : 5.2f);
        _attackTimer = _attackCooldown * 0.5f;
        _groundY = GlobalPosition.Y;
        _knockback = Vector3.Zero;
        Velocity = Vector3.Zero;
        ResetBehaviour();
    }
}
