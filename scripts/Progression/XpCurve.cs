using System;
using Wavebreak.Data;

namespace Wavebreak.Progression;

/// <summary>
/// The arithmetic of levelling: total experience in, level and bar position out.
///
/// <b>The cost of a level is not defined here.</b> It comes from
/// <see cref="Catalog.XpForLevel"/>, with the rest of the balance table:
///
/// <code>
///     cost(n) = 5 + 5(n-1) + (n-1)²
/// </code>
///
/// Linear plus quadratic, not exponential — an exponential curve forces a choice
/// between levels drying up and experience values inflating, and inflating them is
/// how a fifteen-minute run ends up with seven-digit numbers over every corpse.
///
/// Keeping the cost in the catalogue and the arithmetic here matters more than it
/// looks. Two curves in a build is not a duplicated constant, it is a bug: the
/// HUD's bar reads one and the inventory levels on the other, so the bar fills to
/// 80% and resets, or reaches the end and sits there. One source, several readers.
///
/// What this type adds is everything the balance table has no business knowing:
/// the cumulative thresholds, the inverse (total experience back to a level) and
/// the bar fraction. The thresholds are precomputed once into a flat array, so
/// <see cref="LevelForXp"/> is a binary search rather than a loop — the HUD asks
/// for progress every frame.
/// </summary>
public static class XpCurve
{
    /// <summary>
    /// Ceiling on the precomputed tables. Far past anything a run reaches — the
    /// cumulative cost of level 500 is over forty million — but a hard bound means
    /// no read can walk off the end of the array, whatever the curve does.
    /// </summary>
    public const int MaxLevel = 500;

    // Cost[l]      = experience to advance from level l to level l+1.
    // Threshold[l] = cumulative experience needed to *be* level l (Threshold[1] = 0).
    // Sized one past MaxLevel so the "next level" lookup at the cap stays an
    // in-range read instead of a special case at every call site.
    private static readonly int[] Cost = new int[MaxLevel + 2];
    private static readonly int[] Threshold = new int[MaxLevel + 2];

    static XpCurve()
    {
        Threshold[0] = 0;
        Threshold[1] = 0;
        Cost[0] = Catalog.XpForLevel(1);

        for (int level = 1; level <= MaxLevel; level++)
        {
            // Never zero: a level that costs nothing would let CheckLevelUps run
            // to its guard on a single orb.
            Cost[level] = Math.Max(1, Catalog.XpForLevel(level));
            Threshold[level + 1] = Threshold[level] + Cost[level];
        }

        Cost[MaxLevel + 1] = Cost[MaxLevel];
    }

    /// <summary>
    /// Experience required to advance <em>from</em> <paramref name="level"/> to the
    /// next one. This is the denominator of an experience bar, not a running total.
    /// </summary>
    public static int XpForLevel(int level)
    {
        if (level < 1) level = 1;
        return Cost[level > MaxLevel ? MaxLevel : level];
    }

    /// <summary>Cumulative experience needed to have reached <paramref name="level"/>.
    /// Level 1 costs nothing — every run starts there.</summary>
    public static int TotalXpForLevel(int level)
    {
        if (level <= 1) return 0;
        return Threshold[level > MaxLevel + 1 ? MaxLevel + 1 : level];
    }

    /// <summary>
    /// The level a lifetime experience total corresponds to. Binary search, so it
    /// costs nine comparisons at the cap rather than a walk up from level 1.
    /// </summary>
    public static int LevelForXp(int totalXp)
    {
        if (totalXp <= 0) return 1;
        if (totalXp >= Threshold[MaxLevel]) return MaxLevel;

        // Largest level whose threshold is still at or below the total.
        int lo = 1, hi = MaxLevel;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (Threshold[mid] <= totalXp) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>Experience earned since the current level began.</summary>
    public static int XpIntoLevel(int totalXp)
    {
        int level = LevelForXp(totalXp);
        return Math.Max(0, totalXp - TotalXpForLevel(level));
    }

    /// <summary>Experience the current level still needs. Zero only at the cap.</summary>
    public static int XpRemaining(int totalXp)
    {
        int level = LevelForXp(totalXp);
        if (level >= MaxLevel) return 0;
        return Math.Max(0, TotalXpForLevel(level + 1) - totalXp);
    }

    /// <summary>
    /// Progress through the current level, 0..1 — what an experience bar draws.
    ///
    /// Takes the lifetime total rather than a per-level counter on purpose: a
    /// second number to keep in sync is a second number that can be wrong, and
    /// this one is derived, so it cannot be.
    /// </summary>
    public static float ProgressToNext(int totalXp)
    {
        int level = LevelForXp(totalXp);
        if (level >= MaxLevel) return 1f;

        int cost = XpForLevel(level);
        if (cost <= 0) return 1f;

        float into = totalXp - TotalXpForLevel(level);
        return Math.Clamp(into / cost, 0f, 1f);
    }
}
