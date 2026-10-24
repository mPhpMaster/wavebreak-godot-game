using System;
using Godot;

namespace Wavebreak.Audio;

/// <summary>Every sound the game can make. The ambient bed is not here: it loops
/// and lives on its own player, so it never competes for a voice.</summary>
public enum Sfx
{
    FireProjectile, FireMelee, FireAura, FireExplosive,
    EnemyHit, EnemyDeath, EliteDeath,
    PlayerHurt, XpPickup, GoldPickup, LevelUp,
    Dash, Jump, AirJump, Slide, SlamStart, SlamImpact, Land,
    RunWin, RunLose,
}

/// <summary>
/// Builds every sound from oscillators, filtered noise and envelopes, once, at
/// startup. There are no audio files in the project on purpose: nothing to
/// license, nothing to import, and a sound is tuned by changing a number here.
///
/// The palette is deliberately soft, because these sounds repeat hundreds of
/// times a run and the first version was found grating within a minute:
///   * sine and triangle only — no raw square or saw, whose upper harmonics are
///     what makes a sound that repeats every 0.3 s tiring;
///   * a minimum 4 ms attack and 12 ms tail on every envelope, so no buffer
///     starts or ends on a step (a click);
///   * noise is always low-passed below ~2.5 kHz, and every buffer goes through
///     a final gentle low-pass that takes the edge off whatever is left.
///
/// 22.05 kHz mono 16-bit is plenty for short effects and keeps the whole set
/// well under a megabyte. Every buffer is normalised to the same, modest peak
/// so the mix is decided by <see cref="AudioDirector"/>'s volume table, and so
/// nothing can clip on its own.
/// </summary>
internal static partial class SfxSynth
{
    public const int Rate = 22050;
    public const float Peak = 0.5f;

    private const float Tau = MathF.PI * 2f;

    /// <summary>Floors on the envelope edges. Anything shorter is a click.</summary>
    private const float MinAttack = 0.004f;
    private const float Tail = 0.012f;

    /// <summary>The final low-pass every effect goes through.</summary>
    private const float Smoothing = 4200f;

    private enum Wave { Sine, Triangle }

    /// <summary>How many takes of each sound to render. Repeated sounds get a few
    /// slightly different takes, on top of the director's pitch jitter, because
    /// pitch alone still sounds like the same sample sped up.</summary>
    public static int Variants(Sfx id) => id switch
    {
        Sfx.FireProjectile or Sfx.FireMelee or Sfx.FireAura or Sfx.FireExplosive => 3,
        Sfx.EnemyHit or Sfx.EnemyDeath or Sfx.PlayerHurt => 3,
        Sfx.Jump or Sfx.Land => 2,
        _ => 1,
    };

    public static float[] Build(Sfx id, int variant)
    {
        // Seeded per sound and take: the noise is identical every launch, so a
        // sound that is right today is still right tomorrow.
        var rng = new NoiseGen((uint)((int)id * 7919 + variant * 104729 + 1));
        float d = 1f + (variant - 1) * 0.05f; // detune between takes
        float[] b;

        switch (id)
        {
            case Sfx.FireProjectile:
                // A soft puff, not a laser: this plays several times a second.
                b = New(0.07f);
                Tone(b, 0f, 0.065f, 520f * d, 380f * d, Wave.Sine, 1f, 0.004f, 50f);
                Noise(b, rng, 0f, 0.04f, 1800f, 900f, 300f, 0.25f, 0.004f, 60f);
                break;
            case Sfx.FireMelee:
                b = New(0.15f);
                Noise(b, rng, 0f, 0.14f, 600f * d, 1800f * d, 200f, 1f, 0.04f, 20f);
                break;
            case Sfx.FireAura:
                b = New(0.13f);
                Tone(b, 0f, 0.12f, 160f * d, 130f * d, Wave.Sine, 1f, 0.03f, 25f);
                break;
            case Sfx.FireExplosive:
                b = New(0.15f);
                Tone(b, 0f, 0.14f, 140f * d, 50f, Wave.Sine, 1f, 0.004f, 22f);
                Noise(b, rng, 0f, 0.1f, 1200f, 300f, 0f, 0.5f, 0.004f, 35f);
                break;
            case Sfx.EnemyHit:
                // A muted wooden "tock".
                b = New(0.06f);
                Tone(b, 0f, 0.055f, 420f * d, 300f * d, Wave.Sine, 1f, 0.004f, 70f);
                Tone(b, 0f, 0.04f, 840f * d, 600f * d, Wave.Triangle, 0.2f, 0.004f, 90f);
                Noise(b, rng, 0f, 0.025f, 2500f, 1500f, 600f, 0.3f, 0.004f, 120f);
                break;
            case Sfx.EnemyDeath:
                b = New(0.18f);
                Tone(b, 0f, 0.17f, 360f * d, 110f * d, Wave.Sine, 1f, 0.004f, 22f);
                Tone(b, 0f, 0.12f, 720f * d, 220f * d, Wave.Triangle, 0.25f, 0.004f, 30f);
                Noise(b, rng, 0f, 0.12f, 1800f * d, 400f, 100f, 0.35f, 0.004f, 30f);
                break;
            case Sfx.EliteDeath:
                b = New(0.5f);
                Tone(b, 0f, 0.48f, 220f, 55f, Wave.Sine, 1f, 0.005f, 7f);
                Tone(b, 0f, 0.3f, 440f, 110f, Wave.Triangle, 0.3f, 0.005f, 12f);
                Noise(b, rng, 0f, 0.45f, 1500f, 200f, 40f, 0.5f, 0.005f, 9f);
                break;
            case Sfx.PlayerHurt:
                // Two triangles slightly apart beat against each other: clearly
                // "something is wrong" without the buzz a saw would bring.
                b = New(0.24f);
                Tone(b, 0f, 0.23f, 300f * d, 170f * d, Wave.Triangle, 0.8f, 0.005f, 11f);
                Tone(b, 0f, 0.23f, 318f * d, 180f * d, Wave.Triangle, 0.6f, 0.005f, 11f);
                Noise(b, rng, 0f, 0.06f, 1500f, 800f, 200f, 0.35f, 0.004f, 45f);
                break;
            case Sfx.XpPickup:
                b = New(0.11f);
                Tone(b, 0f, 0.1f, 880f, 900f, Wave.Sine, 1f, 0.004f, 32f);
                Tone(b, 0f, 0.07f, 1760f, 1800f, Wave.Sine, 0.18f, 0.004f, 50f);
                break;
            case Sfx.GoldPickup:
                b = New(0.19f);
                Tone(b, 0f, 0.07f, 1046.5f, 1046.5f, Wave.Triangle, 0.6f, 0.004f, 22f);
                Tone(b, 0.055f, 0.13f, 1318.5f, 1318.5f, Wave.Sine, 0.7f, 0.004f, 20f);
                break;
            case Sfx.LevelUp:
                b = New(0.85f);
                Arpeggio(b, new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.08f, 5f);
                break;
            case Sfx.Dash:
                b = New(0.19f);
                Noise(b, rng, 0f, 0.18f, 500f, 2200f, 250f, 1f, 0.03f, 18f);
                Tone(b, 0f, 0.14f, 250f, 600f, Wave.Sine, 0.25f, 0.01f, 18f);
                break;
            case Sfx.Jump:
                b = New(0.11f);
                Tone(b, 0f, 0.1f, 260f * d, 480f * d, Wave.Sine, 1f, 0.005f, 25f);
                Tone(b, 0f, 0.08f, 520f * d, 960f * d, Wave.Triangle, 0.2f, 0.005f, 35f);
                break;
            case Sfx.AirJump:
                b = New(0.16f);
                Tone(b, 0f, 0.15f, 400f, 760f, Wave.Sine, 1f, 0.005f, 18f);
                Tone(b, 0.02f, 0.12f, 1200f, 1500f, Wave.Sine, 0.15f, 0.005f, 25f);
                break;
            case Sfx.Slide:
                b = New(0.34f);
                Noise(b, rng, 0f, 0.33f, 1100f, 700f, 200f, 1f, 0.03f, 7f);
                break;
            case Sfx.SlamStart:
                b = New(0.22f);
                Tone(b, 0f, 0.21f, 800f, 320f, Wave.Sine, 0.7f, 0.01f, 7f);
                Noise(b, rng, 0f, 0.21f, 600f, 1500f, 300f, 0.4f, 0.05f, 6f);
                break;
            case Sfx.SlamImpact:
                b = New(0.65f);
                Tone(b, 0f, 0.6f, 100f, 34f, Wave.Sine, 1f, 0.004f, 6f);
                Noise(b, rng, 0f, 0.45f, 1400f, 150f, 30f, 0.7f, 0.004f, 9f);
                SoftClip(b, 1.2f);
                break;
            case Sfx.Land:
                b = New(0.15f);
                Tone(b, 0f, 0.14f, 120f * d, 55f * d, Wave.Sine, 1f, 0.004f, 20f);
                Noise(b, rng, 0f, 0.09f, 800f, 250f, 60f, 0.5f, 0.004f, 30f);
                break;
            case Sfx.RunWin:
                b = New(1.6f);
                Arpeggio(b, new[] { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f }, 0.12f, 3f);
                break;
            case Sfx.RunLose:
                b = New(1.6f);
                Arpeggio(b, new[] { 392f, 311.13f, 261.63f, 196f }, 0.22f, 2.5f, glide: 0.97f);
                break;
            default:
                b = New(0.05f);
                break;
        }

        LowPass(b, Smoothing);
        Normalize(b, Peak);
        return b;
    }

    /// <summary>
    /// An 8-second drone that loops seamlessly. Every frequency is a whole
    /// multiple of 1/8 Hz, so each partial completes an exact number of cycles
    /// in the loop and the seam has no click. No noise: noise cannot be made to
    /// loop without a crossfade, and a pad does not need it.
    /// </summary>
    public static float[] BuildAmbient()
    {
        const float seconds = 8f;
        float[] b = New(seconds);
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float swell = 0.75f + 0.25f * MathF.Sin(Tau * 0.125f * t);
            float drift = 0.7f + 0.3f * MathF.Sin(Tau * 0.25f * t + 1.3f);
            b[i] = swell * (0.5f * MathF.Sin(Tau * 55f * t)
                          + 0.3f * MathF.Sin(Tau * 82.5f * t))
                 + drift * (0.25f * MathF.Sin(Tau * 110f * t)
                          + 0.18f * MathF.Sin(Tau * 110.25f * t)
                          + 0.12f * MathF.Sin(Tau * 165f * t));
        }
        Normalize(b, 0.35f);
        return b;
    }

    /// <summary>Pack a float buffer as a 16-bit mono stream.</summary>
    public static AudioStreamWav ToStream(float[] samples, bool loop = false)
    {
        byte[] data = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short s = (short)Math.Clamp((int)MathF.Round(samples[i] * 32767f), -32768, 32767);
            data[i * 2] = (byte)(s & 0xff);
            data[i * 2 + 1] = (byte)((s >> 8) & 0xff);
        }

        var stream = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = Rate,
            Stereo = false,
            Data = data,
        };
        if (loop)
        {
            stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            stream.LoopBegin = 0;
            stream.LoopEnd = samples.Length;
        }
        return stream;
    }

    // ------------------------------------------------------------- building

    private static float[] New(float seconds) => new float[(int)(seconds * Rate)];

    /// <summary>Linear attack, exponential decay, and a fade at the end so a voice
    /// cut by its own length never clicks. Both edges are floored here rather
    /// than trusted to each recipe.</summary>
    private static float Envelope(float t, float duration, float attack, float decay)
    {
        attack = MathF.Max(attack, MinAttack);
        float env = t < attack ? t / attack : MathF.Exp(-decay * (t - attack));
        float tail = MathF.Min(1f, (duration - t) / Tail);
        return env * MathF.Max(0f, tail);
    }

    /// <summary>An oscillator gliding exponentially from f0 to f1 — exponential
    /// because pitch is heard logarithmically, so a linear glide sounds like it
    /// rushes at the start.</summary>
    private static void Tone(float[] b, float start, float duration, float f0, float f1,
                             Wave wave, float amp, float attack, float decay)
    {
        int s0 = (int)(start * Rate);
        int n = Math.Min((int)(duration * Rate), b.Length - s0);
        float ratio = f1 / f0;
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float f = f0 * MathF.Pow(ratio, t / duration);
            phase += f / Rate;
            phase -= MathF.Floor(phase);
            b[s0 + i] += amp * Envelope(t, duration, attack, decay) * Osc(wave, phase);
        }
    }

    /// <summary>White noise through a one-pole low-pass whose cutoff glides from
    /// lp0 to lp1, then an optional one-pole high-pass. Cheap, and enough to get
    /// whooshes, scrapes and thuds out of the same function.</summary>
    private static void Noise(float[] b, NoiseGen rng, float start, float duration, float lp0, float lp1,
                              float hp, float amp, float attack, float decay)
    {
        int s0 = (int)(start * Rate);
        int n = Math.Min((int)(duration * Rate), b.Length - s0);
        float ratio = lp1 / lp0;
        float hpCoef = hp > 0f ? 1f - MathF.Exp(-Tau * hp / Rate) : 0f;
        float lp = 0f, hpLow = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float cutoff = MathF.Min(lp0 * MathF.Pow(ratio, t / duration), Rate * 0.45f);
            float a = 1f - MathF.Exp(-Tau * cutoff / Rate);
            lp += a * (rng.Next() - lp);
            float x = lp;
            if (hp > 0f)
            {
                hpLow += hpCoef * (x - hpLow);
                x -= hpLow;
            }
            b[s0 + i] += amp * Envelope(t, duration, attack, decay) * x;
        }
    }

    /// <summary>Notes entering one after another and ringing together, sine with
    /// a quiet triangle an octave up for a little body. <paramref name="glide"/>
    /// below 1 makes every note sag as it rings, which is all "sad" needs here.</summary>
    private static void Arpeggio(float[] b, float[] notes, float step, float decay, float glide = 1f)
    {
        float total = b.Length / (float)Rate;
        for (int k = 0; k < notes.Length; k++)
        {
            float start = k * step;
            float duration = total - start;
            Tone(b, start, duration, notes[k], notes[k] * glide, Wave.Sine, 0.6f, 0.006f, decay);
            Tone(b, start, duration, notes[k] * 2f, notes[k] * 2f * glide, Wave.Triangle, 0.1f, 0.006f, decay * 1.5f);
        }
    }

    private static float Osc(Wave wave, float phase) => wave switch
    {
        Wave.Triangle => 1f - 4f * MathF.Abs(phase - 0.5f),
        _ => MathF.Sin(Tau * phase),
    };

    private static void LowPass(float[] b, float cutoff)
    {
        float a = 1f - MathF.Exp(-Tau * cutoff / Rate);
        float y = 0f;
        for (int i = 0; i < b.Length; i++)
        {
            y += a * (b[i] - y);
            b[i] = y;
        }
    }

    private static void SoftClip(float[] b, float drive)
    {
        for (int i = 0; i < b.Length; i++) b[i] = MathF.Tanh(b[i] * drive);
    }

    private static void Normalize(float[] b, float peak)
    {
        float max = 0f;
        for (int i = 0; i < b.Length; i++) max = MathF.Max(max, MathF.Abs(b[i]));
        if (max < 1e-6f) return;
        float g = peak / max;
        for (int i = 0; i < b.Length; i++) b[i] *= g;
    }

    /// <summary>Xorshift noise in [-1, 1). Deliberately not the run's
    /// <c>Core.Rng</c>: drawing from that would make a seeded run's gameplay
    /// depend on which sounds had been synthesised.</summary>
    internal sealed class NoiseGen
    {
        private uint _s;
        public NoiseGen(uint seed) { _s = seed == 0 ? 0x9E3779B9u : seed; }

        public float Next()
        {
            _s ^= _s << 13;
            _s ^= _s >> 17;
            _s ^= _s << 5;
            return (_s >> 8) * (2f / 16777216f) - 1f;
        }
    }
}
