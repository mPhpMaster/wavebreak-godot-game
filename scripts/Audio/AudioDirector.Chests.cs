using System;
using Godot;
using Wavebreak.Core;

namespace Wavebreak.Audio;

/// <summary>
/// Chest sounds: the lid and its shimmer, the refusal, and the chime when an
/// elite drops a free one.
///
/// A partial of the director rather than new <see cref="Sfx"/> members so the
/// chest system adds sounds without editing the synth table every other sound
/// lives in. The buffers are built here from the same palette rules
/// <see cref="SfxSynth"/> follows (sine/triangle, soft attack, a tail, a final
/// low-pass, the shared peak) and played through the director's own 2D voice
/// pool, so they obey the same stealing and priority as everything else.
/// </summary>
public sealed partial class AudioDirector
{
    private enum ChestSound { Open, OpenGilded, Denied, Dropped }

    private static AudioStreamWav[]? _chestStreams;
    private readonly double[] _chestLast = new double[4];
    private int _chestPlayed;

    /// <summary>Volume, priority, minimum interval. Openings sit just under the
    /// level-up chime: they are a reward, but a rarer and smaller one.</summary>
    private static (float Db, int Priority, float MinInterval) ChestSpec(ChestSound s) => s switch
    {
        ChestSound.Open       => (-10f, 6, 0.15f),
        ChestSound.OpenGilded => (-8f, 6, 0.15f),
        ChestSound.Denied     => (-13f, 5, 0.25f),
        _                     => (-11f, 5, 0.4f),
    };

    private void SubscribeChestSounds()
    {
        _chestStreams ??= new[]
        {
            SfxSynth.ToStream(BuildChest(ChestSound.Open)),
            SfxSynth.ToStream(BuildChest(ChestSound.OpenGilded)),
            SfxSynth.ToStream(BuildChest(ChestSound.Denied)),
            SfxSynth.ToStream(BuildChest(ChestSound.Dropped)),
        };
        EventBus.Subscribe<ChestOpened>(OnChestOpened);
        EventBus.Subscribe<ChestDenied>(OnChestDenied);
        EventBus.Subscribe<ChestSpawned>(OnChestSpawned);
    }

    private void UnsubscribeChestSounds()
    {
        EventBus.Unsubscribe<ChestOpened>(OnChestOpened);
        EventBus.Unsubscribe<ChestDenied>(OnChestDenied);
        EventBus.Unsubscribe<ChestSpawned>(OnChestSpawned);
    }

    private void OnChestOpened(ChestOpened e)
        => PlayChest(e.Tier == ChestTier.Gilded ? ChestSound.OpenGilded : ChestSound.Open);

    private void OnChestDenied(ChestDenied e) => PlayChest(ChestSound.Denied);

    private void OnChestSpawned(ChestSpawned e)
    {
        // Only drops announce themselves; chests placed with the map are found
        // by looking, and five chimes at run start would mean nothing.
        if (e.Free) PlayChest(ChestSound.Dropped);
    }

    private void PlayChest(ChestSound s)
    {
        if (_chestStreams is null) return;
        var (db, priority, minInterval) = ChestSpec(s);
        double now = Now();
        if (now - _chestLast[(int)s] < minInterval) { _dropped++; return; }

        // Same choice as Play: a free voice, else the oldest of the lowest
        // priority at or below ours, else drop.
        Voice? voice = null;
        foreach (Voice v in _voices2D)
        {
            if (!IsPlaying(v.Player)) { voice = v; break; }
            if (v.Priority <= priority
                && (voice is null || v.Priority < voice.Priority
                    || (v.Priority == voice.Priority && v.StartedAt < voice.StartedAt)))
                voice = v;
        }
        if (voice?.Player is not AudioStreamPlayer p) { _dropped++; return; }
        if (IsPlaying(p)) _stolen++;

        p.Stream = _chestStreams[(int)s];
        p.PitchScale = 1f;
        p.VolumeDb = db;
        p.Play();

        // Not a member of Sfx, so no per-sound voice cap counts it as a copy
        // of something else.
        voice.Id = (Sfx)(-1);
        voice.Priority = priority;
        voice.StartedAt = now;
        _chestLast[(int)s] = now;
        _chestPlayed++;
    }

    // ------------------------------------------------------------- synthesis

    private static float[] BuildChest(ChestSound s)
    {
        const int rate = SfxSynth.Rate;
        float seconds = s switch
        {
            ChestSound.Open => 0.9f,
            ChestSound.OpenGilded => 1.3f,
            ChestSound.Denied => 0.26f,
            _ => 0.7f,
        };
        float[] b = new float[(int)(seconds * rate)];
        var noise = new SfxSynth.NoiseGen(0xC4E57u + (uint)s);

        switch (s)
        {
            case ChestSound.Open:
            case ChestSound.OpenGilded:
                // A soft wooden thunk as the latch gives, then a rising shimmer:
                // the lid, then what is under it.
                Tone(b, 0f, 0.14f, 180f, 95f, false, 1f, 0.004f, 24f);
                Noise(b, noise, 0f, 0.08f, 900f, 0.5f, 40f);
                float[] notes = s == ChestSound.OpenGilded
                    ? new[] { 659.25f, 783.99f, 987.77f, 1318.5f, 1567.98f }
                    : new[] { 587.33f, 739.99f, 880f, 1174.66f };
                for (int k = 0; k < notes.Length; k++)
                {
                    float start = 0.1f + k * 0.06f;
                    Tone(b, start, seconds - start, notes[k], notes[k], false, 0.45f, 0.006f, 4.5f);
                    Tone(b, start, seconds - start, notes[k] * 2f, notes[k] * 2f, true, 0.07f, 0.006f, 7f);
                }
                break;
            case ChestSound.Denied:
                // Two low descending blips: "no", without the harshness of a buzzer.
                Tone(b, 0f, 0.11f, 220f, 210f, true, 0.8f, 0.005f, 14f);
                Tone(b, 0.12f, 0.14f, 165f, 150f, true, 0.8f, 0.005f, 12f);
                break;
            default:
                // Free chest dropped: a bright two-note bell.
                Tone(b, 0f, 0.7f, 1046.5f, 1046.5f, false, 0.6f, 0.004f, 6f);
                Tone(b, 0.09f, 0.6f, 1567.98f, 1567.98f, false, 0.5f, 0.004f, 6f);
                break;
        }

        // Final smoothing and normalisation, as SfxSynth does for every buffer.
        float a = 1f - MathF.Exp(-MathF.Tau * 4200f / rate);
        float y = 0f, peak = 0f;
        for (int i = 0; i < b.Length; i++) { y += a * (b[i] - y); b[i] = y; peak = MathF.Max(peak, MathF.Abs(y)); }
        if (peak > 1e-6f) for (int i = 0; i < b.Length; i++) b[i] *= SfxSynth.Peak / peak;
        return b;
    }

    /// <summary>Envelope with SfxSynth's floors: at least 4 ms attack and a
    /// 12 ms fade to zero, so no buffer starts or ends on a click.</summary>
    private static float ChestEnv(float t, float duration, float attack, float decay)
    {
        attack = MathF.Max(attack, 0.004f);
        float env = t < attack ? t / attack : MathF.Exp(-decay * (t - attack));
        return env * MathF.Max(0f, MathF.Min(1f, (duration - t) / 0.012f));
    }

    private static void Tone(float[] b, float start, float duration, float f0, float f1,
                             bool triangle, float amp, float attack, float decay)
    {
        const int rate = SfxSynth.Rate;
        int s0 = (int)(start * rate);
        int n = Math.Min((int)(duration * rate), b.Length - s0);
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float f = f0 * MathF.Pow(f1 / f0, t / duration);
            phase += f / rate;
            phase -= MathF.Floor(phase);
            float osc = triangle ? 1f - 4f * MathF.Abs(phase - 0.5f) : MathF.Sin(MathF.Tau * phase);
            b[s0 + i] += amp * ChestEnv(t, duration, attack, decay) * osc;
        }
    }

    private static void Noise(float[] b, SfxSynth.NoiseGen rng, float start, float duration,
                              float cutoff, float amp, float decay)
    {
        const int rate = SfxSynth.Rate;
        int s0 = (int)(start * rate);
        int n = Math.Min((int)(duration * rate), b.Length - s0);
        float a = 1f - MathF.Exp(-MathF.Tau * cutoff / rate);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            lp += a * (rng.Next() - lp);
            b[s0 + i] += amp * ChestEnv(i / (float)rate, duration, 0.004f, decay) * lp;
        }
    }
}
