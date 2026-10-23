using System.Collections.Generic;
using System.Linq;

namespace Wavebreak.Meta;

/// <summary>
/// What an unlock condition measures. Each is a single number the tracker can
/// produce at any moment; a condition is "metric reaches threshold". Keeping the
/// vocabulary this small is what lets unlocks be data: a new one is a row in
/// <see cref="Unlocks.All"/>, and only a genuinely new kind of measurement
/// needs code.
///
/// <c>Run*</c> metrics reset every run; <c>Total*</c> ones are lifetime and
/// include the run in progress, so a lifetime goal can be met mid-run and
/// announced at the moment it happens rather than on the results screen.
/// </summary>
public enum Metric
{
    TotalKills,
    TotalRuns,          // completed (ended) runs
    TotalWins,
    TotalBosses,
    TotalChests,
    RunSeconds,
    RunElites,
    RunSlams,
    RunAirJumps,
    RunLevel,
    RunItemStacks,
    RunGoldHeld,
    PeakSpeed,
    BossUntouched,      // bosses killed without the player being hit during the fight
    LevelUntouched,     // highest level reached this run before the first hit taken
    SecretsAny,         // distinct secret kinds ever found
    SecretsAll,         // 1 once every kind in SecretSpawner.Kinds has been found

    // ---- quests (Meta/Quests.cs, tracked in UnlockTracker.Quests.cs). Keyed
    // ones read UnlockDef.Key: a weapon id, a character id or a phase number.
    WeaponKills,        // lifetime kills with the weapon in Key
    RunKills,
    RunSlamKills,
    TotalSlamKills,
    RunSlideMetres,
    TotalSlideMetres,
    PhaseBossBeaten,    // 1 once the boss of phase Key has ever been beaten
    CharacterWins,      // lifetime wins as the character in Key
    CharactersWon,      // distinct characters with at least one win
    RunChests,
    PhaseUntouched,     // 1 once phase Key was cleared without taking damage in it
    UntouchedPhaseWin,  // 1 once a run was won with no damage taken in phase 1
    RunShrines,
    TotalShrines,
    RunPhase,           // furthest phase reached this run
    TotalCrits,
    RunGoldEarned,
    RunMaxedWeapons,
    RunHealed,
}

public enum RewardKind { Character, Weapon, Item, Skin, Hat }

public sealed class UnlockDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>The plain condition, shown once the unlock is visible.</summary>
    public required string Description { get; init; }

    public required Metric Metric { get; init; }
    public required float Threshold { get; init; }

    public required RewardKind Reward { get; init; }
    public required string RewardId { get; init; }

    /// <summary>Secret unlocks show as "???" with <see cref="Hint"/> until earned.</summary>
    public bool Secret { get; init; }

    /// <summary>Cryptic line for a hidden secret. Points at the act without
    /// naming it — a hint that states the rule is just a checklist.</summary>
    public string Hint { get; init; } = "";

    /// <summary>Parameter for keyed metrics (weapon id, character id, phase
    /// number). Empty for the rest.</summary>
    public string Key { get; init; } = "";
}

/// <summary>
/// Every persistent unlock, as data.
///
/// Rewards only ever add content (see <see cref="MetaContent"/>): the base game
/// is complete on a fresh profile, and unlocks widen it. Thresholds are set so
/// the first few arrive inside a player's first handful of runs — the codex
/// should feel like it is moving from the start — and the last ones ask for
/// real mastery.
/// </summary>
public static class Unlocks
{
    public static readonly IReadOnlyList<UnlockDef> All = new List<UnlockDef>
    {
        // ---- lifetime: the slow ladder anyone climbs by playing
        new() { Id = "tally_100", Name = "First Hundred", Description = "Defeat 100 enemies in total.",
                Metric = Metric.TotalKills, Threshold = 100, Reward = RewardKind.Item, RewardId = "tidemark_charm" },
        new() { Id = "tally_1000", Name = "Tidebreaker", Description = "Defeat 1,000 enemies in total.",
                Metric = Metric.TotalKills, Threshold = 1000, Reward = RewardKind.Weapon, RewardId = "undertow_chain" },
        new() { Id = "tally_5000", Name = "Shipwrecker", Description = "Defeat 5,000 enemies in total.",
                Metric = Metric.TotalKills, Threshold = 5000, Reward = RewardKind.Item, RewardId = "wreckers_lantern" },
        new() { Id = "regular", Name = "Regular", Description = "Finish 5 runs, however they end.",
                Metric = Metric.TotalRuns, Threshold = 5, Reward = RewardKind.Item, RewardId = "salvagers_hook" },
        new() { Id = "third_tide", Name = "Third Tide", Description = "Finish 3 runs, however they end.",
                Metric = Metric.TotalRuns, Threshold = 3, Reward = RewardKind.Skin, RewardId = "drifter.tidecaller" },
        new() { Id = "keyholder", Name = "Keyholder", Description = "Open 5 chests in total.",
                Metric = Metric.TotalChests, Threshold = 5, Reward = RewardKind.Item, RewardId = "salt_rusted_keyring" },

        // ---- single-run feats
        new() { Id = "hold_5", Name = "Holding the Line", Description = "Survive 5 minutes in a single run.",
                Metric = Metric.RunSeconds, Threshold = 300, Reward = RewardKind.Skin, RewardId = "kiln_sister.forgeborn" },
        new() { Id = "hold_10", Name = "Deep Water", Description = "Survive 10 minutes in a single run.",
                Metric = Metric.RunSeconds, Threshold = 600, Reward = RewardKind.Weapon, RewardId = "quarry_bell" },
        new() { Id = "breakwater", Name = "Breakwater", Description = "Win a run: survive until the tide turns.",
                Metric = Metric.TotalWins, Threshold = 1, Reward = RewardKind.Item, RewardId = "drowned_crown" },
        new() { Id = "elite_10", Name = "Crownbreaker", Description = "Defeat 10 elites in a single run.",
                Metric = Metric.RunElites, Threshold = 10, Reward = RewardKind.Item, RewardId = "stillwater_flask" },
        new() { Id = "bossfall", Name = "Bossfall", Description = "Defeat a boss.",
                Metric = Metric.TotalBosses, Threshold = 1, Reward = RewardKind.Weapon, RewardId = "lantern_moths" },
        new() { Id = "slam_25", Name = "Groundshaker", Description = "Slam 25 times in a single run.",
                Metric = Metric.RunSlams, Threshold = 25, Reward = RewardKind.Item, RewardId = "anvil_soles" },
        new() { Id = "hoard_10", Name = "Packrat", Description = "Hold 10 items at once.",
                Metric = Metric.RunItemStacks, Threshold = 10, Reward = RewardKind.Item, RewardId = "packrats_satchel" },
        new() { Id = "level_20", Name = "Attuned", Description = "Reach level 20 in a single run.",
                Metric = Metric.RunLevel, Threshold = 20, Reward = RewardKind.Item, RewardId = "resonant_fork" },
        new() { Id = "gold_300", Name = "Deep Pockets", Description = "Hold 300 gold at once.",
                Metric = Metric.RunGoldHeld, Threshold = 300, Reward = RewardKind.Item, RewardId = "gilded_tooth" },
        // 28, not the 34 cap: a dash alone peaks at 26, so this asks for one
        // more trick on top (slide downhill, slide-hop) rather than a near-limit.
        new() { Id = "speed_limit", Name = "Slipstream", Description = "Move at 28 m/s.",
                Metric = Metric.PeakSpeed, Threshold = 28, Reward = RewardKind.Skin, RewardId = "gale_courier.stormchaser" },

        // ---- secrets: hidden in the codex until found
        new() { Id = "untouched", Name = "Untouched", Description = "Defeat a boss without being hit during the fight.",
                Metric = Metric.BossUntouched, Threshold = 1, Reward = RewardKind.Item, RewardId = "mirror_scale",
                Secret = true, Hint = "The giant fell, and never once laid a finger on you." },
        new() { Id = "glasswalk", Name = "Glasswalk", Description = "Reach level 10 without taking any damage.",
                Metric = Metric.LevelUntouched, Threshold = 10, Reward = RewardKind.Item, RewardId = "unbruised_feather",
                Secret = true, Hint = "Ten lessons learned, and not one of them hurt." },
        new() { Id = "skyhook", Name = "Skyhook", Description = "Air-jump 100 times in a single run.",
                Metric = Metric.RunAirJumps, Threshold = 100, Reward = RewardKind.Item, RewardId = "gullbone_whistle",
                Secret = true, Hint = "The ground is a suggestion. Ignore it often." },
        new() { Id = "first_shrine", Name = "Something Faint", Description = "Find a hidden shrine.",
                Metric = Metric.SecretsAny, Threshold = 1, Reward = RewardKind.Item, RewardId = "moonwell_drop",
                Secret = true, Hint = "Not everything at the edge of the world is a wall." },
        new() { Id = "all_shrines", Name = "The Augur's Path", Description = "Find every kind of hidden shrine.",
                Metric = Metric.SecretsAll, Threshold = 1, Reward = RewardKind.Skin, RewardId = "gilded_augur.eclipse",
                Secret = true, Hint = "Three lights hide where the arena thins. Greet them all." },
    }.Concat(Quests.All).Concat(ExpansionQuests.All).ToList();

    public static UnlockDef? ById(string? id)
    {
        foreach (UnlockDef u in All) if (u.Id == id) return u;
        return null;
    }

    /// <summary>"Skin: Forgeborn (Kiln Sister)".</summary>
    public static string DescribeSkin(string skinId)
    {
        foreach (CharacterDef c in Characters.All)
            foreach (SkinDef k in c.Skins)
                if (k.Id == skinId) return $"Skin: {k.Name} ({c.DisplayName})";
        return $"Skin: {skinId}";
    }

    /// <summary>The unlock that grants this reward id, or null if no unlock
    /// does — meaning the content is free.</summary>
    public static string? SourceOf(string rewardId)
    {
        foreach (UnlockDef u in All) if (u.RewardId == rewardId) return u.Id;
        return null;
    }

    /// <summary>True if this content id is unlocked (or was never locked).</summary>
    public static bool IsContentUnlocked(string rewardId)
    {
        string? source = SourceOf(rewardId);
        return source is null || Profile.IsUnlocked(source);
    }

    /// <summary>"Character: Kiln Sister" — what the codex and the toast print.</summary>
    public static string DescribeReward(UnlockDef u) => u.Reward switch
    {
        RewardKind.Character => $"Character: {Characters.ById(u.RewardId)?.DisplayName ?? u.RewardId}",
        RewardKind.Skin => DescribeSkin(u.RewardId),
        RewardKind.Hat => $"Hat: {Hats.ById(u.RewardId)?.Name ?? u.RewardId}",
        RewardKind.Weapon => $"Weapon: {MetaContent.NameOf(u.RewardId)}",
        _ => $"Item: {MetaContent.NameOf(u.RewardId)}",
    };
}
