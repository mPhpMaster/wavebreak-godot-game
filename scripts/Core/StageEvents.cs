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
