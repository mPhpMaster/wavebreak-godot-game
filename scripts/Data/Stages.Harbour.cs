using Godot;

namespace Wavebreak.Data;

/// <summary>
/// Stormwatch Harbour, the sixth map: a storm-lit harbour town on a ten-sided
/// headland, the largest map in the game (in-radius about 181 m).
///
/// It is built to be explored rather than read at a glance. The start is a
/// square boxed in by the old town's houses, and a second row of buildings
/// stands behind every street out of it, so from the start no line of sight
/// leaves the town; four long cliff spurs then split the outer belt into four
/// zones you only see once you are in them: Lighthouse Point (north, a raised
/// headland with the lighthouse), the Sunken Quarter (east, half-drowned
/// ruins in flooded streets), Coral Canyon (south, between two rock walls)
/// and the Docks (west, warehouses and moored ships). The layout itself is
/// Game/Arena.Landforms.cs; every blocker there collides and is registered
/// with Game/MapBlockers for steering and placement.
/// </summary>
public static partial class Stages
{
    private static StageDef Stormwatch() => new()
    {
        Id = "stormwatch",
        DisplayName = "Stormwatch Harbour",
        Tagline = "Streets, cliffs and a lighthouse. See what is round the corner.",
        LayoutSeed = 5309,
        Sides = 10, Radius = 190f, ClearRadius = 16f,
        Theme = "harbour", PropDensity = 0.5f,
        Layout = "harbour",
        Wall = new Color(0.30f, 0.72f, 0.86f),
        Terrain = new Color(0.66f, 0.60f, 0.52f),
        Mounds = 5, MoundHeight = 1.8f,
        Platforms = 3, PlatformHeight = 3.0f,
        Ridges = 3, RidgeHeight = 2.0f,
        Landmarks = 5,
        Chests = 8,
        Merchants = 0, Microwaves = 1, Breakables = 38,
        ChargeShrines = 4, ChallengeShrines = 2, StatueShrines = 1, GreedShrines = 1,
        PhaseSeconds = 210f, BossGraceSeconds = 120f,
        EnemyHealthScale = 1.25f, EnemyDamageScale = 1.12f, EnemySpeedScale = 1.04f,
        // Sea water standing in the Sunken Quarter's streets (the landforms
        // put the pools there; any left over go anywhere).
        HazardPools = 10, HazardRadius = 5.5f, HazardSlow = 0.65f, HazardDamage = 0f,
        HazardColor = new Color(0.22f, 0.62f, 0.80f), HazardName = "flooded street",
        BossId = "undertow_oracle",
        MusicId = "harbour",
        // A harbour crowd: crabs, eels, jellies and gulls, the old shore
        // stragglers early, the big shell-backs late. No fire or frost kin,
        // and few shooters, since shots do not path round buildings.
        EnemyIds = new[]
        {
            "mote", "husk", "skitterling", "gravel_brute", "brine_leech", "bloat_polyp", "reefhorn",
            "sidle_crab", "spume_mite", "squall_gull", "lantern_jelly", "crock_mimic", "kelp_shambler",
            "surge_leaper", "sand_scuttler", "shellback_guard", "conch_drummer", "mistwalker", "brine_mortar",
            "lanternfin", "gloom_lurcher", "silt_eel", "deepcaller", "barnacle_hulk", "thunderhead",
            "tidewall_colossus",
            // elites
            "ash_warden", "gale_revenant", "tempest_herald", "carapace_bastion", "mother_crab",
            "coral_warlord", "maelstrom_eye",
        },
    };
}
