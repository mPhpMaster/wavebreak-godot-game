namespace Wavebreak.Stats;

/// <summary>
/// Every number an item, upgrade or buff is allowed to touch.
///
/// Adding a stat here and giving it a base value in <see cref="StatSheet.BaseValue"/>
/// is the whole cost of adding one — nothing else switches on this enum, because
/// modifiers are stored in a table keyed by it rather than as named fields. That
/// is the difference between "add a stat" being a one-line change and being a
/// change to every class that carries stats.
/// </summary>
public enum Stat
{
    // --- survival
    MaxHealth,
    HealthRegen,        // per minute, not per second — see StatSheet.Shape
    Armor,              // raw points, converted to a % on the way out
    Evasion,            // raw points, converted to a % on the way out
    Shield,
    Thorns,
    Lifesteal,

    // --- offence
    Damage,             // a multiplier: 0 means "x1"
    AttackSpeed,
    CritChance,
    CritDamage,
    AreaSize,
    ProjectileCount,
    ProjectileSpeed,
    Knockback,

    // --- movement
    MoveSpeed,
    JumpHeight,
    DashCooldown,

    // --- economy
    PickupRange,
    XpGain,
    GoldGain,
    Luck,

    // --- level-up choices: charges per run. Read as whole numbers; the
    // level-up controller subtracts what it has spent, so an item granting
    // +1 Rerolls mid-run is one more reroll immediately.
    Rerolls,
    Banishes,
    Skips,

    // --- meta: how fast the world gets harder. Lets an item trade danger for
    // reward, which is where a lot of the interesting build decisions live.
    EnemyScaling,

    // --- appended: lifetime multiplier for lingering weapon effects (orbits,
    // puddles, turrets, vortices) and timed item buffs. 1 = as authored.
    Duration,
}

/// <summary>How a modifier folds into a stat. Order of application is fixed by
/// <see cref="StatSheet"/>, so two items can never disagree about it.</summary>
public enum ModifierOp
{
    /// <summary>+5 max health. Applied first.</summary>
    Flat,

    /// <summary>+25% damage. All additive modifiers sum, then apply once — so ten
    /// +10% items give +100%, not 2.59x. This is what keeps stacking readable.</summary>
    Additive,

    /// <summary>x1.5 damage. These multiply with each other, so they compound.
    /// Reserve for rare effects; handing these out freely is how builds explode.</summary>
    Multiplicative,
}
