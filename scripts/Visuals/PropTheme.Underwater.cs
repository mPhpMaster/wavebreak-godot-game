using Godot;

namespace Wavebreak.Visuals;

public sealed partial class PropTheme
{
    private static PropTheme Underwater() => new()
    {
        Id = "underwater",
        FloorA = new Color(0.12f, 0.27f, 0.30f), FloorB = new Color(0.20f, 0.37f, 0.39f),
        FloorPatch = new Color(0.30f, 0.44f, 0.41f), FloorLine = new Color(0.08f, 0.23f, 0.27f),
        PatchAmount = 0.65f, LineStrength = 0.15f, SlabSize = 9f,
        SkyTop = new Color(0.03f, 0.19f, 0.27f), SkyHorizon = new Color(0.08f, 0.35f, 0.43f),
        GroundHorizon = new Color(0.05f, 0.24f, 0.30f),
        Ambient = new Color(0.40f, 0.77f, 0.88f), AmbientEnergy = 0.85f,
        Fog = new Color(0.06f, 0.32f, 0.39f), FogDensity = 0.012f,
        Sun = new Color(0.48f, 0.90f, 1f), SunEnergy = 0.95f, SunPitchDegrees = -75f,
        Glow = 0.8f, Rocks = 0.3f, Trees = 0.16f, Crystals = 0f,
        Ruins = 0.04f, Mushrooms = 0f, Grass = 3f, Flowers = 0f,
        TreeKind = TreeStyle.Kelp,
        RockA = new Color(0.21f, 0.35f, 0.39f), RockB = new Color(0.30f, 0.46f, 0.44f),
        Trunk = new Color(0.15f, 0.46f, 0.40f),
        FoliageA = new Color(0.18f, 0.62f, 0.52f), FoliageB = new Color(0.36f, 0.81f, 0.62f),
        Crystal = new Color(0.38f, 0.92f, 1f),
        RuinA = new Color(0.46f, 0.62f, 0.60f), RuinB = new Color(0.26f, 0.44f, 0.46f),
        GrassA = new Color(0.10f, 0.40f, 0.34f), GrassB = new Color(0.27f, 0.62f, 0.49f),
        MushroomCap = new Color(0.85f, 0.45f, 0.78f),
        FlowerColors = new[] { new Color(0.45f, 0.95f, 1f), new Color(0.96f, 0.55f, 0.79f) },
    };
}
