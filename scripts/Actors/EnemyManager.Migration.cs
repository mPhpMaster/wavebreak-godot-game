using System;

namespace Wavebreak.Actors;

/// <summary>
/// Host migration halves of the enemy manager (Net/NetSession.Migration.cs).
///
/// The host streams its director's clock-keeping once a second (credits, the
/// elite wave schedule, the spawn timer) inside the run snapshot, so a client
/// that becomes the host carries on the same schedule instead of starting the
/// director from zero (which would, for one, fire an elite wave at once).
/// </summary>
public sealed partial class EnemyManager
{
    /// <summary>Number of floats in <see cref="WriteDirectorState"/>.</summary>
    internal const int DirectorStateLength = 5;

    /// <summary>Host: the director's schedule, for the run snapshot.</summary>
    internal void WriteDirectorState(System.Collections.Generic.List<float> into)
    {
        into.Add(_credits);
        into.Add(_nextEliteAt);
        into.Add(_eliteWaveUntil);
        into.Add(_spawnTimer);
        into.Add(_nextBossAt);
    }

    /// <summary>Client: the host's schedule, kept for the day this machine takes over.</summary>
    internal void ReadDirectorState(float[] d, int o)
    {
        if (o + DirectorStateLength > d.Length) return;
        _credits = d[o];
        _nextEliteAt = d[o + 1];
        _eliteWaveUntil = d[o + 2];
        _spawnTimer = MathF.Max(0f, d[o + 3]);
        _nextBossAt = d[o + 4];
    }

    /// <summary>New host: every puppet becomes a real enemy with the current
    /// difficulty, chasing the nearest living player from the next physics step.
    /// Returns how many were promoted.</summary>
    internal int PromotePuppets(float runSeconds)
    {
        _runSeconds = runSeconds;
        UpdateDifficulty(runSeconds);
        int n = 0;
        for (int i = 0; i < _alive.Count; i++)
        {
            Enemy e = _alive[i];
            if (!e.IsAlive) continue;
            e.PromoteFromPuppet(DifficultyDamage, DifficultySpeed);
            n++;
        }
        _gridValid = false;
        return n;
    }
}
