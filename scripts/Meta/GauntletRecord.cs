using System.Collections.Generic;
using Wavebreak.Game.Puzzle;

namespace Wavebreak.Meta;

/// <summary>
/// The Puzzle Gauntlet's best on this profile: a score (gold 3, silver 2,
/// bronze 1, a skip 0 per puzzle), the golds of that run, and how many
/// gauntlets were finished. Kept as profile meta values; written once per
/// finished gauntlet, on every machine of a co-op team (each its own profile).
/// </summary>
public static class GauntletRecord
{
    private const string BestKey = "gauntlet_best";
    private const string GoldsKey = "gauntlet_best_golds";
    private const string RunsKey = "gauntlet_runs";

    public static int Score(IEnumerable<RewardTier> medals)
    {
        int s = 0;
        foreach (RewardTier t in medals)
            s += t switch { RewardTier.Gold => 3, RewardTier.Silver => 2, RewardTier.Bronze => 1, _ => 0 };
        return s;
    }

    public static int MaxScore => 3 * Routes.GauntletPuzzles;

    public static int Best
    {
        get
        {
            Profile.EnsureLoaded();
            return (int)Profile.GetMeta(BestKey);
        }
    }

    public static int BestGolds
    {
        get
        {
            Profile.EnsureLoaded();
            return (int)Profile.GetMeta(GoldsKey);
        }
    }

    public static int Runs
    {
        get
        {
            Profile.EnsureLoaded();
            return (int)Profile.GetMeta(RunsKey);
        }
    }

    /// <summary>A finished gauntlet: count it, keep it if it is the best.
    /// True when it beat the best before it.</summary>
    public static bool Record(IReadOnlyList<RewardTier> medals)
    {
        Profile.EnsureLoaded();
        int score = Score(medals), golds = 0;
        foreach (RewardTier t in medals) if (t == RewardTier.Gold) golds++;
        bool best = score > Best || Runs == 0;
        Profile.SetMeta(RunsKey, Runs + 1);
        if (best)
        {
            Profile.SetMeta(BestKey, score);
            Profile.SetMeta(GoldsKey, golds);
        }
        Profile.Save();
        return best;
    }
}
