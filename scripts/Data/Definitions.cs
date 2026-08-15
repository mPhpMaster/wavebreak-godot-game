using Godot;
using Wavebreak.Combat;
using Wavebreak.Stats;

namespace Wavebreak.Data;

public enum Rarity { Common, Uncommon, Rare, Epic, Legendary }

/// <summary>How a weapon delivers its damage. The runtime picks a firing
/// strategy from this rather than each weapon being its own class, so a new
/// weapon is usually a data change and not a code change.</summary>
public enum AttackShape
{
    /// <summary>Travelling projectile that expires on hit or lifetime.</summary>
    Projectile,
    /// <summary>Instant cone/arc in front of the aim direction.</summary>
    Melee,
    /// <summary>Persistent damaging field centred on the owner.</summary>
    Aura,
    /// <summary>Projectile that detonates for area damage.</summary>
    Explosive,
    /// <summary>Projectile that seeks the nearest target.</summary>
    Homing,
}

/// <summary>
/// A weapon, as data.
///
/// Godot <see cref="Resource"/> is the ScriptableObject equivalent: these are
/// editable in the inspector, saved as .tres, and hot-reload without a rebuild.
/// Keeping weapons as data is what lets the game ship dozens of them without
/// dozens of classes — and lets a designer tune them without a programmer.
/// </summary>
[GlobalClass]
public partial class WeaponDef : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
    [Export] public Rarity Rarity { get; set; } = Rarity.Common;

    [Export] public AttackShape Shape { get; set; } = AttackShape.Projectile;
    [Export] public Element Element { get; set; } = Element.Physical;

    [Export] public float Damage { get; set; } = 10f;
    [Export] public float Cooldown { get; set; } = 1f;
    [Export] public int Projectiles { get; set; } = 1;
    [Export] public float ProjectileSpeed { get; set; } = 18f;
    [Export] public float Range { get; set; } = 18f;
    [Export] public float Radius { get; set; } = 1f;
    [Export] public int Pierce { get; set; }
    [Export] public float Knockback { get; set; } = 2f;
    [Export] public float CritChance { get; set; }
    [Export] public float ProcCoefficient { get; set; } = 1f;

    /// <summary>Degrees of spread across a burst. 0 fires everything on one line.</summary>
    [Export] public float SpreadDegrees { get; set; } = 12f;

    [Export] public Color Tint { get; set; } = new(1f, 1f, 1f);

    /// <summary>Per-level growth. Applied as (1 + PerLevel * (level - 1)), so
    /// level 1 is always exactly the numbers above.</summary>
    [Export] public float DamagePerLevel { get; set; } = 0.25f;
    [Export] public float CooldownPerLevel { get; set; } = -0.06f;
    [Export] public int ProjectilesEveryNLevels { get; set; } = 3;
    [Export] public int MaxLevel { get; set; } = 8;

    public float DamageAtLevel(int level) => Damage * (1f + DamagePerLevel * (level - 1));
    public float CooldownAtLevel(int level)
        => Mathf.Max(0.05f, Cooldown * (1f + CooldownPerLevel * (level - 1)));
    public int ProjectilesAtLevel(int level)
        => Projectiles + (ProjectilesEveryNLevels > 0 ? (level - 1) / ProjectilesEveryNLevels : 0);
}

[GlobalClass]
public partial class EnemyDef : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";

    [Export] public float Health { get; set; } = 20f;
    [Export] public float Damage { get; set; } = 8f;
    [Export] public float MoveSpeed { get; set; } = 3f;
    [Export] public float AttackRange { get; set; } = 1.6f;
    [Export] public float AttackCooldown { get; set; } = 1f;
    [Export] public float Radius { get; set; } = 0.5f;
    [Export] public float Height { get; set; } = 1.6f;
    [Export] public float KnockbackResist { get; set; }

    [Export] public int XpValue { get; set; } = 1;
    [Export] public int GoldValue { get; set; } = 1;

    /// <summary>What this enemy costs the spawn director. The single most useful
    /// balance knob in the game: it decides how the budget converts into threat,
    /// so one number controls whether a wave is a swarm or a squad.</summary>
    [Export] public int Cost { get; set; } = 1;

    /// <summary>Director will not spawn this before the given run time.</summary>
    [Export] public float UnlockAtSeconds { get; set; }

    [Export] public bool IsElite { get; set; }
    [Export] public bool IsBoss { get; set; }
    [Export] public Color Tint { get; set; } = new(0.8f, 0.3f, 0.3f);
}

/// <summary>Which stat an item touches and by how much. A list of these is an
/// item's entire mechanical content, which is why most items need no code.</summary>
[GlobalClass]
public partial class ItemEffect : Resource
{
    [Export] public Stat Stat { get; set; } = Stat.Damage;
    [Export] public ModifierOp Op { get; set; } = ModifierOp.Additive;
    [Export] public float PerStack { get; set; } = 0.1f;
}

[GlobalClass]
public partial class ItemDef : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
    [Export] public Rarity Rarity { get; set; } = Rarity.Common;

    [Export] public Godot.Collections.Array<ItemEffect> Effects { get; set; } = new();

    /// <summary>0 means unlimited. A cap is how a strong linear item stays
    /// offerable without eventually dominating every run.</summary>
    [Export] public int MaxStacks { get; set; }

    /// <summary>Relative chance of being offered, before luck and rarity weighting.</summary>
    [Export] public float Weight { get; set; } = 1f;

    [Export] public Color Tint { get; set; } = new(0.9f, 0.85f, 0.4f);
}
