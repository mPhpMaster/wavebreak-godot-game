using Godot;

namespace Wavebreak.Core;

// Events for the phase/map flow. Kept beside GameEvents rather than inside it so
// the stage system can be read (or removed) as a unit; they are ordinary bus
// events in every other respect.

/// <summary>A phase began on a freshly built map. Published for phase 1 as well,
/// so anything that reacts to "new map" has exactly one code path.</summary>
public readonly struct PhaseStarted
{
    public readonly int Phase;
    public readonly int PhaseCount;
    public readonly string StageId;
    public readonly string StageName;
    public readonly string Tagline;

    public PhaseStarted(int phase, int phaseCount, string stageId, string stageName, string tagline)
    {
        Phase = phase; PhaseCount = phaseCount; StageId = stageId; StageName = stageName; Tagline = tagline;
    }
}

/// <summary>The phase timer ran out and the phase boss is on the field.</summary>
public readonly struct PhaseBossArrived
{
    public readonly int Phase;
    public readonly string BossName;
    public PhaseBossArrived(int phase, string bossName) { Phase = phase; BossName = bossName; }
}

/// <summary>The phase boss died and the way onward is open. Not published after
/// the final phase: that boss ends the run instead.</summary>
public readonly struct PortalOpened
{
    public readonly int Phase;
    public readonly Vector3 Position;
    public PortalOpened(int phase, Vector3 position) { Phase = phase; Position = position; }
}

/// <summary>A puzzle map was entered (puzzle route, Game/Puzzle). Puzzle is
/// 1-based; Count is how many the run has.</summary>
public readonly struct PuzzleStarted
{
    public readonly int Puzzle;
    public readonly int Count;
    public readonly string Name;
    public PuzzleStarted(int puzzle, int count, string name) { Puzzle = puzzle; Count = count; Name = name; }
}

/// <summary>A puzzle map's last room was solved (or the team took the SKIP
/// portal). Tier is a Game.Puzzle.RewardTier; published once per map on every
/// machine, for the banner and the quests.</summary>
public readonly struct PuzzleFinished
{
    public readonly int Tier;
    public readonly float Seconds;
    public readonly int Hints;
    public readonly bool Skipped;
    public PuzzleFinished(int tier, float seconds, int hints, bool skipped) { Tier = tier; Seconds = seconds; Hints = hints; Skipped = skipped; }
}
