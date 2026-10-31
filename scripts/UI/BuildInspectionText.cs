using System;
using System.Text;
using Wavebreak.Combat;
using Wavebreak.Data;
using Wavebreak.Meta;
using Wavebreak.Progression;
using Wavebreak.Stats;

namespace Wavebreak.UI;

internal static class BuildInspectionText
{
    internal static string Weapon(WeaponRuntime weapon, StatSheet stats)
    {
        float dps = weapon.Interval > 0 ? weapon.VolleyDamage(stats) / weapon.Interval : 0;
        return Describe(weapon.Def.DisplayName, weapon.Def.Rarity,
            Loc.F("Lv {0}/{1}  ~{2:0} dps", weapon.Level, weapon.Def.MaxLevel, dps), weapon.Def.Description)
            + Evolution(weapon);
    }

    /// <summary>The weapon's line in the evolution table: what it came from, or
    /// the catalyst it wants.</summary>
    private static string Evolution(WeaponRuntime weapon)
    {
        if (weapon.Evolution is { } done) return "\n\n" + Loc.F("Evolved form of {0}", Loc.T(Evolutions.WeaponName(done)));
        if (Evolutions.ForWeapon(weapon.Def.Id) is not { } e) return "";
        string catalyst = Loc.T(Evolutions.CatalystName(e));
        return "\n\n" + (EvolutionOffers.HasCatalyst(e, Inventory.Current, TomeBook.Current)
            ? Loc.F("Evolves with {0} (held)", catalyst) : Loc.F("Evolves with {0}", catalyst));
    }

    /// <summary>A catalyst's line: which held, unevolved weapon it feeds.</summary>
    internal static string CatalystLine(string id, string format)
        => Evolutions.ForCatalyst(id) is { } e && Game.Main.Instance?.Weapons.Get(e.WeaponId) is { Evolution: null }
            ? Loc.F(format, Loc.T(Evolutions.WeaponName(e))) : "";

    internal static string Tome(TomeDef tome, TomeBook book)
    {
        int level = book.LevelOf(tome);
        return Describe(tome.DisplayName, null, Loc.F("Lv {0}/{1}  {2}", level, tome.MaxLevel,
            tome.DescribeTotal(level)), tome.Description) + CatalystNote(tome.Id);
    }

    private static string CatalystNote(string id)
    {
        string line = CatalystLine(id, "Catalyst for {0}: it evolves that weapon at max level.");
        return line.Length > 0 ? "\n\n" + line : "";
    }

    internal static string Item(ItemStack stack)
    {
        var text = new StringBuilder(Describe(stack.DisplayName, stack.Rarity,
            Loc.F("x{0}", stack.Count), stack.Def.Description));
        if (stack.Def.Effects.Count > 0) text.Append("\n\n").Append(Loc.T("Total effects"));
        foreach (ItemEffect effect in stack.Def.Effects)
        {
            if (effect.Op == ModifierOp.Multiplicative && effect.PerStack <= 0) continue;
            using var total = new ItemEffect { Stat = effect.Stat, Op = effect.Op,
                PerStack = effect.Op == ModifierOp.Multiplicative
                    ? MathF.Pow(effect.PerStack, stack.Count) : effect.PerStack * stack.Count };
            text.Append('\n').Append(Catalog.DescribeEffect(total));
        }
        return text.Append(CatalystNote(stack.Id)).ToString();
    }

    private static string Describe(string name, Rarity? rarity, string detail, string description)
    {
        var text = new StringBuilder(Loc.T(name));
        if (rarity is { } value) text.Append("  ·  ").Append(Loc.T(Catalog.RarityName(value)));
        if (detail.Length > 0) text.Append('\n').Append(detail);
        if (!string.IsNullOrWhiteSpace(description)) text.Append("\n\n").Append(Loc.T(description.Trim()));
        return text.ToString();
    }
}
