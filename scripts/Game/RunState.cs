using Godot;
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

    /// <summary>Nominal length of a full run. 15 minutes is the genre's
    /// comfortable length: long enough for a build to come together, short
    /// enough to retry after a bad start.
    ///
    /// With phases configured this is no longer a timer — the run is won by
    /// killing the last phase boss — but the stage table is tuned so a run lands
    /// near it, and content pacing (unlock times) is still checked against it.</summary>
    public const float TargetSeconds = 15f * 60f;

    // ---- phases. Facts only; the flow that moves them is StageDirector's.

    /// <summary>1-based phase being played. 0 before the first map is built.</summary>
    public int Phase { get; private set; }

    /// <summary>Phases in this run. 0 means no phase flow, and the run falls
    /// back to the old survival timer.</summary>
    public int PhaseCount { get; private set; }

    /// <summary>Seconds since the current phase began.</summary>
    public float PhaseSeconds { get; private set; }

    public string StageId { get; private set; } = "";
    public string StageName { get; private set; } = "";

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
        PhaseSeconds += dt;
        // Only without phases: with them, the win is the last boss dying, and a
        // timer that ended the run mid-boss-fight would steal it.
        if (PhaseCount == 0 && Seconds >= TargetSeconds) Finish(survived: true);
    }

    /// <summary>Record that a new phase has begun on a new map.</summary>
    public void BeginPhase(int phase, int phaseCount, string stageId, string stageName)
    {
        Phase = phase;
        PhaseCount = phaseCount;
        PhaseSeconds = 0f;
        StageId = stageId;
        StageName = stageName;
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

    /// <summary>Co-op client: adopt the host's clock (sent twice a second), so
    /// the HUD's timers agree across the team.</summary>
    public void SyncClock(float seconds, float phaseSeconds)
    {
        if (Over) return;
        Seconds = seconds;
        PhaseSeconds = phaseSeconds;
    }

    public float Minutes => Seconds / 60f;

    /// <summary>mm:ss of the current phase.</summary>
    public string PhaseClock => Format(PhaseSeconds);

    public static string Format(float seconds)
    {
        int total = Mathf.Max(0, (int)seconds);
        return $"{total / 60:00}:{total % 60:00}";
    }

    public string Clock
    {
        get
        {
            int total = (int)Seconds;
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
