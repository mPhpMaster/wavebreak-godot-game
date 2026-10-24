using System.Collections.Generic;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Stats;

namespace Wavebreak.Data;

/// <summary>
/// The content expansion: twelve weapons and forty-three items, appended to
/// the base tables by the static constructor below rather than written into
/// them, so the original lists (and anyone else editing them) stay untouched.
///
/// The weapons each own a delivery the first eighteen do not have — boomerang,
/// orbiting blades, ground hazards, chains, beams, mines, a sentry, a vortex,
/// ricochets, a breath cone, sky strikes and an expanding ring — implemented in
/// <c>Combat/Weapons/WeaponBehaviors.cs</c> on top of the pooled projectiles.
/// Their <see cref="AttackShape"/> is the nearest existing category, which is
/// what the level-up card, audio and codex read.
///
/// Items follow the base table's rarity rules. Many carry a rule as well as a
/// stat (on hit, on kill, when hit, while airborne or sliding, on chest or
/// shrine use, from gold); the rule lives in <c>Combat/Weapons/ItemProcs.cs</c>
/// and the stat rider is what the card's effect line shows. The description
/// states the rule in plain numbers per stack.
/// </summary>
public static partial class Catalog
{
    static Catalog()
    {
        foreach (WeaponDef w in ExpansionWeapons())
        {
            WeaponList.Add(w);
            WeaponsById[w.Id] = w;
        }
        foreach (ItemDef i in ExpansionItems())
        {
            ItemList.Add(i);
            ItemsById[i.Id] = i;
        }
        AddSignatureWeapons();   // Data/Catalog.Heroes.cs
    }

    /// <summary>Ids added by this file, for the expansion audit.</summary>
    public static readonly HashSet<string> ExpansionIds = new();

    private static WeaponDef W(WeaponDef def) { ExpansionIds.Add(def.Id); return def; }
    private static ItemDef X(ItemDef def) { ExpansionIds.Add(def.Id); return def; }

    // =====================================================================
    //  WEAPONS (12) — same 15-18 paper-dps band; lingering ones are priced on
    //  what lands, not on the paper number.
    // =====================================================================

    private static List<WeaponDef> ExpansionWeapons() => new()
    {
        // Boomerang: out and back, so every body in the lane is hit twice.
        W(new WeaponDef
        {
            Id = "gullwing_chakram", DisplayName = "Gullwing Chakram",
            Description = "A hooked ring of whalebone. It goes out, it hangs, and it comes home through everything it missed.",
            Rarity = Rarity.Uncommon, Shape = AttackShape.Projectile, Element = Element.Physical,
            Damage = 14f, Cooldown = 1.6f, Projectiles = 1, ProjectileSpeed = 16f, Range = 12f, Radius = 0.6f,
            Knockback = 2f, CritChance = 0.05f, ProcCoefficient = 0.7f, SpreadDegrees = 30f,
            Tint = new Color(0.92f, 0.94f, 0.86f),
            DamagePerLevel = 0.25f, CooldownPerLevel = -0.05f, ProjectilesEveryNLevels = 3, MaxLevel = 8,
        }),

        // Orbiting blades: a ring of fins round the player for most of the cooldown.
        // Speed is tangential m/s, Range is the orbit radius.
        W(new WeaponDef
        {
            Id = "circling_shoal", DisplayName = "Circling Shoal",
            Description = "Knife-finned fish of cold glass that will not leave your side. Anything that stands close is cut on every pass.",
            Rarity = Rarity.Rare, Shape = AttackShape.Aura, Element = Element.Ice,   // element: Freeze status fits here
            Damage = 9f, Cooldown = 3.2f, Projectiles = 3, ProjectileSpeed = 10f, Range = 2.4f, Radius = 0.55f,
            Knockback = 2.5f, CritChance = 0.05f, ProcCoefficient = 0.5f, SpreadDegrees = 0f,
            Tint = new Color(0.60f, 0.88f, 1.00f),
            DamagePerLevel = 0.26f, CooldownPerLevel = -0.04f, ProjectilesEveryNLevels = 3, MaxLevel = 8,
        }),

        // Trail hazard: a pool under your feet every 0.9s that ticks for 3.2s.
        // Range is the pool's lifetime in seconds; Radius its size.
        W(new WeaponDef
        {
            Id = "brine_wake", DisplayName = "Brine Wake",
            Description = "You leave a trail of stinging brine wherever you walk. Run in circles and they follow you into it.",
            Rarity = Rarity.Common, Shape = AttackShape.Aura, Element = Element.Poison,   // element: Poison status fits here
            Damage = 3.5f, Cooldown = 0.9f, Projectiles = 1, ProjectileSpeed = 0f, Range = 3.2f, Radius = 1.7f,
            Knockback = 0f, CritChance = 0.02f, ProcCoefficient = 0.3f, SpreadDegrees = 0f,
            Tint = new Color(0.50f, 0.92f, 0.55f),
            DamagePerLevel = 0.30f, CooldownPerLevel = -0.04f, ProjectilesEveryNLevels = 0, MaxLevel = 8,
        }),

        // Chain: instant, jumps to the next body within Radius, 12% weaker per link.
        W(new WeaponDef
        {
            Id = "forked_squall", DisplayName = "Forked Squall",
            Description = "Lightning that cannot make up its mind. It strikes one, then the one beside it, then the one beside that.",
            Rarity = Rarity.Uncommon, Shape = AttackShape.Projectile, Element = Element.Lightning,   // element: Shock status fits here
            Damage = 11f, Cooldown = 1.3f, Projectiles = 1, ProjectileSpeed = 0f, Range = 14f, Radius = 5f,
            Knockback = 0.5f, CritChance = 0.04f, ProcCoefficient = 0.6f, SpreadDegrees = 0f,
            Tint = new Color(0.82f, 0.86f, 1.00f),
            DamagePerLevel = 0.25f, CooldownPerLevel = -0.05f, ProjectilesEveryNLevels = 3, MaxLevel = 8,
        }),

        // Beam: instant line the whole Range long; hits everything on it.
        W(new WeaponDef
        {
            Id = "lighthouse_beam", DisplayName = "Lighthouse Beam",
            Description = "A lamp meant to warn ships off the rocks, turned on the things that came out of them instead.",
            Rarity = Rarity.Rare, Shape = AttackShape.Projectile, Element = Element.Fire,   // element: Burn status fits here
            Damage = 30f, Cooldown = 2.0f, Projectiles = 1, ProjectileSpeed = 0f, Range = 20f, Radius = 0.5f,
            Knockback = 3f, CritChance = 0.08f, ProcCoefficient = 1.2f, SpreadDegrees = 20f,
            Tint = new Color(1.00f, 0.92f, 0.62f),
            DamagePerLevel = 0.30f, CooldownPerLevel = -0.05f, ProjectilesEveryNLevels = 4, MaxLevel = 8,
        }),

        // Mines: lobbed at the nearest enemy, sit armed until touched.
        W(new WeaponDef
        {
            Id = "barnacle_mines", DisplayName = "Barnacle Mines",
            Description = "Crusted shells packed with blasting powder. They wait on the floor with the patience of something that has always lived there.",
            Rarity = Rarity.Common, Shape = AttackShape.Explosive, Element = Element.Physical,
            Damage = 18f, Cooldown = 1.2f, Projectiles = 1, ProjectileSpeed = 14f, Range = 12f, Radius = 2.2f,
            Knockback = 4f, CritChance = 0.04f, ProcCoefficient = 1f, SpreadDegrees = 0f,
            Tint = new Color(0.82f, 0.70f, 0.55f),
            DamagePerLevel = 0.26f, CooldownPerLevel = -0.05f, ProjectilesEveryNLevels = 3, MaxLevel = 8,
        }),

        // Turret: one sentry per cooldown, alive for most of it; Radius is its
        // own fire interval, so it shoots about 7/0.45 = 15.5 dps while planted.
        W(new WeaponDef
        {
            Id = "driftwood_sentry", DisplayName = "Driftwood Sentry",
            Description = "A crossbow lashed to a post. Plant it, walk away, and it keeps arguing on your behalf.",
            Rarity = Rarity.Rare, Shape = AttackShape.Projectile, Element = Element.Physical,
            Damage = 7f, Cooldown = 6f, Projectiles = 1, ProjectileSpeed = 28f, Range = 16f, Radius = 0.45f,
            Knockback = 1f, CritChance = 0.06f, ProcCoefficient = 0.5f, SpreadDegrees = 0f,
            Tint = new Color(0.86f, 0.66f, 0.42f),
            DamagePerLevel = 0.26f, CooldownPerLevel = -0.04f, ProjectilesEveryNLevels = 3, MaxLevel = 8,
        }),

        // Black hole: flies to the nearest enemy, holds, pulls inward with small
        // grinding ticks, and collapses for the full hit. Radius = pull radius.
        W(new WeaponDef
        {
            Id = "maelstrom_seed", DisplayName = "Maelstrom Seed",
            Description = "A pearl with a whirlpool folded inside it. Throw it and the sea remembers how to drown things.",
            Rarity = Rarity.Epic, Shape = AttackShape.Explosive, Element = Element.Ice,   // element: Freeze status fits here
            Damage = 40f, Cooldown = 3.5f, Projectiles = 1, ProjectileSpeed = 9f, Range = 12f, Radius = 3.5f,
            Knockback = 6f, CritChance = 0.05f, ProcCoefficient = 1f, SpreadDegrees = 30f,
            Tint = new Color(0.45f, 0.62f, 1.00f),
            DamagePerLevel = 0.28f, CooldownPerLevel = -0.05f, ProjectilesEveryNLevels = 4, MaxLevel = 8,
        }),

        // Ricochet: Pierce is the number of bounces to the next nearest body.
        W(new WeaponDef
        {
            Id = "skimstone_sling", DisplayName = "Skimstone Sling",
            Description = "Flat stones from a cold beach, slung low. Each one skips off a skull and looks for another.",
            Rarity = Rarity.Common, Shape = AttackShape.Projectile, Element = Element.Physical,
            Damage = 10f, Cooldown = 0.8f, Projectiles = 1, ProjectileSpeed = 24f, Range = 14f, Radius = 0.35f,
            Pierce = 3, Knockback = 1.5f, CritChance = 0.06f, ProcCoefficient = 0.8f, SpreadDegrees = 10f,
            Tint = new Color(0.72f, 0.78f, 0.82f),
            DamagePerLevel = 0.25f, CooldownPerLevel = -0.05f, ProjectilesEveryNLevels = 3, MaxLevel = 8,
        }),

        // Cone breath: long, narrow, very fast ticking melee. SpreadDegrees is
        // the full cone width.
        W(new WeaponDef
        {
            Id = "sleetbreath_horn", DisplayName = "Sleetbreath Horn",
            Description = "Blow through it and a gale of sleet comes out the other end, much further than your breath should reach.",
            Rarity = Rarity.Uncommon, Shape = AttackShape.Melee, Element = Element.Ice,   // element: Freeze status fits here
            Damage = 4.5f, Cooldown = 0.3f, Projectiles = 1, ProjectileSpeed = 0f, Range = 7f, Radius = 0.5f,
            Knockback = 0.6f, CritChance = 0.04f, ProcCoefficient = 0.25f, SpreadDegrees = 40f,
            Tint = new Color(0.78f, 0.94f, 1.00f),
            DamagePerLevel = 0.26f, CooldownPerLevel = -0.04f, ProjectilesEveryNLevels = 0, MaxLevel = 8,
        }),

        // Sky strikes: stones fall on random enemies within Range after a beat.
        W(new WeaponDef
        {
            Id = "thunderhead_censer", DisplayName = "Thunderhead Censer",
            Description = "Swing it and the clouds lean down to look. Whatever they see gets hail.",
            Rarity = Rarity.Rare, Shape = AttackShape.Explosive, Element = Element.Lightning,   // element: Shock status fits here
            Damage = 15f, Cooldown = 1.8f, Projectiles = 2, ProjectileSpeed = 0f, Range = 14f, Radius = 2f,
            Knockback = 2f, CritChance = 0.05f, ProcCoefficient = 0.8f, SpreadDegrees = 0f,
            Tint = new Color(0.80f, 0.82f, 1.00f),
            DamagePerLevel = 0.26f, CooldownPerLevel = -0.05f, ProjectilesEveryNLevels = 3, MaxLevel = 8,
        }),

        // Expanding ring: one wave out to Range, hits each body once, shoves hard.
        W(new WeaponDef
        {
            Id = "breaker_swell", DisplayName = "Breaker Swell",
            Description = "You stamp, and a wave rolls out from your feet in every direction at once. It does not care who is in the way.",
            Rarity = Rarity.Rare, Shape = AttackShape.Aura, Element = Element.Physical,
            Damage = 26f, Cooldown = 2.4f, Projectiles = 1, ProjectileSpeed = 9f, Range = 8f, Radius = 0.6f,
            Knockback = 7f, CritChance = 0.04f, ProcCoefficient = 1f, SpreadDegrees = 0f,
            Tint = new Color(0.48f, 0.78f, 0.95f),
            DamagePerLevel = 0.28f, CooldownPerLevel = -0.05f, ProjectilesEveryNLevels = 4, MaxLevel = 8,
        }),
    };

    // =====================================================================
    //  ITEMS (43): 17 stat items, 26 with a rule (see ItemProcs)
    // =====================================================================

    private static List<ItemDef> ExpansionItems() => new()
    {
        // ------------------------------------------------ Common, stats only
        X(Item("kelp_wraps", "Kelp Wraps", Rarity.Common, 1.0f, 0,
            "Wet ribbons of kelp bound round every joint. They keep you together a little longer.",
            new Color(0.40f, 0.70f, 0.42f),
            Effect(Stat.MaxHealth, ModifierOp.Flat, 8f), Effect(Stat.HealthRegen, ModifierOp.Flat, 4f))),
        X(Item("sharkskin_grip", "Sharkskin Grip", Rarity.Common, 1.0f, 0,
            "Rough enough that nothing slips. You swing a fraction sooner and a fraction truer.",
            new Color(0.62f, 0.68f, 0.74f),
            Effect(Stat.AttackSpeed, ModifierOp.Additive, 0.05f), Effect(Stat.CritChance, ModifierOp.Flat, 0.02f))),
        X(Item("pumice_charm", "Pumice Charm", Rarity.Common, 1.0f, 0,
            "Light as foam. Blows that should land find you a hand's width to the left.",
            new Color(0.82f, 0.80f, 0.74f),
            Effect(Stat.Evasion, ModifierOp.Flat, 5f))),
        X(Item("tidewater_flask", "Tidewater Flask", Rarity.Common, 1.0f, 0,
            "Water from a tide that never quite went out. Whatever you leave behind lingers.",
            new Color(0.42f, 0.74f, 0.92f),
            Effect(Stat.Duration, ModifierOp.Additive, 0.12f))),
        X(Item("sextant_lens", "Sextant Lens", Rarity.Common, 1.0f, 0,
            "Shows you exactly how far away things are, which turns out to be closer than they think.",
            new Color(0.80f, 0.86f, 0.62f),
            Effect(Stat.ProjectileSpeed, ModifierOp.Additive, 0.18f))),
        X(Item("ballast_stone", "Ballast Stone", Rarity.Common, 1.0f, 0,
            "Heavy, smooth, and tied to everything you swing. Things land further away.",
            new Color(0.48f, 0.48f, 0.52f),
            Effect(Stat.Knockback, ModifierOp.Additive, 0.25f))),
        X(Item("cork_float", "Cork Float", Rarity.Common, 1.0f, 0,
            "It wants to go up. So, now, do you.",
            new Color(0.86f, 0.70f, 0.48f),
            Effect(Stat.JumpHeight, ModifierOp.Additive, 0.20f), Effect(Stat.PickupRange, ModifierOp.Additive, 0.10f))),

        // ---------------------------------------------- Uncommon, stats only
        X(Item("trawler_net", "Trawler's Net", Rarity.Uncommon, 0.7f, 0,
            "Wide, tarred and torn in all the right places. Everything you do covers more ground.",
            new Color(0.60f, 0.52f, 0.40f),
            Effect(Stat.AreaSize, ModifierOp.Additive, 0.12f), Effect(Stat.PickupRange, ModifierOp.Additive, 0.20f))),
        X(Item("coral_spur", "Coral Spur", Rarity.Uncommon, 0.7f, 0,
            "Living coral grafted to the elbow. It grows outward, toward whoever is closest.",
            new Color(1.00f, 0.52f, 0.48f),
            Effect(Stat.Thorns, ModifierOp.Flat, 6f), Effect(Stat.Armor, ModifierOp.Flat, 3f))),
        X(Item("lamplighters_wick", "Lamplighter's Wick", Rarity.Uncommon, 0.7f, 0,
            "Trimmed to burn all night. Your pools, blades and sentries keep going after they should have stopped.",
            new Color(1.00f, 0.80f, 0.42f),
            Effect(Stat.Duration, ModifierOp.Additive, 0.18f), Effect(Stat.AreaSize, ModifierOp.Additive, 0.06f))),
        X(Item("driftglass_monocle", "Driftglass Monocle", Rarity.Uncommon, 0.7f, 0,
            "Sea-worn glass that shows you where a thing is thinnest.",
            new Color(0.62f, 0.92f, 0.82f),
            Effect(Stat.CritDamage, ModifierOp.Additive, 0.25f))),
        X(Item("harbour_dues", "Harbour Dues", Rarity.Uncommon, 0.7f, 0,
            "A stamped writ that says you are owed. People, and chests, believe it.",
            new Color(0.95f, 0.80f, 0.36f),
            Effect(Stat.GoldGain, ModifierOp.Additive, 0.12f), Effect(Stat.Luck, ModifierOp.Flat, 0.10f))),

        // -------------------------------------------------- Rare, stats only
        X(Item("stormcaller_pennant", "Stormcaller Pennant", Rarity.Rare, 0.45f, 0,
            "It snaps in a wind nobody else can feel. You keep time with it and drag your feet a little.",
            new Color(0.62f, 0.66f, 1.00f),
            Effect(Stat.AttackSpeed, ModifierOp.Additive, 0.15f), Effect(Stat.MoveSpeed, ModifierOp.Additive, -0.05f))),
        X(Item("pearl_divers_lungs", "Pearl Diver's Lungs", Rarity.Rare, 0.45f, 0,
            "Trained on a single breath for four minutes. Hold it, and the hits wash off for a while.",
            new Color(0.88f, 0.90f, 0.96f),
            Effect(Stat.MaxHealth, ModifierOp.Flat, 30f), Effect(Stat.Shield, ModifierOp.Flat, 15f), Effect(Stat.MoveSpeed, ModifierOp.Additive, -0.04f))),
        X(Item("deep_lantern", "Deep Lantern", Rarity.Rare, 0.45f, 0,
            "Its light only shows things worth finding.",
            new Color(0.42f, 0.96f, 0.86f),
            Effect(Stat.Luck, ModifierOp.Flat, 0.35f), Effect(Stat.XpGain, ModifierOp.Additive, 0.08f))),

        // -------------------------------------------------- Epic, stats only
        X(Item("kraken_ink", "Kraken Ink", Rarity.Epic, 0.25f, 2,
            "A vial of something that was still moving when it was bottled. Everything you do hits harder, and is harder to follow.",
            new Color(0.30f, 0.22f, 0.45f),
            Effect(Stat.Damage, ModifierOp.Multiplicative, 1.15f), Effect(Stat.Evasion, ModifierOp.Flat, 10f), Effect(Stat.MaxHealth, ModifierOp.Additive, -0.08f))),
        X(Item("fathom_engine", "Fathom Engine", Rarity.Epic, 0.25f, 2,
            "Brass gears that turn one of everything into two of it, a little weaker.",
            new Color(0.80f, 0.62f, 0.36f),
            Effect(Stat.ProjectileCount, ModifierOp.Flat, 1f), Effect(Stat.Damage, ModifierOp.Additive, -0.08f))),

        // ------------------------------------------------------- on hit
        X(Item("sparking_limpet", "Sparking Limpet", Rarity.Common, 1.0f, 0,
            "Each hit has a 7% chance (per stack, scaled by the weapon's proc rate) to arc to a nearby enemy for 60% of the hit.",
            new Color(0.95f, 0.92f, 0.45f),
            Effect(Stat.Damage, ModifierOp.Additive, 0.03f))),
        X(Item("riptooth_charm", "Riptooth Charm", Rarity.Uncommon, 0.7f, 0,
            "Hits on enemies above 80% health deal 20% extra damage per stack.",
            new Color(0.92f, 0.86f, 0.80f),
            Effect(Stat.CritChance, ModifierOp.Flat, 0.01f))),
        X(Item("brine_bomb_locket", "Brine-Bomb Locket", Rarity.Uncommon, 0.65f, 0,
            "Hits have a 5% chance per stack (scaled by proc rate) to burst for 80% of the hit around the target.",
            new Color(0.40f, 0.86f, 0.70f),
            Effect(Stat.AreaSize, ModifierOp.Additive, 0.04f))),
        X(Item("shatterglass_coral", "Shatterglass Coral", Rarity.Rare, 0.45f, 0,
            "Critical hits shatter, dealing 35% of the hit per stack to enemies within 2.5m of the target.",
            new Color(0.74f, 0.90f, 1.00f),
            Effect(Stat.CritChance, ModifierOp.Flat, 0.03f))),

        // ------------------------------------------------------ on kill
        X(Item("wraith_lantern", "Wraith Lantern", Rarity.Common, 1.0f, 0,
            "Each kill restores 0.6 health per stack.",
            new Color(0.62f, 0.95f, 0.80f),
            Effect(Stat.MaxHealth, ModifierOp.Flat, 5f))),
        X(Item("scrimshaw_tally", "Scrimshaw Tally", Rarity.Uncommon, 0.7f, 0,
            "Every 40 kills carves a notch: +1% damage per stack for the rest of the run (up to 30 notches).",
            new Color(0.94f, 0.90f, 0.78f),
            Effect(Stat.XpGain, ModifierOp.Additive, 0.04f))),
        X(Item("powder_keg", "Powder Keg", Rarity.Rare, 0.45f, 0,
            "Kills have a 15% chance to explode for 60% of the killing blow per stack (at least 10) in 3m.",
            new Color(0.95f, 0.50f, 0.25f),
            Effect(Stat.Damage, ModifierOp.Additive, 0.04f))),
        X(Item("gilded_scale", "Gilded Scale", Rarity.Uncommon, 0.7f, 0,
            "Elites drop 15 extra gold per stack when they die.",
            new Color(1.00f, 0.82f, 0.30f),
            Effect(Stat.GoldGain, ModifierOp.Additive, 0.04f))),
        X(Item("pyre_totem", "Pyre Totem", Rarity.Rare, 0.45f, 0,
            "Kills stoke a frenzy: +2% attack speed for 3s, stacking to 5 per stack held.",
            new Color(1.00f, 0.55f, 0.30f),
            Effect(Stat.AttackSpeed, ModifierOp.Additive, 0.03f))),

        // ---------------------------------------------- when you are hit
        X(Item("urchin_shell", "Urchin Shell", Rarity.Common, 1.0f, 0,
            "When hit, spines burst out for 15 damage per stack to everything within 3m.",
            new Color(0.48f, 0.30f, 0.52f),
            Effect(Stat.Armor, ModifierOp.Flat, 2f))),
        X(Item("ink_sac", "Ink Sac", Rarity.Uncommon, 0.7f, 0,
            "When hit, vanish in a cloud: +0.3s invulnerability per stack and +30% move speed for 2s.",
            new Color(0.22f, 0.20f, 0.30f),
            Effect(Stat.Evasion, ModifierOp.Flat, 3f))),
        X(Item("wrathful_conch", "Wrathful Conch", Rarity.Rare, 0.45f, 0,
            "When hit, +15% damage per stack for 4s.",
            new Color(0.95f, 0.45f, 0.40f),
            Effect(Stat.Thorns, ModifierOp.Flat, 4f))),
        X(Item("lifebuoy", "Lifebuoy", Rarity.Epic, 0.25f, 2,
            "A hit that leaves you under 25% health heals 30% of your max. Recharges in 60s, 15s less per extra stack.",
            new Color(1.00f, 0.42f, 0.36f),
            Effect(Stat.MaxHealth, ModifierOp.Flat, 10f))),

        // ------------------------------------------------------ movement
        X(Item("gale_sail", "Gale Sail", Rarity.Uncommon, 0.7f, 0,
            "While airborne, +12% damage per stack.",
            new Color(0.90f, 0.94f, 1.00f),
            Effect(Stat.JumpHeight, ModifierOp.Additive, 0.10f))),
        X(Item("slickfin_greaves", "Slickfin Greaves", Rarity.Uncommon, 0.7f, 0,
            "While sliding, +20% damage and +10% attack speed per stack.",
            new Color(0.50f, 0.80f, 0.86f),
            Effect(Stat.MoveSpeed, ModifierOp.Additive, 0.03f))),
        X(Item("wavecrest_fins", "Wavecrest Fins", Rarity.Rare, 0.45f, 0,
            "+1.5% damage per stack for every m/s you move above walking speed (up to +30% per stack).",
            new Color(0.40f, 0.72f, 1.00f),
            Effect(Stat.MoveSpeed, ModifierOp.Additive, 0.05f))),
        X(Item("spindrift_cape", "Spindrift Cape", Rarity.Common, 1.0f, 0,
            "Each dash grants +15% attack speed per stack for 1.5s.",
            new Color(0.80f, 0.90f, 0.96f),
            Effect(Stat.DashCooldown, ModifierOp.Additive, -0.05f))),
        X(Item("plunge_weights", "Plunge Weights", Rarity.Uncommon, 0.7f, 0,
            "Landing hard or slamming sends a shock through the ground: 20 damage per stack within 3m.",
            new Color(0.56f, 0.56f, 0.62f),
            Effect(Stat.Knockback, ModifierOp.Additive, 0.10f))),
        X(Item("anchored_stance", "Anchored Stance", Rarity.Uncommon, 0.7f, 0,
            "Standing still for 1s braces you: +8 armour and +12% damage per stack until you move.",
            new Color(0.52f, 0.60f, 0.70f),
            Effect(Stat.Armor, ModifierOp.Flat, 2f))),

        // -------------------------------------------------- interact / gold
        X(Item("diving_bell", "Diving Bell", Rarity.Uncommon, 0.7f, 0,
            "Opening a chest restores 10% of your health per stack.",
            new Color(0.78f, 0.66f, 0.40f),
            Effect(Stat.Luck, ModifierOp.Flat, 0.05f))),
        X(Item("votive_candle", "Votive Candle", Rarity.Uncommon, 0.65f, 0,
            "Using a map shrine grants 25 gold and heals 5% per stack.",
            new Color(1.00f, 0.90f, 0.62f),
            Effect(Stat.XpGain, ModifierOp.Additive, 0.03f))),
        X(Item("misers_anchor", "Miser's Anchor", Rarity.Rare, 0.45f, 3,
            "+1% damage per stack for every 25 gold you are holding (up to +25% per stack).",
            new Color(0.92f, 0.76f, 0.30f),
            Effect(Stat.GoldGain, ModifierOp.Additive, 0.05f))),
        X(Item("coin_divers_glove", "Coin-Diver's Glove", Rarity.Common, 0.9f, 0,
            "Picking up gold restores 0.5 health per stack.",
            new Color(0.86f, 0.76f, 0.46f),
            Effect(Stat.PickupRange, ModifierOp.Additive, 0.08f))),

        // ---------------------------------------------- Legendary rule-changers
        X(Item("eye_of_the_maelstrom", "Eye of the Maelstrom", Rarity.Legendary, 0.10f, 1,
            "Every 10 kills, lightning strikes up to 5 enemies near you for 40 damage plus 4 per level. Your own footing suffers for it.",
            new Color(0.62f, 0.62f, 1.00f),
            Effect(Stat.Damage, ModifierOp.Multiplicative, 1.08f), Effect(Stat.MoveSpeed, ModifierOp.Additive, -0.10f))),
        X(Item("mariners_last_wish", "Mariner's Last Wish", Rarity.Legendary, 0.10f, 1,
            "Once every 120s, a killing blow is refused: you are healed to 50% and untouchable for 2s. You live smaller to earn it.",
            new Color(0.90f, 0.95f, 1.00f),
            Effect(Stat.MaxHealth, ModifierOp.Additive, -0.15f), Effect(Stat.Armor, ModifierOp.Flat, 5f))),
        X(Item("tithe_of_the_deep", "Tithe of the Deep", Rarity.Legendary, 0.10f, 1,
            "Every coin you collect is also counted as experience. The deep takes its cut of what you learn the usual way.",
            new Color(0.95f, 0.78f, 0.25f),
            Effect(Stat.GoldGain, ModifierOp.Additive, 0.20f), Effect(Stat.XpGain, ModifierOp.Additive, -0.25f))),
    };
}
