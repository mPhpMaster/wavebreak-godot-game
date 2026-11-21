using System.Collections.Generic;
using Godot;
using Wavebreak.Game.Puzzle;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The puzzle boons on screen (Game/Puzzle/PuzzleBoons): a banner with the
/// boon's drawn icon, name and what it does each time a solved room gives
/// one (queued when several arrive together), and a small list of the boons
/// held, with their stacks, at the screen's left edge. Lives in the puzzle
/// map's UI layer, so it leaves with the map. Every boon has its own shape
/// (an arrow, a double chevron, an up arrow, a lightning bolt, a heart, a
/// shield), never colour alone.
/// </summary>
public sealed partial class PuzzleBoonHud : Control
{
    public const float ShowSeconds = 3.2f;

    /// <summary>Banners shown since the game started, and the last one's text (checks).</summary>
    public static int Shown { get; private set; }
    public static string LastText { get; private set; } = "";

    private readonly Queue<(PuzzleBoon Boon, bool Gauntlet)> _queue = new();
    private Control _banner = null!;
    private PuzzleBoonIcon _icon = null!;
    private Label _title = null!, _blurb = null!;
    private float _left;
    private VBoxContainer _list = null!;
    private PanelContainer _listPanel = null!;
    private int _listTotal = -1;

    /// <summary>The banner is up (checks).</summary>
    public bool BannerVisible => _banner.Visible;

    public override void _Ready()
    {
        Name = "PuzzleBoonHud";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        ProcessMode = ProcessModeEnum.Always;

        var card = CuteStyle.Panel(CuteStyle.Night, CuteStyle.Mint, 18, 10);
        card.MouseFilter = MouseFilterEnum.Ignore;
        // On the right, under the minimap: clear of the medal banner (top middle),
        // the interact prompt and the room card (bottom middle).
        card.AnchorLeft = 1f; card.AnchorRight = 1f;
        card.AnchorTop = 0.37f; card.AnchorBottom = 0.37f;
        card.OffsetLeft = -404f; card.OffsetRight = -14f;
        card.GrowHorizontal = GrowDirection.Begin;
        card.Visible = false;
        AddChild(card);
        _banner = card;
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 14);
        card.AddChild(row);
        _icon = new PuzzleBoonIcon();
        row.AddChild(_icon);
        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(col);
        _title = CuteStyle.Title("", 24, CuteStyle.OnNight);
        col.AddChild(_title);
        _blurb = CuteStyle.Text("", 15, CuteStyle.OnNightDim);
        _blurb.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(_blurb);
        if (Loc.IsRtl)
        {
            _title.HorizontalAlignment = HorizontalAlignment.Right;
            _blurb.HorizontalAlignment = HorizontalAlignment.Right;
        }

        _listPanel = CuteStyle.Panel(CuteStyle.Night, CuteStyle.Mint, 14, 8);
        _listPanel.MouseFilter = MouseFilterEnum.Ignore;
        _listPanel.AnchorLeft = 0f; _listPanel.AnchorRight = 0f;
        _listPanel.AnchorTop = 0.40f; _listPanel.AnchorBottom = 0.40f;
        _listPanel.OffsetLeft = 14f;
        _listPanel.Visible = false;
        AddChild(_listPanel);
        _list = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _list.AddThemeConstantOverride("separation", 2);
        _listPanel.AddChild(_list);

        PuzzleBoons.Granted += OnGranted;
    }

    public override void _ExitTree() => PuzzleBoons.Granted -= OnGranted;

    private void OnGranted(PuzzleBoon b, bool gauntlet) => _queue.Enqueue((b, gauntlet));

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        bool show = !Net.Coop.IsPaused(GetTree()) && !LevelUpPanel.AnyOpen;
        _left -= dt;
        if (_left <= 0f && _queue.Count > 0)
        {
            (PuzzleBoon b, bool gauntlet) = _queue.Dequeue();
            _icon.Boon = b;
            _icon.QueueRedraw();
            int stacks = PuzzleBoons.Stacks(b);
            _title.Text = Loc.F(PuzzleBoons.BannerTitle, Loc.T(PuzzleBoons.Title(b))) + (stacks > 1 ? $"  x{stacks}" : "");
            _blurb.Text = Loc.T(PuzzleBoons.Blurb(b)) + "\n" + Loc.T(gauntlet ? PuzzleBoons.BannerKeepGauntlet : PuzzleBoons.BannerKeep);
            _left = _queue.Count > 0 ? ShowSeconds * 0.6f : ShowSeconds;
            Shown++;
            LastText = _title.Text;
            Audio.AudioDirector.PlayPuzzle(Audio.PuzzleSound.Bell, 1.8f);
        }
        _banner.Visible = show && _left > 0f;
        _banner.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(_left / 0.5f, 0f, 1f));

        int total = PuzzleBoons.Total;
        if (total != _listTotal) Rebuild();
        _listPanel.Visible = show && total > 0;
    }

    private void Rebuild()
    {
        _listTotal = PuzzleBoons.Total;
        foreach (Node n in _list.GetChildren()) n.QueueFree();
        _list.AddChild(CuteStyle.Text(Loc.T(PuzzleBoons.ListTitle), 14, CuteStyle.Mint));
        foreach (PuzzleBoon b in PuzzleBoons.All)
        {
            int n = PuzzleBoons.Stacks(b);
            if (n == 0) continue;
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 6);
            row.AddChild(new PuzzleBoonIcon { Boon = b, CustomMinimumSize = new Vector2(26f, 26f) });
            row.AddChild(CuteStyle.Text(Loc.T(PuzzleBoons.Title(b)) + (n > 1 ? $" x{n}" : ""), 14, CuteStyle.OnNight));
            _list.AddChild(row);
        }
    }
}

/// <summary>A boon's drawn icon in a ring.</summary>
public partial class PuzzleBoonIcon : Control
{
    public PuzzleBoon Boon;

    public PuzzleBoonIcon()
    {
        CustomMinimumSize = new Vector2(64f, 64f);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        Vector2 c = Size * 0.5f;
        float r = Mathf.Min(Size.X, Size.Y) * 0.46f;
        float w = Mathf.Max(1.5f, r * 0.14f);
        Color ring = CuteStyle.Mint, ink = CuteStyle.OnNight;
        DrawCircle(c, r, new Color(0.08f, 0.10f, 0.16f, 0.9f));
        DrawArc(c, r, 0f, Mathf.Tau, 32, ring, w * 0.8f, true);
        float h = r * 0.55f;
        switch (Boon)
        {
            case PuzzleBoon.Swift:
                // A right arrow with speed lines.
                DrawLine(c + new Vector2(-h, 0f), c + new Vector2(h, 0f), ink, w, true);
                DrawPolyline(new[] { c + new Vector2(h * 0.3f, -h * 0.6f), c + new Vector2(h, 0f), c + new Vector2(h * 0.3f, h * 0.6f) }, ink, w, true);
                DrawLine(c + new Vector2(-h, -h * 0.55f), c + new Vector2(-h * 0.2f, -h * 0.55f), ink, w * 0.6f, true);
                DrawLine(c + new Vector2(-h, h * 0.55f), c + new Vector2(-h * 0.2f, h * 0.55f), ink, w * 0.6f, true);
                break;
            case PuzzleBoon.Spring:
                // Two chevrons up.
                for (int k = 0; k < 2; k++)
                {
                    float y = h * (0.45f - k * 0.75f);
                    DrawPolyline(new[] { c + new Vector2(-h * 0.7f, y + h * 0.4f), c + new Vector2(0f, y - h * 0.3f), c + new Vector2(h * 0.7f, y + h * 0.4f) }, ink, w, true);
                }
                break;
            case PuzzleBoon.Leap:
                // An up arrow over a line (the ground).
                DrawLine(c + new Vector2(0f, h * 0.7f), c + new Vector2(0f, -h), ink, w, true);
                DrawPolyline(new[] { c + new Vector2(-h * 0.55f, -h * 0.4f), c + new Vector2(0f, -h), c + new Vector2(h * 0.55f, -h * 0.4f) }, ink, w, true);
                DrawLine(c + new Vector2(-h, h), c + new Vector2(h, h), ink, w * 0.7f, true);
                break;
            case PuzzleBoon.Dash:
                // A lightning bolt.
                DrawPolyline(new[] { c + new Vector2(h * 0.35f, -h), c + new Vector2(-h * 0.4f, h * 0.1f), c + new Vector2(h * 0.3f, h * 0.1f), c + new Vector2(-h * 0.35f, h) }, ink, w, true);
                break;
            case PuzzleBoon.Vigor:
                // A heart with a plus.
                DrawCircle(c + new Vector2(-h * 0.38f, -h * 0.2f), h * 0.42f, ink);
                DrawCircle(c + new Vector2(h * 0.38f, -h * 0.2f), h * 0.42f, ink);
                DrawColoredPolygon(new[] { c + new Vector2(-h * 0.78f, -h * 0.05f), c + new Vector2(h * 0.78f, -h * 0.05f), c + new Vector2(0f, h * 0.85f) }, ink);
                DrawLine(c + new Vector2(-h * 0.25f, -h * 0.05f), c + new Vector2(h * 0.25f, -h * 0.05f), CuteStyle.Coral, w * 0.8f);
                DrawLine(c + new Vector2(0f, -h * 0.3f), c + new Vector2(0f, h * 0.2f), CuteStyle.Coral, w * 0.8f);
                break;
            default:
                // A shield.
                DrawPolyline(new[] { c + new Vector2(-h * 0.75f, -h * 0.8f), c + new Vector2(h * 0.75f, -h * 0.8f), c + new Vector2(h * 0.75f, 0f), c + new Vector2(0f, h), c + new Vector2(-h * 0.75f, 0f), c + new Vector2(-h * 0.75f, -h * 0.8f) }, ink, w, true);
                DrawLine(c + new Vector2(0f, -h * 0.8f), c + new Vector2(0f, h * 0.9f), ink, w * 0.6f, true);
                break;
        }
    }
}

