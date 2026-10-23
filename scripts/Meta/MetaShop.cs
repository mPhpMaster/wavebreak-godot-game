using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Stats;

namespace Wavebreak.Meta;

/// <summary>One permanent upgrade line in the shop.</summary>
public sealed class ShopUpgradeDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Blurb { get; init; }

    /// <summary>The stat it raises, or null for a charge (reroll/banish/skip)
    /// that is read through <see cref="MetaShop"/>'s static bonuses.</summary>
    public Stat? Stat { get; init; }
    public ModifierOp Op { get; init; } = ModifierOp.Additive;
    public float PerLevel { get; init; }
    public int MaxLevel { get; init; } = 5;

    /// <summary>Price of the first level; later levels follow
    /// <see cref="MetaShop.CostOf"/>.</summary>
    public int BaseCost { get; init; } = 30;

    /// <summary>How one level's worth reads on a tooltip, given the total.</summary>
    public required Func<float, string> Describe { get; init; }
}

/// <summary>
/// Permanent upgrades bought with <see cref="Pearls"/>.
///
/// Levels live in the profile (<c>[meta] shop_&lt;id&gt; = n</c>), and are
/// applied to the player's <see cref="StatSheet"/> once per run by
/// <see cref="ApplyTo"/>, tagged with <see cref="Source"/> so they can be told
/// apart from items and removed exactly, like every other modifier.
///
/// The reroll/banish/skip charges are not stats: whoever owns those charges
/// reads <see cref="BonusRerolls"/>, <see cref="BonusBanishes"/> and
/// <see cref="BonusSkips"/> at run start. They are plain ints so that code does
/// not need to know anything else about the shop.
///
/// Costs rise with level as base x (n+1)^1.5, rounded to 5: a base-30 line
/// costs 30, 85, 155, 240, 335 (845 to max).
/// </summary>
public static class MetaShop
{
    /// <summary>The source tag on every modifier the shop adds.</summary>
    public static readonly object Source = new ShopSource();

    private sealed class ShopSource
    {
        public override string ToString() => "Pearl Shop";
    }

    private static string Pct(float v) => $"+{v * 100f:0.#}%";

    public static readonly IReadOnlyList<ShopUpgradeDef> All = new List<ShopUpgradeDef>
    {
        new() { Id = "vitality", Name = "Thick Hide", Blurb = "More max health.",
                Stat = Stats.Stat.MaxHealth, Op = ModifierOp.Flat, PerLevel = 8f, BaseCost = 30,
                Describe = v => $"+{v:0} max health" },
        new() { Id = "might", Name = "Undertow", Blurb = "Every weapon hits harder.",
                Stat = Stats.Stat.Damage, Op = ModifierOp.Additive, PerLevel = 0.04f, BaseCost = 40,
                Describe = v => $"{Pct(v)} damage" },
        new() { Id = "haste", Name = "Quick Hands", Blurb = "Weapons fire faster.",
                Stat = Stats.Stat.AttackSpeed, Op = ModifierOp.Additive, PerLevel = 0.03f, BaseCost = 45,
                Describe = v => $"{Pct(v)} attack speed" },
        new() { Id = "swiftness", Name = "Sea Legs", Blurb = "Move faster.",
                Stat = Stats.Stat.MoveSpeed, Op = ModifierOp.Additive, PerLevel = 0.03f, BaseCost = 30,
                Describe = v => $"{Pct(v)} move speed" },
        new() { Id = "plating", Name = "Barnacle Plate", Blurb = "Armour, which shaves every hit.",
                Stat = Stats.Stat.Armor, Op = ModifierOp.Flat, PerLevel = 1.5f, BaseCost = 40,
                Describe = v => $"+{v:0.#} armour" },
        new() { Id = "vigour", Name = "Brine Blood", Blurb = "Regenerate health over time.",
                Stat = Stats.Stat.HealthRegen, Op = ModifierOp.Flat, PerLevel = 2f, BaseCost = 30,
                Describe = v => $"+{v:0.#} health per minute" },
        new() { Id = "wisdom", Name = "Tide Reading", Blurb = "More experience from every orb.",
                Stat = Stats.Stat.XpGain, Op = ModifierOp.Additive, PerLevel = 0.05f, BaseCost = 35,
                Describe = v => $"{Pct(v)} experience" },
        new() { Id = "greed", Name = "Salvage Rights", Blurb = "More gold from every drop.",
                Stat = Stats.Stat.GoldGain, Op = ModifierOp.Additive, PerLevel = 0.06f, BaseCost = 30,
                Describe = v => $"{Pct(v)} gold" },
        new() { Id = "fortune", Name = "Lucky Shell", Blurb = "Better offers and rarer finds.",
                Stat = Stats.Stat.Luck, Op = ModifierOp.Flat, PerLevel = 1f, BaseCost = 45,
                Describe = v => $"+{v:0} luck" },
        new() { Id = "reach", Name = "Long Net", Blurb = "Pick things up from further away.",
                Stat = Stats.Stat.PickupRange, Op = ModifierOp.Additive, PerLevel = 0.08f, BaseCost = 25,
                Describe = v => $"{Pct(v)} pickup range" },
        new() { Id = "rerolls", Name = "Second Glance", Blurb = "Extra level-up rerolls each run.",
                PerLevel = 1f, MaxLevel = 3, BaseCost = 60,
                Describe = v => $"+{v:0} reroll{(v == 1f ? "" : "s")} per run" },
        new() { Id = "banishes", Name = "Cast Off", Blurb = "Extra level-up banishes each run.",
                PerLevel = 1f, MaxLevel = 3, BaseCost = 60,
                Describe = v => $"+{v:0} banish{(v == 1f ? "" : "es")} per run" },
        new() { Id = "skips", Name = "Let It Pass", Blurb = "Extra level-up skips each run.",
                PerLevel = 1f, MaxLevel = 3, BaseCost = 40,
                Describe = v => $"+{v:0} skip{(v == 1f ? "" : "s")} per run" },
    };

    public static ShopUpgradeDef? ById(string id)
    {
        foreach (ShopUpgradeDef u in All) if (u.Id == id) return u;
        return null;
    }

    private static string Key(ShopUpgradeDef u) => "shop_" + u.Id;

    public static int LevelOf(ShopUpgradeDef u)
    {
        Profile.EnsureLoaded();
        return (int)Math.Clamp(Profile.GetMeta(Key(u)), 0L, u.MaxLevel);
    }

    public static bool IsMaxed(ShopUpgradeDef u) => LevelOf(u) >= u.MaxLevel;

    /// <summary>Price of going from <paramref name="level"/> to level+1.</summary>
    public static int CostOf(ShopUpgradeDef u, int level)
    {
        float raw = u.BaseCost * MathF.Pow(level + 1, 1.5f);
        return Math.Max(5, (int)MathF.Round(raw / 5f) * 5);
    }

    public static int NextCost(ShopUpgradeDef u) => CostOf(u, LevelOf(u));

    /// <summary>Pearls sunk into one line so far.</summary>
    public static long Invested(ShopUpgradeDef u)
    {
        long sum = 0;
        for (int i = 0; i < LevelOf(u); i++) sum += CostOf(u, i);
        return sum;
    }

    public static long TotalInvested()
    {
        long sum = 0;
        foreach (ShopUpgradeDef u in All) sum += Invested(u);
        return sum;
    }

    /// <summary>Buy the next level and save. False when maxed or unaffordable.</summary>
    public static bool TryBuy(ShopUpgradeDef u)
    {
        int level = LevelOf(u);
        if (level >= u.MaxLevel) return false;
        if (!Pearls.TrySpend(CostOf(u, level))) return false;
        Profile.SetMeta(Key(u), level + 1);
        Profile.Save();
        GD.Print($"META shop bought {u.Id} -> {level + 1}/{u.MaxLevel} balance={Pearls.Balance}");
        return true;
    }

    /// <summary>Every level sold back at full price, then saved. Returns the refund.</summary>
    public static long RefundAll()
    {
        long refund = TotalInvested();
        foreach (ShopUpgradeDef u in All) Profile.SetMeta(Key(u), 0);
        Pearls.Return(refund);
        Profile.Save();
        GD.Print($"META shop refund {refund} balance={Pearls.Balance}");
        return refund;
    }

    // ------------------------------------------------------------ the run

    /// <summary>Extra level-up rerolls per run. Read by the level-up code.</summary>
    public static int BonusRerolls => Charges("rerolls");

    /// <summary>Extra level-up banishes per run. Read by the level-up code.</summary>
    public static int BonusBanishes => Charges("banishes");

    /// <summary>Extra level-up skips per run. Read by the level-up code.</summary>
    public static int BonusSkips => Charges("skips");

    private static int Charges(string id)
        => ById(id) is { } u ? (int)MathF.Round(LevelOf(u) * u.PerLevel) : 0;

    /// <summary>Add every bought stat level to a run's sheet, tagged with
    /// <see cref="Source"/>. Removes any earlier copy first, so calling it
    /// twice cannot double the bonus.</summary>
    public static void ApplyTo(StatSheet sheet)
    {
        sheet.RemoveAllFrom(Source);
        foreach (ShopUpgradeDef u in All)
        {
            if (u.Stat is not { } stat) continue;
            int level = LevelOf(u);
            if (level > 0) sheet.Add(stat, u.Op, level * u.PerLevel, Source);
        }
    }

    /// <summary>"vitality:2,might:1" for logs.</summary>
    public static string Describe()
    {
        var parts = new List<string>();
        foreach (ShopUpgradeDef u in All)
        {
            int level = LevelOf(u);
            if (level > 0) parts.Add($"{u.Id}:{level}");
        }
        return parts.Count == 0 ? "none" : string.Join(",", parts);
    }
}
