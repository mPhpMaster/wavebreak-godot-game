using Godot;

namespace Wavebreak.Data;

public static partial class Stages
{
    public static readonly StageDef Bellspire = new()
    {
        Id = "bellspire", DisplayName = "Bellspire Ward",
        Tagline = "Anchor to buildings. Swing above the streets. Strike the wardens' glowing cores.",
        Layout = "city", Theme = "harbour", LayoutSeed = 88427,
        Radius = 350f, Sides = 12, ClearRadius = 16f, PropDensity = 0.12f,
        Wall = new Color(0.35f, 0.43f, 0.52f), Terrain = new Color(0.39f, 0.43f, 0.47f),
        Mounds = 0, Ridges = 0, Platforms = 0, Landmarks = 0,
        Chests = 16, Breakables = 36, ChargeShrines = 4, ChallengeShrines = 1,
        HazardPools = 0, BossId = "city_archon", MusicId = "harbour",
        EnemyIds = new[] { "city_bellwarden", "city_gatewarden", "city_archon" },
    };
}
