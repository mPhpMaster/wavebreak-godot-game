using Godot;
using Wavebreak.Core;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The results screen's leaderboard line: when a finished run makes the local
/// top ten, a strip under the results panel names its rank and repeats the
/// entry, highlighted. Its own node on the meta canvas layer rather than a
/// part of <see cref="ResultsScreen"/>, so the results screen does not need to
/// know the leaderboard exists — and because the run-end handler that raises
/// the results runs before the one that records the run.
/// </summary>
public sealed partial class LeaderboardBanner : Control
{
    private static readonly Color Back = new(0.06f, 0.07f, 0.11f, 0.94f);
    private static readonly Color Gold = new(1.00f, 0.84f, 0.38f);
    private static readonly Color Ink = new(0.93f, 0.95f, 0.99f);

    private PanelContainer _panel = null!;
    private Label _head = null!;
    private Label _row = null!;

    public override void _Ready()
    {
        Name = "LeaderboardBanner";
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var style = new StyleBoxFlat { BgColor = Back, BorderColor = Gold, ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 6, ContentMarginBottom = 6 };
        style.SetBorderWidthAll(0);
        style.BorderWidthLeft = 4;
        style.SetCornerRadiusAll(5);
        _panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _panel.AddThemeStyleboxOverride("panel", style);
        _panel.AnchorLeft = 0.5f; _panel.AnchorRight = 0.5f;
        _panel.AnchorTop = 1f; _panel.AnchorBottom = 1f;
        _panel.OffsetLeft = -330f; _panel.OffsetRight = 330f;
        _panel.OffsetTop = -74f; _panel.OffsetBottom = -10f;
        AddChild(_panel);

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 0);
        _panel.AddChild(box);
        _head = MainMenu.MakeLabel("", 18, Gold);
        _row = MainMenu.MakeLabel("", 15, Ink);
        _head.HorizontalAlignment = HorizontalAlignment.Center;
        _row.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_head);
        box.AddChild(_row);

        EventBus.Subscribe<LeaderboardPlaced>(OnPlaced);
    }

    public override void _ExitTree() => EventBus.Unsubscribe<LeaderboardPlaced>(OnPlaced);

    private void OnPlaced(LeaderboardPlaced e)
    {
        if (e.Rank <= 0) return;
        LeaderboardEntry? entry = null;
        foreach (LeaderboardEntry x in Leaderboard.Top) if (x.When == e.When) entry = x;
        if (entry is null) return;

        _head.Text = e.Rank == 1 ? Loc.T("NEW BEST RUN  ·  #1 ON THE LEADERBOARD") : Loc.F("NEW LEADERBOARD ENTRY  ·  #{0} OF {1}", e.Rank, Leaderboard.Size);
        _row.Text = LeaderboardPage.Describe(entry);
        _panel.Visible = true;
    }
}
