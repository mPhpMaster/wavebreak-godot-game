using Godot;

namespace Wavebreak.Data;

public static partial class Stages
{
    // Side maps are opt-in: they do not change seeded draws or Classic co-op.
    public static readonly StageDef Lumenreef = new()
    {
        Id = "lumenreef", DisplayName = "Lumenreef Basin",
        Tagline = "Swim through the reef. Hold jump to rise; hold slide to dive.",
        LayoutSeed = 74219, Sides = 16, Radius = 145f, ClearRadius = 16f,
        Theme = "underwater", Underwater = true, PropDensity = 0.65f,
        Wall = new Color(0.12f, 0.65f, 0.73f), Terrain = new Color(0.22f, 0.43f, 0.46f),
        Mounds = 10, MoundHeight = 2.2f, Ridges = 9, RidgeHeight = 2.6f,
        Platforms = 5, PlatformHeight = 3.2f, Landmarks = 8,
        Chests = 7, Breakables = 32, ChargeShrines = 3, ChallengeShrines = 2,
        HazardPools = 14, HazardRadius = 4.5f, HazardSlow = 0.8f, HazardDamage = 0.025f,
        HazardName = "luminous brine", HazardColor = new Color(0.35f, 0.95f, 0.64f),
        BossId = "undertow_oracle", MusicId = "tidal",
        EnemyIds = new[]
        {
            "mote", "brine_leech", "bloat_polyp", "sidle_crab", "lantern_jelly",
            "kelp_shambler", "sand_scuttler", "shellback_guard", "conch_drummer",
            "lanternfin", "silt_eel", "deepcaller", "barnacle_hulk", "reefhorn",
            "carapace_bastion", "mother_crab", "coral_warlord", "maelstrom_eye",
        },
    };
}
