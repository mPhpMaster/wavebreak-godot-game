using Godot;
using Wavebreak.Core;
using Wavebreak.Game;
using Wavebreak.Game.Puzzle;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// A puzzle map's card at the bottom of the screen: which room of how many,
/// what to do there, the puzzle clock and when the SKIP portal opens, the hint
/// key (named for the device in use, <see cref="Keybinds.Hint"/>), the gust
/// timing in a wind room, the bells struck in a bell room, and in co-op how
/// many stand in an exit. Above it, the interact prompt for the piece in
/// reach; in the middle, the medal banner when the last room is solved.
///
/// Every state is said in words and shapes as well as colour: the medal is a
/// star, a diamond, a disc or a square with its name under it.
/// Owned by the <see cref="PuzzleMap"/>, so it leaves with the map.
/// </summary>
public sealed partial class PuzzleHud : Control
{
    private readonly PuzzleMap _map;
    private PanelContainer _panel = null!;
    private Label _room = null!, _objective = null!, _clock = null!, _hint = null!, _extra = null!, _team = null!;
    private PanelContainer _promptPanel = null!;
    private Label _prompt = null!;
    private Control _banner = null!;
    private Medal _medal = null!;
    private Label _bannerTier = null!, _bannerLine = null!;
    private float _bannerFor;

    public PuzzleHud() : this(null!) { }

    public PuzzleHud(PuzzleMap map) => _map = map;

    /// <summary>The banner is up (captures and checks).</summary>
    public bool BannerVisible => _banner.Visible;

    public override void _Ready()
    {
        Name = "PuzzleHud";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        ProcessMode = ProcessModeEnum.Always;

        _panel = CuteStyle.Panel(CuteStyle.Cream, CuteStyle.Seafoam, 18, 10);
        _panel.AnchorLeft = 0.5f; _panel.AnchorRight = 0.5f;
        _panel.AnchorTop = 1f; _panel.AnchorBottom = 1f;
        _panel.OffsetLeft = -320f; _panel.OffsetRight = 320f;
        _panel.OffsetTop = -96f; _panel.OffsetBottom = -96f;
        _panel.GrowHorizontal = GrowDirection.Both;
        _panel.GrowVertical = GrowDirection.Begin;
        _panel.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_panel);
        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 1);
        _panel.AddChild(col);
        // The puzzle's name is on the phase line at the top; this card is the room.
        _room = Line(col, 19, CuteStyle.Ink);
        _objective = Line(col, 15, CuteStyle.InkSoft);
        _objective.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _extra = Line(col, 16, new Color(0.18f, 0.45f, 0.55f));
        _clock = Line(col, 15, CuteStyle.Ink);
        _hint = Line(col, 13, CuteStyle.InkSoft);
        _team = Line(col, 16, new Color(0.20f, 0.55f, 0.45f));

        _promptPanel = CuteStyle.Panel(CuteStyle.Night, CuteStyle.Butter, 14, 8);
        _promptPanel.AnchorLeft = 0.5f; _promptPanel.AnchorRight = 0.5f;
        _promptPanel.AnchorTop = 1f; _promptPanel.AnchorBottom = 1f;
        _promptPanel.OffsetLeft = -220f; _promptPanel.OffsetRight = 220f;
        _promptPanel.OffsetTop = -330f; _promptPanel.OffsetBottom = -290f;
        _promptPanel.GrowHorizontal = GrowDirection.Both;
        _promptPanel.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_promptPanel);
        _prompt = CuteStyle.Text("", 19, CuteStyle.OnNight, HorizontalAlignment.Center);
        _promptPanel.AddChild(_prompt);

        // The medal banner.
        _banner = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _banner.AnchorLeft = 0.5f; _banner.AnchorRight = 0.5f;
        _banner.AnchorTop = 0.13f; _banner.AnchorBottom = 0.13f;
        _banner.OffsetLeft = -320f; _banner.OffsetRight = 320f;
        _banner.GrowHorizontal = GrowDirection.Both;
        AddChild(_banner);
        _medal = new Medal { CustomMinimumSize = new Vector2(0f, 76f), MouseFilter = MouseFilterEnum.Ignore };
        _banner.AddChild(_medal);
        var card = CuteStyle.Panel(CuteStyle.Night, CuteStyle.Butter, 18, 12);
        card.MouseFilter = MouseFilterEnum.Ignore;
        _banner.AddChild(card);
        var inner = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        card.AddChild(inner);
        _bannerTier = CuteStyle.Title("", 32, CuteStyle.OnNight, HorizontalAlignment.Center);
        inner.AddChild(_bannerTier);
        _bannerLine = CuteStyle.Text("", 16, CuteStyle.OnNightDim, HorizontalAlignment.Center);
        _bannerLine.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        inner.AddChild(_bannerLine);

        EventBus.Subscribe<PuzzleFinished>(OnFinished);
    }

    public override void _ExitTree() => EventBus.Unsubscribe<PuzzleFinished>(OnFinished);

    private static Label Line(VBoxContainer col, int size, Color color)
    {
        Label l = CuteStyle.Text("", size, color, HorizontalAlignment.Center);
        col.AddChild(l);
        return l;
    }

    private void OnFinished(PuzzleFinished e)
    {
        var tier = (RewardTier)e.Tier;
        _medal.Tier = tier;
        _medal.QueueRedraw();
        _bannerTier.Text = Loc.T(PuzzleText.TierName(tier));
        _bannerLine.Text = e.Skipped
            ? Loc.T("Skipped. A small consolation is on its way.")
            : Loc.F("Solved in {0}  ·  hints: {1}", RunState.Format(e.Seconds), e.Hints) + "\n" + Loc.T("Claim one reward in the vault, then take the exit.");
        _banner.Visible = true;
        _banner.Modulate = Colors.White;
        _bannerFor = e.Skipped ? 3f : 4.5f;
    }

    public override void _Process(double delta)
    {
        if (_map is null || !IsInstanceValid(_map)) return;
        float dt = (float)delta;
        bool show = !Net.Coop.IsPaused(GetTree()) && !LevelUpPanel.AnyOpen;
        _panel.Visible = show;
        _bannerFor -= dt;
        // Small and short-lived, then fading: it sits over the vault's prize labels.
        if (_bannerFor <= 0f) _banner.Visible = false;
        else if (_bannerFor < 0.8f) _banner.Modulate = new Color(1f, 1f, 1f, _bannerFor / 0.8f);

        PuzzleState st = _map.State;
        PuzzlePlan plan = _map.Plan;
        int r = _map.FocusRoom;
        RoomPlan room = plan.Rooms[Mathf.Clamp(r, 0, plan.Rooms.Count - 1)];

        _room.Text = room.Kind switch
        {
            RoomKind.Reward => Loc.T(PuzzleText.Title(RoomKind.Reward)),
            _ => Loc.F("Room {0}/{1}  ·  {2}  ·  solved {3}/{1}", Mathf.Clamp(room.Index, 1, plan.PuzzleRooms), plan.PuzzleRooms,
                       Loc.T(PuzzleText.Title(room.Kind)), st.SolvedCount),
        };
        bool assist = st.Get(room, PuzzleState.RAssist) != 0;
        _objective.Text = st.Finished && room.Kind != RoomKind.Reward
            ? Loc.T(PuzzleText.Objective(RoomKind.Reward, false))
            : Loc.T(PuzzleText.Objective(room.Kind, assist));

        _extra.Text = Extra(room, st);
        _extra.Visible = _extra.Text.Length > 0;

        if (st.Finished)
            _clock.Text = Loc.F("{0}  ·  solved in {1}", Loc.T(PuzzleText.TierName(st.Tier)), RunState.Format(st.SolveSeconds));
        else if (st.SkipOpen)
            _clock.Text = Loc.F("Time {0}  ·  SKIP PORTAL OPEN (lowest reward)", RunState.Format(_map.Elapsed));
        else
            _clock.Text = Loc.F("Time {0}  ·  gold under {1}  ·  skip portal in {2}", RunState.Format(_map.Elapsed),
                                RunState.Format(plan.GoldSeconds), RunState.Format(Mathf.Max(0f, plan.SkipSeconds - _map.Elapsed)));

        // Keybinds.Hint follows a rebind and the device in use, so this is rebuilt each frame.
        _hint.Text = st.Finished ? "" : _map.HintCooldown > 0f
            ? Loc.F("Hints used: {0}  ·  next hint in {1}s", st.Hints, Mathf.CeilToInt(_map.HintCooldown))
            : Loc.F("{0}: hint (costs the gold medal)  ·  used: {1}", Keybinds.Hint("help_toggle"), st.Hints);
        _hint.Visible = _hint.Text.Length > 0;

        if (PuzzleMap.Inside >= 0)
        {
            string text = Loc.F("In an exit: {0}/{1}", PuzzleMap.Inside, Mathf.Max(1, PuzzleMap.Total));
            if (PuzzleMap.Countdown >= 0f) text += "   " + Loc.F("leaving in {0}s", Mathf.CeilToInt(PuzzleMap.Countdown));
            _team.Text = text;
            _team.Visible = true;
        }
        else _team.Visible = false;

        PuzzleFocus? f = _map.Focus;
        _promptPanel.Visible = show && f is not null;
        if (f is { } focus) _prompt.Text = $"[{Keybinds.Hint("interact")}]  {(focus.Claim ? focus.Verb : Loc.T(focus.Verb))}";
    }

    /// <summary>The line that changes with the room: gusts, bells, lamps, latches.</summary>
    private string Extra(RoomPlan room, PuzzleState st)
    {
        if (st.Get(room, PuzzleState.RSolved) != 0 && room.Kind is not (RoomKind.Start or RoomKind.Reward)) return Loc.T("SOLVED");
        float t = _map.Elapsed;
        switch (room.Kind)
        {
            case RoomKind.Wind:
            {
                float cycle = PuzzleGen.WindCycle(room, 0, t);
                string touched = Loc.F("plates {0}/{1}", PuzzleSolver.PopCount(st.Get(room, PuzzleState.RA)), room.WindCentres.Length);
                return cycle >= room.Calm
                    ? Loc.F("GUST  ·  calm in {0}s", (room.Calm + room.Gust - cycle).ToString("0.0")) + "  ·  " + touched
                    : Loc.F("CALM  ·  gust in {0}s", (room.Calm - cycle).ToString("0.0")) + "  ·  " + touched;
            }
            case RoomKind.Runes:
                return st.RuneShowing(room, t, out _) ? Loc.T("Listen and watch the order...")
                    : Loc.F("Bells struck in order: {0}/{1}", st.Get(room, PuzzleState.RA), room.Sequence.Length);
            case RoomKind.Levers:
            {
                int lit = PuzzleSolver.PopCount(PuzzleSolver.Lamps(room, st.Get(room, PuzzleState.RA)));
                return Loc.F("Lamps lit: {0}/{1}", lit, room.Lamps.Length);
            }
            case RoomKind.Crates:
                return Loc.F("Plates held: {0}/{1}", PuzzleSolver.PopCount(st.Get(room, PuzzleState.RPressed)), room.Plates.Length);
            case RoomKind.Mirrors:
                return Loc.T("Stand by a mirror to turn it.");
            case RoomKind.Twin:
            {
                string a = st.TwinHeld(room, 0, t) ? Loc.T("A held") : Loc.T("A free");
                string b = st.TwinHeld(room, 1, t) ? Loc.T("B held") : Loc.T("B free");
                return $"{a}  ·  {b}";
            }
        }
        return "";
    }

    /// <summary>The medal's shape: star (gold), diamond (silver), disc
    /// (bronze), square (skipped).</summary>
    private sealed partial class Medal : Control
    {
        public RewardTier Tier;

        public override void _Draw()
        {
            Vector2 c = new(Size.X * 0.5f, Size.Y * 0.5f);
            float r = Mathf.Min(Size.Y * 0.45f, 54f);
            Color fill = Tier switch
            {
                RewardTier.Gold => new Color(1f, 0.82f, 0.30f),
                RewardTier.Silver => new Color(0.82f, 0.86f, 0.92f),
                RewardTier.Bronze => new Color(0.82f, 0.56f, 0.34f),
                _ => new Color(0.6f, 0.6f, 0.66f),
            };
            Color edge = fill.Darkened(0.45f);
            Vector2[] pts;
            switch (Tier)
            {
                case RewardTier.Gold:
                    pts = new Vector2[10];
                    for (int i = 0; i < 10; i++)
                    {
                        float a = -Mathf.Pi * 0.5f + Mathf.Tau * i / 10f;
                        float rr = i % 2 == 0 ? r : r * 0.45f;
                        pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                    }
                    break;
                case RewardTier.Silver:
                    pts = new[] { c + new Vector2(0, -r), c + new Vector2(r * 0.75f, 0), c + new Vector2(0, r), c + new Vector2(-r * 0.75f, 0) };
                    break;
                case RewardTier.Bronze:
                    pts = new Vector2[24];
                    for (int i = 0; i < 24; i++) pts[i] = c + new Vector2(Mathf.Cos(Mathf.Tau * i / 24f), Mathf.Sin(Mathf.Tau * i / 24f)) * r * 0.85f;
                    break;
                default:
                    float h = r * 0.7f;
                    pts = new[] { c + new Vector2(-h, -h), c + new Vector2(h, -h), c + new Vector2(h, h), c + new Vector2(-h, h) };
                    break;
            }
            DrawColoredPolygon(pts, fill);
            var loop = new Vector2[pts.Length + 1];
            pts.CopyTo(loop, 0);
            loop[^1] = pts[0];
            DrawPolyline(loop, edge, 4f, true);
        }
    }
}
