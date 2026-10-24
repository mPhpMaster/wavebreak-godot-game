using Godot;

namespace Wavebreak.Actors;

/// <summary>
/// The few health hooks perks need (Combat/Weapons/PerkProcs): a shield top-up
/// for Bubble Net, quiet healing for Red Tide and Lifeline (the same batched
/// trickle as regeneration and lifesteal, so a heal on every crit does not
/// draw a number every frame), and a test-only way to set health for the
/// perk self-check.
/// </summary>
public sealed partial class PlayerHealth
{
    /// <summary>Add <paramref name="amount"/> shield now, up to the current maximum.</summary>
    internal void PerkShield(float amount)
    {
        if (_dead || amount <= 0f) return;
        Shield = Mathf.Min(ShieldMax, Shield + amount);
    }

    /// <summary>A temporary maximum went away: keep the shield inside the new one.</summary>
    internal void ClampShield()
    {
        if (Shield > ShieldMax) Shield = Mathf.Max(0f, ShieldMax);
    }

    /// <summary>Heal without a popup (batched into PlayerHealed like regeneration).</summary>
    internal void PerkTrickle(float amount) => ApplyTrickle(amount);

    /// <summary>--check-perks only: set health to a fraction of max.</summary>
    internal void CheckSetFraction(float fraction)
    {
        if (_dead) return;
        Current = Mathf.Clamp(Max * fraction, 1f, Max);
    }
}
