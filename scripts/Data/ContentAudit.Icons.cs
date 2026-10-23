using System;
using System.Collections.Generic;
using System.Linq;
using Wavebreak.Stats;
using Wavebreak.UI;

namespace Wavebreak.Data;

/// <summary>
/// Every weapon, item, tome and supply resolves to icon textures that exist.
///
/// The icon map falls back to a question mark rather than drawing nothing, so a
/// missing PNG or a typo in a glyph name would otherwise ship as a "?" on a
/// card that nobody happened to roll during testing.
/// </summary>
public static partial class ContentAudit
{
    private static void AuditIcons()
    {
        int specs = 0;
        var glyphs = new HashSet<string>();

        void Spec(IconSpec s, string what)
        {
            specs++;
            Check(s.Glyph != Icons.Fallback, $"icon falls back to '?' for {what}");
            foreach (string g in Icons.GlyphsOf(s))
            {
                glyphs.Add(g);
                Check(Icons.Exists(g) && Icons.Texture(g) is not null, $"icon texture '{g}' missing for {what}");
            }
        }

        foreach (WeaponDef w in Catalog.Weapons.Concat(Meta.MetaContent.Weapons))
            Spec(Icons.For(w), $"weapon {w.Id}");
        foreach (ItemDef i in Catalog.Items.Concat(Meta.MetaContent.Items))
            Spec(Icons.For(i), $"item {i.Id}");
        foreach (TomeDef t in Tomes.All)
            Spec(Icons.For(t), $"tome {t.Id}");
        foreach (Stat s in Enum.GetValues<Stat>())
            Spec(new IconSpec(Icons.ForStat(s).Glyph, Icons.ForStat(s).Color), $"stat {s}");

        Spec(Icons.ForSupply(0.35f, 0), "supply heal");
        Spec(Icons.ForSupply(0f, 60), "supply gold");
        Spec(Icons.ForSupply(0.18f, 35), "supply cache");
        Check(Icons.Exists("up") && Icons.Exists(Icons.Fallback), "icon upgrade/fallback glyphs");

        var ids = new HashSet<string>(Catalog.Weapons.Select(w => w.Id).Concat(Meta.MetaContent.Weapons.Select(w => w.Id))
            .Concat(Catalog.Items.Select(i => i.Id)).Concat(Meta.MetaContent.Items.Select(i => i.Id)));
        foreach (string id in Icons.OverrideIds)
            Check(ids.Contains(id), $"icon override for unknown id {id}");

        foreach (string clash in Icons.Collisions())
            Check(false, $"icons look alike: {clash}");

        Line($"icons specs={specs} glyphs={glyphs.Count}");
    }
}
