using Godot;

namespace Wavebreak.Visuals;

/// <summary>
/// Stormwatch Harbour's biome: wet cobbles under a stormy peach dusk, sea fret
/// thick enough to soften the far streets, coral fronds and kit rocks. The
/// buildings, cliffs and the lighthouse are not scatter; they are the map's
/// landforms (<see cref="Game.Arena"/>, Arena.Landforms.cs).
/// </summary>
public sealed partial class PropTheme
{
    private static PropTheme Harbour() => new()
    {
        Id = "harbour",
        FloorA = new Color(0.46f, 0.47f, 0.50f), FloorB = new Color(0.55f, 0.53f, 0.51f),
        FloorPatch = new Color(0.33f, 0.48f, 0.47f), FloorLine = new Color(0.33f, 0.33f, 0.37f),
        PatchAmount = 0.55f, LineStrength = 0.5f, SlabSize = 2.4f,
        SkyTop = new Color(0.15f, 0.21f, 0.35f), SkyHorizon = new Color(0.88f, 0.67f, 0.56f),
        GroundHorizon = new Color(0.27f, 0.33f, 0.40f),
        Ambient = new Color(0.62f, 0.68f, 0.86f), AmbientEnergy = 0.74f,
        Fog = new Color(0.50f, 0.58f, 0.66f), FogDensity = 0.0105f,
        Sun = new Color(1f, 0.83f, 0.64f), SunEnergy = 1.08f, SunPitchDegrees = -30f, SunYawDegrees = 205f,
        Glow = 0.75f,
        Rocks = 0.25f, Trees = 0.12f, Crystals = 0.07f, Ruins = 0.05f, Mushrooms = 0.08f, Grass = 7f, Flowers = 2f,
        TreeKind = TreeStyle.Kelp,
        RockA = new Color(0.45f, 0.47f, 0.52f), RockB = new Color(0.36f, 0.52f, 0.50f),
        Trunk = new Color(0.32f, 0.48f, 0.40f), FoliageA = new Color(0.95f, 0.55f, 0.55f), FoliageB = new Color(1f, 0.72f, 0.55f),
        Crystal = new Color(1f, 0.55f, 0.66f),
        RuinA = new Color(0.86f, 0.82f, 0.74f), RuinB = new Color(0.45f, 0.62f, 0.60f),
        GrassA = new Color(0.22f, 0.40f, 0.34f), GrassB = new Color(0.42f, 0.62f, 0.48f),
        MushroomCap = new Color(0.98f, 0.62f, 0.45f),
        FlowerColors = new[] { new Color(1f, 0.92f, 0.85f), new Color(0.98f, 0.62f, 0.66f), new Color(0.62f, 0.85f, 1f) },
        Kit = new()
        {
            [PropFactory.Kind.Rock] = new[] { "kenney/nature-kit/rock_largeA.glb", "kenney/nature-kit/rock_tallA.glb",
                                              "kenney/nature-kit/rock_largeC.glb", "kenney/nature-kit/rock_tallC.glb" },
            [PropFactory.Kind.Pillar] = new[] { "kenney/nature-kit/statue_column.glb", "kenney/nature-kit/statue_columnDamaged.glb" },
            [PropFactory.Kind.Mushroom] = new[] { "kenney/nature-kit/plant_bushLarge.glb", "kenney/nature-kit/plant_bush.glb" },
        },
        KitScale = new()
        {
            [PropFactory.Kind.Rock] = 2.2f, [PropFactory.Kind.Pillar] = 2.2f, [PropFactory.Kind.Mushroom] = 2.0f,
        },
    };
}
