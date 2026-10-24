using System.Collections.Generic;
using Godot;
using Wavebreak.Game;
using Wavebreak.Meta;
using Wavebreak.Net;

namespace Wavebreak.UI;

/// <summary>
/// The scoreboard's compact table for the results screen's scoreboard card:
/// one row per player (kills, boss kills, damage dealt, downs, revives),
/// refreshed twice a second so the host's last numbers still land after the
/// screen is up. The full table is on the Y key (<see cref="ScoreboardPanel"/>).
/// </summary>
public sealed partial class ScoreboardRows : GridContainer
{
    private static readonly Color Dim = new(0.70f, 0.76f, 0.86f);
    private static readonly Color Ink = new(0.95f, 0.97f, 1.00f);
    // Downs and revives only mean something with a team.
    private static string[] Heads => Coop.Active
        ? new[] { "Player", "Kills", "Bosses", "Damage", "Downs", "Revives" }
        : new[] { "Player", "Kills", "Bosses", "Damage", "Damage taken" };
    private int _cols;

    private float _refresh;
    private readonly List<Label[]> _rows = new();

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Ignore;
        string[] heads = Heads;
        _cols = heads.Length;
        Columns = _cols;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("h_separation", 16);
        AddThemeConstantOverride("v_separation", 4);
        for (int i = 0; i < _cols; i++)
        {
            Label h = ScoreboardPanel.MakeLabel(Loc.T(heads[i]), 13, Dim, i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right);
            if (i == 0) h.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            AddChild(h);
        }
        Refresh();
    }

    public override void _Process(double delta)
    {
        _refresh -= (float)delta;
        if (_refresh > 0f) return;
        _refresh = 0.5f;
        Refresh();
    }

    private void Refresh()
    {
        RunScoreboard.RefreshLocal();
        IReadOnlyList<ScoreLine> lines = RunScoreboard.All;
        for (int i = 0; i < lines.Count; i++)
        {
            if (i >= _rows.Count)
            {
                var row = new Label[_cols];
                for (int c = 0; c < _cols; c++)
                {
                    row[c] = ScoreboardPanel.MakeLabel("", c == 0 ? 17 : 18, Ink, c == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right);
                    if (c == 0)
                    {
                        row[c].SizeFlagsHorizontal = SizeFlags.ExpandFill;
                        row[c].TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                        row[c].CustomMinimumSize = new Vector2(110f, 0f);
                    }
                    AddChild(row[c]);
                }
                _rows.Add(row);
            }
            ScoreLine s = lines[i];
            Label[] r = _rows[i];
            r[0].Text = s.Id == Coop.LocalId && Coop.Active ? Loc.F("{0} (you)", s.Name) : s.Name;
            r[0].AddThemeColorOverride("font_color", Coop.Active ? Coop.SlotColor(s.Slot) : Ink);
            r[1].Text = Loc.Ltr($"{s.Kills}");
            r[2].Text = Loc.Ltr($"{s.BossKills}");
            r[3].Text = Loc.Ltr(RunScoreboard.Short(s.DamageDealt));
            if (_cols == 6)
            {
                r[4].Text = Loc.Ltr($"{s.Downs}");
                r[5].Text = Loc.Ltr($"{s.Revives}");
            }
            else r[4].Text = Loc.Ltr(RunScoreboard.Short(s.DamageTaken));
            foreach (Label l in r) l.Visible = true;
        }
        for (int i = lines.Count; i < _rows.Count; i++)
            foreach (Label l in _rows[i]) l.Visible = false;
    }
}
