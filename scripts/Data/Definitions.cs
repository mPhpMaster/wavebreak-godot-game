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

/// <summary>
/// How an ordinary enemy fights. The enemy runtime implements each of these
/// once, the same way <see cref="AttackShape"/> works for weapons, so a new
/// monster is a row of numbers and not a class.
///
/// Every behaviour that is not <see cref="Chaser"/> still has its contact
/// swing: the behaviour is what it does *besides* walking into the player,
/// which keeps "do not stand in the crowd" true for the whole roster.
/// </summary>
public enum EnemyBehavior
{
    /// <summary>Walks at the player and swings on contact.</summary>
    Chaser,
    /// <summary>Holds <see cref="EnemyDef.AbilityRange"/>, strafes, and fires
    /// slow aimed volleys after a visible wind-up.</summary>
    Shooter,
    /// <summary>Plants, paints a lane on the ground, then dashes down it.</summary>
    Charger,
    /// <summary>Runs in, swells over a ground ring, and blows itself up.</summary>
    Exploder,
    /// <summary>Marks where the player stands, then jumps there and lands with
    /// a blast.</summary>
    Leaper,
}

/// <summary>One verb in a boss's moveset. See <see cref="BossAttack"/>.</summary>
public enum BossMove
{
    /// <summary>Ground disc under the boss, then a blast and shockwave rings
    /// that roll outward along the floor — jump them.</summary>
    Slam,
    /// <summary>A ring of slow projectiles in every direction.</summary>
    RadialBurst,
    /// <summary>Lane telegraph, then a straight-line dash.</summary>
    Charge,
    /// <summary>Calls <see cref="BossAttack.SummonId"/> adds around itself.</summary>
    Summon,
    /// <summary>Several quick aimed fans at the player.</summary>
    Barrage,
    /// <summary>Delayed blast circles dropped on and around the player.</summary>
    MeteorRain,
}

/// <summary>
/// One attack in a boss moveset, as data. A boss is a list of these plus its
/// <see cref="EnemyDef"/> numbers; the runtime picks among them by weight and
/// range, and every one of them is preceded by a telegraph the player can read.
/// </summary>
[GlobalClass]
public partial class BossAttack : Resource
{
    [Export] public BossMove Move { get; set; } = BossMove.Slam;
    [Export] public float Weight { get; set; } = 1f;

    /// <summary>Seconds of telegraph before it lands. Shortened in phase two,
    /// never removed: a faster tell is harder, a missing one is unfair.</summary>
    [Export] public float WindUp { get; set; } = 1f;

    /// <summary>Projectiles per ring/fan, adds per summon, circles per rain,
    /// rings per slam.</summary>
    [Export] public int Count { get; set; } = 1;

    /// <summary>Slam disc radius, meteor radius, or charge length.</summary>
    [Export] public float Radius { get; set; } = 4f;

    /// <summary>Projectile, shockwave or dash speed, m/s.</summary>
    [Export] public float Speed { get; set; } = 9f;

    /// <summary>Multiplies the boss's (difficulty-scaled) contact damage.</summary>
    [Export] public float DamageScale { get; set; } = 1f;

    /// <summary>1 = always available; 2 = only once the boss is below half
    /// health. Phase-two moves are how the fight changes rather than merely
    /// speeding up.</summary>
    [Export] public int MinPhase { get; set; } = 1;

    /// <summary>Only chosen while the player is between these distances, so a
    /// slam is not wasted on a player across the arena.</summary>
    [Export] public float MinRange { get; set; }
    [Export] public float MaxRange { get; set; } = 99f;

    /// <summary>Enemy id for <see cref="BossMove.Summon"/>.</summary>
    [Export] public string SummonId { get; set; } = "";
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

    // ------------------------------------------------------------ behaviour
    // Read only by the behaviour named in Behavior; a Chaser ignores all of it.

    [Export] public EnemyBehavior Behavior { get; set; } = EnemyBehavior.Chaser;

    /// <summary>Shooter: preferred distance. Charger/Leaper: furthest the
    /// player may be for the move to start. Exploder: distance that lights the
    /// fuse.</summary>
    [Export] public float AbilityRange { get; set; }

    /// <summary>Seconds between uses. For a boss: the gap between moves.</summary>
    [Export] public float AbilityCooldown { get; set; } = 3f;

    /// <summary>Telegraph length, seconds.</summary>
    [Export] public float WindUp { get; set; } = 0.6f;

    /// <summary>Ability damage as a multiple of <see cref="Damage"/>.</summary>
    [Export] public float AbilityDamage { get; set; } = 1f;

    /// <summary>Blast radius (Exploder, Leaper) or lane half-width (Charger).</summary>
    [Export] public float AbilityRadius { get; set; } = 2f;

    /// <summary>Projectile speed (Shooter) or dash speed (Charger), m/s.</summary>
    [Export] public float AbilitySpeed { get; set; } = 8f;

    /// <summary>Shots per volley (Shooter).</summary>
    [Export] public int AbilityCount { get; set; } = 1;

    /// <summary>Degrees across a volley (Shooter).</summary>
    [Export] public float AbilitySpread { get; set; }

    /// <summary>On death, spawn <see cref="SplitCount"/> of this enemy id where
    /// it fell. Orthogonal to <see cref="Behavior"/>: anything can split.</summary>
    [Export] public string SplitInto { get; set; } = "";
    [Export] public int SplitCount { get; set; }

    /// <summary>False for things that only exist as a split or a summon: the
    /// director never buys them on its own.</summary>
    [Export] public bool DirectorSpawns { get; set; } = true;

    /// <summary>
    /// Which stage of a run a boss is written for: 1 = first boss, 2 = middle,
    /// 3 = final. 0 for everything that is not a boss. Nothing in the enemy
    /// layer reads it — it is a hint for whatever schedules bosses (the timer
    /// today, a map/phase system later) so that choice can be made from data.
    /// </summary>
    [Export] public int BossTier { get; set; }

    /// <summary>A boss's moveset. Empty means it only chases and swings.</summary>
    [Export] public Godot.Collections.Array<BossAttack> BossMoves { get; set; } = new();
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
