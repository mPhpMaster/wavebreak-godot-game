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
    private readonly List<(Node3D node, float radius, float speed, float phase)> _drifters = new();
    private Node3D _hero = null!;
    private string? _heroLook;

    private Control _root = null!;
    private readonly Dictionary<string, Control> _pages = new();
    private string _page = "main";
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

    // settings page
    private readonly List<(HSlider slider, VolumeChannel channel)> _volumeSliders = new();
    private CheckButton _mute = null!;
    private Codex _codex = null!;

    // pearl shop / difficulty (UI/MetaShopPage, UI/DifficultyPicker)
    private MetaShopPage _shop = null!;
    private DifficultyPicker _difficulty = null!;
    private ChallengePicker _runPicker = null!;
    private TogglerPage _toggler = null!;
    private Button _runButton = null!;
    private Label _pearls = null!;

    public override void _Ready()
    {
        Launch.Parse();
        if (Launch.SkipsMenu)
        {
            // Deferred: changing scene from inside the outgoing scene's _Ready is
            // the case deferred calls exist for.
            GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, RunScene);
            return;
        }

        Profile.EnsureLoaded();
        GameSettings.Load();
        Keybinds.Load();
        GameSettings.ApplyWindow();
        AudioSettings.Load();
        AudioSettings.Apply();
        MusicDirector.PlayTitle(this);

        // A results screen or a run may have left the tree paused or the mouse
        // captured; the menu needs neither.
        GetTree().Paused = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;

        BuildBackdrop();
        BuildUi();
        ShowPage(Launch.MenuShot is not null ? Launch.MenuPage : "main");

        if (Launch.MenuShot is not null) _ = CaptureAndQuit();
    }

    // ============================================================ backdrop

    /// <summary>A miniature of the arena: disc, rings, scatter, a lit figure in
    /// the middle and a slow tide of silhouettes circling it. Primitives only —
    /// the run's art can replace this without the menu changing shape.</summary>
    private void BuildBackdrop()
    {
        var env = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial
                {
                    SkyTopColor = new Color(0.06f, 0.07f, 0.14f),
                    SkyHorizonColor = new Color(0.30f, 0.22f, 0.40f),
                    GroundHorizonColor = new Color(0.10f, 0.10f, 0.15f),
                    GroundBottomColor = new Color(0.04f, 0.04f, 0.07f),
                } },
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
                AmbientLightEnergy = 0.55f,
                GlowEnabled = true,
                GlowIntensity = 0.8f,
                FogEnabled = true,
                FogLightColor = new Color(0.20f, 0.18f, 0.30f),
                FogDensity = 0.012f,
            },
        };
        AddChild(env);
        AddChild(new DirectionalLight3D
        {
            Rotation = new Vector3(Mathf.DegToRad(-50f), Mathf.DegToRad(30f), 0f),
            LightEnergy = 1.0f,
            ShadowEnabled = true,
        });

        const float radius = 24f;
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 1f, RadialSegments = 64 },
            Position = new Vector3(0f, -0.5f, 0f),
            MaterialOverride = Game.Arena.Flat(new Color(0.15f, 0.17f, 0.22f)),
        });
        for (int r = 6; r < radius; r += 6)
            AddChild(new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = r - 0.1f, OuterRadius = r + 0.1f, RingSegments = 64 },
                Position = new Vector3(0f, 0.01f, 0f),
                MaterialOverride = Game.Arena.Flat(new Color(0.30f, 0.32f, 0.45f), emission: 0.6f),
            });
        AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = radius - 0.4f, OuterRadius = radius + 0.4f, RingSegments = 64 },
            Position = new Vector3(0f, 0.8f, 0f),
            Scale = new Vector3(1f, 3f, 1f),
            MaterialOverride = Game.Arena.Flat(new Color(0.35f, 0.30f, 0.50f), emission: 0.5f),
        });

        var rng = new RandomNumberGenerator { Seed = 7071 };
        for (int i = 0; i < 26; i++)
        {
            float a = rng.RandfRange(0f, Mathf.Tau);
            float d = rng.RandfRange(7f, radius - 3f);
            float h = rng.RandfRange(0.6f, 3.2f);
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.9f, h, 0.9f) },
                Position = new Vector3(Mathf.Cos(a) * d, h * 0.5f, Mathf.Sin(a) * d),
                Rotation = new Vector3(0f, rng.Randf() * Mathf.Tau, 0f),
                MaterialOverride = Game.Arena.Flat(new Color(0.24f, 0.26f, 0.34f).Lerp(new Color(0.36f, 0.30f, 0.46f), rng.Randf())),
            });
        }

        // The hero: the selected character in its chosen skin, rebuilt whenever
        // the selection changes.
        _hero = new Node3D { Name = "Hero" };
        AddChild(_hero);
        // Its own key light: the arena light is far below the camera, and a
        // character lit only by ambient reads as a silhouette.
        _hero.AddChild(new OmniLight3D { Position = new Vector3(0.8f, 2.4f, -2.2f), LightEnergy = 2.2f, OmniRange = 6f });
        TintHero(Characters.ById(Profile.SelectedCharacter) ?? Characters.Default);
        AddChild(new OmniLight3D { Position = new Vector3(0f, 3f, 0f), LightColor = Accent, LightEnergy = 1.5f, OmniRange = 9f });

        // The tide: silhouettes circling at different radii and speeds.
        var foe = Game.Arena.Flat(new Color(0.75f, 0.28f, 0.30f), emission: 0.35f);
        for (int i = 0; i < 34; i++)
        {
            float size = rng.RandfRange(0.35f, 0.7f);
            var body = new MeshInstance3D
            {
                Mesh = i % 3 == 0 ? new SphereMesh { Radius = size, Height = size * 2f }
                                   : new CapsuleMesh { Radius = size * 0.7f, Height = size * 2.6f },
                MaterialOverride = foe,
            };
            var holder = new Node3D();
            holder.AddChild(body);
            body.Position = new Vector3(0f, size * 1.3f, 0f);
            AddChild(holder);
            _drifters.Add((holder, rng.RandfRange(5f, radius - 4f), rng.RandfRange(0.08f, 0.22f) * (i % 2 == 0 ? 1f : -1f), rng.Randf() * Mathf.Tau));
        }

        _camera = new Camera3D { Fov = 60f, Current = true };
        AddChild(_camera);
        UpdateCamera(0f);
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

    public override void _Process(double delta)
    {
        if (_camera is null) return;
        float dt = (float)delta;
        UpdateCamera(dt);
        foreach (var (node, radius, speed, phase) in _drifters)
        {
            float a = phase + _orbit * speed * 10f;
            float r = radius + Mathf.Sin(_orbit * 2f + phase) * 1.2f;
            node.Position = new Vector3(Mathf.Cos(a) * r, Mathf.Abs(Mathf.Sin(_orbit * 9f + phase)) * 0.25f, Mathf.Sin(a) * r);
        }
    }

    private void UpdateCamera(float dt)
    {
        _orbit += dt * 0.06f;
        var pos = new Vector3(Mathf.Cos(_orbit) * 30f, 13f + Mathf.Sin(_orbit * 0.7f) * 2f, Mathf.Sin(_orbit) * 30f);
        _camera.Position = pos;
        // Look a little to the side of centre so the arena sits to the right of
        // the menu column instead of under it.
        Vector3 right = new Vector3(-Mathf.Sin(_orbit), 0f, Mathf.Cos(_orbit));
        _camera.LookAt(right * 9f + new Vector3(0f, 1f, 0f), Vector3.Up);

        // The hero is held at a fixed spot in the frame, right of the menu and
        // character panels, rather than placed in the world: the camera orbits,
        // and anywhere on the ground it spends half the orbit behind the UI.
        if (_hero is not null)
        {
            Basis b = _camera.GlobalTransform.Basis;
            _hero.Position = pos - b.Z * 7.5f + b.X * 4.75f - b.Y * 1.45f;
            // Models face -Z, so turn by the camera direction plus half a turn
            // to look back at the viewer, angled slightly toward the menu.
            Vector3 toCam = (pos - _hero.Position) with { Y = 0f };
            if (toCam.LengthSquared() > 0.01f)
                _hero.Rotation = new Vector3(0f, Mathf.Atan2(toCam.X, toCam.Z) + Mathf.Pi + 0.35f, 0f);
        }
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
        _pages["shop"] = _shop = new MetaShopPage(() => ShowPage("main"));
        _pages["records"] = BuildLeaderboardPage();
        _pages["run"] = _runPicker = new ChallengePicker(() => ShowPage("play"));
        _pages["toggler"] = _toggler = new TogglerPage(() => ShowPage("shop"));   // UI/TogglerPage
        _shop.OpenToggler = () => ShowPage("toggler");
        foreach (Control p in _pages.Values) _root.AddChild(p);
    }

    private Control BuildMainPage()
    {
        var page = new VBoxContainer();
        Place(page, 60f, 220f, 340f, 380f);
        page.AddThemeConstantOverride("separation", 12);

        _playButton = MenuButton("PLAY", () => ShowPage("play"));
        page.AddChild(_playButton);
        page.AddChild(MenuButton("PEARL SHOP", () => ShowPage("shop")));
        page.AddChild(MenuButton("UNLOCKS & SECRETS", () => ShowPage("codex")));
        page.AddChild(MenuButton("LEADERBOARD", () => ShowPage("records")));
        page.AddChild(MenuButton("SETTINGS", () => ShowPage("settings")));
        page.AddChild(MenuButton("QUIT", () => GetTree().Quit()));

        long best = Profile.Get(Profile.BestSeconds);
        Label stats = MakeLabel(
            $"runs {Profile.Get(Profile.Runs)}   ·   best {best / 60:00}:{best % 60:00}   ·   " +
            $"unlocks {Profile.UnlockedIds.Count}/{Unlocks.All.Count}", 15, InkDim);
        stats.CustomMinimumSize = new Vector2(0f, 60f);
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
            string label = open ? c.DisplayName : hidden ? "???" : $"{c.DisplayName}  ·  locked";
            Button b = MenuButton(label, () => StartRun(c));
            if (!open) b.AddThemeColorOverride("font_color", Locked);
            b.FocusEntered += () => ShowCharacter(c);
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
        Button prev = SkinArrow("<", -1);
        Button next = SkinArrow(">", +1);
        _skinLabel = MakeLabel("", 18, Ink);
        _skinLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _skinLabel.HorizontalAlignment = HorizontalAlignment.Center;
        skinRow.AddChild(prev);
        skinRow.AddChild(_skinLabel);
        skinRow.AddChild(next);
        box.AddChild(skinRow);
        BuildHatRow(box);
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
            _skinLabel.Text = $"skin: {skin.Name}   ({_skinIndex + 1}/{n})";
            _skinLabel.AddThemeColorOverride("font_color", Ink);
        }
        else
        {
            UnlockDef? source = Unlocks.ById(Unlocks.SourceOf(skin.Id));
            string how = source is null ? "" : source.Secret && !Profile.IsUnlocked(source.Id) ? $" — {source.Hint}" : $" — {source.Description}";
            _skinLabel.Text = $"skin: {skin.Name}  ·  locked{how}   ({_skinIndex + 1}/{n})";
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

        _charName.Text = open ? c.DisplayName : hidden ? "???" : c.DisplayName;
        _charLook.Text = $"look: {c.LookId}   ·   id: {c.Id}";
        _charBlurb.Text = hidden ? "Someone else walks these shores. You have not met them yet." : c.Blurb;
        _charStats.Text = hidden ? "" :
            $"Starts with {c.StartingWeaponDef.DisplayName}\n{Characters.Describe(c)}";
        _charPassive.Text = hidden || c.PassiveName.Length == 0 ? "" : $"Passive — {c.PassiveName}: {c.PassiveText}";
        _charLock.Text = open ? "Press to begin the run"
            : hidden ? $"Secret — {source!.Hint}"
            : $"Locked — {source?.Description}";
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
        Place(page, 60f, 200f, 380f, 480f);
        page.AddThemeConstantOverride("separation", 10);

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
        _mute = new CheckButton { Text = "Mute all", ButtonPressed = AudioSettings.Muted };
        _mute.AddThemeFontSizeOverride("font_size", 18);
        _mute.Toggled += on => { if (on != AudioSettings.Muted) AudioSettings.ToggleMute(); };
        page.AddChild(_mute);

        page.AddChild(SectionLabel("CONTROLS & DISPLAY"));
        HSlider sens = Slider(GameSettings.MinSensitivity, GameSettings.MaxSensitivity, GameSettings.SensitivityStep, GameSettings.Sensitivity);
        Label sensValue = MakeLabel($"{GameSettings.Sensitivity:0.00}x", 16, Ink);
        sens.ValueChanged += v => { GameSettings.SetSensitivity((float)v); sensValue.Text = $"{GameSettings.Sensitivity:0.00}x"; };
        page.AddChild(Row("Mouse sensitivity", sens, sensValue));

        var full = new CheckButton { Text = "Fullscreen", ButtonPressed = GameSettings.Fullscreen };
        full.AddThemeFontSizeOverride("font_size", 18);
        full.Toggled += GameSettings.SetFullscreen;
        page.AddChild(full);

        Label keys = MakeLabel("In a run:  - / =  volume   ·   9 / 0  effects   ·   [ / ]  music   ·   M  mute", 14, InkDim);
        page.AddChild(keys);
        page.AddChild(MenuButton("KEY BINDINGS", () => ShowPage("keys")));
        page.AddChild(MenuButton("BACK", () => ShowPage("main")));
        return page;
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
            case "play":
                _runButton.Text = ChallengePicker.Summary();
                int i = Math.Max(0, IndexOfCharacter(arg.Length > 0 ? arg : Profile.SelectedCharacter));
                _charButtons[i].GrabFocus();
                ScrollRosterTo(_charButtons[i]);
                ShowCharacter(Characters.All[i]);
                break;
            case "codex":
                _codex.Refresh();
                if (secrets) _codex.FocusFirstSecret();
                else _codex.FocusFirst();
                break;
            case "settings":
                _volumeSliders[0].slider.GrabFocus();
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
                _pearls.Text = $"{Pearls.Name} {Pearls.Balance}   ·   difficulty {DifficultyTiers.Selected.Name}";
                _playButton.GrabFocus();
                break;
        }
    }

    private static int IndexOfCharacter(string id)
    {
        for (int i = 0; i < Characters.All.Count; i++) if (Characters.All[i].Id == id) return i;
        return -1;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_root is null) return;
        if (_page == "play" && (@event.IsActionPressed("ui_left") || @event.IsActionPressed("ui_right")))
        {
            CycleSkin(@event.IsActionPressed("ui_left") ? -1 : 1);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (KeybindPanel.AnyListening) return;
        if (@event.IsActionPressed("ui_cancel") && _page != "main")
        {
            GetViewport().SetInputAsHandled();
            ShowPage(_page switch { "run" => "play", "toggler" => "shop", _ => "main" });
        }
    }

    // ------------------------------------------------------------- capture

    private async System.Threading.Tasks.Task CaptureAndQuit()
    {
        SceneTree tree = GetTree();
        await ToSignal(tree.CreateTimer(2.5), SceneTreeTimer.SignalName.Timeout);
        await Game.Screenshot.Capture(tree, this, Launch.MenuShot!);
        GD.Print($"MENU page={_page} unlocks={Profile.UnlockedIds.Count}/{Unlocks.All.Count} secrets={Profile.FoundSecrets.Count} " +
                 $"characters={string.Join(",", CharacterStates())}");
        tree.Quit();
    }

    private static IEnumerable<string> CharacterStates()
    {
        foreach (CharacterDef c in Characters.All) yield return $"{c.Id}:{(c.IsAvailable ? "open" : "locked")}";
    }

    // -------------------------------------------------------------- helpers

    internal static Button MenuButton(string text, Action onPressed)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.All, Alignment = HorizontalAlignment.Left };
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
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
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
