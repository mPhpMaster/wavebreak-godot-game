using System.Collections.Generic;

namespace Wavebreak.Visuals;

/// <summary>
/// Looks for the heroes trio (Meta/Characters.Heroes.cs): three more Kenney
/// cube pets, resolved and hat-fitted exactly like the cute crew's
/// (<see cref="ResolveCute"/>). Tops are the files' own heights from the glTF
/// bounds: the parrot's crest, the dog's ears and the fox's ears stick out
/// above the 1.43 cube, and the hat sits on the cube.
/// </summary>
public static partial class PlayerLooks
{
    private static readonly Dictionary<string, (string File, float Top)> HeroLooks = new()
    {
        ["pet-parrot"] = ("kenney/cube-pets/animal-parrot.glb", 1.681f),   // Sunny
        ["pet-dog"] = ("kenney/cube-pets/animal-dog.glb", 1.584f),         // Biscuit
        ["pet-fox"] = ("kenney/cube-pets/animal-fox.glb", 1.686f),         // Flick
    };
}
