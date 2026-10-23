using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The "route & challenges" page, opened from the play page. Two views on one
/// soft rounded board (<see cref="CuteStyle"/>):
///
///   <b>Map challenges</b> — five map tabs, each with its eleven named
///   challenges (Meta/MapChallenges) as cards: goal, rules, reward, done mark.
///   Pressing a card selects it (one at a time; press again to clear), which
///   also sets the route to start on that map.
///
///   <b>Modifiers</b> — the twelve global run modifiers (Meta/Challenges) as
///   toggles, any number at once, with the combined Pearls multiplier.
///
/// The route pill cycles Meta/Routes. Every change is saved at once. All
/// controls are focusable buttons, so keyboard and gamepad navigation come from
/// the built-in ui_* actions like the rest of the menu.
/// </summary>
public sealed partial class ChallengePicker : Control
{
    private static readonly Dictionary<string, (Color Color, string Glyph)> MapLook = new()
    {
        ["thornmeadow"] = (CuteStyle.Mint, "clover"),
        ["rimeglass"] = (CuteStyle.Sky, "snowflake"),
        ["gravemoor"] = (CuteStyle.Lavender, "moon"),
        ["shallows"] = (CuteStyle.Seafoam, "wave"),
        ["cinder"] = (CuteStyle.Peach, "flame"),
    };

    private static readonly Dictionary<string, string> ModifierGlyph = new()
    {
        ["hyper"] = "fastforward", ["glass"] = "heart_broken", ["swarm"] = "multi", ["molten"] = "flame",
        ["speed"] = "hourglass", ["chonk"] = "expand", ["noitems"] = "pouch", ["pacifist"] = "bell",
        ["elites"] = "crown", ["fog"] = "cloud", ["lowgrav"] = "feather", ["surge"] = "wave",
    };

    private readonly Action _back;
    private bool _modifiersView;
    private string _map = "thornmeadow";

    private Button _tabMaps = null!, _tabMods = null!, _route = null!, _clear = null!, _backButton = null!;
    private Control _mapsView = null!, _modsView = null!;
    private readonly List<(string Id, Button Button, Label Count)> _mapTabs = new();
    private GridContainer _cards = null!;
    private readonly List<(MapChallengeDef? Def, Button Button)> _cardButtons = new();
    private readonly List<(ChallengeDef Def, Button Button)> _toggles = new();
    private Label _total = null!;
    private Label _detailTitle = null!, _detailBody = null!;
    private IconView _detailIcon = null!;

    public ChallengePicker(Action back)
    {
        _back = back;
        Name = "ChallengePicker";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Build();
    }

    /// <summary>One line for the play page's button.</summary>
    public static string Summary()
    {
        IReadOnlyList<ChallengeDef> set = Challenges.Selected;
        string chal = set.Count == 0 ? "no modifiers" : $"{set.Count} modifier{(set.Count == 1 ? "" : "s")} x{Challenges.RewardFor(set):0.##}";
        if (MapChallenges.Selected is { } m) return $"MAP CHALLENGE: {m.Name.ToUpperInvariant()}  ·  {chal}";
        return $"ROUTE: {Routes.Selected.Name.ToUpperInvariant()}  ·  {chal}";
    }

    // ============================================================== build

    private void Build()
    {
        Label heading = CuteStyle.Title("ROUTE & CHALLENGES", 20, CuteStyle.OnNightDim);
        MainMenu.Place(heading, 64f, 182f, 400f, 26f);
        AddChild(heading);
        _total = CuteStyle.Title("", 17, CuteStyle.Butter, HorizontalAlignment.Right);
        MainMenu.Place(_total, 620f, 182f, 600f, 26f);
        AddChild(_total);

        PanelContainer board = CuteStyle.Panel(new Color(CuteStyle.Night, 0.86f), new Color(CuteStyle.Lavender, 0.45f), 24, 14);
        MainMenu.Place(board, 48f, 212f, 1184f, 450f);
        AddChild(board);
        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 10);
        board.AddChild(col);

        // Tabs and the route.
        var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        top.AddThemeConstantOverride("separation", 10);
        col.AddChild(top);
        _tabMaps = CuteStyle.Pill("Map challenges", CuteStyle.Lavender, () => ShowView(false), 17);
        _tabMods = CuteStyle.Pill("Modifiers", CuteStyle.Butter, () => ShowView(true), 17);
        foreach (Button b in new[] { _tabMaps, _tabMods }) { b.CustomMinimumSize = new Vector2(190f, 40f); top.AddChild(b); }
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        top.AddChild(spacer);
        _route = CuteStyle.Pill("", CuteStyle.Sky, PressRoute, 16);
        _route.CustomMinimumSize = new Vector2(520f, 40f);
        _route.ClipText = true;
        top.AddChild(_route);

        var stack = new Control { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(0f, 306f) };
        col.AddChild(stack);
        _mapsView = BuildMapsView();
        _modsView = BuildModifiersView();
        stack.AddChild(_mapsView);
        stack.AddChild(_modsView);

        // Detail strip.
        PanelContainer detail = CuteStyle.Panel(CuteStyle.Cream, new Color(CuteStyle.Lavender, 0.6f), 16, 8);
        detail.CustomMinimumSize = new Vector2(0f, 54f);
        col.AddChild(detail);
        var drow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        drow.AddThemeConstantOverride("separation", 12);
        detail.AddChild(drow);
        _detailIcon = CuteStyle.Icon(new IconSpec("flag", CuteStyle.Lavender), 38f, CuteStyle.Lavender);
        _detailIcon.Disc = false;
        _detailIcon.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        drow.AddChild(_detailIcon);
        var dcol = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        dcol.AddThemeConstantOverride("separation", 0);
        drow.AddChild(dcol);
        _detailTitle = CuteStyle.Text("", 16, CuteStyle.Ink);
        _detailBody = CuteStyle.Text("", 13, CuteStyle.InkSoft);
        _detailBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        dcol.AddChild(_detailTitle);
        dcol.AddChild(_detailBody);

        // Bottom buttons.
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 12);
        MainMenu.Place(row, 60f, 670f, 700f, 40f);
        AddChild(row);
        _backButton = CuteStyle.Pill("BACK", CuteStyle.Peach, () => _back(), 18);
        _clear = CuteStyle.Pill("CLEAR ALL", CuteStyle.Rose, ClearAll, 18);
        foreach (Button b in new[] { _backButton, _clear }) { b.CustomMinimumSize = new Vector2(180f, 42f); row.AddChild(b); }
        _clear.FocusEntered += () => ShowText("flag", CuteStyle.Rose, "Clear all", "Unselect the map challenge and every modifier.");
        _backButton.FocusEntered += () => ShowText("next", CuteStyle.Peach, "Back", "Back to choosing who washes up.");
    }

    private Control BuildMapsView()
    {
        var view = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        view.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        view.AddThemeConstantOverride("separation", 10);

        var tabs = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        tabs.AddThemeConstantOverride("separation", 8);
        view.AddChild(tabs);
        foreach (string id in MapChallenges.MapOrder)
        {
            string mapId = id;
            (Color color, string glyph) = MapLook[id];
            Button b = CuteStyle.Pill("", color, () => { _map = mapId; RebuildCards(); }, 15);
            b.CustomMinimumSize = new Vector2(222f, 40f);
            var inner = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            inner.AddThemeConstantOverride("separation", 6);
            IconView ic = CuteStyle.Icon(new IconSpec(glyph, color.Darkened(0.05f)), 26f, color);
            ic.Disc = false;
            ic.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            inner.AddChild(ic);
            Label name = CuteStyle.Text(Data.Stages.ById(id)?.DisplayName ?? id, 15, CuteStyle.Ink);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.ClipText = true;
            inner.AddChild(name);
            Label count = CuteStyle.Text("", 13, CuteStyle.InkSoft);
            inner.AddChild(count);
            b.AddChild(CuteStyle.Inset(inner, 12, 0, 12, 0));
            b.FocusEntered += () => ShowMapText(mapId);
            tabs.AddChild(b);
            _mapTabs.Add((id, b, count));
        }

        _cards = new GridContainer { Columns = 4, MouseFilter = MouseFilterEnum.Ignore };
        _cards.AddThemeConstantOverride("h_separation", 10);
        _cards.AddThemeConstantOverride("v_separation", 8);
        view.AddChild(_cards);
        return view;
    }

    private Control BuildModifiersView()
    {
        var view = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        view.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        view.AddThemeConstantOverride("separation", 10);
        var grid = new GridContainer { Columns = 3, MouseFilter = MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10);
        view.AddChild(grid);
        foreach (ChallengeDef def in Challenges.All)
        {
            ChallengeDef c = def;
            Button b = CuteStyle.Card(CuteStyle.Butter, () => { Challenges.Toggle(c); Profile.Save(); Refresh(); });
            b.CustomMinimumSize = new Vector2(376f, 66f);
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 10);
            IconView ic = CuteStyle.Icon(new IconSpec(ModifierGlyph.GetValueOrDefault(c.Id, "star"), CuteStyle.Butter), 36f, CuteStyle.Butter);
            ic.Disc = false;
            ic.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(ic);
            var tcol = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            tcol.AddThemeConstantOverride("separation", 0);
            tcol.AddChild(CuteStyle.Text(c.Name, 17, CuteStyle.Ink));
            tcol.AddChild(CuteStyle.Text(c.RewardText, 13, CuteStyle.InkSoft));
            row.AddChild(tcol);
            PanelContainer mark = CuteStyle.Tag("ON", CuteStyle.Mint, CuteStyle.Ink, 13);
            mark.Name = "Mark";
            mark.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(mark);
            b.AddChild(CuteStyle.Inset(row, 12, 6, 14, 6));
            b.FocusEntered += () => ShowModifier(c);
            grid.AddChild(b);
            _toggles.Add((c, b));
        }
        return view;
    }

    /// <summary>The selected map's eleven cards plus "no map challenge".</summary>
    private void RebuildCards()
    {
        foreach (Node n in _cards.GetChildren()) { _cards.RemoveChild(n); n.QueueFree(); }
        _cardButtons.Clear();
        foreach (MapChallengeDef def in MapChallenges.ForMap(_map))
            _cardButtons.Add((def, MapCard(def)));
        _cardButtons.Add((null, NoneCard()));
        foreach (var (_, b) in _cardButtons) _cards.AddChild(b);
        Refresh();
    }

    private Button MapCard(MapChallengeDef def)
    {
        Color color = MapLook[def.MapId].Color;
        bool done = MapChallenges.IsDone(def);
        Button b = CuteStyle.Card(color, () => PressCard(def));
        b.CustomMinimumSize = new Vector2(281f, 80f);

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);
        string glyph = CardGlyph(def);
        IconView ic = CuteStyle.Icon(new IconSpec(glyph, color), 34f, color);
        ic.Disc = false;
        ic.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(ic);

        var tcol = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        tcol.AddThemeConstantOverride("separation", 1);
        row.AddChild(tcol);
        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        Label name = CuteStyle.Text(def.Name, 15, CuteStyle.Ink);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.ClipText = true;
        head.AddChild(name);
        if (done) head.AddChild(CuteStyle.Tag("DONE", CuteStyle.Mint, CuteStyle.Ink, 11));
        tcol.AddChild(head);
        Label rules = CuteStyle.Text(def.RulesText, 11, CuteStyle.InkSoft);
        rules.ClipText = true;
        tcol.AddChild(rules);
        var tags = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        tags.AddThemeConstantOverride("separation", 4);
        tags.AddChild(CuteStyle.Tag(def.GoalShort, color.Lerp(Colors.White, 0.35f), CuteStyle.Ink, 11));
        tags.AddChild(CuteStyle.Tag($"+{def.Pearls}", CuteStyle.Butter.Lerp(Colors.White, 0.35f), CuteStyle.Ink, 11));
        if (def.Cosmetic != CosmeticKind.None)
            tags.AddChild(CuteStyle.Tag(def.Cosmetic == CosmeticKind.Hat ? "HAT" : "SKIN", CuteStyle.Rose.Lerp(Colors.White, 0.3f), CuteStyle.Ink, 11));
        tcol.AddChild(tags);

        b.AddChild(CuteStyle.Inset(row, 10, 6, 10, 6));
        b.FocusEntered += () => ShowMapChallenge(def);
        return b;
    }

    private Button NoneCard()
    {
        Button b = CuteStyle.Card(CuteStyle.Muted, () => PressCard(null));
        b.CustomMinimumSize = new Vector2(281f, 80f);
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);
        IconView ic = CuteStyle.Icon(new IconSpec("wave", CuteStyle.Sky), 34f, CuteStyle.Sky);
        ic.Disc = false;
        ic.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(ic);
        var tcol = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        tcol.AddChild(CuteStyle.Text("No map challenge", 15, CuteStyle.Ink));
        tcol.AddChild(CuteStyle.Text("Play your chosen route", 11, CuteStyle.InkSoft));
        row.AddChild(tcol);
        b.AddChild(CuteStyle.Inset(row, 10, 6, 10, 6));
        b.FocusEntered += () => ShowText("wave", CuteStyle.Sky, "No map challenge",
            "Play the route on the pill above, with whatever modifiers you have switched on.");
        return b;
    }

    // ============================================================ actions

    /// <summary>Open on a view: "modifiers", a map id, or the selected map
    /// challenge's map. Called by the menu on entry ("run:&lt;arg&gt;").</summary>
    public void Open(string arg = "")
    {
        if (arg == "modifiers") _modifiersView = true;
        else
        {
            _modifiersView = false;
            if (MapChallenges.MapOrder.Contains(arg)) _map = arg;
            else if (MapChallenges.Selected is { } sel) _map = sel.MapId;
        }
        RebuildCards();
        ShowView(_modifiersView);
        FocusFirst();
    }

    public void FocusFirst()
    {
        if (_modifiersView) { if (_toggles.Count > 0) _toggles[0].Button.GrabFocus(); return; }
        MapChallengeDef? sel = MapChallenges.Selected;
        foreach (var (def, b) in _cardButtons)
            if (def is not null && def == sel) { b.GrabFocus(); return; }
        if (_cardButtons.Count > 0) _cardButtons[0].Button.GrabFocus();
    }

    private void ShowView(bool modifiers)
    {
        _modifiersView = modifiers;
        _mapsView.Visible = !modifiers;
        _modsView.Visible = modifiers;
        CuteStyle.StylePill(_tabMaps, CuteStyle.Lavender, !modifiers);
        CuteStyle.StylePill(_tabMods, CuteStyle.Butter, modifiers);
        Refresh();
    }

    private void PressCard(MapChallengeDef? def)
    {
        MapChallenges.Select(def is null || MapChallenges.Selected == def ? null : def);
        Profile.Save();
        Refresh();
        if (def is not null) ShowMapChallenge(def);
    }

    private void PressRoute()
    {
        if (MapChallenges.Selected is not null)
        {
            ShowText("flag", CuteStyle.Sky, "Route set by the map challenge",
                "A map challenge always starts on its own map; the next two are drawn. Clear it to pick a route.");
            return;
        }
        Routes.Cycle(+1);
        Profile.Save();
        Refresh();
        RouteDef r = Routes.Selected;
        ShowText("next", CuteStyle.Sky, $"Route: {r.Name}", $"{Routes.DescribeMaps(r)}.  {r.Blurb}");
    }

    private void ClearAll()
    {
        Challenges.ClearSelection();
        MapChallenges.Select(null);
        Profile.Save();
        Refresh();
    }

    // ============================================================ refresh

    public void Refresh()
    {
        MapChallengeDef? sel = MapChallenges.Selected;
        RouteDef r = Routes.Selected;
        _route.Text = sel is not null
            ? $"Route:  {Routes.DescribeMaps(Routes.ById("start:" + sel.MapId) ?? r)}   (map challenge)"
            : $"Route:  {r.Name}   ·   {Routes.DescribeMaps(r)}";

        foreach (var (id, b, count) in _mapTabs)
        {
            bool on = id == _map;
            CuteStyle.StylePill(b, MapLook[id].Color, on);
            int n = MapChallenges.DoneCount(id);
            int total = MapChallenges.ForMap(id).Count();
            count.Text = sel?.MapId == id ? $"* {n}/{total}" : $"{n}/{total}";
        }
        foreach (var (def, b) in _cardButtons)
        {
            Color color = def is null ? CuteStyle.Muted : MapLook[def.MapId].Color;
            bool on = def is null ? sel is null : def == sel;
            CuteStyle.StyleCard(b, color, on, false);
        }
        foreach (var (c, b) in _toggles)
        {
            bool on = Challenges.IsSelected(c);
            CuteStyle.StyleCard(b, CuteStyle.Butter, on, false);
            if (b.FindChild("Mark", true, false) is Control mark) mark.Visible = on;
        }

        IReadOnlyList<ChallengeDef> set = Challenges.Selected;
        string mods = set.Count == 0 ? "no modifiers" : $"{set.Count} modifier{(set.Count == 1 ? "" : "s")}";
        string map = sel is null ? "" : $"{sel.Name}  ·  ";
        _total.Text = $"{map}{mods}  ·  {Pearls.Name} x{Challenges.RewardFor(set.Concat(sel?.Modifiers.Select(Challenges.ById).OfType<ChallengeDef>() ?? Enumerable.Empty<ChallengeDef>()).Distinct()):0.##}";
    }

    /// <summary>The card's picture: the crown for a run-winning challenge,
    /// else the map twist, else its first modifier, else a skull for the
    /// pressure tiers.</summary>
    private static string CardGlyph(MapChallengeDef c)
    {
        if (c.Goal == MapGoal.Swarm) return "crown";
        string? twist = c.Twist switch
        {
            MapTwist.Brambles => "spikes",
            MapTwist.Whiteout => "cloud",
            MapTwist.Frost => "snowflake",
            MapTwist.Night => "moon",
            MapTwist.RestlessGraves => "skull",
            MapTwist.HighTide => "wave",
            MapTwist.LavaFloor => "flame",
            MapTwist.Ashfall => "campfire",
            _ => null,
        };
        if (c.Tier > 0) return twist is null ? "skull" : twist;
        if (twist is not null) return twist;
        return c.Modifiers.Length > 0 ? ModifierGlyph.GetValueOrDefault(c.Modifiers[0], "flag") : "flag";
    }

    private void ShowMapText(string mapId)
    {
        (Color color, string glyph) = MapLook[mapId];
        int n = MapChallenges.DoneCount(mapId);
        ShowText(glyph, color, Data.Stages.ById(mapId)?.DisplayName ?? mapId,
            $"{n} of {MapChallenges.ForMap(mapId).Count()} challenges done here.  {Data.Stages.ById(mapId)?.Tagline}");
    }

    private void ShowMapChallenge(MapChallengeDef c)
    {
        long times = MapChallenges.Completions(c);
        string record = times > 0 ? $"Done {times}x." : "Not done yet.";
        string selected = MapChallenges.Selected == c ? "  Selected: press again to clear." : "  Press to select.";
        Color color = MapLook[c.MapId].Color;
        string glyph = CardGlyph(c);
        ShowText(glyph, color, $"{c.Name}  ·  {c.GoalText}",
            $"{c.Blurb}  Rules: {c.RulesText}.  Reward: {c.RewardText}.  {record}{selected}");
    }

    private void ShowModifier(ChallengeDef c)
    {
        List<string> maps = Challenges.ClearedMaps(c);
        long wins = Challenges.Wins(c);
        string record = maps.Count == 0 ? "not cleared yet" : $"cleared on {string.Join(", ", maps)}";
        if (wins > 0) record += $"; won {wins}x";
        ShowText(ModifierGlyph.GetValueOrDefault(c.Id, "star"), CuteStyle.Butter, $"{c.Name}  ·  {c.RewardText}", $"{c.Blurb}  ({record})");
    }

    private void ShowText(string glyph, Color color, string title, string body)
    {
        _detailIcon.Set(new IconSpec(glyph, color), color);
        _detailTitle.Text = title;
        _detailBody.Text = body;
    }
}
