using System.Text;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Data;
using Wavebreak.Meta;
using Wavebreak.Progression;
using Wavebreak.Stats;

namespace Wavebreak.UI;

/// <summary>
/// The build box beside the stats: weapons, tomes and items as icon tiles with
/// a level or stack badge, and the name and effect in a hover tooltip, so a
/// 180-item build fits in a glance instead of a column of clipped names.
/// </summary>
public sealed partial class StatsPanel
{
    private const float TileSize = 44f;
    private VBoxContainer _build = null!;
    private ScrollContainer _buildScroll = null!;

    private Control BuildBox()
    {
        // Left to right in every language: in RTL the scroll area and the flow
        // rows mirrored their contents out of the box. Headings are aligned
        // back to the reading side in RebuildBuild.
        var box = new PanelContainer { MouseFilter = MouseFilterEnum.Pass, LayoutDirection = LayoutDirectionEnum.Ltr };
        box.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.12f, 0.18f, 0.96f),
            BorderColor = new Color(0.30f, 0.55f, 0.70f, 0.5f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10,
        });
        _buildScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            MouseFilter = MouseFilterEnum.Pass,
            CustomMinimumSize = new Vector2(372f, 0f),
        };
        box.AddChild(_buildScroll);
        _build = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _build.AddThemeConstantOverride("separation", 4);
        _buildScroll.AddChild(_build);
        return box;
    }

    private void RebuildBuild(StatSheet stats)
    {
        Clear(_build);

        Section(_build, Loc.F("WEAPONS  {0}/{1}", _weapons.Weapons.Count, _weapons.Slots));
        HFlowContainer weapons = Flow();
        foreach (WeaponRuntime w in _weapons.Weapons)
        {
            float dps = w.Interval > 0f ? w.VolleyDamage(stats) / w.Interval : 0f;
            string tip = Tip(w.Def.DisplayName, w.Def.Rarity, Loc.F("Lv {0}/{1}  ~{2:0} dps", w.Level, w.Def.MaxLevel, dps), w.Def.Description);
            weapons.AddChild(Tile(Icons.For(w.Def), Catalog.RarityColor(w.Def.Rarity), Loc.F("Lv{0}", w.Level), tip));
        }
        Empty(weapons);

        TomeBook? book = TomeBook.Current;
        Section(_build, Loc.F("TOMES  {0}/{1}", book?.Count ?? 0, book?.Slots ?? Tomes.MaxSlots));
        HFlowContainer tomes = Flow();
        if (book is not null)
            foreach (TomeDef t in book.Held)
            {
                int level = book.LevelOf(t);
                string tip = Tip(t.DisplayName, null, Loc.F("Lv {0}/{1}  {2}", level, t.MaxLevel, t.DescribeTotal(level)), t.Description);
                tomes.AddChild(Tile(Icons.For(t), t.Tint, Loc.F("Lv{0}", level), tip));
            }
        Empty(tomes);

        Section(_build, Loc.F("ITEMS  {0}", _inventory.TotalStacks));
        HFlowContainer items = Flow();
        foreach (ItemStack item in _inventory.Items)
        {
            var effects = new StringBuilder();
            foreach (ItemEffect e in item.Def.Effects) effects.Append('\n').Append(Loc.T(Catalog.DescribeEffect(e)));
            string tip = Tip(item.DisplayName, item.Rarity, item.Count > 1 ? Loc.F("x{0}", item.Count) : "",
                             item.Def.Description) + effects;
            items.AddChild(Tile(Icons.For(item.Def), Catalog.RarityColor(item.Rarity), item.Count > 1 ? Loc.F("x{0}", item.Count) : "", tip));
        }
        Empty(items);

        if (LevelUpChoices.Current is { } c)
        {
            Section(_build, "Reroll · Banish · Skip");
            _build.AddChild(Text($"{c.RerollsLeft} · {c.BanishesLeft} · {c.SkipsLeft}", 14, Dim));
        }

        if (Loc.IsRtl)
            foreach (Node n in _build.GetChildren())
                if (n is Label l) l.HorizontalAlignment = HorizontalAlignment.Right;
    }

    private HFlowContainer Flow()
    {
        // Icons flow left to right in every language: mirrored, the flow
        // container misplaced its tiles by one column.
        var flow = new HFlowContainer { MouseFilter = MouseFilterEnum.Pass, SizeFlagsHorizontal = SizeFlags.ExpandFill,
                                        LayoutDirection = LayoutDirectionEnum.Ltr };
        flow.AddThemeConstantOverride("h_separation", 6);
        flow.AddThemeConstantOverride("v_separation", 6);
        _build.AddChild(flow);
        return flow;
    }

    private void Empty(Container flow)
    {
        if (flow.GetChildCount() == 0) flow.AddChild(Text(Loc.T("none yet"), 14, Dim));
    }

    /// <summary>One icon with a small badge (level or stack count) in its
    /// corner; the tooltip carries everything else.</summary>
    private static Control Tile(IconSpec spec, Color ring, string badge, string tip)
    {
        // The icon is the tile: one control, so nothing inside can drift out
        // of place when the layout mirrors for Arabic.
        IconView tile = IconView.Make(TileSize);
        tile.Set(spec, ring);
        tile.MouseFilter = MouseFilterEnum.Stop;
        tile.TooltipText = tip;
        tile.LayoutDirection = LayoutDirectionEnum.Ltr;
        if (badge.Length > 0)
        {
            var b = new Label
            {
                Text = badge,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                MouseFilter = MouseFilterEnum.Ignore,
                AutoTranslateMode = AutoTranslateModeEnum.Disabled,
            };
            b.AddThemeFontSizeOverride("font_size", 11);
            b.AddThemeColorOverride("font_color", Colors.White);
            b.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
            b.AddThemeConstantOverride("outline_size", 4);
            b.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            b.OffsetRight = 2f;
            b.OffsetBottom = 2f;
            tile.AddChild(b);
        }
        return tile;
    }

    private static string Tip(string name, Rarity? rarity, string detail, string description)
    {
        var sb = new StringBuilder(Loc.T(name));
        if (rarity is { } r) sb.Append("  ·  ").Append(Loc.T(r.ToString()));
        if (detail.Length > 0) sb.Append('\n').Append(detail);
        string d = Loc.T(description.Trim());
        if (d.Length > 0) sb.Append("\n\n").Append(d);
        return sb.ToString();
    }
}
