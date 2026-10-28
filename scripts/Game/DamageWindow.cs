using System;

namespace Wavebreak.Game;

/// <summary>Ten one-second buckets: bounded storage regardless of hit frequency.</summary>
internal sealed class DamageWindow
{
    private readonly long[] _seconds = new long[10];
    private readonly float[] _damage = new float[10];
    private readonly double _started;
    public DamageWindow(double started) => _started = started;
    public void Add(double time, float damage)
    {
        long second = (long)time; int bucket = (int)(second % 10);
        if (_seconds[bucket] != second) { _seconds[bucket] = second; _damage[bucket] = 0; }
        _damage[bucket] += damage;
    }
    public float Rate(double time)
    {
        long current = (long)time; float total = 0;
        for (int i = 0; i < 10; i++) if (current - _seconds[i] is >= 0 and < 10) total += _damage[i];
        return total / (float)Math.Clamp(time - _started, 1, 10);
    }
}
