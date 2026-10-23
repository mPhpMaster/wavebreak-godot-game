using System.Collections.Generic;
using Wavebreak.Data;

namespace Wavebreak.Meta;

/// <summary>
/// The quest half of the unlock table: the Megabonk-style list of "do this
/// specific thing" goals that sits on top of the original 21 unlocks in
/// <see cref="Unlocks"/>. Same <see cref="UnlockDef"/> rows, same tracker, same
/// codex — <see cref="Unlocks.All"/> is the two lists joined.
///
/// Rewards are the three gated characters and their skins, plus skins for the
/// free four. Every reward id is granted by exactly one quest
/// (<see cref="Unlocks.SourceOf"/> takes the first match), so a new quest needs
/// a new skin, never a shared one.
///
/// Lifetime counters that the profile did not already keep are stored as plain
/// stat keys (prefix <c>q.</c>) through <see cref="Profile.Add"/>/<see cref="Profile.Best"/>;
/// the profile file stays one flat [stats] table and needs no migration.
/// </summary>
public static partial class Quests
{
    public const int PhaseCount = 3;

    private static string W(string weaponId) => Catalog.WeaponById(weaponId)?.DisplayName ?? weaponId;

    public static readonly IReadOnlyList<UnlockDef> All = new List<UnlockDef>
    {
        // ---- phase bosses: one per map, the spine of a run
        new() { Id = "q_boss_p1", Name = "First Gate", Description = "Defeat the phase 1 boss.",
                Metric = Metric.PhaseBossBeaten, Key = "1", Threshold = 1, Reward = RewardKind.Skin, RewardId = "drifter.wavecutter" },
        new() { Id = "q_boss_p2", Name = "Second Gate", Description = "Defeat the phase 2 boss.",
                Metric = Metric.PhaseBossBeaten, Key = "2", Threshold = 1, Reward = RewardKind.Character, RewardId = "breakwater_warden" },
        new() { Id = "q_boss_p3", Name = "Last Gate", Description = "Defeat the phase 3 boss.",
                Metric = Metric.PhaseBossBeaten, Key = "3", Threshold = 1, Reward = RewardKind.Skin, RewardId = "kiln_sister.ashmother" },
        new() { Id = "q_bosses_25", Name = "Giantsbane", Description = "Defeat 25 bosses in total.",
                Metric = Metric.TotalBosses, Threshold = 25, Reward = RewardKind.Skin, RewardId = "drifter.driftwood" },
        new() { Id = "q_phase_3", Name = "Third Shore", Description = "Reach phase 3 in a single run.",
                Metric = Metric.RunPhase, Threshold = 3, Reward = RewardKind.Skin, RewardId = "gale_courier.dawn" },

        // ---- win with each character
        new() { Id = "q_win_drifter", Name = "Driftwood Crown", Description = "Win a run as The Drifter.",
                Metric = Metric.CharacterWins, Key = "drifter", Threshold = 1, Reward = RewardKind.Skin, RewardId = "drifter.lighthouse" },
        new() { Id = "q_win_kiln", Name = "Banked Fires", Description = "Win a run as the Kiln Sister.",
                Metric = Metric.CharacterWins, Key = "kiln_sister", Threshold = 1, Reward = RewardKind.Skin, RewardId = "kiln_sister.slag" },
        new() { Id = "q_win_gale", Name = "Delivered", Description = "Win a run as the Gale Courier.",
                Metric = Metric.CharacterWins, Key = "gale_courier", Threshold = 1, Reward = RewardKind.Skin, RewardId = "gale_courier.zephyr" },
        new() { Id = "q_win_augur", Name = "Foretold", Description = "Win a run as the Gilded Augur.",
                Metric = Metric.CharacterWins, Key = "gilded_augur", Threshold = 1, Reward = RewardKind.Skin, RewardId = "gilded_augur.oracle" },
        new() { Id = "q_win_warden", Name = "The Wall Holds", Description = "Win a run as the Breakwater Warden.",
                Metric = Metric.CharacterWins, Key = "breakwater_warden", Threshold = 1, Reward = RewardKind.Skin, RewardId = "breakwater_warden.bastion" },
        new() { Id = "q_win_knave", Name = "Clean Getaway", Description = "Win a run as the Wrecker's Knave.",
                Metric = Metric.CharacterWins, Key = "wreckers_knave", Threshold = 1, Reward = RewardKind.Skin, RewardId = "wreckers_knave.smuggler" },
        new() { Id = "q_win_storm", Name = "Fair Weather", Description = "Win a run as the Stormwright.",
                Metric = Metric.CharacterWins, Key = "stormwright", Threshold = 1, Reward = RewardKind.Skin, RewardId = "stormwright.thunderhead" },
        // 7 = the original roster; the expansion crew is not required.
        new() { Id = "q_win_all", Name = "Full Crew", Description = "Win a run with 7 different characters.",
                Metric = Metric.CharactersWon, Threshold = 7, Reward = RewardKind.Skin, RewardId = "gilded_augur.gilt" },

        // ---- weapon mastery: lifetime kills with one weapon
        new() { Id = "q_wk_rail", Name = "Rail Hand", Description = $"Defeat 1,000 enemies with {W("splinter_rail")}.",
                Metric = Metric.WeaponKills, Key = "splinter_rail", Threshold = 1000, Reward = RewardKind.Skin, RewardId = "drifter.saltrunner" },
        new() { Id = "q_wk_halo", Name = "Kiln Keeper", Description = $"Defeat 1,000 enemies with {W("ember_halo")}.",
                Metric = Metric.WeaponKills, Key = "ember_halo", Threshold = 1000, Reward = RewardKind.Skin, RewardId = "kiln_sister.lamplighter" },
        new() { Id = "q_wk_sparkfan", Name = "Spark Post", Description = $"Defeat 1,000 enemies with {W("sparkfan")}.",
                Metric = Metric.WeaponKills, Key = "sparkfan", Threshold = 1000, Reward = RewardKind.Skin, RewardId = "gale_courier.postrider" },
        new() { Id = "q_wk_wisp", Name = "Will-o'-the-Tide", Description = $"Defeat 1,000 enemies with {W("wisp_seeker")}.",
                Metric = Metric.WeaponKills, Key = "wisp_seeker", Threshold = 1000, Reward = RewardKind.Skin, RewardId = "gilded_augur.seer" },
        new() { Id = "q_wk_cleaver", Name = "Butcher's Arc", Description = $"Defeat 750 enemies with {W("cleaver_arc")}.",
                Metric = Metric.WeaponKills, Key = "cleaver_arc", Threshold = 750, Reward = RewardKind.Skin, RewardId = "breakwater_warden.rustplate" },
        new() { Id = "q_wk_static", Name = "Grounded", Description = $"Defeat 750 enemies with {W("static_field")}.",
                Metric = Metric.WeaponKills, Key = "static_field", Threshold = 750, Reward = RewardKind.Skin, RewardId = "stormwright.rainmaker" },
        new() { Id = "q_wk_mortar", Name = "Indirect Fire", Description = $"Defeat 500 enemies with {W("mortar_shell")}.",
                Metric = Metric.WeaponKills, Key = "mortar_shell", Threshold = 500, Reward = RewardKind.Skin, RewardId = "wreckers_knave.wreckling" },

        // ---- single-run feats (the codex shows the best attempt so far)
        new() { Id = "q_level_30", Name = "Overflowing", Description = "Reach level 30 in a single run.",
                Metric = Metric.RunLevel, Threshold = 30, Reward = RewardKind.Skin, RewardId = "gale_courier.kite" },
        new() { Id = "q_run_kills", Name = "Riptide", Description = "Defeat 1,500 enemies in a single run.",
                Metric = Metric.RunKills, Threshold = 1500, Reward = RewardKind.Skin, RewardId = "wreckers_knave.gutter" },
        new() { Id = "q_run_chests", Name = "Treasure Run", Description = "Open 8 chests in a single run.",
                Metric = Metric.RunChests, Threshold = 8, Reward = RewardKind.Skin, RewardId = "wreckers_knave.cutpurse" },
        new() { Id = "q_run_slide", Name = "Skimmer", Description = "Slide 300 metres in a single run.",
                Metric = Metric.RunSlideMetres, Threshold = 300, Reward = RewardKind.Skin, RewardId = "gale_courier.squall" },
        new() { Id = "q_run_slamkills", Name = "Crater Maker", Description = "Kill 50 enemies with slams in a single run.",
                Metric = Metric.RunSlamKills, Threshold = 50, Reward = RewardKind.Skin, RewardId = "breakwater_warden.harbor" },
        new() { Id = "q_run_gold", Name = "Prospector", Description = "Pick up 1,500 gold in a single run.",
                Metric = Metric.RunGoldEarned, Threshold = 1500, Reward = RewardKind.Skin, RewardId = "kiln_sister.glassblower" },
        new() { Id = "q_run_maxed", Name = "Arsenal", Description = "Have 3 weapons at max level in a single run.",
                Metric = Metric.RunMaxedWeapons, Threshold = 3, Reward = RewardKind.Skin, RewardId = "stormwright.overcast" },
        new() { Id = "q_run_healed", Name = "Second Wind", Description = "Heal 500 health in a single run.",
                Metric = Metric.RunHealed, Threshold = 500, Reward = RewardKind.Skin, RewardId = "breakwater_warden.tidewall" },
        new() { Id = "q_clean_p1", Name = "Clean Tide", Description = "Clear phase 1 without taking any damage.",
                Metric = Metric.PhaseUntouched, Key = "1", Threshold = 1, Reward = RewardKind.Skin, RewardId = "breakwater_warden.ironwake" },

        // ---- lifetime ladders
        new() { Id = "q_chests_25", Name = "Light Fingers", Description = "Open 25 chests in total.",
                Metric = Metric.TotalChests, Threshold = 25, Reward = RewardKind.Character, RewardId = "wreckers_knave" },
        new() { Id = "q_chests_100", Name = "Deep Vault", Description = "Open 100 chests in total.",
                Metric = Metric.TotalChests, Threshold = 100, Reward = RewardKind.Skin, RewardId = "gilded_augur.moth" },
        new() { Id = "q_crits", Name = "Struck Twice", Description = "Land 2,000 critical hits in total.",
                Metric = Metric.TotalCrits, Threshold = 2000, Reward = RewardKind.Character, RewardId = "stormwright" },
        new() { Id = "q_slide_total", Name = "Long Slide Home", Description = "Slide 5,000 metres in total.",
                Metric = Metric.TotalSlideMetres, Threshold = 5000, Reward = RewardKind.Skin, RewardId = "drifter.foam" },
        new() { Id = "q_slamkills_total", Name = "Tectonic", Description = "Kill 500 enemies with slams in total.",
                Metric = Metric.TotalSlamKills, Threshold = 500, Reward = RewardKind.Skin, RewardId = "kiln_sister.cinder" },
        new() { Id = "q_kills_25000", Name = "Leviathan", Description = "Defeat 25,000 enemies in total.",
                Metric = Metric.TotalKills, Threshold = 25000, Reward = RewardKind.Skin, RewardId = "breakwater_warden.seawall" },
        new() { Id = "q_shrines_20", Name = "Devout", Description = "Activate 20 hidden shrines in total.",
                Metric = Metric.TotalShrines, Threshold = 20, Reward = RewardKind.Skin, RewardId = "stormwright.calm" },

        // ---- secrets
        new() { Id = "q_spotless", Name = "Spotless", Description = "Win a run without taking damage in phase 1.",
                Metric = Metric.UntouchedPhaseWin, Threshold = 1, Reward = RewardKind.Skin, RewardId = "wreckers_knave.inkblot",
                Secret = true, Hint = "The first shore never knew you were there, and the last one learned too late." },
        // 33 of the 34 m/s cap: a slide-hop chain off a dash, downhill.
        new() { Id = "q_speed_33", Name = "Terminal Velocity", Description = "Move at 33 m/s.",
                Metric = Metric.PeakSpeed, Threshold = 33, Reward = RewardKind.Skin, RewardId = "stormwright.tempest",
                Secret = true, Hint = "Faster than the slipstream. Faster than the storm that chases it." },
        new() { Id = "q_pilgrim", Name = "Pilgrim", Description = "Activate 3 hidden shrines in a single run.",
                Metric = Metric.RunShrines, Threshold = 3, Reward = RewardKind.Skin, RewardId = "gilded_augur.prophet",
                Secret = true, Hint = "One light on every shore, in one long walk." },
    };

    // ------------------------------------------------------------ profile keys

    public const string KeySlamKills = "q.slam_kills";
    public const string KeySlideMetres = "q.slide_m";
    public const string KeyShrines = "q.shrines";
    public const string KeyCrits = "q.crits";
    public const string KeyUntouchedWin = "q.untouched_win";

    public static string KeyWeaponKills(string weaponId) => "q.wk." + weaponId;
    public static string KeyCharacterWins(string characterId) => "q.win." + characterId;
    public static string KeyPhaseBoss(string phase) => "q.phase_boss." + phase;
    public static string KeyPhaseUntouched(string phase) => "q.phase_clean." + phase;

    /// <summary>Personal best of a single-run metric, kept so the codex can show
    /// how close the best attempt got.</summary>
    public static string KeyBest(Metric metric) => "q.best." + metric;

    /// <summary>Metrics that measure one run. The codex shows these as
    /// "best X / threshold" rather than a running total.</summary>
    public static bool IsRunMetric(Metric m) => m is Metric.RunSeconds or Metric.RunElites or Metric.RunSlams
        or Metric.RunAirJumps or Metric.RunLevel or Metric.RunItemStacks or Metric.RunGoldHeld or Metric.PeakSpeed
        or Metric.LevelUntouched or Metric.RunKills or Metric.RunSlamKills or Metric.RunSlideMetres or Metric.RunChests
        or Metric.RunShrines or Metric.RunPhase or Metric.RunGoldEarned or Metric.RunMaxedWeapons or Metric.RunHealed
        || IsMasteryRunMetric(m);

    public static int CharactersWon()
    {
        int n = 0;
        foreach (CharacterDef c in Characters.All)
            if (Profile.Get(KeyCharacterWins(c.Id)) > 0) n++;
        return n;
    }

    /// <summary>
    /// What the profile alone says about a quest, between runs: the lifetime
    /// total for ladder metrics, the best attempt for single-run ones, or -1 when
    /// it cannot be measured outside a run (a flag that is either earned or not).
    /// </summary>
    public static long StoredProgress(UnlockDef u)
    {
        long best = Profile.Get(KeyBest(u.Metric));
        return u.Metric switch
        {
            Metric.TotalKills => Profile.Get(Profile.TotalKills),
            Metric.TotalRuns => Profile.Get(Profile.Runs),
            Metric.TotalWins => Profile.Get(Profile.Wins),
            Metric.TotalBosses => Profile.Get(Profile.BossesKilled),
            Metric.TotalChests => Profile.Get(Profile.ChestsOpened),
            Metric.WeaponKills => Profile.Get(KeyWeaponKills(u.Key)),
            Metric.CharacterWins => Profile.Get(KeyCharacterWins(u.Key)),
            Metric.CharactersWon => CharactersWon(),
            Metric.TotalSlamKills => Profile.Get(KeySlamKills),
            Metric.TotalSlideMetres => Profile.Get(KeySlideMetres),
            Metric.TotalShrines => Profile.Get(KeyShrines),
            Metric.TotalCrits => Profile.Get(KeyCrits),
            Metric.SecretsAny => Profile.FoundSecrets.Count,
            // Older runs predate the best-value keys; the profile's own bests
            // fill in for the two it always kept.
            Metric.RunSeconds => System.Math.Max(best, Profile.Get(Profile.BestSeconds)),
            Metric.RunLevel => System.Math.Max(best, Profile.Get(Profile.BestLevel)),
            Metric.RunKills => System.Math.Max(best, Profile.Get(Profile.BestKills)),
            _ when IsRunMetric(u.Metric) => best,
            _ => MasteryStored(u),
        };
    }
}
