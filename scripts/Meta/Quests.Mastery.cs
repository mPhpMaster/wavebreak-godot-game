using System.Collections.Generic;
using Wavebreak.Data;

namespace Wavebreak.Meta;

/// <summary>
/// Profile keys and codex helpers for the mastery quests
/// (<see cref="MasteryQuests"/>). Same rule as the original quest keys: plain
/// <c>q.</c>-prefixed stat entries, so the profile stays one flat table.
/// </summary>
public static partial class Quests
{
    // ---- one-off feats (Metric.Feat, Key = one of these)
    public const string FeatNoChestWin = "no_chest_win";
    public const string FeatOneWeaponWin = "one_weapon_win";
    public const string FeatGreedyWin = "greedy_win";
    public const string FeatCrowdedWin = "crowded_win";
    public const string FeatLowLevelWin = "low_level_win";
    public const string FeatBossMegacrit = "boss_megacrit";
    public const string FeatBossFreeze = "boss_freeze";
    public const string FeatBossSlam = "boss_slam";
    public const string FeatQuietMinute = "quiet_minute";

    /// <summary>The stat key a row reads. Built once per row (see
    /// <see cref="UnlockDef.StatKey"/>); "" for metrics that have none.</summary>
    internal static string StatKeyFor(UnlockDef u) => u.Metric switch
    {
        Metric.WeaponKills => KeyWeaponKills(u.Key),
        Metric.CharacterWins => KeyCharacterWins(u.Key),
        Metric.PhaseBossBeaten => KeyPhaseBoss(u.Key),
        Metric.PhaseUntouched => KeyPhaseUntouched(u.Key),
        Metric.WeaponMaxed => KeyWeaponMaxed(u.Key),
        Metric.TomeMaxed => KeyTomeMaxed(u.Key),
        Metric.CharacterKills => KeyCharacterKills(u.Key),
        Metric.CharacterHardWins => KeyCharacterHardWins(u.Key),
        Metric.DifficultyWins => KeyDifficultyWins(u.Key),
        Metric.MapClears => KeyMapClears(u.Key),
        Metric.ChallengeWins => KeyChallengeWins(u.Key),
        Metric.Feat => KeyFeat(u.Key),
        _ => LifetimeKey(u.Metric) ?? "",
    };

    public static string KeyWeaponMaxed(string id) => "q.wmax." + id;
    public static string KeyTomeMaxed(string id) => "q.tmax." + id;
    public static string KeyCharacterKills(string id) => "q.ck." + id;
    public static string KeyCharacterHardWins(string id) => "q.hwin." + id;
    public static string KeyDifficultyWins(string id) => "q.dwin." + id;
    public static string KeyMapClears(string id) => "q.map." + id;
    public static string KeyChallengeWins(string id) => "q.cwin." + id;
    public static string KeyFeat(string id) => "q.feat." + id;

    /// <summary>The stat key of an un-keyed lifetime metric, or null.</summary>
    public static string? LifetimeKey(Metric m) => m switch
    {
        Metric.TotalBurns => "q.burns",
        Metric.TotalFreezes => "q.freezes",
        Metric.TotalShocks => "q.shocks",
        Metric.TotalPoisons => "q.poisons",
        Metric.TotalMegacrits => "q.megacrits",
        Metric.TotalMerchantGold => "q.merchant_gold",
        Metric.TotalMicrowaves => "q.microwaves",
        Metric.TotalPots => "q.pots",
        Metric.TotalCharges => "q.charges",
        Metric.TotalShrineChallenges => "q.shrine_challenges",
        Metric.TotalGreed => "q.greed",
        Metric.TotalJumps => "q.jumps",
        Metric.TotalAirJumps => "q.air_jumps",
        Metric.TotalDashes => "q.dashes",
        Metric.TotalSlams => Profile.TotalSlams,
        Metric.TotalElites => Profile.ElitesKilled,
        Metric.TotalGoldEarned => "q.gold",
        _ => null,
    };

    public static bool IsMasteryRunMetric(Metric m) => m is Metric.RunFreezes or Metric.RunMegacrits
        or Metric.RunMerchantBuys or Metric.RunPots or Metric.RunGreed or Metric.RunDashes
        or Metric.SwarmSeconds or Metric.RunTomesMaxed;

    private static string[]? _mapKeys, _challengeKeys;

    /// <summary>Every map's clear key, built once.</summary>
    internal static string[] MapKeys
    {
        get
        {
            if (_mapKeys is not null) return _mapKeys;
            var list = new List<string>();
            foreach (StageDef s in Stages.All) if (!list.Contains(KeyMapClears(s.Id))) list.Add(KeyMapClears(s.Id));
            return _mapKeys = list.ToArray();
        }
    }

    internal static string[] ChallengeKeys
    {
        get
        {
            if (_challengeKeys is not null) return _challengeKeys;
            var keys = new string[Challenges.All.Count];
            for (int i = 0; i < keys.Length; i++) keys[i] = KeyChallengeWins(Challenges.All[i].Id);
            return _challengeKeys = keys;
        }
    }

    internal static int DistinctPositive(string[] keys)
    {
        int n = 0;
        foreach (string k in keys) if (Profile.Get(k) > 0) n++;
        return n;
    }

    /// <summary><see cref="StoredProgress"/> for the mastery metrics: the
    /// lifetime total, or -1 for one-off flags (shown as LOCKED).</summary>
    private static long MasteryStored(UnlockDef u) => u.Metric switch
    {
        Metric.WeaponMaxed or Metric.TomeMaxed or Metric.Feat => -1,
        Metric.MapsCleared => DistinctPositive(MapKeys),
        Metric.ChallengesWon => DistinctPositive(ChallengeKeys),
        _ when u.StatKey.Length > 0 => Profile.Get(u.StatKey),
        _ => -1,
    };
}

/// <summary>
/// The mastery layer of the quest book, joined last onto <see cref="Unlocks.All"/>:
/// one kill and one max-level quest per weapon, one per tome, a kill count and
/// a Hard win per character, wins per tier, map and challenge, element and
/// economy ladders, movement, the Final Swarm, and a handful of secret feats.
///
/// Rewards: the second wave of character skins (each character ends with six
/// or more), six gated expansion items and two gated expansion weapons, and
/// Pearls for the rest. Pearl rows carry a unique "pearls.&lt;id&gt;" reward id
/// so <see cref="Unlocks.SourceOf"/> never mistakes one for a content gate.
///
/// Per-weapon and per-tome rows are generated from the catalogue, so content
/// added later gets its quests without anyone remembering to write them.
/// </summary>
public static class MasteryQuests
{
    public static readonly IReadOnlyList<UnlockDef> All = Build();

    // ------------------------------------------------------------ helpers

    private static UnlockDef P(string id, string name, string desc, Metric m, float threshold, int pearls,
                               string key = "", string? hint = null)
        => new()
        {
            Id = id, Name = name, Description = desc, Metric = m, Threshold = threshold, Key = key,
            Reward = RewardKind.Pearls, RewardId = "pearls." + id, Pearls = pearls,
            Secret = hint is not null, Hint = hint ?? "",
        };

    private static UnlockDef R(string id, string name, string desc, Metric m, float threshold, RewardKind kind, string reward,
                               string key = "", string? hint = null)
        => new()
        {
            Id = id, Name = name, Description = desc, Metric = m, Threshold = threshold, Key = key,
            Reward = kind, RewardId = reward, Secret = hint is not null, Hint = hint ?? "",
        };

    private static string CharName(string id) => Characters.ById(id)?.DisplayName ?? id;

    // Weapons whose quests grant a character skin (the character who starts
    // with it) instead of Pearls: (kill skin, max-level skin).
    private static readonly Dictionary<string, (string? Kills, string? Max)> WeaponSkins = new()
    {
        ["brinespike"] = ("harpooner.narwhal", "harpooner.icebreaker"),
        ["searing_longshot"] = ("lamplighter.flare", "lamplighter.foglamp"),
        ["glasswire_needles"] = ("riptide_dancer.glasswing", "riptide_dancer.undertow"),
        ["forgefall_maul"] = ("reef_hermit.anemone", "reef_hermit.urchin"),
        ["mortar_shell"] = (null, "wreck_diver.anglerlight"),
        ["hailstone_cluster"] = ("gull_rider.sleet", "gull_rider.skua"),
        ["rimeheart"] = ("ebb_witch.rime", "ebb_witch.neap"),
        ["blightwasp_swarm"] = ("squall_runner.wasp", "squall_runner.hailrunner"),
    };

    /// <summary>Weapons that already have a kill quest in <see cref="Quests"/>.</summary>
    private static readonly HashSet<string> HasKillQuest = new()
    {
        "splinter_rail", "ember_halo", "sparkfan", "wisp_seeker", "cleaver_arc", "static_field", "mortar_shell",
    };

    // Per character: (kills-as skin or null for Pearls, Hard-win skin).
    private static readonly (string Id, string? KillSkin, string HardSkin)[] CrewSkins =
    {
        ("drifter", null, "drifter.stormglass"),
        ("kiln_sister", null, "kiln_sister.blackglass"),
        ("gale_courier", null, "gale_courier.nightpost"),
        ("gilded_augur", null, "gilded_augur.verdigris"),
        ("breakwater_warden", null, "breakwater_warden.deepwatch"),
        ("wreckers_knave", "wreckers_knave.bilge", "wreckers_knave.redtide"),
        ("stormwright", "stormwright.squallglass", "stormwright.aurora"),
        ("harpooner", "harpooner.blackwater", "harpooner.leviathan"),
        ("lamplighter", "lamplighter.hearth", "lamplighter.lighthouse"),
        ("riptide_dancer", "riptide_dancer.coral", "riptide_dancer.moonwake"),
        ("reef_hermit", "reef_hermit.kelpbeard", "reef_hermit.abyssal"),
        ("wreck_diver", "wreck_diver.rust", "wreck_diver.goldleaf"),
        ("gull_rider", "gull_rider.puffin", "gull_rider.albatross"),
        ("ebb_witch", "ebb_witch.brackish", "ebb_witch.spring_tide"),
        ("squall_runner", "squall_runner.thunder", "squall_runner.downpour"),
    };

    private static readonly (string Id, string Name, string Clear, string Veteran)[] Maps =
    {
        ("thornmeadow", "Thornmeadow", "Through the Thorns", "Meadow Warden"),
        ("rimeglass", "Rimeglass Plateau", "Glass Underfoot", "Rime Walker"),
        ("cinder", "Cinder Reach", "Ash on the Wind", "Reach Runner"),
        ("gravemoor", "Gravemoor", "Rest Disturbed", "Moor Sexton"),
        ("shallows", "Saltglass Shallows", "Low Water", "Shallows Pilot"),
    };

    // ------------------------------------------------------------- table

    private static List<UnlockDef> Build()
    {
        var list = new List<UnlockDef>(220);

        // ---- weapons: kills and max level, generated from every weapon table
        var weapons = new List<WeaponDef>();
        foreach (WeaponDef w in Catalog.Weapons) weapons.Add(w);
        foreach (WeaponDef w in MetaContent.Weapons) if (!weapons.Exists(x => x.Id == w.Id)) weapons.Add(w);
        foreach (WeaponDef w in weapons)
        {
            WeaponSkins.TryGetValue(w.Id, out var skins);
            if (!HasKillQuest.Contains(w.Id))
            {
                string id = "qm_wk_" + w.Id, name = $"Seasoned: {w.DisplayName}";
                string desc = $"Defeat 500 enemies with {w.DisplayName}.";
                list.Add(skins.Kills is { } ks
                    ? R(id, name, desc, Metric.WeaponKills, 500, RewardKind.Skin, ks, w.Id)
                    : P(id, name, desc, Metric.WeaponKills, 500, 40, w.Id));
            }
            {
                string id = "qm_wmax_" + w.Id, name = $"Tempered: {w.DisplayName}";
                string desc = $"Raise {w.DisplayName} to its maximum level (Lv {w.MaxLevel}) in a run.";
                list.Add(skins.Max is { } ms
                    ? R(id, name, desc, Metric.WeaponMaxed, 1, RewardKind.Skin, ms, w.Id)
                    : P(id, name, desc, Metric.WeaponMaxed, 1, 30, w.Id));
            }
        }

        // ---- tomes: max level, one each
        foreach (TomeDef t in Tomes.All)
            list.Add(P("qm_tmax_" + t.Id, $"Learned by Heart: {t.DisplayName}",
                       $"Raise {t.DisplayName} to its maximum level (Lv {t.MaxLevel}) in a run.",
                       Metric.TomeMaxed, 1, 25, t.Id));

        // ---- characters: a long career, and a win on Hard
        foreach (var (cid, killSkin, hardSkin) in CrewSkins)
        {
            string who = CharName(cid);
            string kid = "qm_ck_" + cid, kname = $"Old Salt: {who}", kdesc = $"Defeat 2,500 enemies in total as {who}.";
            list.Add(killSkin is not null
                ? R(kid, kname, kdesc, Metric.CharacterKills, 2500, RewardKind.Skin, killSkin, cid)
                : P(kid, kname, kdesc, Metric.CharacterKills, 2500, 60, cid));
            list.Add(R("qm_hw_" + cid, $"Heavy Seas: {who}", $"Win a run as {who} on Hard or Nightmare.",
                       Metric.CharacterHardWins, 1, RewardKind.Skin, hardSkin, cid));
        }

        // ---- difficulty tiers
        list.Add(R("qm_diff_hard", "Rough Water", "Win a run on Hard.",
                   Metric.DifficultyWins, 1, RewardKind.Weapon, "thunderhead_censer", "hard"));
        list.Add(P("qm_diff_hard_5", "Weathered Hull", "Win 5 runs on Hard.", Metric.DifficultyWins, 5, 200, "hard"));
        list.Add(R("qm_diff_nightmare", "Black Squall", "Win a run on Nightmare.",
                   Metric.DifficultyWins, 1, RewardKind.Item, "eye_of_the_maelstrom", "nightmare"));
        list.Add(P("qm_diff_nightmare_3", "Storm-Born", "Win 3 runs on Nightmare.", Metric.DifficultyWins, 3, 400, "nightmare"));

        // ---- maps: a first clear and a veteran's ten
        foreach (var (mid, mname, clear, veteran) in Maps)
        {
            string desc = $"Defeat the phase boss on {mname}.";
            list.Add(mid == "shallows"
                ? R("qm_map_" + mid, clear, desc, Metric.MapClears, 1, RewardKind.Weapon, "lighthouse_beam", mid)
                : P("qm_map_" + mid, clear, desc, Metric.MapClears, 1, 40, mid));
            list.Add(P("qm_map10_" + mid, veteran, $"Defeat the phase boss on {mname} 10 times.", Metric.MapClears, 10, 120, mid));
        }
        list.Add(P("qm_maps_all", "Chartmaker", "Defeat a phase boss on every map.", Metric.MapsCleared, Maps.Length, 150));

        // ---- challenges: a win with each, and collections
        foreach (ChallengeDef c in Challenges.All)
            list.Add(P("qm_chal_" + c.Id, $"Weathered: {c.Name}", $"Win a run with the {c.Name} challenge active.",
                       Metric.ChallengeWins, 1, 60, c.Id));
        list.Add(P("qm_chal_6", "Storm Chaser", "Win with 6 different challenges.", Metric.ChallengesWon, 6, 250));
        list.Add(P("qm_chal_all", "Every Weather", $"Win with all {Challenges.All.Count} challenges.", Metric.ChallengesWon, Challenges.All.Count, 600));

        // ---- elements
        list.Add(P("qm_burn_1000", "Kindler", "Set 1,000 enemies alight in total.", Metric.TotalBurns, 1000, 40));
        list.Add(R("qm_burn_2500", "Signal Fires", "Set 2,500 enemies alight in total.", Metric.TotalBurns, 2500, RewardKind.Skin, "lamplighter.ember"));
        list.Add(P("qm_burn_10000", "Coast Ablaze", "Set 10,000 enemies alight in total.", Metric.TotalBurns, 10000, 150));
        list.Add(P("qm_freeze_250", "Cold Snap", "Freeze 250 enemies solid in total.", Metric.TotalFreezes, 250, 40));
        list.Add(R("qm_freeze_1000", "Pack Ice", "Freeze 1,000 enemies solid in total.", Metric.TotalFreezes, 1000, RewardKind.Skin, "ebb_witch.moonpull"));
        list.Add(P("qm_freeze_run", "Icebound Shore", "Freeze 100 enemies in a single run.", Metric.RunFreezes, 100, 80));
        list.Add(P("qm_shock_1000", "Live Wire", "Shock 1,000 enemies in total.", Metric.TotalShocks, 1000, 40));
        list.Add(R("qm_shock_5000", "St. Elmo's Fire", "Shock 5,000 enemies in total.", Metric.TotalShocks, 5000, RewardKind.Skin, "squall_runner.static"));
        list.Add(P("qm_poison_1000", "Bitter Water", "Apply 1,000 poison stacks in total.", Metric.TotalPoisons, 1000, 40));
        list.Add(P("qm_poison_10000", "Red Tide Bloom", "Apply 10,000 poison stacks in total.", Metric.TotalPoisons, 10000, 150));
        list.Add(R("qm_mega_100", "Rogue Wave", "Land 100 megacrits in total.", Metric.TotalMegacrits, 100, RewardKind.Item, "kraken_ink"));
        list.Add(P("qm_mega_1000", "Hundred-Year Wave", "Land 1,000 megacrits in total.", Metric.TotalMegacrits, 1000, 150));
        list.Add(P("qm_mega_run", "Spring Tide Strike", "Land 50 megacrits in a single run.", Metric.RunMegacrits, 50, 80));

        // ---- economy: merchants, microwaves, pots, shrines, greed
        list.Add(P("qm_merchant_1000", "Good Custom", "Spend 1,000 gold at merchants in total.", Metric.TotalMerchantGold, 1000, 40));
        list.Add(R("qm_merchant_5000", "Harbour Patron", "Spend 5,000 gold at merchants in total.", Metric.TotalMerchantGold, 5000, RewardKind.Skin, "wreck_diver.saltstained"));
        list.Add(P("qm_merchant_run", "Shopping Spree", "Buy 5 items from merchants in a single run.", Metric.RunMerchantBuys, 5, 60));
        list.Add(R("qm_microwave_5", "Second Helping", "Duplicate 5 items in microwaves in total.", Metric.TotalMicrowaves, 5, RewardKind.Item, "lifebuoy"));
        list.Add(R("qm_microwave_15", "Galley Cook", "Duplicate 15 items in microwaves in total.", Metric.TotalMicrowaves, 15, RewardKind.Skin, "wreck_diver.copperhelm"));
        list.Add(R("qm_pots_50", "Crockery", "Break 50 pots in total.", Metric.TotalPots, 50, RewardKind.Item, "powder_keg"));
        list.Add(R("qm_pots_300", "Bull in the Galley", "Break 300 pots in total.", Metric.TotalPots, 300, RewardKind.Skin, "reef_hermit.driftshell"));
        list.Add(P("qm_pots_run", "Smash and Grab", "Break 25 pots in a single run.", Metric.RunPots, 25, 60));
        list.Add(R("qm_charge_10", "Shore Lights", "Fill 10 charge shrines in total.", Metric.TotalCharges, 10, RewardKind.Item, "fathom_engine"));
        list.Add(P("qm_charge_50", "Keeper of Lights", "Fill 50 charge shrines in total.", Metric.TotalCharges, 50, 150));
        list.Add(P("qm_shrinechal_5", "Dared", "Win 5 challenge shrines in total.", Metric.TotalShrineChallenges, 5, 60));
        list.Add(P("qm_shrinechal_25", "Double Dared", "Win 25 challenge shrines in total.", Metric.TotalShrineChallenges, 25, 180));
        list.Add(P("qm_greed_run", "Gilded Chains", "Take 5 greed stacks in a single run.", Metric.RunGreed, 5, 80));
        list.Add(P("qm_greed_25", "Covetous", "Take 25 greed stacks in total.", Metric.TotalGreed, 25, 120));
        list.Add(P("qm_gold_25000", "Treasure Fleet", "Pick up 25,000 gold in total.", Metric.TotalGoldEarned, 25000, 150));

        // ---- movement
        list.Add(P("qm_jumps_2000", "Spring-Heeled", "Jump 2,000 times in total.", Metric.TotalJumps, 2000, 50));
        list.Add(P("qm_airjumps_1000", "Spindrift", "Air-jump 1,000 times in total.", Metric.TotalAirJumps, 1000, 50));
        list.Add(R("qm_airjumps_5000", "Cloud Skipper", "Air-jump 5,000 times in total.", Metric.TotalAirJumps, 5000, RewardKind.Skin, "gull_rider.cloudrunner"));
        list.Add(P("qm_dash_1000", "Darting", "Dash 1,000 times in total.", Metric.TotalDashes, 1000, 50));
        list.Add(R("qm_dash_3000", "Wavestep", "Dash 3,000 times in total.", Metric.TotalDashes, 3000, RewardKind.Skin, "riptide_dancer.foamstep"));
        list.Add(P("qm_dash_run", "Quicksilver", "Dash 150 times in a single run.", Metric.RunDashes, 150, 60));
        list.Add(P("qm_slams_1000", "Pile Driver", "Slam 1,000 times in total.", Metric.TotalSlams, 1000, 80));

        // ---- the Final Swarm
        list.Add(R("qm_swarm_60", "After the End", "Survive 1 minute in the Final Swarm.", Metric.SwarmSeconds, 60, RewardKind.Item, "deep_lantern"));
        list.Add(P("qm_swarm_180", "Rising Water", "Survive 3 minutes in the Final Swarm.", Metric.SwarmSeconds, 180, 120));
        list.Add(P("qm_swarm_300", "Floodwall", "Survive 5 minutes in the Final Swarm.", Metric.SwarmSeconds, 300, 250));

        // ---- assorted ladders
        list.Add(R("qm_elites_250", "Trophy Hunter", "Defeat 250 elites in total.", Metric.TotalElites, 250, RewardKind.Skin, "harpooner.bonepicker"));
        list.Add(P("qm_elites_1000", "Crown Collector", "Defeat 1,000 elites in total.", Metric.TotalElites, 1000, 150));
        list.Add(P("qm_bosses_50", "Giant-Wrecker", "Defeat 50 bosses in total.", Metric.TotalBosses, 50, 200));
        list.Add(P("qm_level_40", "Brimming", "Reach level 40 in a single run.", Metric.RunLevel, 40, 100));
        list.Add(P("qm_stacks_25", "Overloaded", "Hold 25 items at once.", Metric.RunItemStacks, 25, 80));
        list.Add(P("qm_tomes_3", "Library at Sea", "Have 3 tomes at max level in a single run.", Metric.RunTomesMaxed, 3, 80));

        // ---- secrets
        list.Add(P("qm_feat_nochest", "Empty Hold", "Win a run without opening a chest.", Metric.Feat, 1, 150, Quests.FeatNoChestWin,
                   "Every lock on every shore, left exactly as you found it."));
        list.Add(P("qm_feat_oneweapon", "One Good Oar", "Win a run holding a single weapon.", Metric.Feat, 1, 200, Quests.FeatOneWeaponWin,
                   "Some sailors carry one tool and never need another."));
        list.Add(P("qm_feat_greedy", "Gilded Noose", "Win a run with 5 or more greed stacks.", Metric.Feat, 1, 200, Quests.FeatGreedyWin,
                   "Take what the statue offers. Take it again. Win anyway."));
        list.Add(P("qm_feat_crowded", "Heavy Weather", "Win a run with 3 or more challenges active.", Metric.Feat, 1, 250, Quests.FeatCrowdedWin,
                   "Three storms at once, and still the lighthouse stands."));
        list.Add(P("qm_feat_lowlevel", "Light Ballast", "Win a run at level 25 or lower.", Metric.Feat, 1, 200, Quests.FeatLowLevelWin,
                   "The last gate fell to someone who had barely begun to learn."));
        list.Add(P("qm_feat_bossmega", "Killing Wave", "Finish a boss with a megacrit.", Metric.Feat, 1, 100, Quests.FeatBossMegacrit,
                   "The last blow on the greatest foe was also the brightest."));
        list.Add(P("qm_feat_bossfreeze", "Iceberg", "Freeze a boss solid.", Metric.Feat, 1, 100, Quests.FeatBossFreeze,
                   "Even giants stop to shiver."));
        list.Add(P("qm_feat_bossslam", "Keelhauled", "Finish a boss with a slam.", Metric.Feat, 1, 100, Quests.FeatBossSlam,
                   "Bring the sky down on its head."));
        list.Add(P("qm_feat_quiet", "Dead Calm", "Reach the one-minute mark without defeating anything.", Metric.Feat, 1, 80, Quests.FeatQuietMinute,
                   "A whole minute of peace on a hostile shore."));
        list.Add(P("qm_swarm_600", "Beyond the Tide Line", "Survive 10 minutes in the Final Swarm.", Metric.SwarmSeconds, 600, 500,
                   hint: "After the end there is more. Outlast it."));

        return list;
    }
}
