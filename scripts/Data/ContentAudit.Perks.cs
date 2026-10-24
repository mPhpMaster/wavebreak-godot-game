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
using Wavebreak.UI;

namespace Wavebreak.Data;

/// <summary>
/// The fifteen perks (Data/Tomes.Perks.cs) in the content audit.
///
/// Per perk: 5-8 levels, every param table as long as the levels and moving
/// the right way, every level an upgrade, a card that says what the level
/// adds and the total, an icon of its own, Arabic for every string it can
/// draw, and no stat modifier written by the book. Then the integration a
/// perk inherits from being a tome: the slot limit, level-up offers, the
/// Toggler, banish, "Prefer tomes" auto-pick, and the chest roll.
///
/// The effect probe is the in-game <c>--check-perks</c> (Game/PerksCheck.cs):
/// it needs the live run, real enemies and the hit events this audit turns
/// off. Here the probe is the level table itself, which is the only source of
/// the numbers the runtime uses.
/// </summary>
public static partial class ContentAudit
{
    private static readonly object PerkBaseline = new();

    private static void AuditPerks(WeaponSystem system)
    {
        IReadOnlyList<PerkDef> perks = Perks.All;
        Check(perks.Count == 15, $"perks: {perks.Count} (want 15)");
        Check(perks.Select(p => p.Id).Distinct().Count() == perks.Count, "perks: duplicate ids");

        foreach (PerkDef p in perks)
        {
            Check(p.Id.StartsWith("perk_") && Tomes.ById(p.Id) == p, $"perk {p.Id}: not in the tome table");
            Check(p.MaxLevel is >= 5 and <= 8, $"perk {p.Id}: {p.MaxLevel} levels (want 5-8)");
            Check(p.Params.Length is >= 3 and <= 4, $"perk {p.Id}: {p.Params.Length} params");
            Check(Holes(p.Template).Count() == p.Params.Length, $"perk {p.Id}: template holes != params");
            AuditPerkLevels(p);
            AuditPerkText(p);
            AuditPerkBook(p);

            IconSpec icon = Icons.For(p);
            Check(icon.Glyph == p.Glyph && Icons.Exists(p.Glyph) && Icons.Exists(p.Badge), $"perk {p.Id}: icon {p.Glyph}/{p.Badge}");
            Check(icon.Glyph != "tome", $"perk {p.Id}: drawn as a book");
        }
        Check(perks.Count(p => p.Coop) == 2, "perks: want two team perks");
        Check(perks.Select(p => p.Rarity).Distinct().Count() >= 3, "perks: fewer than three rarities");

        foreach (string s in Perks.Strings())
            Check(Loc.ArabicFor(s) is not null, $"perk Arabic missing: \"{s}\"");

        AuditPerkSlots();
        OfferPool? runPool = OfferPool.Current;
        try
        {
            AuditPerkOffers(system);
            AuditPerkChests();
        }
        finally
        {
            OfferPool.Current = runPool;
            if (Main.Instance?.Run?.Rng is { } runRng) PickupField.Ensure(runRng);
        }
        Check(PerkProcs.Instance is not null, "perks: the runtime node is not attached to the run");
        Line($"perks count={perks.Count} levels={string.Join(",", perks.Select(p => p.MaxLevel))} lang={Loc.Language}");
    }

    /// <summary>Tables as long as the levels, moving one way, every level an upgrade.</summary>
    private static void AuditPerkLevels(PerkDef p)
    {
        foreach (PerkParam q in p.Params)
        {
            Check(q.Values.Length == p.MaxLevel, $"perk {p.Id}: '{q.Label}' has {q.Values.Length} values for {p.MaxLevel} levels");
            for (int i = 1; i < q.Values.Length; i++)
            {
                bool ok = q.LowerIsBetter ? q.Values[i] <= q.Values[i - 1] : q.Values[i] >= q.Values[i - 1];
                Check(ok, $"perk {p.Id}: '{q.Label}' gets worse at level {i + 1}");
            }
            Check(q.Values.All(v => v >= 0f), $"perk {p.Id}: '{q.Label}' negative");
        }
        for (int level = 2; level <= p.MaxLevel; level++)
        {
            bool better = p.Params.Any(q => MathF.Abs(q.At(level) - q.At(level - 1)) > 1e-4f);
            Check(better, $"perk {p.Id}: level {level} changes nothing");
        }
        // Each level reaches the runtime: V() reads the table, 0 when not held.
        Check(p.V(0, 0) == 0f && p.V(0, p.MaxLevel) == p.Params[0].Values[^1], $"perk {p.Id}: V() does not read the table");
    }

    /// <summary>Card, build-box and codex text for every level, in the current language.</summary>
    private static void AuditPerkText(PerkDef p)
    {
        var seen = new HashSet<string>();
        for (int level = 0; level < p.MaxLevel; level++)
        {
            string card = p.CardText(level);
            string total = p.DescribeTotal(level + 1);
            Check(card.Contains('\n') && !card.Contains('{') && total.Length > 10 && !total.Contains('{'),
                  $"perk {p.Id}: card text at level {level + 1}: \"{card}\"");
            Check(seen.Add(total), $"perk {p.Id}: level {level + 1} reads the same as an earlier one");
            if (level > 0) Check(p.Adds(level + 1).Length > 0, $"perk {p.Id}: level {level + 1} adds nothing on the card");
            if (Loc.IsArabic)
                Check(Loc.HasArabic(card) && Loc.HasArabic(total), $"perk {p.Id}: level {level + 1} card is not Arabic");
        }
        Offer o = Offer.ForTome(p, 0);
        Check(o.Detail == "New perk" && o.Effect == p.CardText(0), $"perk {p.Id}: new card detail '{o.Detail}'");
        Line($"perk {p.Id,-20} {p.Rarity,-9} max={p.MaxLevel} L1=\"{p.Summary(1)}\" max=\"{p.Summary(p.MaxLevel)}\"");
    }

    /// <summary>The book levels a perk to its cap and writes no stat for it.</summary>
    private static void AuditPerkBook(PerkDef p)
    {
        var sheet = new StatSheet();
        foreach (Stat s in Enum.GetValues<Stat>())
            sheet.Add(s, ModifierOp.Flat, Catalog.IsFractionStat(s) ? 0.2f : 20f, PerkBaseline);
        var before = Enum.GetValues<Stat>().ToDictionary(s => s, s => sheet.Get(s));
        var book = new TomeBook(sheet, publish: false);
        int levels = 0;
        while (book.Add(p)) levels++;
        Check(levels == p.MaxLevel && !book.CanTake(p), $"perk {p.Id}: book took {levels} levels");
        foreach (Stat s in Enum.GetValues<Stat>())
            Check(MathF.Abs(sheet.Get(s) - before[s]) < 1e-4f, $"perk {p.Id}: the book wrote {s}");
        book.Clear();
    }

    /// <summary>Perks share the tome slots: a full book refuses a new perk and
    /// a new tome alike, and a held perk still levels.</summary>
    private static void AuditPerkSlots()
    {
        var book = new TomeBook(new StatSheet(), publish: false);
        PerkDef held = Perks.All[0];
        book.Add(held);
        foreach (TomeDef t in Tomes.All.Where(t => t is not PerkDef).Take(Tomes.MaxSlots - 1)) book.Add(t);
        bool full = book.IsFull;
        bool refused = !book.CanTake(Perks.All[1]) && !book.Add(Perks.All[1]);
        bool levels = book.Add(held);
        Check(full && refused && levels, $"perk slots: full={full} newPerkRefused={refused} heldLevels={levels}");
        book.Clear();
    }

    /// <summary>Perk cards turn up, the Toggler and a banish keep them out,
    /// and "Prefer tomes" takes one.</summary>
    private static void AuditPerkOffers(WeaponSystem system)
    {
        var pool = new OfferPool(Catalog.Items, Catalog.Weapons, new Rng(0x9E2C));
        var book = new TomeBook(new StatSheet(), publish: false);
        pool.Tomes = book;
        PerkDef off = Perks.All[2], banned = Perks.All[3];
        pool.DisabledTomes.Add(off.Id);
        pool.Banished.Add(banned);
        var seen = new Dictionary<string, int>();
        int perkCards = 0, cards = 0;
        Offer? perkCard = null;
        for (int r = 0; r < 4000; r++)
            foreach (Offer o in pool.Roll(3, system, null, null))
            {
                cards++;
                if (o.Tome is not PerkDef p) continue;
                perkCards++;
                perkCard ??= o;
                seen[p.Id] = seen.TryGetValue(p.Id, out int n) ? n + 1 : 1;
                Check(o.Effect.Length > 0 && o.Detail == "New perk", $"perk card {p.Id}: '{o.Detail}'");
            }
        foreach (PerkDef p in Perks.All.Where(p => p != off && p != banned))
            Check(seen.ContainsKey(p.Id), $"perk {p.Id} never offered in 4000 rolls");
        Check(!seen.ContainsKey(off.Id), $"perk {off.Id} offered while switched off in the Toggler");
        Check(!seen.ContainsKey(banned.Id), $"perk {banned.Id} offered after a banish");
        Line($"perk offers: {perkCards}/{cards} cards ({100f * perkCards / Math.Max(1, cards):0.#}%)");

        if (perkCard is not null)
        {
            var item = Catalog.Items.First(i => i.Rarity == Rarity.Legendary);
            var hand = new List<Offer> { Offer.ForItem(item, 0), perkCard, Offer.ForWeapon(Catalog.Weapons[0], 0) };
            Offer pick = Autopilot.PickByRule(hand, AutoPickRule.PreferTomes, new Rng(3));
            Check(pick == perkCard, $"perks: 'Prefer tomes' did not pick the perk card (took {pick.Title})");
        }

        // Upgrade card: "what this level adds" plus the total.
        PerkDef up = Perks.All[0];
        book.Add(up);
        Offer upgrade = Offer.ForTome(up, 1);
        Check(upgrade.Effect.Contains('\n') && upgrade.Effect.Contains(up.Adds(2)) && upgrade.Effect.Contains(up.Summary(2)),
              $"perk upgrade card: \"{upgrade.Effect}\"");
        book.Clear();
    }

    /// <summary>Chests sometimes pay a perk: never one that cannot be taken.</summary>
    private static void AuditPerkChests()
    {
        if (Actors.Player.Instance is null) return;
        var pool = new OfferPool(Catalog.Items, Catalog.Weapons, new Rng(0xC4E5));
        var book = new TomeBook(new StatSheet(), publish: false);
        pool.Tomes = book;
        foreach (PerkDef p in Perks.All.Skip(1)) pool.DisabledTomes.Add(p.Id);
        var rng = new Rng(77);
        int gilded = 0, wrong = 0;
        for (int i = 0; i < 400; i++)
            if (PerkProcs.RollChestPerk(pool, true, rng) is { } o)
            {
                gilded++;
                if (o.Tome != Perks.All[0]) wrong++;
            }
        while (book.Add(Perks.All[0])) { }
        int maxed = 0;
        for (int i = 0; i < 200; i++) if (PerkProcs.RollChestPerk(pool, true, rng) is not null) maxed++;
        Check(gilded > 60 && wrong == 0 && maxed == 0, $"perk chests: gilded={gilded}/400 wrong={wrong} offeredWhenMaxed={maxed}");
        Line($"perk chests: gilded {gilded}/400 ({100f * gilded / 400f:0}%)");
        book.Clear();
    }
}
