using System.Collections.Generic;
using Godot;

namespace Wavebreak.Visuals;

public enum TreeStyle { Round, Pine, Dead, Kelp }

/// <summary>
/// A biome, as data: floor, sky, light and what grows on the ground. A map or
/// phase picks one (<see cref="Get"/>) and hands it to <see cref="PropFactory"/>.
///
/// The presets are tuned as sets rather than colour by colour: each one keeps the
/// floor darker and less saturated than anything that moves on it, because the
/// first job of a survivors-like arena is that the crowd reads against it.
/// </summary>
public sealed class PropTheme
{
    public string Id = "meadow";

    // Floor: two large-scale colours blended by slow noise, a patch colour, a
    // fine speckle, and an optional slab/crack line pattern.
    public Color FloorA, FloorB, FloorPatch, FloorLine;
    public float PatchAmount = 0.6f;
    public float LineStrength;         // 0 = no slabs
    public float SlabSize = 4f;

    // Sky and light.
    public Color SkyTop, SkyHorizon, GroundHorizon;
    public Color Ambient;
    public float AmbientEnergy = 0.7f;
    public Color Fog;
    public float FogDensity = 0.006f;
    public Color Sun;
    public float SunEnergy = 1.2f;
    public float SunPitchDegrees = -50f;
    public float SunYawDegrees = 35f;
    public float Glow = 0.6f;

    // Props, per 100 m² of floor at density 1.
    public float Rocks = 0.35f, Trees = 0.25f, Crystals, Ruins = 0.1f, Mushrooms;
    public float Grass = 22f, Flowers = 4f;
    public TreeStyle TreeKind = TreeStyle.Round;
    public bool SnowCaps;

    public Color RockA, RockB, Trunk, FoliageA, FoliageB, Crystal, RuinA, RuinB, GrassA, GrassB, MushroomCap;
    public Color[] FlowerColors = { new(1f, 0.85f, 0.3f) };

    /// <summary>
    /// Imported models to use instead of the procedural prop of a kind, as paths
    /// under <see cref="ArtAssets.Root"/>. Variants cycle through the list. A kind
    /// with no entry (or whose files are missing) stays procedural, so a theme
    /// can mix the two — kit trees over procedural snow-capped rocks, say.
    /// </summary>
    public Dictionary<PropFactory.Kind, string[]> Kit = new();

    /// <summary>Uniform scale for a kind's kit models (kits are authored at
    /// tile scale, about a metre; the arena wants trees nearer four).</summary>
    public Dictionary<PropFactory.Kind, float> KitScale = new();

    /// <summary>Whether this theme's props of <paramref name="k"/> come from a kit.</summary>
    public bool UsesKit(PropFactory.Kind k) => ArtAssets.UseImported && Kit.TryGetValue(k, out string[]? f) && f.Length > 0;

    private static readonly Dictionary<string, PropTheme> Presets = new();

    /// <summary>Every preset id.</summary>
    public static readonly string[] Ids = { "meadow", "graveyard", "frost", "ashlands", "tidal" };

    /// <summary>A preset by id; unknown ids get the meadow.</summary>
    public static PropTheme Get(string? id)
    {
        id ??= "meadow";
        if (Presets.TryGetValue(id, out PropTheme? t)) return t;
        t = id switch
        {
            "ashlands" => Ashlands(),
            "frost" => Frost(),
            "tidal" => Tidal(),
            "graveyard" => Graveyard(),
            _ => Meadow(),
        };
        Presets[id] = t;
        return t;
    }

    private static PropTheme Meadow() => new()
    {
        Id = "meadow",
        FloorA = new Color(0.30f, 0.42f, 0.24f), FloorB = new Color(0.36f, 0.46f, 0.25f),
        FloorPatch = new Color(0.45f, 0.40f, 0.28f), FloorLine = new Color(0.22f, 0.28f, 0.18f),
        PatchAmount = 0.55f,
        SkyTop = new Color(0.28f, 0.48f, 0.82f), SkyHorizon = new Color(0.78f, 0.84f, 0.88f),
        GroundHorizon = new Color(0.35f, 0.42f, 0.32f),
        Ambient = new Color(0.62f, 0.7f, 0.85f), AmbientEnergy = 0.75f,
        Fog = new Color(0.68f, 0.78f, 0.88f), FogDensity = 0.004f,
        Sun = new Color(1f, 0.94f, 0.82f), SunEnergy = 1.25f,
        Rocks = 0.4f, Trees = 0.5f, Ruins = 0.12f, Mushrooms = 0.12f, Grass = 26f, Flowers = 5f,
        TreeKind = TreeStyle.Round,
        RockA = new Color(0.46f, 0.47f, 0.5f), RockB = new Color(0.45f, 0.58f, 0.3f),
        Trunk = new Color(0.42f, 0.28f, 0.18f), FoliageA = new Color(0.25f, 0.52f, 0.22f), FoliageB = new Color(0.52f, 0.72f, 0.28f),
        Crystal = new Color(0.5f, 0.9f, 1f),
        RuinA = new Color(0.68f, 0.64f, 0.56f), RuinB = new Color(0.5f, 0.6f, 0.36f),
        GrassA = new Color(0.24f, 0.42f, 0.18f), GrassB = new Color(0.55f, 0.74f, 0.3f),
        MushroomCap = new Color(0.85f, 0.25f, 0.2f),
        FlowerColors = new[] { new Color(1f, 0.85f, 0.3f), new Color(1f, 0.95f, 0.95f), new Color(0.95f, 0.45f, 0.6f), new Color(0.6f, 0.65f, 1f) },
        Kit = new()
        {
            [PropFactory.Kind.Tree] = new[] { "kenney/nature-kit/tree_oak.glb", "kenney/nature-kit/tree_default.glb", "kenney/nature-kit/tree_detailed.glb", "kenney/nature-kit/tree_fat.glb" },
            [PropFactory.Kind.Rock] = new[] { "kenney/nature-kit/rock_largeA.glb", "kenney/nature-kit/rock_largeB.glb", "kenney/nature-kit/rock_largeC.glb", "kenney/nature-kit/stone_largeA.glb" },
            [PropFactory.Kind.Pillar] = new[] { "kenney/nature-kit/statue_column.glb", "kenney/nature-kit/statue_columnDamaged.glb", "kenney/nature-kit/statue_obelisk.glb" },
            [PropFactory.Kind.Arch] = new[] { "kenney/nature-kit/statue_ring.glb" },
            [PropFactory.Kind.Wall] = new[] { "kenney/nature-kit/log_large.glb", "kenney/nature-kit/stump_old.glb", "kenney/nature-kit/statue_block.glb" },
            [PropFactory.Kind.Mushroom] = new[] { "kenney/nature-kit/plant_bushLarge.glb", "kenney/nature-kit/plant_bushDetailed.glb",
                                                  "kenney/nature-kit/mushroom_redGroup.glb", "kenney/nature-kit/plant_bush.glb" },
        },
        KitScale = new()
        {
            [PropFactory.Kind.Tree] = 3.4f, [PropFactory.Kind.Rock] = 2.2f, [PropFactory.Kind.Pillar] = 2.2f,
            [PropFactory.Kind.Arch] = 2.4f, [PropFactory.Kind.Wall] = 2.2f, [PropFactory.Kind.Mushroom] = 2.2f,
        },
    };

    private static PropTheme Ashlands() => new()
    {
        Id = "ashlands",
        FloorA = new Color(0.17f, 0.15f, 0.15f), FloorB = new Color(0.24f, 0.2f, 0.19f),
        FloorPatch = new Color(0.32f, 0.16f, 0.1f), FloorLine = new Color(1f, 0.38f, 0.1f),
        PatchAmount = 0.5f, LineStrength = 0.9f, SlabSize = 5.5f,
        SkyTop = new Color(0.1f, 0.06f, 0.08f), SkyHorizon = new Color(0.62f, 0.26f, 0.14f),
        GroundHorizon = new Color(0.15f, 0.08f, 0.07f),
        Ambient = new Color(0.75f, 0.45f, 0.38f), AmbientEnergy = 0.55f,
        Fog = new Color(0.35f, 0.16f, 0.1f), FogDensity = 0.012f,
        Sun = new Color(1f, 0.62f, 0.42f), SunEnergy = 1.05f, SunPitchDegrees = -38f,
        Glow = 0.9f,
        Rocks = 0.45f, Trees = 0.12f, Crystals = 0.14f, Ruins = 0.1f, Grass = 4f, Flowers = 0f,
        TreeKind = TreeStyle.Dead,
        RockA = new Color(0.16f, 0.14f, 0.14f), RockB = new Color(0.3f, 0.24f, 0.22f),
        Trunk = new Color(0.16f, 0.12f, 0.11f), FoliageA = new Color(0.2f, 0.15f, 0.12f), FoliageB = new Color(0.3f, 0.2f, 0.15f),
        Crystal = new Color(1f, 0.45f, 0.12f),
        RuinA = new Color(0.36f, 0.24f, 0.22f), RuinB = new Color(0.22f, 0.15f, 0.14f),
        GrassA = new Color(0.25f, 0.2f, 0.16f), GrassB = new Color(0.5f, 0.32f, 0.18f),
        MushroomCap = new Color(1f, 0.5f, 0.2f),
    };

    private static PropTheme Frost() => new()
    {
        Id = "frost",
        FloorA = new Color(0.74f, 0.8f, 0.88f), FloorB = new Color(0.86f, 0.9f, 0.95f),
        FloorPatch = new Color(0.6f, 0.72f, 0.84f), FloorLine = new Color(0.55f, 0.66f, 0.8f),
        PatchAmount = 0.5f, LineStrength = 0.25f, SlabSize = 7f,
        SkyTop = new Color(0.32f, 0.45f, 0.68f), SkyHorizon = new Color(0.82f, 0.88f, 0.95f),
        GroundHorizon = new Color(0.6f, 0.68f, 0.78f),
        Ambient = new Color(0.65f, 0.75f, 0.95f), AmbientEnergy = 0.7f,
        Fog = new Color(0.8f, 0.86f, 0.95f), FogDensity = 0.011f,
        Sun = new Color(1f, 0.96f, 0.92f), SunEnergy = 1.0f, SunPitchDegrees = -32f,
        Rocks = 0.3f, Trees = 0.3f, Crystals = 0.12f, Ruins = 0.06f, Grass = 5f, Flowers = 0f,
        TreeKind = TreeStyle.Pine, SnowCaps = true,
        Kit = new()
        {
            [PropFactory.Kind.Tree] = new[] { "kenney/nature-kit/tree_pineRoundA.glb", "kenney/nature-kit/tree_pineTallA.glb",
                                              "kenney/nature-kit/tree_pineDefaultA.glb", "kenney/nature-kit/tree_pineRoundC.glb" },
        },
        KitScale = new() { [PropFactory.Kind.Tree] = 3.4f },
        RockA = new Color(0.4f, 0.45f, 0.55f), RockB = new Color(0.92f, 0.95f, 1f),
        Trunk = new Color(0.35f, 0.25f, 0.2f), FoliageA = new Color(0.12f, 0.32f, 0.3f), FoliageB = new Color(0.2f, 0.45f, 0.4f),
        Crystal = new Color(0.45f, 0.9f, 1f),
        RuinA = new Color(0.62f, 0.66f, 0.75f), RuinB = new Color(0.95f, 0.97f, 1f),
        GrassA = new Color(0.5f, 0.55f, 0.5f), GrassB = new Color(0.85f, 0.9f, 0.95f),
        MushroomCap = new Color(0.5f, 0.8f, 1f),
    };

    private static PropTheme Graveyard() => new()
    {
        Id = "graveyard",
        FloorA = new Color(0.22f, 0.26f, 0.2f), FloorB = new Color(0.28f, 0.3f, 0.22f),
        FloorPatch = new Color(0.3f, 0.24f, 0.2f), FloorLine = new Color(0.18f, 0.2f, 0.17f),
        PatchAmount = 0.7f,
        SkyTop = new Color(0.12f, 0.1f, 0.22f), SkyHorizon = new Color(0.45f, 0.38f, 0.55f),
        GroundHorizon = new Color(0.16f, 0.16f, 0.2f),
        Ambient = new Color(0.55f, 0.55f, 0.8f), AmbientEnergy = 0.65f,
        Fog = new Color(0.36f, 0.34f, 0.5f), FogDensity = 0.014f,
        Sun = new Color(0.8f, 0.82f, 1f), SunEnergy = 0.9f, SunPitchDegrees = -42f,
        Glow = 0.8f,
        Rocks = 0.9f, Trees = 0.3f, Crystals = 0.15f, Ruins = 0.16f, Mushrooms = 0.16f, Grass = 10f, Flowers = 0f,
        TreeKind = TreeStyle.Dead,
        RockA = new Color(0.4f, 0.4f, 0.45f), RockB = new Color(0.45f, 0.52f, 0.4f),
        Trunk = new Color(0.25f, 0.2f, 0.18f), FoliageA = new Color(0.2f, 0.25f, 0.2f), FoliageB = new Color(0.3f, 0.35f, 0.25f),
        Crystal = new Color(0.6f, 0.9f, 0.5f),
        RuinA = new Color(0.5f, 0.5f, 0.55f), RuinB = new Color(0.4f, 0.5f, 0.38f),
        GrassA = new Color(0.25f, 0.3f, 0.2f), GrassB = new Color(0.45f, 0.45f, 0.3f),
        MushroomCap = new Color(1f, 0.55f, 0.15f),
        Kit = new()
        {
            [PropFactory.Kind.Tree] = new[] { "kenney/graveyard-kit/pine.glb", "kenney/graveyard-kit/pine-crooked.glb", "kenney/graveyard-kit/pine-fall-crooked.glb" },
            [PropFactory.Kind.Rock] = new[] { "kenney/graveyard-kit/gravestone-cross.glb", "kenney/graveyard-kit/gravestone-round.glb",
                                              "kenney/graveyard-kit/gravestone-bevel.glb", "kenney/graveyard-kit/gravestone-broken.glb" },
            [PropFactory.Kind.Pillar] = new[] { "kenney/graveyard-kit/pillar-obelisk.glb", "kenney/graveyard-kit/lightpost-single.glb", "kenney/graveyard-kit/cross-column.glb" },
            [PropFactory.Kind.Arch] = new[] { "kenney/graveyard-kit/crypt-small.glb", "kenney/graveyard-kit/crypt.glb" },
            [PropFactory.Kind.Wall] = new[] { "kenney/graveyard-kit/iron-fence.glb", "kenney/graveyard-kit/iron-fence-damaged.glb", "kenney/graveyard-kit/stone-wall-damaged.glb" },
            [PropFactory.Kind.Mushroom] = new[] { "kenney/graveyard-kit/pumpkin-carved.glb", "kenney/graveyard-kit/pumpkin-tall-carved.glb",
                                                  "kenney/graveyard-kit/lantern-candle.glb", "kenney/graveyard-kit/fire-basket.glb" },
            [PropFactory.Kind.Crystal] = new[] { "kenney/graveyard-kit/rocks.glb", "kenney/graveyard-kit/rocks-tall.glb", "kenney/graveyard-kit/coffin-old.glb", "kenney/graveyard-kit/urn-round.glb" },
        },
        KitScale = new()
        {
            [PropFactory.Kind.Tree] = 2.6f, [PropFactory.Kind.Rock] = 1.6f, [PropFactory.Kind.Pillar] = 1.8f,
            [PropFactory.Kind.Arch] = 1.7f, [PropFactory.Kind.Wall] = 2.2f, [PropFactory.Kind.Mushroom] = 1.6f,
            [PropFactory.Kind.Crystal] = 1.6f,
        },
    };

    private static PropTheme Tidal() => new()
    {
        Id = "tidal",
        FloorA = new Color(0.72f, 0.64f, 0.46f), FloorB = new Color(0.8f, 0.72f, 0.52f),
        FloorPatch = new Color(0.36f, 0.56f, 0.55f), FloorLine = new Color(0.62f, 0.55f, 0.4f),
        PatchAmount = 0.75f,
        SkyTop = new Color(0.2f, 0.52f, 0.78f), SkyHorizon = new Color(0.72f, 0.9f, 0.92f),
        GroundHorizon = new Color(0.4f, 0.6f, 0.6f),
        Ambient = new Color(0.6f, 0.8f, 0.85f), AmbientEnergy = 0.75f,
        Fog = new Color(0.62f, 0.85f, 0.88f), FogDensity = 0.008f,
        Sun = new Color(1f, 0.95f, 0.85f), SunEnergy = 1.2f,
        Rocks = 0.35f, Trees = 0.3f, Crystals = 0.06f, Ruins = 0.08f, Mushrooms = 0.06f, Grass = 12f, Flowers = 3f,
        TreeKind = TreeStyle.Kelp,
        RockA = new Color(0.5f, 0.48f, 0.45f), RockB = new Color(0.38f, 0.6f, 0.5f),
        Trunk = new Color(0.3f, 0.5f, 0.3f), FoliageA = new Color(0.95f, 0.45f, 0.45f), FoliageB = new Color(1f, 0.65f, 0.5f),
        Crystal = new Color(0.4f, 1f, 0.85f),
        RuinA = new Color(0.82f, 0.8f, 0.72f), RuinB = new Color(0.4f, 0.62f, 0.52f),
        GrassA = new Color(0.2f, 0.45f, 0.35f), GrassB = new Color(0.4f, 0.7f, 0.5f),
        MushroomCap = new Color(0.95f, 0.55f, 0.75f),
        FlowerColors = new[] { new Color(1f, 0.6f, 0.7f), new Color(1f, 0.95f, 0.85f), new Color(0.55f, 0.85f, 1f) },
    };
}
