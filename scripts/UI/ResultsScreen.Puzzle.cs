using System.Collections.Generic;
using Godot;
using Wavebreak.Game;
using Wavebreak.Game.Puzzle;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The results screen after puzzle maps: a Puzzle Gauntlet run gets its own
/// title, a line of medals (each a shape and a count, not colour alone) and
/// its best score; a puzzle-route run gets one more tile with its medals.
/// </summary>
public sealed partial class ResultsScreen
{
    private static readonly Color MedalGold = new(1f, 0.82f, 0.30f);
    private static readonly Color MedalSilver = new(0.82f, 0.86f, 0.92f);
    private static readonly Color MedalBronze = new(0.82f, 0.56f, 0.34f);
    private static readonly Color MedalSkip = new(0.62f, 0.62f, 0.68f);

    /// <summary>Medals of this run's puzzles (empty without any).</summary>
    private static List<RewardTier> Medals() => Main.Instance?.Stages?.PuzzleMedals ?? new List<RewardTier>();

    private static int CountOf(List<RewardTier> medals, RewardTier t)
    {
        int n = 0;
        foreach (RewardTier m in medals) if (m == t) n++;
        return n;
    }

    /// <summary>A Puzzle Gauntlet run: title, subtitle and tiles are its own.
    /// False for any other run (the usual screen is written).</summary>
    private bool WriteGauntlet()
    {
        if (!StageDirector.GauntletRoute) return false;
        List<RewardTier> medals = Medals();
        bool done = medals.Count >= Routes.GauntletPuzzles;
        bool best = done && GauntletRecord.Record(medals);
        int score = GauntletRecord.Score(medals);
        _title.Text = Loc.T(done ? "PUZZLES COMPLETE" : "GAUNTLET OVER");
        _title.AddThemeColorOverride("font_color", done ? Victory : Defeat);
        _panelStyle.BorderColor = done ? Victory : Defeat;
        _subtitle.Text = Loc.F("{0}  ·  puzzles {1}/{2}  ·  score {3}/{4}", Loc.T(PuzzleText.GauntletName), medals.Count, Routes.GauntletPuzzles, score, GauntletRecord.MaxScore);
        if (best) _badges.AddChild(Badge(Loc.T("NEW BEST GAUNTLET"), Gold));

        foreach (Node c in _tiles.GetChildren()) c.QueueFree();
        Tile("star", MedalGold, $"{CountOf(medals, RewardTier.Gold)}", Loc.T(PuzzleText.TierName(RewardTier.Gold)));
        Tile("gem", MedalSilver, $"{CountOf(medals, RewardTier.Silver)}", Loc.T(PuzzleText.TierName(RewardTier.Silver)));
        Tile("coin", MedalBronze, $"{CountOf(medals, RewardTier.Bronze)}", Loc.T(PuzzleText.TierName(RewardTier.Bronze)));
        Tile("hourglass", MedalSkip, $"{CountOf(medals, RewardTier.Skipped)}", Loc.T(PuzzleText.TierName(RewardTier.Skipped)));
        Tile("crown", Gold, $"{GauntletRecord.Best}/{GauntletRecord.MaxScore}", Loc.T("Best score"));
        GD.Print($"[gauntlet] results medals={string.Join(",", medals)} score={score} best={GauntletRecord.Best} new_best={best}");
        return true;
    }

    /// <summary>The puzzle route: one more tile, the medals so far.</summary>
    private void WritePuzzleTile()
    {
        if (!StageDirector.PuzzleRoute) return;
        List<RewardTier> medals = Medals();
        if (medals.Count == 0) return;
        string line = Loc.Ltr($"{CountOf(medals, RewardTier.Gold)} / {CountOf(medals, RewardTier.Silver)} / {CountOf(medals, RewardTier.Bronze)}");
        Tile("star", MedalGold, line, Loc.T("Puzzle medals"));
    }
}
