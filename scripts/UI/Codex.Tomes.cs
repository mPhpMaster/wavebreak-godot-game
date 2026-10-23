using Godot;
using Wavebreak.Data;

namespace Wavebreak.UI;

/// <summary>Reference cards for the tomes: always available, so they are
/// shown plainly, with the per-level step, the cap and the total at max.</summary>
public sealed partial class Codex
{
    private static Button TomeCard(TomeDef t)
        => Card(t.DisplayName, t.Description,
                $"{t.DescribeStep()} per level  ·  max Lv {t.MaxLevel}: {t.DescribeTotal(t.MaxLevel)}",
                $"TOME  ·  {Catalog.RarityName(t.Rarity)}", t.Tint, true);
}
