namespace Wavebreak.Progression;

/// <summary>
/// Co-op late join (Net/NetSession.Snapshot.cs): a player who joins a run in
/// progress is brought to the team's level. The experience is banked
/// directly (no XP-gain scaling: the target is a level, not an amount), and
/// each level raises the usual <see cref="Core.LevelGained"/>, whose card the
/// run picks automatically while <see cref="Game.Main.CatchUpPicks"/> lasts.
/// </summary>
public sealed partial class Inventory
{
    internal void CatchUpTo(int level)
    {
        if (level <= Level) return;
        level = System.Math.Min(level, XpCurve.MaxLevel);
        int need = XpCurve.TotalXpForLevel(level) - Xp;
        if (need > 0) Bank(need);
    }
}
