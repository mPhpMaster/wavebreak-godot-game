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
        AddChild(_lineup = new CoopLineup(_coop));   // the lobby's players on the beach
        foreach (Control p in _pages.Values) _root.AddChild(p);
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
        return page;
    }

    // ------------------------------------------------------------ characters

    private Control BuildCharacterPage()
    {
        var page = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        Label heading = MakeLabel("CHOOSE WHO WASHES UP", 20, InkDim);
        Place(heading, 60f, 196f, 400f, 28f);
        page.AddChild(heading);

        var list = new VBoxContainer();
        Place(list, 60f, 236f, 340f, 460f);
        // 6, not 10: seven characters and Back must fit above the window's edge.
        list.AddThemeConstantOverride("separation", 6);
        page.AddChild(list);

        // The roster outgrew the column: it scrolls, and follows keyboard focus.
        VBoxContainer charList = CharacterScroll(list);

        foreach (CharacterDef def in Characters.All)
        {
            CharacterDef c = def;
            bool open = c.IsAvailable;
            bool hidden = !open && Unlocks.ById(c.UnlockedBy) is { Secret: true };
            string label = open ? Loc.T(c.DisplayName) : hidden ? "???" : Loc.F("{0}  ·  locked", c.DisplayName);
            // Clicking picks the character; the run starts only from START RUN,
            // so the skin and hat can be chosen first.
            Button b = MenuButton(label, () => PickCharacter(c));
            if (!open) b.AddThemeColorOverride("font_color", Locked);
            b.MouseEntered += () => b.GrabFocus();
            _charButtons.Add(b);
            charList.AddChild(b);
        }
        _difficulty = new DifficultyPicker();
        list.AddChild(_difficulty);

        // Route & challenges (UI/ChallengePicker), above the character panel: the
        // panel grows with skin, hat and passive rows and would cover it below.
        _runButton = MenuButton("", () => ShowPage("run"));
        Place(_runButton, 470f, 186f, 520f, 42f);
        _runButton.MouseEntered += () => _runButton.GrabFocus();
        page.AddChild(_runButton);
        _difficulty.Button.FocusNeighborRight = _difficulty.Button.GetPathTo(_runButton);
        _startButton = MenuButton("START RUN", () => { if (_shownCharacter is { } sel) StartRun(sel); });
        _startButton.AddThemeColorOverride("font_color", Gold);
        _startButton.AddThemeColorOverride("font_focus_color", Gold);
        _startButton.AddThemeColorOverride("font_hover_color", Gold);
        list.AddChild(MenuButton("BACK", () => ShowPage("main")));

        var panel = Panel(470f, 236f, 520f, 300f);
        page.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);
        _charName = MakeLabel("", 30, Ink);
        _charLook = MakeLabel("", 14, InkDim);
        _charBlurb = MakeLabel("", 17, Ink);
        _charBlurb.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _charStats = MakeLabel("", 16, Accent);
        _charStats.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _charLock = MakeLabel("", 16, Gold);
        _charLock.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _charPassive = MakeLabel("", 16, Gold);
        _charPassive.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        foreach (Label l in new[] { _charName, _charLook, _charBlurb, _charStats, _charPassive, _charLock }) box.AddChild(l);

        // Skin picker: the arrow buttons and the left/right keys both cycle it.
        // The buttons take no focus so the up/down character list keeps it.
        var skinRow = new HBoxContainer();
        skinRow.AddThemeConstantOverride("separation", 10);
        // Right to left, "previous" sits on the right and points that way.
        Button prev = SkinArrow(Loc.IsRtl ? ">" : "<", -1);
        Button next = SkinArrow(Loc.IsRtl ? "<" : ">", +1);
        _skinLabel = MakeLabel("", 18, Ink);
        _skinLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _skinLabel.HorizontalAlignment = HorizontalAlignment.Center;
        skinRow.AddChild(prev);
        skinRow.AddChild(_skinLabel);
        skinRow.AddChild(next);
        box.AddChild(skinRow);
        BuildHatRow(box);
        // Last in the panel, under the skin and hat it applies to.
        _startButton.CustomMinimumSize = new Vector2(0f, 46f);
        box.AddChild(_startButton);
        return page;
    }

    private Button SkinArrow(string text, int step)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(44f, 36f) };
        b.AddThemeFontSizeOverride("font_size", 20);
        b.Pressed += () => CycleSkin(step);
        return b;
    }

    private void CycleSkin(int step)
    {
        if (_shownCharacter is not { } c) return;
        int n = c.Skins.Count;
        _skinIndex = ((_skinIndex + step) % n + n) % n;
        ShowSkin();
    }

    private void ShowSkin()
    {
        if (_shownCharacter is not { } c) return;
        SkinDef skin = c.Skins[_skinIndex];
        int n = c.Skins.Count;
        if (skin.IsAvailable)
        {
            _skinLabel.Text = Loc.F("skin: {0}   ({1}/{2})", skin.Name, _skinIndex + 1, n);
            _skinLabel.AddThemeColorOverride("font_color", Ink);
        }
        else
        {
            UnlockDef? source = Unlocks.ById(Unlocks.SourceOf(skin.Id));
            string how = source is null ? "" : " — " + Loc.T(source.Secret && !Profile.IsUnlocked(source.Id) ? source.Hint : source.Description);
            _skinLabel.Text = Loc.F("skin: {0}  ·  locked{1}   ({2}/{3})", skin.Name, how, _skinIndex + 1, n);
            _skinLabel.AddThemeColorOverride("font_color", Locked);
        }
        _skinLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        ShowHeroSkin(skin);
        ShowHat();
    }

    private void ShowCharacter(CharacterDef c)
    {
        bool open = c.IsAvailable;
        UnlockDef? source = Unlocks.ById(c.UnlockedBy);
        bool hidden = !open && source is { Secret: true };

        // Name, blurb, starting weapon and stats, passive: UI/CharacterInfo,
        // shared with the CO-OP page.
        CharacterInfo info = CharacterInfo.Of(c);
        info.Into(_charName, _charBlurb, _charStats, null);
        _charPassive.Text = info.Passive;
        _charLook.Text = Loc.F("look: {0}   ·   id: {1}", Loc.Ltr(c.LookId), Loc.Ltr(c.Id));
        _charLook.AutoTranslateMode = AutoTranslateModeEnum.Disabled; // ids, a developer line
        _charLock.Text = open ? Loc.T("Pick a skin and a hat, then press START RUN")
            : hidden ? Loc.F("Secret — {0}", source!.Hint)
            : Loc.F("Locked — {0}", source?.Description ?? "");
        if (open) TintHero(c);

        if (_shownCharacter != c)
        {
            _shownCharacter = c;
            SkinDef chosen = c.SelectedSkin;
            _skinIndex = 0;
            for (int i = 0; i < c.Skins.Count; i++) if (c.Skins[i].Id == chosen.Id) _skinIndex = i;
        }
        ShowSkin();
    }

    /// <summary>Make <paramref name="c"/> the selection: the panel, hero, skin
    /// and hat rows follow it, its button turns gold, and START RUN is enabled
    /// when it is unlocked. Focus moves to START RUN so Enter twice still plays.</summary>
    private void PickCharacter(CharacterDef c, bool focusStart = true)
    {
        ShowCharacter(c);
        for (int i = 0; i < _charButtons.Count; i++)
        {
            CharacterDef each = Characters.All[i];
            Color colour = each == c ? Gold : each.IsAvailable ? Ink : Locked;
            _charButtons[i].AddThemeColorOverride("font_color", colour);
        }
        _startButton.Disabled = !c.IsAvailable;
        if (focusStart && c.IsAvailable) _startButton.GrabFocus();
    }

    private void StartRun(CharacterDef c)
    {
        if (!c.IsAvailable) return;
        Profile.SelectedCharacter = c.Id;
        // Only a skin the player owns is remembered; picking a locked one
        // previews it but runs in the last owned choice.
        if (_shownCharacter == c && c.Skins[_skinIndex] is { IsAvailable: true } skin)
            Profile.SelectSkin(c.Id, skin.Id);
        SaveHatChoice(c);
        Profile.Save();
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, RunScene);
    }

    // ----------------------------------------------------------------- codex

    private Control BuildCodexPage()
    {
        var page = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _codex = new Codex();
        Place(_codex, 60f, 196f, 1160f, 450f);
        page.AddChild(_codex);

        Button back = MenuButton("BACK", () => ShowPage("main"));
        Place(back, 60f, 660f, 200f, 44f);
        page.AddChild(back);
        _codex.BackButton = back;
        return page;
    }

    // ----------------------------------------------------------- leaderboard

    private LeaderboardPage _leaderboard = null!;
    private Button _leaderboardBack = null!;

    private Control BuildLeaderboardPage()
    {
        var page = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _leaderboard = new LeaderboardPage();
        Place(_leaderboard, 60f, 196f, 1000f, 450f);
        page.AddChild(_leaderboard);
        _leaderboardBack = MenuButton("BACK", () => ShowPage("main"));
        Place(_leaderboardBack, 60f, 660f, 200f, 44f);
        page.AddChild(_leaderboardBack);
        return page;
    }

    // -------------------------------------------------------------- settings

    private Control BuildSettingsPage()
    {
        var page = new VBoxContainer();
        Place(page, 60f, 190f, 380f, 520f);
        page.AddThemeConstantOverride("separation", 6);

        page.AddChild(SectionLabel("SOUND"));
        foreach (var (name, ch) in new[] { ("Master", VolumeChannel.Master), ("Effects", VolumeChannel.Sfx), ("Music", VolumeChannel.Music) })
        {
            VolumeChannel channel = ch;
            HSlider s = Slider(0, 10, 1, Mathf.Round(AudioSettings.Get(channel) * 10f));
            Label value = MakeLabel("", 16, Ink);
            void Show() => value.Text = $"{(int)s.Value}/10";
            Show();
            s.ValueChanged += v =>
            {
                int steps = (int)Math.Round(v - Math.Round(AudioSettings.Get(channel) * 10f));
                if (steps != 0) AudioSettings.Nudge(channel, steps);
                Show();
                _mute.SetPressedNoSignal(AudioSettings.Muted);
            };
            _volumeSliders.Add((s, channel));
            page.AddChild(Row(name, s, value));
        }
        _mute = new CheckButton { Text = Loc.T("Mute all"), ButtonPressed = AudioSettings.Muted };
        _mute.AddThemeFontSizeOverride("font_size", 18);
        _mute.Toggled += on => { if (on != AudioSettings.Muted) AudioSettings.ToggleMute(); };
        page.AddChild(Loc.IsRtl ? MuteRow(_mute, n => Row(n, new HBoxContainer(), MakeLabel("", 16, Ink))) : _mute);

        page.AddChild(SectionLabel("CONTROLS & DISPLAY"));
        HSlider sens = Slider(GameSettings.MinSensitivity, GameSettings.MaxSensitivity, GameSettings.SensitivityStep, GameSettings.Sensitivity);
        Label sensValue = MakeLabel($"{GameSettings.Sensitivity:0.00}x", 16, Ink);
        sens.ValueChanged += v => { GameSettings.SetSensitivity((float)v); sensValue.Text = $"{GameSettings.Sensitivity:0.00}x"; };
        page.AddChild(Row("Mouse sensitivity", sens, sensValue));
        page.AddChild(Row("Language", LanguagePicker(), MakeLabel("", 16, Ink)));

        Label keys = MakeLabel("In a run:  - / =  volume   ·   9 / 0  effects   ·   [ / ]  music   ·   M  mute", 14, InkDim);
        page.AddChild(keys);
        // Side by side: with the language row added, three stacked buttons no
        // longer fit above the window's bottom edge.
        var pages = new HBoxContainer();
        pages.AddThemeConstantOverride("separation", 8);
        pages.AddThemeConstantOverride("separation", 6);
        foreach (Button b in new[] { MenuButton("GAMEPLAY", () => ShowPage("gameplay")), MenuButton("GRAPHICS", () => ShowPage("graphics")), MenuButton("KEY BINDINGS", () => ShowPage("keys")) })
        {
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            b.AddThemeFontSizeOverride("font_size", 16);
            // Three side by side: tighter margins so they share the column.
            foreach (string box in new[] { "normal", "hover", "pressed", "focus" })
                if (b.GetThemeStylebox(box) is StyleBoxFlat flat) { flat.ContentMarginLeft = 10; flat.ContentMarginRight = 8; }
            pages.AddChild(b);
        }
        page.AddChild(pages);
        page.AddChild(MenuButton("BACK", () => ShowPage("main")));
        return page;
    }

    /// <summary>English / العربية. Choosing one applies it (locale, layout
    /// direction, font fallback) and rebuilds the title on the settings page, so
    /// every label built in code is redone in the new language at once.</summary>
    private OptionButton LanguagePicker()
    {
        var ob = new OptionButton { FocusMode = Control.FocusModeEnum.All };
        // Native names are never translated: a player lost in the wrong
        // language must still find their own.
        ob.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
        ob.GetPopup().AutoTranslateMode = AutoTranslateModeEnum.Disabled;
        for (int i = 0; i < Loc.Codes.Length; i++) ob.AddItem(Loc.NativeNames[i], i);
        ob.Selected = Math.Max(0, Array.IndexOf(Loc.Codes, Loc.Language));
        ob.AddThemeFontSizeOverride("font_size", 18);
        ob.ItemSelected += index =>
        {
            string code = Loc.Codes[Math.Clamp((int)index, 0, Loc.Codes.Length - 1)];
            if (code == Loc.Language) return;
            Loc.SetLanguage(code, GetTree());
            DevReturnPage = "settings";
            GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
        };
        return ob;
    }

    private Control BuildKeysPage()
    {
        // Wider than the left vignette, so it carries its own backing.
        var frame = new PanelContainer();
        var style = new StyleBoxFlat { BgColor = PanelBack, ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 12, ContentMarginBottom = 12 };
        style.SetCornerRadiusAll(6);
        frame.AddThemeStyleboxOverride("panel", style);
        Place(frame, 44f, 180f, 640f, 530f);
        frame.AddChild(new KeybindPanel(() => ShowPage("settings")));
        return frame;
    }

    private Control BuildGraphicsPage()
    {
        var frame = new PanelContainer();
        var style = new StyleBoxFlat { BgColor = PanelBack, ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 12, ContentMarginBottom = 12 };
        style.SetCornerRadiusAll(6);
        frame.AddThemeStyleboxOverride("panel", style);
        Place(frame, 44f, 176f, 700f, 534f);
        frame.AddChild(_graphics = new GraphicsPanel(() => ShowPage("settings"), 370f));
        return frame;
    }

    private GameplayPanel _gameplay = null!;

    private Control BuildGameplayPage()
    {
        var frame = new PanelContainer();
        var style = new StyleBoxFlat { BgColor = PanelBack, ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 12, ContentMarginBottom = 12 };
        style.SetCornerRadiusAll(6);
        frame.AddThemeStyleboxOverride("panel", style);
        Place(frame, 44f, 186f, 560f, 0f);
        frame.AddChild(_gameplay = new GameplayPanel(() => ShowPage("settings"), 240f));
        return frame;
    }

    private static Control Row(string name, Control control, Label value)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        Label l = MakeLabel(name, 18, Ink);
        l.CustomMinimumSize = new Vector2(170f, 0f);
        control.CustomMinimumSize = new Vector2(200f, 28f);
        control.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(l);
        row.AddChild(control);
        row.AddChild(value);
        return row;
    }

    /// <summary>Right to left, the mute switch sits in a row like the sliders
    /// (name, then the control under the sliders' start) instead of a lone
    /// switch at the far edge. <paramref name="row"/> builds the row with an
    /// empty HBox in the control slot; the switch moves into it, text-less.</summary>
    internal static Control MuteRow(CheckButton mute, Func<string, Control> row)
    {
        string name = mute.Text;
        mute.Text = "";
        mute.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        mute.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        Control r = row(name);
        foreach (Node n in r.GetChildren())
            if (n is HBoxContainer slot) { slot.AddChild(mute); break; }
        return r;
    }

    private static HSlider Slider(double min, double max, double step, double value)
        => new() { MinValue = min, MaxValue = max, Step = step, Value = value, FocusMode = Control.FocusModeEnum.All };

    // ---------------------------------------------------------------- pages

    private void ShowPage(string name)
    {
        // "page:arg" for captures: "codex:secrets" scrolls to the hidden entries,
        // "play:<character id>" opens with that character focused.
        string arg = "";
        int colon = name.IndexOf(':');
        if (colon >= 0) { arg = name[(colon + 1)..]; name = name[..colon]; }
        bool secrets = arg == "secrets";
        if (!_pages.ContainsKey(name)) name = "main";
        if (_page == "boombox" && name != "boombox") _boombox.Closing();
        _page = name;
        foreach (var (key, page) in _pages) page.Visible = key == name;

        // Focus is what makes the page keyboard- and gamepad-usable; every page
        // lands with something focused.
        switch (name)
        {
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
                _runButton.Text = ChallengePicker.Summary();
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

    public override void _ExitTree() => Net.SteamLobby.JoinRequested -= OnSteamJoinRequested;

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
