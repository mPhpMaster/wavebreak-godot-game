using System.Collections.Generic;

namespace Wavebreak.Visuals;

/// <summary>
/// Looks for the cute crew (Meta/Characters.Cute.cs): Kenney Cube Pets (CC0,
/// already in assets/kenney/cube-pets) worn as heroes. They stand a head
/// shorter than the humans, which is most of the charm, and their whole body
/// is the head, so <see cref="ModelSpec.HeadScale"/> tells the hat code
/// (<see cref="CharacterRig.SetHat"/>) to size hats to the cube.
/// </summary>
public static partial class PlayerLooks
{
    private const float PetHeight = 1.3f;

    /// <summary>Model file, and the file's own top of the cube and total height
    /// in model units (from the glTF bounds: the cube is 1.25 wide and tops out
    /// at 1.43; ears, tufts and tails stick out above that). The hat sits on
    /// the cube top and is sized to the cube's half-width (0.625), a touch
    /// under so brims do not swallow the face.</summary>
    private static readonly Dictionary<string, (string File, float Top)> CuteLooks = new()
    {
        ["pet-otter"] = ("kenney/cube-pets/animal-beaver.glb", 1.501f),     // Pip (the beaver model, read as an otter)
        ["pet-chick"] = ("kenney/cube-pets/animal-chick.glb", 1.594f),      // Lumi
        ["pet-polar"] = ("kenney/cube-pets/animal-polar.glb", 1.501f),      // Tilly
        ["pet-penguin"] = ("kenney/cube-pets/animal-penguin.glb", 1.594f),  // Pebble
        ["pet-pig"] = ("kenney/cube-pets/animal-pig.glb", 1.584f),          // Rosie
        ["pet-cat"] = ("kenney/cube-pets/animal-cat.glb", 1.710f),          // Captain Whiskers
    };

    private const float CubeTop = 1.43f;
    private const float CubeHalfWidth = 0.625f;
    private const float HatFit = 0.92f;

    private static ModelSpec? ResolveCute(string id)
    {
        if (!CuteLooks.TryGetValue(id, out var look) && !HeroLooks.TryGetValue(id, out look)) return null;   // + PlayerLooks.Heroes.cs
        ModelSpec? s = ArtAssets.Model(look.File, PetHeight, ArtAssets.ClipsFor(look.File), "hero:" + id);
        if (s is null) return null;
        s.LodDistance = 0f;
        s.HatTop = CubeTop / look.Top;
        // CharacterRig.SetHat sizes a hat to Height * 0.14 * HeadScale.
        s.HeadScale = CubeHalfWidth / look.Top / 0.14f * HatFit;
        return s;
    }
}
