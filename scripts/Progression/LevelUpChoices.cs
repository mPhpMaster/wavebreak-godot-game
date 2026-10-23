using System;
using System.Collections.Generic;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Stats;

namespace Wavebreak.Progression;

/// <summary>Which level-up control was spent.</summary>
public enum LevelUpControl { Reroll, Banish, Skip }

/// <summary>Published when the player (or the autopilot) spends a reroll,
/// banish or skip. <see cref="What"/> names the banished definition's id.</summary>
public readonly struct LevelUpControlUsed
{
    public readonly LevelUpControl Control;
    public readonly int Left;
    public readonly string What;
    public LevelUpControlUsed(LevelUpControl control, int left, string what)
    {
        Control = control; Left = left; What = what;
    }
}

/// <summary>
/// The run's level-up rolls plus the three ways out of a bad one: reroll the
/// whole hand, banish one card from the pool for the rest of the run, or skip
/// the level for a little gold.
///
/// <b>Charges are stats.</b> The totals are <see cref="Stat.Rerolls"/>,
/// <see cref="Stat.Banishes"/> and <see cref="Stat.Skips"/> on the player's
/// sheet; this class only counts what has been spent. "Left" is total minus
/// spent, so an item, character passive or unlock that adds a Flat +1 works
/// immediately and without any code here.
///
/// The panel and the autopilot both go through this, so a human and a bot
/// spend the same charges under the same rules.
/// </summary>
public sealed class LevelUpChoices
{
    /// <summary>The live run's controller, or null outside a run.</summary>
    public static LevelUpChoices? Current { get; private set; }

    /// <summary>How many cards a level-up shows.</summary>
    public const int Cards = 3;

    private readonly OfferPool _pool;
    private readonly WeaponSystem? _weapons;
    private readonly Inventory? _inventory;
    private readonly StatSheet _stats;
    private readonly bool _publish;

    private int _rerollsUsed;
    private int _banishesUsed;
    private int _skipsUsed;

    /// <param name="publish">False for a scratch controller (the content audit):
    /// it neither replaces <see cref="Current"/> nor publishes events.</param>
    public LevelUpChoices(OfferPool pool, WeaponSystem? weapons, Inventory? inventory, StatSheet stats,
                          TomeBook? tomes, bool publish = true)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _weapons = weapons;
        _inventory = inventory;
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        _publish = publish;
        _pool.Tomes = tomes;
        if (publish) Current = this;
    }

    public OfferPool Pool => _pool;

    // ------------------------------------------------------------- charges

    public int RerollsTotal => Charges(Stat.Rerolls);
    public int BanishesTotal => Charges(Stat.Banishes);
    public int SkipsTotal => Charges(Stat.Skips);

    public int RerollsLeft => Math.Max(0, RerollsTotal - _rerollsUsed);
    public int BanishesLeft => Math.Max(0, BanishesTotal - _banishesUsed);
    public int SkipsLeft => Math.Max(0, SkipsTotal - _skipsUsed);

    public int RerollsUsed => _rerollsUsed;
    public int BanishesUsed => _banishesUsed;
    public int SkipsUsed => _skipsUsed;

    /// <summary>Definitions banished so far, in no particular order.</summary>
    public int BanishedCount => _pool.Banished.Count;

    private int Charges(Stat stat) => Math.Max(0, (int)MathF.Floor(_stats.Get(stat) + 1e-4f));

    // --------------------------------------------------------------- rolls

    /// <summary>A fresh hand for a level-up.</summary>
    public List<Offer> Roll(int count = Cards) => _pool.Roll(count, _weapons, _inventory, _stats);

    /// <summary>
    /// Spend a reroll and draw a new hand of the same size. Tries a few times to
    /// avoid handing back the identical set — a reroll that changes nothing reads
    /// as a bug, even when the pool is merely small. False, and nothing spent,
    /// when no charge is left.
    /// </summary>
    public bool TryReroll(IReadOnlyList<Offer> current, out List<Offer> fresh)
    {
        fresh = new List<Offer>();
        if (RerollsLeft <= 0) return false;

        int count = Math.Max(1, current.Count);
        for (int attempt = 0; attempt < 4; attempt++)
        {
            fresh = Roll(count);
            if (!SameSet(fresh, current)) break;
        }

        _rerollsUsed++;
        Announce(LevelUpControl.Reroll, RerollsLeft, "");
        return true;
    }

    /// <summary>True when <paramref name="offer"/> can be banished at all:
    /// supply payouts are not in the pool, so there is nothing to remove.</summary>
    public static bool CanBanish(Offer? offer) => offer?.Source is not null;

    /// <summary>
    /// Spend a banish: remove <paramref name="offer"/>'s definition from the
    /// pool for the rest of the run and return a replacement card that is not
    /// any of <paramref name="others"/>. Held weapons and tomes simply stop
    /// being offered upgrades; nothing is taken from the player.
    /// </summary>
    public bool TryBanish(Offer offer, IReadOnlyList<Offer> others, out Offer replacement)
    {
        replacement = offer;
        if (BanishesLeft <= 0 || offer.Source is not { } source) return false;

        _pool.Banished.Add(source);
        _banishesUsed++;
        replacement = _pool.RollReplacement(others, _weapons, _inventory, _stats);
        string id = offer.Weapon?.Id ?? offer.Item?.Id ?? offer.Tome?.Id ?? "";
        Announce(LevelUpControl.Banish, BanishesLeft, id);
        return true;
    }

    /// <summary>Gold paid for skipping a level: small on purpose, the skip's
    /// value is keeping a build clean, not income. Scaled by Gold Gain.</summary>
    public static int SkipGold(int level) => 10 + 2 * Math.Max(1, level);

    /// <summary>Spend a skip and pay the consolation. False when none left.</summary>
    public bool TrySkip()
    {
        if (SkipsLeft <= 0) return false;
        _skipsUsed++;
        _inventory?.GrantGold(SkipGold(_inventory.Level));
        Announce(LevelUpControl.Skip, SkipsLeft, "");
        return true;
    }

    /// <summary>"rerolls=1/2 banishes=0/1 skips=3/3" — used/total, for logs.</summary>
    public string Describe()
        => $"rerolls={_rerollsUsed}/{RerollsTotal} banishes={_banishesUsed}/{BanishesTotal} skips={_skipsUsed}/{SkipsTotal}";

    private void Announce(LevelUpControl control, int left, string what)
    {
        if (_publish) EventBus.Publish(new LevelUpControlUsed(control, left, what));
    }

    private static bool SameSet(IReadOnlyList<Offer> a, IReadOnlyList<Offer> b)
    {
        if (a.Count != b.Count) return false;
        foreach (Offer x in a)
        {
            bool found = false;
            foreach (Offer y in b)
                if (x.Source is not null ? ReferenceEquals(x.Source, y.Source) : x.Title == y.Title) { found = true; break; }
            if (!found) return false;
        }
        return true;
    }
}
