using System.Collections.Generic;
using Wavebreak.Game.Puzzle;

namespace Wavebreak.Game;

/// <summary>
/// The Puzzle Gauntlet (Meta/Routes "puzzles"): puzzle maps only, back to
/// back, <see cref="Meta.Routes.GauntletPuzzles"/> of them, each a step harder
/// (<see cref="PuzzleGen.DifficultyFor"/> climbs with the puzzle's number,
/// as on the puzzle route). There are no guards, no prize pedestals and no
/// rest rooms: the vault's EXIT (or the SKIP portal) leads straight into the
/// next puzzle, and after the last the run ends with the results screen,
/// which shows the medals (UI/ResultsScreen.Puzzle.cs) and keeps the best
/// score on the profile (Meta/GauntletRecord). The run's stage list is six
/// maps long only so <see cref="_index"/> (the puzzle's number) works as on
/// the puzzle route; no action map is ever entered. A finished gauntlet is
/// not a won run: no quest or leaderboard counts it as one.
/// </summary>
public sealed partial class StageDirector
{
    /// <summary>This run is the Puzzle Gauntlet.</summary>
    public static bool GauntletRoute => Meta.Routes.Active?.Id == Meta.Routes.GauntletId && !Meta.EndlessMode.Active;

    /// <summary>The last puzzle of the gauntlet has been left.</summary>
    public bool GauntletDone { get; private set; }

    /// <summary>Every puzzle's medal so far, the standing one too once it is
    /// finished or skipped (a client's results come while it still stands).</summary>
    public List<RewardTier> PuzzleMedals
    {
        get
        {
            var list = new List<RewardTier>(PuzzleTiers);
            if (!_puzzleTierRecorded && Puzzle is { } map && (map.State.Finished || map.State.Skipped))
                list.Add(map.State.Tier == RewardTier.None ? RewardTier.Skipped : map.State.Tier);
            return list;
        }
    }

    /// <summary>Host and single-player: a gauntlet puzzle has been left.</summary>
    private void LeaveGauntletPuzzle(string why, RewardTier tier)
    {
        if (_index < _stages.Count - 1)
        {
            Log($"gauntlet: puzzle {_index + 1} done ({why}, {tier}) -> puzzle {_index + 2}/{_stages.Count}");
            EnterPuzzle(_index + 1);
            return;
        }
        // The last one: its medal goes on the list now and the map stays
        // standing behind the results screen.
        PuzzleTiers.Add(tier == RewardTier.None ? RewardTier.Skipped : tier);
        _puzzleTierRecorded = true;
        Puzzle?.GrantOnLeave();
        GauntletDone = true;
        Log($"gauntlet complete: {string.Join(",", PuzzleTiers)}");
        _run.Finish(survived: true);
    }
}
