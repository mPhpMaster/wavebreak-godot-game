using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;
using Wavebreak.Stats;

namespace Wavebreak.Data;

/// <summary>
/// Thirteen more tomes, appended to <see cref="Tomes"/> by the static
/// constructor so the base table stays as written.
///
/// Ten are plain one-stat tomes for the stats the first thirteen left out.
/// Three are odd ones whose primary stat is only half the story; their extra
/// modifiers come from <see cref="TomeExtras"/>, tagged with the same def, so
/// <see cref="Progression.TomeBook"/>'s remove-and-rewrite keeps working and a
/// cleared book leaves nothing behind.
/// </summary>
public static partial class Tomes
{
    static Tomes()
    {
        foreach (TomeDef t in ExpansionTomes()) List.Add(t);
        foreach (PerkDef p in Perks.All) List.Add(p);   // Data/Tomes.Perks.cs
    }

    /// <summary>Ids added by this file, for the expansion audit.</summary>
    public static readonly HashSet<string> ExpansionIds = new();

    private static TomeDef T(TomeDef def) { ExpansionIds.Add(def.Id); return def; }

    private static List<TomeDef> ExpansionTomes() => new()
    {
        // Plain stats
        T(Tome("tome_mist", "Tome of Mist", Rarity.Common, Stat.Evasion, ModifierOp.Flat, 4f, 5, 0.75f,
               "How fog moves around a lighthouse. Blows find you less often.", new Color(0.80f, 0.86f, 0.92f))),
        T(Tome("tome_surf", "Tome of Surf", Rarity.Common, Stat.Knockback, ModifierOp.Additive, 0.15f, 5, 0.7f,
               "Every wave pushes. So do your weapons.", new Color(0.55f, 0.85f, 0.95f))),
        T(Tome("tome_gales", "Tome of Gales", Rarity.Common, Stat.ProjectileSpeed, ModifierOp.Additive, 0.10f, 5, 0.7f,
               "Charts of trade winds. Shots, stones and chakrams fly faster.", new Color(0.86f, 0.92f, 0.72f))),
        T(Tome("tome_slack_water", "Tome of Slack Water", Rarity.Uncommon, Stat.Duration, ModifierOp.Additive, 0.10f, 5, 0.6f,
               "The still hour between tides. Pools, blades, sentries and buffs last longer.", new Color(0.45f, 0.70f, 0.85f))),
        T(Tome("tome_spray", "Tome of Spray", Rarity.Uncommon, Stat.Shield, ModifierOp.Flat, 8f, 5, 0.6f,
               "A curtain of salt spray that takes the first of every blow.", new Color(0.62f, 0.92f, 1.00f))),
        T(Tome("tome_spines", "Tome of Spines", Rarity.Common, Stat.Thorns, ModifierOp.Flat, 4f, 5, 0.7f,
               "A field guide to everything on the reef you should not touch.", new Color(0.72f, 0.42f, 0.70f))),
        T(Tome("tome_leeches", "Tome of Leeches", Rarity.Uncommon, Stat.Lifesteal, ModifierOp.Flat, 0.01f, 5, 0.55f,
               "Patient, small, and always fed. Heal for a share of the damage you deal.", new Color(0.80f, 0.25f, 0.30f))),
        T(Tome("tome_fracture", "Tome of Fracture", Rarity.Uncommon, Stat.CritDamage, ModifierOp.Additive, 0.10f, 5, 0.6f,
               "Where shells crack, and how far. Critical hits hit harder.", new Color(1.00f, 0.70f, 0.55f))),
        T(Tome("tome_updrafts", "Tome of Updrafts", Rarity.Common, Stat.JumpHeight, ModifierOp.Additive, 0.10f, 5, 0.65f,
               "Gulls never flap when they do not have to. Jump higher, stay up longer.", new Color(0.92f, 0.95f, 1.00f))),
        T(Tome("tome_second_thoughts", "Tome of Second Thoughts", Rarity.Rare, Stat.Rerolls, ModifierOp.Flat, 1f, 3, 0.35f,
               "Margins full of crossings-out. One more reroll per level.", new Color(0.70f, 0.80f, 0.70f))),

        // Odd ones (see TomeExtras)
        T(Tome("tome_balance", "Tome of Balance", Rarity.Uncommon, Stat.Damage, ModifierOp.Additive, 0.03f, 5, 0.55f,
               "Neither ebb nor flood. Every level: +3% damage, attack speed, area and move speed, +5 max health, +1.5 armour, +1% crit.",
               new Color(0.85f, 0.85f, 0.85f))),
        T(Tome("tome_chaos", "Tome of Chaos", Rarity.Rare, Stat.Luck, ModifierOp.Flat, 0.05f, 5, 0.4f,
               "The pages rearrange themselves. Every level: +0.05 luck and a large boost to one random stat.",
               new Color(0.95f, 0.40f, 0.85f))),
        T(Tome("tome_cursed", "Cursed Tome", Rarity.Rare, Stat.EnemyScaling, ModifierOp.Additive, 0.08f, 5, 0.35f,
               "Read aloud, it calls them. Every level: +8% more enemies, and +6% experience, +8% gold and +0.1 luck.",
               new Color(0.55f, 0.20f, 0.45f))),
    };
}

/// <summary>
/// Extra modifiers for the multi-stat tomes, applied by <see cref="Progression.TomeBook.Add"/>
/// right after the primary stat. The book has already removed everything
/// tagged with the def, so this writes the full total for the level each time.
/// </summary>
public static class TomeExtras
{
    private readonly record struct Roll(Stat Stat, ModifierOp Op, float Value);

    private static readonly Roll[] ChaosTable =
    {
        new(Stat.Damage, ModifierOp.Additive, 0.10f),
        new(Stat.AttackSpeed, ModifierOp.Additive, 0.10f),
        new(Stat.AreaSize, ModifierOp.Additive, 0.12f),
        new(Stat.MaxHealth, ModifierOp.Flat, 25f),
        new(Stat.Armor, ModifierOp.Flat, 6f),
        new(Stat.Evasion, ModifierOp.Flat, 8f),
        new(Stat.MoveSpeed, ModifierOp.Additive, 0.08f),
        new(Stat.CritChance, ModifierOp.Flat, 0.05f),
        new(Stat.CritDamage, ModifierOp.Additive, 0.20f),
        new(Stat.HealthRegen, ModifierOp.Flat, 10f),
        new(Stat.PickupRange, ModifierOp.Additive, 0.30f),
        new(Stat.Duration, ModifierOp.Additive, 0.15f),
        new(Stat.ProjectileSpeed, ModifierOp.Additive, 0.20f),
        new(Stat.Thorns, ModifierOp.Flat, 8f),
    };

    /// <summary>Chaos rolls per sheet, so each run (and each scratch sheet
    /// in the audit) has its own and a level-up never re-rolls earlier picks.</summary>
    private static readonly ConditionalWeakTable<StatSheet, List<int>> ChaosRolls = new();

    private static Core.Rng? _rng;

    public static void Apply(TomeDef def, StatSheet stats, int level)
    {
        switch (def.Id)
        {
            case "tome_balance":
                stats.Add(Stat.AttackSpeed, ModifierOp.Additive, 0.03f * level, def);
                stats.Add(Stat.AreaSize, ModifierOp.Additive, 0.03f * level, def);
                stats.Add(Stat.MoveSpeed, ModifierOp.Additive, 0.03f * level, def);
                stats.Add(Stat.MaxHealth, ModifierOp.Flat, 5f * level, def);
                stats.Add(Stat.Armor, ModifierOp.Flat, 1.5f * level, def);
                stats.Add(Stat.CritChance, ModifierOp.Flat, 0.01f * level, def);
                break;

            case "tome_cursed":
                stats.Add(Stat.XpGain, ModifierOp.Additive, 0.06f * level, def);
                stats.Add(Stat.GoldGain, ModifierOp.Additive, 0.08f * level, def);
                stats.Add(Stat.Luck, ModifierOp.Flat, 0.10f * level, def);
                break;

            case "tome_chaos":
            {
                List<int> rolls = ChaosRolls.GetValue(stats, _ => new List<int>());
                _rng ??= new Core.Rng(Game.Main.Instance?.Run?.Seed ^ 0xC4A05UL ?? 0xC4A05UL);
                while (rolls.Count < level) rolls.Add(_rng.RangeInt(0, ChaosTable.Length));
                for (int i = 0; i < level; i++)
                {
                    Roll r = ChaosTable[rolls[i]];
                    stats.Add(r.Stat, r.Op, r.Value, def);
                }
                if (level == rolls.Count && level > 0)
                {
                    Roll last = ChaosTable[rolls[level - 1]];
                    GD.Print($"TOME chaos L{level}: {Catalog.DescribeEffect(new ItemEffect { Stat = last.Stat, Op = last.Op, PerStack = last.Value })}");
                }
                break;
            }
        }
    }

    /// <summary>Ids with extras, for the audit.</summary>
    public static readonly string[] Ids = { "tome_balance", "tome_chaos", "tome_cursed" };
}
