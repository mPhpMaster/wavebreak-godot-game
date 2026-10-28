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
            Loc.F("Lv {0}/{1}  ~{2:0} dps", weapon.Level, weapon.Def.MaxLevel, dps), weapon.Def.Description);
    }

    internal static string Tome(TomeDef tome, TomeBook book)
    {
        int level = book.LevelOf(tome);
        return Describe(tome.DisplayName, null, Loc.F("Lv {0}/{1}  {2}", level, tome.MaxLevel,
            tome.DescribeTotal(level)), tome.Description);
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
        return text.ToString();
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
