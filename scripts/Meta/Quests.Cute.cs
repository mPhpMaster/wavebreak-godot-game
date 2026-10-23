using System.Collections.Generic;

namespace Wavebreak.Meta;

/// <summary>
/// Quests for the cute crew (Meta/Characters.Cute.cs) and the cute wardrobe
/// (Meta/Hats.Cute.cs): one gate per character, two skin quests per character,
/// three hats for winning, and the three pastel skins for the original
/// roster. Every metric here is one the tracker already measures, so no
/// tracker code is involved. Joined onto <see cref="Unlocks.All"/> last.
/// </summary>
public static class CuteQuests
{
    private static UnlockDef Q(string id, string name, string desc, Metric metric, float threshold, RewardKind kind, string reward, string key = "")
        => new() { Id = id, Name = name, Description = desc, Metric = metric, Key = key, Threshold = threshold, Reward = kind, RewardId = reward };

    public static readonly IReadOnlyList<UnlockDef> All = new List<UnlockDef>
    {
        // ---- character gates
        Q("qc_pip", "Spare Parts", "Break 50 pots in total.", Metric.TotalPots, 50, RewardKind.Character, "pip"),
        Q("qc_lumi", "Past Bedtime", "Survive 10 minutes in a single run.", Metric.RunSeconds, 600, RewardKind.Character, "lumi"),
        Q("qc_tilly", "First Spell", "Freeze 100 enemies in total.", Metric.TotalFreezes, 100, RewardKind.Character, "tilly"),
        Q("qc_pebble", "Special Delivery", "Dash 500 times in total.", Metric.TotalDashes, 500, RewardKind.Character, "pebble"),
        Q("qc_rosie", "Green Fins", "Heal 1,000 health in a single run.", Metric.RunHealed, 1000, RewardKind.Character, "rosie"),
        Q("qc_whiskers", "X Marks the Spot", "Earn 5,000 gold in total.", Metric.TotalGoldEarned, 5000, RewardKind.Character, "whiskers"),

        // ---- cute crew skins
        Q("qc_pip_kills", "Busy Paws", "Defeat 1,500 enemies as Pip the Tinker.", Metric.CharacterKills, 1500, RewardKind.Skin, "pip.mint", "pip"),
        Q("qc_pip_mines", "Tinker's Toolbox", "Defeat 1,000 enemies with Barnacle Mines.", Metric.WeaponKills, 1000, RewardKind.Skin, "pip.caramel", "barnacle_mines"),
        Q("qc_lumi_kills", "Lantern Bearer", "Defeat 1,500 enemies as Lumi.", Metric.CharacterKills, 1500, RewardKind.Skin, "lumi.peach", "lumi"),
        Q("qc_lumi_win", "Home by Morning", "Win a run as Lumi.", Metric.CharacterWins, 1, RewardKind.Skin, "lumi.sky", "lumi"),
        Q("qc_tilly_kills", "Practice Makes Bubbles", "Defeat 1,500 enemies as Tilly the Apprentice.", Metric.CharacterKills, 1500, RewardKind.Skin, "tilly.rose", "tilly"),
        Q("qc_tilly_win", "Graduation Day", "Win a run as Tilly the Apprentice.", Metric.CharacterWins, 1, RewardKind.Skin, "tilly.moonbeam", "tilly"),
        Q("qc_pebble_kills", "Signed For", "Defeat 1,500 enemies as Pebble the Courier.", Metric.CharacterKills, 1500, RewardKind.Skin, "pebble.blush", "pebble"),
        Q("qc_pebble_win", "Return to Sender", "Win a run as Pebble the Courier.", Metric.CharacterWins, 1, RewardKind.Skin, "pebble.icicle", "pebble"),
        Q("qc_rosie_kills", "Weeding", "Defeat 1,500 enemies as Rosie the Gardener.", Metric.CharacterKills, 1500, RewardKind.Skin, "rosie.lagoon", "rosie"),
        Q("qc_rosie_poison", "Prickly Garden", "Apply 2,000 poison stacks in total.", Metric.TotalPoisons, 2000, RewardKind.Skin, "rosie.lavender"),
        Q("qc_whiskers_kills", "Mouser", "Defeat 1,500 enemies as Captain Whiskers.", Metric.CharacterKills, 1500, RewardKind.Skin, "whiskers.calico", "whiskers"),
        Q("qc_whiskers_crits", "Sharp Claws", "Land 20,000 critical hits in total.", Metric.TotalCrits, 20000, RewardKind.Skin, "whiskers.midnight"),

        // ---- hats: win as the crew
        Q("qc_pip_win", "It Flies!", "Win a run as Pip the Tinker.", Metric.CharacterWins, 1, RewardKind.Hat, "propeller_cap", "pip"),
        Q("qc_rosie_win", "In Full Bloom", "Win a run as Rosie the Gardener.", Metric.CharacterWins, 1, RewardKind.Hat, "flower_crown", "rosie"),
        Q("qc_whiskers_win", "Cat's Whiskers", "Win a run as Captain Whiskers.", Metric.CharacterWins, 1, RewardKind.Hat, "cat_ears", "whiskers"),

        // ---- pastel skins for the original roster
        Q("qc_candyfloss", "Sweet Tooth", "Finish 25 runs.", Metric.TotalRuns, 25, RewardKind.Skin, "drifter.candyfloss"),
        Q("qc_festival", "Kite Festival", "Jump 3,000 times in total.", Metric.TotalJumps, 3000, RewardKind.Skin, "gale_courier.festival"),
        Q("qc_marshmallow", "Toasty", "Set 500 enemies alight in total.", Metric.TotalBurns, 500, RewardKind.Skin, "kiln_sister.marshmallow"),
    };
}
