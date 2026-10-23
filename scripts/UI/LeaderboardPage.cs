using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The main menu's leaderboard page: the local top ten as a table, with the
/// entry from the most recent run this session highlighted. Read-only; it
/// rebuilds from <see cref="Leaderboard.Top"/> whenever the page opens.
/// </summary>
public sealed partial class LeaderboardPage : Control
{
    private static readonly Color Ink = new(0.93f, 0.95f, 0.99f);
    private static readonly Color InkDim = new(0.62f, 0.68f, 0.78f);
    private static readonly Color Gold = new(1.00f, 0.84f, 0.38f);
    private static readonly Color Victory = new(0.45f, 0.92f, 0.58f);
    private static readonly Color Defeat = new(1.00f, 0.45f, 0.42f);

    private static readonly string[] Headings = { "#", "RESULT", "TIME", "KILLS", "LEVEL", "PHASE", "CHARACTER", "DIFFICULTY", "DATE" };

    private GridContainer _grid = null!;
    private Label _empty = null!;

    /// <summary>"WON  14:02  ·  1,204 kills  ·  level 31  ·  phase 3  ·  Kiln Sister".</summary>
    public static string Describe(LeaderboardEntry e)
    {
        string s = Loc.F("{0}  {1}  ·  {2:N0} kills  ·  level {3}", e.Won ? "WON" : "FELL", e.Clock, e.Kills, e.Level);
        if (e.Phase > 0) s += "  ·  " + Loc.F("phase {0}", e.Phase);
        s += "  ·  " + Loc.T(e.CharacterName);
        if (e.Difficulty.Length > 0) s += "  ·  " + Loc.T(e.Difficulty);
        return s;
    }

    public override void _Ready()
    {
        var frame = new PanelContainer();
        var style = new StyleBoxFlat { BgColor = new Color(0.05f, 0.06f, 0.10f, 0.86f), ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 16, ContentMarginBottom = 16 };
        style.SetCornerRadiusAll(6);
        frame.AddThemeStyleboxOverride("panel", style);
        frame.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(frame);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        frame.AddChild(col);
        col.AddChild(MainMenu.MakeLabel("LEADERBOARD  ·  best ten runs on this machine", 18, InkDim));

        _grid = new GridContainer { Columns = Headings.Length };
        _grid.AddThemeConstantOverride("h_separation", 26);
        _grid.AddThemeConstantOverride("v_separation", 6);
        col.AddChild(_grid);

        _empty = MainMenu.MakeLabel("No finished runs yet. Die gloriously or win outright to get on the board.", 16, InkDim);
        col.AddChild(_empty);
        Refresh();
    }

    public void Refresh()
    {
        if (_grid is null) return;
        foreach (Node c in _grid.GetChildren()) { _grid.RemoveChild(c); c.QueueFree(); }

        foreach (string h in Headings) _grid.AddChild(MainMenu.MakeLabel(h, 13, InkDim));

        int rank = 0;
        foreach (LeaderboardEntry e in Leaderboard.Top)
        {
            rank++;
            bool latest = e.When == Leaderboard.LastRecorded;
            Color ink = latest ? Gold : Ink;
            int size = latest ? 19 : 17;
            Cell(latest ? $"{rank}  NEW" : rank.ToString(), ink, size);
            Cell(e.Won ? "WON" : "FELL", e.Won ? Victory : Defeat, size);
            Cell(e.Clock, ink, size);
            Cell(e.Kills.ToString("N0"), ink, size);
            Cell(e.Level.ToString(), ink, size);
            Cell(e.Phase > 0 ? e.Phase.ToString() : "-", ink, size);
            Cell(e.CharacterName, ink, size);
            Cell(e.Difficulty.Length > 0 ? e.Difficulty : "-", ink, size);
            Cell(e.DateText, latest ? Gold : InkDim, 14);
        }
        _empty.Visible = rank == 0;
    }

    private void Cell(string text, Color color, int size) => _grid.AddChild(MainMenu.MakeLabel(text, size, color));
}
