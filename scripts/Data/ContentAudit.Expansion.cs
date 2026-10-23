using System;
using System.Collections.Generic;
using System.Linq;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Combat.Weapons;
using Wavebreak.Core;
using Wavebreak.Game;
using Wavebreak.Meta;
using Wavebreak.Progression;
using Wavebreak.Stats;

namespace Wavebreak.Data;

/// <summary>
/// The content expansion in the audit: every new weapon has a firing
/// behaviour, every rule item has a handler, the multi-stat tomes move their
/// secondary stats and clean up, gated rewards resolve, and the offer pool's
/// mix of weapons, items and tomes stays in a sane band now that each table
/// has roughly doubled. (The generic weapon, item and tome audits already run
/// over the new entries, since they iterate the whole catalogue.)
/// </summary>
public static partial class ContentAudit
{
    private static void AuditExpansion(WeaponSystem system)
    {
        Line($"expansion weapons={Catalog.Weapons.Count} items={Catalog.Items.Count} tomes={Tomes.All.Count} " +
             $"new={Catalog.ExpansionIds.Count}+{Tomes.ExpansionIds.Count}");
        Check(Catalog.Weapons.Count >= 30, $"only {Catalog.Weapons.Count} weapons (want 30)");
        Check(Catalog.Items.Count >= 85, $"only {Catalog.Items.Count} items (want 85+)");
        Check(Tomes.All.Count >= 26, $"only {Tomes.All.Count} tomes (want 26)");
        Check(MathF.Abs(StatSheet.BaseValue(Stat.Duration) - 1f) < 1e-5f, "Duration base is not 1");

        // Weapons: behaviour <-> catalogue, both directions.
        foreach (string id in WeaponBehaviors.Ids)
            Check(Catalog.WeaponById(id) is not null && Catalog.ExpansionIds.Contains(id), $"behaviour {id} has no expansion weapon");
        foreach (WeaponDef w in Catalog.Weapons.Where(w => Catalog.ExpansionIds.Contains(w.Id)))
            Check(WeaponBehaviors.Has(w.Id), $"expansion weapon {w.Id} has no behaviour");

        // Items: every rule is wired to a real item.
        foreach (string id in ItemProcs.Handled)
            Check(Catalog.ItemById(id) is not null && Catalog.ExpansionIds.Contains(id), $"item rule {id} has no item");
        Check(ItemProcs.Handled.Distinct().Count() == ItemProcs.Handled.Length, "duplicate item rule ids");

        // Tomes: the odd ones move more than their primary stat.
        foreach (string id in TomeExtras.Ids)
        {
            TomeDef? t = Tomes.ById(id);
            Check(t is not null && Tomes.ExpansionIds.Contains(id), $"tome extra {id} has no tome");
            if (t is not null) AuditTomeExtras(t);
        }

        // Gates: each reward resolves and is granted by its row.
        foreach (UnlockDef u in ExpansionUnlocks.All)
        {
            bool known = u.Reward == RewardKind.Weapon ? Catalog.WeaponById(u.RewardId) is not null : Catalog.ItemById(u.RewardId) is not null;
            Check(known && Catalog.ExpansionIds.Contains(u.RewardId), $"gate {u.Id} rewards unknown '{u.RewardId}'");
            Check(Unlocks.SourceOf(u.RewardId) == u.Id, $"gate {u.Id}: reward {u.RewardId} is granted by {Unlocks.SourceOf(u.RewardId)}");
        }

        OfferPool? runPool = OfferPool.Current;
        try { AuditOfferMix(system); }
        finally
        {
            OfferPool.Current = runPool;
            if (Main.Instance?.Run?.Rng is { } runRng) PickupField.Ensure(runRng);
        }
    }

    private static readonly object ExtrasBaseline = new();

    private static void AuditTomeExtras(TomeDef t)
    {
        var sheet = new StatSheet();
        foreach (Stat s in Enum.GetValues<Stat>())
            sheet.Add(s, ModifierOp.Flat, Catalog.IsFractionStat(s) ? 0.2f : 20f, ExtrasBaseline);
        var before = Enum.GetValues<Stat>().ToDictionary(s => s, s => sheet.Get(s));

        var book = new TomeBook(sheet, publish: false);
        for (int i = 0; i < t.MaxLevel; i++) book.Add(t);
        int moved = Enum.GetValues<Stat>().Count(s => s != t.Stat && MathF.Abs(sheet.Get(s) - before[s]) > 1e-4f);
        Check(moved >= 1, $"tome {t.Id} moved no secondary stat");

        book.Clear();
        foreach (Stat s in Enum.GetValues<Stat>())
            Check(MathF.Abs(sheet.Get(s) - before[s]) < 1e-4f, $"tome {t.Id} extras leave residue on {s}");
        Line($"tome-extras {t.Id} secondary stats moved={moved}");
    }

    /// <summary>
    /// The shape of a level-up after the expansion: weapons, items and tomes
    /// each keep a real share of the cards, no single entry dominates, and
    /// Legendary items stay rare. Fresh scratch pool, nothing held.
    /// </summary>
    private static void AuditOfferMix(WeaponSystem system)
    {
        var pool = new OfferPool(Catalog.Items, Catalog.Weapons, new Rng(0xE7A5));
        pool.Tomes = new TomeBook(new StatSheet(), publish: false);

        int weapons = 0, items = 0, tomes = 0, cards = 0, legendary = 0;
        var per = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        const int rolls = 3000;
        for (int r = 0; r < rolls; r++)
        {
            foreach (Offer o in pool.Roll(3, system, null, null))
            {
                cards++;
                if (o.IsWeapon) weapons++;
                else if (o.IsItem) { items++; if (o.Rarity == Rarity.Legendary) legendary++; }
                else if (o.IsTome) tomes++;
                if (o.Source is { } src) per[src] = per.TryGetValue(src, out int n) ? n + 1 : 1;
            }
        }

        float W(int n) => n / (float)Math.Max(1, cards);
        Line($"offer-mix weapons={W(weapons):P0} items={W(items):P0} tomes={W(tomes):P0} legendaryItems={W(legendary):P1} " +
             $"maxSingle={W(per.Values.DefaultIfEmpty(0).Max()):P1}");
        Check(W(weapons) is > 0.10f and < 0.60f, $"offer mix: weapons {W(weapons):P0} outside 10-60%");
        Check(W(items) is > 0.25f and < 0.75f, $"offer mix: items {W(items):P0} outside 25-75%");
        Check(W(tomes) is > 0.08f and < 0.45f, $"offer mix: tomes {W(tomes):P0} outside 8-45%");
        Check(W(legendary) < 0.03f, $"offer mix: legendary items {W(legendary):P1} of cards");
        Check(W(per.Values.DefaultIfEmpty(0).Max()) < 0.05f, "offer mix: one entry takes 5%+ of all cards");

        // Every new item and weapon actually turns up.
        foreach (string id in Catalog.ExpansionIds)
        {
            object? def = (object?)Catalog.WeaponById(id) ?? Catalog.ItemById(id);
            Check(def is not null && per.ContainsKey(def), $"expansion {id} never offered in {rolls} rolls");
        }
        foreach (string id in Tomes.ExpansionIds)
            Check(Tomes.ById(id) is { } t && per.ContainsKey(t), $"expansion tome {id} never offered in {rolls} rolls");
    }
}
