using Godot;
using Wavebreak.Data;

namespace Wavebreak.Core;

// Map shrine events (Game/ShrineSpawner). Kept out of GameEvents.cs so the
// shrine feature adds to the bus without editing the shared event table.

/// <summary>What happened at a map shrine.</summary>
public enum ShrineOutcome
{
    /// <summary>A charge ring filled; the blessing panel is about to open.</summary>
    Charged,
    /// <summary>A stat blessing was granted (charge pick or statue).</summary>
    Blessed,
    /// <summary>Not enough gold, or a challenge is already running.</summary>
    Denied,
    ChallengeStarted,
    ChallengeWon,
    ChallengeFailed,
    /// <summary>A greed curse stack was accepted.</summary>
    Cursed,
}

/// <summary>Published by the shrine spawner for audio, quests and logs.
/// <see cref="Detail"/> is a short human string ("Damage +12%", "kill 40").</summary>
public readonly struct MapShrineUsed
{
    public readonly MapShrineKind Kind;
    public readonly ShrineOutcome Outcome;
    public readonly Vector3 Position;
    public readonly string Detail;

    public MapShrineUsed(MapShrineKind kind, ShrineOutcome outcome, Vector3 position, string detail = "")
    {
        Kind = kind; Outcome = outcome; Position = position; Detail = detail;
    }
}
