using System.Collections.Generic;

namespace Wavebreak.Meta;

/// <summary>
/// Quests for the heroes trio (Meta/Characters.Heroes.cs): one gate per
/// character and five skin quests each (two careers, a win, a Hard win, and
/// kills with the signature weapon). Every metric is one the tracker already
/// measures, so no tracker code is involved. Joined onto <see cref="Unlocks.All"/>.
/// </summary>
public static class HeroQuests
{
    private static UnlockDef Q(string id, string name, string desc, Metric metric, float threshold, RewardKind kind, string reward, string key = "")
        => new() { Id = id, Name = name, Description = desc, Metric = metric, Key = key, Threshold = threshold, Reward = kind, RewardId = reward };

    public static readonly IReadOnlyList<UnlockDef> All = new List<UnlockDef>
    {
        // ---- character gates
        Q("qh_sunny", "Crow's Nest", "Land 3,000 critical hits in total.", Metric.TotalCrits, 3000, RewardKind.Character, "sunny"),
        Q("qh_biscuit", "Shipshape", "Survive 12 minutes in a single run.", Metric.RunSeconds, 720, RewardKind.Character, "biscuit"),
        Q("qh_flick", "Kite Season", "Air-jump 500 times in total.", Metric.TotalAirJumps, 500, RewardKind.Character, "flick"),

        // ---- Sunny
        Q("qh_sunny_kills", "Eyes Front", "Defeat 1,000 enemies as Sunny the Lookout.", Metric.CharacterKills, 1000, RewardKind.Skin, "sunny.macaw", "sunny"),
        Q("qh_sunny_kills2", "Long Watch", "Defeat 4,000 enemies as Sunny the Lookout.", Metric.CharacterKills, 4000, RewardKind.Skin, "sunny.petrel", "sunny"),
        Q("qh_sunny_win", "Land Ho!", "Win a run as Sunny the Lookout.", Metric.CharacterWins, 1, RewardKind.Skin, "sunny.lagoon", "sunny"),
        Q("qh_sunny_hard", "Weather Eye", "Win a run as Sunny the Lookout on Hard or Nightmare.", Metric.CharacterHardWins, 1, RewardKind.Skin, "sunny.cockatoo", "sunny"),
        Q("qh_sunny_glint", "Sharpshooter", "Defeat 1,500 enemies with the Spyglass Glint.", Metric.WeaponKills, 1500, RewardKind.Skin, "sunny.parakeet", "spyglass_glint"),

        // ---- Biscuit
        Q("qh_biscuit_kills", "Good Boy", "Defeat 1,000 enemies as Biscuit the Sea Dog.", Metric.CharacterKills, 1000, RewardKind.Skin, "biscuit.sailor", "biscuit"),
        Q("qh_biscuit_kills2", "Harbour Master", "Defeat 4,000 enemies as Biscuit the Sea Dog.", Metric.CharacterKills, 4000, RewardKind.Skin, "biscuit.midnight", "biscuit"),
        Q("qh_biscuit_win", "All Hands Ashore", "Win a run as Biscuit the Sea Dog.", Metric.CharacterWins, 1, RewardKind.Skin, "biscuit.seafoam", "biscuit"),
        Q("qh_biscuit_hard", "Storm Moorings", "Win a run as Biscuit the Sea Dog on Hard or Nightmare.", Metric.CharacterHardWins, 1, RewardKind.Skin, "biscuit.rust", "biscuit"),
        Q("qh_biscuit_anchor", "Dropped Anchor", "Defeat 1,500 enemies with the Mooring Anchor.", Metric.WeaponKills, 1500, RewardKind.Skin, "biscuit.sandy", "mooring_anchor"),

        // ---- Flick
        Q("qh_flick_kills", "Tail Wind", "Defeat 1,000 enemies as Flick the Kite Fox.", Metric.CharacterKills, 1000, RewardKind.Skin, "flick.arctic", "flick"),
        Q("qh_flick_kills2", "Dune Runner", "Defeat 4,000 enemies as Flick the Kite Fox.", Metric.CharacterKills, 4000, RewardKind.Skin, "flick.nightkite", "flick"),
        Q("qh_flick_win", "High Flyer", "Win a run as Flick the Kite Fox.", Metric.CharacterWins, 1, RewardKind.Skin, "flick.ember", "flick"),
        Q("qh_flick_hard", "Into the Gale", "Win a run as Flick the Kite Fox on Hard or Nightmare.", Metric.CharacterHardWins, 1, RewardKind.Skin, "flick.lavender", "flick"),
        Q("qh_flick_kite", "String Theory", "Defeat 1,500 enemies with the Stormfang Blades.", Metric.WeaponKills, 1500, RewardKind.Skin, "flick.seaglass", "skimmer_kite"),
    };
}
