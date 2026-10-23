using System.Collections.Generic;
using Godot;
using Wavebreak.Stats;

namespace Wavebreak.Data;

/// <summary>
/// Items for the element and megacrit systems (Combat/StatusEffects). Kept out
/// of the main item table so the balance record there stays readable and so
/// other content partials do not collide with it.
///
/// Every effect here is an ordinary stat, so these need no code: the status
/// rules read IgniteChance, ChillChance, ShockChance, PoisonChance, StatusChance,
/// StatusDuration, ElementalDamage, FreezePower, MegacritChance and
/// UpgradeStats off the player's sheet on every hit.
/// </summary>
public static partial class Catalog
{
    /// <summary>Chance-type stats added by the element system; a flat +0.08 on
    /// them means "+8%" on a card.</summary>
    private static bool IsElementFractionStat(Stat stat)
        => stat is Stat.StatusChance or Stat.MegacritChance or Stat.IgniteChance
            or Stat.ChillChance or Stat.ShockChance or Stat.PoisonChance;

    private static List<ItemDef> WithElementItems(List<ItemDef> items)
    {
        items.AddRange(BuildElementItems());
        return items;
    }

    private static List<ItemDef> BuildElementItems() => new()
    {
        // ---------------------------------------------------------- Uncommon
        // One proc item per element. ~8-10% per hit at proc coefficient 1 is
        // roughly one status every second on a mid-speed weapon: noticeable,
        // not yet a build.

        Item("cinder_barnacle", "Cinder Barnacle", Rarity.Uncommon, 0.7f, 0,
            "It grew on a ship that burned to the waterline, and it never quite cooled.",
            new Color(1.00f, 0.52f, 0.22f),
            Effect(Stat.IgniteChance, ModifierOp.Flat, 0.08f),
            Effect(Stat.ElementalDamage, ModifierOp.Additive, 0.05f)),

        Item("brine_frost_charm", "Brine-Frost Charm", Rarity.Uncommon, 0.7f, 0,
            "A knot of sea ice that will not melt in the hand. Whatever it touches slows down to look at it.",
            new Color(0.62f, 0.88f, 1.00f),
            Effect(Stat.ChillChance, ModifierOp.Flat, 0.10f),
            Effect(Stat.StatusDuration, ModifierOp.Additive, 0.08f)),

        Item("blowfish_spine", "Blowfish Spine", Rarity.Uncommon, 0.7f, 0,
            "Still bristling. The sting is small; the next one is worse.",
            new Color(0.62f, 0.95f, 0.40f),
            Effect(Stat.PoisonChance, ModifierOp.Flat, 0.10f),
            Effect(Stat.ElementalDamage, ModifierOp.Additive, 0.05f)),

        // ------------------------------------------------------------- Rare

        // Shock chains and stuns, so it is priced a tier up from the others.
        Item("squall_lantern", "Squall Lantern", Rarity.Rare, 0.45f, 0,
            "The flame inside is a small piece of a thunderhead. It jumps to whatever is closest.",
            new Color(0.95f, 0.92f, 0.45f),
            Effect(Stat.ShockChance, ModifierOp.Flat, 0.07f),
            Effect(Stat.AttackSpeed, ModifierOp.Additive, 0.05f)),

        // The freeze item: four of these and two Chill stacks freeze anything
        // that is not a boss.
        Item("glacier_tooth", "Glacier Tooth", Rarity.Rare, 0.45f, 3,
            "Calved from the bottom of the ice, where it is old enough to stop things moving altogether.",
            new Color(0.75f, 0.94f, 1.00f),
            Effect(Stat.FreezePower, ModifierOp.Flat, 1f),
            Effect(Stat.ChillChance, ModifierOp.Flat, 0.05f)),

        // The generalist: better odds and longer statuses for whatever element
        // the build already uses.
        Item("tidecallers_prism", "Tidecaller's Prism", Rarity.Rare, 0.45f, 0,
            "Light through it comes out wet, hot, cold and humming, depending on where you stand.",
            new Color(0.70f, 0.80f, 1.00f),
            Effect(Stat.StatusChance, ModifierOp.Flat, 0.08f),
            Effect(Stat.StatusDuration, ModifierOp.Additive, 0.15f)),

        // The megacrit entry point. Needs crits to exist, so it brings a few.
        Item("narwhal_tusk", "Narwhal Tusk", Rarity.Rare, 0.45f, 4,
            "A spiral of ivory that finds the one place a thing cannot afford to be hit.",
            new Color(0.95f, 0.90f, 0.98f),
            Effect(Stat.MegacritChance, ModifierOp.Flat, 0.10f),
            Effect(Stat.CritChance, ModifierOp.Flat, 0.03f)),

        // -------------------------------------------------------------- Epic

        Item("maelstrom_pearl", "Maelstrom Pearl", Rarity.Epic, 0.25f, 3,
            "Every layer of nacre was laid down during a different storm.",
            new Color(0.55f, 0.75f, 0.95f),
            Effect(Stat.ElementalDamage, ModifierOp.Additive, 0.25f),
            Effect(Stat.StatusChance, ModifierOp.Flat, 0.06f)),

        // --------------------------------------------------------- Legendary

        // Every proc at once: turns any weapon, physical ones included, into a
        // status engine.
        Item("eye_of_the_gyre", "Eye of the Gyre", Rarity.Legendary, 0.10f, 1,
            "The still point at the centre of the whirlpool. Everything that passes it is changed.",
            new Color(0.45f, 0.95f, 0.85f),
            Effect(Stat.IgniteChance, ModifierOp.Flat, 0.10f),
            Effect(Stat.ChillChance, ModifierOp.Flat, 0.10f),
            Effect(Stat.ShockChance, ModifierOp.Flat, 0.10f),
            Effect(Stat.PoisonChance, ModifierOp.Flat, 0.10f),
            Effect(Stat.ElementalDamage, ModifierOp.Additive, 0.20f)),

        // The megacrit Legendary: a quarter of crits become megacrits, and the
        // crits themselves hit harder.
        Item("drowned_moon", "Drowned Moon", Rarity.Legendary, 0.10f, 1,
            "It sank one night and never came back up. Down there it still pulls the tide, hard.",
            new Color(0.85f, 0.45f, 1.00f),
            Effect(Stat.MegacritChance, ModifierOp.Flat, 0.25f),
            Effect(Stat.CritChance, ModifierOp.Flat, 0.05f),
            Effect(Stat.CritDamage, ModifierOp.Flat, 0.30f)),

        // Weapon upgrades roll one extra stat line. Only worth it with weapon
        // levels still to take, which is the decision.
        Item("shipwrights_plane", "Shipwright's Plane", Rarity.Legendary, 0.10f, 1,
            "Every hull it has shaved has come out a little better than it was drawn.",
            new Color(0.90f, 0.72f, 0.45f),
            Effect(Stat.UpgradeStats, ModifierOp.Flat, 1f),
            Effect(Stat.Luck, ModifierOp.Flat, 0.5f)),
    };
}
