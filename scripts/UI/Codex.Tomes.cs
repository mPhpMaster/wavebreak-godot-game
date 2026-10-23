using Godot;
using Wavebreak.Data;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>Reference cards for the tomes: always available, so they are
/// shown plainly, with the per-level step, the cap and the total at max.</summary>
public sealed partial class Codex
{
    private static Button TomeCard(TomeDef t)
        => Card(t.DisplayName, t.Description,
                Loc.F("{0} per level  ·  max Lv {1}: {2}", t.DescribeStep(), t.MaxLevel, t.DescribeTotal(t.MaxLevel)),
                Loc.F("TOME  ·  {0}", Catalog.RarityName(t.Rarity)), t.Tint, true);
}
