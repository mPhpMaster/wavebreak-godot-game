using System;
using Godot;
using Wavebreak.Combat.Weapons;
using Wavebreak.Core;

namespace Wavebreak.Audio;

/// <summary>
/// The signature weapons' own sounds (Combat/Weapons/Signatures.cs), a partial
/// like the chest and shrine sounds and built with the same synth helpers.
/// Each weapon gets its own little voice, soft enough to repeat all run:
///
///   * Spyglass: a short glassy "ting" on every shot; a bullseye adds a bright
///     fifth above it; the max-level ricochet is a quick rising pair.
///   * Anchor: a rope whoosh on the throw, a low wooden "clonk" on landing,
///     and a soft chain rattle with a low hum when it is hauled in.
///   * Kite: a breathy flutter when the string whips through a crowd, and a
///     quiet crackle for the storm kite's lightning.
///
/// Every sound has a minimum gap (rate limit) on top of the voice pool's own
/// priority stealing, and a little pitch jitter so a repeat is not a copy.
/// </summary>
public sealed partial class AudioDirector
{
    private static AudioStreamWav[]? _heroStreams;
    private readonly double[] _heroLast = new double[8];

    private static (float Db, int Priority, float MinInterval, float Jitter) HeroSpec(SignatureSound s) => s switch
    {
        SignatureSound.SpyglassCast => (-15f, 4, 0.12f, 0.04f),
        SignatureSound.SpyglassBullseye => (-13f, 5, 0.12f, 0.03f),
        SignatureSound.SpyglassRicochet => (-14f, 4, 0.2f, 0.03f),
        SignatureSound.AnchorThrow => (-16f, 3, 0.25f, 0.05f),
        SignatureSound.AnchorLand => (-12f, 5, 0.18f, 0.06f),
        SignatureSound.AnchorHaul => (-13f, 5, 0.25f, 0.04f),
        SignatureSound.KiteWhip => (-18f, 3, 0.45f, 0.08f),
        _ => (-16f, 4, 0.5f, 0.06f),   // KiteZap
    };

    private void SubscribeHeroSounds()
    {
        if (_heroStreams is null)
        {
            _heroStreams = new AudioStreamWav[8];
            for (int i = 0; i < _heroStreams.Length; i++)
                _heroStreams[i] = SfxSynth.ToStream(BuildHero((SignatureSound)i));
        }
        EventBus.Subscribe<SignatureCue>(OnSignatureCue);
    }

    private void UnsubscribeHeroSounds() => EventBus.Unsubscribe<SignatureCue>(OnSignatureCue);

    private void OnSignatureCue(SignatureCue e)
    {
        if (_heroStreams is null) return;
        int k = (int)e.Sound;
        if (k < 0 || k >= _heroStreams.Length) return;
        var (db, priority, minInterval, jitter) = HeroSpec(e.Sound);
        double now = Now();
        if (now - _heroLast[k] < minInterval) { _dropped++; return; }

        Voice? voice = null;
        foreach (Voice v in _voices3D)
        {
            if (!IsPlaying(v.Player)) { voice = v; break; }
            if (v.Priority <= priority
                && (voice is null || v.Priority < voice.Priority
                    || (v.Priority == voice.Priority && v.StartedAt < voice.StartedAt)))
                voice = v;
        }
        if (voice?.Player is not AudioStreamPlayer3D p) { _dropped++; return; }
        if (IsPlaying(p)) _stolen++;

        p.Stream = _heroStreams[k];
        p.PitchScale = Math.Clamp(1f + (NextRand() * 2f - 1f) * jitter, 0.5f, 2f);
        p.VolumeDb = db;
        p.GlobalPosition = e.Position;
        p.Play();

        voice.Id = (Sfx)(-1);
        voice.Priority = priority;
        voice.StartedAt = now;
        _heroLast[k] = now;
    }

    private static float[] BuildHero(SignatureSound s)
    {
        const int rate = SfxSynth.Rate;
        float seconds = s switch
        {
            SignatureSound.SpyglassCast => 0.16f,
            SignatureSound.SpyglassBullseye => 0.32f,
            SignatureSound.SpyglassRicochet => 0.22f,
            SignatureSound.AnchorThrow => 0.24f,
            SignatureSound.AnchorLand => 0.30f,
            SignatureSound.AnchorHaul => 0.42f,
            SignatureSound.KiteWhip => 0.22f,
            _ => 0.20f,
        };
        float[] b = new float[(int)(seconds * rate)];
        var noise = new SfxSynth.NoiseGen(0x4E20u + (uint)s * 131u);

        switch (s)
        {
            case SignatureSound.SpyglassCast:
                // A small glass "ting": G6 with a quiet octave, gone fast.
                Tone(b, 0f, 0.15f, 1568f, 1540f, false, 0.6f, 0.004f, 30f);
                Tone(b, 0f, 0.10f, 3136f, 3100f, false, 0.10f, 0.004f, 45f);
                Noise(b, noise, 0f, 0.03f, 2400f, 0.08f, 90f);
                break;
            case SignatureSound.SpyglassBullseye:
                // The ting, then a bright fifth that rings a little: "got it".
                Tone(b, 0f, 0.14f, 1568f, 1550f, false, 0.55f, 0.004f, 32f);
                Tone(b, 0.045f, 0.27f, 2349.3f, 2349.3f, false, 0.45f, 0.004f, 14f);
                Tone(b, 0.045f, 0.20f, 1174.7f, 1174.7f, true, 0.10f, 0.004f, 18f);
                break;
            case SignatureSound.SpyglassRicochet:
                Tone(b, 0f, 0.09f, 1760f, 1980f, false, 0.5f, 0.004f, 30f);
                Tone(b, 0.07f, 0.15f, 2637f, 2700f, false, 0.4f, 0.004f, 22f);
                break;
            case SignatureSound.AnchorThrow:
                // Rope paying out: a soft rising whoosh, no tone to tire of.
                Noise(b, noise, 0f, 0.23f, 900f, 0.8f, 9f);
                Tone(b, 0f, 0.2f, 180f, 260f, false, 0.18f, 0.03f, 12f);
                break;
            case SignatureSound.AnchorLand:
                // A low wooden-iron "clonk": a falling body and a hollow partial.
                Tone(b, 0f, 0.28f, 150f, 82f, false, 0.9f, 0.004f, 13f);
                Tone(b, 0f, 0.16f, 330f, 300f, true, 0.30f, 0.004f, 24f);
                Tone(b, 0f, 0.10f, 990f, 960f, false, 0.08f, 0.004f, 40f);
                Noise(b, noise, 0f, 0.07f, 1300f, 0.35f, 45f);
                break;
            case SignatureSound.AnchorHaul:
                // Chain rattle (five little ticks) over a low hum: hauled in.
                for (int i = 0; i < 5; i++)
                {
                    float t0 = 0.02f + i * 0.055f;
                    Tone(b, t0, 0.05f, 1250f - i * 60f, 1180f - i * 60f, true, 0.22f, 0.004f, 60f);
                    Noise(b, noise, t0, 0.03f, 2200f, 0.12f, 90f);
                }
                Tone(b, 0f, 0.40f, 98f, 110f, false, 0.55f, 0.03f, 7f);
                break;
            case SignatureSound.KiteWhip:
                // A breathy flutter: band-limited noise beating at about 28 Hz.
                Noise(b, noise, 0f, 0.21f, 1700f, 0.7f, 10f);
                for (int i = 0; i < b.Length; i++)
                {
                    float t = i / (float)rate;
                    b[i] *= 0.55f + 0.45f * MathF.Sin(MathF.Tau * 28f * t);
                }
                Tone(b, 0f, 0.18f, 620f, 880f, false, 0.12f, 0.01f, 14f);
                break;
            default:
                // Storm kite: a quiet crackle and a falling triangle.
                Noise(b, noise, 0f, 0.12f, 2500f, 0.5f, 28f);
                Tone(b, 0f, 0.18f, 880f, 440f, true, 0.35f, 0.004f, 16f);
                break;
        }

        float a = 1f - MathF.Exp(-MathF.Tau * 4200f / rate);
        float y = 0f, peak = 0f;
        for (int i = 0; i < b.Length; i++) { y += a * (b[i] - y); b[i] = y; peak = MathF.Max(peak, MathF.Abs(y)); }
        if (peak > 1e-6f) for (int i = 0; i < b.Length; i++) b[i] *= SfxSynth.Peak / peak;
        return b;
    }
}
