using Wavebreak.Core;

namespace Wavebreak.Game;

/// <summary>
/// Everything true about the current run and nothing else.
///
/// Kept separate from <see cref="Main"/> deliberately: the run's facts — how long
/// it has lasted, how many things died, which seed produced it — are read by the
/// HUD, the director and the results screen, and none of those should need a
/// reference to the scene root to ask. It also means a headless test can drive a
/// run without a scene tree at all.
/// </summary>
public sealed class RunState
{
    public ulong Seed { get; }
    public Rng Rng { get; }

    public float Seconds { get; private set; }
    public int Kills { get; private set; }
    public int Elites { get; private set; }
    public bool Over { get; private set; }
    public bool Survived { get; private set; }

    /// <summary>How long a full run lasts before the player wins. 15 minutes is
    /// the genre's comfortable length: long enough for a build to come together,
    /// short enough to retry after a bad start.</summary>
    public const float TargetSeconds = 15f * 60f;

    /// <summary>Mirrored off the bus so the results screen and the RunEnded
    /// payload carry a real level instead of a zero.</summary>
    public int Level { get; private set; } = 1;

    public RunState(ulong seed)
    {
        Seed = seed;
        Rng = new Rng(seed);
        EventBus.Subscribe<EnemyKilled>(OnKill);
        EventBus.Subscribe<LevelGained>(OnLevel);
    }

    public void Dispose()
    {
        EventBus.Unsubscribe<EnemyKilled>(OnKill);
        EventBus.Unsubscribe<LevelGained>(OnLevel);
    }

    private void OnKill(EnemyKilled e)
    {
        Kills++;
        if (e.WasElite) Elites++;
    }

    private void OnLevel(LevelGained e) => Level = e.Level;

    public void Tick(float dt)
    {
        if (Over) return;
        Seconds += dt;
        if (Seconds >= TargetSeconds) Finish(survived: true);
    }

    /// <summary>
    /// End the run and tell everyone. Used by the survival timer.
    ///
    /// Publishing here matters: death is announced by PlayerHealth, but nothing
    /// announced a *win*, so reaching the 15-minute mark set the flags and left
    /// the player standing in a world that had stopped mattering, with no
    /// results screen. Over is set before publishing so a handler that calls
    /// back in re-enters exactly once and stops.
    /// </summary>
    public void Finish(bool survived)
    {
        if (Over) return;
        Over = true;
        Survived = survived;
        EventBus.Publish(new RunEnded(survived, Seconds, Level, Kills));
    }

    /// <summary>Record an ending that something else already announced — death,
    /// published by PlayerHealth. Deliberately silent: publishing again from
    /// inside a RunEnded handler would show the results screen twice.</summary>
    public void MarkOver(bool survived)
    {
        if (Over) return;
        Over = true;
        Survived = survived;
    }

    public float Minutes => Seconds / 60f;

    public string Clock
    {
        get
        {
            int total = (int)Seconds;
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
