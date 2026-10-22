using System.Collections.Generic;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Data;
using Wavebreak.Stats;

namespace Wavebreak.Meta;

/// <summary>
/// Content that starts locked and is earned through <see cref="Unlocks"/>.
///
/// Kept out of <see cref="Catalog"/> on purpose. The catalogue is the base game
/// and stays whole: a new player is offered exactly what they were offered
/// before this layer existed. Unlocks only ever <em>add</em> to the pool, so the
/// fairness rule ("nothing you had is taken away to be sold back to you") is
/// enforced by where the content lives, not by a flag someone can forget.
///
/// Same balance rules as the catalogue (see the ITEMS header there): one effect
/// at ~10% for a Common, Multiplicative only from Epic, Legendaries are trades.
/// Earned content is deliberately sideways rather than stronger — an unlock that
/// is strictly better than base content turns the codex into a grind wall.
/// </summary>
public static class MetaContent
{
    public static readonly IReadOnlyList<WeaponDef> Weapons = BuildWeapons();
    public static readonly IReadOnlyList<ItemDef> Items = BuildItems();

    private static readonly Dictionary<string, WeaponDef> WeaponsById = Index(Weapons, w => w.Id);
    private static readonly Dictionary<string, ItemDef> ItemsById = Index(Items, i => i.Id);

    public static WeaponDef? WeaponById(string? id)
        => id is not null && WeaponsById.TryGetValue(id, out WeaponDef? d) ? d : null;

    public static ItemDef? ItemById(string? id)
        => id is not null && ItemsById.TryGetValue(id, out ItemDef? d) ? d : null;

    /// <summary>
    /// The item list a run's offer pool should draw from: the whole base catalogue
    /// plus whatever locked items this profile has earned.
    /// </summary>
    public static List<ItemDef> OfferItems(IReadOnlyList<ItemDef> catalogue)
    {
        var list = new List<ItemDef>(catalogue);
        foreach (ItemDef def in Items)
            if (Unlocks.IsContentUnlocked(def.Id)) list.Add(def);
        return list;
    }

    /// <summary>Weapon counterpart of <see cref="OfferItems"/>.</summary>
    public static List<WeaponDef> OfferWeapons(IReadOnlyList<WeaponDef> catalogue)
    {
        var list = new List<WeaponDef>(catalogue);
        foreach (WeaponDef def in Weapons)
            if (Unlocks.IsContentUnlocked(def.Id)) list.Add(def);
        return list;
    }

    /// <summary>Display name for any id this layer can grant, base or locked.</summary>
    public static string NameOf(string id)
        => WeaponById(id)?.DisplayName ?? ItemById(id)?.DisplayName
           ?? Catalog.WeaponById(id)?.DisplayName ?? Catalog.ItemById(id)?.DisplayName ?? id;

    private static Dictionary<string, T> Index<T>(IReadOnlyList<T> list, System.Func<T, string> key)
    {
        var map = new Dictionary<string, T>();
        foreach (T t in list) map[key(t)] = t;
        return map;
    }

    // =====================================================================
    //  WEAPONS — each fills a gap in the base roster's shapes rather than
    //  repeating one, so earning it changes how a run can play.
    // =====================================================================

    private static List<WeaponDef> BuildWeapons() => new()
    {
        // A short, very wide melee that drags: low damage, huge knockback
        // reversed in feel by its tiny cooldown. The "never let them touch you"
        // weapon, earned by the player who has already killed a thousand things.
        new WeaponDef
        {
            Id = "undertow_chain",
            DisplayName = "Undertow Chain",
            Description = "A length of anchor chain swung low. It does not cut so much as insist.",
            Rarity = Rarity.Uncommon,
            Shape = AttackShape.Melee,
            Element = Element.Ice,
            Damage = 9f,
            Cooldown = 0.55f,       // 16.4 dps per body, like the starter
            Projectiles = 1,
            ProjectileSpeed = 0f,
            Range = 3.4f,
            Radius = 3.4f,
            Pierce = 0,
            Knockback = 5.5f,
            CritChance = 0.03f,
            ProcCoefficient = 0.6f,
            SpreadDegrees = 150f,   // arc width for melee
            Tint = new Color(0.58f, 0.80f, 0.90f),
            DamagePerLevel = 0.26f,
            CooldownPerLevel = -0.05f,
            ProjectilesEveryNLevels = 0,
            MaxLevel = 8,
        },

        // Slow homing with a big blast: the only seeker that explodes. Priced like
        // Mortar Shell (long cooldown, high single number) because it is Mortar
        // with the aiming removed.
        new WeaponDef
        {
            Id = "lantern_moths",
            DisplayName = "Lantern Moths",
            Description = "Paper-winged lights that drift toward warmth and burst when they find it.",
            Rarity = Rarity.Rare,
            Shape = AttackShape.Homing,
            Element = Element.Fire,
            Damage = 15f,
            Cooldown = 1.5f,        // 20 dps over two moths, less after misses
            Projectiles = 2,
            ProjectileSpeed = 11f,
            Range = 24f,
            Radius = 0.4f,
            Pierce = 0,
            Knockback = 1.2f,
            CritChance = 0.04f,
            ProcCoefficient = 0.8f,
            SpreadDegrees = 60f,
            Tint = new Color(1.00f, 0.80f, 0.45f),
            DamagePerLevel = 0.25f,
            CooldownPerLevel = -0.06f,
            ProjectilesEveryNLevels = 3,
            MaxLevel = 8,
        },

        // A pulse aura with the widest reach in the game and the slowest beat.
        // Earned for surviving ten minutes, which is when the arena is full
        // enough for a slow, enormous circle to be worth the wait.
        new WeaponDef
        {
            Id = "quarry_bell",
            DisplayName = "Quarry Bell",
            Description = "Rung once, it shakes the ground for a long way round. Then you wait for it to stop humming.",
            Rarity = Rarity.Rare,
            Shape = AttackShape.Aura,
            Element = Element.Physical,
            Damage = 30f,
            Cooldown = 2.8f,        // 10.7 dps per body over a very large circle
            Projectiles = 1,
            ProjectileSpeed = 0f,
            Range = 8f,
            Radius = 8f,
            Pierce = 0,
            Knockback = 4f,
            CritChance = 0.03f,
            ProcCoefficient = 1.2f,
            SpreadDegrees = 0f,
            Tint = new Color(0.86f, 0.78f, 0.62f),
            DamagePerLevel = 0.30f,
            CooldownPerLevel = -0.06f,
            ProjectilesEveryNLevels = 0,
            MaxLevel = 8,
        },
    };

    // =====================================================================
    //  ITEMS
    // =====================================================================

    private static List<ItemDef> BuildItems() => new()
    {
        // ---- Common
        Item("tidemark_charm", "Tidemark Charm", Rarity.Common, 1.0f, 0,
            "A pebble with a line worn round it where the water stopped. You hit and move a little harder.",
            new Color(0.56f, 0.82f, 0.86f),
            Effect(Stat.Damage, ModifierOp.Additive, 0.06f),
            Effect(Stat.MoveSpeed, ModifierOp.Additive, 0.04f)),

        Item("salvagers_hook", "Salvager's Hook", Rarity.Common, 1.0f, 0,
            "Long-handled and patient. Whatever drops, it reaches.",
            new Color(0.74f, 0.70f, 0.58f),
            Effect(Stat.PickupRange, ModifierOp.Additive, 0.40f),
            Effect(Stat.GoldGain, ModifierOp.Additive, 0.12f)),

        Item("salt_rusted_keyring", "Salt-Rusted Keyring", Rarity.Common, 0.9f, 0,
            "None of the keys fit anything any more. The luck of carrying them still does.",
            new Color(0.80f, 0.56f, 0.40f),
            Effect(Stat.GoldGain, ModifierOp.Additive, 0.20f),
            Effect(Stat.Luck, ModifierOp.Flat, 0.2f)),

        // ---- Uncommon
        Item("stillwater_flask", "Stillwater Flask", Rarity.Uncommon, 0.7f, 0,
            "Water from somewhere nothing has ever moved. It settles you.",
            new Color(0.60f, 0.86f, 0.98f),
            Effect(Stat.MaxHealth, ModifierOp.Flat, 25f),
            Effect(Stat.HealthRegen, ModifierOp.Flat, 4f)),

        Item("anvil_soles", "Anvil Soles", Rarity.Uncommon, 0.7f, 0,
            "Iron under the heel. Everything you land on, and everything you hit, moves further.",
            new Color(0.58f, 0.60f, 0.66f),
            Effect(Stat.Armor, ModifierOp.Flat, 5f),
            Effect(Stat.Knockback, ModifierOp.Additive, 0.20f)),

        Item("packrats_satchel", "Packrat's Satchel", Rarity.Uncommon, 0.7f, 0,
            "Overstuffed, mismatched, and never quite closed. Everything is useful eventually.",
            new Color(0.82f, 0.66f, 0.44f),
            Effect(Stat.XpGain, ModifierOp.Additive, 0.10f),
            Effect(Stat.GoldGain, ModifierOp.Additive, 0.10f),
            Effect(Stat.PickupRange, ModifierOp.Additive, 0.20f)),

        Item("gilded_tooth", "Gilded Tooth", Rarity.Uncommon, 0.6f, 0,
            "Somebody's investment. The odds lean slightly toward whoever is carrying it.",
            new Color(1.00f, 0.84f, 0.36f),
            Effect(Stat.GoldGain, ModifierOp.Additive, 0.25f),
            Effect(Stat.Luck, ModifierOp.Flat, 0.5f)),

        // ---- Rare
        Item("resonant_fork", "Resonant Fork", Rarity.Rare, 0.45f, 0,
            "Struck once, it never quite stops. Everything you hold falls into its rhythm.",
            new Color(0.84f, 0.88f, 0.96f),
            Effect(Stat.AttackSpeed, ModifierOp.Additive, 0.12f),
            Effect(Stat.CritChance, ModifierOp.Flat, 0.04f)),

        Item("moonwell_drop", "Moonwell Drop", Rarity.Rare, 0.45f, 0,
            "A bead of something that glows faintly in the dark. It was waiting where nobody looks.",
            new Color(0.78f, 0.80f, 1.00f),
            Effect(Stat.MaxHealth, ModifierOp.Flat, 15f),
            Effect(Stat.HealthRegen, ModifierOp.Flat, 6f),
            Effect(Stat.XpGain, ModifierOp.Additive, 0.10f)),

        Item("gullbone_whistle", "Gullbone Whistle", Rarity.Rare, 0.45f, 0,
            "Blow it and the air remembers being wind. You go up further and come down later.",
            new Color(0.92f, 0.92f, 0.86f),
            Effect(Stat.JumpHeight, ModifierOp.Additive, 0.30f),
            Effect(Stat.MoveSpeed, ModifierOp.Additive, 0.08f)),

        // ---- Epic
        Item("unbruised_feather", "Unbruised Feather", Rarity.Epic, 0.25f, 3,
            "It has been through everything you have, and there is not a mark on it.",
            new Color(0.96f, 0.98f, 1.00f),
            Effect(Stat.Evasion, ModifierOp.Flat, 12f),
            Effect(Stat.MoveSpeed, ModifierOp.Additive, 0.10f)),

        Item("mirror_scale", "Mirror Scale", Rarity.Epic, 0.25f, 2,
            "Shed by something that was never touched. Whatever you strike sees itself first.",
            new Color(0.80f, 0.94f, 0.98f),
            // AttackSpeed, not Damage: Damage is a bonus stat with base 0, and a
            // multiplier on (0 + additives) scales the bonus rather than the hit.
            Effect(Stat.AttackSpeed, ModifierOp.Multiplicative, 1.10f),
            Effect(Stat.Armor, ModifierOp.Flat, 8f)),

        Item("wreckers_lantern", "Wrecker's Lantern", Rarity.Epic, 0.25f, 2,
            "Hung on the wrong rock to bring things in. Everything you fire has company; none of it hits as hard.",
            new Color(1.00f, 0.62f, 0.30f),
            Effect(Stat.ProjectileCount, ModifierOp.Flat, 1f),
            Effect(Stat.Damage, ModifierOp.Additive, -0.08f)),

        // ---- Legendary: a trade, like every legendary.
        Item("drowned_crown", "Drowned Crown", Rarity.Legendary, 0.10f, 1,
            "Worn by whoever won the last flood. Fortune follows it, and so does the tide.",
            new Color(0.40f, 0.86f, 0.78f),
            Effect(Stat.Luck, ModifierOp.Flat, 2f),
            Effect(Stat.EnemyScaling, ModifierOp.Additive, 0.15f)),
    };

    private static ItemEffect Effect(Stat stat, ModifierOp op, float perStack)
        => new() { Stat = stat, Op = op, PerStack = perStack };

    private static ItemDef Item(string id, string name, Rarity rarity, float weight, int maxStacks,
                                string description, Color tint, params ItemEffect[] effects)
    {
        var array = new Godot.Collections.Array<ItemEffect>();
        foreach (ItemEffect e in effects) array.Add(e);
        return new ItemDef
        {
            Id = id, DisplayName = name, Description = description, Rarity = rarity,
            Weight = weight, MaxStacks = maxStacks, Tint = tint, Effects = array,
        };
    }
}
