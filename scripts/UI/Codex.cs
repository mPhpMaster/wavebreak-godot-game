using System.Collections.Generic;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The unlocks and secrets book: lifetime stats across the top, a row of
/// filter tabs, then the cards — every unlock, the shrines, the tomes and the
/// challenges.
///
/// Secret unlocks are drawn as "???" with their cryptic hint until earned; the
/// plain condition and the reward appear only afterwards. Normal unlocks show
/// their condition and, where the profile can measure it, how far along the
/// player is — "412 / 1000" turns a locked card into a goal.
///
/// With ~300 cards the tabs are what keep it usable. Every card is built once
/// per <see cref="Refresh"/> and a tab only toggles visibility, so switching is
/// instant. Keyboard/gamepad: left/right walks the tabs (focusing a tab selects
/// it), down enters the grid, up from the first row returns to the tabs, and
/// Q/E, Page Up/Down or the shoulder buttons switch tab from anywhere. Cards
/// are focusable buttons in a ScrollContainer that follows focus, which is all
/// scrolling needs.
/// </summary>
public sealed partial class Codex : Control
{
    private static readonly Color Ink = new(0.93f, 0.95f, 0.99f);
    private static readonly Color InkDim = new(0.62f, 0.68f, 0.78f);
    private static readonly Color Earned = new(0.45f, 0.92f, 0.58f);
    private static readonly Color Gold = new(1.00f, 0.84f, 0.38f);
    private static readonly Color SecretInk = new(0.78f, 0.58f, 1.00f);

    /// <summary>Which tabs a card belongs to; "All" is every bit.</summary>
    [System.Flags]
    private enum Tab
    {
        None = 0,
        Quests = 1 << 0,
        Secrets = 1 << 1,
        Characters = 1 << 2,
        Challenges = 1 << 3,
        Lore = 1 << 4,
        All = Quests | Secrets | Characters | Challenges | Lore,
    }

    private static readonly (Tab Tab, string Label)[] Tabs =
    {
        (Tab.All, "All"), (Tab.Quests, "Quests"), (Tab.Secrets, "Secrets"),
        (Tab.Characters, "Characters"), (Tab.Challenges, "Challenges"), (Tab.Lore, "Shrines & Tomes"),
    };

    /// <summary>Set by the menu so the last card can hand focus down to it.</summary>
    public Button? BackButton { get; set; }

    private Label _stats = null!;
    private GridContainer _grid = null!;
    private ScrollContainer _scroll = null!;
    private readonly List<Button> _cards = new();
    private readonly List<Tab> _cardTabs = new();
    private readonly List<Button> _visible = new();
    private readonly Button[] _tabButtons = new Button[Tabs.Length];
    private int _tab;

    public override void _Ready()
    {
        var col = new VBoxContainer();
        col.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        col.AddThemeConstantOverride("separation", 8);
        AddChild(col);

        _stats = MainMenu.MakeLabel("", 16, InkDim);
        col.AddChild(_stats);

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 6);
        col.AddChild(tabs);
        for (int i = 0; i < Tabs.Length; i++)
        {
            int index = i;
            Button b = TabButton();
            b.Pressed += () => SelectTab(index, focusGrid: true);
            b.FocusEntered += () => SelectTab(index, focusGrid: false);
            tabs.AddChild(b);
            _tabButtons[i] = b;
        }
        for (int i = 0; i < Tabs.Length; i++)
        {
            // Wrap left/right across the row, so the tabs work as a carousel.
            _tabButtons[i].FocusNeighborLeft = _tabButtons[i].GetPathTo(_tabButtons[(i + Tabs.Length - 1) % Tabs.Length]);
            _tabButtons[i].FocusNeighborRight = _tabButtons[i].GetPathTo(_tabButtons[(i + 1) % Tabs.Length]);
        }

        var scroll = _scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        col.AddChild(scroll);

        _grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _grid.AddThemeConstantOverride("h_separation", 10);
        _grid.AddThemeConstantOverride("v_separation", 10);
        scroll.AddChild(_grid);

        Refresh();
    }

    public void FocusFirst()
    {
        if (_visible.Count > 0) _visible[0].GrabFocus();
        else _tabButtons[_tab].GrabFocus();
    }

    /// <summary>Open the Secrets tab and focus its first card. Used by the menu
    /// capture to show the hidden entries.</summary>
    public async void FocusFirstSecret()
    {
        SelectTab(System.Array.FindIndex(Tabs, t => t.Tab == Tab.Secrets), focusGrid: false);
        // The grid has no layout until it has been sorted, and scrolling to an
        // unplaced card goes nowhere.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_visible.Count == 0) return;
        _visible[0].GrabFocus();
        _scroll.EnsureControlVisible(_visible[0]);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!IsVisibleInTree() || !e.IsPressed() || e.IsEcho()) return;
        int step = 0;
        if (e.IsActionPressed("ui_page_up")) step = -1;
        else if (e.IsActionPressed("ui_page_down")) step = 1;
        else if (e is InputEventKey { PhysicalKeycode: Key.Q }) step = -1;
        else if (e is InputEventKey { PhysicalKeycode: Key.E }) step = 1;
        else if (e is InputEventJoypadButton { ButtonIndex: JoyButton.LeftShoulder }) step = -1;
        else if (e is InputEventJoypadButton { ButtonIndex: JoyButton.RightShoulder }) step = 1;
        if (step == 0) return;
        GetViewport().SetInputAsHandled();
        bool inTabs = System.Array.IndexOf(_tabButtons, GetViewport().GuiGetFocusOwner()) >= 0;
        SelectTab((_tab + step + Tabs.Length) % Tabs.Length, focusGrid: !inTabs);
        if (inTabs) _tabButtons[_tab].GrabFocus();
    }

    /// <summary>Rebuild from the profile. Cheap, and only done when the page opens.</summary>
    public void Refresh()
    {
        if (_grid is null) return;
        foreach (Node c in _grid.GetChildren()) { _grid.RemoveChild(c); c.QueueFree(); }
        _cards.Clear();
        _cardTabs.Clear();

        long best = Profile.Get(Profile.BestSeconds);
        int secretsTotal = 0, secretsEarned = 0;
        foreach (UnlockDef u in Unlocks.All)
            if (u.Secret) { secretsTotal++; if (Profile.IsUnlocked(u.Id)) secretsEarned++; }
        _stats.Text =
            $"UNLOCKED {Profile.UnlockedIds.Count}/{Unlocks.All.Count}   ·   SECRETS {secretsEarned}/{secretsTotal}   ·   " +
            $"runs {Profile.Get(Profile.Runs)}   wins {Profile.Get(Profile.Wins)}   kills {Profile.Get(Profile.TotalKills)}   " +
            $"bosses {Profile.Get(Profile.BossesKilled)}   best {best / 60:00}:{best % 60:00}   best level {Profile.Get(Profile.BestLevel)}";

        foreach (UnlockDef u in Unlocks.All) AddCard(UnlockCard(u), TabsOf(u));
        foreach (SecretKindDef k in SecretSpawner.Kinds) AddCard(ShrineCard(k), Tab.Secrets | Tab.Lore);
        foreach (Data.TomeDef t in Data.Tomes.All) AddCard(TomeCard(t), Tab.Lore);
        foreach (ChallengeDef c in Challenges.All) AddCard(ChallengeCard(c), Tab.Challenges);

        for (int i = 0; i < Tabs.Length; i++)
        {
            int n = 0;
            foreach (Tab t in _cardTabs) if ((t & Tabs[i].Tab) != 0) n++;
            _tabButtons[i].Text = $"{Tabs[i].Label}  {n}";
        }
        ApplyFilter();
    }

    /// <summary>Which tabs an unlock appears under (besides "All").</summary>
    private static Tab TabsOf(UnlockDef u)
    {
        Tab t = u.Secret ? Tab.Secrets : Tab.Quests;
        if (u.Reward is RewardKind.Character or RewardKind.Skin or RewardKind.Hat
            || u.Metric is Metric.CharacterWins or Metric.CharactersWon or Metric.CharacterKills or Metric.CharacterHardWins)
            t |= Tab.Characters;
        if (u.Metric is Metric.ChallengeWins or Metric.ChallengesWon or Metric.DifficultyWins)
            t |= Tab.Challenges;
        return t;
    }

    private void AddCard(Button card, Tab tabs)
    {
        _grid.AddChild(card);
        _cards.Add(card);
        _cardTabs.Add(tabs);
    }

    private void SelectTab(int index, bool focusGrid)
    {
        if (index < 0 || index >= Tabs.Length) return;
        if (index != _tab)
        {
            _tab = index;
            ApplyFilter();
            _scroll.ScrollVertical = 0;
        }
        if (focusGrid) FocusFirst();
    }

    /// <summary>Show the active tab's cards and rewire the focus edges: the
    /// tabs lead down into the first card, the first row leads back up to the
    /// active tab, and the last row leads down to Back.</summary>
    private void ApplyFilter()
    {
        Tab want = Tabs[_tab].Tab;
        _visible.Clear();
        for (int i = 0; i < _cards.Count; i++)
        {
            bool show = (_cardTabs[i] & want) != 0;
            Button card = _cards[i];
            card.Visible = show;
            card.FocusNeighborTop = new NodePath();
            card.FocusNeighborBottom = new NodePath();
            if (show) _visible.Add(card);
        }
        for (int i = 0; i < _tabButtons.Length; i++)
        {
            Button b = _tabButtons[i];
            bool active = i == _tab;
            b.AddThemeColorOverride("font_color", active ? Gold : InkDim);
            b.AddThemeStyleboxOverride("normal", TabBox(active));
            b.FocusNeighborBottom = _visible.Count > 0 ? b.GetPathTo(_visible[0]) : new NodePath();
        }

        int cols = _grid.Columns;
        for (int i = 0; i < _visible.Count && i < cols; i++)
            _visible[i].FocusNeighborTop = _visible[i].GetPathTo(_tabButtons[_tab]);
        if (BackButton is not null)
        {
            // The last row may be short; every card in it points down at Back,
            // so the page can always be left by keyboard.
            int lastRow = _visible.Count == 0 ? 0 : (_visible.Count - 1) / cols * cols;
            for (int i = lastRow; i < _visible.Count; i++)
                _visible[i].FocusNeighborBottom = _visible[i].GetPathTo(BackButton);
            BackButton.FocusNeighborTop = BackButton.GetPathTo(_visible.Count > 0 ? _visible[^1] : _tabButtons[_tab]);
        }
    }

    private static Button TabButton()
    {
        var b = new Button { FocusMode = FocusModeEnum.All, CustomMinimumSize = new Vector2(0f, 34f) };
        b.AddThemeFontSizeOverride("font_size", 15);
        b.AddThemeColorOverride("font_focus_color", Gold);
        b.AddThemeColorOverride("font_hover_color", Gold);
        b.AddThemeStyleboxOverride("hover", TabBox(false));
        b.AddThemeStyleboxOverride("pressed", TabBox(true));
        var focus = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = Gold };
        focus.SetBorderWidthAll(2);
        focus.SetCornerRadiusAll(4);
        b.AddThemeStyleboxOverride("focus", focus);
        return b;
    }

    private static StyleBoxFlat TabBox(bool active)
    {
        var box = new StyleBoxFlat
        {
            BgColor = active ? new Color(0.14f, 0.16f, 0.24f, 0.95f) : new Color(0.06f, 0.07f, 0.11f, 0.85f),
            BorderColor = active ? Gold : new Color(0, 0, 0, 0),
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
        };
        box.BorderWidthBottom = active ? 2 : 0;
        box.SetCornerRadiusAll(4);
        return box;
    }

    private static Button UnlockCard(UnlockDef u)
    {
        bool done = Profile.IsUnlocked(u.Id);
        bool hidden = u.Secret && !done;

        string title = hidden ? "???" : u.Name;
        string body = hidden ? u.Hint : u.Description;
        string reward = hidden ? "Reward: ???" : Unlocks.DescribeReward(u);
        if (!hidden && u.Reward == RewardKind.Character && Characters.ById(u.RewardId) is { } c)
            reward += $"  (look: {c.LookId})";
        string status = done ? "UNLOCKED" : hidden ? "SECRET" : Progress(u);
        Color accent = done ? Earned : u.Secret ? SecretInk : Gold;

        return Card(title, body, reward, status, accent, done);
    }

    private static Button ShrineCard(SecretKindDef k)
    {
        bool found = Profile.HasFoundSecret(k.Id);
        return Card(found ? k.DisplayName : "???",
                    found ? "A hidden shrine. It answered when touched." : "Something faint waits near the edge of the arena.",
                    found ? $"Gift: {k.RewardText}\nThis run: {SecretKindDef.Describe(k.RunBoons)}\nEvery run after: {SecretKindDef.Describe(k.LastingBoons)}"
                          : "Gift: ???  ·  grants stat boosts when found",
                    found ? "FOUND" : "HIDDEN SHRINE",
                    found ? Earned : SecretInk, found);
    }

    /// <summary>Progress text for conditions the profile can measure between
    /// runs: a running total for lifetime ladders, and for single-run feats the
    /// best attempt so far, labelled "best" so it does not read as a bar that
    /// carries over between runs.</summary>
    private static string Progress(UnlockDef u)
    {
        long have = Quests.StoredProgress(u);
        if (have < 0) return "LOCKED";
        string amount = $"{System.Math.Min(have, (long)u.Threshold):N0} / {u.Threshold:N0}";
        return Quests.IsRunMetric(u.Metric) ? $"BEST  {amount}" : amount;
    }

    private static Button Card(string title, string body, string reward, string status, Color accent, bool done)
    {
        var card = new Button { FocusMode = FocusModeEnum.All, CustomMinimumSize = new Vector2(370f, 132f) };
        var normal = new StyleBoxFlat { BgColor = done ? new Color(0.07f, 0.12f, 0.10f, 0.92f) : new Color(0.06f, 0.07f, 0.11f, 0.90f), BorderColor = accent };
        normal.SetBorderWidthAll(0);
        normal.BorderWidthLeft = 4;
        normal.SetCornerRadiusAll(5);
        var focus = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = Gold };
        focus.SetBorderWidthAll(2);
        focus.SetCornerRadiusAll(5);
        card.AddThemeStyleboxOverride("normal", normal);
        card.AddThemeStyleboxOverride("hover", normal);
        card.AddThemeStyleboxOverride("pressed", normal);
        card.AddThemeStyleboxOverride("focus", focus);

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        box.OffsetLeft = 14; box.OffsetRight = -10; box.OffsetTop = 8; box.OffsetBottom = -8;
        box.AddThemeConstantOverride("separation", 2);
        card.AddChild(box);

        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        Label t = MainMenu.MakeLabel(title, 19, done ? Ink : accent.Lerp(Ink, 0.3f));
        t.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(t);
        head.AddChild(MainMenu.MakeLabel(status, 12, accent));
        box.AddChild(head);

        Label b = MainMenu.MakeLabel(body, 14, done ? Ink : InkDim);
        b.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        b.SizeFlagsVertical = SizeFlags.ExpandFill;
        box.AddChild(b);
        box.AddChild(MainMenu.MakeLabel(reward, 13, accent));
        return card;
    }
}
