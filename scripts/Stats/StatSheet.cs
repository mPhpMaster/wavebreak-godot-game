using System;
using System.Collections.Generic;

namespace Wavebreak.Stats;

/// <summary>A single contribution to a stat, tagged with who supplied it.</summary>
/// <remarks>
/// <paramref name="Source"/> is the important field. Without it, removing an
/// item's effect means remembering the exact value that was added and
/// subtracting it — which drifts the moment anything else recalculates, and
/// silently leaves stat residue behind when an effect ends. With it, removal is
/// "drop everything tagged with this source" and cannot drift.
/// </remarks>
public readonly record struct StatModifier(Stat Stat, ModifierOp Op, float Value, object Source);

/// <summary>
/// The stat pipeline. One place where a number becomes its final value:
///
///     final = Shape( (base + flat) * (1 + sum(additive)) * prod(multiplicative) )
///
/// Everything that wants to change a number registers a <see cref="StatModifier"/>
/// instead of writing to a field. That is what makes items composable: two items
/// touching the same stat never need to know about each other.
///
/// Values are cached and invalidated per stat, so reads are cheap enough to do
/// inside the frame loop and writes stay rare.
/// </summary>
public sealed class StatSheet
{
    private readonly List<StatModifier> _modifiers = new();
    private readonly Dictionary<Stat, float> _cache = new();

    /// <summary>Raised with the stat that changed, so UI and systems can react
    /// without polling every value every frame.</summary>
    public event Action<Stat>? Changed;

    // ---------------------------------------------------------------- base

    /// <summary>
    /// The value with no modifiers at all. These are the starting numbers for a
    /// fresh run, and they are deliberately all in one readable table — balance
    /// changes should never require reading a constructor.
    /// </summary>
    public static float BaseValue(Stat stat) => stat switch
    {
        Stat.MaxHealth       => 100f,
        Stat.HealthRegen     => 6f,     // per minute
        Stat.Armor           => 0f,
        Stat.Evasion         => 0f,
        Stat.Shield          => 0f,
        Stat.Thorns          => 0f,
        Stat.Lifesteal       => 0f,

        Stat.Damage          => 0f,     // read as a bonus: 0 => x1
        Stat.AttackSpeed     => 1f,
        Stat.CritChance      => 0.05f,
        Stat.CritDamage      => 1f,     // +100% on crit
        Stat.AreaSize        => 1f,
        Stat.ProjectileCount => 0f,     // a bonus on top of each weapon's own count
        Stat.ProjectileSpeed => 1f,
        Stat.Knockback       => 1f,

        Stat.MoveSpeed       => 6f,     // metres/second
        Stat.JumpHeight      => 3.5f,   // lowered from 6: height is now earned (Tome of Updrafts)
        Stat.DashCooldown    => 2.5f,   // seconds

        Stat.PickupRange     => 4f,
        Stat.XpGain          => 0f,
        Stat.GoldGain        => 0f,
        Stat.Luck            => 0f,

        Stat.Rerolls         => 2f,     // charges per run, see LevelUpChoices
        Stat.Banishes        => 1f,
        Stat.Skips           => 3f,

        Stat.EnemyScaling    => 1f,

        Stat.ElementalDamage => 0f,     // read as a bonus: 0 => x1
        Stat.StatusDuration  => 1f,
        Stat.Duration        => 1f,
        _ => 0f,
    };

    // ----------------------------------------------------------- modifiers

    public void Add(StatModifier mod)
    {
        _modifiers.Add(mod);
        Invalidate(mod.Stat);
    }

    public void Add(Stat stat, ModifierOp op, float value, object source)
        => Add(new StatModifier(stat, op, value, source));

    /// <summary>Drop every modifier a source contributed. Idempotent.</summary>
    public void RemoveAllFrom(object source)
    {
        for (int i = _modifiers.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_modifiers[i].Source, source))
            {
                Stat stat = _modifiers[i].Stat;
                _modifiers.RemoveAt(i);
                Invalidate(stat);
            }
        }
    }

    public void Clear()
    {
        _modifiers.Clear();
        _cache.Clear();
    }

    private void Invalidate(Stat stat)
    {
        _cache.Remove(stat);
        Changed?.Invoke(stat);
    }

    // --------------------------------------------------------------- reads

    public float Get(Stat stat)
    {
        if (_cache.TryGetValue(stat, out float cached))
            return cached;

        float flat = 0f, additive = 0f, multiplicative = 1f;
        foreach (StatModifier m in _modifiers)
        {
            if (m.Stat != stat) continue;
            switch (m.Op)
            {
                case ModifierOp.Flat:           flat += m.Value; break;
                case ModifierOp.Additive:       additive += m.Value; break;
                case ModifierOp.Multiplicative: multiplicative *= m.Value; break;
            }
        }

        // Stacked penalties stop at -75%. Items without a stack cap each take a
        // slice off (max health -10..-25% apiece); summed without a floor, a few
        // copies reached -100% and left a level-60 build on 1 max health. The
        // trade-off items still bite, they just cannot erase the stat.
        if (!IsBonusStat(stat)) additive = Math.Max(additive, -MaxPenalty);

        float raw = IsBonusStat(stat)
            // A bonus stat is read as the multiplier 1 + value, with base 0. Run
            // through the general formula, (0 + flat) * (1 + additive) is still 0,
            // so every "+10% Damage" and "x1.2 Damage" did nothing at all. For these
            // stats percentages add onto the bonus and multipliers scale the whole
            // multiplier: two +10% items give x1.20, and x1.2 on top gives x1.44.
            ? (1f + BaseValue(stat) + flat + additive) * multiplicative - 1f
            : (BaseValue(stat) + flat) * (1f + additive) * multiplicative;
        float value = Shape(stat, raw);
        _cache[stat] = value;
        return value;
    }

    /// <summary>Floor on summed additive penalties for regular stats.</summary>
    public const float MaxPenalty = 0.75f;

    /// <summary>Stats stored as a bonus over x1 and read through
    /// <see cref="GetMultiplier"/>.</summary>
    /// <summary>Asymptote of the XP and gold bonus: at most x3 income.</summary>
    public const float IncomeCap = 2f;

    public static bool IsBonusStat(Stat stat) => stat is Stat.Damage or Stat.XpGain or Stat.GoldGain or Stat.ElementalDamage;

    /// <summary>Damage multiplier form of a "bonus" stat: 0 reads as x1.</summary>
    public float GetMultiplier(Stat stat) => 1f + Get(stat);

    /// <summary>
    /// Per-stat clamps applied after the arithmetic. Kept here rather than at
    /// each call site so a stat cannot be read unclamped by accident — that is
    /// the bug that produces a negative max health three systems away from
    /// whatever caused it.
    /// </summary>
    private static float Shape(Stat stat, float v) => stat switch
    {
        Stat.MaxHealth       => Math.Max(1f, v),
        Stat.AttackSpeed     => Math.Max(0.01f, v),
        Stat.MoveSpeed       => Math.Max(0f, v),
        Stat.ProjectileSpeed => Math.Max(0.01f, v),
        Stat.AreaSize        => Math.Max(0.05f, v),
        Stat.DashCooldown    => Math.Max(0.05f, v),
        Stat.CritChance      => Math.Clamp(v, 0f, 1f),
        Stat.Armor           => Math.Max(0f, v),
        Stat.Evasion         => Math.Max(0f, v),
        Stat.Rerolls or Stat.Banishes or Stat.Skips => Math.Max(0f, v),
        Stat.StatusChance or Stat.MegacritChance or Stat.IgniteChance or Stat.ChillChance
            or Stat.ShockChance or Stat.PoisonChance => Math.Clamp(v, 0f, 1f),
        Stat.StatusDuration  => Math.Max(0.1f, v),
        Stat.FreezePower or Stat.UpgradeStats => Math.Max(0f, v),
        // Diminishing returns on income. Once +% items actually applied, stacking
        // them had no ceiling: a 15-minute run ended at level 103 with 193k gold
        // and 200 items. v / (1 + v/2): +50% -> +40%, +100% -> +67%,
        // +300% -> +120%, never past +200%. Penalties pass through unchanged.
        Stat.XpGain or Stat.GoldGain => v > 0f ? v / (1f + v / IncomeCap) : Math.Max(-0.9f, v),
        _ => v,
    };

    // -------------------------------------------------------- derived math

    /// <summary>
    /// Armour points to a damage-reduction fraction, with diminishing returns so
    /// stacking never reaches immunity.
    ///
    ///     reduction = armor / (armor + K)
    ///
    /// K is the "half point": at armor == K you take half damage. Tuning one
    /// constant moves the whole curve, which is much easier to balance than a
    /// piecewise table. Capped below 1 so there is always a way to die.
    /// </summary>
    public const float ArmorHalfPoint = 30f;
    public const float MaxReduction = 0.9f;

    public float ArmorReduction()
    {
        float a = Get(Stat.Armor);
        return a <= 0f ? 0f : Math.Min(a / (a + ArmorHalfPoint), MaxReduction);
    }

    public const float EvasionHalfPoint = 40f;
    public const float MaxEvasion = 0.6f;

    public float EvasionChance()
    {
        float e = Get(Stat.Evasion);
        return e <= 0f ? 0f : Math.Min(e / (e + EvasionHalfPoint), MaxEvasion);
    }

    /// <summary>Health regenerated this frame. The stat is per minute because
    /// that is the readable unit on an item tooltip; the conversion lives here
    /// so no caller has to remember it.</summary>
    public float RegenPerSecond() => Get(Stat.HealthRegen) / 60f;

    /// <summary>Seconds between shots for a weapon with the given base cooldown.</summary>
    public float AttackInterval(float baseCooldown)
        => Math.Max(0.02f, baseCooldown / Get(Stat.AttackSpeed));

    // ----------------------------------------------------------- debugging

    public IReadOnlyList<StatModifier> Modifiers => _modifiers;

    public string Describe(Stat stat)
    {
        float flat = 0f, additive = 0f, multiplicative = 1f;
        foreach (StatModifier m in _modifiers)
        {
            if (m.Stat != stat) continue;
            switch (m.Op)
            {
                case ModifierOp.Flat:           flat += m.Value; break;
                case ModifierOp.Additive:       additive += m.Value; break;
                case ModifierOp.Multiplicative: multiplicative *= m.Value; break;
            }
        }
        return $"{stat}: ({BaseValue(stat)} + {flat}) * {1f + additive:0.##} * "
             + $"{multiplicative:0.##} = {Get(stat):0.##}";
    }
}
