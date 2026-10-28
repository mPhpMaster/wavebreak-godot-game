using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Audio;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The title screen: an orbiting view over a small arena, and four pages —
/// main, character select, codex, settings.
///
/// The first thing it does is decide whether to exist. Every automated launch
/// (smoke test, input guards, content audit, captures) passes game arguments
/// and expects a live run, so <see cref="Launch.SkipsMenu"/> sends those straight
/// to the run scene before anything is built. Only a bare launch — a player —
/// sees the menu.
///
/// Built in code like the HUD, for the same reason: one readable file, no
/// editor state to drift. Every control is a real focusable Button or Slider,
/// so the built-in ui_* actions give keyboard and gamepad navigation for free;
/// the only custom input is ui_cancel stepping back a page.
/// </summary>
public sealed partial class MainMenu : Node3D
{
    public const string RunScene = "res://scenes/Main.tscn";
    public const string TitleScene = "res://scenes/Title.tscn";

    private static readonly Color Ink = new(0.93f, 0.95f, 0.99f);
    private static readonly Color InkDim = new(0.62f, 0.68f, 0.78f);
    private static readonly Color Accent = new(0.45f, 0.82f, 1.00f);
    private static readonly Color Gold = new(1.00f, 0.84f, 0.38f);
    private static readonly Color Locked = new(0.50f, 0.52f, 0.58f);
    private static readonly Color Outline = new(0.02f, 0.03f, 0.05f, 0.9f);
    private static readonly Color PanelBack = new(0.05f, 0.06f, 0.10f, 0.86f);

    private Camera3D _camera = null!;
    private float _orbit;
    private Node3D _hero = null!;
    private string? _heroLook;

    private Control _root = null!;
    private readonly Dictionary<string, Control> _pages = new();
    private string _page = "main";
    /// <summary>Page to land on after the developer panel reloads the title.</summary>
    internal static string? DevReturnPage;
    internal string CurrentPage => _page;
    private Button _playButton = null!;

    // character page
    private Label _charName = null!;
    private Label _charLook = null!;
    private Label _charBlurb = null!;
    private Label _charStats = null!;
    private Label _charLock = null!;
    private Label _charPassive = null!;
    private Label _skinLabel = null!;
    private CharacterDef? _shownCharacter;
    private int _skinIndex;
    private readonly List<Button> _charButtons = new();
    private Button _startButton = null!;

    // settings page
    private readonly List<(HSlider slider, VolumeChannel channel)> _volumeSliders = new();
    private CheckButton _mute = null!;
    private Codex _codex = null!;
    private GraphicsPanel _graphics = null!;

    // pearl shop / difficulty (UI/MetaShopPage, UI/DifficultyPicker)
    private MetaShopPage _shop = null!;
    private DifficultyPicker _difficulty = null!;
    private ChallengePicker _runPicker = null!;
    private TogglerPage _toggler = null!;
    private BoomboxPage _boombox = null!;
    private CoopPage _coop = null!;
    private CoopLineup? _lineup;
    private Button _runButton = null!;
    private Label _pearls = null!;

    public override void _Ready()
    {
        Launch.Parse();
        Loc.Init(this);
        if (Updates.UpdateCheck.Requested) { AddChild(new Updates.UpdateCheck()); return; }
        if (Launch.DumpStrings is { } dump)
        {
            LocContent.Dump(dump);
            GetTree().Quit();
            return;
        }
        // --steam-selftest: the Steam transport over a fake Steam (Net/Steam/SteamSelfTest.cs).
        if (Net.SteamSelfTest.Requested)
        {
            AddChild(new Net.SteamSelfTest());
            return;
        }
        // --check-browse: the public games list's filter and format (Net/PublicGames.cs).
        if (Net.PublicGames.CheckRequested)
        {
            Net.PublicGames.RunCheck();
            GetTree().Quit();
            return;
        }
        // --coop-host / --coop-join: the automated co-op test. No menu; the
        // session starts the run when everyone is in (Net/NetSession.Test.cs).
        // With --menu-shot the menu is built too, to photograph the lobby.
        bool coopArgs = Net.NetSession.AutoRequested();
        bool coopAuto = coopArgs && !Net.NetSession.AutoStarted;   // false when the session already ended
        if (coopAuto && Launch.MenuShot is null)
        {
            Net.NetSession.StartAuto(GetTree());
            return;
        }
        if (Launch.SkipsMenu && !coopArgs)
        {
            // Deferred: changing scene from inside the outgoing scene's _Ready is
            // the case deferred calls exist for.
            GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, RunScene);
            return;
        }

        Profile.EnsureLoaded();
        GameSettings.Load();
        Keybinds.Load();
        GraphicsSettings.Load();
        GraphicsSettings.ApplyWindowOnce();
        AudioSettings.Load();
        AudioSettings.Apply();
        MusicDirector.PlayTitle(this);

        // A results screen or a run may have left the tree paused or the mouse
        // captured; the menu needs neither.
        GetTree().Paused = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;

        BuildBackdrop();
        BuildUi();
        GraphicsSettings.ApplyAll(this);
        if (Updates.UpdateFixtures.Requested) Engine.MaxFps = 5;
        // Launched from a Steam invite (+connect_lobby <id>): start Steam and
        // go straight to the CO-OP page, which joins.
        if (Net.SteamApp.Allowed && Net.SteamLobby.TakeCommandLineLobby() is var lobby && lobby != 0)
        {
            GD.Print($"[steam] launched to join lobby {lobby}");
            Net.SteamLobby.PendingJoin = lobby;
            CoopPage.OpenOnTitle = true;
        }
        ShowPage(DevReturnPage ?? (Launch.MenuShot is not null ? Launch.MenuPage : CoopPage.OpenOnTitle ? "coop" : "main"));
        DevReturnPage = null;
        if (Launch.MenuExpeditionCheck is not null) AddChild(new Game.ExpeditionMenuCheck(this));
        DevPanel.Install(this);
        Net.SteamLobby.JoinRequested += OnSteamJoinRequested;

        if (coopAuto) Net.NetSession.StartAuto(GetTree());
        else if (coopArgs) Net.NetSession.TitleShown(this, _page);   // the host-leaves test lands here
        // Lobby captures: the host waits for the guest (who starts a moment later) to photograph first.
        if (Launch.MenuShot is not null) _ = CaptureAndQuit(coopAuto ? 4.0 : 2.5);
    }

    private void TintHero(CharacterDef def) => ShowHeroSkin(def.SelectedSkin);

    /// <summary>Swap the backdrop hero to a skin: its model and its recolour.
    /// Rebuilds only on change (keyed by skin id, since several skins share a
    /// model): the focus handlers call this on every hover.</summary>
    private void ShowHeroSkin(SkinDef skin)
    {
        if (_heroLook == skin.Id) return;
        _heroLook = skin.Id;
        foreach (Node child in _hero.GetChildren())
        {
            if (child is Light3D) continue;
            _hero.RemoveChild(child);
            child.QueueFree();
        }
        Node3D model = Visuals.PlayerLooks.Build(skin.LookId, skin.Paint);
        model.Scale = Vector3.One * 1.0f;
        _hero.AddChild(model);
        ApplyMenuHat();
    }

    // ================================================================== UI

    private void BuildUi()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_root);

        // Left-hand vignette so text never fights the backdrop.
        var shade = new ColorRect { Color = new Color(0.02f, 0.03f, 0.06f, 0.55f), MouseFilter = Control.MouseFilterEnum.Ignore };
        shade.AnchorBottom = 1f; shade.OffsetRight = 470f;
        _root.AddChild(shade);

        // Version, small, bottom right (left in RTL): config/version in project.godot.
        string version = ProjectSettings.GetSetting("application/config/version", "").AsString();
        var ver = new Label
        {
            Text = version.Length > 0 ? $"v{version}" : "",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutoTranslateMode = AutoTranslateModeEnum.Disabled,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        ver.AddThemeFontSizeOverride("font_size", 14);
        ver.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.6f));
        ver.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.6f));
        ver.AddThemeConstantOverride("outline_size", 4);
        ver.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight);
        ver.OffsetLeft = -160f; ver.OffsetTop = -30f; ver.OffsetRight = -12f; ver.OffsetBottom = -8f;
        _root.AddChild(ver);

        Label title = MakeLabel("WAVEBREAK", 72, Ink);
        title.AutoTranslateMode = AutoTranslateModeEnum.Disabled; // the name stays the name
        Place(title, 56f, 54f, 600f, 90f);
        title.AddThemeConstantOverride("outline_size", 10);
        _root.AddChild(title);
        Label tag = MakeLabel("hold the line until the tide turns", 18, Accent);
        Place(tag, 60f, 140f, 600f, 26f);
        _root.AddChild(tag);

        _pages["main"] = BuildMainPage();
        _pages["play"] = BuildCharacterPage();
        _pages["expeditions"] = BuildExpeditionPage();
        _pages["codex"] = BuildCodexPage();
        _pages["settings"] = BuildSettingsPage();
        _pages["keys"] = BuildKeysPage();
        _pages["graphics"] = BuildGraphicsPage();
        _pages["gameplay"] = BuildGameplayPage();   // UI/GameplayPanel
        _pages["shop"] = _shop = new MetaShopPage(() => ShowPage("main"));
        _pages["records"] = BuildLeaderboardPage();
        _pages["run"] = _runPicker = new ChallengePicker(() => ShowPage("play"));
        _pages["toggler"] = _toggler = new TogglerPage(() => ShowPage("shop"));   // UI/TogglerPage
        _shop.OpenToggler = () => ShowPage("toggler");
        _pages["boombox"] = _boombox = new BoomboxPage(() => ShowPage("main"));   // UI/BoomboxPage
        _pages["coop"] = _coop = new CoopPage(() => ShowPage("main"));   // UI/CoopPage
        _pages["credits"] = BuildCreditsPage();   // UI/MainMenu.Credits
        AddUpdates(_pages["main"]);
        AddChild(_lineup = new CoopLineup(_coop));   // the lobby's players on the beach
        foreach (Control p in _pages.Values) _root.AddChild(p);
        AddCreditsButton();
    }

    private Control BuildMainPage()
    {
        var page = new VBoxContainer();
        Place(page, 60f, 220f, 340f, 380f);
        page.AddThemeConstantOverride("separation", 12);

        _playButton = MenuButton("PLAY", () => ShowPage("play"));
        page.AddChild(_playButton);
        page.AddChild(MenuButton("CO-OP", () => ShowPage("coop")));
        page.AddChild(MenuButton("PEARL SHOP", () => ShowPage("shop")));
        page.AddChild(MenuButton("UNLOCKS & SECRETS", () => ShowPage("codex")));
        page.AddChild(MenuButton("LEADERBOARD", () => ShowPage("records")));
        page.AddChild(MenuButton("BOOMBOX", () => ShowPage("boombox")));
        page.AddChild(MenuButton("SETTINGS", () => ShowPage("settings")));
        page.AddChild(MenuButton("QUIT", () => GetTree().Quit()));
        // Eight buttons since CO-OP joined: a little tighter, so the stats
        // lines below still fit above the window's bottom edge.
        page.AddThemeConstantOverride("separation", 7);
        foreach (Node n in page.GetChildren())
            if (n is Button b) b.CustomMinimumSize = new Vector2(0f, 44f);

        long best = Profile.Get(Profile.BestSeconds);
        Label stats = MakeLabel(
            Loc.F("runs {0}   ·   best {1:00}:{2:00}   ·   unlocks {3}/{4}",
                  Profile.Get(Profile.Runs), best / 60, best % 60, Profile.UnlockedIds.Count, Unlocks.All.Count), 15, InkDim);
        stats.CustomMinimumSize = new Vector2(0f, 30f);
        stats.VerticalAlignment = VerticalAlignment.Bottom;
        page.AddChild(stats);
        _pearls = MakeLabel("", 15, Gold);
        page.AddChild(_pearls);
        return WrapMainExpeditions(page);
    }

    // ------------------------------------------------------------ characters

    // ----------------------------------------------------------------- codex

    private void ShowPage(string name)
    {
        // "page:arg" for captures: "codex:secrets" scrolls to the hidden entries,
        // "play:<character id>" opens with that character focused.
        string arg = "";
        int colon = name.IndexOf(':');
        if (colon >= 0) { arg = name[(colon + 1)..]; name = name[..colon]; }
        bool secrets = arg == "secrets";
        if (!_pages.ContainsKey(name)) name = "main";
        if (_page == "updates" && name != "updates" && _updates?.Busy == true) _updates.Cancel();
        if (_page == "boombox" && name != "boombox") _boombox.Closing();
        if (name == "main" && _page is "expeditions" or "coop") RestoreExpeditionClassic();
        _page = name;
        foreach (var (key, page) in _pages) page.Visible = key == name;
        if (_creditsButton is not null) _creditsButton.Visible = name == "main";

        // Focus is what makes the page keyboard- and gamepad-usable; every page
        // lands with something focused.
        switch (name)
        {
            case "updates":
                _updatePanel?.FocusFirst();
                break;
            case "expeditions":
                OpenExpeditions();
                break;
            case "run":
                _runPicker.Open(arg);
                break;
            case "toggler":
                _toggler.Open(arg);
                break;
            case "boombox":
                _boombox.Open(arg);
                break;
            case "coop":
                _coop.Open();
                break;
            case "play":
                _difficulty.Refresh();
                _runButton.Text = RouteBarText();
                int i = Math.Max(0, IndexOfCharacter(arg.Length > 0 ? arg : Profile.SelectedCharacter));
                _charButtons[i].GrabFocus();
                ScrollRosterTo(_charButtons[i]);
                PickCharacter(Characters.All[i], focusStart: false);
                break;
            case "codex":
                _codex.Refresh();
                if (secrets) _codex.FocusFirstSecret();
                else _codex.FocusFirst();
                break;
            case "settings":
                _volumeSliders[0].slider.GrabFocus();
                break;
            case "gameplay":
                _gameplay.Refresh();
                _gameplay.FocusFirst();
                break;
            case "graphics":
                _graphics.Refresh();
                if (arg == "bottom") _graphics.FocusLast();
                else _graphics.FocusFirst();
                break;
            case "shop":
                _shop.Refresh();
                if (arg.Length > 0) _shop.FocusLine(arg); else _shop.FocusFirst();
                break;
            case "records":
                _leaderboard.Refresh();
                _leaderboardBack.GrabFocus();
                break;
            default:
                _pearls.Text = Loc.F("{0} {1}   ·   difficulty {2}", Pearls.Name, Pearls.Balance, DifficultyTiers.Selected.Name);
                _playButton.GrabFocus();
                break;
        }
    }

    /// <summary>A Steam invite was accepted (overlay or "Join Game") while the
    /// title is up: the CO-OP page opens and joins.</summary>
    private void OnSteamJoinRequested()
    {
        if (_root is null || DevPanel.IsOpen) return;
        ShowPage("coop");
    }

    public override void _ExitTree()
    {
        Net.SteamLobby.JoinRequested -= OnSteamJoinRequested;
        StopUpdates();
    }

    private static int IndexOfCharacter(string id)
    {
        for (int i = 0; i < Characters.All.Count; i++) if (Characters.All[i].Id == id) return i;
        return -1;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_root is null || DevPanel.IsOpen) return;
        if (_page == "play" && (@event.IsActionPressed("ui_left") || @event.IsActionPressed("ui_right")))
        {
            CycleSkin((@event.IsActionPressed("ui_left") ? -1 : 1) * (Loc.IsRtl ? -1 : 1));
            GetViewport().SetInputAsHandled();
            return;
        }
        if (KeybindPanel.AnyListening || GraphicsPanel.Blocking) return;
        if (@event.IsActionPressed("ui_cancel") && _page != "main")
        {
            GetViewport().SetInputAsHandled();
            if (_page == "updates" && _updatePanel?.CancelBusy() == true) return;
            if (_page == "coop") CoopPage.LeaveSession();   // same as BACK
            ShowPage(_page switch { "run" => "play", "toggler" => "shop", "keys" or "graphics" or "gameplay" => "settings", _ => "main" });
        }
    }

    // ------------------------------------------------------------- capture

    private async System.Threading.Tasks.Task CaptureAndQuit(double after)
    {
        SceneTree tree = GetTree();
        await ToSignal(tree.CreateTimer(after), SceneTreeTimer.SignalName.Timeout);
        // A lobby capture (--coop-lobby-only): wait until both sides are in
        // and ready, so each photographs the full line-up on the beach.
        bool lobby = Net.Coop.Active;
        for (int i = 0; lobby && i < 60 && !(Net.Coop.Roster.Count >= 2 && Net.Coop.Roster.TrueForAll(p => p.Ready)); i++)
            await ToSignal(tree.CreateTimer(0.25), SceneTreeTimer.SignalName.Timeout);
        if (lobby) await ToSignal(tree.CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        if (_page == "coop") ShowPage("coop");   // redraw with whoever joined
        await Game.Screenshot.Capture(tree, this, Launch.MenuShot!);
        GD.Print($"MENU page={_page} unlocks={Profile.UnlockedIds.Count}/{Unlocks.All.Count} secrets={Profile.FoundSecrets.Count} " +
                 $"characters={string.Join(",", CharacterStates())}" +
                 (_page == "coop" ? $" lineup={_lineup?.Count ?? 0} difficulty={Net.Coop.DifficultyId}" : ""));
        // Stay in the lobby a little longer so the other side's capture still sees two players.
        if (lobby) for (int k = 0; k < 16 && Net.Coop.Active; k++) await ToSignal(tree.CreateTimer(0.25), SceneTreeTimer.SignalName.Timeout);
        tree.Quit();
    }

    private static IEnumerable<string> CharacterStates()
    {
        foreach (CharacterDef c in Characters.All) yield return $"{c.Id}:{(c.IsAvailable ? "open" : "locked")}";
    }

    // -------------------------------------------------------------- helpers

    internal static Button MenuButton(string text, Action onPressed)
    {
        var b = new Button { Text = Loc.T(text), FocusMode = Control.FocusModeEnum.All, Alignment = HorizontalAlignment.Left };
        b.CustomMinimumSize = new Vector2(0f, 48f);
        b.AddThemeFontSizeOverride("font_size", 22);
        b.AddThemeColorOverride("font_focus_color", Gold);
        b.AddThemeColorOverride("font_hover_color", Gold);
        b.AddThemeStyleboxOverride("normal", ButtonBox(new Color(0.08f, 0.10f, 0.16f, 0.85f), new Color(0, 0, 0, 0)));
        b.AddThemeStyleboxOverride("hover", ButtonBox(new Color(0.12f, 0.15f, 0.24f, 0.92f), new Color(0, 0, 0, 0)));
        b.AddThemeStyleboxOverride("pressed", ButtonBox(new Color(0.16f, 0.20f, 0.30f, 0.95f), Gold));
        b.AddThemeStyleboxOverride("focus", ButtonBox(new Color(0, 0, 0, 0), Gold));
        b.Pressed += onPressed;
        return b;
    }

    private static StyleBoxFlat ButtonBox(Color back, Color border)
    {
        var s = new StyleBoxFlat { BgColor = back, BorderColor = border, ContentMarginLeft = 18, ContentMarginRight = 18 };
        s.SetBorderWidthAll(0);
        s.BorderWidthLeft = border.A > 0 ? 4 : 0;
        s.SetCornerRadiusAll(4);
        return s;
    }

    private static PanelContainer Panel(float left, float top, float width, float height)
    {
        var p = new PanelContainer();
        var s = new StyleBoxFlat { BgColor = PanelBack, ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 18, ContentMarginBottom = 18 };
        s.SetCornerRadiusAll(6);
        p.AddThemeStyleboxOverride("panel", s);
        Place(p, left, top, width, height);
        return p;
    }

    private static Label SectionLabel(string text)
    {
        Label l = MakeLabel(text, 15, InkDim);
        l.CustomMinimumSize = new Vector2(0f, 30f);
        l.VerticalAlignment = VerticalAlignment.Bottom;
        return l;
    }

    internal static Label MakeLabel(string text, int size, Color color)
    {
        var label = new Label { Text = Loc.T(text), MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", Outline);
        label.AddThemeConstantOverride("outline_size", 5);
        return label;
    }

    internal static void Place(Control c, float left, float top, float width, float height)
    {
        c.AnchorLeft = 0f; c.AnchorRight = 0f; c.AnchorTop = 0f; c.AnchorBottom = 0f;
        c.OffsetLeft = left; c.OffsetTop = top;
        c.OffsetRight = left + width; c.OffsetBottom = top + height;
    }
}
