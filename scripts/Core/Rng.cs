using System.Collections.Generic;
using Godot;

namespace Wavebreak.Core;

/// <summary>
/// The run's random source.
///
/// Deliberately not <c>GD.Randf</c>: a run seeded by an explicit number can be
/// replayed exactly, which is what makes a balance bug reproducible and lets the
/// headless tests assert real outcomes instead of ranges. Seeded runs are also
/// the foundation for daily challenges later.
/// </summary>
public sealed class Rng
{
    private readonly RandomNumberGenerator _rng = new();

    public ulong Seed { get; }

    public Rng(ulong seed)
    {
        Seed = seed;
        _rng.Seed = seed;
    }

    public float Float() => _rng.Randf();
    public float Range(float min, float max) => _rng.RandfRange(min, max);
    public int RangeInt(int minInclusive, int maxExclusive)
        => maxExclusive <= minInclusive ? minInclusive
         : _rng.RandiRange(minInclusive, maxExclusive - 1);

    public bool Chance(float probability) => probability > 0f && _rng.Randf() < probability;

    public Vector3 OnUnitCircleXZ()
    {
        float a = _rng.RandfRange(0f, Mathf.Tau);
        return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
    }

    public T Pick<T>(IReadOnlyList<T> items) => items[RangeInt(0, items.Count)];

    /// <summary>
    /// Weighted pick. Weights need not sum to anything in particular; a
    /// non-positive weight means "cannot be chosen", which is how rarity gating
    /// and one-shot uniques are expressed without a second code path.
    /// </summary>
    public int PickWeighted(IReadOnlyList<float> weights)
    {
        float total = 0f;
        foreach (float w in weights) if (w > 0f) total += w;
        if (total <= 0f) return -1;

        float roll = _rng.RandfRange(0f, total);
        for (int i = 0; i < weights.Count; i++)
        {
            if (weights[i] <= 0f) continue;
            roll -= weights[i];
            if (roll <= 0f) return i;
        }
        // float drift can leave roll marginally positive past the last entry
        for (int i = weights.Count - 1; i >= 0; i--)
            if (weights[i] > 0f) return i;
        return -1;
    }
}
