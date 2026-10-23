using System.Collections.Generic;

namespace Wavebreak.Visuals;

/// <summary>The expansion crew's looks: the Kenney blocky characters, one
/// per new character (see Meta/Characters.Expansion.cs).</summary>
public static partial class PlayerLooks
{
    private static readonly Dictionary<string, string> ExpansionLooks = new()
    {
        ["blocky-a"] = "kenney/blocky-characters/character-a.glb",
        ["blocky-b"] = "kenney/blocky-characters/character-b.glb",
        ["blocky-c"] = "kenney/blocky-characters/character-c.glb",
        ["blocky-d"] = "kenney/blocky-characters/character-d.glb",
        ["blocky-e"] = "kenney/blocky-characters/character-e.glb",
        ["blocky-f"] = "kenney/blocky-characters/character-f.glb",
        ["blocky-g"] = "kenney/blocky-characters/character-g.glb",
        ["blocky-h"] = "kenney/blocky-characters/character-h.glb",
    };
}
