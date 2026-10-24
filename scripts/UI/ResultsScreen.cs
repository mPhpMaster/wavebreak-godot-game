using System.Collections.Generic;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Game;
using Wavebreak.Progression;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// What the run was, once it is over.
///
/// Raised by <see cref="RunEnded"/> — <c>Game.Main</c> is the handler and calls
/// <see cref="Show"/> — and it stays up until the player restarts.
///
/// Layout, top to bottom, all in containers so it mirrors for Arabic and
/// fits 1280x720 and 1920x1080 without scrolling (only the item grid scrolls):
/// <list type="number">
/// <item>Header: VICTORY / YOU SURVIVED / YOU DIED, the difficulty tier as a
/// badge, a leaderboard ribbon when the run placed, and one short line about
/// how it ended.</item>
/// <item>Six stat tiles with icons: time, level, kills, bosses, gold, Pearls.</item>
/// <item>Three cards: the Pearls breakdown (lines, total, balance apart), the
/// build (weapon and item icons in a grid, names on hover), and the
/// scoreboard (one row per player; single-player shows you).</item>
/// <item>Big RESTART and MAIN MENU buttons with their keys under them, and the
/// seed in small print.</item>
/// </list>
///
/// The totals come from the inventory and weapon system (authoritative); the
/// itemised build is accumulated from the bus as the run happens.
///
/// Restart reloads the scene rather than resetting state in place: a reload is
/// the only way to be certain nothing from the dead run (a handler still on the
/// bus) survives into the next.
/// </summary>
public sealed partial class ResultsScreen : Control
{
    private const float MaxWidth = 1560f, MaxHeight = 940f, Margin = 20f;

    private static readonly Color Dim = new(0.01f, 0.02f, 0.04f, 0.80f);
    private static readonly Color PanelBack = new(0.07f, 0.08f, 0.13f, 0.98f);
    private static readonly Color CardBack = new(0.12f, 0.14f, 0.22f, 1f);
    private static readonly Color Ink = new(0.95f, 0.97f, 1.00f);
    private static readonly Color InkDim = new(0.70f, 0.76f, 0.86f);
    private static readonly Color Outline = new(0.02f, 0.03f, 0.05f, 0.9f);
    private static readonly Color Victory = new(0.45f, 0.95f, 0.60f);
    private static readonly Color Defeat = new(1.00f, 0.44f, 0.42f);
    private static readonly Color Gold = new(1.00f, 0.84f, 0.38f);
    private static readonly Color PearlTint = new(0.78f, 0.90f, 1.00f);

    // ------------------------------------------------------------ bus state

    private readonly Dictionary<string, int> _itemStacks = new();
    private readonly List<string> _itemOrder = new();
    private readonly Dictionary<string, int> _weaponLevels = new();
    private readonly List<string> _weaponOrder = new();

    // ------------------------------------------------------------- widgets

    private PanelContainer _panel = null!;
    private StyleBoxFlat _panelStyle = null!;
    private Label _title = null!;
    private Label _subtitle = null!;
    private Label _seed = null!;
    private HBoxContainer _badges = null!;
    private HBoxContainer _tiles = null!;
    private VBoxContainer _pearlCard = null!;
    private HFlowContainer _weaponGrid = null!;
    private HFlowContainer _itemGrid = null!;
    private Label _buildEmpty = null!;

    private bool _subscribed;
    private RunRewards.Payout? _payout;
    private int _leaderboardRank;

    /// <summary>True once the screen has been raised for this run.</summary>
    public bool IsShown { get; private set; }

    // ------------------------------------------------------------ lifecycle

    public override void _Ready()
    {
        Name = "ResultsScreen";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // The screen pauses the game, so it has to keep running through it.
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;

        Build();

        EventBus.Subscribe<ItemAcquired>(OnItemAcquired);
        EventBus.Subscribe<WeaponAcquired>(OnWeaponAcquired);
        EventBus.Subscribe<LeaderboardPlaced>(OnLeaderboard);
        _subscribed = true;
    }

    public override void _ExitTree()
    {
        if (!_subscribed) return;
        _subscribed = false;
        EventBus.Unsubscribe<ItemAcquired>(OnItemAcquired);
        EventBus.Unsubscribe<WeaponAcquired>(OnWeaponAcquired);
        EventBus.Unsubscribe<LeaderboardPlaced>(OnLeaderboard);
    }

    private void OnItemAcquired(ItemAcquired e)
    {
        if (string.IsNullOrEmpty(e.Id)) return;
        if (!_itemStacks.ContainsKey(e.Id)) _itemOrder.Add(e.Id);
        _itemStacks[e.Id] = e.NewStackCount;
    }

    private void OnWeaponAcquired(WeaponAcquired e)
    {
        if (string.IsNullOrEmpty(e.Id)) return;
        if (!_weaponLevels.ContainsKey(e.Id)) _weaponOrder.Add(e.Id);
        _weaponLevels[e.Id] = e.NewLevel;
    }

    /// <summary>The run made the local top ten (published after Show).</summary>
    private void OnLeaderboard(LeaderboardPlaced e)
    {
        if (e.Rank <= 0) return;
        _leaderboardRank = e.Rank;
        if (IsShown) WriteBadges();
    }

    // ---------------------------------------------------------------- build

    private void Build()
    {
        var dim = new ColorRect { Color = Dim };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        dim.MouseFilter = MouseFilterEnum.Stop;
        AddChild(dim);

        _panelStyle = Box(PanelBack, Defeat, 22);
        _panelStyle.SetBorderWidthAll(0);
        _panelStyle.BorderWidthTop = 6;
        _panelStyle.ContentMarginLeft = _panelStyle.ContentMarginRight = 22;
        _panelStyle.ContentMarginTop = 14;
        _panelStyle.ContentMarginBottom = 12;
        _panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        _panel.AddThemeStyleboxOverride("panel", _panelStyle);
        AddChild(_panel);
        Resized += Fit;

        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 10);
        _panel.AddChild(col);

        // ---- header
        var header = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        header.AddThemeConstantOverride("separation", 16);
        col.AddChild(header);
        _title = MakeLabel(Loc.T("YOU DIED"), 50, Defeat);
        _title.VerticalAlignment = VerticalAlignment.Center;
        header.AddChild(_title);
        var headRight = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        headRight.AddThemeConstantOverride("separation", 4);
        header.AddChild(headRight);
        _badges = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _badges.AddThemeConstantOverride("separation", 8);
        headRight.AddChild(_badges);
        _subtitle = MakeLabel("", 18, InkDim);
        _subtitle.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        headRight.AddChild(_subtitle);

        // ---- stat tiles
        _tiles = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _tiles.AddThemeConstantOverride("separation", 10);
        col.AddChild(_tiles);

        // ---- cards
        var cards = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ExpandFill };
        cards.AddThemeConstantOverride("separation", 12);
        col.AddChild(cards);

        _pearlCard = Card(cards, Loc.T(Pearls.Name).ToUpper(), PearlTint, 0.25f);

        VBoxContainer build = Card(cards, Loc.T("BUILD"), Gold, 0.42f);
        build.AddChild(Caption(Loc.T("WEAPONS")));
        _weaponGrid = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore };
        _weaponGrid.AddThemeConstantOverride("h_separation", 8);
        _weaponGrid.AddThemeConstantOverride("v_separation", 6);
        build.AddChild(_weaponGrid);
        build.AddChild(Caption(Loc.T("ITEMS")));
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        build.AddChild(scroll);
        _itemGrid = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _itemGrid.AddThemeConstantOverride("h_separation", 6);
        _itemGrid.AddThemeConstantOverride("v_separation", 6);
        scroll.AddChild(_itemGrid);
        _buildEmpty = MakeLabel(Loc.T("No items this run."), 16, InkDim);
        _buildEmpty.Visible = false;
        build.AddChild(_buildEmpty);

        VBoxContainer board = Card(cards, Loc.F("SCOREBOARD  [{0}]", Keybinds.Describe(Keybinds.Primary(ScoreboardPanel.Action))), new Color(0.62f, 0.98f, 1f), 0.33f);
        board.AddChild(new ScoreboardRows());

        // ---- buttons. No keyboard focus: Space is ui_accept, so a focused
        // button would be pressed by the Space the player was hammering to jump
        // when they died. Keys go through _Input (Enter / R, Esc).
        var buttons = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 24);
        col.AddChild(buttons);
        buttons.AddChild(BigButton(Loc.T("RESTART"), Loc.T("Enter  /  R"), Victory, Restart, 300f));
        buttons.AddChild(BigButton(Loc.T("MAIN MENU"), Loc.Ltr("Esc"), InkDim, ToMainMenu, 240f));

        _seed = MakeLabel("", 12, new Color(0.52f, 0.57f, 0.66f), HorizontalAlignment.Center);
        col.AddChild(_seed);
    }

    /// <summary>Centre the panel, as large as the screen allows up to a cap.</summary>
    private void Fit()
    {
        Vector2 size = Size;
        float w = Mathf.Min(size.X - Margin * 2f, MaxWidth);
        float h = Mathf.Min(size.Y - Margin * 2f, MaxHeight);
        _panel.AnchorLeft = 0.5f; _panel.AnchorRight = 0.5f; _panel.AnchorTop = 0.5f; _panel.AnchorBottom = 0.5f;
        _panel.OffsetLeft = -w * 0.5f; _panel.OffsetRight = w * 0.5f;
        _panel.OffsetTop = -h * 0.5f; _panel.OffsetBottom = h * 0.5f;
    }

    private VBoxContainer Card(Container parent, string title, Color accent, float stretch)
    {
        StyleBoxFlat style = Box(CardBack, accent, 16);
        style.SetBorderWidthAll(0);
        style.BorderWidthTop = 3;
        style.ContentMarginLeft = style.ContentMarginRight = 14;
        style.ContentMarginTop = 10;
        style.ContentMarginBottom = 10;
        var card = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = stretch };
        card.AddThemeStyleboxOverride("panel", style);
        parent.AddChild(card);
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 6);
        card.AddChild(box);
        box.AddChild(MakeLabel(title, 17, accent));
        return box;
    }

    private Control BigButton(string text, string hint, Color accent, System.Action pressed, float width)
    {
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 2);
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(width, 52f) };
        b.AddThemeFontSizeOverride("font_size", 24);
        b.AddThemeColorOverride("font_color", Ink);
        b.AddThemeColorOverride("font_hover_color", accent);
        b.AddThemeStyleboxOverride("normal", Box(new Color(0.15f, 0.18f, 0.28f), accent, 14, 2));
        b.AddThemeStyleboxOverride("hover", Box(new Color(0.20f, 0.24f, 0.36f), accent, 14, 3));
        b.AddThemeStyleboxOverride("pressed", Box(new Color(0.24f, 0.29f, 0.42f), accent, 14, 3));
        b.Pressed += pressed;
        box.AddChild(b);
        box.AddChild(MakeLabel(hint, 15, InkDim, HorizontalAlignment.Center));
        return box;
    }

    // ----------------------------------------------------------------- show

    /// <summary>
    /// Raise the screen for a finished run. Idempotent: a run can publish
    /// <see cref="RunEnded"/> from more than one place, and the second call
    /// must not rebuild the screen under a player already reaching for it.
    /// </summary>
    public void Show(RunState? run, Inventory? inventory, WeaponSystem? weapons)
    {
        if (IsShown) return;
        IsShown = true;

        bool survived = run?.Survived ?? false;
        Color accent = survived ? Victory : Defeat;

        // Pearls are paid here at the latest (idempotent; usually already paid
        // by the RunEnded handler), so the numbers below are what was credited.
        _payout = RunRewards.Settle();
        FinalSwarm? swarm = Main.Instance?.Stages?.Swarm;

        // A death in the Final Swarm is still a won run: the last boss fell.
        bool hostLeft = Net.NetSession.Instance?.MigrationFallback is { Length: > 0 };
        _title.Text = Loc.T(survived ? (swarm is not null ? "VICTORY" : "YOU SURVIVED") : hostLeft ? "RUN ENDED" : "YOU DIED");
        _title.AddThemeColorOverride("font_color", accent);
        _panelStyle.BorderColor = accent;

        if (swarm is not null)
            _subtitle.Text = Loc.F("Held the Final Swarm for {0}  ·  {1} kills  ·  enemy hp +{2}%",
                                   RunState.Format(swarm.Seconds), swarm.Kills, swarm.HealthBonusPercent);
        else if (run is not null && run.StageName.Length > 0)
            _subtitle.Text = Loc.F("Map {0} of {1}  ·  {2}", run.Phase, run.PhaseCount, Loc.T(run.StageName));
        _seed.Text = run is null ? "" : Loc.F("seed {0}", run.Seed);

        WriteBadges();
        WriteTiles(run, inventory);
        WritePearls();
        WriteBuild();

        Visible = true;
        _coopRun = Net.Coop.InRun;
        GetTree().Paused = true;
        Fit();

        // Release the camera's mouse capture so the buttons can be clicked; the
        // reloaded scene's camera captures it again on its own. _Process keeps
        // it free while the screen is up.
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _shownAtMsec = Time.GetTicksMsec();
    }

    public override void _Process(double delta)
    {
        // A panel that closes after the run ended (a level-up or shrine panel
        // left open under the results, the co-op world still running) restores
        // the mouse capture it found when it opened, which hid the cursor on
        // the results. Nothing may capture it while this screen is up.
        if (Visible && Input.MouseMode != Input.MouseModeEnum.Visible)
            Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void WriteBadges()
    {
        foreach (Node c in _badges.GetChildren()) c.QueueFree();
        DifficultyTier tier = DifficultyTiers.Active;
        _badges.AddChild(Badge(Loc.T(tier.Name).ToUpper(), tier.Reward > 1f ? Defeat : new Color(0.55f, 0.85f, 1f)));
        if (Net.Coop.InRun || _coopRun) _badges.AddChild(Badge(Loc.T("CO-OP"), new Color(0.62f, 0.98f, 1f)));
        if (_leaderboardRank > 0)
            _badges.AddChild(Badge(_leaderboardRank == 1 ? Loc.T("NEW BEST RUN") : Loc.F("LEADERBOARD #{0}", _leaderboardRank), Gold));
    }

    private void WriteTiles(RunState? run, Inventory? inventory)
    {
        foreach (Node c in _tiles.GetChildren()) c.QueueFree();
        int bosses = Main.Instance?.Stages?.BossesDefeated ?? 0;
        Tile("hourglass", new Color(0.62f, 0.86f, 1f), run?.Clock ?? "00:00", Loc.T("Time"));
        Tile("up", new Color(0.80f, 0.72f, 1f), $"{inventory?.Level ?? 1}", Loc.T("Level"));
        Tile("skull", new Color(1f, 0.62f, 0.56f), $"{run?.Kills ?? 0}", Loc.T("Kills"));
        Tile("crown", Gold, $"{bosses}", Loc.T("Bosses"));
        Tile("coin", new Color(1f, 0.78f, 0.30f), $"{RunScoreboard.LocalSelf().Gold}", Loc.T("Gold earned"));
        Tile("gem", PearlTint, _payout is { } p ? $"+{p.Total}" : "0", Loc.T(Pearls.Name));
    }

    private void Tile(string glyph, Color tint, string value, string caption)
    {
        var tile = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        StyleBoxFlat style = Box(tint with { A = 0.13f }, tint with { A = 0.55f }, 16, 2);
        style.ContentMarginLeft = style.ContentMarginRight = 10;
        style.ContentMarginTop = style.ContentMarginBottom = 6;
        tile.AddThemeStyleboxOverride("panel", style);
        _tiles.AddChild(tile);
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 10);
        tile.AddChild(row);
        IconView icon = IconView.Make(40f, false);
        icon.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        icon.Set(new IconSpec(glyph, tint), tint);
        row.AddChild(icon);
        var text = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("separation", -4);
        row.AddChild(text);
        text.AddChild(MakeLabel(Loc.Ltr(value), 30, Ink));
        text.AddChild(MakeLabel(caption, 14, InkDim));
    }

    private void WritePearls()
    {
        while (_pearlCard.GetChildCount() > 1) { Node c = _pearlCard.GetChild(1); _pearlCard.RemoveChild(c); c.QueueFree(); }
        if (_payout is not { } pay)
        {
            _pearlCard.AddChild(MakeLabel(Loc.T("No Pearls this run."), 16, InkDim));
            return;
        }
        foreach (RunRewards.Line line in pay.Lines)
            if (line.Amount > 0) _pearlCard.AddChild(Pair(Loc.T(line.Label), Loc.Ltr($"+{line.Amount}"), 16, Ink, PearlTint));
        if (pay.Tier.Reward > 1f)
            _pearlCard.AddChild(Pair(Loc.T(pay.Tier.Name), Loc.Ltr($"x{pay.Tier.Reward:0.##}"), 16, Ink, Defeat));
        _pearlCard.AddChild(new HSeparator { MouseFilter = MouseFilterEnum.Ignore });
        _pearlCard.AddChild(Pair(Loc.T("TOTAL"), Loc.Ltr($"+{pay.Total}"), 24, PearlTint, PearlTint));
        var spacer = new Control { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ExpandFill };
        _pearlCard.AddChild(spacer);
        var balance = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        StyleBoxFlat s = Box(new Color(0.08f, 0.10f, 0.16f), PearlTint with { A = 0.5f }, 12, 1);
        s.ContentMarginLeft = s.ContentMarginRight = 10;
        s.ContentMarginTop = s.ContentMarginBottom = 4;
        balance.AddThemeStyleboxOverride("panel", s);
        balance.AddChild(Pair(Loc.T("Balance"), Loc.Ltr($"{Pearls.Balance}"), 15, InkDim, Ink));
        _pearlCard.AddChild(balance);
    }

    private void WriteBuild()
    {
        foreach (Node c in _weaponGrid.GetChildren()) c.QueueFree();
        foreach (Node c in _itemGrid.GetChildren()) c.QueueFree();
        foreach (string id in _weaponOrder)
        {
            WeaponDef? def = Catalog.WeaponById(id);
            if (def is null) continue;
            int level = _weaponLevels[id];
            bool maxed = level >= def.MaxLevel;
            string name = Loc.T(def.DisplayName);
            _weaponGrid.AddChild(IconTile(Icons.For(def), Catalog.RarityColor(def.Rarity), 46f,
                maxed ? Loc.T("MAX") : Loc.F("Lv {0}", level), maxed ? Gold : Ink,
                maxed ? Loc.F("{0}  ·  MAX", name) : Loc.F("{0}  ·  Lv {1}", name, level)));
        }
        foreach (string id in _itemOrder)
        {
            ItemDef? def = Catalog.ItemById(id);
            if (def is null) continue;
            int stacks = _itemStacks[id];
            string name = Loc.T(def.DisplayName);
            _itemGrid.AddChild(IconTile(Icons.For(def), Catalog.RarityColor(def.Rarity), 38f,
                stacks > 1 ? Loc.Ltr($"x{stacks}") : "", Ink, stacks > 1 ? $"{name}  ×{stacks}" : name));
        }
        _buildEmpty.Visible = _itemOrder.Count == 0;
    }

    /// <summary>An icon on a rarity disc with a small caption under it; the
    /// full name on hover.</summary>
    private static Control IconTile(IconSpec spec, Color ring, float size, string caption, Color captionColor, string tooltip)
    {
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass, TooltipText = tooltip };
        box.AddThemeConstantOverride("separation", -2);
        IconView icon = IconView.Make(size);
        icon.Set(spec, ring);
        icon.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        box.AddChild(icon);
        if (caption.Length > 0) box.AddChild(MakeLabel(caption, 13, captionColor, HorizontalAlignment.Center));
        return box;
    }

    private static HBoxContainer Pair(string left, string right, int size, Color leftColor, Color rightColor)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        Label l = MakeLabel(left, size, leftColor);
        l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        row.AddChild(l);
        row.AddChild(MakeLabel(right, size, rightColor));
        return row;
    }

    private static Control Badge(string text, Color tint)
    {
        var p = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        StyleBoxFlat s = Box(tint with { A = 0.20f }, tint, 12, 2);
        s.ContentMarginLeft = s.ContentMarginRight = 12;
        s.ContentMarginTop = 2;
        s.ContentMarginBottom = 3;
        p.AddThemeStyleboxOverride("panel", s);
        p.AddChild(MakeLabel(text, 16, tint));
        return p;
    }

    private static Label Caption(string text) => MakeLabel(text, 14, InkDim);

    // ---------------------------------------------------------------- input

    public override void _Input(InputEvent @event)
    {
        if (!Visible || DevPanel.IsOpen) return;
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        if (key.Keycode == Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            ToMainMenu();
            return;
        }

        // Not Space: it is the jump key, and a death usually lands mid-jump.
        if (key.Keycode is not (Key.Enter or Key.KpEnter or Key.R)) return;

        GetViewport().SetInputAsHandled();
        Restart();
    }

    /// <summary>Input that arrives sooner than this after the screen opens was
    /// meant for the game, not for this screen, and is ignored.</summary>
    private const ulong ArmDelayMsec = 1000;

    private bool _coopRun;

    private ulong _shownAtMsec;

    /// <summary>
    /// True once a restart may be honoured. Never in an automated capture run:
    /// those windows open on top of whatever the user is typing into, take focus,
    /// and a stray Enter would silently replace the run being measured.
    /// </summary>
    private bool Armed => !Game.Screenshot.Requested
                          && Time.GetTicksMsec() - _shownAtMsec >= ArmDelayMsec;

    private void Restart()
    {
        if (!Armed) return;
        // A co-op run cannot be restarted alone: back to the title instead.
        if (_coopRun)
        {
            ToMainMenu();
            return;
        }

        // Unpause first: the paused flag lives on the SceneTree, so reloading
        // while paused hands the fresh run a frozen tree.
        GetTree().Paused = false;
        GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
    }

    /// <summary>Leave the run for the title screen. Same arming and unpause
    /// rules as <see cref="Restart"/>, for the same reasons.</summary>
    private void ToMainMenu()
    {
        if (!Armed) return;
        GetTree().Paused = false;
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, MainMenu.TitleScene);
    }

    // -------------------------------------------------------------- helpers

    private static StyleBoxFlat Box(Color back, Color border, int radius, int borderWidth = 0)
    {
        var s = new StyleBoxFlat { BgColor = back, BorderColor = border };
        s.SetBorderWidthAll(borderWidth);
        s.SetCornerRadiusAll(radius);
        return s;
    }

    private static Label MakeLabel(string text, int size, Color color,
                                   HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = align,
            MouseFilter = MouseFilterEnum.Ignore,
            AutoTranslateMode = AutoTranslateModeEnum.Disabled,
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", Outline);
        label.AddThemeConstantOverride("outline_size", 4);
        return label;
    }
}
