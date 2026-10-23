using System.Collections.Generic;

namespace Wavebreak.Meta;

/// <summary>
/// Six cute hats (meshes in <c>Visuals/HatModels.Cute.cs</c>). Three are bought
/// with Pearls, three are earned by winning as a member of the cute crew
/// (<see cref="CuteQuests"/>). Appended after the original wardrobe, so saved
/// choices keep their meaning.
/// </summary>
public static partial class Hats
{
    private static IReadOnlyList<HatDef> WithCute(List<HatDef> list)
    {
        list.AddRange(new HatDef[]
        {
            new() { Id = "bunny_ears", Name = "Bunny Ears", PearlCost = 180 },
            new() { Id = "flower_crown", Name = "Flower Crown" },          // win as Rosie
            new() { Id = "cat_ears", Name = "Cat Ears" },                  // win as Captain Whiskers
            new() { Id = "tiny_crown", Name = "Tiny Crown", PearlCost = 450 },
            new() { Id = "propeller_cap", Name = "Propeller Cap" },        // win as Pip
            new() { Id = "shell_bow", Name = "Seashell Bow", PearlCost = 220 },
        });
        return list;
    }
}
