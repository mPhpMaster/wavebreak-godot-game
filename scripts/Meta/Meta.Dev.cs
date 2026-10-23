using System.Collections.Generic;
using System.Linq;

namespace Wavebreak.Meta;

// Developer-panel setters for meta state that the game itself only ever moves
// one way (UI/DevPanel). Kept apart from the real rules so it is obvious which
// code paths a player can reach.

public static partial class MetaShop
{
    /// <summary>Set a line's level directly, no Pearls involved. Does not save.</summary>
    public static void SetLevel(ShopUpgradeDef u, int level)
        => Profile.SetMeta(Key(u), System.Math.Clamp(level, 0, u.MaxLevel));
}

public static partial class DifficultyTiers
{
    /// <summary>Count a win on <paramref name="tier"/> if it has none. Does not save.</summary>
    public static void MarkWon(DifficultyTier tier)
    {
        if (Profile.GetMeta(WonKey(tier)) <= 0) Profile.SetMeta(WonKey(tier), 1);
    }

    /// <summary>Change the tier of the run in progress. EnemyManager reads
    /// <see cref="Active"/> every tick, so it applies at once.</summary>
    public static void DevSetActive(DifficultyTier tier) => Active = tier;
}

public static partial class Challenges
{
    /// <summary>Turn one challenge on or off for the run in progress (enemy-side
    /// multipliers apply at once; player-side stat changes apply from the next
    /// run) and in the saved selection.</summary>
    public static void DevToggleLive(ChallengeDef c)
    {
        var set = new List<ChallengeDef>(Active);
        if (!set.Remove(c)) set.Add(c);
        Apply(set.OrderBy(IndexOf).ToList());
        if (IsSelected(c) != IsActive(c.Id)) Toggle(c);
    }
}
