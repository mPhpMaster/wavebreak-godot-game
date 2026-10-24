using System;
using System.Collections.Generic;
using System.Linq;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Game;
using Wavebreak.Progression;
using Wavebreak.Stats;

namespace Wavebreak.Data;

/// <summary>
/// Tomes and the level-up controls (reroll, banish, skip) in the content audit.
/// Everything runs on scratch objects (<c>publish: false</c>), so the run's own
/// tome book and charges are untouched.
/// </summary>
public static partial class ContentAudit
{
    private static readonly object TomeBaseline = new();

    private static void AuditTomes(WeaponSystem system)
    {
        IReadOnlyList<TomeDef> tomes = Tomes.All;
        Line($"tomes count={tomes.Count} slots={Tomes.MaxSlots}");
        Check(tomes.Count >= 10, $"only {tomes.Count} tomes (want 10+)");

        // Ids: unique among tomes and not shared with any other content, since
        // a banish is keyed by the definition and logs print the id.
        var other = new HashSet<string>(Catalog.Weapons.Select(w => w.Id)
            .Concat(Catalog.Items.Select(i => i.Id)).Concat(Catalog.Enemies.Select(e => e.Id)));
        foreach (var dup in tomes.GroupBy(t => t.Id).Where(g => g.Count() > 1))
            Check(false, $"duplicate tome id {dup.Key}");

        foreach (TomeDef t in tomes)
        {
            Check(!string.IsNullOrWhiteSpace(t.Id) && !other.Contains(t.Id), $"tome id {t.Id} empty or shared");
            Check(Tomes.ById(t.Id) == t && t.DisplayName.Length > 0 && t.Description.Length > 0, $"tome lookup/name {t.Id}");
            Check(t.MaxLevel is >= 1 and <= 10, $"tome {t.Id} max level {t.MaxLevel}");
            Check(t.Weight > 0f, $"tome {t.Id} weight");
            if (t is PerkDef) continue;   // no stat of its own: ContentAudit.Perks.cs
            Check(t.Op == ModifierOp.Multiplicative ? t.PerLevel > 1f : t.PerLevel > 0f, $"tome {t.Id} step {t.PerLevel} is not an upgrade");
            AuditTomeLevels(t);
        }

        AuditTomeSlots();

        // The scratch pools below publish themselves as OfferPool.Current; give
        // the run's pool back afterwards so chests keep drawing from it.
        OfferPool? runPool = OfferPool.Current;
        try
        {
            AuditTomeOffers(system);
            AuditLevelUpControls(system);
        }
        finally
        {
            OfferPool.Current = runPool;
            if (Main.Instance?.Run?.Rng is { } runRng) PickupField.Ensure(runRng);
        }
    }

    /// <summary>Each level moves the stat up; the income tomes stay under the
    /// soft cap at max; removing the tome leaves nothing behind.</summary>
    private static void AuditTomeLevels(TomeDef t)
    {
        var sheet = new StatSheet();
        foreach (Stat s in Enum.GetValues<Stat>())
            sheet.Add(s, ModifierOp.Flat, Catalog.IsFractionStat(s) ? 0.2f : 20f, TomeBaseline);
        var before = Enum.GetValues<Stat>().ToDictionary(s => s, s => sheet.Get(s));

        var book = new TomeBook(sheet, publish: false);
        float last = sheet.Get(t.Stat);
        bool rising = true;
        for (int level = 1; level <= t.MaxLevel; level++)
        {
            rising &= book.Add(t);
            float now = sheet.Get(t.Stat);
            rising &= now > last + 1e-5f;
            last = now;
        }
        Check(rising && book.LevelOf(t) == t.MaxLevel, $"tome {t.Id} does not rise every level to {t.MaxLevel}");
        Check(!book.CanTake(t) && !book.Add(t), $"tome {t.Id} accepted a level past its cap");

        // Income: the same soft cap items live under. Read on a clean sheet so
        // the audit baseline does not count against it.
        if (t.Stat is Stat.XpGain or Stat.GoldGain)
        {
            var clean = new StatSheet();
            clean.Add(t.Stat, t.Op, t.ValueAtLevel(t.MaxLevel), t);
            Check(clean.Get(t.Stat) < StatSheet.IncomeCap, $"tome {t.Id} at max breaks the income cap");
        }

        book.Clear();
        foreach (Stat s in Enum.GetValues<Stat>())
            Check(MathF.Abs(sheet.Get(s) - before[s]) < 1e-4f, $"tome {t.Id} leaves residue on {s}");

        Line($"tome {t.Id,-18} {t.Rarity,-9} max={t.MaxLevel} step={t.DescribeStep()} total={t.DescribeTotal(t.MaxLevel)}");
    }

    /// <summary>Four slots: a fifth new tome is refused, an owned one still levels.</summary>
    private static void AuditTomeSlots()
    {
        var sheet = new StatSheet();
        var book = new TomeBook(sheet, publish: false);
        int learned = 0;
        foreach (TomeDef t in Tomes.All.Take(Tomes.MaxSlots)) if (book.Add(t)) learned++;
        TomeDef? extra = Tomes.All.Skip(Tomes.MaxSlots).FirstOrDefault();
        bool refused = extra is null || (!book.CanTake(extra) && !book.Add(extra));
        bool stillLevels = Tomes.All[0].MaxLevel < 2 || book.Add(Tomes.All[0]);
        Check(learned == Tomes.MaxSlots && refused && stillLevels,
              $"tome slots: learned={learned} fifthRefused={refused} ownedStillLevels={stillLevels}");
        book.Clear();
    }

    /// <summary>Every tome turns up as a card, and a full book offers only
    /// upgrades of what it holds.</summary>
    private static void AuditTomeOffers(WeaponSystem system)
    {
        var pool = new OfferPool(Catalog.Items, Catalog.Weapons, new Rng(0x70AE5));
        var book = new TomeBook(new StatSheet(), publish: false);
        pool.Tomes = book;

        var counts = new Dictionary<string, int>();
        int tomeCards = 0, cards = 0;
        for (int r = 0; r < 4000; r++)
        {
            foreach (Offer o in pool.Roll(3, system, null, null))
            {
                cards++;
                if (o.Tome is null) continue;
                tomeCards++;
                counts[o.Tome.Id] = counts.TryGetValue(o.Tome.Id, out int n) ? n + 1 : 1;
            }
        }
        foreach (TomeDef t in Tomes.All)
            Check(counts.ContainsKey(t.Id), $"tome {t.Id} never offered in 4000 rolls");
        Line($"tome offers: {tomeCards}/{cards} cards ({100f * tomeCards / Math.Max(1, cards):0}%) " +
             string.Join(" ", counts.OrderByDescending(p => p.Value).Select(p => $"{p.Key}={p.Value}")));

        foreach (TomeDef t in Tomes.All.Take(Tomes.MaxSlots)) book.Add(t);
        var held = new HashSet<TomeDef>(book.Held);
        bool onlyHeld = true;
        for (int r = 0; r < 500; r++)
            foreach (Offer o in pool.Roll(3, system, null, null))
                if (o.Tome is { } tome && !held.Contains(tome)) onlyHeld = false;
        Check(onlyHeld, "full tome book was offered a new tome");

        if (Main.Instance?.Run?.Rng is { } runRng) PickupField.Ensure(runRng);
    }

    /// <summary>Reroll, banish and skip on scratch objects: charges come from
    /// the stats and run out, a banished card never returns, and a Flat stat
    /// modifier adds a charge immediately.</summary>
    private static void AuditLevelUpControls(WeaponSystem system)
    {
        var sheet = new StatSheet();
        var pool = new OfferPool(Catalog.Items, Catalog.Weapons, new Rng(0xBA415));
        var book = new TomeBook(sheet, publish: false);
        var choices = new LevelUpChoices(pool, system, null, sheet, book, publish: false);

        int rerolls = choices.RerollsLeft, banishes = choices.BanishesLeft, skips = choices.SkipsLeft;
        Check(rerolls == (int)StatSheet.BaseValue(Stat.Rerolls) && banishes == (int)StatSheet.BaseValue(Stat.Banishes)
              && skips == (int)StatSheet.BaseValue(Stat.Skips),
              $"level-up charges {rerolls}/{banishes}/{skips} do not match the base stats");

        List<Offer> hand = choices.Roll();
        int rerolled = 0;
        while (choices.TryReroll(hand, out List<Offer> fresh)) { hand = fresh; rerolled++; if (rerolled > 20) break; }
        Check(rerolled == rerolls && choices.RerollsLeft == 0, $"rerolled {rerolled} times with {rerolls} charges");

        sheet.Add(Stat.Rerolls, ModifierOp.Flat, 1f, TomeBaseline);
        Check(choices.RerollsLeft == 1, "a +1 Rerolls modifier did not add a charge");
        sheet.RemoveAllFrom(TomeBaseline);

        Offer? target = hand.FirstOrDefault(LevelUpChoices.CanBanish);
        if (target is not null)
        {
            var others = hand.Where(o => o != target).ToList();
            bool banished = choices.TryBanish(target, others, out Offer replacement);
            bool distinct = replacement.Source is null || others.All(o => !ReferenceEquals(o.Source, replacement.Source));
            bool gone = !ReferenceEquals(replacement.Source, target.Source);
            for (int r = 0; r < 2000 && gone; r++)
                foreach (Offer o in choices.Roll())
                    if (ReferenceEquals(o.Source, target.Source)) gone = false;
            Check(banished && distinct && gone, $"banish {target.Title}: ok={banished} distinctReplacement={distinct} neverReturns={gone}");
            Check(!choices.TryBanish(hand[0], others, out _) || banishes > 1, "banish did not run out");
        }
        else
        {
            Check(false, "banish: no banishable card in the opening hand");
        }

        int skipped = 0;
        while (choices.TrySkip()) { skipped++; if (skipped > 20) break; }
        Check(skipped == skips, $"skipped {skipped} times with {skips} charges");

        Line($"level-up controls: {choices.Describe()} banished={choices.BanishedCount}");
        if (Main.Instance?.Run?.Rng is { } runRng) PickupField.Ensure(runRng);
    }

    /// <summary>End-of-run line: what the run spent and which tomes it read.</summary>
    private static void ReportLevelUp()
    {
        if (LevelUpChoices.Current is { } c)
            Line($"levelup {c.Describe()} banished={c.BanishedCount}");
        if (TomeBook.Current is { } book)
            Line("tomes " + string.Join(" ", book.Held.Select(t => $"{t.Id}=L{book.LevelOf(t)}")));
    }
}
