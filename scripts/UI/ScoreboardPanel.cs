using System.Collections.Generic;
using Godot;
using Wavebreak.Game;
using Wavebreak.Meta;
using Wavebreak.Net;

namespace Wavebreak.UI;

/// <summary>
/// The scoreboard: one row per player with kills, boss kills, downs, revives
/// given, damage dealt and taken, level and gold earned (Game/RunScoreboard).
///
/// Shown while the "scoreboard" action is held (Tab by default; Y is the
/// character sheet), and toggled by the SCOREBOARD button on the results
/// screen. On a layer above the results screen, and it runs while paused, so
/// it works on the pause menu and at the end of a run too. In co-op the
/// numbers are the host's merged table; in single-player it is just you.
/// </summary>
public sealed partial class ScoreboardPanel : Control
{
    public const string Action = "scoreboard";

    public static ScoreboardPanel? Instance { get; private set; }

    /// <summary>Held open by a test capture.</summary>
    public static bool ForceShow;

    private static readonly Color Back = new(0.06f, 0.07f, 0.11f, 0.94f);
    private static readonly Color Head = new(0.62f, 0.98f, 1.00f);
    private static readonly Color Ink = new(0.94f, 0.96f, 0.99f);
    private static readonly Color Dim = new(0.62f, 0.68f, 0.78f);
    private static readonly Color Outline = new(0.02f, 0.03f, 0.05f, 0.9f);

    private static readonly string[] Columns =
        { "Player", "Kills", "Bosses", "Downs", "Deaths", "Revives", "Damage", "DPS (10s)", "Healed", "Taken", "Level", "Gold" };

    private PanelContainer _panel = null!;
    private GridContainer _grid = null!;
    private Label _hint = null!;
    private bool _resultsOpen;
    private bool _holdingMouse;
    private ScoreMetric _sort;
    private readonly List<ScoreLine> _ordered = new();
    private float _refresh;

    public override void _Ready()
    {
        Name = "Scoreboard";
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Visible = false;
        // --show-scoreboard: hold it open for a capture (like the stats sheet's flag).
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--show-scoreboard") >= 0) ForceShow = true;

        var style = new StyleBoxFlat { BgColor = Back, BorderColor = Head, ContentMarginLeft = 26, ContentMarginRight = 26, ContentMarginTop = 16, ContentMarginBottom = 16 };
        style.SetBorderWidthAll(0);
        style.BorderWidthTop = 4;
        style.SetCornerRadiusAll(16);
        _panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        _panel.AddThemeStyleboxOverride("panel", style);
        _panel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        _panel.GrowHorizontal = GrowDirection.Both;
        _panel.GrowVertical = GrowDirection.Both;
        AddChild(_panel);

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 10);
        _panel.AddChild(box);
        box.AddChild(MakeLabel(Loc.T("SCOREBOARD"), 26, Head, HorizontalAlignment.Center));

        var sorting = new OptionButton { FocusMode = FocusModeEnum.None,
            TooltipText = Loc.T("Sort players by damage, DPS, kills, deaths or healing restored.") };
        foreach (string label in new[] { "Damage", "DPS (10s)", "Kills", "Deaths", "Healed" }) sorting.AddItem(Loc.T(label));
        sorting.ItemSelected += index => { _sort = (ScoreMetric)(int)index; Rebuild(); };
        box.AddChild(sorting);
        _grid = new GridContainer { Columns = Columns.Length, MouseFilter = MouseFilterEnum.Ignore };
        _grid.AddThemeConstantOverride("h_separation", 12);
        _grid.AddThemeConstantOverride("v_separation", 6);
        box.AddChild(_grid);

        _hint = MakeLabel("", 13, Dim, HorizontalAlignment.Center);
        box.AddChild(_hint);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        ForceShow = false;
        if (_holdingMouse) Actors.PlayerCamera.ReleaseCursor(this);
        _holdingMouse = false;
    }

    /// <summary>The results screen's SCOREBOARD button.</summary>
    public void ToggleResults() => _resultsOpen = !_resultsOpen;

    public override void _Process(double delta)
    {
        bool held = !DevPanel.IsOpen && InputMap.HasAction(Action) && Input.IsActionPressed(Action);
        bool want = ForceShow || _resultsOpen || held;
        if (want != Visible)
        {
            Visible = want;
            if (want && !_holdingMouse) { Actors.PlayerCamera.HoldCursor(this); _holdingMouse = true; }
            else if (!want && _holdingMouse) { Actors.PlayerCamera.ReleaseCursor(this); _holdingMouse = false; }
            _refresh = 0f;
        }
        if (!Visible) return;
        _refresh -= (float)delta;
        if (_refresh > 0f) return;
        // Damage moves every hit and does not bump the version; twice a
        // second is plenty for a table someone is glancing at.
        _refresh = 0.5f;
        RunScoreboard.RefreshLocal(); RunScoreboard.RefreshMeters();
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (Node c in _grid.GetChildren()) { _grid.RemoveChild(c); c.QueueFree(); }
        for (int i = 0; i < Columns.Length; i++)
            _grid.AddChild(MakeLabel(Loc.T(Columns[i]), 14, Dim, i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right));

        ScoreboardOrdering.Fill(RunScoreboard.All, _ordered, _sort);
        foreach (ScoreLine l in _ordered)
        {
            bool you = l.Id == Coop.LocalId;
            Color tint = Coop.Active ? Coop.SlotColor(l.Slot) : Ink;
            string name = you && Coop.Active ? Loc.F("{0} (you)", l.Name) : l.Name;
            Label playerName = MakeLabel(name, 17, tint);
            playerName.ClipText = true; playerName.CustomMinimumSize = new Vector2(160, 0);
            _grid.AddChild(playerName);
            Cell($"{l.Kills}", ScoreboardOrdering.Leads(l, ScoreMetric.Kills, _ordered));
            Cell($"{l.BossKills}");
            Cell($"{l.Downs}");
            Cell($"{l.Deaths}", ScoreboardOrdering.Leads(l, ScoreMetric.Deaths, _ordered));
            Cell($"{l.Revives}");
            Cell(RunScoreboard.Short(l.DamageDealt), ScoreboardOrdering.Leads(l, ScoreMetric.Damage, _ordered));
            Cell(RunScoreboard.Short(l.Dps), ScoreboardOrdering.Leads(l, ScoreMetric.Dps, _ordered));
            Cell(RunScoreboard.Short(l.HealingRestored), ScoreboardOrdering.Leads(l, ScoreMetric.Healing, _ordered));
            Cell(RunScoreboard.Short(l.DamageTaken));
            Cell($"{l.Level}");
            Cell($"{l.GoldEarned}");
        }
        string key = Keybinds.Describe(Keybinds.Primary(Action));
        _hint.Text = Coop.Active ? Loc.F("hold {0}  ·  the host keeps the score", key) : Loc.F("hold {0}", key);
    }

    private void Cell(string text, bool leader = false) => _grid.AddChild(MakeLabel(Loc.Ltr(text), 17, leader ? Colors.Gold : Ink, HorizontalAlignment.Right));

    internal static Label MakeLabel(string text, int size, Color color, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = new Label { Text = text, HorizontalAlignment = align, MouseFilter = MouseFilterEnum.Ignore, AutoTranslateMode = AutoTranslateModeEnum.Disabled };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeColorOverride("font_outline_color", Outline);
        l.AddThemeConstantOverride("outline_size", 4);
        return l;
    }
}
