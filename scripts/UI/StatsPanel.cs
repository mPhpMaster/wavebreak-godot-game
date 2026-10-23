using System;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Audio;
using Wavebreak.Combat;
using Wavebreak.Data;
using Wavebreak.Meta;
using Wavebreak.Game;
using Wavebreak.Progression;
using Wavebreak.Stats;

namespace Wavebreak.UI;

/// <summary>
/// Hold Tab: the whole character sheet in one look — run state, every stat with
/// how far it has moved from base, the loadout, and the sound settings and
/// controls.
///
/// Hold rather than toggle, because the question it answers ("what is my crit
/// chance right now?") is a glance, and a toggle that is left open turns into a
/// panel blocking the arena in the middle of a wave. It does not pause either,
/// for the same reason, and it ignores the mouse so a level-up card under it
/// can still be clicked.
///
/// Every stat is shown with its unit and in the form the player reasons in:
/// armour as the damage reduction it actually gives, the Damage bonus as a
/// multiplier, crit as a percentage. A raw "Armor 18" or "Damage 0.35" is
/// accurate and useless.
/// </summary>
public sealed partial class StatsPanel : Control
{
    private static readonly Color Ink = new(0.93f, 0.95f, 0.98f);
    private static readonly Color Dim = new(0.60f, 0.65f, 0.75f);
    private static readonly Color Heading = new(0.62f, 0.98f, 1.00f);
    private static readonly Color Better = new(0.45f, 0.92f, 0.52f);
    private static readonly Color Worse = new(1.00f, 0.45f, 0.42f);
    private static readonly Color Outline = new(0.02f, 0.03f, 0.05f, 0.9f);

    /// <summary>How often the open sheet re-reads the numbers. Stats change on
    /// level-ups and pickups, not every frame; four times a second is plenty and
    /// keeps a busy wave from rebuilding text sixty times a second.</summary>
    private const float RefreshInterval = 0.25f;

    internal static readonly (string Title, Stat[] Stats)[] Groups =
    {
        ("SURVIVAL", new[] { Stat.MaxHealth, Stat.HealthRegen, Stat.Armor, Stat.Evasion,
                             Stat.Shield, Stat.Thorns, Stat.Lifesteal }),
        ("OFFENCE",  new[] { Stat.Damage, Stat.AttackSpeed, Stat.CritChance, Stat.CritDamage,
                             Stat.AreaSize, Stat.ProjectileCount, Stat.ProjectileSpeed, Stat.Knockback }),
        ("MOBILITY", new[] { Stat.MoveSpeed, Stat.JumpHeight, Stat.DashCooldown }),
        ("UTILITY",  new[] { Stat.PickupRange, Stat.XpGain, Stat.GoldGain, Stat.Luck, Stat.EnemyScaling }),
        ("ELEMENTS", new[] { Stat.ElementalDamage, Stat.StatusChance, Stat.StatusDuration, Stat.MegacritChance }),
    };

    private readonly Player _player;
    private readonly Inventory _inventory;
    private readonly WeaponSystem _weapons;
    private readonly RunState _run;

    private VBoxContainer _character = null!;
    private VBoxContainer _stats = null!;
    private VBoxContainer _loadout = null!;
    private VBoxContainer _settings = null!;
    private float _refresh;
    private bool _available;
    private Label _title = null!;
    private Label _hint = null!;

    /// <summary>Buttons the pause menu puts under the sheet. Empty and hidden
    /// for the plain hold-Tab view.</summary>
    public HBoxContainer ActionRow { get; private set; } = null!;

    /// <summary>
    /// Held open by the pause menu: the sheet stays up without Tab, is titled
    /// PAUSED, and shows the menu's buttons. One view for both rather than a
    /// second copy of every stat row that would drift out of step with this one.
    /// </summary>
    public bool Pinned
    {
        get => _pinned;
        set
        {
            _pinned = value;
            if (_title is null) return;
            _title.Text = Loc.T(value ? "PAUSED" : "CHARACTER SHEET");
            _hint.Text = Loc.T(value ? "Esc / P  resume" : "hold TAB");
            ActionRow.Visible = value;
            _refresh = 0f;
        }
    }
    private bool _pinned;

    public StatsPanel(Player player, Inventory inventory, WeaponSystem weapons, RunState run)
    {
        _player = player;
        _inventory = inventory;
        _weapons = weapons;
        _run = run;
    }

    public override void _Ready()
    {
        Name = "StatsPanel";
        // Readable while the level-up panel has the tree paused — that is
        // exactly when you want to check your build before choosing.
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Ignore;
        // AndOffsets, not just anchors: under a CanvasLayer the control starts
        // with a zero-sized rect, and anchors alone leave the offsets holding
        // it there -- the sheet then lays out in a corner, half off-screen.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Visible = false;

        _available = InputMap.HasAction("stats");

        var dim = new ColorRect
        {
            Color = new Color(0.02f, 0.03f, 0.06f, 0.72f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // A CenterContainer re-centres the frame whenever its content resizes,
        // which a manually centred panel does not.
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(centre);
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var frame = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.08f, 0.12f, 0.94f),
            BorderColor = new Color(0.30f, 0.55f, 0.70f, 0.8f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
            ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 14, ContentMarginBottom = 12,
        });
        centre.AddChild(frame);

        var outer = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        outer.AddThemeConstantOverride("separation", 8);
        frame.AddChild(outer);

        _title = Text(Loc.T("CHARACTER SHEET"), 22, Heading, HorizontalAlignment.Center);
        outer.AddChild(_title);

        var columns = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        columns.AddThemeConstantOverride("separation", 32);
        outer.AddChild(columns);

        _character = Column(columns, 210f);
        _stats = Column(columns, 300f);
        _loadout = Column(columns, 280f);
        _settings = Column(columns, 210f);

        ActionRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, Visible = false };
        ActionRow.AddThemeConstantOverride("separation", 14);
        outer.AddChild(ActionRow);

        _hint = Text(Loc.T("hold TAB"), 13, Dim, HorizontalAlignment.Center);
        outer.AddChild(_hint);
    }

    public override void _Process(double delta)
    {
        bool want = _pinned || Screenshot.ShowStats || (_available && Input.IsActionPressed("stats"));
        if (want != Visible)
        {
            Visible = want;
            if (want) _refresh = 0f;
        }
        if (!Visible) return;

        _refresh -= (float)delta;
        if (_refresh > 0f) return;
        _refresh = RefreshInterval;
        Rebuild();
    }

    // --------------------------------------------------------------- content

    private void Rebuild()
    {
        if (!IsInstanceValid(_player)) return;
        StatSheet stats = _player.Stats;

        Clear(_character);
        Section(_character, "RUN");
        Row(_character, "Time", _run.Clock);
        Row(_character, "Level", _inventory.Level.ToString());
        Row(_character, "XP", $"{_inventory.XpIntoLevel} / {_inventory.XpForNextLevel}");
        Row(_character, "Kills", _run.Kills.ToString());
        Row(_character, "Gold", _inventory.Gold.ToString());

        Section(_character, "HEALTH");
        PlayerHealth health = _player.Health;
        Row(_character, "Health", $"{health.Current:0} / {health.Max:0}");
        if (health.ShieldMax > 0f) Row(_character, "Shield", $"{health.Shield:0} / {health.ShieldMax:0}");
        Row(_character, "Regen", Loc.F("{0:0.##} /s", stats.RegenPerSecond()));
        Row(_character, "Damage taken", $"-{stats.ArmorReduction() * 100f:0}%");
        Row(_character, "Dodge", $"{stats.EvasionChance() * 100f:0}%");

        Section(_character, "MOVEMENT");
        float dash = _player.DashCooldownRemaining;
        Row(_character, "Dash", dash <= 0f ? Loc.T("ready") : Loc.F("{0:0.0}s", dash), dash <= 0f ? Better : Ink);
        Row(_character, "Speed now", Loc.F("{0:0.0} m/s", _player.PlanarVelocity.Length()));
        Row(_character, "Top speed", Loc.F("{0:0.0} m/s", _player.PeakSpeed));

        Clear(_stats);
        foreach ((string title, Stat[] group) in Groups)
        {
            Section(_stats, title);
            foreach (Stat stat in group) StatRow(stats, stat);
        }

        Clear(_loadout);
        Section(_loadout, Loc.F("WEAPONS  {0}/{1}", _weapons.Weapons.Count, _weapons.Slots));
        foreach (WeaponRuntime w in _weapons.Weapons)
        {
            Color c = Catalog.RarityColor(w.Def.Rarity);
            float dps = w.Interval > 0f ? w.VolleyDamage(stats) / w.Interval : 0f;
            Row(_loadout, w.Def.DisplayName, Loc.F("Lv {0}/{1}  ~{2:0} dps", w.Level, w.Def.MaxLevel, dps), c, Icons.For(w.Def));
        }

        TomeSection();

        Section(_loadout, Loc.F("ITEMS  {0}", _inventory.TotalStacks));
        if (_inventory.Items.Count == 0) _loadout.AddChild(Text(Loc.T("none yet"), 14, Dim));
        // Two per row: a late build carries thirty-odd distinct items, which in a
        // single column would run off the bottom of a 720p screen.
        var grid = new GridContainer { Columns = 2, MouseFilter = MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 0);
        _loadout.AddChild(grid);
        foreach (ItemStack item in _inventory.Items)
        {
            string label = item.Count > 1 ? $"{Loc.T(item.DisplayName)} x{item.Count}" : Loc.T(item.DisplayName);
            Label l = Text(label, 13, Catalog.RarityColor(item.Rarity));
            l.CustomMinimumSize = new Vector2(112f, 0f);
            l.ClipText = true;
            grid.AddChild(IconView.Row(Icons.For(item.Def), l, 18f));
        }

        Clear(_settings);
        Section(_settings, "SOUND");
        _settings.AddChild(Text(Loc.T(AudioSettings.Describe(VolumeChannel.Master)), 13, AudioSettings.Muted ? Worse : Ink));
        _settings.AddChild(Text(Loc.T(AudioSettings.Describe(VolumeChannel.Sfx)), 13, Ink));
        _settings.AddChild(Text(Loc.T(AudioSettings.Describe(VolumeChannel.Music)), 13, Ink));

        Section(_settings, "CONTROLS");
        // Read from the live bindings, so a rebound key shows its new name here.
        Row(_settings, "Mouse", Loc.T("look"), Dim);
        foreach ((string action, string label) in ControlRows)
        {
            if (!InputMap.HasAction(action)) continue;
            string a = Keybinds.Describe(Keybinds.Get(action, 0));
            InputEvent? second = Keybinds.Get(action, 1);
            Row(_settings, second is null ? a : $"{a}, {Keybinds.Describe(second)}", Loc.T(label), Dim);
        }
        Row(_settings, "Esc", Loc.T("pause menu"), Dim);

        Section(_settings, "RUN");
        Row(_settings, "Seed", _run.Seed.ToString(), Dim);
    }

    private static readonly (string Action, string Label)[] ControlRows =
    {
        ("move_forward", "forward"),
        ("move_back", "back"),
        ("move_left", "left"),
        ("move_right", "right"),
        ("jump", "jump / double"),
        ("dash", "dash"),
        ("slide", "slide / slam"),
        ("interact", "interact"),
        ("stats", "this sheet"),
        ("pause", "pause"),
        ("mute", "mute"),
    };

    /// <summary>One stat: name, value in its natural unit, and the change from
    /// base coloured by whether it helps. Dash cooldown and enemy strength are
    /// the two where a lower number is the good direction.</summary>
    private void StatRow(StatSheet stats, Stat stat)
    {
        float value = stats.Get(stat);
        float baseValue = StatSheet.BaseValue(stat);
        float diff = value - baseValue;

        Color colour = Ink;
        if (Mathf.Abs(diff) > 1e-4f)
        {
            bool lowerIsBetter = stat is Stat.DashCooldown or Stat.EnemyScaling;
            colour = (diff > 0f) != lowerIsBetter ? Better : Worse;
        }

        Row(_stats, Catalog.StatName(stat), Format(stats, stat, value), colour);
    }

    internal static string Format(StatSheet stats, Stat stat, float v) => stat switch
    {
        Stat.MaxHealth       => $"{v:0}",
        Stat.HealthRegen     => Loc.F("{0:0.#} /min", v),
        Stat.Armor           => Loc.F("{0:0}  (-{1:0}% dmg)", v, stats.ArmorReduction() * 100f),
        Stat.Evasion         => Loc.F("{0:0}  ({1:0}% dodge)", v, stats.EvasionChance() * 100f),
        Stat.Shield          => $"{v:0}",
        Stat.Thorns          => $"{v:0.#}",
        Stat.Lifesteal       => $"{v * 100f:0.#}%",
        Stat.Damage          => $"x{1f + v:0.00}",
        Stat.AttackSpeed     => $"x{v:0.00}",
        Stat.CritChance      => $"{v * 100f:0.#}%",
        Stat.CritDamage      => $"+{v * 100f:0}%",
        Stat.ProjectileCount => $"+{v:0}",
        Stat.MoveSpeed       => Loc.F("{0:0.0} m/s", v),
        Stat.JumpHeight      => Loc.F("{0:0.0} m", v),
        Stat.DashCooldown    => Loc.F("{0:0.00} s", v),
        Stat.PickupRange     => Loc.F("{0:0.0} m", v),
        Stat.XpGain or Stat.GoldGain or Stat.Luck => $"{(v >= 0f ? "+" : "")}{v * 100f:0}%",
        Stat.ElementalDamage => $"x{1f + v:0.00}",
        Stat.StatusChance or Stat.MegacritChance or Stat.IgniteChance or Stat.ChillChance
            or Stat.ShockChance or Stat.PoisonChance => $"+{v * 100f:0.#}%",
        _                    => $"x{v:0.00}",
    };

    // --------------------------------------------------------------- helpers

    private static VBoxContainer Column(Container parent, float width)
    {
        var column = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(width, 0f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        column.AddThemeConstantOverride("separation", 0);
        parent.AddChild(column);
        return column;
    }

    private static void Clear(Node column)
    {
        foreach (Node child in column.GetChildren())
        {
            column.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static void Section(VBoxContainer column, string title)
    {
        if (column.GetChildCount() > 0) column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        column.AddChild(Text(Loc.T(title), 15, Heading));
    }

    private static void Row(VBoxContainer column, string name, string value, Color? valueColour = null,
                            IconSpec? icon = null)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        if (icon is { } spec)
        {
            IconView view = IconView.Make(18f, disc: false);
            view.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            view.Set(spec);
            row.AddChild(view);
        }
        Label left = Text(Loc.T(name), 14, valueColour == Dim ? Dim : Ink);
        left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        left.ClipText = true;
        row.AddChild(left);
        row.AddChild(Text(value, 14, valueColour ?? Ink, HorizontalAlignment.Right));
        column.AddChild(row);
    }

    private static Label Text(string text, int size, Color color,
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
        label.AddThemeConstantOverride("outline_size", 4);
        return label;
    }
}
