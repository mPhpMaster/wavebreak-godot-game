using Godot;

namespace Wavebreak.Game;

public enum ReefCurrentKind { Horizontal, Diagonal, Updraft, Downdraft }

/// <summary>
/// One water current: a capsule from <see cref="From"/> to <see cref="To"/> that
/// pushes swimmers toward <see cref="To"/>. Its life is fixed when it is made:
/// a telegraph (visible, no push), the active stretch, then a fade-out. Every
/// time is on the phase clock (<see cref="Game.RunState.PhaseSeconds"/>), which
/// co-op keeps in step, so every machine sees the same current at the same time.
/// </summary>
public sealed class ReefCurrentFlow
{
    public const float TelegraphSeconds = 1.5f;
    public const float FadeSeconds = 2f;
    /// <summary>Push ramps in over this long once the telegraph ends.</summary>
    public const float RampSeconds = 0.5f;

    public ReefCurrentKind Kind;
    public Vector3 From, To;
    public float Radius, Strength;
    public float Born, ActiveSeconds;
    /// <summary>Slot that made it (-1: placed by a self-check).</summary>
    public int Slot = -1;
    public int Cycle;

    public float Length => From.DistanceTo(To);
    public Vector3 Direction => (To - From).Normalized();
    public Vector3 Centre => (From + To) * 0.5f;
    public float PushStart => Born + TelegraphSeconds;
    public float FadeStart => PushStart + ActiveSeconds;
    public float Dies => FadeStart + FadeSeconds;
    public bool Vertical => Kind is ReefCurrentKind.Updraft or ReefCurrentKind.Downdraft;

    /// <summary>A slot's rest cycle that found no open water: never drawn, never pushes.</summary>
    public bool Empty => Strength <= 0f || Radius <= 0f;
    public bool Alive(float t) => !Empty && t >= Born && t < Dies;

    /// <summary>0..1 share of the full push at phase time <paramref name="t"/>:
    /// nothing while telegraphing, a short ramp, full, then a linear fade.</summary>
    public float Push(float t)
    {
        if (t < PushStart || t >= Dies) return 0f;
        if (t < PushStart + RampSeconds) return (t - PushStart) / RampSeconds;
        if (t < FadeStart) return 1f;
        return 1f - (t - FadeStart) / FadeSeconds;
    }

    /// <summary>0..1 how strongly it is drawn: a faint ghost growing through the
    /// telegraph (so it reads as "something is coming"), full while it pushes.</summary>
    public float Visibility(float t)
    {
        if (t < Born || t >= Dies) return 0f;
        if (t < PushStart) return 0.15f + 0.4f * ((t - Born) / TelegraphSeconds);
        return Mathf.Max(0.55f, Push(t));
    }

    /// <summary>0..1 telegraph progress (1 once pushing), for the warning shell.</summary>
    public float Telegraph(float t) => t < Born ? 0f : Mathf.Clamp((t - Born) / TelegraphSeconds, 0f, 1f);

    /// <summary>Weight 0..1 of a point inside the capsule: full in the core,
    /// soft at the rim and over the last few metres of each end.</summary>
    public float Weight(Vector3 at)
    {
        Vector3 line = To - From;
        float length2 = line.LengthSquared();
        if (length2 < 0.01f) return 0f;
        float s = (at - From).Dot(line) / length2;
        if (s < -0.1f || s > 1.1f) return 0f;
        float distance = at.DistanceTo(From + line * Mathf.Clamp(s, 0f, 1f));
        if (distance >= Radius) return 0f;
        float rim = 1f - Mathf.SmoothStep(Radius * 0.55f, Radius, distance);
        float length = Mathf.Sqrt(length2);
        float ends = Mathf.Clamp(Mathf.Min(s * length + 1.5f, (1f - s) * length + 1.5f) / 4f, 0f, 1f);
        return rim * ends;
    }

    public Vector3 VelocityAt(Vector3 at, float t)
    {
        float push = Empty ? 0f : Push(t);
        if (push <= 0f) return Vector3.Zero;
        float weight = Weight(at);
        return weight <= 0f ? Vector3.Zero : Direction * (Strength * push * weight);
    }
}
