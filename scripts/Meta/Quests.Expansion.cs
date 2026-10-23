using System.Collections.Generic;

namespace Wavebreak.Meta;

/// <summary>
/// Quests added with the expansion crew and the hat wardrobe: one per new
/// character (the gate), and one "win as" per new character that grants a
/// hat. All use metrics the tracker already measures, so no tracker code is
/// involved. Joined into <see cref="Unlocks.All"/> after <see cref="Quests.All"/>.
/// </summary>
public static class ExpansionQuests
{
    public static readonly IReadOnlyList<UnlockDef> All = new List<UnlockDef>
    {
        // ---- character gates
        new() { Id = "qx_harpooner", Name = "Whalebreaker", Description = "Defeat 5 bosses in total.",
                Metric = Metric.TotalBosses, Threshold = 5, Reward = RewardKind.Character, RewardId = "harpooner" },
        new() { Id = "qx_lamplighter", Name = "Guiding Light", Description = "Reach level 25 in a single run.",
                Metric = Metric.RunLevel, Threshold = 25, Reward = RewardKind.Character, RewardId = "lamplighter" },
        new() { Id = "qx_dancer", Name = "On the Crest", Description = "Defeat 1,000 enemies in a single run.",
                Metric = Metric.RunKills, Threshold = 1000, Reward = RewardKind.Character, RewardId = "riptide_dancer" },
        new() { Id = "qx_hermit", Name = "Weathered", Description = "Win 3 runs.",
                Metric = Metric.TotalWins, Threshold = 3, Reward = RewardKind.Character, RewardId = "reef_hermit" },
        new() { Id = "qx_diver", Name = "Salvage Rights", Description = "Open 50 chests in total.",
                Metric = Metric.TotalChests, Threshold = 50, Reward = RewardKind.Character, RewardId = "wreck_diver" },
        new() { Id = "qx_gull", Name = "Updraft", Description = "Move at 31 m/s.",
                Metric = Metric.PeakSpeed, Threshold = 31, Reward = RewardKind.Character, RewardId = "gull_rider" },
        new() { Id = "qx_witch", Name = "Ebb and Flow", Description = "Activate 10 hidden shrines in total.",
                Metric = Metric.TotalShrines, Threshold = 10, Reward = RewardKind.Character, RewardId = "ebb_witch" },
        new() { Id = "qx_runner", Name = "Stormfront", Description = "Defeat 10,000 enemies in total.",
                Metric = Metric.TotalKills, Threshold = 10000, Reward = RewardKind.Character, RewardId = "squall_runner" },

        // ---- hats: win as each new character
        new() { Id = "qx_win_harpooner", Name = "Landed", Description = "Win a run as The Harpooner.",
                Metric = Metric.CharacterWins, Key = "harpooner", Threshold = 1, Reward = RewardKind.Hat, RewardId = "captains_cap" },
        new() { Id = "qx_win_lamplighter", Name = "Lamps Lit", Description = "Win a run as The Lamplighter.",
                Metric = Metric.CharacterWins, Key = "lamplighter", Threshold = 1, Reward = RewardKind.Hat, RewardId = "lantern_hat" },
        new() { Id = "qx_win_dancer", Name = "Last Dance", Description = "Win a run as the Riptide Dancer.",
                Metric = Metric.CharacterWins, Key = "riptide_dancer", Threshold = 1, Reward = RewardKind.Hat, RewardId = "anchor_crest" },
        new() { Id = "qx_win_hermit", Name = "Coral Throne", Description = "Win a run as the Reef Hermit.",
                Metric = Metric.CharacterWins, Key = "reef_hermit", Threshold = 1, Reward = RewardKind.Hat, RewardId = "coral_crown" },
        new() { Id = "qx_win_diver", Name = "Surfaced", Description = "Win a run as the Wreck Diver.",
                Metric = Metric.CharacterWins, Key = "wreck_diver", Threshold = 1, Reward = RewardKind.Hat, RewardId = "diving_helmet" },
        new() { Id = "qx_win_gull", Name = "Fair Winds", Description = "Win a run as the Gull Rider.",
                Metric = Metric.CharacterWins, Key = "gull_rider", Threshold = 1, Reward = RewardKind.Hat, RewardId = "gull_perch" },
        new() { Id = "qx_win_witch", Name = "Turn of the Tide", Description = "Win a run as the Ebb Witch.",
                Metric = Metric.CharacterWins, Key = "ebb_witch", Threshold = 1, Reward = RewardKind.Hat, RewardId = "jellyfish_cap" },
        new() { Id = "qx_win_runner", Name = "Outran the Storm", Description = "Win a run as the Squall Runner.",
                Metric = Metric.CharacterWins, Key = "squall_runner", Threshold = 1, Reward = RewardKind.Hat, RewardId = "storm_cloud" },
    };
}
