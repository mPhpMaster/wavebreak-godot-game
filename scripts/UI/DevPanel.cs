using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The developer panel: Ctrl+Shift+Alt+F12, anywhere (title or run), and no
/// other way in. Six tabs: Profile (unlocks, Pearls, shop, tiers), Player,
/// Build, Spawn, Stage and Info.
///
/// It lives under the scene tree's root rather than in a scene, so it survives
/// the title -> run -> title scene changes; each scene calls
/// <see cref="Install"/> from its _Ready, which also tells it the scene changed.
/// It keeps itself the root's last child, which makes its _Input the first to
/// see a key, so the chord works even over a panel that swallows keys.
///
/// While open it owns the keyboard: the other panels' _Input hooks return
/// early on <see cref="IsOpen"/>, so a number typed in a search box never picks
/// a level-up card. The mouse is freed and given back on close, the same way
/// the level-up panel does it. "Pause game while open" (on by default) pauses
/// the run behind it and re-asserts that pause each frame, because the
/// level-up panel unpauses the tree when it closes.
///
/// Any run-side action sets <see cref="UsedThisRun"/>: such a run earns no
/// quests, lifetime stats, leaderboard row or tier/challenge wins.
/// Profile-side actions are applied directly and saved (never for an
/// automated run, which has no profile path).
/// </summary>
public sealed partial class DevPanel : CanvasLayer
{
    public static DevPanel? Instance { get; private set; }

    /// <summary>True while the panel is on screen. Other panels read it to
    /// stay out of the way.</summary>
    public static bool IsOpen => Instance is { _open: true } p && IsInstanceValid(p);

    /// <summary>Set by any run-side action, and at run start when a lasting
    /// cheat (god mode, fly, speed, frozen AI) is still on.</summary>
    public static bool UsedThisRun { get; private set; }

    // Lasting toggles, kept across runs and re-applied to each new player.
    private static bool _pauseWhileOpen = true;
    private static bool _god;
    private static bool _fly;
    private static float _speedBonus;
    private static bool _autoPick;

    /// <summary>Source tag for every stat modifier the panel adds.</summary>
    public static readonly object Source = new DevSource();
    private sealed class DevSource { public override string ToString() => "Developer panel"; }

    private bool _open;
    private bool _pausedByUs;
    private bool _mouseWasCaptured;
    private Main? _builtKey;
    private bool _built;
    private int _buildingTab = -1;
    private Control? _root;
    private TabContainer _tabs = null!;
    private Label _status = null!;
    private Label _context = null!;
    private int _tab;
    private double _refresh;
    private bool _titleReloadPending;
    private double _titleReloadIn;

    /// <summary>Every button by a stable id, so the self-check presses the
    /// real controls rather than calling around them.</summary>
    private readonly Dictionary<string, BaseButton> _buttons = new();
    private readonly List<(int Tab, Action Refresh)> _refreshers = new();

    public string LastStatus { get; private set; } = "";
    public bool LastOk { get; private set; } = true;

    // ------------------------------------------------------------- install

    /// <summary>Called by each scene's _Ready (the title and the run).</summary>
    public static void Install(Node scene)
    {
        Engine.TimeScale = 1.0;
        if (scene is Main main) OnRunStarted(main);

        if (Instance is { } existing && IsInstanceValid(existing))
        {
            existing.OnSceneChanged(scene);
            return;
        }
        var panel = new DevPanel();
        Instance = panel;
        scene.GetTree().Root.CallDeferred(Node.MethodName.AddChild, panel);
    }

    private static void OnRunStarted(Main main)
    {
        UsedThisRun = _god || _fly || _speedBonus != 0f || EnemyManager.DevFrozen;
        if (_god) Screenshot.Invulnerable = true;
        ApplyFly(main.Player);
        ApplySpeed(main.Player);
        if (_autoPick) main.LevelUp.AutoPickSeconds = 0.05f;
        if (UsedThisRun) GD.Print("DEV run starts with developer cheats on; it will not count");
    }

    public override void _Ready()
    {
        Name = "DevPanel";
        Layer = 120;
        ProcessMode = ProcessModeEnum.Always;
        Instance = this;
        StartChecks();
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    private void OnSceneChanged(Node scene)
    {
        if (scene is Main && _open) Close();
        else if (_open && !ReferenceEquals(_builtKey, Main.Instance)) CallDeferred(MethodName.Rebuild);
        StartChecks();
    }

    // --------------------------------------------------------------- input

    private static bool IsChord(InputEvent e)
        => e is InputEventKey { Pressed: true, Echo: false } k
           && (k.Keycode == Key.F12 || k.PhysicalKeycode == Key.F12)
           && k.CtrlPressed && k.ShiftPressed && k.AltPressed;

    public override void _Input(InputEvent @event)
    {
        if (IsChord(@event))
        {
            GetViewport().SetInputAsHandled();
            Toggle();
            return;
        }
        if (_open && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    /// <summary>Nothing the panel did not use reaches the game while it is
    /// open (interact, jump, zoom), whether or not the run is paused.</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_open && @event is InputEventKey or InputEventMouseButton) GetViewport().SetInputAsHandled();
    }

    public void Toggle()
    {
        if (_open) Close();
        else Open();
    }

    public void Open()
    {
        if (_open) return;
        if (!_built || !ReferenceEquals(_builtKey, Main.Instance)) Rebuild();
        _open = true;
        _root!.Visible = true;
        _mouseWasCaptured = Input.MouseMode == Input.MouseModeEnum.Captured;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        RefreshAll();
        UpdatePause();
        GD.Print("DEV panel open");
    }

    public void Close()
    {
        if (!_open) return;
        _open = false;
        if (_root is not null) _root.Visible = false;
        if (_pausedByUs)
        {
            _pausedByUs = false;
            if (!OtherPanelHoldsPause()) GetTree().Paused = false;
        }
        if (_mouseWasCaptured && !GetTree().Paused && Main.Instance is not null) PlayerCamera.TryCaptureMouse();
        _mouseWasCaptured = false;
        GD.Print("DEV panel closed");
    }

    // --------------------------------------------------------------- frame

    public override void _Process(double delta)
    {
        // Stay last under the root: the newest scene is added after us, and the
        // last child sees input first.
        if (GetParent() is { } parent && GetIndex() != parent.GetChildCount() - 1)
            parent.MoveChild(this, -1);

        if (_titleReloadPending)
        {
            _titleReloadIn -= delta;
            if (_titleReloadIn <= 0) ReloadTitle();
        }

        if (!_open) return;
        if (Input.MouseMode != Input.MouseModeEnum.Visible) Input.MouseMode = Input.MouseModeEnum.Visible;
        UpdatePause();

        _refresh -= delta;
        if (_refresh > 0) return;
        _refresh = 0.25;
        RefreshAll();
    }

    private static bool InLiveRun => Main.Instance is { } m && IsInstanceValid(m) && m.Run is { Over: false };

    private void UpdatePause()
    {
        if (_pauseWhileOpen && InLiveRun)
        {
            if (!GetTree().Paused)
            {
                GetTree().Paused = true;
                _pausedByUs = true;
            }
        }
        else if (_pausedByUs)
        {
            _pausedByUs = false;
            if (!OtherPanelHoldsPause()) GetTree().Paused = false;
        }
    }

    /// <summary>True when a game panel that pauses the tree is open (level-up,
    /// pause menu, results, shrine, merchant/microwave), so closing must not
    /// unpause behind it.</summary>
    private bool OtherPanelHoldsPause()
    {
        if (Main.Instance is not { } main || !IsInstanceValid(main)) return false;
        if (main.LevelUp.IsOpen || main.Results.Visible) return true;
        return Holds(main);

        static bool Holds(Node n)
        {
            foreach (Node c in n.GetChildren())
            {
                switch (c)
                {
                    case PauseMenu { IsOpen: true }:
                    case ShrinePanel { IsOpen: true }:
                    case ModalPanel { IsOpen: true }:
                        return true;
                    case Node3D:
                        continue;   // the world: arena, enemies, props
                }
                if (Holds(c)) return true;
            }
            return false;
        }
    }

    // --------------------------------------------------------------- build

    private void Rebuild()
    {
        if (_root is not null)
        {
            _tab = _tabs.CurrentTab;
            _root.QueueFree();
        }
        _buttons.Clear();
        _refreshers.Clear();
        _builtKey = Main.Instance;
        _built = true;

        _root = new Control { Theme = DevUi.MakeTheme(), MouseFilter = Control.MouseFilterEnum.Stop, Visible = _open };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var dim = new ColorRect { Color = new Color(0.01f, 0.02f, 0.04f, 0.45f), MouseFilter = Control.MouseFilterEnum.Ignore };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(dim);

        var frame = new PanelContainer();
        var style = new StyleBoxFlat { BgColor = DevUi.Back, BorderColor = new Color(0.35f, 0.60f, 0.85f, 0.9f),
                                       ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10 };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(8);
        frame.AddThemeStyleboxOverride("panel", style);
        frame.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        frame.OffsetLeft = 28; frame.OffsetTop = 18; frame.OffsetRight = -28; frame.OffsetBottom = -18;
        _root.AddChild(frame);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        frame.AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 14);
        header.AddChild(DevUi.Lbl("DEVELOPER PANEL", 22, DevUi.Gold));
        _context = DevUi.Lbl("", 13, DevUi.Dim);
        _context.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _context.ClipText = true;
        header.AddChild(_context);
        header.AddChild(Reg("pause", DevUi.Toggle("Pause game while open", _pauseWhileOpen, on =>
        {
            _pauseWhileOpen = on;
            UpdatePause();
            Say(on ? "The run pauses while the panel is open." : "The run keeps going while the panel is open.");
        })));
        header.AddChild(Reg("close", DevUi.Btn("Close", Close, minWidth: 70f)));
        column.AddChild(header);

        _tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        column.AddChild(_tabs);
        _buildingTab = 0; AddTab("Profile", BuildProfileTab());
        _buildingTab = 1; AddTab("Player", RunOnly(BuildPlayerTab));
        _buildingTab = 2; AddTab("Build", RunOnly(BuildBuildTab));
        _buildingTab = 3; AddTab("Spawn", RunOnly(BuildSpawnTab));
        _buildingTab = 4; AddTab("Stage", RunOnly(BuildStageTab));
        _buildingTab = 5; AddTab("Info", BuildInfoTab());
        _buildingTab = -1;
        _tabs.CurrentTab = Mathf.Clamp(_tab, 0, _tabs.GetTabCount() - 1);
        _tabs.TabChanged += _ => RefreshAll();

        _status = DevUi.Lbl(LastStatus.Length > 0 ? LastStatus : "Ready.", 15, LastOk ? DevUi.Good : DevUi.Bad);
        _status.ClipText = true;
        column.AddChild(_status);
    }

    private void AddTab(string title, Control content)
    {
        content.Name = title;
        _tabs.AddChild(content);
    }

    /// <summary>A refresher for the tab being built (runs while it is shown).</summary>
    private void OnRefresh(Action refresh) => _refreshers.Add((_buildingTab, refresh));

    private static Control RunOnly(Func<Control> build)
    {
        if (Main.Instance is { } m && IsInstanceValid(m) && m.Run is not null) return build();
        var box = new VBoxContainer();
        box.AddChild(DevUi.Lbl("Start a run to use this tab.", 18, DevUi.Dim));
        box.AddChild(DevUi.Lbl("The Profile and Info tabs work here on the title screen.", 15, DevUi.Dim));
        return box;
    }

    /// <summary>Register a button under an id (for the self-check) and return it.</summary>
    private T Reg<T>(string id, T button) where T : BaseButton
    {
        _buttons[id] = button;
        return button;
    }

    private void RefreshAll()
    {
        if (_root is null) return;
        _context.Text = DescribeContext();
        int tab = _tabs.CurrentTab;
        foreach (var (t, refresh) in _refreshers)
            if (t == tab || t < 0) refresh();
    }

    private static string DescribeContext()
    {
        if (Main.Instance is not { } m || !IsInstanceValid(m) || m.Run is null)
            return Loc.F("title screen  ·  profile {0}", Loc.Ltr(Meta.Profile.Path ?? Loc.T("(memory only: automated run)")));
        string state = m.Run.Over ? Loc.T("run over") : Loc.F("phase {0}/{1} {2}  ·  {3}", m.Run.Phase, m.Run.PhaseCount, m.Run.StageName, m.Run.Clock);
        return Loc.F("{0}  ·  x{1:0.##} time", state, Engine.TimeScale) + (UsedThisRun ? Loc.T("  ·  dev run: not counted") : "");
    }

    // -------------------------------------------------------------- actions

    /// <summary>Run one panel action: report its message on the status line,
    /// mark the run as touched when <paramref name="runSide"/>, and turn an
    /// exception into a red status instead of a crash.</summary>
    private bool Act(string what, Func<string?> action, bool runSide = true)
    {
        try
        {
            string? message = action();
            bool ok = message is not null;
            if (ok && runSide && Main.Instance is not null)
            {
                if (!UsedThisRun) GD.Print("DEV this run is now marked as developer-assisted (no quests or leaderboard)");
                UsedThisRun = true;
            }
            Say(ok ? message! : $"{what}: nothing to do here.", ok);
            GD.Print($"DEV {(ok ? "ok" : "noop")} {what}: {message}");
            RefreshAll();
            return ok;
        }
        catch (InvalidOperationException refused)
        {
            // An expected refusal (slots full and the like), not a bug.
            Say($"{what}: {refused.Message}.", false);
            GD.Print($"DEV refused {what}: {refused.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Say($"{what} failed: {ex.Message}", false);
            GD.PushError($"DEV {what} failed: {ex}");
            return false;
        }
    }

    private void Say(string message, bool ok = true)
    {
        LastStatus = message;
        LastOk = ok;
        if (_status is null || !IsInstanceValid(_status)) return;
        // LastStatus stays English (the self-check reads it); the line shows the
        // player's language.
        _status.Text = Loc.T(message);
        _status.AddThemeColorOverride("font_color", ok ? DevUi.Good : DevUi.Bad);
    }

    // ---------------------------------------------------------- title page

    /// <summary>After a profile change on the title, rebuild the menu so the
    /// character list, codex and shop show it. Debounced: a burst of clicks
    /// reloads once.</summary>
    private void RequestTitleRefresh()
    {
        if (Main.Instance is not null) return;
        _titleReloadPending = true;
        _titleReloadIn = 0.25;
    }

    private void ReloadTitle()
    {
        _titleReloadPending = false;
        if (GetTree().CurrentScene is not MainMenu menu) return;
        MainMenu.DevReturnPage = menu.CurrentPage;
        GD.Print($"DEV title menu reloaded to show the change (page {menu.CurrentPage})");
        GetTree().ReloadCurrentScene();
    }
}
