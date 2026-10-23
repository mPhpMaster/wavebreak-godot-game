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
/// The numbers come from two places on purpose. The totals that the rest of the
/// game already tracks (level, gold, item stacks, weapon count) are read from the
/// inventory and weapon system, because those are authoritative and a second
/// count that disagrees with the HUD is worse than no count. The *itemised* lists
/// are accumulated from the bus as the run happens, because "which items did I
/// end up with" is a question the UI can answer for itself without asking the
/// inventory to expose its internals to a screen that only wants to print them.
///
/// Restart reloads the scene rather than resetting state in place. Every system
/// here is built to be constructed once per run, and a reload is the only way to
/// be certain nothing survived that should not have — which matters most for the
/// bug this whole codebase is arranged to avoid: a handler from a dead run still
/// subscribed to the bus.
/// </summary>
public sealed partial class ResultsScreen : Control
{
    private const float PanelWidth = 940f;
    private const float PanelHeight = 560f;

    private static readonly Color Dim = new(0.01f, 0.02f, 0.03f, 0.86f);
    private static readonly Color PanelBack = new(0.08f, 0.09f, 0.13f, 0.98f);
    private static readonly Color Ink = new(0.94f, 0.96f, 0.99f);
    private static readonly Color InkDim = new(0.62f, 0.68f, 0.78f);
    private static readonly Color Outline = new(0.02f, 0.03f, 0.05f, 0.9f);
    private static readonly Color Victory = new(0.42f, 0.92f, 0.55f);
    private static readonly Color Defeat = new(1.00f, 0.36f, 0.34f);

    // ------------------------------------------------------------ bus state
    // Accumulated for the whole run, not gathered at the end: an item's id is
    // published once when it is taken, and nothing keeps a list of them for us.

    private readonly Dictionary<string, int> _itemStacks = new();
    private readonly List<string> _itemOrder = new();
    private readonly Dictionary<string, int> _weaponLevels = new();
    private readonly List<string> _weaponOrder = new();

    // ------------------------------------------------------------- widgets

    private ColorRect _accent = null!;
    private Label _title = null!;
    private Label _subtitle = null!;
    private Label _statNames = null!;
    private Label _statValues = null!;
    private VBoxContainer _loadout = null!;
    private Button _restart = null!;

    private bool _subscribed;
    private Meta.RunRewards.Payout? _payout;
    private static readonly Color PearlTint = new(0.80f, 0.90f, 1.00f);

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
        _subscribed = true;
    }

    public override void _ExitTree()
    {
        if (!_subscribed) return;
        _subscribed = false;
        EventBus.Unsubscribe<ItemAcquired>(OnItemAcquired);
        EventBus.Unsubscribe<WeaponAcquired>(OnWeaponAcquired);
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

    // ---------------------------------------------------------------- build

    private void Build()
    {
        var dim = new ColorRect { Color = Dim };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        dim.MouseFilter = MouseFilterEnum.Stop;
        AddChild(dim);

        var panel = new ColorRect { Color = PanelBack, MouseFilter = MouseFilterEnum.Ignore };
        Centre(panel, PanelWidth, PanelHeight);
        AddChild(panel);

        _accent = new ColorRect { Color = Defeat, MouseFilter = MouseFilterEnum.Ignore };
        _accent.AnchorLeft = 0f; _accent.AnchorRight = 1f;
        _accent.AnchorTop = 0f; _accent.AnchorBottom = 0f;
        _accent.OffsetTop = 0f; _accent.OffsetBottom = 6f;
        panel.AddChild(_accent);

        _title = MakeLabel(Loc.T("RUN OVER"), 52, Defeat, HorizontalAlignment.Center);
        Inset(_title, 0f, 34f, PanelWidth, 62f);
        panel.AddChild(_title);

        _subtitle = MakeLabel("", 16, InkDim, HorizontalAlignment.Center);
        Inset(_subtitle, 0f, 98f, PanelWidth, 24f);
        panel.AddChild(_subtitle);

        // Left half: the scoreboard. Two multi-line labels rather than one, or a
        // grid of a dozen: the names are left-aligned and the values right-aligned
        // in a column beside them, which is the only way the numbers line up under
        // a proportional font. Padding one string with spaces does not.
        Label statsHeading = MakeLabel(Loc.T("RUN"), 15, InkDim);
        Inset(statsHeading, 56f, 148f, 300f, 20f);
        panel.AddChild(statsHeading);

        _statNames = MakeLabel("", 22, InkDim);
        // Right-to-left swaps the two columns' alignment, so the names' box
        // moves to the right end of the values' box to keep them apart.
        Inset(_statNames, Loc.IsRtl ? 236f : 56f, 176f, 200f, 300f);
        panel.AddChild(_statNames);

        _statValues = MakeLabel("", 22, Ink, HorizontalAlignment.Right);
        Inset(_statValues, 56f, 176f, 380f, 300f);
        panel.AddChild(_statValues);

        // Right half: what the run was actually built out of. Scrolled, because a
        // long run genuinely does end with twenty-plus stacks and a truncated
        // list is the one thing a results screen must not do.
        Label loadoutHeading = MakeLabel(Loc.T("BUILD"), 15, InkDim);
        Inset(loadoutHeading, PanelWidth - 440f, 148f, 300f, 20f);
        panel.AddChild(loadoutHeading);

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        Inset(scroll, PanelWidth - 440f, 176f, 384f, 300f);
        panel.AddChild(scroll);

        _loadout = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _loadout.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_loadout);

        // No keyboard focus. Space is ui_accept, so a focused button is pressed by
        // the same Space the player was hammering to jump when they died -- the
        // run restarted before the results could even be read. Keyboard restart
        // goes through _Input instead (Enter / R), the mouse still clicks it.
        _restart = new Button { Text = Loc.T("RESTART"), FocusMode = FocusModeEnum.None };
        _restart.AddThemeFontSizeOverride("font_size", 22);
        Inset(_restart, PanelWidth * 0.5f - 130f, PanelHeight - 74f, 260f, 46f);
        panel.AddChild(_restart);
        _restart.Pressed += Restart;

        // Back to the title (Meta: profile/unlocks). Same no-focus rule as Restart.
        var menu = new Button { Text = Loc.T("MAIN MENU"), FocusMode = FocusModeEnum.None };
        menu.AddThemeFontSizeOverride("font_size", 18);
        Inset(menu, PanelWidth * 0.5f + 150f, PanelHeight - 70f, 180f, 38f);
        panel.AddChild(menu);
        menu.Pressed += ToMainMenu;

        Label hint = MakeLabel(Loc.T("enter  ·  R   restart        esc   main menu"), 14, InkDim, HorizontalAlignment.Center);
        Inset(hint, 0f, PanelHeight - 24f, PanelWidth, 20f);
        panel.AddChild(hint);
    }

    // ----------------------------------------------------------------- show

    /// <summary>
    /// Raise the screen for a finished run.
    ///
    /// Idempotent: a run can publish <see cref="RunEnded"/> from more than one
    /// place — the player dying and the clock running out in the same frame is
    /// not exotic — and the second call must not rebuild the list or steal focus
    /// from a player already reaching for the button.
    /// </summary>
    /// <param name="run">The finished run. Supplies the clock and the seed.</param>
    /// <param name="inventory">Authoritative level, experience, gold and stack
    /// count.</param>
    /// <param name="weapons">Authoritative weapon count.</param>
    public void Show(RunState? run, Inventory? inventory, WeaponSystem? weapons)
    {
        if (IsShown) return;
        IsShown = true;

        bool survived = run?.Survived ?? false;
        Color accent = survived ? Victory : Defeat;

        // Pearls are paid here at the latest (idempotent; usually already paid
        // by the RunEnded handler), so the numbers below are what was credited.
        _payout = Meta.RunRewards.Settle();
        FinalSwarm? swarm = Main.Instance?.Stages?.Swarm;

        // A death in the Final Swarm is still a won run: the last boss fell.
        _title.Text = Loc.T(survived ? (swarm is not null ? "VICTORY" : "YOU SURVIVED") : "YOU DIED");
        _title.AddThemeColorOverride("font_color", accent);
        _accent.Color = accent;

        _subtitle.Text = run is null ? "" : Loc.F("seed {0}", run.Seed);
        if (swarm is not null)
            _subtitle.Text = Loc.F("Run won  ·  then held the Final Swarm for {0} ({1} kills, enemy hp +{2}%)",
                                   RunState.Format(swarm.Seconds), swarm.Kills, swarm.HealthBonusPercent) + "  ·  " + _subtitle.Text;
        if (Meta.DifficultyTiers.Active.Reward > 1f)
            _subtitle.Text = $"{Loc.T(Meta.DifficultyTiers.Active.Name)}  ·  " + _subtitle.Text;

        WriteStats(run, inventory, weapons);
        BuildLoadout();

        Visible = true;
        GetTree().Paused = true;

        // Release the camera's mouse capture so Restart can be clicked. The
        // reloaded scene's camera captures it again on its own.
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _shownAtMsec = Time.GetTicksMsec();
    }

    private void WriteStats(RunState? run, Inventory? inventory, WeaponSystem? weapons)
    {
        string clock = run?.Clock ?? "00:00";
        int kills = run?.Kills ?? 0;
        int elites = run?.Elites ?? 0;

        int level = inventory?.Level ?? 1;
        int xp = inventory?.Xp ?? 0;
        int gold = inventory?.Gold ?? 0;
        int stacks = inventory?.TotalStacks ?? 0;
        int weaponCount = weapons?.Weapons.Count ?? 0;

        _statNames.Text = Loc.T("Time\nLevel\nKills\nElites\nGold\nWeapons\nItems\nExperience");
        _statValues.Text = $"{clock}\n{level}\n{kills}\n{elites}\n{gold}\n{weaponCount}\n{stacks}\n{xp}";
        if (_payout is { } pay)
        {
            _statNames.Text += $"\n{Loc.T(Meta.Pearls.Name)}";
            _statValues.Text += $"\n+{pay.Total}";
        }
    }

    /// <summary>
    /// Fills the build list: weapons with their levels, then items with their
    /// stack counts, each in its own rarity colour.
    ///
    /// Rebuilt from scratch rather than updated in place — it is written exactly
    /// once per run, at the moment everything else has already stopped, so the
    /// allocation is free and the code is a third the size.
    /// </summary>
    private void BuildLoadout()
    {
        foreach (Node child in _loadout.GetChildren()) child.QueueFree();

        // Where this run's Pearls came from (Meta.RunRewards), above the build.
        if (_payout is { } pay)
        {
            _loadout.AddChild(Heading(Loc.T(pay.Headline).ToUpperInvariant()));
            foreach (Meta.RunRewards.Line line in pay.Lines)
                if (line.Amount > 0) _loadout.AddChild(Row($"{Loc.T(line.Label)}  ·  +{line.Amount}", PearlTint));
            _loadout.AddChild(Row(Loc.F("Balance  ·  {0}", Meta.Pearls.Balance), InkDim));
        }

        if (_weaponOrder.Count > 0)
        {
            _loadout.AddChild(Heading(Loc.T("WEAPONS")));
            foreach (string id in _weaponOrder)
            {
                WeaponDef? def = Catalog.WeaponById(id);
                int level = _weaponLevels[id];
                bool maxed = def is not null && level >= def.MaxLevel;
                string name = Loc.T(def?.DisplayName ?? id);

                _loadout.AddChild(Row(
                    maxed ? Loc.F("{0}  ·  MAX", name) : Loc.F("{0}  ·  Lv {1}", name, level),
                    def is null ? Ink : Catalog.RarityColor(def.Rarity)));
            }
        }

        if (_itemOrder.Count == 0) return;

        _loadout.AddChild(Heading(Loc.T("ITEMS")));
        foreach (string id in _itemOrder)
        {
            ItemDef? def = Catalog.ItemById(id);
            int stacks = _itemStacks[id];
            string name = Loc.T(def?.DisplayName ?? id);

            _loadout.AddChild(Row(
                stacks > 1 ? $"{name}  ×{stacks}" : name,

                def is null ? Ink : Catalog.RarityColor(def.Rarity)));
        }
    }

    private static Label Heading(string text)
    {
        Label label = MakeLabel(text, 14, InkDim);
        label.CustomMinimumSize = new Vector2(0f, 26f);
        label.VerticalAlignment = VerticalAlignment.Bottom;
        return label;
    }

    private static Label Row(string text, Color color)
    {
        Label label = MakeLabel(text, 17, color);
        label.CustomMinimumSize = new Vector2(0f, 21f);
        return label;
    }

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

        // Unpause first. The paused flag lives on the SceneTree, not on the
        // scene, so reloading while paused hands the fresh run a frozen tree and
        // the game never starts.
        GetTree().Paused = false;

        // Deferred: this runs from inside input handling or a button signal, and
        // freeing the scene that owns the node currently emitting is exactly the
        // situation deferred calls exist for.
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

    private static Label MakeLabel(string text, int size, Color color,
                                   HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = align,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", Outline);
        label.AddThemeConstantOverride("outline_size", 5);
        return label;
    }

    private static void Centre(Control control, float width, float height)
    {
        control.AnchorLeft = 0.5f; control.AnchorRight = 0.5f;
        control.AnchorTop = 0.5f; control.AnchorBottom = 0.5f;
        control.OffsetLeft = -width * 0.5f;
        control.OffsetRight = width * 0.5f;
        control.OffsetTop = -height * 0.5f;
        control.OffsetBottom = height * 0.5f;
    }

    private static void Inset(Control control, float left, float top, float width, float height)
    {
        control.AnchorLeft = 0f; control.AnchorRight = 0f;
        control.AnchorTop = 0f; control.AnchorBottom = 0f;
        control.OffsetLeft = left;
        control.OffsetRight = left + width;
        control.OffsetTop = top;
        control.OffsetBottom = top + height;
    }
}
