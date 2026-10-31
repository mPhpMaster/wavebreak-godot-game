using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;
using Wavebreak.Meta;
using Wavebreak.Progression;

namespace Wavebreak.UI;

/// <summary>
/// A first Classic run's short tour: a card above the dash meter with one thing
/// to try at a time, moving on when the player has done it or the moment has
/// passed. Nothing waits for it, and F2 or the pad's help button ends it.
///
/// Shown once: the profile records it as soon as it starts, so quitting half way
/// does not bring it back. Settings > Accessibility can ask for it again. Never
/// in co-op, on an expedition (<see cref="TraversalPractice"/> has those) or in
/// an automated run, where it would sit over every capture.
/// </summary>
public sealed partial class ClassicTutorial : Control
{
    /// <summary>Profile meta: non-zero once the tour has played.</summary>
    public const string SeenKey = "classic_tutorial_seen";

    private const float WalkMetres = 6f;

    private delegate string Text();

    /// <summary>One thing to try: its line, what ends it, and how long it stays
    /// at most if the player does something else instead.</summary>
    private readonly record struct Step(Text Line, System.Func<bool> Done, float Seconds);

    private Step[] _steps = null!;
    private int _index;
    private float _inStep;
    private Vector3 _from;
    private float _walked;
    private int _startJumps, _startDashes, _startKills, _startChests;
    private bool _skipHeld, _toggleHeld;
    private PanelContainer _panel = null!;
    private Label _count = null!;
    private Label _line = null!;
    private Label _skip = null!;
    private int _shownStep = -1;
    private int _hints = -1;

    /// <summary>--check-tutorial: the step on screen, or -1 once finished.</summary>
    internal int StepIndex => _index < _steps.Length ? _index : -1;

    internal string LineText => _line.Text;

    internal string SkipText => _skip.Text;

    public static bool Eligible(bool automated, bool coop, string? route, long seen)
        => !automated && !coop && route == "classic" && seen == 0;

    public static bool ShouldShow()
    {
        Profile.EnsureLoaded();
        return Eligible(Launch.Automated, Net.Coop.Active, Routes.Active?.Id, Profile.GetMeta(SeenKey));
    }

    /// <summary>Settings: play it again on the next Classic run.</summary>
    public static void Replay()
    {
        Profile.SetMeta(SeenKey, 0);
        Profile.Save();
    }

    public override void _Ready()
    {
        Name = "ClassicTutorial";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Profile.SetMeta(SeenKey, 1);
        Profile.Save();

        _panel = CuteStyle.Panel(CuteStyle.Cream, CuteStyle.Butter, 18, 10);
        _panel.AnchorLeft = 0.5f; _panel.AnchorRight = 0.5f;
        _panel.AnchorTop = 1f; _panel.AnchorBottom = 1f;
        _panel.OffsetLeft = -300f; _panel.OffsetRight = 300f;
        _panel.OffsetTop = -96f; _panel.OffsetBottom = -96f;
        _panel.GrowHorizontal = GrowDirection.Both;
        _panel.GrowVertical = GrowDirection.Begin;
        _panel.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_panel);

        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 2);
        _panel.AddChild(col);
        _count = CuteStyle.Text("", 14, CuteStyle.InkSoft, HorizontalAlignment.Center);
        col.AddChild(_count);
        _line = CuteStyle.Text("", 19, CuteStyle.Ink, HorizontalAlignment.Center);
        _line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(_line);
        _skip = CuteStyle.Text("", 13, CuteStyle.InkSoft, HorizontalAlignment.Center);
        col.AddChild(_skip);

        Player? player = Player.Instance;
        _from = player?.GlobalPosition ?? Vector3.Zero;
        _startJumps = player?.Jumps ?? 0;
        _startDashes = player?.Dashes ?? 0;
        _startKills = Main.Instance?.Run.Kills ?? 0;
        _startChests = ChestSpawner.Instance?.Opened ?? 0;

        _steps = new[]
        {
            new Step(MoveLine, () => _walked >= WalkMetres, 20f),
            new Step(() => Loc.T("Your weapons aim and fire by themselves. Just stay close enough."),
                     () => (Main.Instance?.Run.Kills ?? 0) > _startKills, 12f),
            new Step(() => Loc.T("Enemies drop XP orbs. Walk over them to fill the bar and pick a card each level."),
                     () => (Inventory.Current?.Level ?? 1) > 1, 45f),
            new Step(() => Loc.F("Chests hold upgrades for gold. Stand beside one and press {0}.", Keybinds.Hint("interact")),
                     () => (ChestSpawner.Instance?.Opened ?? 0) > _startChests, 25f),
            new Step(() => Loc.F("{0} jumps and {1} dashes. Dash through trouble.", Keybinds.Hint("jump"), Keybinds.Hint("dash")),
                     () => (player?.Jumps ?? 0) > _startJumps && (player?.Dashes ?? 0) > _startDashes, 25f),
            new Step(() => Loc.T("Beat the boss and a portal opens. Step into it to reach the next map."),
                     () => false, 12f),
        };
    }

    private static string MoveLine()
    {
        if (PadInput.UsingPad)
            return Loc.T("Left stick moves, right stick looks around. Try a walk.");
        string keys = $"{Keybinds.Hint("move_forward")} {Keybinds.Hint("move_left")} {Keybinds.Hint("move_back")} {Keybinds.Hint("move_right")}";
        return Loc.F("{0} moves, the mouse looks around. Try a walk.", Loc.Ltr(keys));
    }

    private bool SkipPressed()
    {
        bool skip = Input.IsPhysicalKeyPressed(Key.F2);
        bool toggle = Input.IsActionPressed("help_toggle") && PadInput.PlayHasPad;
        bool pressed = (skip && !_skipHeld) || (toggle && !_toggleHeld);
        _skipHeld = skip;
        _toggleHeld = toggle;
        return pressed;
    }

    public override void _Process(double delta)
    {
        if (_index >= _steps.Length) return;
        if (SkipPressed())
        {
            Finish();
            return;
        }

        // Out of the way of whatever owns the game right now.
        bool busy = Net.Coop.IsPaused(GetTree()) || LevelUpPanel.AnyOpen || LevelUpPanel.AnyCompactOpen
                    || Main.Instance?.Stages is { InRestRoom: true };
        _panel.Visible = !busy;
        if (busy) return;

        if (Player.Instance is { } player && IsInstanceValid(player))
        {
            _walked += _from.DistanceTo(player.GlobalPosition);
            _from = player.GlobalPosition;
        }

        float dt = (float)delta;
        _inStep += dt;
        Step step = _steps[_index];
        if (step.Done() || _inStep >= step.Seconds)
        {
            _index++;
            _inStep = 0f;
            if (_index >= _steps.Length) { Finish(); return; }
        }

        if (_index == _shownStep && Hints.Stamp == _hints) return;
        _shownStep = _index;
        _hints = Hints.Stamp;
        _count.Text = Loc.F("Tip {0}/{1}", _index + 1, _steps.Length);
        _line.Text = _steps[_index].Line();
        _skip.Text = PadInput.UsingPad ? Loc.F("{0}: skip tutorial", Keybinds.Hint("help_toggle")) : Loc.T("F2: skip tutorial");
    }

    private void Finish()
    {
        _index = _steps.Length;
        Visible = false;
    }
}
