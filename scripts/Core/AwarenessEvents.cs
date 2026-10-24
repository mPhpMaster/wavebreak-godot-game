namespace Wavebreak.Core;

// Events for the team-awareness layer (minimap, name tags, boss toast,
// scoreboard). Kept out of GameEvents.cs so the feature stays in its own files.

/// <summary>A boss died. <see cref="KillerId"/> is the peer whose hit landed
/// last (the host decides in co-op; the local player in single-player), and
/// <see cref="Boss"/> is the boss's English display name (translate on show).</summary>
public readonly struct BossDefeated
{
    public readonly long KillerId;
    public readonly string KillerName;
    public readonly string Boss;
    public BossDefeated(long killerId, string killerName, string boss)
    {
        KillerId = killerId; KillerName = killerName; Boss = boss;
    }
}
