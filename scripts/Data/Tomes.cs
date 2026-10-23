using System.Collections.Generic;
using Godot;
using Wavebreak.Stats;

namespace Wavebreak.Data;

/// <summary>
/// A tome: a levelled stat upgrade, the third kind of level-up offer beside
/// weapons and items.
///
/// Where an item is a bundle of effects (often with a trade-off) that stacks,
/// a tome is exactly one clean stat that grows by a fixed step per level, up
/// to <see cref="MaxLevel"/>. The player has <see cref="Tomes.MaxSlots"/> of
/// them, so choosing which stats to "invest" in is a build decision in the same
/// way choosing weapons is.
///
/// Its whole mechanical content is (<see cref="Stat"/>, <see cref="Op"/>,
/// <see cref="PerLevel"/>), written to the StatSheet tagged with the def as the
/// source — so a tome goes through the same pipeline, caps and floors as
/// everything else (income soft cap, -75% penalty floor, per-stat clamps).
/// </summary>
[GlobalClass]
public partial class TomeDef : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
    [Export] public Rarity Rarity { get; set; } = Rarity.Common;

    [Export] public Stat Stat { get; set; } = Stat.Damage;
    [Export] public ModifierOp Op { get; set; } = ModifierOp.Additive;

    /// <summary>What each level adds. Level n contributes n * PerLevel
    /// (or PerLevel^n for a multiplicative tome).</summary>
    [Export] public float PerLevel { get; set; } = 0.08f;

    [Export] public int MaxLevel { get; set; } = 5;

    /// <summary>Relative chance of being offered, before luck. Same scale as
    /// <see cref="ItemDef.Weight"/>.</summary>
    [Export] public float Weight { get; set; } = 0.8f;

    [Export] public Color Tint { get; set; } = new(0.85f, 0.80f, 0.60f);

    /// <summary>The single modifier value this tome contributes at a level.</summary>
    public float ValueAtLevel(int level)
    {
        if (level <= 0) return Op == ModifierOp.Multiplicative ? 1f : 0f;
        return Op == ModifierOp.Multiplicative ? Mathf.Pow(PerLevel, level) : PerLevel * level;
    }

    /// <summary>"+8% Damage" — one level's worth, in the catalogue's wording.</summary>
    public string DescribeStep() => Catalog.DescribeEffect(new ItemEffect { Stat = Stat, Op = Op, PerStack = PerLevel });

    /// <summary>"+24% Damage" — the total at a level.</summary>
    public string DescribeTotal(int level)
        => Catalog.DescribeEffect(new ItemEffect { Stat = Stat, Op = Op, PerStack = ValueAtLevel(level) });
}

/// <summary>
/// The tome table. Kept beside the catalogue but in its own file so the item
/// and weapon tables stay untouched.
///
/// Balance record. A tome at max level is worth roughly two to three copies of
/// the matching common item, and it costs a level-up per step plus one of only
/// four slots — so it competes with items on efficiency, never on reach. The
/// two income tomes go through the StatSheet's income soft cap like any item
/// (+30% XP shapes to about +26%); nothing a tome adds is exempt from a clamp.
/// </summary>
public static class Tomes
{
    /// <summary>Distinct tomes a player can hold in one run.</summary>
    public const int MaxSlots = 4;

    public static IReadOnlyList<TomeDef> All => List;

    public static TomeDef? ById(string? id)
    {
        if (id is null) return null;
        foreach (TomeDef t in List) if (t.Id == id) return t;
        return null;
    }

    private static readonly List<TomeDef> List = new()
    {
        // Offence
        Tome("tome_edges", "Tome of Edges", Rarity.Common, Stat.Damage, ModifierOp.Additive, 0.08f, 5, 0.85f,
             "Every page is a whetstone. +40% damage at full study.", new Color(0.95f, 0.45f, 0.38f)),
        Tome("tome_tempo", "Tome of Tempo", Rarity.Common, Stat.AttackSpeed, ModifierOp.Additive, 0.07f, 5, 0.85f,
             "A metronome bound in sharkskin. Weapons recover faster.", new Color(1.00f, 0.78f, 0.35f)),
        Tome("tome_breadth", "Tome of Breadth", Rarity.Common, Stat.AreaSize, ModifierOp.Additive, 0.08f, 5, 0.8f,
             "Maps of currents that go further than they should. Bigger auras and blasts.", new Color(0.55f, 0.80f, 1.00f)),
        Tome("tome_multitudes", "Tome of Multitudes", Rarity.Rare, Stat.ProjectileCount, ModifierOp.Flat, 1f, 3, 0.35f,
             "Read it twice and you start seeing two of everything. So do your weapons.", new Color(0.72f, 0.55f, 1.00f)),
        Tome("tome_precision", "Tome of Precision", Rarity.Uncommon, Stat.CritChance, ModifierOp.Flat, 0.03f, 5, 0.6f,
             "Anatomy of things that live under pressure. Find the soft parts.", new Color(1.00f, 0.55f, 0.75f)),

        // Survival
        Tome("tome_vigour", "Tome of Vigour", Rarity.Common, Stat.MaxHealth, ModifierOp.Flat, 15f, 5, 0.85f,
             "Breathing exercises for depths no one breathes at.", new Color(0.40f, 0.90f, 0.50f)),
        Tome("tome_shells", "Tome of Shells", Rarity.Common, Stat.Armor, ModifierOp.Flat, 4f, 5, 0.8f,
             "How the crabs do it, illustrated.", new Color(0.70f, 0.72f, 0.78f)),
        Tome("tome_renewal", "Tome of Renewal", Rarity.Common, Stat.HealthRegen, ModifierOp.Flat, 6f, 5, 0.8f,
             "The tide always comes back. So do you, a little at a time.", new Color(0.45f, 0.95f, 0.75f)),

        // Mobility and economy
        Tome("tome_currents", "Tome of Currents", Rarity.Common, Stat.MoveSpeed, ModifierOp.Additive, 0.05f, 5, 0.8f,
             "Charts of every fast water in the world. Move with them.", new Color(0.50f, 0.95f, 1.00f)),
        Tome("tome_tides", "Tome of Tides", Rarity.Common, Stat.PickupRange, ModifierOp.Additive, 0.15f, 5, 0.75f,
             "Things wash up at your feet. Pickups come from further away.", new Color(0.40f, 0.70f, 0.95f)),
        Tome("tome_insight", "Tome of Insight", Rarity.Uncommon, Stat.XpGain, ModifierOp.Additive, 0.06f, 5, 0.6f,
             "Margins full of somebody else's mistakes. More experience (soft-capped).", new Color(0.42f, 0.86f, 1.00f)),
        Tome("tome_plenty", "Tome of Plenty", Rarity.Uncommon, Stat.GoldGain, ModifierOp.Additive, 0.08f, 5, 0.6f,
             "A ledger that balances itself in your favour. More gold (soft-capped).", new Color(1.00f, 0.84f, 0.32f)),
        Tome("tome_fortune", "Tome of Fortune", Rarity.Uncommon, Stat.Luck, ModifierOp.Flat, 0.12f, 5, 0.55f,
             "Every page landed face up. Rarer offers and chests.", new Color(0.60f, 1.00f, 0.55f)),
    };

    private static TomeDef Tome(string id, string name, Rarity rarity, Stat stat, ModifierOp op,
                                float perLevel, int maxLevel, float weight, string description, Color tint)
        => new()
        {
            Id = id,
            DisplayName = name,
            Rarity = rarity,
            Stat = stat,
            Op = op,
            PerLevel = perLevel,
            MaxLevel = maxLevel,
            Weight = weight,
            Description = description,
            Tint = tint,
        };
}
