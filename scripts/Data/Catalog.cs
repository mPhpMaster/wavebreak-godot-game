using System.Collections.Generic;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Stats;

namespace Wavebreak.Data;

/// <summary>
/// Every piece of content in the game, built in code.
///
/// The obvious alternative is a folder of <c>.tres</c> files. That is the right
/// answer once a designer is tuning the game daily, and <see cref="WeaponDef"/>
/// and friends are <see cref="Resource"/>s precisely so that switch costs
/// nothing later. It is the wrong answer right now: balance is a set of
/// *relationships* — this common has to be worth taking against that one, this
/// enemy's cost has to sit where the director's threat curve will find it — and
/// those relationships are invisible when each number lives in its own binary
/// file. One reviewable file makes the whole balance table diffable.
///
/// Read the comments as the balance record. Where a number was chosen against
/// another number, the comment says which.
/// </summary>
public static partial class Catalog
{
    // ------------------------------------------------------------------ access

    /// <summary>Every weapon. Index 0 is the starting weapon — see
    /// <c>Game.Main.BuildWorld</c> — so it must be the one that teaches the game.</summary>
    public static IReadOnlyList<WeaponDef> Weapons => WeaponList;

    /// <summary>Every enemy, in no particular order; the director sorts by
    /// <see cref="EnemyDef.Cost"/> and <see cref="EnemyDef.UnlockAtSeconds"/>.</summary>
    public static IReadOnlyList<EnemyDef> Enemies => EnemyList;

    /// <summary>Every passive item the offer pool can roll.</summary>
    public static IReadOnlyList<ItemDef> Items => ItemList;

    /// <summary>Weapon by id, or null. Used by UI that only carries an id from an
    /// event and should never hold a reference to the runtime that owns it.
    /// Falls back to the unlockable content in Meta.MetaContent, so earned
    /// weapons and items are named like any other.</summary>
    public static WeaponDef? WeaponById(string? id)
        => id is not null && WeaponsById.TryGetValue(id, out WeaponDef? def) ? def : Meta.MetaContent.WeaponById(id);

    /// <summary>Item by id, or null.</summary>
    public static ItemDef? ItemById(string? id)
        => id is not null && ItemsById.TryGetValue(id, out ItemDef? def) ? def : Meta.MetaContent.ItemById(id);

    /// <summary>Enemy by id, or null.</summary>
    public static EnemyDef? EnemyById(string? id)
        => id is not null && EnemiesById.TryGetValue(id, out EnemyDef? def) ? def : null;

    // -------------------------------------------------------------- xp curve

    /// <summary>
    /// Experience needed to go from <paramref name="level"/> to the next one.
    ///
    ///     cost(n) = 5 + 5(n-1) + (n-1)²
    ///
    /// Linear plus quadratic rather than exponential. Exponential curves force a
    /// choice between "levels stop arriving" and "xp values must inflate to
    /// match", and inflating xp values is how a fifteen-minute run ends up
    /// showing seven-digit numbers over every corpse. This shape gives roughly a
    /// level every eight seconds early, every forty by minute twelve, and lands a
    /// clean run somewhere near level 40 — which is enough level-ups for a build
    /// to be a build, and few enough that each one is still a decision.
    ///
    /// This is the run's XP curve, kept with the rest of the balance table. The
    /// HUD's bar reads it, so anything that grants levels should too.
    /// </summary>
    public static int XpForLevel(int level)
    {
        int n = level < 1 ? 0 : level - 1;
        // Was 5 + 5n + n^2, which put level 10 at 429 cumulative xp -- around
        // nine minutes of killing at the observed rate. The build never came
        // together before the crowd did, so runs ended at level 4 with one
        // weapon regardless of how well they were played.
        //
        // 5 + 4n + 0.6n^2 keeps the quadratic shape (so late levels still cost
        // real time) while roughly halving the early cost: level 10 lands near
        // three minutes, which is when the second and third weapons need to
        // exist for the difficulty ramp to be survivable at all.
        return 5 + 4 * n + (int)(0.6f * n * n);
    }

    // ---------------------------------------------------------------- rarity

    /// <summary>
    /// The colour a rarity is drawn in. One definition, because a rarity that
    /// looks different on the level-up card than it does in the results list is
    /// read as two different things.
    ///
    /// Chosen for contrast against the dark UI panels and for separation from
    /// each other under the game's heavy orange/red damage flashes — which is why
    /// Legendary is amber rather than the more usual red.
    /// </summary>
    public static Color RarityColor(Rarity rarity) => rarity switch
    {
        Rarity.Common    => new Color(0.78f, 0.82f, 0.88f),
        Rarity.Uncommon  => new Color(0.36f, 0.85f, 0.47f),
        Rarity.Rare      => new Color(0.31f, 0.66f, 1.00f),
        Rarity.Epic      => new Color(0.70f, 0.42f, 1.00f),
        Rarity.Legendary => new Color(1.00f, 0.70f, 0.24f),
        _                => Colors.White,
    };

    /// <summary>Display name of a rarity, for card headers.</summary>
    public static string RarityName(Rarity rarity) => rarity switch
    {
        Rarity.Common    => "COMMON",
        Rarity.Uncommon  => "UNCOMMON",
        Rarity.Rare      => "RARE",
        Rarity.Epic      => "EPIC",
        Rarity.Legendary => "LEGENDARY",
        _                => "",
    };

    // ------------------------------------------------------------ vocabulary

    /// <summary>
    /// What a stat is called in front of the player.
    ///
    /// Kept beside the content rather than in the UI because it is naming, not
    /// layout: the enum member is what a programmer types and this is what the
    /// game promises it does, and those two drifting apart is a content bug that
    /// only ever surfaces on a card nobody proof-read.
    /// </summary>
    public static string StatName(Stat stat) => stat switch
    {
        Stat.MaxHealth       => "Max Health",
        Stat.HealthRegen     => "Regeneration",
        Stat.Armor           => "Armour",
        Stat.Evasion         => "Evasion",
        Stat.Shield          => "Shield",
        Stat.Thorns          => "Thorns",
        Stat.Lifesteal       => "Lifesteal",
        Stat.Damage          => "Damage",
        Stat.AttackSpeed     => "Attack Speed",
        Stat.CritChance      => "Crit Chance",
        Stat.CritDamage      => "Crit Damage",
        Stat.AreaSize        => "Area",
        Stat.ProjectileCount => "Projectiles",
        Stat.ProjectileSpeed => "Projectile Speed",
        Stat.Knockback       => "Knockback",
        Stat.MoveSpeed       => "Move Speed",
        Stat.JumpHeight      => "Jump Height",
        Stat.DashCooldown    => "Dash Cooldown",
        Stat.PickupRange     => "Pickup Range",
        Stat.XpGain          => "Experience",
        Stat.GoldGain        => "Gold",
        Stat.Luck            => "Luck",
        Stat.EnemyScaling    => "Enemy Strength",
        Stat.ElementalDamage => "Elemental Damage",
        Stat.StatusChance    => "Status Chance",
        Stat.StatusDuration  => "Status Duration",
        Stat.MegacritChance  => "Megacrit Chance",
        Stat.IgniteChance    => "Ignite Chance",
        Stat.ChillChance     => "Chill Chance",
        Stat.ShockChance     => "Shock Chance",
        Stat.PoisonChance    => "Poison Chance",
        Stat.FreezePower     => "Freeze Power",
        Stat.UpgradeStats    => "Upgrade Stat Lines",
        _                    => stat.ToString(),
    };

    /// <summary>
    /// Stats whose raw value is already a fraction, so a <see cref="ModifierOp.Flat"/>
    /// modifier on them reads as percentage points rather than as a count.
    ///
    /// Getting this wrong is a 100x lie on a card — "+0.06 Crit Chance" and
    /// "+6 Crit Chance" are both wrong in ways a player will believe.
    /// </summary>
    public static bool IsFractionStat(Stat stat)
        => stat is Stat.CritChance or Stat.CritDamage or Stat.Lifesteal
            || IsElementFractionStat(stat);

    /// <summary>One line of plain English for an item effect, e.g. "+10% Damage",
    /// "+6 Armour", "x1.20 Damage", "-12% Move Speed".</summary>
    public static string DescribeEffect(ItemEffect effect)
    {
        // Arabic: the stat word leads ("الضرر +10%") and the amount follows it,
        // which is how the line reads right to left. English is unchanged.
        if (Meta.Loc.IsArabic) return DescribeEffectArabic(effect);
        string name = StatName(effect.Stat);
        float v = effect.PerStack;

        switch (effect.Op)
        {
            case ModifierOp.Multiplicative:
                return $"x{v:0.##} {name}";

            case ModifierOp.Additive:
                return $"{Signed(v * 100f, "0.#")}% {name}";

            default:
                if (IsFractionStat(effect.Stat))
                    return $"{Signed(v * 100f, "0.#")}% {name}";
                if (effect.Stat == Stat.HealthRegen)
                    return $"{Signed(v, "0.#")} {name} /min";
                return $"{Signed(v, "0.##")} {name}";
        }
    }

    private static string DescribeEffectArabic(ItemEffect effect)
    {
        string name = Meta.Loc.T(StatName(effect.Stat));
        float v = effect.PerStack;
        string amount = effect.Op switch
        {
            ModifierOp.Multiplicative => $"×{v:0.##}",
            ModifierOp.Additive => $"{Signed(v * 100f, "0.#")}%",
            _ when IsFractionStat(effect.Stat) => $"{Signed(v * 100f, "0.#")}%",
            _ when effect.Stat == Stat.HealthRegen => $"{Signed(v, "0.#")}{Meta.Loc.T("/min")}",
            _ => Signed(v, "0.##"),
        };
        return $"{name} {Meta.Loc.Ltr(amount)}";
    }


    private static string Signed(float value, string format)
        => value >= 0f ? "+" + value.ToString(format) : value.ToString(format);

    // ------------------------------------------------------------- the tables

    private static readonly List<WeaponDef> WeaponList = BuildWeapons();
    private static readonly List<EnemyDef> EnemyList = WithExpansionEnemies(BuildEnemies());
    private static readonly List<ItemDef> ItemList = WithElementItems(BuildItems());

    private static readonly Dictionary<string, WeaponDef> WeaponsById = Index(WeaponList, static w => w.Id);
    private static readonly Dictionary<string, EnemyDef> EnemiesById = Index(EnemyList, static e => e.Id);
    private static readonly Dictionary<string, ItemDef> ItemsById = Index(ItemList, static i => i.Id);

    private static Dictionary<string, T> Index<T>(List<T> source, System.Func<T, string> key)
    {
        var map = new Dictionary<string, T>(source.Count);
        foreach (T entry in source)
        {
            string id = key(entry);
            if (id.Length > 0) map[id] = entry;
        }
        return map;
    }

    // =====================================================================
    //  WEAPONS
    // =====================================================================
    //
    //  The band every weapon is balanced into is single-target DPS at level 1:
    //
    //      dps = Damage * Projectiles / Cooldown
    //
    //  Everything sits between 15 and 18 except the Epic, which is allowed 19,
    //  and the Legendary, which is allowed 21. That deliberately narrow band is the point — weapons are meant to be
    //  chosen for *shape*, not for a bigger number, and a weapon that is simply
    //  better makes the level-up screen a formality. What separates them is how
    //  much of that DPS survives contact with reality: a spread shot loses most
    //  of it at range, an aura applies all of it to a crowd and little of it to a
    //  boss, a homing shot never misses and is priced for it.
    //
    //  Scaling is the second axis. Weapons that already hit many targets get no
    //  extra projectiles per level (ProjectilesEveryNLevels = 0) and grow only in
    //  damage; single-target weapons get extra projectiles, which is what keeps a
    //  level-8 starter relevant against a level-8 aura.
    //
    //  Element decides the status a weapon applies (Combat/StatusEffects: Fire
    //  burns, Ice chills then freezes, Lightning shocks, Poison stacks) and is
    //  scaled by Elemental Damage. Physical weapons apply nothing on their own.

    private static List<WeaponDef> BuildWeapons() => new()
    {
        // ---- 1. The starter. Everything else is balanced against this line.
        // Plain, accurate, forgiving cooldown, and one pierce so a corridor of
        // trash is not a wall. If a new player cannot clear minute one with this
        // alone, the whole early curve is wrong.
        new WeaponDef
        {
            Id = "splinter_rail",
            DisplayName = "Splinter Rail",
            Description = "A shard of rail glass, thrown flat and fast. Punches through one body before it breaks.",
            Rarity = Rarity.Common,
            Shape = AttackShape.Projectile,
            Element = Element.Physical,
            Damage = 12f,
            Cooldown = 0.75f,       // 16.0 dps
            Projectiles = 1,
            ProjectileSpeed = 26f,
            Range = 20f,
            Radius = 0.3f,
            Pierce = 1,
            Knockback = 2.4f,
            CritChance = 0.03f,
            ProcCoefficient = 1f,
            SpreadDegrees = 4f,
            Tint = new Color(0.72f, 0.90f, 1.00f),
            DamagePerLevel = 0.26f,
            CooldownPerLevel = -0.06f,
            ProjectilesEveryNLevels = 3,
            MaxLevel = 8,
        },

        // ---- 2. Crowd-clearing spread. Same DPS on paper, a third of it at
        // range, well over double at point blank. The weapon that teaches the
        // player that standing in the swarm is a choice and not a mistake.
        new WeaponDef
        {
            Id = "sparkfan",
            DisplayName = "Fan of Sparks",
            Description = "Three arcs at once, wide enough to be careless with. Terrible at distance, unfair up close.",
            Rarity = Rarity.Common,
            Shape = AttackShape.Projectile,
            Element = Element.Lightning,
            Damage = 6f,
            Cooldown = 1.1f,        // 16.4 dps across 3 shots
            Projectiles = 3,
            ProjectileSpeed = 21f,
            Range = 13f,
            Radius = 0.28f,
            Pierce = 0,
            Knockback = 1.4f,
            CritChance = 0.04f,
            ProcCoefficient = 0.7f, // three hits per swing; priced down so it does not out-proc everything
            SpreadDegrees = 34f,
            Tint = new Color(1.00f, 0.92f, 0.42f),
            DamagePerLevel = 0.22f,
            CooldownPerLevel = -0.05f,
            ProjectilesEveryNLevels = 3,
            MaxLevel = 8,
        },

        // ---- 3. Wide melee. Hits everything in the cone for full damage, so its
        // real output is (dps x bodies) and it out-scales every projectile in a
        // crowd. Priced with the shortest reach in the game: you have to be in
        // the thing you are killing.
        new WeaponDef
        {
            Id = "cleaver_arc",
            DisplayName = "Cleaver Arc",
            Description = "A wide, heavy sweep. Everything in front of you pays the same price.",
            Rarity = Rarity.Uncommon,
            Shape = AttackShape.Melee,
            Element = Element.Physical,
            Damage = 22f,
            Cooldown = 1.25f,       // 17.6 dps per target, and it rarely hits one
            Projectiles = 1,
            ProjectileSpeed = 0f,
            Range = 4.2f,
            Radius = 1.0f,
            Pierce = 0,
            Knockback = 5.5f,       // the knockback is the defence: it buys the space back
            CritChance = 0.06f,
            ProcCoefficient = 1.15f,
            SpreadDegrees = 62f,    // cone half-angle
            Tint = new Color(1.00f, 0.55f, 0.35f),
            DamagePerLevel = 0.28f,
            CooldownPerLevel = -0.05f,
            ProjectilesEveryNLevels = 0,   // already area; growth is all damage
            MaxLevel = 8,
        },

        // ---- 4. Fast narrow melee. The anti-elite melee option: a tight cone
        // means it commits its whole output to one target, and the fastest swing
        // rate in the game makes it the best carrier for lifesteal and on-hit.
        new WeaponDef
        {
            Id = "riptide_blades",
            DisplayName = "Riptide Blades",
            Description = "Twin cold edges worked in short, fast strokes. Narrow, relentless, and very hungry.",
            Rarity = Rarity.Uncommon,
            Shape = AttackShape.Melee,
            Element = Element.Ice,
            Damage = 8f,
            Cooldown = 0.45f,       // 17.8 dps
            Projectiles = 1,
            ProjectileSpeed = 0f,
            Range = 3.4f,
            Radius = 0.8f,
            Pierce = 0,
            Knockback = 1.2f,
            CritChance = 0.10f,     // the highest innate crit: it is the crit-build weapon
            ProcCoefficient = 0.55f,
            SpreadDegrees = 30f,
            Tint = new Color(0.55f, 0.92f, 1.00f),
            DamagePerLevel = 0.24f,
            CooldownPerLevel = -0.055f,
            ProjectilesEveryNLevels = 0,
            MaxLevel = 8,
        },

        // ---- 5. Close aura. Lowest single-target output of anything on this
        // list and that is the trade: it needs no aiming, no cooldown discipline
        // and no line of sight, and it is the only weapon that keeps working
        // while the player is doing nothing but running away.
        new WeaponDef
        {
            Id = "ember_halo",
            DisplayName = "Ember Halo",
            Description = "A ring of slow fire that will not go out. Standing near you is the mistake.",
            Rarity = Rarity.Uncommon,
            Shape = AttackShape.Aura,
            Element = Element.Fire,
            Damage = 7f,
            Cooldown = 0.6f,        // 11.7 dps per body — low on purpose, it hits all of them
            Projectiles = 1,
            ProjectileSpeed = 0f,
            Range = 3.6f,
            Radius = 3.6f,
            Pierce = 0,
            Knockback = 0.4f,
            CritChance = 0.02f,
            ProcCoefficient = 0.4f, // ticks constantly; a full coefficient would trivialise procs
            SpreadDegrees = 0f,
            Tint = new Color(1.00f, 0.46f, 0.16f),
            DamagePerLevel = 0.30f,
            CooldownPerLevel = -0.05f,
            ProjectilesEveryNLevels = 0,
            MaxLevel = 8,
        },

        // ---- 6. Wide aura. Half the tick rate of the Halo and twice the reach,
        // with real knockback — this is a control tool that happens to do damage,
        // and the pairing to take when the run is drowning rather than starving.
        new WeaponDef
        {
            Id = "static_field",
            DisplayName = "Static Field",
            Description = "A slow, wide pulse. It shoves as much as it burns, which is usually what you needed.",
            Rarity = Rarity.Rare,
            Shape = AttackShape.Aura,
            Element = Element.Lightning,
            Damage = 17f,
            Cooldown = 1.6f,        // 10.6 dps per body, over a much bigger circle
            Projectiles = 1,
            ProjectileSpeed = 0f,
            Range = 6.4f,
            Radius = 6.4f,
            Pierce = 0,
            Knockback = 6.5f,
            CritChance = 0.03f,
            ProcCoefficient = 0.9f,
            SpreadDegrees = 0f,
            Tint = new Color(0.62f, 0.72f, 1.00f),
            DamagePerLevel = 0.30f,
            CooldownPerLevel = -0.06f,
            ProjectilesEveryNLevels = 0,
            MaxLevel = 8,
        },

        // ---- 7. Heavy explosive. The biggest single number in the early game,
        // on the longest cooldown, with damage falling to half at the rim. Rewards
        // leading a pack; punishes panic-firing into the one enemy on your heel.
        new WeaponDef
        {
            Id = "mortar_shell",
            DisplayName = "Mortar Shell",
            Description = "Lobbed, slow, and enormously final. Aim where they will be, not where they are.",
            Rarity = Rarity.Rare,
            Shape = AttackShape.Explosive,
            Element = Element.Fire,
            Damage = 34f,
            Cooldown = 2.2f,        // 15.5 dps single-target, far more into a pack
            Projectiles = 1,
            ProjectileSpeed = 15f,
            Range = 18f,
            Radius = 3.4f,          // blast radius; AreaSize items multiply this
            Pierce = 0,
            Knockback = 7f,
            CritChance = 0.05f,
            ProcCoefficient = 1.5f, // slow and heavy, so it procs generously
            SpreadDegrees = 6f,
            Tint = new Color(1.00f, 0.66f, 0.20f),
            DamagePerLevel = 0.32f,
            CooldownPerLevel = -0.055f,
            ProjectilesEveryNLevels = 4,   // a second shell is a big step; make it rare
            MaxLevel = 8,
        },

        // ---- 8. Light explosive. Two small bursts instead of one big one — the
        // same idea as the Mortar with the variance taken out, which makes it the
        // safer pick and therefore the cheaper rarity.
        new WeaponDef
        {
            Id = "bloomcap_spores",
            DisplayName = "Bloomcap Spores",
            Description = "Fat green pods that split on contact. Not impressive alone. Rarely alone.",
            Rarity = Rarity.Uncommon,
            Shape = AttackShape.Explosive,
            Element = Element.Poison,
            Damage = 9f,
            Cooldown = 1.0f,        // 18.0 dps across two pods
            Projectiles = 2,
            ProjectileSpeed = 17f,
            Range = 14f,
            Radius = 1.9f,
            Pierce = 0,
            Knockback = 1.8f,
            CritChance = 0.03f,
            ProcCoefficient = 0.75f,
            SpreadDegrees = 18f,
            Tint = new Color(0.55f, 0.95f, 0.35f),
            DamagePerLevel = 0.24f,
            CooldownPerLevel = -0.05f,
            ProjectilesEveryNLevels = 3,
            MaxLevel = 8,
        },

        // ---- 9. Cheap homing. Never misses, so its per-shot damage is the
        // lowest of any projectile on the list. The weapon you take when your
        // build has stopped being able to aim — which, past minute eight, it has.
        new WeaponDef
        {
            Id = "wisp_seeker",
            DisplayName = "Wisp Seeker",
            Description = "Two pale lights that pick their own target and are patient about reaching it.",
            Rarity = Rarity.Rare,
            Shape = AttackShape.Homing,
            Element = Element.Ice,
            Damage = 8f,
            Cooldown = 1.0f,        // 16.0 dps, and effectively all of it lands
            Projectiles = 2,
            ProjectileSpeed = 14f,  // slow: a fast seeker is unreadable and unmissable
            Range = 26f,
            Radius = 0.32f,
            Pierce = 0,
            Knockback = 0.8f,
            CritChance = 0.05f,
            ProcCoefficient = 0.65f,
            SpreadDegrees = 40f,    // they leave in different directions and curve back
            Tint = new Color(0.72f, 0.86f, 1.00f),
            DamagePerLevel = 0.25f,
            CooldownPerLevel = -0.06f,
            ProjectilesEveryNLevels = 3,
            MaxLevel = 8,
        },

        // ---- 10. The Legendary. Allowed to break the DPS band — 21 against
        // everyone else's 16 — because a Legendary that is merely sideways is a
        // disappointment, and because the whole of that number is spent on a
        // single two-and-a-half second commitment. Pierce 4 makes it a line
        // weapon: it wants a queue, and the queue is exactly what the late game
        // provides.
        new WeaponDef
        {
            Id = "judgement_lance",
            DisplayName = "Judgement Lance",
            Description = "It selects one target, and then it selects everything standing behind that target.",
            Rarity = Rarity.Legendary,
            Shape = AttackShape.Homing,
            Element = Element.Lightning,
            Damage = 55f,
            Cooldown = 2.6f,        // 21.2 dps before pierce, far more with it
            Projectiles = 1,
            ProjectileSpeed = 30f,
            Range = 34f,
            Radius = 0.55f,
            Pierce = 4,
            Knockback = 9f,
            CritChance = 0.12f,
            ProcCoefficient = 2f,   // one enormous hit; it is meant to set off everything
            SpreadDegrees = 0f,
            Tint = new Color(0.95f, 0.85f, 1.00f),
            DamagePerLevel = 0.34f,
            CooldownPerLevel = -0.05f,
            ProjectilesEveryNLevels = 4,
            MaxLevel = 8,
        },

        // ================================================= second wave of weapons
        //
        // Same 15-18 dps band, same rule that shape is what is being chosen. Each
        // of these exists to fill a role the first ten leave open: a line-piercer
        // that is not a Legendary, a crit carrier at range, a damage aura that
        // does not push, a slow melee that is all commitment, a scatter
        // explosive, a homing swarm, a sniper, and an Epic so the weapon table
        // has every rarity in it.

        // ---- 11. Heavy line-piercer. Slow, fat, and it does not stop at the
        // first body: pierce 6 means its real output is (dps x queue length), the
        // same bet the Lance makes, at a common price and without the homing.
        // The low speed is the cost — it will be dodged by anything that moves
        // sideways, which in practice is Runners.
        new WeaponDef
        {
            Id = "brinespike",
            DisplayName = "Brinespike",
            Description = "A barbed spike of crusted salt, thrown hard. It goes through the first of them and keeps its mind on the rest.",
            Rarity = Rarity.Uncommon,
            Shape = AttackShape.Projectile,
            Element = Element.Poison,
            Damage = 29f,
            Cooldown = 1.8f,        // 16.1 dps per body in the line
            Projectiles = 1,
            ProjectileSpeed = 15f,  // slow on purpose: the price of pierce 6
            Range = 22f,
            Radius = 0.45f,
            Pierce = 6,
            Knockback = 3.5f,
            CritChance = 0.04f,
            ProcCoefficient = 1.3f,
            SpreadDegrees = 0f,
            Tint = new Color(0.60f, 0.90f, 0.62f),
            DamagePerLevel = 0.28f,
            CooldownPerLevel = -0.05f,
            ProjectilesEveryNLevels = 4,   // a second spike is a whole second line
            MaxLevel = 8,
        },

        // ---- 12. The crit carrier. Four needles at 4 damage is a pitiful number
        // on its own; 22% innate crit is the highest in the game and it is rolled
        // per needle, so every crit item is worth roughly twice as much here as
        // on anything else. Proc coefficient is the lowest on the list for the
        // same reason as the Fan's: four hits a volley must not out-proc a Mortar.
        new WeaponDef
        {
            Id = "glasswire_needles",
            DisplayName = "Glasswire Needles",
            Description = "A fistful of drawn-glass needles. Most of them only sting. Some of them find the seam.",
            Rarity = Rarity.Common,
            Shape = AttackShape.Projectile,
            Element = Element.Physical,
            Damage = 4f,
            Cooldown = 1.0f,        // 16.0 dps across four needles
            Projectiles = 4,
            ProjectileSpeed = 32f,
            Range = 16f,
            Radius = 0.22f,
            Pierce = 0,
            Knockback = 0.5f,
            CritChance = 0.22f,     // highest innate crit: it is the crit-build weapon at range
            ProcCoefficient = 0.35f,
            SpreadDegrees = 10f,    // tight: a needle group, not a fan
            Tint = new Color(0.86f, 0.96f, 1.00f),
            DamagePerLevel = 0.22f,
            CooldownPerLevel = -0.05f,
            ProjectilesEveryNLevels = 2,   // 4 -> 7 by level 8: x1.75, under the rail's x3
            MaxLevel = 8,
        },

        // ---- 13. Tight damage aura. The Halo's opposite: a third less reach and
        // close to the full DPS band per body, with no knockback at all — it
        // wants them to stay. That makes it the aura for a stand-and-take-it
        // build (armour, thorns, regen) rather than for a kiting one, and the
        // only aura where AreaSize items are a real damage increase.
        new WeaponDef
        {
            Id = "rimeheart",
            DisplayName = "Rimeheart",
            Description = "A close ring of cutting cold. It does not push anything away. It would rather they stayed.",
            Rarity = Rarity.Uncommon,
            Shape = AttackShape.Aura,
            Element = Element.Ice,
            Damage = 13f,
            Cooldown = 0.85f,       // 15.3 dps per body, inside a 2.3m ring
            Projectiles = 1,
            ProjectileSpeed = 0f,
            Range = 2.3f,
            Radius = 2.3f,
            Pierce = 0,
            Knockback = 0f,         // the design: nothing is shoved out of the ring
            CritChance = 0.04f,
            ProcCoefficient = 0.5f,
            SpreadDegrees = 0f,
            Tint = new Color(0.62f, 0.90f, 1.00f),
            DamagePerLevel = 0.30f,
            CooldownPerLevel = -0.05f,
            ProjectilesEveryNLevels = 0,
            MaxLevel = 8,
        },

        // ---- 14. Slow heavy melee. One swing every 2.8s for 48, in an arc as
        // wide as the Cleaver's and a little longer. Its DPS is ordinary; what
        // it sells is the size of the single number — enough to one-shot a
        // pack of trash through minute five — and the biggest knockback in the
        // game, which buys back the 2.8 seconds it spends doing nothing.
        new WeaponDef
        {
            Id = "forgefall_maul",
            DisplayName = "Forgefall Maul",
            Description = "Still glowing from the anvil. You will not swing it often, and you will not need to.",
            Rarity = Rarity.Rare,
            Shape = AttackShape.Melee,
            Element = Element.Fire,
            Damage = 48f,
            Cooldown = 2.8f,        // 17.1 dps per body, in one enormous lump
            Projectiles = 1,
            ProjectileSpeed = 0f,
            Range = 4.8f,
            Radius = 1.2f,
            Pierce = 0,
            Knockback = 9.5f,
            CritChance = 0.08f,
            ProcCoefficient = 1.8f, // slow and heavy, procs like the Mortar
            SpreadDegrees = 80f,
            Tint = new Color(1.00f, 0.40f, 0.20f),
            DamagePerLevel = 0.32f,
            CooldownPerLevel = -0.055f,
            ProjectilesEveryNLevels = 0,   // area already; growth is all damage
            MaxLevel = 8,
        },

        // ---- 15. Scatter explosive. Three small blasts thrown wide — the
        // Spores' idea pushed further toward coverage. Individually the weakest
        // explosions on the list; together they put an area denial carpet in
        // front of the player, which is what a kiting build wants behind it.
        new WeaponDef
        {
            Id = "hailstone_cluster",
            DisplayName = "Hailstone Cluster",
            Description = "Three fist-sized stones of old ice that burst when they land. Throw them where you are about to stop being.",
            Rarity = Rarity.Common,
            Shape = AttackShape.Explosive,
            Element = Element.Ice,
            Damage = 5.5f,
            Cooldown = 1.0f,        // 16.5 dps across three stones
            Projectiles = 3,
            ProjectileSpeed = 13f,
            Range = 11f,
            Radius = 1.5f,
            Pierce = 0,
            Knockback = 1.6f,
            CritChance = 0.03f,
            ProcCoefficient = 0.5f,
            SpreadDegrees = 46f,
            Tint = new Color(0.80f, 0.92f, 1.00f),
            DamagePerLevel = 0.22f,
            CooldownPerLevel = -0.05f,
            ProjectilesEveryNLevels = 3,
            MaxLevel = 8,
        },

        // ---- 16. Homing swarm. Four slow seekers at four damage: the Wisp's
        // never-miss promise spread over more bodies, so it spends its output on
        // the crowd rather than on one target. The lowest per-hit number in the
        // game, which is why ProjectileCount items look so good on it — every
        // bonus projectile is another dart.
        new WeaponDef
        {
            Id = "blightwasp_swarm",
            DisplayName = "Blightwasp Swarm",
            Description = "A papery nest that will not stay shut. What leaves it chooses its own target, and it is never you.",
            Rarity = Rarity.Uncommon,
            Shape = AttackShape.Homing,
            Element = Element.Poison,
            Damage = 4f,
            Cooldown = 1.0f,        // 16.0 dps over four darts, nearly all of it lands
            Projectiles = 4,
            ProjectileSpeed = 12f,
            Range = 22f,
            Radius = 0.24f,
            Pierce = 0,
            Knockback = 0.3f,
            CritChance = 0.05f,
            ProcCoefficient = 0.3f,
            SpreadDegrees = 70f,    // they leave as a cloud and converge
            Tint = new Color(0.78f, 0.95f, 0.30f),
            DamagePerLevel = 0.24f,
            CooldownPerLevel = -0.05f,
            ProjectilesEveryNLevels = 3,
            MaxLevel = 8,
        },

        // ---- 17. Sniper. The longest reach and fastest shot of anything that is
        // not the Lance, a big crit chance, and pierce 2 so a lucky line pays.
        // Its weakness is the one every sniper has: it fires at the *nearest*
        // target, and the nearest target past minute eight is usually something
        // a smaller weapon should have handled.
        new WeaponDef
        {
            Id = "searing_longshot",
            DisplayName = "Searing Longshot",
            Description = "A single white-hot bolt from very far away. It arrives before the sound does.",
            Rarity = Rarity.Rare,
            Shape = AttackShape.Projectile,
            Element = Element.Fire,
            Damage = 38f,
            Cooldown = 2.3f,        // 16.5 dps, every point of it on one target
            Projectiles = 1,
            ProjectileSpeed = 46f,
            Range = 38f,
            Radius = 0.32f,
            Pierce = 2,
            Knockback = 4f,
            CritChance = 0.18f,
            ProcCoefficient = 1.6f,
            SpreadDegrees = 0f,
            Tint = new Color(1.00f, 0.82f, 0.55f),
            DamagePerLevel = 0.30f,
            CooldownPerLevel = -0.055f,
            ProjectilesEveryNLevels = 4,
            MaxLevel = 8,
        },

        // ---- 18. The Epic. Sits between the band and the Legendary at 19.3 dps:
        // three homing bolts that each pierce twice, so it is a seeker that does
        // not stop at its target. Priced up from the Wisp because pierce on a
        // homing shot is close to free value — it turns around and finds the
        // next one.
        new WeaponDef
        {
            Id = "tempest_spindle",
            DisplayName = "Tempest Spindle",
            Description = "A brass spindle wound with live weather. Each thread it lets go of finds three throats before it frays.",
            Rarity = Rarity.Epic,
            Shape = AttackShape.Homing,
            Element = Element.Lightning,
            Damage = 9f,
            Cooldown = 1.4f,        // 19.3 dps before pierce
            Projectiles = 3,
            ProjectileSpeed = 17f,
            Range = 28f,
            Radius = 0.34f,
            Pierce = 2,
            Knockback = 1.2f,
            CritChance = 0.07f,
            ProcCoefficient = 0.8f,
            SpreadDegrees = 54f,
            Tint = new Color(0.70f, 0.78f, 1.00f),
            DamagePerLevel = 0.27f,
            CooldownPerLevel = -0.055f,
            ProjectilesEveryNLevels = 4,
            MaxLevel = 8,
        },
    };

    // =====================================================================
    //  ENEMIES
    // =====================================================================
    //
    //  Cost is the only knob the director reads for "how much of this should be
    //  on screen". It spends credits at (2.2 + 2.6 x minutes) per second and
    //  weights each definition by how close its cost is to a target threat of
    //  (1 + 0.9 x minutes) — so cost, not health, decides whether a minute feels
    //  like a swarm or a squad, and the useful cost range across a fifteen minute
    //  run is about 1 to 15.
    //
    //  Costs here are laid on a rough doubling ladder (1, 2, 2, 4, 6, 9, 13) so
    //  that the target curve passes through each in turn and the previous tier
    //  fades out on its own. UnlockAtSeconds is set slightly *before* the target
    //  reaches the cost, so a new enemy arrives as an occasional shock first and
    //  becomes the staple a minute later.
    //
    //  Silhouette is classified from the stats by Actors.Enemy: speed >= 5 reads
    //  as a Runner, health >= 60 or knockback resist >= 0.5 as a Brute. Those
    //  thresholds are respected deliberately below, so that what a thing looks
    //  like is what it does.
    //
    //  Health is multiplied by the director's difficulty curve, which reaches
    //  roughly 3x by minute five and 12x by minute twelve. Base values are
    //  therefore small; anything authored to feel tough at minute twelve would be
    //  unkillable at minute one.

    private static List<EnemyDef> BuildEnemies() => new()
    {
        // ---- Grunt, cost 1. Present from the first second; the thing the
        // starting weapon is timed against (16 dps kills one in under a second).
        new EnemyDef
        {
            Id = "mote",
            DisplayName = "Mote",
            Health = 14f, Damage = 6f, MoveSpeed = 3.2f,
            AttackRange = 1.5f, AttackCooldown = 1.1f,
            Radius = 0.34f, Height = 1.0f, KnockbackResist = 0f,
            XpValue = 1, GoldValue = 1,
            Cost = 1, UnlockAtSeconds = 0f,
            Tint = new Color(0.45f, 0.72f, 0.45f),
        },

        // ---- Grunt, cost 2. The other opening enemy: slower and twice as solid,
        // so the first minute already contains a pacing decision rather than one
        // uniform stream.
        new EnemyDef
        {
            Id = "husk",
            DisplayName = "Husk",
            Health = 27f, Damage = 9f, MoveSpeed = 2.8f,
            AttackRange = 1.7f, AttackCooldown = 1.2f,
            Radius = 0.46f, Height = 1.7f, KnockbackResist = 0.1f,
            XpValue = 2, GoldValue = 1,
            Cost = 2, UnlockAtSeconds = 0f,
            Tint = new Color(0.72f, 0.44f, 0.32f),
        },

        // ---- Runner, cost 2. Speed 5.6 against the player's base 6: it cannot
        // quite catch a player who commits to running, which is the entire lesson
        // it exists to teach.
        new EnemyDef
        {
            Id = "skitterling",
            DisplayName = "Skitterling",
            Health = 17f, Damage = 7f, MoveSpeed = 4.4f,
            AttackRange = 1.3f, AttackCooldown = 0.75f,
            Radius = 0.32f, Height = 0.8f, KnockbackResist = 0f,
            XpValue = 2, GoldValue = 1,
            Cost = 2, UnlockAtSeconds = 40f,
            Tint = new Color(0.95f, 0.85f, 0.30f),
        },

        // ---- Brute, cost 4. First enemy that will not be shoved: knockback
        // resist 0.45 is under the silhouette threshold but high enough that
        // Cleaver Arc stops buying free space, which is when the player learns
        // that crowd control has limits.
        new EnemyDef
        {
            Id = "gravel_brute",
            DisplayName = "Gravel Brute",
            Health = 82f, Damage = 15f, MoveSpeed = 2.3f,
            AttackRange = 2.0f, AttackCooldown = 1.4f,
            Radius = 0.64f, Height = 1.95f, KnockbackResist = 0.45f,
            XpValue = 5, GoldValue = 3,
            Cost = 4, UnlockAtSeconds = 105f,
            Tint = new Color(0.42f, 0.50f, 0.72f),
        },

        // ---- Runner, cost 6. Faster than the player and hits hard. Answered by
        // positioning or by armour, not by outrunning it — the first genuine
        // "your build has to do something" enemy.
        new EnemyDef
        {
            Id = "cinderling",
            DisplayName = "Cinderling",
            Health = 48f, Damage = 16f, MoveSpeed = 4.9f,
            AttackRange = 1.5f, AttackCooldown = 0.9f,
            Radius = 0.38f, Height = 1.2f, KnockbackResist = 0.15f,
            XpValue = 7, GoldValue = 4,
            Cost = 6, UnlockAtSeconds = 180f,
            Tint = new Color(1.00f, 0.48f, 0.18f),
        },

        // ---- Brute, cost 9. The mid-game wall: high health, high knockback
        // resist, slow. Nothing about it is a surprise; it simply has to be
        // killed, and it is the honest test of whether damage has kept up.
        // 165 -> 115: at 165 a 13-minute bot run killed 2 of 52 — a wall that is
        // never broken stops being a test and becomes scenery.
        new EnemyDef
        {
            Id = "pale_sentinel",
            DisplayName = "Pale Sentinel",
            Health = 115f, Damage = 23f, MoveSpeed = 2.6f,
            AttackRange = 2.2f, AttackCooldown = 1.5f,
            Radius = 0.72f, Height = 2.25f, KnockbackResist = 0.7f,
            XpValue = 12, GoldValue = 7,
            Cost = 9, UnlockAtSeconds = 300f,
            Tint = new Color(0.78f, 0.74f, 0.92f),
        },

        // ---- Brute, cost 13. Top of the ordinary ladder; the target threat only
        // reaches 13 at about minute thirteen, so this is what the last two
        // minutes are mostly made of. 275 -> 185 for the same reason as the
        // Sentinel (4 kills of 40 at 275).
        new EnemyDef
        {
            Id = "rift_maw",
            DisplayName = "Rift Maw",
            Health = 185f, Damage = 31f, MoveSpeed = 3.4f,
            AttackRange = 2.4f, AttackCooldown = 1.3f,
            Radius = 0.82f, Height = 2.5f, KnockbackResist = 0.55f,
            XpValue = 20, GoldValue = 12,
            Cost = 13, UnlockAtSeconds = 460f,
            Tint = new Color(0.85f, 0.25f, 0.72f),
        },

        // ---- Elite, cost 8, first wave at 70s. Elites only spawn during elite
        // waves and get a x6 weight there, so cost is about how many arrive at
        // once rather than how often: 8 means a handful, not a crowd. XP is
        // deliberately six times a Gravel Brute's — an elite wave should be worth
        // standing and fighting.
        new EnemyDef
        {
            Id = "ash_warden",
            DisplayName = "Warden of Ash",
            Health = 240f, Damage = 25f, MoveSpeed = 3.7f,
            AttackRange = 2.1f, AttackCooldown = 1.15f,
            Radius = 0.80f, Height = 2.4f, KnockbackResist = 0.6f,
            XpValue = 30, GoldValue = 20,
            Cost = 8, UnlockAtSeconds = 60f, IsElite = true,
            Tint = new Color(1.00f, 0.62f, 0.20f),
        },

        // ---- Elite, cost 16. Replaces the Warden in late elite waves — same
        // cost-vs-target logic as the ordinary ladder, so the two do not compete
        // for the same minute.
        new EnemyDef
        {
            Id = "frost_herald",
            DisplayName = "Frostbound Herald",
            Health = 640f, Damage = 35f, MoveSpeed = 3.2f,
            AttackRange = 2.6f, AttackCooldown = 1.4f,
            Radius = 0.95f, Height = 2.85f, KnockbackResist = 0.85f,
            XpValue = 70, GoldValue = 45,
            Cost = 16, UnlockAtSeconds = 320f, IsElite = true,
            Tint = new Color(0.42f, 0.80f, 1.00f),
        },

        // ---- Boss, tier 1. Ends the first phase (Game.StageDirector), or comes
        // on the old timer when no stage director runs. Health rides the crowd's
        // curve (x2.9 at 3:30, x6.6 at 8:00, x12 at 12:00) times the phase's own
        // boss scale. 1900 was tuned unscaled at four minutes to about a 70s
        // fight for the bot's typical build there (measured 70s and 184s on two
        // seeds); the phase scale trims that for the phase fight, where the
        // crowd is capped and overtime hardens it. Bosses written for later
        // tiers carry more base health on purpose — the bot is far weaker than
        // a practised player, so later fights lean hard rather than soft.
        //
        // Reach 3.2m and a 1.6s swing: readable, avoidable, and lethal if
        // ignored. The gold value alone is worth more than the preceding minute.
        new EnemyDef
        {
            Id = "thresher_prime",
            DisplayName = "Thresher Prime",
            Health = 1900f, Damage = 46f, MoveSpeed = 2.95f,
            AttackRange = 3.2f, AttackCooldown = 1.6f,
            Radius = 1.5f, Height = 4.2f, KnockbackResist = 0.95f,
            XpValue = 400, GoldValue = 260,
            Cost = 30, UnlockAtSeconds = 230f, IsBoss = true,
            Tint = new Color(0.92f, 0.18f, 0.24f),
            BossTier = 1,
            // The first boss teaches the vocabulary, one verb per move: leave
            // the disc and jump the ring (Slam), sidestep the lane (Charge),
            // find the gap (Burst). Phase two adds nothing new to read, only
            // something new to manage: Bloat Polyps that walk in and burst.
            AbilityCooldown = 2.4f,
            BossMoves = Moves(
                new BossAttack { Move = BossMove.Slam, Weight = 1.2f, WindUp = 1.1f, Radius = 4.5f, Count = 1, Speed = 10f, DamageScale = 1.0f, MaxRange = 9f },
                new BossAttack { Move = BossMove.Charge, Weight = 1.0f, WindUp = 0.9f, Radius = 16f, Speed = 20f, DamageScale = 1.1f, MinRange = 4f, MaxRange = 18f },
                new BossAttack { Move = BossMove.RadialBurst, Weight = 0.9f, WindUp = 0.8f, Count = 14, Speed = 7f, DamageScale = 0.4f },
                new BossAttack { Move = BossMove.Summon, Weight = 0.8f, WindUp = 0.8f, Count = 4, SummonId = "bloat_polyp", MinPhase = 2 }),
        },

        // ============================================== second wave of enemies
        //
        // Nothing below unlocks before 150s, so the first two and a half minutes
        // — the part of the run tuned against the starter weapon alone — are
        // exactly as they were. The new ordinary enemies fill the gaps in the
        // cost ladder (3, 5, 11, 17) rather than doubling up on a rung, so each
        // one has a minute where the director's target threat passes through it
        // and it is the staple, instead of competing with an existing enemy for
        // the same slot.

        // ---- Runner, cost 3, at 150s. A swarm runner: fast enough to be a
        // Runner (5.2 >= 5) but only marginally tougher than a Skitterling, and
        // a third cheaper per body than anything else that fast, so the director
        // buys them in numbers. The point is a minute-three wave that arrives
        // from the side the player was not watching.
        new EnemyDef
        {
            Id = "brine_leech",
            DisplayName = "Brine Leech",
            Health = 24f, Damage = 8f, MoveSpeed = 4.3f,
            AttackRange = 1.3f, AttackCooldown = 0.7f,
            Radius = 0.30f, Height = 0.7f, KnockbackResist = 0f,
            XpValue = 3, GoldValue = 2,
            Cost = 3, UnlockAtSeconds = 150f,
            Tint = new Color(0.36f, 0.78f, 0.70f),
        },

        // ---- Grunt, cost 5, at 230s. Deliberately *not* a Brute: health 55 and
        // resist 0.35 sit just under both silhouette thresholds, so it reads as
        // an ordinary soldier and is shoved like one — it is the honest filler
        // between the Gravel Brute and the Cinderling, a thing that just takes
        // a few more hits than the eye expects.
        new EnemyDef
        {
            Id = "kelp_shambler",
            DisplayName = "Kelp Shambler",
            Health = 55f, Damage = 13f, MoveSpeed = 2.5f,
            AttackRange = 1.8f, AttackCooldown = 1.25f,
            Radius = 0.52f, Height = 1.85f, KnockbackResist = 0.35f,
            XpValue = 6, GoldValue = 3,
            Cost = 5, UnlockAtSeconds = 230f,
            Tint = new Color(0.30f, 0.56f, 0.34f),
        },

        // ---- Runner, cost 11, at 400s. The late runner. Cinderling is the
        // first enemy faster than the player; this is the first *tough* one —
        // 120 health at 6.1 speed means a kiting build can no longer outrun its
        // problems and has to kill them, which is the question minute seven is
        // supposed to ask.
        new EnemyDef
        {
            Id = "gloom_lurcher",
            DisplayName = "Gloom Lurcher",
            Health = 120f, Damage = 25f, MoveSpeed = 4.9f,
            AttackRange = 1.7f, AttackCooldown = 0.95f,
            Radius = 0.50f, Height = 1.5f, KnockbackResist = 0.3f,
            XpValue = 16, GoldValue = 9,
            Cost = 11, UnlockAtSeconds = 400f,
            Tint = new Color(0.40f, 0.30f, 0.62f),
        },

        // ---- Brute, cost 17, at 600s. Above the Rift Maw on the ladder; the
        // target threat only reaches 17 after the run is over, so it is never
        // the staple — it is the thing that turns up in the last five minutes
        // often enough to matter and rarely enough to be an event. Nearly
        // unshovable and very slow: the late-game test of raw damage that the
        // Pale Sentinel is for minute five. 420 -> 150 and 2.2 -> 2.9 speed: at
        // 420 (about 3,800 after scaling) a full late build killed none of the
        // 21 it met, and at 2.2 m/s most of them never reached it to be fought.
        new EnemyDef
        {
            Id = "tidewall_colossus",
            DisplayName = "Tidewall Colossus",
            Health = 150f, Damage = 38f, MoveSpeed = 2.9f,
            AttackRange = 2.6f, AttackCooldown = 1.6f,
            Radius = 0.98f, Height = 3.0f, KnockbackResist = 0.9f,
            XpValue = 30, GoldValue = 18,
            Cost = 17, UnlockAtSeconds = 600f,
            Tint = new Color(0.38f, 0.58f, 0.66f),
        },

        // ---- Elite, cost 11, at 190s. Fills the gap between the Warden (8) and
        // the Herald (16) on the elite ladder, so the mid-game elite waves are a
        // mix rather than a hand-off. Faster and frailer than the Warden: an
        // elite that chases, so an elite wave stops being something a player can
        // simply walk away from — and, since behaviours, one that closes the last
        // few metres with a telegraphed charge instead of an invisible lunge.
        new EnemyDef
        {
            Id = "gale_revenant",
            DisplayName = "Gale Revenant",
            Health = 330f, Damage = 22f, MoveSpeed = 4.6f,
            AttackRange = 2.0f, AttackCooldown = 0.95f,
            Radius = 0.70f, Height = 2.3f, KnockbackResist = 0.5f,
            XpValue = 45, GoldValue = 28,
            Cost = 11, UnlockAtSeconds = 190f, IsElite = true,
            Tint = new Color(0.75f, 0.95f, 0.88f),
            Behavior = EnemyBehavior.Charger,
            AbilityRange = 12f, AbilityCooldown = 3.6f, WindUp = 0.75f,
            AbilitySpeed = 20f, AbilityDamage = 1.4f, AbilityRadius = 0.9f,
        },

        // ---- Boss, cost 40, at 470s. Never the first boss — Thresher Prime is
        // the only one unlocked at 240s — and weighted 40:30 against it at 8 and
        // 12 minutes, so a run usually meets both. Faster than the Thresher and
        // swinging more often, with less health: a boss you cannot simply
        // outwalk, which is the same lesson the Gale Revenant teaches in an
        // elite wave, asked again at the scale of a boss.
        new EnemyDef
        {
            Id = "breaker_matriarch",
            DisplayName = "Breaker Matriarch",
            Health = 3400f, Damage = 50f, MoveSpeed = 3.5f,
            AttackRange = 3.6f, AttackCooldown = 1.3f,
            Radius = 1.65f, Height = 4.8f, KnockbackResist = 0.97f,
            XpValue = 550, GoldValue = 360,
            Cost = 40, UnlockAtSeconds = 470f, IsBoss = true,
            Tint = new Color(0.20f, 0.62f, 0.86f),
            BossTier = 2,
            // A charger first: two of every five moves are lanes, and phase
            // two chains them. MeteorRain arrives at half health so the
            // second half cannot be survived by sidestepping alone.
            AbilityCooldown = 2.1f,
            BossMoves = Moves(
                new BossAttack { Move = BossMove.Charge, Weight = 2.0f, WindUp = 0.8f, Radius = 20f, Speed = 24f, DamageScale = 1.0f, MinRange = 4f, MaxRange = 22f },
                new BossAttack { Move = BossMove.Slam, Weight = 1.0f, WindUp = 1.0f, Radius = 5f, Count = 1, Speed = 11f, DamageScale = 1.0f, MaxRange = 10f },
                new BossAttack { Move = BossMove.Barrage, Weight = 1.0f, WindUp = 0.6f, Count = 3, Speed = 9f, DamageScale = 0.4f },
                new BossAttack { Move = BossMove.MeteorRain, Weight = 1.0f, WindUp = 0.5f, Count = 4, Radius = 2.6f, Speed = 1.3f, DamageScale = 0.8f, MinPhase = 2 }),
        },

        // ============================================= third wave: behaviours
        //
        // Monsters that fight differently rather than harder. Every one of
        // them unlocks after 120s, so the opening two minutes — the part tuned
        // against the starter weapon alone, where the bot currently dies
        // between 1:00 and 1:35 — contain exactly the roster they did before.
        //
        // Costs sit on rungs the ladder already has (2, 3, 4, 5, 6, 10), each
        // next to an older enemy of similar threat, so a minute's budget buys
        // a *mix* of chasers and specialists instead of the specialist
        // displacing the chaser. Ability damage is written as a multiple of
        // contact damage so the director's damage curve scales both together.

        // ---- Shooter, cost 3, at 125s. The first ranged enemy: one slow shot
        // (7.5 m/s against a 6 m/s player — dodged by walking sideways) after
        // a 0.7s glow, from 10m. Fragile, so it teaches "go and kill the
        // thing at the back" without punishing a player who ignores it.
        new EnemyDef
        {
            Id = "brinespitter",
            DisplayName = "Brinespitter",
            Health = 20f, Damage = 6f, MoveSpeed = 2.9f,
            AttackRange = 1.5f, AttackCooldown = 1.2f,
            Radius = 0.4f, Height = 1.5f, KnockbackResist = 0f,
            XpValue = 3, GoldValue = 2,
            Cost = 3, UnlockAtSeconds = 125f,
            Tint = new Color(0.86f, 0.46f, 0.74f),
            Behavior = EnemyBehavior.Shooter,
            AbilityRange = 10f, AbilityCooldown = 3.2f, WindUp = 0.7f,
            AbilityDamage = 1f, AbilitySpeed = 7.5f, AbilityCount = 1,
        },

        // ---- Exploder, cost 2, at 140s. Fast and frail; lights a 0.9s fuse
        // at 2.2m and bursts for 2.6x its contact damage over a 2.6m disc.
        // Killing it during the swell cancels the blast, and letting it go off
        // pays no xp — the right answer is always to shoot it first.
        new EnemyDef
        {
            Id = "bloat_polyp",
            DisplayName = "Bloat Polyp",
            Health = 16f, Damage = 5f, MoveSpeed = 3.9f,
            AttackRange = 1.3f, AttackCooldown = 1.0f,
            Radius = 0.45f, Height = 0.9f, KnockbackResist = 0f,
            XpValue = 2, GoldValue = 1,
            Cost = 2, UnlockAtSeconds = 140f,
            Tint = new Color(0.80f, 0.86f, 0.34f),
            Behavior = EnemyBehavior.Exploder,
            AbilityRange = 2.2f, WindUp = 0.9f,
            AbilityRadius = 2.6f, AbilityDamage = 2.6f,
        },

        // ---- Charger, cost 4, at 165s. Slow on foot; from 11m it plants,
        // paints its lane for 0.85s (locking direction at the halfway point)
        // and runs it at 17 m/s. Sidestep or dash through: the dash's
        // i-frames are exactly what the lane is asking for.
        new EnemyDef
        {
            Id = "reefhorn",
            DisplayName = "Reefhorn",
            Health = 50f, Damage = 10f, MoveSpeed = 2.7f,
            AttackRange = 1.7f, AttackCooldown = 1.3f,
            Radius = 0.55f, Height = 1.4f, KnockbackResist = 0.3f,
            XpValue = 5, GoldValue = 3,
            Cost = 4, UnlockAtSeconds = 165f,
            Tint = new Color(0.88f, 0.40f, 0.32f),
            Behavior = EnemyBehavior.Charger,
            AbilityRange = 11f, AbilityCooldown = 4.5f, WindUp = 0.85f,
            AbilitySpeed = 17f, AbilityDamage = 1.6f, AbilityRadius = 0.8f,
        },

        // ---- Leaper, cost 5, at 215s. Marks the ground under the player,
        // crouches for 0.55s and lands there with a 2.4m blast. The marker
        // fills across crouch and flight together, so its edge closing is the
        // countdown — and anyone who moved on the mark is already safe.
        new EnemyDef
        {
            Id = "surge_leaper",
            DisplayName = "Surge Leaper",
            Health = 60f, Damage = 12f, MoveSpeed = 3.3f,
            AttackRange = 1.6f, AttackCooldown = 1.2f,
            Radius = 0.5f, Height = 1.3f, KnockbackResist = 0.25f,
            XpValue = 6, GoldValue = 4,
            Cost = 5, UnlockAtSeconds = 215f,
            Tint = new Color(0.36f, 0.62f, 0.96f),
            Behavior = EnemyBehavior.Leaper,
            AbilityRange = 12f, AbilityCooldown = 4.0f, WindUp = 0.55f,
            AbilityRadius = 2.4f, AbilityDamage = 1.5f,
        },

        // ---- Splitter, cost 6, at 270s. A plain chaser that bursts into four
        // Shoal Fry when it dies. The cost buys the brood too, which is why it
        // is priced a rung above its own threat: killing it is the start of
        // the fight, not the end, and it is most dangerous killed at your feet.
        new EnemyDef
        {
            Id = "shoalmother",
            DisplayName = "Shoalmother",
            Health = 90f, Damage = 14f, MoveSpeed = 2.4f,
            AttackRange = 1.9f, AttackCooldown = 1.3f,
            Radius = 0.7f, Height = 1.6f, KnockbackResist = 0.4f,
            XpValue = 6, GoldValue = 4,
            Cost = 6, UnlockAtSeconds = 270f,
            Tint = new Color(0.52f, 0.76f, 0.86f),
            SplitInto = "shoal_fry", SplitCount = 4,
        },

        // ---- Summon only: the Shoalmother's brood and the Knell Leviathan's
        // adds. Never bought by the director, so it has no place on the cost
        // ladder; fast and very frail, a swarm that punishes standing still.
        new EnemyDef
        {
            Id = "shoal_fry",
            DisplayName = "Shoal Fry",
            Health = 10f, Damage = 5f, MoveSpeed = 4.2f,
            AttackRange = 1.2f, AttackCooldown = 0.8f,
            Radius = 0.26f, Height = 0.6f, KnockbackResist = 0f,
            XpValue = 1, GoldValue = 0,
            Cost = 1, UnlockAtSeconds = 0f,
            Tint = new Color(0.72f, 0.90f, 0.96f),
            DirectorSpawns = false,
        },

        // ---- Shooter, cost 10, at 420s. The late ranged threat: a three-shot
        // fan from 9m behind the brutes it arrives with, so the fan has to be
        // read and walked through the gaps. First tuned at 13m and 120 health,
        // where it sat beyond most weapons' reach and was killed 2 times in 92.
        new EnemyDef
        {
            Id = "deepcaller",
            DisplayName = "Deepcaller",
            Health = 85f, Damage = 18f, MoveSpeed = 2.6f,
            AttackRange = 1.8f, AttackCooldown = 1.3f,
            Radius = 0.6f, Height = 2.1f, KnockbackResist = 0.3f,
            XpValue = 14, GoldValue = 8,
            Cost = 10, UnlockAtSeconds = 420f,
            Tint = new Color(0.56f, 0.36f, 0.86f),
            Behavior = EnemyBehavior.Shooter,
            AbilityRange = 9f, AbilityCooldown = 3.6f, WindUp = 0.8f,
            AbilityDamage = 0.8f, AbilitySpeed = 8.5f, AbilityCount = 3, AbilitySpread = 30f,
        },

        // ---- Boss, tier 2, cost 38, at 470s. The ranged boss: it fights from
        // mid range, filling the floor with blast circles and the air with rings
        // while Brinespitters crowd the gaps. Below half it learns to slam with
        // a double ring, so closing in stops being free. Every move has a
        // MaxRange: with none it cast from across the arena, never walked into
        // reach, and outlived a seven-minute fight at half health.
        new EnemyDef
        {
            Id = "undertow_oracle",
            DisplayName = "Undertow Oracle",
            Health = 3200f, Damage = 40f, MoveSpeed = 2.4f,
            AttackRange = 3.0f, AttackCooldown = 1.5f,
            Radius = 1.4f, Height = 4.4f, KnockbackResist = 0.95f,
            XpValue = 500, GoldValue = 320,
            Cost = 38, UnlockAtSeconds = 470f, IsBoss = true,
            Tint = new Color(0.28f, 0.84f, 0.74f),
            BossTier = 2,
            AbilityCooldown = 2.2f,
            BossMoves = Moves(
                new BossAttack { Move = BossMove.RadialBurst, Weight = 1.1f, WindUp = 0.9f, Count = 18, Speed = 6.5f, DamageScale = 0.35f, MaxRange = 13f },
                new BossAttack { Move = BossMove.Barrage, Weight = 0.9f, WindUp = 0.6f, Count = 3, Speed = 9.5f, DamageScale = 0.35f, MaxRange = 14f },
                new BossAttack { Move = BossMove.MeteorRain, Weight = 1.2f, WindUp = 0.5f, Count = 5, Radius = 2.4f, Speed = 1.4f, DamageScale = 0.8f, MaxRange = 18f },
                new BossAttack { Move = BossMove.Summon, Weight = 0.6f, WindUp = 0.8f, Count = 3, SummonId = "brinespitter", MaxRange = 14f },
                new BossAttack { Move = BossMove.Slam, Weight = 1.0f, WindUp = 1.0f, Radius = 4f, Count = 2, Speed = 9f, DamageScale = 0.9f, MaxRange = 9f, MinPhase = 2 }),
        },

        // ---- Boss, tier 3, cost 55, at 650s. The last boss and the only one
        // that uses everything: a double-ring slam, a 22m charge, meteor
        // rain, Shoal Fry swarms, and from half health a 24-shot ring. Highest
        // cost, so once it unlocks the twelve-minute boss is usually this.
        new EnemyDef
        {
            Id = "knell_leviathan",
            DisplayName = "Knell Leviathan",
            Health = 4600f, Damage = 55f, MoveSpeed = 3.2f,
            AttackRange = 3.8f, AttackCooldown = 1.4f,
            Radius = 1.85f, Height = 5.0f, KnockbackResist = 0.98f,
            XpValue = 700, GoldValue = 450,
            Cost = 55, UnlockAtSeconds = 650f, IsBoss = true,
            Tint = new Color(0.32f, 0.32f, 0.95f),
            BossTier = 3,
            AbilityCooldown = 2.0f,
            BossMoves = Moves(
                new BossAttack { Move = BossMove.Slam, Weight = 1.2f, WindUp = 1.2f, Radius = 5.5f, Count = 2, Speed = 12f, DamageScale = 1.0f, MaxRange = 11f },
                new BossAttack { Move = BossMove.Charge, Weight = 1.0f, WindUp = 0.9f, Radius = 22f, Speed = 26f, DamageScale = 1.1f, MinRange = 5f, MaxRange = 24f },
                new BossAttack { Move = BossMove.MeteorRain, Weight = 1.0f, WindUp = 0.5f, Count = 6, Radius = 2.8f, Speed = 1.2f, DamageScale = 0.8f },
                new BossAttack { Move = BossMove.Summon, Weight = 0.7f, WindUp = 0.8f, Count = 6, SummonId = "shoal_fry" },
                new BossAttack { Move = BossMove.RadialBurst, Weight = 1.0f, WindUp = 0.7f, Count = 24, Speed = 8f, DamageScale = 0.35f, MinPhase = 2 }),
        },
    };

    // =====================================================================
    //  ITEMS
    // =====================================================================
    //
    //  The rule the whole table is written to: a Common taken at level 2 should
    //  be worth roughly 10% of one axis of the build, and a Legendary should
    //  change what the run is about. Concretely —
    //
    //    Common     one effect, ~+10% or the flat equivalent, unlimited stacks
    //    Uncommon   two small effects, or one at ~+20%
    //    Rare       two real effects, usually with a small cost attached
    //    Epic       a Multiplicative, or a large flat with a real drawback, capped
    //    Legendary  reshapes the run; capped at one; both are trades, not gifts
    //
    //  Additive modifiers sum before applying, so ten +10% items are +100% and
    //  not x2.59 — that is what keeps a stack of Commons readable and is why
    //  Multiplicative appears only at Epic and above.
    //
    //  Weight falls with rarity (1.0 / 0.7 / 0.45 / 0.25 / 0.10) so that the
    //  offer pool has a sensible shape even before it applies its own rarity
    //  weighting and the Luck stat.
    //
    //  MaxStacks 0 means unlimited, and is only given to items whose effect is
    //  linear and whose stat has diminishing returns of its own (armour, evasion)
    //  or which stay honest under addition (damage, speed, xp).

    private static List<ItemDef> BuildItems() => new()
    {
        // ------------------------------------------------------------ Common

        // The reference Common. Every other item's value is quoted against this
        // one: "+10% damage, forever, no cost".
        Item("whetstone", "Whetstone", Rarity.Common, 1.0f, 0,
            "A stone worn concave by use. Ten percent sharper, every time, without complaint.",
            new Color(0.80f, 0.84f, 0.90f),
            Effect(Stat.Damage, ModifierOp.Additive, 0.10f)),

        // Movement is the defensive layer, so speed is priced like damage. The
        // jump and dash riders cost nothing to give and make the three movement
        // stats reachable, which they otherwise would not be.
        Item("springheel_boots", "Springheel Boots", Rarity.Common, 1.0f, 0,
            "Sprung at the heel. You move a little quicker, land a little later, and recover a little sooner.",
            new Color(0.62f, 0.88f, 0.72f),
            Effect(Stat.MoveSpeed, ModifierOp.Additive, 0.07f),
            Effect(Stat.JumpHeight, ModifierOp.Additive, 0.22f),
            Effect(Stat.DashCooldown, ModifierOp.Additive, -0.10f)),

        // +6 armour is 17% reduction on its own and only 6% more when it is the
        // tenth copy — the armour curve makes this self-limiting, which is why it
        // can be unlimited without ever reaching immunity.
        Item("iron_plating", "Iron Plating", Rarity.Common, 1.0f, 0,
            "Riveted scrap, badly fitted. Takes the edge off everything and the whole of nothing.",
            new Color(0.66f, 0.70f, 0.76f),
            Effect(Stat.Armor, ModifierOp.Flat, 6f)),

        // Pickup range is quality-of-life that quietly becomes damage: orbs
        // collected are levels gained. Paired with a little gold so it is never a
        // dead pick for a player who already has range.
        Item("scavengers_pouch", "Scavenger's Pouch", Rarity.Common, 1.0f, 0,
            "Deep, greasy, and always slightly open. Things fall into it from further away than they should.",
            new Color(0.86f, 0.74f, 0.42f),
            Effect(Stat.PickupRange, ModifierOp.Additive, 0.35f),
            Effect(Stat.GoldGain, ModifierOp.Additive, 0.08f)),

        // +18 max health is 18% of the starting pool and shrinks as a share of it,
        // which is correct: a flat heal item should be a strong early pick and a
        // filler one at minute twelve.
        Item("field_rations", "Field Rations", Rarity.Common, 1.0f, 0,
            "Dense, salted, and joyless. You are measurably harder to kill and no happier about it.",
            new Color(0.94f, 0.58f, 0.52f),
            Effect(Stat.MaxHealth, ModifierOp.Flat, 18f),
            Effect(Stat.HealthRegen, ModifierOp.Flat, 2f)),

        // Attack speed divides cooldowns, so +8% additive is very slightly better
        // than +8% damage on a stacked build. Held one notch below Whetstone's
        // 10% to keep the two honest against each other.
        Item("oiled_gears", "Oiled Gears", Rarity.Common, 1.0f, 0,
            "Everything you own cycles a fraction faster. The fraction adds up.",
            new Color(0.78f, 0.82f, 0.58f),
            Effect(Stat.AttackSpeed, ModifierOp.Additive, 0.08f)),

        // XP gain compounds into every other pick, so it is priced above the
        // 10% line at 12% — worth taking early, close to worthless at minute
        // fourteen, which is exactly the shape a scaling item should have.
        Item("students_ledger", "Student's Ledger", Rarity.Common, 0.9f, 0,
            "Someone else's notes, meticulously kept. You learn faster from other people's mistakes.",
            new Color(0.68f, 0.86f, 0.94f),
            Effect(Stat.XpGain, ModifierOp.Additive, 0.12f)),

        // ---------------------------------------------------------- Uncommon

        // Base crit is 5% for +100%; this roughly doubles the chance and adds a
        // sixth to the multiplier — about +9% dps on its own, and considerably
        // more once a second crit item lands. Crit is the build that pays for
        // patience.
        Item("focusing_lens", "Focusing Lens", Rarity.Uncommon, 0.7f, 0,
            "Ground for one purpose. Now and then it finds the exact place a thing comes apart.",
            new Color(0.60f, 0.90f, 1.00f),
            Effect(Stat.CritChance, ModifierOp.Flat, 0.06f),
            Effect(Stat.CritDamage, ModifierOp.Additive, 0.15f)),

        // Projectile speed is a real stat for slow shots (Mortar, Wisp Seeker) and
        // nearly dead for fast ones, so it carries a flat damage rider to stay
        // offerable in every build.
        Item("stormglass_shard", "Stormglass Shard", Rarity.Uncommon, 0.7f, 0,
            "Cut from a thing that was moving too fast to be glass. Your shots have taken the hint.",
            new Color(0.72f, 0.78f, 1.00f),
            Effect(Stat.ProjectileSpeed, ModifierOp.Additive, 0.25f),
            Effect(Stat.Damage, ModifierOp.Additive, 0.08f)),

        // Shield regenerates on its own after six quiet seconds, so 20 points is
        // worth far more than 20 health to a player who can disengage — and much
        // less to one who cannot. Deliberately the most build-dependent defence.
        Item("bulwark_charm", "Bulwark Charm", Rarity.Uncommon, 0.7f, 0,
            "A small warm weight at the sternum. It comes back on its own if you give it a moment.",
            new Color(0.55f, 0.80f, 1.00f),
            Effect(Stat.Shield, ModifierOp.Flat, 20f),
            Effect(Stat.Armor, ModifierOp.Flat, 4f)),

        // +8 evasion is 17% dodge on the curve. Evasion nullifies a hit outright
        // rather than reducing it, which makes it strictly better than armour
        // against the big late-game swings and strictly worse against a swarm.
        Item("wind_dancers_sash", "Wind-Dancer's Sash", Rarity.Uncommon, 0.7f, 0,
            "Weightless, and it moves before you do. Some blows simply arrive where you were.",
            new Color(0.72f, 0.94f, 0.86f),
            Effect(Stat.Evasion, ModifierOp.Flat, 8f),
            Effect(Stat.MoveSpeed, ModifierOp.Additive, 0.06f)),

        // Area is a multiplier on auras and blast radii and does nothing at all
        // for the two melee weapons and the rail. The damage rider is what keeps
        // it from being a trap pick in the wrong build.
        Item("emberbrand", "Emberbrand", Rarity.Uncommon, 0.7f, 0,
            "A brand that never quite cools. Everything you set alight burns a little wider.",
            new Color(1.00f, 0.60f, 0.30f),
            Effect(Stat.AreaSize, ModifierOp.Additive, 0.14f),
            Effect(Stat.Damage, ModifierOp.Additive, 0.08f)),

        // Lifesteal scales with the proc coefficient, so this is worth roughly
        // twice as much on the Mortar as on the Halo. Capped at six because
        // unbounded sustain removes the only real failure state in the run.
        Item("vampiric_fang", "Vampiric Fang", Rarity.Uncommon, 0.65f, 6,
            "Still warm. Whatever it takes from them, it gives most of back to you.",
            new Color(0.86f, 0.30f, 0.42f),
            Effect(Stat.Lifesteal, ModifierOp.Flat, 0.025f)),

        // -------------------------------------------------------------- Rare

        // +1 projectile on every weapon is enormous — it is a flat multiplier on
        // the whole arsenal — so it is sold with a real attack-speed cost and a
        // cap of three. Taking all three is a legitimate build; taking six would
        // be the only build.
        Item("splitshot_core", "Splitshot Core", Rarity.Rare, 0.45f, 3,
            "It insists on doing everything twice. It is not quick about it.",
            new Color(0.55f, 0.75f, 1.00f),
            Effect(Stat.ProjectileCount, ModifierOp.Flat, 1f),
            Effect(Stat.AttackSpeed, ModifierOp.Additive, -0.09f)),

        // Thorns is the only stat that turns being surrounded into an advantage,
        // which is why it comes bundled with armour and a speed penalty: the item
        // wants you to stand still, so it had better help you survive doing it.
        Item("thornmail_coat", "Thornmail Coat", Rarity.Rare, 0.45f, 0,
            "Worn barbed side out. Crowding you becomes its own punishment.",
            new Color(0.72f, 0.68f, 0.60f),
            Effect(Stat.Thorns, ModifierOp.Flat, 14f),
            Effect(Stat.Armor, ModifierOp.Flat, 8f),
            Effect(Stat.MoveSpeed, ModifierOp.Additive, -0.05f)),

        // Doubles the base crit chance and adds 40% to the multiplier: about
        // +20% dps by itself, and the piece that makes a crit build tip over.
        // The health cost is flat, so it hurts most in the first two minutes —
        // which is the right time for a greedy item to be dangerous.
        Item("duelists_edge", "Duelist's Edge", Rarity.Rare, 0.45f, 0,
            "Balanced for one exchange rather than for a war. It has opinions about where to strike.",
            new Color(1.00f, 0.84f, 0.46f),
            Effect(Stat.CritChance, ModifierOp.Flat, 0.10f),
            Effect(Stat.CritDamage, ModifierOp.Additive, 0.40f),
            Effect(Stat.MaxHealth, ModifierOp.Flat, -12f)),

        // Knockback is control, not damage, and control is what keeps a melee or
        // aura build alive past minute ten. Priced as a Rare because it changes
        // how a fight is fought rather than how fast it ends.
        Item("gravity_well_anchor", "Gravity-Well Anchor", Rarity.Rare, 0.45f, 0,
            "Heavy in a way that has nothing to do with its size. Things near it are pushed further than they expect.",
            new Color(0.66f, 0.56f, 0.92f),
            Effect(Stat.Knockback, ModifierOp.Additive, 0.60f),
            Effect(Stat.AreaSize, ModifierOp.Additive, 0.18f),
            Effect(Stat.ProjectileSpeed, ModifierOp.Additive, -0.10f)),

        // The economy item. Nothing here is damage, and all of it is more
        // damage later — luck biases the offer pool, xp brings the level-ups
        // forward, gold buys whatever gold buys.
        Item("magpies_eye", "Magpie's Eye", Rarity.Rare, 0.45f, 0,
            "You notice the good ones now. You cannot stop noticing them.",
            new Color(0.94f, 0.88f, 0.52f),
            Effect(Stat.Luck, ModifierOp.Flat, 2f),
            Effect(Stat.GoldGain, ModifierOp.Additive, 0.30f),
            Effect(Stat.XpGain, ModifierOp.Additive, 0.15f)),

        // -------------------------------------------------------------- Epic

        // The first Multiplicative in the table. x1.18 compounds with itself and
        // with every additive attack-speed item, which is exactly the explosive
        // behaviour Multiplicative is reserved for — hence the cap of three and
        // a regen cost that a Field Rations stack has to answer.
        Item("overclocked_heart", "Overclocked Heart", Rarity.Epic, 0.25f, 3,
            "It beats considerably faster than yours did. It does not intend to slow down.",
            new Color(1.00f, 0.42f, 0.48f),
            Effect(Stat.AttackSpeed, ModifierOp.Multiplicative, 1.18f),
            Effect(Stat.HealthRegen, ModifierOp.Flat, -3f)),

        // Evasion +22 is 35% dodge on the curve, which combined with a filled
        // shield makes a genuinely evasive build possible for the first time. The
        // max-health cost is percentage-based so it scales with the build it
        // enables rather than being a flat early tax.
        Item("phase_cloak", "Phase Cloak", Rarity.Epic, 0.25f, 2,
            "Half of you is reliably somewhere else. The half that stays is thinner for it.",
            new Color(0.62f, 0.86f, 1.00f),
            Effect(Stat.Evasion, ModifierOp.Flat, 22f),
            Effect(Stat.Shield, ModifierOp.Flat, 25f),
            Effect(Stat.MaxHealth, ModifierOp.Additive, -0.15f)),

        // x1.20 damage against a -12% speed penalty: the clean trade of the
        // defensive layer for the offensive one. Three of these is x1.73 damage
        // and -36% speed, which is a real build and a real way to die.
        Item("siege_battery", "Siege Battery", Rarity.Epic, 0.25f, 3,
            "Enormous output, appalling portability. You will be arriving late to everything.",
            new Color(1.00f, 0.72f, 0.30f),
            Effect(Stat.Damage, ModifierOp.Multiplicative, 1.20f),
            Effect(Stat.MoveSpeed, ModifierOp.Additive, -0.12f)),

        // 6% lifesteal is more sustain than the entire regen line can offer, and
        // the -20% max health makes that sustain necessary rather than optional.
        // The point is a build that cannot afford to stop killing.
        Item("blood_pact", "Blood Pact", Rarity.Epic, 0.25f, 2,
            "The terms are simple and entirely one-sided until the moment you stop winning.",
            new Color(0.82f, 0.20f, 0.30f),
            Effect(Stat.Lifesteal, ModifierOp.Flat, 0.06f),
            Effect(Stat.Damage, ModifierOp.Additive, 0.25f),
            Effect(Stat.MaxHealth, ModifierOp.Additive, -0.20f)),

        // --------------------------------------------------------- Legendary

        // +2 projectiles is a flat multiplier on every weapon at once, and the
        // multiplicative attack speed multiplies that again. This is the "the run
        // is now about volume" item, and one is the correct number of them.
        Item("heart_of_the_storm", "Heart of the Storm", Rarity.Legendary, 0.10f, 1,
            "It has been raining inside this thing for a very long time, and it has just found somewhere to fall.",
            new Color(0.78f, 0.90f, 1.00f),
            Effect(Stat.ProjectileCount, ModifierOp.Flat, 2f),
            Effect(Stat.AttackSpeed, ModifierOp.Multiplicative, 1.25f),
            Effect(Stat.CritChance, ModifierOp.Flat, 0.08f)),

        // The only item that touches EnemyScaling, and the reason that stat
        // exists. x1.35 scaling raises enemy health, damage and the director's
        // whole credit rate; x1.45 damage and +60% to both economies is meant to
        // outpace it — but only if the build is already working. Taking this at
        // minute two is a different decision from taking it at minute ten, and
        // that is the entire design.
        Item("crown_of_ruin", "Crown of Ruin", Rarity.Legendary, 0.10f, 1,
            "It fits perfectly. Everything outside now knows exactly where you are, and how much you are worth.",
            new Color(1.00f, 0.36f, 0.30f),
            Effect(Stat.Damage, ModifierOp.Multiplicative, 1.45f),
            Effect(Stat.EnemyScaling, ModifierOp.Multiplicative, 1.35f),
            Effect(Stat.XpGain, ModifierOp.Additive, 0.60f),
            Effect(Stat.GoldGain, ModifierOp.Additive, 0.60f),
            Effect(Stat.Luck, ModifierOp.Flat, 3f)),

        // ================================================ second wave of items
        //
        // Written to give the first twenty-four something to *combine with*.
        // Four build lines run through them, and each line has a Common to
        // start it, something in the middle that commits to it, and a capped
        // top end that pays for itself with a real cost:
        //
        //   thorns    Barnacle Crust -> Ballast Greaves / Coral Bastion -> Leviathan Hide
        //   crit      Gutting Hook -> Gambler's Coin -> Headsman's Oath
        //   area      Tidepool Lantern -> Spiral Conch -> (Rimeheart, Static Field)
        //   movement  Gull Feather / Bosun's Whistle -> Riptide Anklet -> Stormrunner Treads -> Windborne Kite
        //
        // Several of them are trades rather than gifts. A trade is what makes an
        // item build-defining: a -20% projectile speed on the Spiral Conch is a
        // free pick for an aura build and a real decision for a Lance build, and
        // that difference is the whole point of the item.

        // ------------------------------------------------------------ Common

        // The movement Common. Jump height is the stat that decides whether a
        // player can hop a pack instead of going round it; the speed rider is
        // small so this stays a movement item rather than a second Springheel.
        Item("gull_feather", "Gull Feather", Rarity.Common, 1.0f, 0,
            "Light enough to forget you are carrying it. The ground seems to forget you too, for a moment longer.",
            new Color(0.92f, 0.94f, 0.96f),
            Effect(Stat.JumpHeight, ModifierOp.Additive, 0.30f),
            Effect(Stat.MoveSpeed, ModifierOp.Additive, 0.04f)),

        // Dash cooldown as a Common. -14% additive on a 2.5s base is about a
        // third of a second per copy, and the pickup rider means a player who
        // dashes through the orb field collects more of it.
        Item("bosuns_whistle", "Bosun's Whistle", Rarity.Common, 1.0f, 0,
            "One sharp note and your legs remember they had somewhere to be.",
            new Color(0.84f, 0.80f, 0.62f),
            Effect(Stat.DashCooldown, ModifierOp.Additive, -0.14f),
            Effect(Stat.PickupRange, ModifierOp.Additive, 0.15f)),

        // The crit Common. Crit damage alone is worth little at 5% base chance
        // and a great deal on Glasswire Needles or Riptide Blades — which is the
        // shape of a build-starter: weak in general, strong in the right hands.
        Item("gutting_hook", "Gutting Hook", Rarity.Common, 1.0f, 0,
            "Curved for one job. When something does come apart, it comes apart further.",
            new Color(0.88f, 0.70f, 0.62f),
            Effect(Stat.CritDamage, ModifierOp.Additive, 0.20f)),

        // The thorns Common. 6 thorns returns six damage (scaled by the damage
        // stat) to every enemy that swings at you, which is nothing on a kiting
        // build and a quiet aura on one that stands in the crowd.
        Item("barnacle_crust", "Barnacle Crust", Rarity.Common, 1.0f, 0,
            "It grew on you while you weren't looking. Anything that grabs you comes away bleeding.",
            new Color(0.70f, 0.66f, 0.58f),
            Effect(Stat.Thorns, ModifierOp.Flat, 6f),
            Effect(Stat.Armor, ModifierOp.Flat, 2f)),

        // Regen and shield together: the Common that rewards breaking contact.
        // 5/min regen is close to the base rate again, and 6 shield refills on
        // its own every time the player gets six quiet seconds.
        Item("salted_bandages", "Salted Bandages", Rarity.Common, 1.0f, 0,
            "They sting going on. They work. Both of those are the point.",
            new Color(0.94f, 0.90f, 0.80f),
            Effect(Stat.HealthRegen, ModifierOp.Flat, 5f),
            Effect(Stat.Shield, ModifierOp.Flat, 6f)),

        // ---------------------------------------------------------- Uncommon

        // The dash item. -20% dash cooldown with a little speed and evasion:
        // a player who is always about to dash is a player who is rarely hit.
        Item("riptide_anklet", "Riptide Anklet", Rarity.Uncommon, 0.7f, 0,
            "A cord of braided current around the ankle. It pulls, and you go with it.",
            new Color(0.46f, 0.82f, 0.90f),
            Effect(Stat.DashCooldown, ModifierOp.Additive, -0.20f),
            Effect(Stat.MoveSpeed, ModifierOp.Additive, 0.05f),
            Effect(Stat.Evasion, ModifierOp.Flat, 3f)),

        // The anti-movement item, and the thorns build's commitment piece. Big
        // armour and thorns, paid for in speed and jump — exactly the stats a
        // thorns build was not going to use, and exactly the ones a kiting build
        // cannot give up. One card, two completely different answers.
        Item("ballast_greaves", "Ballast Greaves", Rarity.Uncommon, 0.7f, 0,
            "Lead in the soles. You are not going anywhere, and nothing that reaches you leaves unhurt.",
            new Color(0.56f, 0.58f, 0.64f),
            Effect(Stat.Armor, ModifierOp.Flat, 12f),
            Effect(Stat.Thorns, ModifierOp.Flat, 6f),
            Effect(Stat.MoveSpeed, ModifierOp.Additive, -0.08f),
            Effect(Stat.JumpHeight, ModifierOp.Additive, -0.20f)),

        // Area with a pickup rider. Pure value for an aura or explosive build,
        // and the pickup range keeps it from being a dead card in a rail build.
        Item("tidepool_lantern", "Tidepool Lantern", Rarity.Uncommon, 0.7f, 0,
            "A jar of water that remembers the moon. Its light reaches a little further than it should.",
            new Color(0.56f, 0.84f, 0.96f),
            Effect(Stat.AreaSize, ModifierOp.Additive, 0.18f),
            Effect(Stat.PickupRange, ModifierOp.Additive, 0.25f)),

        // A small, cheap luck item. Luck is the only stat that improves future
        // level-ups rather than this one, so it is priced as an investment.
        Item("tarnished_compass", "Tarnished Compass", Rarity.Uncommon, 0.7f, 0,
            "It does not point north. It points at whatever is worth having, which is more useful.",
            new Color(0.82f, 0.72f, 0.42f),
            Effect(Stat.Luck, ModifierOp.Flat, 1f),
            Effect(Stat.PickupRange, ModifierOp.Additive, 0.15f),
            Effect(Stat.GoldGain, ModifierOp.Additive, 0.10f)),

        // Lifesteal in exchange for regeneration: sustain moves from "time" to
        // "kills". Good on a build that is always hitting, bad on one that runs.
        // Capped for the same reason as the Fang.
        Item("lamprey_ring", "Lamprey Ring", Rarity.Uncommon, 0.6f, 4,
            "It latches on to whatever you hit. It stops feeding you the moment you stop hitting.",
            new Color(0.72f, 0.36f, 0.48f),
            Effect(Stat.Lifesteal, ModifierOp.Flat, 0.015f),
            Effect(Stat.HealthRegen, ModifierOp.Flat, -3f)),

        // -------------------------------------------------------------- Rare

        // The kiting Rare: real speed and dash, sold for armour. A player who is
        // never touched does not need armour — and one who is, very much does.
        Item("stormrunner_treads", "Stormrunner Treads", Rarity.Rare, 0.45f, 0,
            "Soled with something that crackles when it touches ground. You leave before the thunder does.",
            new Color(0.64f, 0.74f, 1.00f),
            Effect(Stat.MoveSpeed, ModifierOp.Additive, 0.16f),
            Effect(Stat.DashCooldown, ModifierOp.Additive, -0.15f),
            Effect(Stat.Armor, ModifierOp.Flat, -6f)),

        // The crit build's middle piece. +12% crit is more than the Duelist's
        // Edge, sold for a slice of base damage: it is a strict loss on a build
        // that does not crit, and a large gain on one with crit damage behind it.
        Item("gamblers_coin", "Gambler's Coin", Rarity.Rare, 0.45f, 0,
            "Both sides are heads. You are no longer sure which side you are on.",
            new Color(0.96f, 0.82f, 0.36f),
            Effect(Stat.CritChance, ModifierOp.Flat, 0.12f),
            Effect(Stat.Luck, ModifierOp.Flat, 1f),
            Effect(Stat.Damage, ModifierOp.Additive, -0.08f)),

        // Big area for slow projectiles. Free on an aura build, real on a mortar
        // build (bigger blast, slower shell), and a trap on a Lance build.
        Item("spiral_conch", "Spiral Conch", Rarity.Rare, 0.45f, 0,
            "Hold it to your ear and you hear the sea. Hold it out and the sea hears you.",
            new Color(0.96f, 0.76f, 0.70f),
            Effect(Stat.AreaSize, ModifierOp.Additive, 0.32f),
            Effect(Stat.ProjectileSpeed, ModifierOp.Additive, -0.20f)),

        // The thorns build's Rare. Thorns and a large flat health pool, paid for
        // in attack speed: the item wants enemies to come to you, so it does not
        // mind that your weapons slow down while they do.
        Item("coral_bastion", "Coral Bastion", Rarity.Rare, 0.45f, 0,
            "A living wall that grew around your ribs. It is sharp on the outside and very patient.",
            new Color(0.96f, 0.52f, 0.50f),
            Effect(Stat.Thorns, ModifierOp.Flat, 14f),
            Effect(Stat.MaxHealth, ModifierOp.Flat, 25f),
            Effect(Stat.AttackSpeed, ModifierOp.Additive, -0.08f)),

        // The small Crown. More gold and xp for a slightly stronger world; three
        // copies is +24% enemy strength, which is a fair price for +120% gold.
        Item("bounty_ledger", "Bounty Ledger", Rarity.Rare, 0.45f, 3,
            "Every name in it has a price. Somebody out there has just added yours.",
            new Color(0.86f, 0.78f, 0.56f),
            Effect(Stat.GoldGain, ModifierOp.Additive, 0.40f),
            Effect(Stat.XpGain, ModifierOp.Additive, 0.15f),
            Effect(Stat.EnemyScaling, ModifierOp.Multiplicative, 1.08f)),

        // -------------------------------------------------------------- Epic

        // The crit build's top end: x1.5 on crit damage, so a +100% crit becomes
        // +150% and every Gutting Hook behind it is multiplied too. The attack
        // speed cost means it only pays if the build crits often already.
        Item("headsmans_oath", "Headsman's Oath", Rarity.Epic, 0.25f, 2,
            "A promise to finish every stroke you begin. You begin fewer of them.",
            new Color(0.70f, 0.30f, 0.34f),
            Effect(Stat.CritDamage, ModifierOp.Multiplicative, 1.5f),
            Effect(Stat.AttackSpeed, ModifierOp.Additive, -0.12f)),

        // The movement Epic: the only multiplicative speed in the game. x1.15
        // compounds with every additive speed item, and the max-health cost is a
        // percentage so it bites hardest on the build that relies on not being
        // hit at all.
        Item("windborne_kite", "Windborne Kite", Rarity.Epic, 0.25f, 2,
            "Tethered at the wrist and always pulling. You are lighter than you were, in every sense.",
            new Color(0.76f, 0.94f, 1.00f),
            Effect(Stat.MoveSpeed, ModifierOp.Multiplicative, 1.15f),
            Effect(Stat.JumpHeight, ModifierOp.Additive, 0.40f),
            Effect(Stat.DashCooldown, ModifierOp.Additive, -0.25f),
            Effect(Stat.MaxHealth, ModifierOp.Additive, -0.10f)),

        // +1 projectile sold for damage instead of attack speed — the Splitshot
        // Core's sibling. Which one is better depends on the weapon: on a
        // seeker or the needles, count beats size; on the Maul it does nothing
        // but widen the arc, and the damage cost is real.
        Item("twin_tide_quiver", "Twin-Tide Quiver", Rarity.Epic, 0.25f, 2,
            "Everything you loose from it comes out in pairs. Neither of the pair is quite whole.",
            new Color(0.52f, 0.70f, 0.96f),
            Effect(Stat.ProjectileCount, ModifierOp.Flat, 1f),
            Effect(Stat.Damage, ModifierOp.Additive, -0.12f)),

        // --------------------------------------------------------- Legendary

        // The thorns Legendary. x2.5 on every point of thorns the build has and
        // will have, plus enough armour to survive being the bait. The speed cost
        // is the commitment: this run is now about being reached.
        Item("leviathan_hide", "Leviathan Hide", Rarity.Legendary, 0.10f, 1,
            "Flayed from something that was never killed, only outlasted. It still resents being touched.",
            new Color(0.40f, 0.66f, 0.70f),
            Effect(Stat.Thorns, ModifierOp.Flat, 10f),
            Effect(Stat.Thorns, ModifierOp.Multiplicative, 2.5f),
            Effect(Stat.Armor, ModifierOp.Flat, 20f),
            Effect(Stat.MoveSpeed, ModifierOp.Additive, -0.15f)),

        // The glass-cannon Legendary. x1.35 attack speed and x1.12 damage is the
        // biggest offensive multiplier in the game, and it takes away a quarter
        // of your health and all of your base regeneration to get it. A run that
        // takes this has to stop being hit, or start stealing life.
        Item("abyssal_metronome", "Abyssal Metronome", Rarity.Legendary, 0.10f, 1,
            "It keeps time for something very large, very far down. Everything you do now happens on its beat.",
            new Color(0.36f, 0.30f, 0.70f),
            Effect(Stat.AttackSpeed, ModifierOp.Multiplicative, 1.35f),
            Effect(Stat.Damage, ModifierOp.Multiplicative, 1.12f),
            Effect(Stat.MaxHealth, ModifierOp.Additive, -0.25f),
            Effect(Stat.HealthRegen, ModifierOp.Flat, -6f)),
    };

    // ------------------------------------------------------------- builders
    // Two small helpers so that an item reads as one statement. Forty-odd
    // items written as object initialisers with nested Godot arrays is a page of
    // punctuation, and the balance numbers stop being visible in it.

    private static Godot.Collections.Array<BossAttack> Moves(params BossAttack[] moves)
        => new(moves);

    private static ItemEffect Effect(Stat stat, ModifierOp op, float perStack)
        => new() { Stat = stat, Op = op, PerStack = perStack };

    /// <summary>
    /// Stack cap for items that did not set one. "0 = unlimited" let a single
    /// +30%-gold item be taken a dozen times; with real income bonuses that ran
    /// the economy away. Rarer items stack less because each copy is stronger.
    /// </summary>
    public static int DefaultStackCap(Rarity rarity) => rarity switch
    {
        Rarity.Common => 8,
        Rarity.Uncommon => 6,
        Rarity.Rare => 4,
        Rarity.Epic => 3,
        _ => 1,
    };

    private static ItemDef Item(
        string id, string name, Rarity rarity, float weight, int maxStacks,
        string description, Color tint, params ItemEffect[] effects)
    {
        var array = new Godot.Collections.Array<ItemEffect>();
        foreach (ItemEffect effect in effects) array.Add(effect);

        return new ItemDef
        {
            Id = id,
            DisplayName = name,
            Description = description,
            Rarity = rarity,
            Effects = array,
            MaxStacks = maxStacks > 0 ? maxStacks : DefaultStackCap(rarity),
            Weight = weight,
            Tint = tint,
        };
    }
}
