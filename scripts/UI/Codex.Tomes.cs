using Godot;
using Wavebreak.Data;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>Reference cards for the tomes: always available, so they are
/// shown plainly, with the per-level step, the cap and the total at max.</summary>
public sealed partial class Codex
{
    private static Button TomeCard(TomeDef t)
        => t is PerkDef p
            ? Card(p.DisplayName, p.Description,
                   Loc.F("Lv 1: {0}\nmax Lv {1}: {2}", p.Summary(1), p.MaxLevel, p.Summary(p.MaxLevel)),
                   Loc.F("PERK  ·  {0}", Catalog.RarityName(p.Rarity)), p.Tint, true)
            : Card(t.DisplayName, t.Description,
                Loc.F("{0} per level  ·  max Lv {1}: {2}", t.DescribeStep(), t.MaxLevel, t.DescribeTotal(t.MaxLevel)),
                Loc.F("TOME  ·  {0}", Catalog.RarityName(t.Rarity)), t.Tint, true);
}
