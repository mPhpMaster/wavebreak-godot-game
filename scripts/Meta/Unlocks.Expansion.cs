using System.Collections.Generic;

namespace Wavebreak.Meta;

/// <summary>
/// Unlocks that gate a handful of the expansion's catalogue entries
/// (<c>Data/Catalog.Expansion.cs</c>). Joined onto <see cref="Unlocks.All"/>.
///
/// Only the showier pieces are gated — two weapons and three items, out of
/// twelve and forty-three — so a fresh profile still sees almost all of the
/// expansion. <see cref="MetaContent.OfferItems"/> and
/// <see cref="MetaContent.OfferWeapons"/> drop any catalogue entry whose id is
/// a reward here until it is earned; base content has no reward row and is
/// never filtered. Each reward id appears in exactly one row.
/// </summary>
public static class ExpansionUnlocks
{
    public static readonly IReadOnlyList<UnlockDef> All = new List<UnlockDef>
    {
        new() { Id = "x_rail_300", Name = "Steady Hand", Description = "Defeat 300 enemies with the Splinter Rail.",
                Metric = Metric.WeaponKills, Key = "splinter_rail", Threshold = 300, Reward = RewardKind.Weapon, RewardId = "driftwood_sentry" },
        new() { Id = "x_bosses_3", Name = "Eye of the Storm", Description = "Defeat 3 bosses in total.",
                Metric = Metric.TotalBosses, Threshold = 3, Reward = RewardKind.Weapon, RewardId = "maelstrom_seed" },
        new() { Id = "x_gold_held_500", Name = "Heavy Purse", Description = "Hold 500 gold at once.",
                Metric = Metric.RunGoldHeld, Threshold = 500, Reward = RewardKind.Item, RewardId = "misers_anchor" },
        new() { Id = "x_gold_earned_2000", Name = "Salvage Rights", Description = "Earn 2,000 gold in a single run.",
                Metric = Metric.RunGoldEarned, Threshold = 2000, Reward = RewardKind.Item, RewardId = "tithe_of_the_deep" },
        new() { Id = "x_wins_2", Name = "Twice Returned", Description = "Win 2 runs.",
                Metric = Metric.TotalWins, Threshold = 2, Reward = RewardKind.Item, RewardId = "mariners_last_wish" },
    };
}
