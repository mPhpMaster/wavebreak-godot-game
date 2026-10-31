using Godot;
using Wavebreak.Data;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>Evolution cards. The recipe always shows, because finding the
/// pairing should not need a wiki; the evolved name and what it does stay hidden
/// until the player has made it once (saved in the profile).</summary>
public sealed partial class Codex
{
    /// <summary>Open the Evolutions tab (the menu capture: codex:evolutions).</summary>
    public void OpenEvolutions()
    {
        SelectTab(System.Array.FindIndex(Tabs, t => t.Tab == Tab.Evolutions), focusGrid: false);
        FocusFirst();
    }

    private static Button EvolutionCard(EvolutionDef e)
    {
        bool found = Profile.HasEvolved(e.Id);
        string recipe = Loc.F("{0} (Lv {1}) + {2}", Loc.T(Evolutions.WeaponName(e)),
                              Catalog.WeaponById(e.WeaponId)?.MaxLevel ?? 8, Loc.T(Evolutions.CatalystName(e)));
        return Card(found ? e.Name : "???",
                    found ? e.Description : "Bring this pair together and take the gold card to see what it becomes.",
                    recipe,
                    found ? "EVOLVED" : "UNDISCOVERED",
                    found ? e.Tint : Gold, found);
    }
}
