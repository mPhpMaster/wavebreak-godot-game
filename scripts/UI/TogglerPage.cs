using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Wavebreak.Data;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The Toggler (Meta/Toggler): every weapon, item and tome a run can offer as
/// a tile with its icon and an ON/OFF tag, in a searchable, filterable grid.
/// Pressing a tile flips it and saves. A shelf at its minimum refuses to drop
/// further and says why, so offers never run dry.
///
/// Opened from the Pearl Shop's Toggler line once it is bought; the menu id is
/// "toggler" (<c>--menu-page=toggler</c> for captures, "toggler:items" etc.
/// to open on one shelf).
/// </summary>
public sealed partial class TogglerPage : Control
{
    private enum Shelf { All, Weapons, Items, Tomes }

    private readonly Action _back;
    private List<ToggleEntry> _entries = new();
    private readonly List<(ToggleEntry Entry, Button Tile, Label Tag, PanelContainer TagBox)> _tiles = new();
    private Shelf _shelf = Shelf.All;
    private LineEdit _search = null!;
    private GridContainer _grid = null!;
    private ScrollContainer _scroll = null!;
    private readonly List<(Shelf Shelf, Button Button)> _shelfPills = new();
    private Label _counts = null!, _status = null!, _locked = null!;
    private Button _backButton = null!;
    private bool _built;

    public TogglerPage(Action back)
    {
        _back = back;
        Name = "TogglerPage";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    private void Build()
    {
        if (_built) return;
        _built = true;

        Label heading = CuteStyle.Title(Loc.T("THE TOGGLER"), 20, CuteStyle.OnNightDim);
        MainMenu.Place(heading, 64f, 182f, 300f, 26f);
        AddChild(heading);
        _counts = CuteStyle.Title("", 15, CuteStyle.Butter, HorizontalAlignment.Right);
        MainMenu.Place(_counts, 520f, 184f, 700f, 24f);
        AddChild(_counts);

        PanelContainer board = CuteStyle.Panel(new Color(CuteStyle.Night, 0.86f), new Color(CuteStyle.Mint, 0.45f), 24, 14);
        MainMenu.Place(board, 48f, 212f, 1184f, 450f);
        AddChild(board);
        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 10);
        board.AddChild(col);

        var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        top.AddThemeConstantOverride("separation", 10);
        col.AddChild(top);
        _search = new LineEdit { PlaceholderText = Loc.T("Search…"), CustomMinimumSize = new Vector2(290f, 40f), ClearButtonEnabled = true };
        _search.AddThemeFontSizeOverride("font_size", 17);
        _search.AddThemeStyleboxOverride("normal", CuteStyle.Box(CuteStyle.Cream, new Color(CuteStyle.Mint, 0.7f), 20, 2, 10));
        _search.AddThemeStyleboxOverride("focus", CuteStyle.Box(new Color(0, 0, 0, 0), Colors.White, 20, 3, 10));
        _search.AddThemeColorOverride("font_color", CuteStyle.Ink);
        _search.AddThemeColorOverride("font_placeholder_color", CuteStyle.InkSoft);
        _search.AddThemeColorOverride("caret_color", CuteStyle.Ink);
        _search.TextChanged += _ => ApplyFilter();
        IconView lens = CuteStyle.Icon(new IconSpec("target", CuteStyle.Mint), 30f, CuteStyle.Mint);
        lens.Disc = false;
        lens.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        top.AddChild(lens);
        top.AddChild(_search);
        foreach ((Shelf shelf, string label, Color color) in new[]
                 {
                     (Shelf.All, "All", CuteStyle.Mint), (Shelf.Weapons, "Weapons", CuteStyle.Peach),
                     (Shelf.Items, "Items", CuteStyle.Sky), (Shelf.Tomes, "Tomes", CuteStyle.Lavender),
                 })
        {
            Shelf s = shelf;
            Button b = CuteStyle.Pill(Loc.T(label), color, () => { _shelf = s; ApplyFilter(); }, 16);
            b.CustomMinimumSize = new Vector2(118f, 40f);
            top.AddChild(b);
            _shelfPills.Add((s, b));
        }
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        top.AddChild(spacer);
        Button allOn = CuteStyle.Pill(Loc.T("All on"), CuteStyle.Butter, AllOn, 16);
        allOn.CustomMinimumSize = new Vector2(110f, 40f);
        top.AddChild(allOn);

        _locked = CuteStyle.Text("", 15, CuteStyle.Coral, HorizontalAlignment.Center);
        _locked.Visible = false;
        col.AddChild(_locked);

        _scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 330f),
        };
        col.AddChild(_scroll);
        _grid = new GridContainer { Columns = 5, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _grid.AddThemeConstantOverride("h_separation", 8);
        _grid.AddThemeConstantOverride("v_separation", 8);
        _scroll.AddChild(_grid);

        var bottom = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        bottom.AddThemeConstantOverride("separation", 14);
        MainMenu.Place(bottom, 60f, 670f, 1160f, 40f);
        AddChild(bottom);
        _backButton = CuteStyle.Pill(Loc.T("BACK"), CuteStyle.Peach, () => _back(), 18);
        _backButton.CustomMinimumSize = new Vector2(180f, 40f);
        bottom.AddChild(_backButton);
        _status = CuteStyle.Title("", 15, CuteStyle.OnNightDim);
        _status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _status.ClipText = true;
        _status.VerticalAlignment = VerticalAlignment.Center;
        bottom.AddChild(_status);
    }

    private void BuildTiles()
    {
        foreach (Node n in _grid.GetChildren()) { _grid.RemoveChild(n); n.QueueFree(); }
        _tiles.Clear();
        _entries = Toggler.Entries();
        foreach (ToggleEntry e in _entries)
        {
            ToggleEntry entry = e;
            Color shelf = ShelfColor(e.Kind);
            Button tile = CuteStyle.Card(shelf, () => Flip(entry));
            tile.CustomMinimumSize = new Vector2(222f, 56f);
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 8);
            IconSpec spec = e.Def switch
            {
                WeaponDef w => Icons.For(w),
                ItemDef i => Icons.For(i),
                TomeDef t => Icons.For(t),
                _ => new IconSpec(Icons.Fallback, Colors.White),
            };
            IconView ic = CuteStyle.Icon(spec, 38f, Catalog.RarityColor(e.Rarity));
            ic.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(ic);
            var tcol = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            tcol.AddThemeConstantOverride("separation", 0);
            Label name = CuteStyle.Text(Loc.T(e.Name), 14, CuteStyle.Ink);
            name.ClipText = true;
            tcol.AddChild(name);
            Label kind = CuteStyle.Text(Loc.IsArabic ? $"{Loc.T(e.Kind.ToString())}  ·  {Loc.T(Catalog.RarityName(e.Rarity))}" : $"{e.Kind}  ·  {e.Rarity}", 11, CuteStyle.InkSoft);
            tcol.AddChild(kind);
            row.AddChild(tcol);
            PanelContainer tagBox = CuteStyle.Tag(Loc.T("ON"), CuteStyle.Mint, CuteStyle.Ink, 12);
            tagBox.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(tagBox);
            tile.AddChild(CuteStyle.Inset(row, 8, 4, 8, 4));
            tile.FocusEntered += () => ShowEntry(entry);
            _grid.AddChild(tile);
            _tiles.Add((e, tile, (Label)tagBox.GetChild(0), tagBox));
        }
    }

    private static Color ShelfColor(ToggleKind k) => k switch
    {
        ToggleKind.Weapon => CuteStyle.Peach,
        ToggleKind.Item => CuteStyle.Sky,
        _ => CuteStyle.Lavender,
    };

    // ============================================================ actions

    /// <summary>Called by the menu on entry: rebuild from the profile, land focus.
    /// <paramref name="arg"/> may name a shelf ("weapons", "items", "tomes") or
    /// start a search ("search=fire").</summary>
    public void Open(string arg = "")
    {
        Build();
        BuildTiles();
        _shelf = arg switch { "weapons" => Shelf.Weapons, "items" => Shelf.Items, "tomes" => Shelf.Tomes, _ => Shelf.All };
        _search.Text = arg.StartsWith("search=") ? arg["search=".Length..] : "";
        bool owned = Toggler.Owned;
        _locked.Visible = !owned;
        _locked.Text = owned ? "" : Loc.T("Buy The Toggler in the Pearl Shop to make these choices count. You can set them up now.");
        ApplyFilter();
        _status.Text = Loc.T("Press a tile to switch it off or on. Switched-off things never show up in level-ups, chests or the merchant.");
        if (_tiles.FirstOrDefault(t => t.Tile.Visible).Tile is { } first) first.GrabFocus();
        else _search.GrabFocus();
    }

    private void Flip(ToggleEntry e)
    {
        bool off = Toggler.IsOff(e.Id);
        if (!off && Toggler.AtMinimum(e, _entries))
        {
            int total = _entries.Count(x => x.Kind == e.Kind);
            _status.Text = Loc.F(e.Kind switch
            {
                ToggleKind.Weapon => "Keep at least {0} weapons on, so offers never run dry.",
                ToggleKind.Item => "Keep at least {0} items on, so offers never run dry.",
                _ => "Keep at least {0} tomes on, so offers never run dry.",
            }, Toggler.MinOf(e.Kind, total));
            _status.AddThemeColorOverride("font_color", CuteStyle.Coral);
            return;
        }
        Toggler.Set(e.Id, !off);
        Profile.Save();
        _status.Text = Loc.F(off ? "{0} is now ON." : "{0} is now OFF.", e.Name);
        _status.AddThemeColorOverride("font_color", off ? CuteStyle.Mint : CuteStyle.Peach);
        RefreshTiles();
    }

    private void AllOn()
    {
        foreach (ToggleEntry e in _entries) Toggler.Set(e.Id, false);
        Profile.Save();
        _status.Text = Loc.T("Everything is back on.");
        _status.AddThemeColorOverride("font_color", CuteStyle.Mint);
        RefreshTiles();
    }

    private void ApplyFilter()
    {
        string q = _search.Text.Trim();
        foreach (var (e, tile, _, _) in _tiles)
        {
            bool shelf = _shelf switch
            {
                Shelf.Weapons => e.Kind == ToggleKind.Weapon,
                Shelf.Items => e.Kind == ToggleKind.Item,
                Shelf.Tomes => e.Kind == ToggleKind.Tome,
                _ => true,
            };
            bool match = q.Length == 0 || e.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                         || Loc.T(e.Name).Contains(q, StringComparison.OrdinalIgnoreCase)
                         || e.Rarity.ToString().Contains(q, StringComparison.OrdinalIgnoreCase);
            tile.Visible = shelf && match;
        }
        foreach (var (s, b) in _shelfPills)
            CuteStyle.StylePill(b, s switch { Shelf.Weapons => CuteStyle.Peach, Shelf.Items => CuteStyle.Sky, Shelf.Tomes => CuteStyle.Lavender, _ => CuteStyle.Mint }, s == _shelf);
        RefreshTiles();
    }

    private void RefreshTiles()
    {
        foreach (var (e, tile, tag, box) in _tiles)
        {
            bool off = Toggler.IsOff(e.Id);
            CuteStyle.StyleCard(tile, ShelfColor(e.Kind), false, off);
            tag.Text = Loc.T(off ? "OFF" : "ON");
            var s = new StyleBoxFlat { BgColor = off ? new Color(0.78f, 0.76f, 0.82f) : CuteStyle.Mint };
            s.SetCornerRadiusAll(10);
            s.ContentMarginLeft = 8; s.ContentMarginRight = 8; s.ContentMarginTop = 1; s.ContentMarginBottom = 1;
            box.AddThemeStyleboxOverride("panel", s);
            tile.Modulate = off ? new Color(1f, 1f, 1f, 0.72f) : Colors.White;
        }
        string Shelf(ToggleKind k, string name)
        {
            int total = _entries.Count(x => x.Kind == k);
            int on = total - Toggler.OffCount(_entries, k);
            return $"{Loc.T(name)} {on}/{total}";
        }
        _counts.Text = $"{Loc.T("switched on:")}   {Shelf(ToggleKind.Weapon, "Weapons")}   ·   {Shelf(ToggleKind.Item, "Items")}   ·   {Shelf(ToggleKind.Tome, "Tomes")}";
    }

    private void ShowEntry(ToggleEntry e)
    {
        string what = e.Def switch
        {
            WeaponDef w => w.Description,
            ItemDef i => Progression.OfferPool.DescribeEffects(i),
            TomeDef t => t.Description,
            _ => "",
        };
        _status.Text = $"{Loc.T(e.Name)}: {Loc.T(what)}";
        _status.AddThemeColorOverride("font_color", CuteStyle.OnNightDim);
    }
}
